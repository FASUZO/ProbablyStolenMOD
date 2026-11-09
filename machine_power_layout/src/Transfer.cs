using System;
using System.Collections.Generic;

namespace MachinePowerLayout
{
    public class Transfer
    {
        public AreaItem Item;          // the item being moved
        public GridModel From;
        public GridModel To;
        public int X, Y;               // target cell in the receiving panel
        public Orientation Ori;
        public double Net;             // objective delta of the whole move
        public int ReceiverGain;       // deficit units removed from the receiver
    }

    /// <summary>
    /// A machine plus the arrays it needs, moved together. Shipping the machine alone is
    /// pointless when the receiving panel has no spare arrays: it would land there unpowered.
    /// Shipping arrays alone into a panel with no machine is pointless too. So the two have
    /// to cross in one atomic step, which is what a bundle is.
    /// </summary>
    public class Bundle
    {
        public Transfer Machine;
        public List<Transfer> Extras = new List<Transfer>();
        public double Net;
    }

    /// <summary>
    /// Cross-panel scheduling: adjacency never crosses inventories, so a panel with spare
    /// arrays can only help a starved panel by physically handing one over.
    ///
    /// For every (donor array, receiving panel, target cell) triple the move is simulated
    /// on the models and scored with the same objective as the solver. The best positive
    /// move is taken, then the search repeats, so several panels can be rebalanced in one
    /// run. Only power arrays move; machines stay where the player put them.
    /// </summary>
    public static class TransferPlanner
    {
        public static string LastReason = "";

        public static List<Transfer> Plan(List<GridModel> models, int maxTransfers, bool allowMachines)
        {
            return Plan(models, maxTransfers, allowMachines, 400);
        }

        public static List<Transfer> Plan(List<GridModel> models, int maxTransfers, bool allowMachines, int budgetMs)
        {
            var result = new List<Transfer>();
            LastReason = "";
            if (models == null || models.Count < 2) { LastReason = "fewer than 2 panels"; return result; }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int iter = 0; iter < maxTransfers; iter++)
            {
                if (sw.ElapsedMilliseconds > budgetMs) { LastReason = "time budget hit"; break; }
                string why;
                Transfer best = NextBest(models, allowMachines, out why);
                if (best == null) { LastReason = why; break; }
                Commit(models, best);
                result.Add(best);
            }
            return result;
        }

