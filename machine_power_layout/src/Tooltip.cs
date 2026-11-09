using System;
using System.Collections.Generic;
using Palette = RenderHandler.ColorPalette;

namespace MachinePowerLayout
{
    /// <summary>
    /// Live power read-out for the item tooltip. Everything is computed at hover time from the
    /// item's actual cell and its container, so moving an array updates the number immediately -
    /// there is no cached snapshot to go stale. Only the net figure is shown, by request.
    /// </summary>
    public static class TooltipCache
    {
        public static void Clear() { }
        public static void Capture(GridModel model, bool asAfter) { }

        /// <summary>Appends "净耗电 +N/天" to an item tooltip (Chinese text, game font).</summary>
        public static void Append(RichTextBuilder b, GameItem item)
        {
            if (b == null || item == null) return;

            int draw;
            try
            {
                if (!item.IsTag("machinery_drawn_power")) return;   // not a power-drawing machine
                draw = item.GetTagReadonly("machinery_drawn_power").GetInt();
            }
            catch (Exception) { return; }
            if (draw <= 0) return;                                  // idle device: nothing to show

            int arrays = CountAdjacentArrays(item);
            if (arrays < 0) return;

            int charge = 2;
            try { charge = NetworkUpgrade.IsCommercialPowerUnlocked() ? 4 : 2; }
            catch (Exception) { }

            int net = arrays * charge - draw;
            string text = "\u51c0\u8017\u7535 " + (net >= 0 ? "+" : "") + net + "/\u5929";
            if (net < 0) text += "\uff08\u7f3a\u7535\uff09";

            b.AddLine(text, true, net >= 0 ? Palette.LightGreen : Palette.LightRed,
                      true, false, false, false,
                      Palette.None, Palette.None, Palette.None);
        }

        /// <summary>Adjacent arrays right now, or -1 when the item is not in a grid.</summary>
        private static int CountAdjacentArrays(GameItem item)
        {
            GameInventory inv = null;
            try { inv = item.parentInventory; }
            catch (Exception) { }
            if (inv == null) return -1;

            List<GameItem> list = null;
            try { list = inv.childItems; }
            catch (Exception) { }
            if (list == null) return -1;

            GridShape mine = null;
            try { mine = item.modifiedShape; }
            catch (Exception) { }
            if (mine == null)
            {
                try { mine = item.shape; }
                catch (Exception) { }
            }
            if (mine == null) return -1;

            int w, h;
            bool[] mask = Shapes.MaskOf(mine, out w, out h);
            if (mask == null) return -1;
            int x0 = mine.minX, y0 = mine.minY;

            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                GameItem other = list[i];
                if (other == null || ReferenceEquals(other, item)) continue;
                if (!GridModel.IsPowerArray(other)) continue;

                GridShape os = null;
                try { os = other.modifiedShape; }
                catch (Exception) { }
                if (os == null) continue;
                if (Touches(mask, w, h, x0, y0, os)) n++;
            }
            return n;
        }

        /// <summary>Edge-sharing only, same rule the game uses (diagonals do not count).</summary>
        private static bool Touches(bool[] mask, int w, int h, int x0, int y0, GridShape other)
        {
            int ow, oh;
            bool[] om = Shapes.MaskOf(other, out ow, out oh);
            if (om == null) return false;
            int ox = other.minX, oy = other.minY;

            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    if (!mask[dy * w + dx]) continue;
                    int cx = x0 + dx, cy = y0 + dy;
                    if (At(om, ow, oh, ox, oy, cx + 1, cy)) return true;
                    if (At(om, ow, oh, ox, oy, cx - 1, cy)) return true;
                    if (At(om, ow, oh, ox, oy, cx, cy + 1)) return true;
                    if (At(om, ow, oh, ox, oy, cx, cy - 1)) return true;
                }
            return false;
        }

        private static bool At(bool[] m, int w, int h, int x0, int y0, int x, int y)
        {
            int dx = x - x0, dy = y - y0;
            if (dx < 0 || dy < 0 || dx >= w || dy >= h) return false;
            return m[dy * w + dx];
        }
    }
}
