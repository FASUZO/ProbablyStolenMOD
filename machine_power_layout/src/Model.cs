using System;
using System.Collections.Generic;
using UnityEngine;

namespace MachinePowerLayout
{
    /// <summary>Shape helpers. All shapes are built through the game's own GridShapeBuilder.</summary>
    public static class Shapes
    {
        public static GridShape Build(GameItem item, int ori, int gx, int gy)
        {
            GridShape src = item.modifiedShape;
            if (src == null) src = item.shape;
            if (src == null) return null;
            var b = new GridShapeBuilder(src);
            b.SetOrientation(ori);
            b.SetPosition(gx, gy);
            return b.shape;
        }

        public static bool[] MaskOf(GridShape s, out int w, out int h)
        {
            w = 0;
            h = 0;
            if (s == null) return null;
            int x0 = s.minX;
            int y0 = s.minY;
            w = s.maxX - x0 + 1;
            h = s.maxY - y0 + 1;
            if (w <= 0 || h <= 0) return null;
            var mask = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mask[y * w + x] = s.Get(x0 + x, y0 + y) != 0;
            return mask;
        }

        public static bool IsFullRect(bool[] mask, int w, int h)
        {
            if (mask == null || w <= 0 || h <= 0) return false;
            for (int i = 0; i < mask.Length; i++) if (!mask[i]) return false;
            return true;
        }
    }

    public class Orientation
    {
        public int Ori;
        public int W, H;
        public bool[] Mask;
    }

    /// <summary>One item living in the machine-area grid.</summary>
    public class AreaItem
    {
        public GameItem Item;
        public int Index;          // stable index inside GridModel.Items
        public int X, Y;           // current top-left position, grid-local (0-based)
        public int Ori;            // current shape orientation
        public int W, H;           // current footprint
        public bool[] Mask;        // W*H, true = cell occupied by the item
        public bool IsSource;      // power array (charges neighbours)
        public bool IsMachine;     // rechargeable machine (target)
        public bool IsBattery;     // loose power_source_item (target)
        public int Demand;         // per-night power draw (machines)
        public int BaseDemand;     // draw before module effects
        public int Energy;         // battery charge now
        public int MaxEnergy;      // battery capacity
        public string Key = "?";   // ASCII identity for logs (sprite path)
        public string Identifier = "";  // stable item id (water_purifier, smuggler_bay, ...)

        /// <summary>Footprint orientations this item may use.</summary>
        public List<Orientation> Orientations = new List<Orientation>();

        public bool Target { get { return IsMachine || IsBattery; } }
        public int Area { get { return W * H; } }
    }

    /// <summary>Grid snapshot plus everything needed to score a layout of it.</summary>
    public class GridModel
    {
        public GameGridInventory Inv;
        public int W, H;                 // grid size, grid-local
        public int Ox, Oy;               // global offset of grid-local (0,0)
        public int ItemX0, ItemY0;       // global origin of the items' bounding box
        public int ShapeW, ShapeH;       // panel outline reported by inventoryShape
        public bool[] Blocked;           // W*H, true = not a usable cell
        public List<AreaItem> Items = new List<AreaItem>();
        public int ChargePerArray = 2;
        public bool Commercial;

        public int Cell(int x, int y) { return y * W + x; }
        public bool InBounds(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H; }

        /// <summary>Usable cells that no item occupies - the solver's headroom.</summary>
        public int FreeCells
        {
            get
            {
                var occ = new bool[W * H];
                for (int i = 0; i < Items.Count; i++)
                {
                    AreaItem it = Items[i];
                    for (int dy = 0; dy < it.H; dy++)
                        for (int dx = 0; dx < it.W; dx++)
                        {
                            if (!it.Mask[dy * it.W + dx]) continue;
                            int x = it.X + dx, y = it.Y + dy;
                            if (x < 0 || y < 0 || x >= W || y >= H) continue;
                            occ[y * W + x] = true;
                        }
                }
                int n = 0;
                for (int i = 0; i < occ.Length; i++) if (!occ[i] && !Blocked[i]) n++;
                return n;
            }
        }