        /// <summary>
        /// The single best cross-panel move right now, without committing it. The caller is
        /// expected to apply it to the live game and then re-solve both panels: the receiving
        /// panel usually needs to be rearranged to make room for the newcomer, and a panel
        /// that only gets "an empty cell that already touches a machine" has almost no room
        /// left in it, which is why the old single-shot version stopped after one transfer.
        /// </summary>
        public static Transfer NextBest(List<GridModel> models, bool allowMachines, out string why)
        {
            why = "";
            if (models == null || models.Count < 2) { why = "fewer than 2 panels"; return null; }

            Transfer best = null;
            double bestNet = 1e-6;
            int tried = 0;
            double bestSeen = double.NegativeInfinity;

            for (int di = 0; di < models.Count; di++)
            {
                GridModel donor = models[di];
                int dL = donor.ChargePerArray;
                if (dL <= 0) continue;
                Metrics dm = Report.EvaluateModel(donor, dL);
                double donorValue = Planner.Value(dm);

                for (int ai = 0; ai < donor.Items.Count; ai++)
                {
                    AreaItem arr = donor.Items[ai];
                    if (!arr.IsSource && !(allowMachines && arr.IsMachine)) continue;
                    if (arr.Orientations.Count == 0) continue;

                    // simulate the donation
                    donor.Items.RemoveAt(ai);
                    Metrics dAfter = Report.EvaluateModel(donor, dL);
                    double donorDelta = Planner.Value(dAfter);
                    donorDelta -= donorValue;
                    donor.Items.Insert(ai, arr);

                    // The donation must not make the donor worse. "Worse" is measured on the
                    // deficit, not just the uncovered count: losing a spare array may leave the
                    // same number of machines short but deepen the hole. (A machine is the
                    // reverse case - relocating a starved machine into a panel full of arrays
                    // lowers the donor's deficit, so it passes naturally.)
                    if (dAfter.Uncovered > dm.Uncovered) continue;
                    if (dAfter.Deficit > dm.Deficit) continue;

                    for (int ri = 0; ri < models.Count; ri++)
                    {
                        if (ri == di) continue;
                        GridModel recv = models[ri];
                        int rL = recv.ChargePerArray;
                        Metrics before = Report.EvaluateModel(recv, rL);

                        // Shipping an array to a panel with no power-drawing machine is pure
                        // waste: it cannot score a contact there. (An empty panel still made
                        // a "profit" while idle machines counted their neighbours, which is
                        // exactly how a spare array ended up in the 24x10 panel.)
                        if (arr.IsSource && before.Machines == 0 && !HasChargeableBattery(recv)) continue;

                        // An array can help a panel in two ways: cover a machine that is
                        // short, or simply score more contacts there (priority 3). Both are
                        // legitimate, so no gate here - the objective decides.
                        double recvValue = Planner.Value(before);

                        List<int[]> spots = SpotList(recv, arr);
                        int origX = arr.X, origY = arr.Y;
                        for (int si = 0; si < spots.Count; si++)
                        {
                            int x = spots[si][0], y = spots[si][1], oi = spots[si][2];

                            arr.X = x; arr.Y = y;
                            recv.Items.Add(arr);
                            Metrics afterM = Report.EvaluateModel(recv, rL);
                            recv.Items.RemoveAt(recv.Items.Count - 1);
                            arr.X = origX; arr.Y = origY;      // the donor model must stay intact

                            double net = donorDelta + (Planner.Value(afterM) - recvValue);
                            tried++;
                            if (net > bestSeen) bestSeen = net;
                            if (net > bestNet)
                            {
                                bestNet = net;
                                best = new Transfer
                                {
                                    Item = arr,
                                    From = donor,
                                    To = recv,
                                    X = x,
                                    Y = y,
                                    Ori = arr.Orientations[oi],
                                    Net = net,
                                    ReceiverGain = before.Deficit - afterM.Deficit
                                };
                            }
                        }
                    }
                }
            }

            if (best == null)
                why = "no net improvement (tried " + tried + " placements, best net "
                      + (double.IsNegativeInfinity(bestSeen) ? "n/a" : bestSeen.ToString("F1")) + ")";
            else
                why = "best net " + bestNet.ToString("F1") + " of " + tried + " placements";
            return best;
        }

        /// <summary>Applies a chosen transfer to the in-memory models (not the game).</summary>
        public static void Commit(List<GridModel> models, Transfer t)
        {
            if (t == null) return;
            t.From.Items.Remove(t.Item);
            t.Item.X = t.X;
            t.Item.Y = t.Y;
            t.Item.Ori = t.Ori.Ori;
            t.Item.W = t.Ori.W;
            t.Item.H = t.Ori.H;
            t.Item.Mask = t.Ori.Mask;
            t.To.Items.Add(t.Item);
        }

        // ------------------------------------------------------------------
        // Bundles: a machine plus the arrays it needs, crossing together
        // ------------------------------------------------------------------

        private class Snap
        {
            public AreaItem It;
            public GridModel Owner;
            public int X, Y, Ori, W, H;
            public bool[] Mask;
        }

        private static List<Snap> Snapshot(GridModel a, GridModel b)
        {
            var list = new List<Snap>();
            Take(a); Take(b);
            return list;

            void Take(GridModel m)
            {
                for (int i = 0; i < m.Items.Count; i++)
                {
                    AreaItem it = m.Items[i];
                    list.Add(new Snap
                    {
                        It = it, Owner = m, X = it.X, Y = it.Y,
                        Ori = it.Ori, W = it.W, H = it.H, Mask = it.Mask
                    });
                }
            }
        }

        private static void RestoreSnap(List<Snap> snap, GridModel a, GridModel b)
        {
            a.Items.Clear();
            b.Items.Clear();
            for (int i = 0; i < snap.Count; i++)
            {
                Snap s = snap[i];
                s.It.X = s.X; s.It.Y = s.Y;
                s.It.Ori = s.Ori; s.It.W = s.W; s.It.H = s.H; s.It.Mask = s.Mask;
                s.Owner.Items.Add(s.It);
            }
            for (int i = 0; i < a.Items.Count; i++) a.Items[i].Index = i;
            for (int i = 0; i < b.Items.Count; i++) b.Items[i].Index = i;
        }

