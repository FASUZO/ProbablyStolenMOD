using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MachinePowerLayout
{
    public struct Metrics
    {
        public int Uncovered;    // machines with demand > 0 whose nightly supply < demand
        public int Deficit;      // total missing power per night
        public int MaxDeficit;   // worst single machine, for tie-breaking
        public int Wasted;       // supply exceeding demand on machines (report only)
        public int Contacts;     // total machine<->array adjacencies (the "score")
        public int BatteryGain;  // charge that lands in loose batteries
        public int Covered;      // power-drawing machines fully covered
        public int Machines;     // power-drawing machine count
        public int Powerless;    // machines with no nightly draw (excluded from coverage)
        public int Moves;        // items that would move
    }

    public class Placement
    {
        public AreaItem Item;
        public Orientation Ori;
        public int X, Y;
    }

    public class Plan
    {
        public List<Placement> Moves = new List<Placement>();
        public Metrics Before;
        public Metrics After;
        public double Score;
        public bool Feasible = true;
        public string Note = "";
    }

    /// <summary>
    /// Layout solver for the machine area.
    ///
    /// Objective is lexicographic, in one numeric scale:
    ///   1e6 per unpowered machine  ->  fewest machines left short
    ///   1e3 per missing power unit ->  smallest nightly deficit
    ///   small terms               ->  tight placement, charge for loose
    ///                                batteries, fewest moved items
    ///
    /// Search: randomized greedy construction + best-improvement local search
    /// (single-item relocation plus same-size machine swaps) wrapped in an
    /// iterated-local-search loop: kick, re-optimise, keep only improvements.
    /// Everything is bounded by a wall-clock budget.
    /// </summary>
    public class Planner
    {
        private readonly GridModel m;
        private readonly bool fullMode;
        private readonly int L;
        private readonly Random rng = new Random(20261108);

        private readonly int[] owner;      // cell -> item index or -1
        private readonly int[] curX, curY, curOri;
        private readonly int[] origX, origY, origOri;
        private readonly bool[] movable;
        private readonly List<int> movList = new List<int>();
        private readonly List<int> targets = new List<int>();
        private readonly int[] seen;
        private int seenGen;

        // best-so-far snapshot
        private readonly int[] bestX, bestY, bestO;
        private bool haveBest;

        public Planner(GridModel model, bool full)
        {
            m = model;
            fullMode = full;
            L = model.ChargePerArray;
            int n = model.Items.Count;
            owner = new int[model.W * model.H];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;

            curX = new int[n]; curY = new int[n]; curOri = new int[n];
            origX = new int[n]; origY = new int[n]; origOri = new int[n];
            bestX = new int[n]; bestY = new int[n]; bestO = new int[n];
            movable = new bool[n];
            seen = new int[n];

            for (int i = 0; i < n; i++)
            {
                AreaItem it = model.Items[i];
                curX[i] = it.X; curY[i] = it.Y; curOri[i] = 0;
                origX[i] = it.X; origY[i] = it.Y; origOri[i] = 0;
                movable[i] = it.IsSource || (full && it.IsMachine);
                if (movable[i]) movList.Add(i);
                if (it.Target) targets.Add(i);
            }
            ResetAll();
        }

        public bool HasWork { get { return movList.Count > 0; } }
        public int ChargePerArray { get { return L; } }

        /// <summary>ASCII dump of what the solver thinks the grid looks like; logged on failure.</summary>
        public string Diagnostics()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("grid ").Append(m.W).Append('x').Append(m.H)
              .Append(" origin=(").Append(m.Ox).Append(',').Append(m.Oy).Append(')')
              .Append(" panel=").Append(m.ShapeW).Append('x').Append(m.ShapeH)
              .Append(" items_from=(").Append(m.ItemX0).Append(',').Append(m.ItemY0).Append(')')
              .Append(" items=").Append(m.Items.Count)
              .Append(" movable=").Append(movList.Count).Append('\n');
            try
            {
                GridShape gs = m.Inv != null ? m.Inv.inventoryShape : null;
                if (gs != null)
                {
                    int filled = 0;
                    for (int y = gs.minY; y <= gs.maxY; y++)
                        for (int x = gs.minX; x <= gs.maxX; x++)
                            if (gs.Get(x, y) != 0) filled++;
                    sb.Append("shape w/h=").Append(gs.width).Append('/').Append(gs.height)
                      .Append(" global=").Append(gs.globalWidth).Append('/').Append(gs.globalHeight)
                      .Append(" min=(").Append(gs.minX).Append(',').Append(gs.minY).Append(')')
                      .Append(" max=(").Append(gs.maxX).Append(',').Append(gs.maxY).Append(')')
                      .Append(" filled=").Append(filled).Append('\n');
                }
            }
            catch (Exception) { }
            int free = 0;
            for (int i = 0; i < m.Blocked.Length; i++) if (!m.Blocked[i]) free++;
            sb.Append("cells=").Append(m.Blocked.Length).Append(" usable=").Append(free).Append('\n');
            for (int i = 0; i < m.Items.Count; i++)
            {
                AreaItem it = m.Items[i];
                sb.Append("  ").Append(it.IsSource ? 'A' : it.IsMachine ? 'M' : it.IsBattery ? 'B' : '.')
                  .Append(' ').Append(it.Key == null ? "?" : it.Key)
                  .Append(" at(").Append(it.X).Append(',').Append(it.Y).Append(")")
                  .Append(' ').Append(it.W).Append('x').Append(it.H)
                  .Append(" ori=").Append(it.Ori)
                  .Append(" formas=").Append(it.Orientations.Count)
                  .Append(" demand=").Append(it.Demand)
                  .Append('\n');
            }
            return sb.ToString();
        }

        private Orientation O(int idx) { return m.Items[idx].Orientations[curOri[idx]]; }

        // ------------------------------------------------------------------
        // occupancy
        // ------------------------------------------------------------------

        private void Stamp(int idx)
        {
            Orientation o = O(idx);
            int x0 = curX[idx], y0 = curY[idx];
            for (int y = 0; y < o.H; y++)
                for (int x = 0; x < o.W; x++)
                    if (o.Mask[y * o.W + x]) owner[m.Cell(x0 + x, y0 + y)] = idx;
        }

        private void Unstamp(int idx)
        {
            Orientation o = O(idx);
            int x0 = curX[idx], y0 = curY[idx];
            for (int y = 0; y < o.H; y++)
                for (int x = 0; x < o.W; x++)
                    if (o.Mask[y * o.W + x])
                    {
                        int c = m.Cell(x0 + x, y0 + y);
                        if (owner[c] == idx) owner[c] = -1;
                    }
        }

        private bool Fit(int idx, int oriIndex, int x, int y)
        {
            Orientation o = m.Items[idx].Orientations[oriIndex];
            if (x < 0 || y < 0 || x + o.W > m.W || y + o.H > m.H) return false;
            for (int dy = 0; dy < o.H; dy++)
                for (int dx = 0; dx < o.W; dx++)
                {
                    if (!o.Mask[dy * o.W + dx]) continue;
                    int c = m.Cell(x + dx, y + dy);
                    if (m.Blocked[c] || owner[c] != -1) return false;
                }
            return true;
        }

        private void Put(int idx, int oriIndex, int x, int y)
        {
            curOri[idx] = oriIndex;
            curX[idx] = x;
            curY[idx] = y;
            Stamp(idx);
        }

        private void ResetAll()
        {
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            for (int k = 0; k < movList.Count; k++)
            {
                int idx = movList[k];
                curX[idx] = origX[idx]; curY[idx] = origY[idx]; curOri[idx] = origOri[idx];
            }
            for (int i = 0; i < m.Items.Count; i++)
                if (!movable[i])
                {
                    curX[i] = origX[i]; curY[i] = origY[i]; curOri[i] = origOri[i];
                    Stamp(i);
                }
        }

        /// <summary>
        /// Occupancy = exactly what is on the grid right now, nothing moved. This is the
        /// fallback when the shop is too packed to rebuild from scratch (the freed cells
        /// become 1-wide slivers that a 2x2 array cannot occupy): optimise in place.
        /// </summary>
        private bool SeedCurrent()
        {
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            for (int i = 0; i < m.Items.Count; i++)
            {
                curX[i] = origX[i]; curY[i] = origY[i]; curOri[i] = origOri[i];
            }
            for (int i = 0; i < m.Items.Count; i++)
            {
                Orientation o = O(i);
                for (int y = 0; y < o.H; y++)
                {
                    for (int x = 0; x < o.W; x++)
                    {
                        if (!o.Mask[y * o.W + x]) continue;
                        int cx = origX[i] + x, cy = origY[i] + y;
                        if (cx < 0 || cy < 0 || cx >= m.W || cy >= m.H) { ClearOwner(); return false; }
                        int c = m.Cell(cx, cy);
                        if (owner[c] != -1) { ClearOwner(); return false; }
                        owner[c] = i;
                    }
                }
            }
            return true;
        }

        private void ClearOwner()
        {
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
        }

        private void SaveBest()        {
            for (int k = 0; k < movList.Count; k++)
            {
                int idx = movList[k];
                bestX[idx] = curX[idx]; bestY[idx] = curY[idx]; bestO[idx] = curOri[idx];
            }
            haveBest = true;
        }

        private void LoadBest()
        {
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            for (int k = 0; k < movList.Count; k++)
            {
                int idx = movList[k];
                Put(idx, bestO[idx], bestX[idx], bestY[idx]);
            }
            for (int i = 0; i < m.Items.Count; i++)
                if (!movable[i]) Stamp(i);
        }

        // ------------------------------------------------------------------
        // scoring
        // ------------------------------------------------------------------

        private int AdjacentSources(int targetIdx)
        {
            seenGen++;
            int count = 0;
            Orientation o = O(targetIdx);
            int x0 = curX[targetIdx], y0 = curY[targetIdx];
            for (int dy = 0; dy < o.H; dy++)
                for (int dx = 0; dx < o.W; dx++)
                {
                    if (!o.Mask[dy * o.W + dx]) continue;
                    int cx = x0 + dx, cy = y0 + dy;
                    count += CountAt(cx + 1, cy);
                    count += CountAt(cx - 1, cy);
                    count += CountAt(cx, cy + 1);
                    count += CountAt(cx, cy - 1);
                }
            return count;
        }

        private int CountAt(int x, int y)
        {
            if (!m.InBounds(x, y)) return 0;
            int idx = owner[m.Cell(x, y)];
            if (idx < 0) return 0;
            if (!m.Items[idx].IsSource) return 0;
            if (seen[idx] == seenGen) return 0;
            seen[idx] = seenGen;
            return 1;
        }

        public Metrics Evaluate()
        {
            var r = new Metrics();
            for (int t = 0; t < targets.Count; t++)
            {
                int idx = targets[t];
                AreaItem it = m.Items[idx];
                int adj = AdjacentSources(idx);
                int supply = adj * L;
                if (it.IsMachine)
                {
                    if (it.Demand <= 0)
                    {
                        // no nightly draw (a device that is not powered by a battery);
                        // it must not inflate the "powered" ratio, and an array next to it
                        // scores nothing - otherwise the solver happily ships arrays into an
                        // empty panel to "touch" a machine that never needs power.
                        r.Powerless++;
                        continue;
                    }
                    r.Contacts += adj;
                    r.Machines++;
                    int need = it.Demand;
                    if (supply >= need) r.Covered++;
                    else
                    {
                        r.Uncovered++;
                        int d = need - supply;
                        r.Deficit += d;
                        if (d > r.MaxDeficit) r.MaxDeficit = d;
                    }
                    if (supply > need) r.Wasted += supply - need;
                }
                else
                {
                    int room = it.MaxEnergy - it.Energy;
                    if (room > 0) r.BatteryGain += supply < room ? supply : room;
                }
            }
            for (int k = 0; k < movList.Count; k++)
            {
                int idx = movList[k];
                if (curX[idx] != origX[idx] || curY[idx] != origY[idx] || curOri[idx] != origOri[idx])
                    r.Moves++;
            }
            return r;
        }

        /// <summary>
        /// The three priorities, in one scale:
        ///   1e6 per unpowered machine   -> fewest machines left short
        ///   1e3 per missing power unit  -> smallest nightly deficit
        ///   1e2 per worst-machine unit  -> spread the remaining shortfall
        ///   1.0 per adjacency           -> maximise the total score (contacts)
        ///   small terms                 -> battery charging, fewest moves
        /// Note that surplus is deliberately NOT penalised: more contacts is the score.
        /// </summary>
        public static double Value(Metrics r)
        {
            return -(1e6 * r.Uncovered) - (1e3 * r.Deficit) - (1e2 * r.MaxDeficit)
                   + 1.0 * r.Contacts + 0.8 * r.BatteryGain - 0.02 * r.Moves;
        }

        // ------------------------------------------------------------------
        // construction
        // ------------------------------------------------------------------

        private List<int> BuildOrder(bool randomized)
        {
            var order = new List<int>(movList);
            if (fullMode)
            {
                order.Sort((a, b) =>
                {
                    AreaItem x = m.Items[a], y = m.Items[b];
                    if (x.IsSource != y.IsSource) return x.IsSource ? 1 : -1;
                    if (x.Demand != y.Demand) return y.Demand.CompareTo(x.Demand);
                    return y.Area.CompareTo(x.Area);
                });
            }
            else
            {
                order.Sort((a, b) => m.Items[b].Area.CompareTo(m.Items[a].Area));
            }

            if (randomized && order.Count > 1)
            {
                // Shuffle inside a window so the size ordering mostly survives.
                for (int i = 0; i + 1 < order.Count; i++)
                {
                    if (rng.NextDouble() < 0.45)
                    {
                        int j = Math.Min(order.Count - 1, i + rng.Next(0, 4));
                        int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
                    }
                }
            }
            return order;
        }

        private bool Construct(bool randomized)
        {
            ResetAll();
            List<int> order = BuildOrder(randomized);
            for (int i = 0; i < order.Count; i++)
                if (!TryPlaceBest(order[i], randomized)) return false;
            return true;
        }

        /// <summary>
        /// Full-relayout seed: shelf-pack the machines with two-cell gutters, then
        /// drop the arrays into the gutters. Machines packed with 2-wide corridors
        /// are the only geometry where a 2x2 array can touch 3-4 machines at once,
        /// which is where the nightly charge actually comes from.
        /// </summary>
        private bool ConstructShelf()
        {
            ResetAll();
            List<int> order = BuildOrder(false);

            var machines = new List<int>();
            var arrays = new List<int>();
            for (int i = 0; i < order.Count; i++)
            {
                if (m.Items[order[i]].IsMachine) machines.Add(order[i]);
                else arrays.Add(order[i]);
            }

            int x = 0, y = 0, rowH = 0;
            for (int i = 0; i < machines.Count; i++)
            {
                int idx = machines[i];
                AreaItem it = m.Items[idx];
                Orientation o = it.Orientations[0];
                if (x + o.W > m.W)
                {
                    x = 0;
                    y += rowH + 2;
                    rowH = 0;
                }
                bool placed = false;
                if (y + o.H <= m.H && Fit(idx, 0, x, y))
                {
                    Put(idx, 0, x, y);
                    x += o.W + 2;
                    if (o.H > rowH) rowH = o.H;
                    placed = true;
                }
                if (!placed && !TryPlaceBest(idx, false)) return false;
            }

            for (int i = 0; i < arrays.Count; i++)
                if (!TryPlaceBest(arrays[i], false)) return false;
            return true;
        }

        private bool TryPlaceBest(int idx, bool randomized)
        {
            AreaItem it = m.Items[idx];
            double best = double.NegativeInfinity;
            int bestOri = -1, bestX = -1, bestY = -1;

            var xs = new List<int>();
            var ys = new List<int>();
            var os = new List<int>();
            var vs = new List<double>();

            for (int oi = 0; oi < it.Orientations.Count; oi++)
            {
                Orientation o = it.Orientations[oi];
                for (int y = 0; y + o.H <= m.H; y++)
                {
                    for (int x = 0; x + o.W <= m.W; x++)
                    {
                        if (!Fit(idx, oi, x, y)) continue;
                        Put(idx, oi, x, y);
                        double s = Value(Evaluate());
                        Unstamp(idx);
                        if (s > best) { best = s; bestOri = oi; bestX = x; bestY = y; }
                        if (randomized) { xs.Add(x); ys.Add(y); os.Add(oi); vs.Add(s); }
                    }
                }
            }

            if (bestOri < 0) return false;

            if (randomized && xs.Count > 1)
            {
                // Softmax over candidates: temperature 25 = a quarter of one power unit,
                // so near-ties get explored while clearly better spots still win.
                double top = double.NegativeInfinity;
                for (int i = 0; i < vs.Count; i++) if (vs[i] > top) top = vs[i];
                var weights = new double[vs.Count];
                double sum = 0;
                const double tau = 25.0;
                for (int i = 0; i < vs.Count; i++)
                {
                    weights[i] = Math.Exp(Math.Max(-30.0, (vs[i] - top) / tau));
                    sum += weights[i];
                }
                double roll = rng.NextDouble() * sum;
                int pick = vs.Count - 1;
                for (int i = 0; i < weights.Length; i++)
                {
                    roll -= weights[i];
                    if (roll <= 0) { pick = i; break; }
                }
                bestOri = os[pick]; bestX = xs[pick]; bestY = ys[pick];
            }

            Put(idx, bestOri, bestX, bestY);
            return true;
        }

        // ------------------------------------------------------------------
        // local search
        // ------------------------------------------------------------------

        /// <summary>Best-improvement relocation of every movable item, then machine swaps.</summary>
        private double LocalSearch(Stopwatch sw, int budgetMs, int maxPasses)
        {
            double cur = Value(Evaluate());
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool improved = false;
                for (int k = 0; k < movList.Count; k++)
                {
                    if (sw.ElapsedMilliseconds > budgetMs) return cur;
                    int idx = movList[k];
                    AreaItem it = m.Items[idx];
                    int curO = curOri[idx], curXX = curX[idx], curYY = curY[idx];
                    Unstamp(idx);

                    double best = cur;
                    int bestOri = -1, bestX = curXX, bestY = curYY;
                    for (int oi = 0; oi < it.Orientations.Count; oi++)
                    {
                        Orientation o = it.Orientations[oi];
                        for (int y = 0; y + o.H <= m.H; y++)
                        {
                            for (int x = 0; x + o.W <= m.W; x++)
                            {
                                if (x == curXX && y == curYY && oi == curO) continue;
                                if (!Fit(idx, oi, x, y)) continue;
                                Put(idx, oi, x, y);
                                double s = Value(Evaluate());
                                Unstamp(idx);
                                if (s > best + 1e-9) { best = s; bestOri = oi; bestX = x; bestY = y; }
                            }
                        }
                    }

                    if (bestOri >= 0) { Put(idx, bestOri, bestX, bestY); cur = best; improved = true; }
                    else Put(idx, curO, curXX, curYY);
                }

                double afterSwap = SwapMovables(sw, budgetMs, cur);
                if (afterSwap > cur + 1e-9) { cur = afterSwap; improved = true; }

                if (fullMode)
                {
                    double after = MachineSwaps(sw, budgetMs, cur);
                    if (after > cur + 1e-9) { cur = after; improved = true; }
                }
                if (!improved) break;
            }
            return cur;
        }

        /// <summary>2-opt over same-footprint machines: which machine sits where matters.</summary>
        private double MachineSwaps(Stopwatch sw, int budgetMs, double cur)
        {
            var machines = new List<int>();
            for (int k = 0; k < movList.Count; k++)
            {
                int idx = movList[k];
                if (m.Items[idx].IsMachine) machines.Add(idx);
            }
            if (machines.Count < 2) return cur;

            bool improved = true;
            int guard = 0;
            while (improved && guard++ < 6)
            {
                improved = false;
                for (int i = 0; i < machines.Count; i++)
                {
                    if (sw.ElapsedMilliseconds > budgetMs) return cur;
                    for (int j = i + 1; j < machines.Count; j++)
                    {
                        int a = machines[i], b = machines[j];
                        Orientation oa = O(a), ob = O(b);
                        if (oa.W != ob.W || oa.H != ob.H) continue;
                        if (m.Items[a].Demand == m.Items[b].Demand) continue;

                        int ax = curX[a], ay = curY[a], bx = curX[b], by = curY[b];
                        int ao = curOri[a], bo = curOri[b];
                        Unstamp(a); Unstamp(b);

                        bool ok = Fit(a, ao, bx, by) && Fit(b, bo, ax, ay);
                        if (ok)
                        {
                            Put(a, ao, bx, by);
                            Put(b, bo, ax, ay);
                            double s = Value(Evaluate());
                            if (s > cur + 1e-9) { cur = s; improved = true; continue; }
                            Unstamp(a); Unstamp(b);
                        }
                        Put(a, ao, ax, ay);
                        Put(b, bo, bx, by);
                    }
                }
            }
            return cur;
        }

        /// <summary>
        /// Pairwise swap of two movable items with the same footprint. In a packed grid
        /// this is frequently the only legal move: it needs no free cell at all, and it
        /// is what redistributes adjacency from over-supplied machines to a starved one.
        /// </summary>
        private double SwapMovables(Stopwatch sw, int budgetMs, double cur)
        {
            var groups = new Dictionary<int, List<int>>();
            for (int k = 0; k < movList.Count; k++)
            {
                int idx = movList[k];
                Orientation o = O(idx);
                int key = o.W * 100 + o.H;
                List<int> g;
                if (!groups.TryGetValue(key, out g))
                {
                    g = new List<int>();
                    groups[key] = g;
                }
                g.Add(idx);
            }

            bool improved = true;
            int guard = 0;
            while (improved && guard++ < 6)
            {
                improved = false;
                foreach (KeyValuePair<int, List<int>> kv in groups)
                {
                    List<int> g = kv.Value;
                    if (g.Count < 2) continue;
                    for (int i = 0; i < g.Count; i++)
                    {
                        if (sw.ElapsedMilliseconds > budgetMs) return cur;
                        for (int j = i + 1; j < g.Count; j++)
                        {
                            int a = g[i], b = g[j];
                            int ax = curX[a], ay = curY[a], bx = curX[b], by = curY[b];
                            if (ax == bx && ay == by) continue;
                            int ao = curOri[a], bo = curOri[b];

                            Unstamp(a);
                            Unstamp(b);
                            if (!Fit(a, ao, bx, by) || !Fit(b, bo, ax, ay))
                            {
                                Put(a, ao, ax, ay);
                                Put(b, bo, bx, by);
                                continue;
                            }
                            Put(a, ao, bx, by);
                            Put(b, bo, ax, ay);
                            double s = Value(Evaluate());
                            if (s > cur + 1e-9)
                            {
                                cur = s;
                                improved = true;
                                continue;
                            }
                            Unstamp(a);
                            Unstamp(b);
                            Put(a, ao, ax, ay);
                            Put(b, bo, bx, by);
                        }
                    }
                }
            }
            return cur;
        }

        /// <summary>Random relocation of a few movable items; ignores the objective on purpose.</summary>
        private void Kick(int strength)
        {
            for (int i = 0; i < strength; i++)
            {
                if (movList.Count == 0) return;
                if (rng.NextDouble() < 0.45 && TryRandomSwap()) continue;

                int idx = movList[rng.Next(movList.Count)];
                AreaItem it = m.Items[idx];
                int oldO = curOri[idx], oldX = curX[idx], oldY = curY[idx];
                Unstamp(idx);

                int oi = rng.Next(it.Orientations.Count);
                Orientation o = it.Orientations[oi];
                int x, y;
                NextMove(idx, o, out x, out y);
                if (Fit(idx, oi, x, y)) Put(idx, oi, x, y);
                else Put(idx, oldO, oldX, oldY);
            }
        }

        /// <summary>Random swap of two same-footprint items; the kick that works in a full grid.</summary>
        private bool TryRandomSwap()
        {
            int a = movList[rng.Next(movList.Count)];
            Orientation oa = O(a);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int b = movList[rng.Next(movList.Count)];
                if (b == a) continue;
                Orientation ob = O(b);
                if (oa.W != ob.W || oa.H != ob.H) continue;

                int ax = curX[a], ay = curY[a], bx = curX[b], by = curY[b];
                int ao = curOri[a], bo = curOri[b];
                Unstamp(a);
                Unstamp(b);
                if (Fit(a, ao, bx, by) && Fit(b, bo, ax, ay))
                {
                    Put(a, ao, bx, by);
                    Put(b, bo, ax, ay);
                    return true;
                }
                Put(a, ao, ax, ay);
                Put(b, bo, bx, by);
            }
            return false;
        }

        /// <summary>Proposes a destination: mostly hugging a random target, sometimes uniform.</summary>
        private void NextMove(int idx, Orientation o, out int x, out int y)
        {
            if (targets.Count > 0 && rng.NextDouble() < 0.7)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    int t = targets[rng.Next(targets.Count)];
                    if (t == idx) continue;
                    AreaItem tgt = m.Items[t];
                    int gx = tgt.X + rng.Next(0, Math.Max(1, tgt.W));
                    int gy = tgt.Y + rng.Next(0, Math.Max(1, tgt.H));
                    int side = rng.Next(0, 4);
                    if (side == 0) gx++;
                    else if (side == 1) gx--;
                    else if (side == 2) gy++;
                    else gy--;

                    x = gx - rng.Next(0, Math.Max(1, o.W));
                    y = gy - rng.Next(0, Math.Max(1, o.H));
                    if (x >= 0 && y >= 0 && x + o.W <= m.W && y + o.H <= m.H) return;
                }
            }
            x = rng.Next(0, Math.Max(1, m.W - o.W + 1));
            y = rng.Next(0, Math.Max(1, m.H - o.H + 1));
        }

        // ------------------------------------------------------------------
        // entry
        // ------------------------------------------------------------------

        public Plan Solve(int budgetMs)
        {
            ResetAll();
            Metrics before = Evaluate();
            var plan = new Plan { Before = before };

            if (movList.Count == 0)
            {
                plan.After = before;
                plan.Score = Value(before);
                plan.Note = "nothing movable";
                return plan;
            }

            var sw = Stopwatch.StartNew();
            double best = double.NegativeInfinity;
            int restarts = 0, kicks = 0;
            string seedNote = "";
            bool rebuilt = false;

            // Baseline: the layout as it stands, evaluated with zero churn. Anything the
            // search produces has to beat this, so "already optimal" can never turn into a
            // pointless shuffle of equivalent positions.
            if (SeedCurrent())
            {
                best = Value(Evaluate());
                SaveBest();
            }

            // Seeding. Rebuilding from scratch needs enough contiguous free space; in a
            // packed shop the freed cells are 1-wide slivers and 2x2 arrays cannot go
            // back in. Retry with shuffled orders, then fall back to the live layout.
            for (int attempt = 0; attempt < 8 && !rebuilt; attempt++)
            {
                rebuilt = (fullMode && (attempt % 2) == 1) ? ConstructShelf() : Construct(attempt > 0);
            }
            if (!rebuilt)
            {
                if (!SeedCurrent())
                {
                    ResetAll();
                    plan.Feasible = false;
                    plan.Note = "current layout is inconsistent (overlapping items)";
                    plan.After = before;
                    plan.Score = Value(before);
                    return plan;
                }
                seedNote = " seed=current-layout rebuilt=False";
            }

            while (true)
            {
                if (rebuilt && restarts > 0)
                {
                    bool ok = (fullMode && (restarts % 2) == 1) ? ConstructShelf() : Construct(true);
                    if (!ok && !SeedCurrent()) break;
                }
                else if (!rebuilt && restarts > 0)
                {
                    // Single basin: the kicks below are the diversification.
                    if (!SeedCurrent()) break;
                }

                double cur = LocalSearch(sw, budgetMs, 6);
                if (cur > best) { best = cur; SaveBest(); }

                int maxKicks = rebuilt ? 25 : 150;
                int localKicks = 0;
                while (localKicks < maxKicks && sw.ElapsedMilliseconds <= budgetMs)
                {
                    int[] sx = new int[movList.Count];
                    int[] sy = new int[movList.Count];
                    int[] so = new int[movList.Count];
                    for (int k = 0; k < movList.Count; k++)
                    {
                        int idx = movList[k];
                        sx[k] = curX[idx]; sy[k] = curY[idx]; so[k] = curOri[idx];
                    }

                    Kick(2 + rng.Next(3));
                    double v = LocalSearch(sw, budgetMs, 4);
                    if (v > cur + 1e-9)
                    {
                        cur = v;
                        if (v > best) { best = v; SaveBest(); }
                    }
                    else
                    {
                        for (int k = 0; k < movList.Count; k++) Unstamp(movList[k]);
                        for (int k = 0; k < movList.Count; k++)
                        {
                            int idx = movList[k];
                            Put(idx, so[k], sx[k], sy[k]);
                        }
                    }
                    localKicks++;
                    kicks++;
                }

                restarts++;
                if (sw.ElapsedMilliseconds > budgetMs) break;
                if (!rebuilt) break;                    // one long basin is enough here
                if (restarts > 200) break;
            }

            if (!haveBest)
            {
                ResetAll();
                plan.Feasible = false;
                plan.Note = "no feasible arrangement";
                plan.After = before;
                plan.Score = Value(before);
                return plan;
            }

            LoadBest();
            plan.After = Evaluate();
            plan.Score = Value(plan.After);
            for (int k = 0; k < movList.Count; k++)
            {
                int idx = movList[k];
                if (curX[idx] == origX[idx] && curY[idx] == origY[idx] && curOri[idx] == origOri[idx]) continue;
                plan.Moves.Add(new Placement
                {
                    Item = m.Items[idx],
                    Ori = m.Items[idx].Orientations[curOri[idx]],
                    X = curX[idx],
                    Y = curY[idx]
                });
            }
            plan.Note = "restarts=" + restarts + " kicks=" + kicks + " ms=" + sw.ElapsedMilliseconds + seedNote;
            return plan;
        }
    }
}