        public int MachineCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Items.Count; i++) if (Items[i].IsMachine) n++;
                return n;
            }
        }

        public int ArrayCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Items.Count; i++) if (Items[i].IsSource) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------
        // Locate the machine area
        // ------------------------------------------------------------------

        /// <summary>
        /// Every grid inventory that holds at least one machine, most machines first.
        /// The store can expose several machine areas ("机器区（扩展）" panels); adjacency
        /// never crosses inventories, so each one has to be optimised on its own.
        /// </summary>
        public static List<GameGridInventory> MachineAreas()
        {
            return MachineAreas(false);
        }

        /// <summary>
        /// openOnly: keep only panels whose UI is currently on screen (the window the
        /// player has open). If nothing reports as open, all panels are returned, so the
        /// hotkey never silently does nothing.
        /// </summary>
        public static List<GameGridInventory> MachineAreas(bool openOnly)
        {
            var result = new List<GameGridInventory>();
            List<GameGridInventory> all = Candidates();
            for (int i = 0; i < all.Count; i++)
            {
                int machines, arrays;
                CountTargets(all[i], out machines, out arrays);
                if (machines > 0) result.Add(all[i]);
            }
            result.Sort(delegate (GameGridInventory a, GameGridInventory b)
            {
                int ma, aa, mb, ab;
                CountTargets(a, out ma, out aa);
                CountTargets(b, out mb, out ab);
                if (ma != mb) return mb.CompareTo(ma);
                return ab.CompareTo(aa);
            });

            if (openOnly)
            {
                var visible = new List<GameGridInventory>();
                for (int i = 0; i < result.Count; i++)
                    if (IsWindowOpen(result[i])) visible.Add(result[i]);
                if (visible.Count > 0) return visible;
            }
            return result;
        }

        /// <summary>True when the inventory's UI is on screen (window open, parents active).</summary>
        public static bool IsWindowOpen(GameGridInventory inv)
        {
            try
            {
                if (inv == null) return false;
                RectTransform rt = inv.rectTransform;
                if (rt == null) return false;
                if (rt.gameObject == null) return false;
                return rt.gameObject.activeInHierarchy;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Counts machines and arrays inside one grid inventory.</summary>
        public static void Describe(GameGridInventory inv, out int machines, out int arrays)
        {
            CountTargets(inv, out machines, out arrays);
        }

        /// <summary>Diagnostic dump of the last Locate() scan (grid sizes and contents).</summary>
        public static string LastScan = "";

        public static GameGridInventory Locate(out string how)
        {
            how = "none";
            GameGridInventory best = null;
            int bestScore = -1;
            string dump = "";

            List<GameGridInventory> candidates = Candidates();

            for (int i = 0; i < candidates.Count; i++)
            {
                GameGridInventory inv = candidates[i];
                int machines, arrays;
                CountTargets(inv, out machines, out arrays);

                GridShape gs = null;
                try { gs = inv.inventoryShape; }
                catch (Exception) { }
                dump += " [" + i + "] " + (gs == null ? "?x?" : gs.globalWidth + "x" + gs.globalHeight)
                        + " m=" + machines + " a=" + arrays + ";";

                if (machines <= 0 && arrays <= 0) continue;
                int score = machines * 100 + arrays;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = inv;
                    how = "candidate #" + i;
                }
            }

            LastScan = dump;
            return best;
        }

        /// <summary>
        /// Candidate grid inventories. GameGridInventory is a plain class (not a
        /// UnityEngine.Object), so it cannot be found with FindObjectsOfType; the
        /// store's grids are read from the entry point's public properties instead,
        /// plus every grid inventory nested inside those (machine areas can live
        /// inside containers).
        /// </summary>
        private static List<GameGridInventory> Candidates()
        {
            var list = new List<GameGridInventory>();

            try
            {
                if (PlayerStore.Instance != null) Add(list, PlayerStore.Instance.GetStoreInventory());
            }
            catch (Exception) { }

            EmporiumEntry e = null;
            try { e = EmporiumEntry.Instance; }
            catch (Exception) { }
            if (e != null)
            {
                try { Add(list, e.invElement); } catch (Exception) { }
                try { Add(list, e.backInvinvElement); } catch (Exception) { }
                try { Add(list, e.backInvinvElementCounter); } catch (Exception) { }
                try { Add(list, e.frontInvinvElement); } catch (Exception) { }
                try { Add(list, e.showcaseElement); } catch (Exception) { }
                try { Add(list, e.hiddenElement); } catch (Exception) { }
                try { Add(list, e.bazarLeftinvElement); } catch (Exception) { }
                try { Add(list, e.soldElement); } catch (Exception) { }
                try { Add(list, e.trashcanInvElement); } catch (Exception) { }
                try { Add(list, e.afterhourInventory); } catch (Exception) { }
                try { Add(list, e.hirelingInv); } catch (Exception) { }
                try { Add(list, e.responseInventory); } catch (Exception) { }
                try { Add(list, e.responseInventoryClosable); } catch (Exception) { }
            }

            // Nested inventories (containers / storage bays / machine bodies).
            var extra = new List<GameGridInventory>();
            int roots = list.Count;
            for (int i = 0; i < roots; i++)
            {
                try
                {
                    List<GameGridInventory> kids =
                        GraphUtils.FindAllChildrenType<GameGridInventory>(list[i], KeepAll, KeepAll);
                    if (kids == null) continue;
                    for (int k = 0; k < kids.Count; k++)
                    {
                        GameGridInventory kid = kids[k];
                        if (kid == null || list.Contains(kid) || extra.Contains(kid)) continue;
                        extra.Add(kid);
                    }
                }
                catch (Exception) { }
                if (list.Count + extra.Count > 60) break;
            }
            list.AddRange(extra);
            return list;
        }

        private static bool KeepAll(GameGridInventory inv) { return inv != null; }

        private static void Add(List<GameGridInventory> list, GameGridInventory inv)
        {
            if (inv == null) return;
            if (list.Contains(inv)) return;
            list.Add(inv);
        }

        private static void CountTargets(GameGridInventory inv, out int machines, out int arrays)
        {
            machines = 0;
            arrays = 0;
            List<GameItem> items = null;
            try { items = inv.childItems; }
            catch (Exception) { return; }
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                GameItem it = items[i];
                if (it == null) continue;
                if (IsPowerArray(it)) arrays++;
                else if (IsRealMachine(it)) machines++;
            }
        }

        public static bool IsPowerArray(GameItem item)
        {
            if (item == null) return false;
            string sprite = item.spritePath;
            if (!string.IsNullOrEmpty(sprite) && sprite.StartsWith("powerblock", StringComparison.Ordinal)) return true;
            string id = item.identifier;
            return id == "powerblock";
        }

        /// <summary>
        /// A machine in the layout sense: it carries MACHINE_RECHARGABLE_TAG and is not a power
        /// array. A machine that draws nothing at night (bottle_printer) still counts here - it
        /// is filtered later by Demand, because "no nightly draw" is a runtime property, not a
        /// reason to suspect the item. Storage containers never reach this point: smuggler_bay
        /// only has STORAGE / CONTAINER_TAG, no MACHINE_RECHARGABLE_TAG.
        /// </summary>
        public static bool IsRealMachine(GameItem item)
        {
            if (item == null) return false;
            if (IsPowerArray(item)) return false;
            try { return item.IsTag(MachineHelper.MACHINE_RECHARGABLE_TAG); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        /// <summary>
        /// Planning rectangle = union of the panel outline (inventoryShape extent) and the
        /// items' bounding box. Pure so it can be asserted offline: dropping the shape makes
        /// a panel whose top rows are empty shrink, which is exactly bug 1.9.0 fixed.
        /// </summary>
        public static bool PlanRect(int ix0, int iy0, int ix1, int iy1,
                                    int sx0, int sy0, int sx1, int sy1,
                                    out int ox0, out int oy0, out int ox1, out int oy1,
                                    out bool shapeUsed)
        {
            ox0 = ix0; oy0 = iy0; ox1 = ix1; oy1 = iy1;
            shapeUsed = false;
            if (ix0 == int.MaxValue || iy0 == int.MaxValue || ix1 == int.MinValue) return false;

            bool sane = sx1 >= sx0 && sy1 >= sy0
                        && (sx1 - sx0 + 1) <= 64 && (sy1 - sy0 + 1) <= 64;
            if (!sane) return true;

            shapeUsed = true;
            if (sx0 < ox0) ox0 = sx0;
            if (sy0 < oy0) oy0 = sy0;
            if (sx1 > ox1) ox1 = sx1;
            if (sy1 > oy1) oy1 = sy1;
            return true;
        }

        public static GridModel Build(GameGridInventory inv, bool allowRotation)
        {
            if (inv == null) return null;
            GridShape gs = inv.inventoryShape;
            if (gs == null) return null;

            List<GameItem> children = inv.childItems;
            if (children == null) return null;

            var m = new GridModel();
            m.Inv = inv;

            // The planning region is the panel's own cell rectangle UNION the bounding box
            // of the items. Using the item bounding box alone (pre-1.9.0) silently dropped
            // any row or column that happened to be empty - a 11x14 panel whose top two rows
            // were free became an 11x12 model, so the solver could never use that space.
            //
            // The union is safe because inventoryShape's extent is the panel outline; when
            // the shape carries no usable mask (Get() returns 0 everywhere, which is what
            // happens for these panel inventories) the rectangle is treated as usable and
            // the game's own CheckShapePlacement stays the final authority at apply time.
            int gx0 = int.MaxValue, gy0 = int.MaxValue;
            int gx1 = int.MinValue, gy1 = int.MinValue;

            for (int i = 0; i < children.Count; i++)
            {
                GameItem it = children[i];
                if (it == null) continue;
                AreaItem ai = Read(it, allowRotation);
                if (ai == null) continue;
                if (ai.X < gx0) gx0 = ai.X;
                if (ai.Y < gy0) gy0 = ai.Y;
                if (ai.X + ai.W - 1 > gx1) gx1 = ai.X + ai.W - 1;
                if (ai.Y + ai.H - 1 > gy1) gy1 = ai.Y + ai.H - 1;
                ai.Index = m.Items.Count;
                m.Items.Add(ai);
            }
            if (m.Items.Count == 0) return null;

            int sx0 = gs.minX, sy0 = gs.minY, sx1 = gs.maxX, sy1 = gs.maxY;
            bool shapeUsed;
            int ux0, uy0, ux1, uy1;
            if (!PlanRect(gx0, gy0, gx1, gy1, sx0, sy0, sx1, sy1,
                          out ux0, out uy0, out ux1, out uy1, out shapeUsed))
                return null;
            gx0 = ux0; gy0 = uy0; gx1 = ux1; gy1 = uy1;
            if (shapeUsed) { m.ShapeW = sx1 - sx0 + 1; m.ShapeH = sy1 - sy0 + 1; }

            m.ItemX0 = gx0; m.ItemY0 = gy0;
            m.Ox = gx0;
            m.Oy = gy0;
            m.W = gx1 - gx0 + 1;
            m.H = gy1 - gy0 + 1;
            if (m.W <= 0 || m.H <= 0 || m.W > 64 || m.H > 64) return null;

            // Holes are honoured only when the shape actually carries a mask. A shape with
            // no filled cell at all is meaningless for planning, so the rectangle is used.
            m.Blocked = new bool[m.W * m.H];
            int filled = 0;
            for (int y = gs.minY; y <= gs.maxY; y++)
                for (int x = gs.minX; x <= gs.maxX; x++)
                    if (gs.Get(x, y) != 0) filled++;

            if (filled > 0)
            {
                for (int y = 0; y < m.H; y++)
                {
                    for (int x = 0; x < m.W; x++)
                    {
                        int gx = gx0 + x, gy = gy0 + y;
                        bool insideShape = gx >= gs.minX && gx <= gs.maxX && gy >= gs.minY && gy <= gs.maxY;
                        if (insideShape && gs.Get(gx, gy) == 0) m.Blocked[m.Cell(x, y)] = true;
                    }
                }
                // Safety net: a mask that blocks cells which are demonstrably occupied is
                // wrong, so drop it rather than plan into a dead end.
                for (int i = 0; i < m.Items.Count; i++)
                {
                    AreaItem it = m.Items[i];
                    for (int dy = 0; dy < it.H; dy++)
                        for (int dx = 0; dx < it.W; dx++)
                        {
                            if (!it.Mask[dy * it.W + dx]) continue;
                            int cx = it.X - gx0 + dx, cy = it.Y - gy0 + dy;
                            if (cx >= 0 && cy >= 0 && cx < m.W && cy < m.H)
                                m.Blocked[m.Cell(cx, cy)] = false;
                        }
                }
            }

            // to grid-local coordinates
            for (int i = 0; i < m.Items.Count; i++)
            {
                m.Items[i].X -= gx0;
                m.Items[i].Y -= gy0;
            }

            m.Commercial = false;
            try { m.Commercial = NetworkUpgrade.IsCommercialPowerUnlocked(); }
            catch (Exception) { }
            m.ChargePerArray = m.Commercial ? 4 : 2;
            if (ModMain.Cfg != null && ModMain.Cfg.ChargeOverride.Value > 0)
                m.ChargePerArray = ModMain.Cfg.ChargeOverride.Value;

            return m;
        }

        private static AreaItem Read(GameItem it, bool allowRotation)
        {
            GridShape sh = null;
            try { sh = it.modifiedShape; }
            catch (Exception) { }
            if (sh == null)
            {
                try { sh = it.shape; }
                catch (Exception) { }
            }
            if (sh == null) return null;

            var ai = new AreaItem();
            ai.Item = it;
            ai.Ori = sh.orientation;
            ai.Mask = Shapes.MaskOf(sh, out ai.W, out ai.H);
            if (ai.Mask == null) return null;
            ai.X = sh.minX;
            ai.Y = sh.minY;

            try
            {
                ai.IsSource = IsPowerArray(it);
                // "Rechargeable" is the right test for "this is a machine". Two corrections
                // matter here:
                //   - an array also carries the tag, so it must be excluded explicitly;
                //   - a charging device without a machinery_drawn_power tag (bottle_printer)
                //     is still a machine. It simply does not run at night, so Demand stays 0
                //     and it is reported as idle instead of being thrown out of the model.
                // Storage containers (smuggler_bay) never carry the tag at all - they only
                // have STORAGE / CONTAINER_TAG - so they are excluded for free.
                ai.IsMachine = !ai.IsSource && it.IsTag(MachineHelper.MACHINE_RECHARGABLE_TAG);
                ai.IsBattery = it.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG);
            }
            catch (Exception)
            {
                ai.IsSource = false; ai.IsMachine = false; ai.IsBattery = false;
            }

            string sprite = it.spritePath;
            ai.Key = string.IsNullOrEmpty(sprite) ? "?" : sprite;
            try { ai.Identifier = it.identifier; }
            catch (Exception) { ai.Identifier = ai.Key; }
            if (string.IsNullOrEmpty(ai.Identifier)) ai.Identifier = ai.Key;

            if (ai.IsMachine)
            {
                ai.Demand = ReadIntTag(it, "machinery_drawn_power");
                ai.BaseDemand = ReadIntTag(it, "MACHINERY_DRAWN_POWER_BASE");
                try
                {
                    GameSlotInventory slot = MachineHelper.GetBatterySlot(it);
                    if (slot != null && slot.childItem != null) ReadBattery(slot.childItem, ai);
                }
                catch (Exception) { }
            }
            else if (ai.IsBattery)
            {
                ReadBattery(it, ai);
            }

            ai.Orientations.Add(new Orientation { Ori = ai.Ori, W = ai.W, H = ai.H, Mask = ai.Mask });
            if (allowRotation) AddRotated(it, ai);
            return ai;
        }

        private static void ReadBattery(GameItem battery, AreaItem ai)
        {
            ai.Energy = ReadIntTag(battery, PowerHelper.POWER_SOURCE_ITEM_ENERGY);
            ai.MaxEnergy = ReadIntTag(battery, PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY);
        }

        public static int ReadIntTag(GameItem it, string tag)
        {
            try
            {
                if (!it.IsTag(tag)) return 0;
                return it.GetTagReadonly(tag).GetInt();
            }
            catch (Exception) { return 0; }
        }

        private static void AddRotated(GameItem it, AreaItem ai)
        {
            if (!Shapes.IsFullRect(ai.Mask, ai.W, ai.H)) return;
            if (ai.W == ai.H) return;
            int alt = (ai.Ori + 1) % 4;
            GridShape rotated;
            try { rotated = Shapes.Build(it, alt, 0, 0); }
            catch (Exception) { return; }
            if (rotated == null) return;
            int w, h;
            bool[] mask = Shapes.MaskOf(rotated, out w, out h);
            if (mask == null) return;
            if (w == ai.W && h == ai.H) return;              // SetOrientation had no effect
            if (!Shapes.IsFullRect(mask, w, h)) return;
            ai.Orientations.Add(new Orientation { Ori = alt, W = w, H = h, Mask = mask });
        }
    }
}