        private static void MoveItem(AreaItem it, GridModel from, GridModel to, int x, int y, Orientation o)
        {
            from.Items.Remove(it);
            it.X = x; it.Y = y;
            it.Ori = o.Ori; it.W = o.W; it.H = o.H; it.Mask = o.Mask;
            to.Items.Add(it);
        }

        /// <summary>Any legal placement, spilling outward from the top-left corner.</summary>
        private static void AddAnywhere(GridModel model, AreaItem item, int limit, List<int[]> into)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < into.Count; i++)
                seen.Add((into[i][1] * model.W + into[i][0]) * 8 + into[i][2]);

            bool[] occ = Occupancy(model);
            int added = 0;
            for (int oi = 0; oi < item.Orientations.Count && added < limit; oi++)
            {
                Orientation o = item.Orientations[oi];
                for (int y = 0; y + o.H <= model.H && added < limit; y++)
                {
                    for (int x = 0; x + o.W <= model.W && added < limit; x++)
                    {
                        if (!Fits(model, o, x, y, occ)) continue;
                        int key = (y * model.W + x) * 8 + oi;
                        if (!seen.Add(key)) continue;
                        into.Add(new[] { x, y, oi });
                        added++;
                    }
                }
            }
        }

        /// <summary>Legal placements of an array that hug the given machine.</summary>
        private static List<int[]> SpotsAround(GridModel model, AreaItem arr, AreaItem mach)
        {
            var spots = new List<int[]>();
            var seen = new HashSet<int>();
            bool[] occ = Occupancy(model);

            for (int dy = 0; dy < mach.H; dy++)
            {
                for (int dx = 0; dx < mach.W; dx++)
                {
                    if (!mach.Mask[dy * mach.W + dx]) continue;
                    int cx = mach.X + dx, cy = mach.Y + dy;
                    TryAnchor(model, arr, cx + 1, cy, occ, seen, spots);
                    TryAnchor(model, arr, cx - 1, cy, occ, seen, spots);
                    TryAnchor(model, arr, cx, cy + 1, occ, seen, spots);
                    TryAnchor(model, arr, cx, cy - 1, occ, seen, spots);
                }
            }
            return spots;
        }

        /// <summary>
        /// Best relocation of one machine to another panel, together with as many arrays as
        /// that machine needs on the other side. Returns null when nothing scores above zero.
        /// </summary>
        public static Bundle NextBestBundle(List<GridModel> models, int budgetMs)
        {
            if (models == null || models.Count < 2) return null;

            Bundle best = null;
            double bestNet = 1e-6;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int considered = 0;

            for (int di = 0; di < models.Count; di++)
            {
                GridModel donor = models[di];
                int dL = donor.ChargePerArray;
                if (dL <= 0) continue;

                for (int ri = 0; ri < models.Count; ri++)
                {
                    if (ri == di) continue;
                    GridModel recv = models[ri];
                    int rL = recv.ChargePerArray;
                    if (rL <= 0) continue;

                    double baseScore = Planner.Value(Report.EvaluateModel(donor, dL))
                                       + Planner.Value(Report.EvaluateModel(recv, rL));

                    for (int mi = 0; mi < donor.Items.Count; mi++)
                    {
                        AreaItem mach = donor.Items[mi];
                        if (!mach.IsMachine) continue;
                        if (mach.Demand <= 0) continue;
                        if (mach.Orientations.Count == 0) continue;

                        List<int[]> spots = SpotsNextToArrays(recv, mach);
                        // An empty panel has no array to hug, yet it is exactly where a
                        // crowded panel wants to send a machine - the arrays travel with it.
                        if (spots.Count < 6) AddAnywhere(recv, mach, 20, spots);
                        int limit = Math.Min(spots.Count, 16);
                        for (int si = 0; si < limit; si++)
                        {
                            if (sw.ElapsedMilliseconds > budgetMs) return best;
                            considered++;

                            List<Snap> snap = Snapshot(donor, recv);
                            int x = spots[si][0], y = spots[si][1], oi = spots[si][2];
                            Orientation mo = mach.Orientations[oi];
                            MoveItem(mach, donor, recv, x, y, mo);

                            // How many arrays does this machine still need over here?
                            int supply = AdjacentSources(recv, mach, Occupancy(recv)) * rL;
                            int missing = mach.Demand - supply;
                            int want = missing > 0 ? (missing + rL - 1) / rL : 0;
                            if (want > 4) want = 4;

                            var extras = new List<Transfer>();
                            bool ok = true;
                            for (int e = 0; e < want; e++)
                            {
                                AreaItem pick = null;
                                int px = 0, py = 0;
                                Orientation po = null;
                                for (int ai = donor.Items.Count - 1; ai >= 0; ai--)
                                {
                                    AreaItem arr = donor.Items[ai];
                                    if (!arr.IsSource) continue;
                                    if (arr.Orientations.Count == 0) continue;
                                    List<int[]> spots2 = SpotsAround(recv, arr, mach);
                                    if (spots2.Count == 0) continue;
                                    pick = arr;
                                    px = spots2[0][0];
                                    py = spots2[0][1];
                                    po = arr.Orientations[spots2[0][2]];
                                    break;
                                }
                                if (pick == null) { ok = false; break; }
                                MoveItem(pick, donor, recv, px, py, po);
                                extras.Add(new Transfer
                                {
                                    Item = pick, From = donor, To = recv,
                                    X = px, Y = py, Ori = po
                                });
                            }

                            if (ok && extras.Count == want)
                            {
                                double score = Planner.Value(Report.EvaluateModel(donor, dL))
                                               + Planner.Value(Report.EvaluateModel(recv, rL))
                                               - baseScore;
                                if (score > bestNet)
                                {
                                    bestNet = score;
                                    best = new Bundle
                                    {
                                        Machine = new Transfer
                                        {
                                            Item = mach, From = donor, To = recv,
                                            X = x, Y = y, Ori = mo, Net = score
                                        },
                                        Extras = extras,
                                        Net = score
                                    };
                                }
                            }

                            RestoreSnap(snap, donor, recv);
                        }
                    }
                }
            }

            LastBundleReason = best == null
                ? "no machine relocation pays off (" + considered + " placements tried)"
                : "best bundle net=" + bestNet.ToString("F1") + " with " + best.Extras.Count
                  + " array(s), of " + considered + " placements";
            return best;
        }

        public static string LastBundleReason = "";


        /// <summary>True when the panel holds a loose battery with room left to charge.</summary>
        private static bool HasChargeableBattery(GridModel m)
        {
            for (int i = 0; i < m.Items.Count; i++)
            {
                AreaItem it = m.Items[i];
                if (!it.IsBattery) continue;
                if (it.MaxEnergy > it.Energy) return true;
            }
            return false;
        }

        /// <summary>
        /// Candidate cells for an incoming item: the union of "next to a machine that is
        /// short" (fixes a deficit) and "touches as many machines as possible" (scores more
        /// contacts). Deduplicated, best first.
        /// </summary>
        private static List<int[]> SpotList(GridModel model, AreaItem item)
        {
            List<int[]> spots = item.IsSource ? SpotsBestForArray(model, item) : SpotsNextToArrays(model, item);
            if (!item.IsSource) return spots;

            List<int[]> starved = SpotsNextToStarved(model, item);
            var seen = new HashSet<int>();
            for (int i = 0; i < spots.Count; i++) seen.Add((spots[i][1] * model.W + spots[i][0]) * 8 + spots[i][2]);
            for (int i = 0; i < starved.Count; i++)
            {
                int key = (starved[i][1] * model.W + starved[i][0]) * 8 + starved[i][2];
                if (seen.Add(key)) spots.Add(starved[i]);
            }
            return spots;
        }

        /// <summary>Free placements of an array, most adjacent machines first (top 48).</summary>
        private static List<int[]> SpotsBestForArray(GridModel model, AreaItem arr)
        {
            var scored = new List<int[]>();
            bool[] occ = Occupancy(model);

            for (int oi = 0; oi < arr.Orientations.Count; oi++)
            {
                Orientation o = arr.Orientations[oi];
                for (int y = 0; y + o.H <= model.H; y++)
                {
                    for (int x = 0; x + o.W <= model.W; x++)
                    {
                        if (!Fits(model, o, x, y, occ)) continue;
                        int adj = NeighbourCount(model, o, x, y, true);
                        if (adj <= 0) continue;
                        scored.Add(new[] { x, y, oi, adj });
                    }
                }
            }

            scored.Sort(delegate (int[] p, int[] q) { return q[3].CompareTo(p[3]); });
            var spots = new List<int[]>();
            for (int i = 0; i < scored.Count && i < 48; i++)
                spots.Add(new[] { scored[i][0], scored[i][1], scored[i][2] });
            return spots;
        }

        /// <summary>Distinct neighbours of a hypothetical footprint, optionally machines only.</summary>
        private static int NeighbourCount(GridModel model, Orientation o, int x, int y, bool machinesOnly)
        {
            var hit = new HashSet<int>();
            for (int dy = 0; dy < o.H; dy++)
                for (int dx = 0; dx < o.W; dx++)
                {
                    if (!o.Mask[dy * o.W + dx]) continue;
                    int cx = x + dx, cy = y + dy;
                    Collect(model, cx + 1, cy, machinesOnly, hit);
                    Collect(model, cx - 1, cy, machinesOnly, hit);
                    Collect(model, cx, cy + 1, machinesOnly, hit);
                    Collect(model, cx, cy - 1, machinesOnly, hit);
                }
            return hit.Count;
        }

        private static void Collect(GridModel model, int x, int y, bool machinesOnly, HashSet<int> hit)
        {
            if (x < 0 || y < 0 || x >= model.W || y >= model.H) return;
            for (int i = 0; i < model.Items.Count; i++)
            {
                AreaItem o = model.Items[i];
                if (machinesOnly)
                {
                    if (!o.IsMachine) continue;
                }
                else
                {
                    if (!o.IsSource) continue;
                }
                int dx = x - o.X, dy = y - o.Y;
                if (dx < 0 || dy < 0 || dx >= o.W || dy >= o.H) continue;
                if (o.Mask[dy * o.W + dx]) { hit.Add(i); return; }
            }
        }

        /// <summary>
        /// Free placements for a machine: anywhere it fits and touches at least one array,
        /// best contacts first. Used when a machine is moved into a panel that has spare
        /// arrays but nothing short today.
        /// </summary>
        private static List<int[]> SpotsNextToArrays(GridModel model, AreaItem mach)
        {
            var scored = new List<int[]>();
            bool[] occ = Occupancy(model);

            for (int oi = 0; oi < mach.Orientations.Count; oi++)
            {
                Orientation o = mach.Orientations[oi];
                for (int y = 0; y + o.H <= model.H; y++)
                {
                    for (int x = 0; x + o.W <= model.W; x++)
                    {
                        if (!Fits(model, o, x, y, occ)) continue;
                        int adj = ArraysAround(model, o, x, y, occ);
                        if (adj <= 0) continue;
                        scored.Add(new[] { x, y, oi, adj });
                    }
                }
            }

            scored.Sort(delegate (int[] p, int[] q) { return q[3].CompareTo(p[3]); });
            var spots = new List<int[]>();
            for (int i = 0; i < scored.Count && i < 48; i++)
                spots.Add(new[] { scored[i][0], scored[i][1], scored[i][2] });
            return spots;
        }

        private static int ArraysAround(GridModel model, Orientation o, int x, int y, bool[] occ)
        {
            var hit = new HashSet<int>();
            for (int dy = 0; dy < o.H; dy++)
                for (int dx = 0; dx < o.W; dx++)
                {
                    if (!o.Mask[dy * o.W + dx]) continue;
                    int cx = x + dx, cy = y + dy;
                    Probe(model, occ, cx + 1, cy, hit);
                    Probe(model, occ, cx - 1, cy, hit);
                    Probe(model, occ, cx, cy + 1, hit);
                    Probe(model, occ, cx, cy - 1, hit);
                }
            return hit.Count;
        }

        /// <summary>Free placements of this array that touch at least one uncovered machine.</summary>
        private static List<int[]> SpotsNextToStarved(GridModel model, AreaItem arr)
        {
            var spots = new List<int[]>();
            var seen = new HashSet<int>();
            bool[] occ = Occupancy(model);

            for (int i = 0; i < model.Items.Count; i++)
            {
                AreaItem mch = model.Items[i];
                if (!mch.IsMachine) continue;
                if (mch.Demand <= 0) continue;

                int supply = AdjacentSources(model, mch, occ);
                int have = supply * model.ChargePerArray;
                if (have >= mch.Demand) continue;                  // already covered

                for (int dy = 0; dy < mch.H; dy++)
                {
                    for (int dx = 0; dx < mch.W; dx++)
                    {
                        if (!mch.Mask[dy * mch.W + dx]) continue;
                        int cx = mch.X + dx, cy = mch.Y + dy;
                        TryAnchor(model, arr, cx + 1, cy, occ, seen, spots);
                        TryAnchor(model, arr, cx - 1, cy, occ, seen, spots);
                        TryAnchor(model, arr, cx, cy + 1, occ, seen, spots);
                        TryAnchor(model, arr, cx, cy - 1, occ, seen, spots);
                    }
                }
            }
            return spots;
        }

        /// <summary>Places the array so that one of its cells lands on the anchor cell.</summary>
        private static void TryAnchor(GridModel model, AreaItem arr, int ax, int ay,
                                      bool[] occ, HashSet<int> seen, List<int[]> spots)
        {
            for (int oi = 0; oi < arr.Orientations.Count; oi++)
            {
                Orientation o = arr.Orientations[oi];
                for (int inside = 0; inside < o.W * o.H; inside++)
                {
                    if (!o.Mask[inside]) continue;
                    int dx = inside % o.W, dy = inside / o.W;
                    int x = ax - dx, y = ay - dy;
                    if (x < 0 || y < 0 || x + o.W > model.W || y + o.H > model.H) continue;

                    int key = (y * model.W + x) * 8 + oi;
                    if (!seen.Add(key)) continue;
                    if (!Fits(model, o, x, y, occ)) continue;
                    spots.Add(new[] { x, y, oi });
                }
            }
        }

        private static bool Fits(GridModel model, Orientation o, int x, int y, bool[] occ)
        {
            for (int dy = 0; dy < o.H; dy++)
                for (int dx = 0; dx < o.W; dx++)
                {
                    if (!o.Mask[dy * o.W + dx]) continue;
                    int c = model.Cell(x + dx, y + dy);
                    if (model.Blocked[c] || occ[c]) return false;
                }
            return true;
        }

        private static bool[] Occupancy(GridModel model)
        {
            var occ = new bool[model.W * model.H];
            for (int i = 0; i < model.Items.Count; i++)
            {
                AreaItem it = model.Items[i];
                for (int dy = 0; dy < it.H; dy++)
                    for (int dx = 0; dx < it.W; dx++)
                    {
                        if (!it.Mask[dy * it.W + dx]) continue;
                        int x = it.X + dx, y = it.Y + dy;
                        if (x < 0 || y < 0 || x >= model.W || y >= model.H) continue;
                        occ[y * model.W + x] = true;
                    }
            }
            return occ;
        }

        private static int AdjacentSources(GridModel model, AreaItem mch, bool[] occ)
        {
            var hit = new HashSet<int>();
            for (int dy = 0; dy < mch.H; dy++)
                for (int dx = 0; dx < mch.W; dx++)
                {
                    if (!mch.Mask[dy * mch.W + dx]) continue;
                    int cx = mch.X + dx, cy = mch.Y + dy;
                    Probe(model, occ, cx + 1, cy, hit);
                    Probe(model, occ, cx - 1, cy, hit);
                    Probe(model, occ, cx, cy + 1, hit);
                    Probe(model, occ, cx, cy - 1, hit);
                }
            return hit.Count;
        }

        private static void Probe(GridModel model, bool[] occ, int x, int y, HashSet<int> hit)
        {
            if (x < 0 || y < 0 || x >= model.W || y >= model.H) return;
            if (!occ[model.Cell(x, y)]) return;
            for (int i = 0; i < model.Items.Count; i++)
            {
                AreaItem o = model.Items[i];
                if (!o.IsSource) continue;
                int dx = x - o.X, dy = y - o.Y;
                if (dx < 0 || dy < 0 || dx >= o.W || dy >= o.H) continue;
                if (o.Mask[dy * o.W + dx]) { hit.Add(i); return; }
            }
        }
    }
}
