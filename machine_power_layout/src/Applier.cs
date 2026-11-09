using System;
using System.Collections.Generic;

namespace MachinePowerLayout
{
    /// <summary>Applies a plan to the live grid inventory, with full rollback on failure.</summary>
    public static class Applier
    {
        /// <summary>
        /// Applies the plan. Individual items whose target cell the game refuses are skipped
        /// rather than aborting the whole batch, because the planning rectangle is the panel
        /// outline and a non-rectangular panel has cells the game will not accept. The result
        /// is then re-read from the live grid; if it scores worse than the baseline the whole
        /// attempt is rolled back, so a partial apply can never leave a worse layout.
        /// </summary>
        public static bool Apply(GridModel model, Plan plan, out string error, out int mismatches, out int skipped)
        {
            error = null;
            mismatches = 0;
            skipped = 0;
            if (plan == null || plan.Moves.Count == 0) return true;

            GameGridInventory inv = model.Inv;
            List<Placement> moves = plan.Moves;

            var saved = new Dictionary<GameItem, GridShape>();
            for (int i = 0; i < moves.Count; i++)
            {
                GameItem it = moves[i].Item.Item;
                GridShape s = null;
                try { s = it.modifiedShape; }
                catch (Exception) { }
                if (s == null)
                {
                    try { s = it.shape; }
                    catch (Exception) { }
                }
                saved[it] = s;
            }

            // 1. take everything that moves out of the grid
            var expelled = new List<GameItem>();
            var placed = new List<Placement>();
            var rejected = new List<Placement>();
            for (int i = 0; i < moves.Count; i++)
            {
                GameItem it = moves[i].Item.Item;
                bool ok;
                try { ok = inv.Expel(it); }
                catch (Exception e) { ok = false; error = "expel threw: " + e.GetType().Name; }
                if (!ok)
                {
                    if (error == null) error = "expel refused: " + moves[i].Item.Key;
                    Restore(inv, placed, expelled, saved);
                    return false;
                }
                expelled.Add(it);
            }

            // 2. put them back, biggest first (machines before arrays)
            var ordered = new List<Placement>(moves);
            ordered.Sort((a, b) =>
            {
                if (a.Item.IsSource != b.Item.IsSource) return a.Item.IsSource ? 1 : -1;
                return b.Item.Area.CompareTo(a.Item.Area);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                Placement p = ordered[i];
                GameItem it = p.Item.Item;
                GridShape target = null;
                string why = null;
                try { target = Shapes.Build(it, p.Ori.Ori, p.X + model.Ox, p.Y + model.Oy); }
                catch (Exception e) { why = "shape build threw: " + e.GetType().Name; }

                if (target != null)
                {
                    bool fits;
                    try { fits = inv.CheckShapePlacement(it, target, new List<GameItem>()); }
                    catch (Exception e) { fits = false; why = "placement check threw: " + e.GetType().Name; }
                    if (!fits)
                    {
                        if (why == null) why = "no room at (" + p.X + "," + p.Y + ")";
                    }
                    else
                    {
                        it.modifiedShape = target;
                        bool accepted;
                        try { accepted = inv.UncheckedAccept(it); }
                        catch (Exception e) { accepted = false; why = "accept threw: " + e.GetType().Name; }
                        if (accepted)
                        {
                            placed.Add(p);
                            continue;
                        }
                        if (why == null) why = "accept refused";
                    }
                }

                // Keep the item out of the way but remember it: Restore() can put it back.
                skipped++;
                if (error == null) error = why + " for " + p.Item.Key;
                rejected.Add(p);
            }

            try { inv.Validate(); }
            catch (Exception) { }

            // A partial apply is only accepted when it actually scores better than the
            // layout we started from. Read the live grid back instead of trusting the model.
            if (rejected.Count > 0)
            {
                GridModel live = null;
                try { live = GridModel.Build(inv, true); }
                catch (Exception) { }
                double got = double.NegativeInfinity;
                if (live != null)
                {
                    try { got = Planner.Value(Report.EvaluateModel(live, model.ChargePerArray)); }
                    catch (Exception) { }
                }
                double baseline = Planner.Value(plan.Before);
                if (double.IsNegativeInfinity(got) || got < baseline - 1e-6)
                {
                    Restore(inv, placed, expelled, saved);
                    error = "partial apply scored worse than the original layout, rolled back ("
                            + rejected.Count + " cell(s) refused)";
                    return false;
                }
            }

            try { inv.Validate(); }
            catch (Exception) { }

            // Did the game really keep the requested positions? Trust but verify.
            for (int i = 0; i < placed.Count; i++)
            {
                Placement p = placed[i];
                GridShape actual = null;
                try { actual = p.Item.Item.modifiedShape; }
                catch (Exception) { }
                if (actual == null) { mismatches++; continue; }
                if (actual.minX - model.Ox != p.X || actual.minY - model.Oy != p.Y) mismatches++;
            }
            return true;
        }

        /// <summary>
        /// Applies a whole multi-panel plan: every item leaves its panel first, then goes to
        /// its target cell - possibly on another panel. One refusal unwinds the entire batch,
        /// so a half-applied layout is never left behind.
        /// </summary>
        public static bool ApplyGlobal(List<Remap> moves, out string error, out int skipped, out int mismatches)
        {
            error = null;
            skipped = 0;
            mismatches = 0;
            if (moves == null || moves.Count == 0) return true;

            var saved = new Dictionary<GameItem, GridShape>();
            for (int i = 0; i < moves.Count; i++)
            {
                GameItem it = moves[i].Item.Item;
                GridShape s = null;
                try { s = it.modifiedShape; }
                catch (Exception) { }
                saved[it] = s;
            }

            // 1. take everything out
            var done = new List<Remap>();
            for (int i = 0; i < moves.Count; i++)
            {
                Remap m = moves[i];
                bool ok;
                try { ok = m.From.Inv.Expel(m.Item.Item); }
                catch (Exception e) { ok = false; error = "expel threw: " + e.GetType().Name; }
                if (!ok)
                {
                    if (error == null) error = "expel refused: " + m.Item.Key;
                    Unwind(done, saved);
                    return false;
                }
                done.Add(m);
            }

            // 2. put them back, biggest first so small items fill the gaps
            var ordered = new List<Remap>(moves);
            ordered.Sort(delegate (Remap a, Remap b)
            {
                if (a.Item.IsSource != b.Item.IsSource) return a.Item.IsSource ? 1 : -1;
                return b.Item.Area.CompareTo(a.Item.Area);
            });

            var placed = new List<Remap>();
            for (int i = 0; i < ordered.Count; i++)
            {
                Remap m = ordered[i];
                GameItem it = m.Item.Item;
                GridShape target = null;
                try { target = Shapes.Build(it, m.Ori.Ori, m.X + m.To.Ox, m.Y + m.To.Oy); }
                catch (Exception e) { error = "shape build threw: " + e.GetType().Name; }

                if (target != null)
                {
                    bool fits;
                    try { fits = m.To.Inv.CheckShapePlacement(it, target, new List<GameItem>()); }
                    catch (Exception e) { fits = false; error = "placement check threw: " + e.GetType().Name; }
                    if (fits)
                    {
                        it.modifiedShape = target;
                        bool accepted;
                        try { accepted = m.To.Inv.UncheckedAccept(it); }
                        catch (Exception e) { accepted = false; error = "accept threw: " + e.GetType().Name; }
                        if (accepted) { placed.Add(m); continue; }
                        if (error == null) error = "accept refused";
                    }
                    else if (error == null) error = "no room at (" + m.X + "," + m.Y + ")";
                }

                skipped++;
                if (error == null) error = "placement failed for " + m.Item.Key;
            }

            ValidateAll(moves);

            if (skipped > 0)
            {
                Unwind(done, saved);
                error = skipped + " of " + moves.Count + " item(s) had no legal target cell, rolled back ("
                        + (error == null ? "?" : error) + ")";
                return false;
            }

            // 3. did the game keep the cells we asked for?
            for (int i = 0; i < placed.Count; i++)
            {
                Remap m = placed[i];
                GridShape actual = null;
                try { actual = m.Item.Item.modifiedShape; }
                catch (Exception) { }
                if (actual == null) { mismatches++; continue; }
                if (actual.minX - m.To.Ox != m.X || actual.minY - m.To.Oy != m.Y) mismatches++;
            }
            return true;
        }

        private static void ValidateAll(List<Remap> moves)
        {
            var seen = new HashSet<GameGridInventory>();
            for (int i = 0; i < moves.Count; i++)
            {
                GameGridInventory inv = moves[i].To.Inv;
                if (inv == null || !seen.Add(inv)) continue;
                try { inv.Validate(); }
                catch (Exception) { }
            }
        }

        /// <summary>Put every item that left its panel back where it came from.</summary>
        private static void Unwind(List<Remap> done, Dictionary<GameItem, GridShape> saved)
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                Remap m = done[i];
                GridShape s;
                if (!saved.TryGetValue(m.Item.Item, out s) || s == null) continue;
                try
                {
                    if (m.Item.Item.parentInventory != null) m.Item.Item.parentInventory.Expel(m.Item.Item);
                }
                catch (Exception) { }
                try
                {
                    m.Item.Item.modifiedShape = s;
                    m.From.Inv.CheckShapePlacement(m.Item.Item, s, new List<GameItem>());
                    m.From.Inv.UncheckedAccept(m.Item.Item);
                    m.From.Inv.Validate();
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Move one item from one grid inventory to another (cross-panel rebalancing).
        /// Rolls back to the original cell if the destination refuses it.
        /// </summary>
        public static bool CrossMove(Transfer t, out string error)
        {
            error = null;
            if (t == null || t.Item == null || t.Item.Item == null) { error = "empty transfer"; return false; }
            GameGridInventory from = t.From != null ? t.From.Inv : null;
            GameGridInventory to = t.To != null ? t.To.Inv : null;
            if (from == null || to == null) { error = "missing inventory"; return false; }

            GameItem item = t.Item.Item;
            GridShape original = item.modifiedShape;
            GridShape target = Shapes.Build(item, t.Ori.Ori, t.X + t.To.Ox, t.Y + t.To.Oy);
            if (target == null) { error = "shape build failed"; return false; }

            bool ok;
            try { ok = from.Expel(item); }
            catch (Exception e) { ok = false; error = "expel threw: " + e.GetType().Name; }
            if (!ok)
            {
                if (error == null) error = "expel refused";
                return false;
            }

            bool fits;
            try { fits = to.CheckShapePlacement(item, target, new List<GameItem>()); }
            catch (Exception e) { fits = false; error = "check threw: " + e.GetType().Name; }
            if (!fits)
            {
                PutBack(from, item, original);
                if (error == null) error = "destination cell rejected";
                return false;
            }

            item.modifiedShape = target;
            bool accepted;
            try { accepted = to.UncheckedAccept(item); }
            catch (Exception e) { accepted = false; error = "accept threw: " + e.GetType().Name; }
            if (!accepted)
            {
                PutBack(from, item, original);
                if (error == null) error = "accept refused";
                return false;
            }

            try { to.Validate(); } catch (Exception) { }
            try { from.Validate(); } catch (Exception) { }
            return true;
        }

        /// <summary>
        /// Undo a cross-panel move: take the item out of the panel it landed in and put it back
        /// where it came from, using the shape it had before. Used to unwind a bundle whose
        /// later steps were refused.
        /// </summary>
        public static bool MoveBack(Transfer t, GridShape original)
        {
            if (t == null || original == null) return false;
            GameItem item = t.Item.Item;
            GameGridInventory from = t.To != null ? t.To.Inv : null;
            GameGridInventory to = t.From != null ? t.From.Inv : null;
            if (from == null || to == null) return false;

            try
            {
                bool ok = from.Expel(item);
                if (!ok) return false;
                item.modifiedShape = original;
                to.CheckShapePlacement(item, original, new List<GameItem>());
                to.UncheckedAccept(item);
                to.Validate();
                return true;
            }
            catch (Exception) { return false; }
        }

        private static void PutBack(GameGridInventory from, GameItem item, GridShape original)
        {
            try
            {
                if (original != null) item.modifiedShape = original;
                if (item.parentInventory == null) from.UncheckedAccept(item);
                from.Validate();
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Undo an attempt: take out exactly what this attempt placed, then put every
        /// item that left the grid back at its saved shape. Items that never left are
        /// not touched, so a failed run cannot strand or lose them.
        /// </summary>
        private static void Restore(GameGridInventory inv, List<Placement> placed,
                                    List<GameItem> expelled, Dictionary<GameItem, GridShape> saved)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                GameItem it = placed[i].Item.Item;
                try
                {
                    if (it.parentInventory == inv) inv.Expel(it);
                }
                catch (Exception) { }
            }

            for (int i = 0; i < expelled.Count; i++)
            {
                GameItem it = expelled[i];
                GridShape s;
                if (!saved.TryGetValue(it, out s) || s == null) continue;
                try
                {
                    if (it.parentInventory == inv) continue;
                    it.modifiedShape = s;
                    inv.CheckShapePlacement(it, s, new List<GameItem>());
                    inv.UncheckedAccept(it);
                    inv.Validate();
                }
                catch (Exception) { }
            }
        }
    }
}
