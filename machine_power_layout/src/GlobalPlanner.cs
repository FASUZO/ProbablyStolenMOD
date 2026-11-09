using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MachinePowerLayout
{
    /// <summary>One item that has to move, possibly to another panel.</summary>
    public class Remap
    {
        public AreaItem Item;
        public GridModel From, To;
        public int X, Y;               // target cell on To
        public int OldX, OldY;         // where it was on From
        public Orientation Ori;
        public bool Crosses { get { return From != To; } }
    }

    public class GlobalPlan
    {
        public List<Remap> Moves = new List<Remap>();
        public int NegBefore, NegAfter;
        public double PenBefore, PenAfter;
        public double ScoreBefore, ScoreAfter;
        public int ContactsBefore, ContactsAfter;
        public int MovesAcross;
        public string Note = "";
    }

    /// <summary>
    /// Joint solver over every open machine area, following the reference algorithm
    /// (layout_config / 布局优化算法2.py):
    ///
    ///   item final score = -draw + contact_weight * (number of distinct adjacent arrays)
    ///   obj = neg_count * 1e7 + neg_penalty_sum * 1e4 - total_score       (lower is better)
    ///
    /// The pool is shared: a machine and an array may end up on different panels than they
    /// started on, because adjacency never crosses inventories and only a physical move can
    /// balance supply against demand.
    /// </summary>
    public class GlobalPlanner
    {
        private readonly List<GridModel> boards = new List<GridModel>();
        private readonly List<string> boardLabels = new List<string>();
        private readonly List<int[]> owner = new List<int[]>();      // per board: -1 free, -2 fixed, else pool index

        private readonly List<AreaItem> pool = new List<AreaItem>();
        private readonly List<int> curB = new List<int>();
        private readonly List<int> curX = new List<int>();
        private readonly List<int> curY = new List<int>();
        private readonly List<int> curO = new List<int>();

        private readonly List<int> orgB = new List<int>();
        private readonly List<int> orgX = new List<int>();
        private readonly List<int> orgY = new List<int>();
        private readonly List<int> orgO = new List<int>();

        private int[] bestB, bestX, bestY, bestO;
        private double bestObj = double.MaxValue;
        private int bestMoves = int.MaxValue;

        private readonly int charge;
        private readonly int totalDemand;
        private readonly bool arraysOnly;
        private readonly Random rng;

        private readonly int[] mark;
        private int markGen;

        public GlobalPlanner(List<GridModel> models, List<string> labels, int chargePerArray,
                             bool arraysOnly, int seed)
        {
            boards.AddRange(models);
            if (labels != null) boardLabels.AddRange(labels);
            while (boardLabels.Count < boards.Count) boardLabels.Add("P" + (boardLabels.Count + 1));

            charge = chargePerArray > 0 ? chargePerArray : 1;
            this.arraysOnly = arraysOnly;
            rng = new Random(seed);

            for (int bi = 0; bi < boards.Count; bi++)
            {
                GridModel m = boards[bi];
                var own = new int[m.W * m.H];
                for (int i = 0; i < own.Length; i++) own[i] = -1;
                for (int i = 0; i < m.Blocked.Length; i++) if (m.Blocked[i]) own[i] = -2;
                owner.Add(own);
            }

            // Build the shared pool: movable arrays plus machines that actually draw power.
            // Everything else (batteries, storage containers, idle devices) stays nailed down.
            int demand = 0;
            for (int bi = 0; bi < boards.Count; bi++)
            {
                GridModel m = boards[bi];
                for (int ii = 0; ii < m.Items.Count; ii++)
                {
                    AreaItem it = m.Items[ii];
                    bool movable = it.IsSource || (it.IsMachine && it.Demand > 0 && !arraysOnly);
                    if (!movable || it.Orientations.Count == 0)
                    {
                        MarkCells(bi, it, -2);
                        continue;
                    }
                    int pi = pool.Count;
                    pool.Add(it);
                    curB.Add(bi); curX.Add(it.X); curY.Add(it.Y); curO.Add(OriIndex(it, it.Ori));
                    orgB.Add(bi); orgX.Add(it.X); orgY.Add(it.Y); orgO.Add(OriIndex(it, it.Ori));
                    MarkCells(bi, it, pi);
                    if (it.IsMachine) demand += it.Demand;
                }
            }
            totalDemand = demand;
            mark = new int[pool.Count];
        }

        public int PoolSize { get { return pool.Count; } }
        public int MachineCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < pool.Count; i++) if (pool[i].IsMachine) n++;
                return n;
            }
        }
        public int ArrayCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < pool.Count; i++) if (pool[i].IsSource) n++;
                return n;
            }
        }

        private static int OriIndex(AreaItem it, int ori)
        {
            for (int i = 0; i < it.Orientations.Count; i++)
                if (it.Orientations[i].Ori == ori) return i;
            return 0;
        }

        private void MarkCells(int bi, AreaItem it, int value)
        {
            GridModel m = boards[bi];
            int[] own = owner[bi];
            for (int dy = 0; dy < it.H; dy++)
                for (int dx = 0; dx < it.W; dx++)
                {
                    if (it.Mask == null || !it.Mask[dy * it.W + dx]) continue;
                    int x = it.X + dx, y = it.Y + dy;
                    if (x < 0 || y < 0 || x >= m.W || y >= m.H) continue;
                    own[y * m.W + x] = value;
                }
        }

        // ------------------------------------------------------------------
        // geometry
        // ------------------------------------------------------------------

        private bool CanPlace(int pi, int bi, int x, int y, int oi)
        {
            GridModel m = boards[bi];
            Orientation o = pool[pi].Orientations[oi];
            if (x < 0 || y < 0 || x + o.W > m.W || y + o.H > m.H) return false;
            int[] own = owner[bi];
            for (int dy = 0; dy < o.H; dy++)
                for (int dx = 0; dx < o.W; dx++)
                {
                    if (!o.Mask[dy * o.W + dx]) continue;
                    if (own[(y + dy) * m.W + (x + dx)] != -1) return false;
                }
            return true;
        }

        private void Write(int pi, int bi, int x, int y, int oi, int value)
        {
            GridModel m = boards[bi];
            Orientation o = pool[pi].Orientations[oi];
            int[] own = owner[bi];
            for (int dy = 0; dy < o.H; dy++)
                for (int dx = 0; dx < o.W; dx++)
                {
                    if (!o.Mask[dy * o.W + dx]) continue;
                    own[(y + dy) * m.W + (x + dx)] = value;
                }
        }

        private void Take(int pi)
        {
            Write(pi, curB[pi], curX[pi], curY[pi], curO[pi], -1);
        }

        private void Put(int pi, int bi, int x, int y, int oi)
        {
            curB[pi] = bi; curX[pi] = x; curY[pi] = y; curO[pi] = oi;
            Write(pi, bi, x, y, oi, pi);
        }

        // ------------------------------------------------------------------
        // scoring (reference algorithm)
        // ------------------------------------------------------------------

        private int AdjacentArrays(int pi)
        {
            int bi = curB[pi];
            GridModel m = boards[bi];
            int[] own = owner[bi];
            AreaItem it = pool[pi];
            Orientation o = it.Orientations[curO[pi]];
            int x0 = curX[pi], y0 = curY[pi];

            markGen++;
            int n = 0;
            for (int dy = 0; dy < o.H; dy++)
                for (int dx = 0; dx < o.W; dx++)
                {
                    if (!o.Mask[dy * o.W + dx]) continue;
                    int cx = x0 + dx, cy = y0 + dy;
                    n += ProbeArray(own, m, cx + 1, cy);
                    n += ProbeArray(own, m, cx - 1, cy);
                    n += ProbeArray(own, m, cx, cy + 1);
                    n += ProbeArray(own, m, cx, cy - 1);
                }
            return n;
        }

        private int ProbeArray(int[] own, GridModel m, int x, int y)
        {
            if (x < 0 || y < 0 || x >= m.W || y >= m.H) return 0;
            int q = own[y * m.W + x];
            if (q < 0) return 0;
            if (!pool[q].IsSource) return 0;
            if (mark[q] == markGen) return 0;
            mark[q] = markGen;
            return 1;
        }

        private void Score(out int neg, out double pen, out int contacts)
        {
            neg = 0; pen = 0; contacts = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                AreaItem it = pool[i];
                if (!it.IsMachine) continue;
                int c = AdjacentArrays(i);
                contacts += c;
                int final = charge * c - it.Demand;
                if (final < 0) { neg++; pen += -final; }
            }
        }

        private double Objective(int neg, double pen, int contacts)
        {
            double total = (double)charge * contacts - totalDemand;
            return neg * 1e7 + pen * 1e4 - total;
        }

        private double SaEnergy(int neg, double pen, int contacts)
        {
            double total = (double)charge * contacts - totalDemand;
            return neg * 18.0 + pen * 2.5 - total * 0.4;
        }

        private double CurrentEnergy()
        {
            int neg, contacts; double pen;
            Score(out neg, out pen, out contacts);
            return SaEnergy(neg, pen, contacts);
        }

        private double CurrentObjective()
        {
            int neg, contacts; double pen;
            Score(out neg, out pen, out contacts);
            return Objective(neg, pen, contacts);
        }

        // ------------------------------------------------------------------
        // search
        // ------------------------------------------------------------------

        public GlobalPlan Solve(int budgetMs)
        {
            var plan = new GlobalPlan();
            if (pool.Count == 0)
            {
                plan.Note = "nothing movable";
                return plan;
            }

            int neg0, con0; double pen0;
            Score(out neg0, out pen0, out con0);
            plan.NegBefore = neg0;
            plan.PenBefore = pen0;
            plan.ContactsBefore = con0;
            plan.ScoreBefore = (double)charge * con0 - totalDemand;

            bestObj = Objective(neg0, pen0, con0);
            bestMoves = 0;
            SaveBest();

            var sw = Stopwatch.StartNew();
            int restarts = 0;
            while (sw.ElapsedMilliseconds < budgetMs)
            {
                if (restarts == 0) RestoreOrg();
                else if (!ConstructRandom()) { restarts++; continue; }

                double cur = CurrentEnergy();
                double t = 3.0;
                const double cooling = 0.9985;
                const int stepsPerRound = 6000;

                for (int step = 0; step < stepsPerRound; step++)
                {
                    if ((step & 63) == 0 && sw.ElapsedMilliseconds > budgetMs) break;

                    int pi, ob, ox, oy, oo;
                    if (!TryRandomMove(out pi, out ob, out ox, out oy, out oo)) continue;

                    double v = CurrentEnergy();
                    double delta = cur - v;
                    if (delta >= 0 || rng.NextDouble() < Math.Exp(Math.Max(-50.0, delta / Math.Max(0.10, t))))
                    {
                        cur = v;
                    }
                    else
                    {
                        RestoreOne(pi, ob, ox, oy, oo);
                    }
                    t = Math.Max(0.10, t * cooling);
                }

                LocalRefine(sw, budgetMs, 2);
                TrySaveBest();

                restarts++;
                if (restarts > 400) break;
            }

            RestoreBest();
            int neg1, con1; double pen1;
            Score(out neg1, out pen1, out con1);
            plan.NegAfter = neg1;
            plan.PenAfter = pen1;
            plan.ContactsAfter = con1;
            plan.ScoreAfter = (double)charge * con1 - totalDemand;

            // build the move list from the difference against the original layout
            for (int i = 0; i < pool.Count; i++)
            {
                if (curB[i] == orgB[i] && curX[i] == orgX[i] && curY[i] == orgY[i] && curO[i] == orgO[i])
                    continue;
                Orientation o = pool[i].Orientations[curO[i]];
                plan.Moves.Add(new Remap
                {
                    Item = pool[i],
                    From = boards[orgB[i]],
                    To = boards[curB[i]],
                    OldX = orgX[i],
                    OldY = orgY[i],
                    X = curX[i],
                    Y = curY[i],
                    Ori = o
                });
                if (curB[i] != orgB[i]) plan.MovesAcross++;
            }

            plan.Note = "restarts=" + restarts + " ms=" + sw.ElapsedMilliseconds
                        + " pool=" + pool.Count + " boards=" + boards.Count;
            return plan;
        }

        private bool TryRandomMove(out int pi, out int ob, out int ox, out int oy, out int oo)
        {
            pi = rng.Next(pool.Count);
            ob = curB[pi]; ox = curX[pi]; oy = curY[pi]; oo = curO[pi];

            AreaItem it = pool[pi];
            if (it.Orientations.Count == 0) return false;

            // 55%: relocate, 45%: swap with a same-size item (works even when the grid is full)
            if (pool.Count > 1 && rng.NextDouble() < 0.45)
            {
                int pj = rng.Next(pool.Count);
                if (pj == pi) return false;
                if (!SameFootprint(pi, pj)) return false;
                SwapItems(pi, pj);
                return true;
            }

            Take(pi);
            int bi = rng.Next(boards.Count);
            int oi = rng.Next(it.Orientations.Count);
            Orientation o = it.Orientations[oi];
            int bx = rng.Next(0, Math.Max(1, boards[bi].W - o.W + 1));
            int by = rng.Next(0, Math.Max(1, boards[bi].H - o.H + 1));

            if (!CanPlace(pi, bi, bx, by, oi))
            {
                Put(pi, ob, ox, oy, oo);
                return false;
            }
            Put(pi, bi, bx, by, oi);
            return true;
        }

        private bool SameFootprint(int a, int b)
        {
            AreaItem ia = pool[a], ib = pool[b];
            return ia.W == ib.W && ia.H == ib.H && ia.Orientations.Count == ib.Orientations.Count;
        }

        /// <summary>Swap two items between cells. Undone by restoring both with RestoreOne.</summary>
        private void SwapItems(int a, int b)
        {
            int ab = curB[a], ax = curX[a], ay = curY[a], ao = curO[a];
            int bb = curB[b], bx = curX[b], by = curY[b], bo = curO[b];
            Take(a); Take(b);
            Put(a, bb, bx, by, bo);
            Put(b, ab, ax, ay, ao);
        }

        private void RestoreOne(int pi, int bi, int x, int y, int oi)
        {
            Take(pi);
            Put(pi, bi, x, y, oi);
        }

        private void RestoreOrg()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                RestoreOne(i, orgB[i], orgX[i], orgY[i], orgO[i]);
            }
        }

        /// <summary>Randomised construction, used by the extra restarts.</summary>
        private bool ConstructRandom()
        {
            for (int i = 0; i < pool.Count; i++) Take(i);
            // machines first, biggest demand first: they are the hardest to fit
            var order = new List<int>();
            for (int i = 0; i < pool.Count; i++) order.Add(i);
            order.Sort(delegate (int a, int b)
            {
                bool ma = pool[a].IsMachine, mb = pool[b].IsMachine;
                if (ma != mb) return ma ? -1 : 1;
                if (ma && pool[a].Demand != pool[b].Demand) return pool[b].Demand.CompareTo(pool[a].Demand);
                return pool[b].Area.CompareTo(pool[a].Area);
            });

            for (int k = 0; k < order.Count; k++)
            {
                int pi = order[k];
                if (PlaceAnywhere(pi)) continue;
                RestoreOne(pi, orgB[pi], orgX[pi], orgY[pi], orgO[pi]);
                if (!CanPlace(pi, orgB[pi], orgX[pi], orgY[pi], orgO[pi]))
                {
                    // could not even get back to where it was - abandon this restart
                    return false;
                }
                Put(pi, orgB[pi], orgX[pi], orgY[pi], orgO[pi]);
            }
            return true;
        }

        private bool PlaceAnywhere(int pi)
        {
            AreaItem it = pool[pi];
            var bOrder = new List<int>();
            for (int i = 0; i < boards.Count; i++) bOrder.Add(i);
            Shuffle(bOrder);

            for (int bo = 0; bo < bOrder.Count; bo++)
            {
                int bi = bOrder[bo];
                GridModel m = boards[bi];
                for (int oi = 0; oi < it.Orientations.Count; oi++)
                {
                    Orientation o = it.Orientations[oi];
                    for (int y = 0; y + o.H <= m.H; y++)
                        for (int x = 0; x + o.W <= m.W; x++)
                            if (CanPlace(pi, bi, x, y, oi)) { Put(pi, bi, x, y, oi); return true; }
                }
            }
            return false;
        }

        private void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        /// <summary>
        /// Exhaustive single-item improvement: for every item, try every cell on every panel.
        /// This is the "局部精修" step of the reference algorithm.
        /// </summary>
        private void LocalRefine(Stopwatch sw, int budgetMs, int passes)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                bool changed = false;
                for (int pi = 0; pi < pool.Count; pi++)
                {
                    if (sw.ElapsedMilliseconds > budgetMs) return;

                    int ob = curB[pi], ox = curX[pi], oy = curY[pi], oo = curO[pi];
                    double bestLocal = CurrentObjective();
                    int nb = ob, nx = ox, ny = oy, no = oo;

                    Take(pi);
                    AreaItem it = pool[pi];
                    for (int bi = 0; bi < boards.Count; bi++)
                    {
                        GridModel m = boards[bi];
                        for (int oi = 0; oi < it.Orientations.Count; oi++)
                        {
                            Orientation o = it.Orientations[oi];
                            for (int y = 0; y + o.H <= m.H; y++)
                                for (int x = 0; x + o.W <= m.W; x++)
                                {
                                    if (!CanPlace(pi, bi, x, y, oi)) continue;
                                    Write(pi, bi, x, y, oi, pi);
                                    int neg, con; double pen;
                                    Score(out neg, out pen, out con);
                                    double v = Objective(neg, pen, con) - 0.02 * Dist(pi, bi, x, y);
                                    Write(pi, bi, x, y, oi, -1);
                                    if (v < bestLocal - 1e-9)
                                    {
                                        bestLocal = v;
                                        nb = bi; nx = x; ny = y; no = oi;
                                    }
                                }
                        }
                    }
                    Put(pi, nb, nx, ny, no);
                    if (nb != ob || nx != ox || ny != oy || no != oo) changed = true;
                }
                if (!changed) break;
            }
        }

        /// <summary>Tiny preference for staying put, so equal-scoring moves are not made.</summary>
        private int Dist(int pi, int bi, int x, int y)
        {
            if (bi != orgB[pi]) return 1;
            return (x == orgX[pi] && y == orgY[pi]) ? 0 : 0;
        }

        /// <summary>
        /// Keep the best objective, but when two layouts score the same prefer the one that
        /// moves fewer items - a giant reshuffle for zero gain is not worth doing in game.
        /// </summary>
        private void TrySaveBest()
        {
            double obj = CurrentObjective();
            int mv = CountMoves();
            if (obj < bestObj - 1e-6 || (Math.Abs(obj - bestObj) <= 1e-6 && mv < bestMoves))
            {
                bestObj = obj;
                bestMoves = mv;
                SaveBest();
            }
        }

        private int CountMoves()
        {
            int n = 0;
            for (int i = 0; i < pool.Count; i++)
                if (curB[i] != orgB[i] || curX[i] != orgX[i] || curY[i] != orgY[i] || curO[i] != orgO[i]) n++;
            return n;
        }

        private void SaveBest()
        {
            bestB = (int[])ToArray(curB);
            bestX = (int[])ToArray(curX);
            bestY = (int[])ToArray(curY);
            bestO = (int[])ToArray(curO);
        }

        private static Array ToArray(List<int> list)
        {
            var a = new int[list.Count];
            list.CopyTo(a);
            return a;
        }

        private void RestoreBest()
        {
            if (bestB == null) return;
            for (int i = 0; i < pool.Count; i++)
            {
                Take(i);
                Put(i, bestB[i], bestX[i], bestY[i], bestO[i]);
            }
        }
    }
}
