using System;
using System.Collections.Generic;
using System.Text;

namespace MachinePowerLayout
{
    /// <summary>ASCII-only reporting (the F8 console font has no CJK glyphs).</summary>
    public static class Report
    {
        public static string Map(GridModel m, Plan plan)
        {
            var cells = new char[m.W * m.H];
            for (int i = 0; i < cells.Length; i++) cells[i] = m.Blocked[i] ? '#' : '.';

            // plan overrides for moved items
            var over = new Dictionary<int, Placement>();
            var moved = new HashSet<int>();
            if (plan != null)
            {
                for (int i = 0; i < plan.Moves.Count; i++)
                {
                    Placement p = plan.Moves[i];
                    over[p.Item.Index] = p;
                    moved.Add(p.Item.Index);
                }
            }

            for (int i = 0; i < m.Items.Count; i++)
            {
                AreaItem it = m.Items[i];
                char c = 'o';
                if (it.IsSource) c = 'A';
                else if (it.IsMachine) c = 'M';
                else if (it.IsBattery) c = 'B';

                int x0 = it.X, y0 = it.Y;
                bool[] mask = it.Mask;
                int w = it.W, h = it.H;
                Placement p;
                if (over.TryGetValue(i, out p))
                {
                    x0 = p.X; y0 = p.Y; mask = p.Ori.Mask; w = p.Ori.W; h = p.Ori.H;
                }
                for (int dy = 0; dy < h; dy++)
                    for (int dx = 0; dx < w; dx++)
                    {
                        if (!mask[dy * w + dx]) continue;
                        int cx = x0 + dx, cy = y0 + dy;
                        if (cx < 0 || cy < 0 || cx >= m.W || cy >= m.H) continue;
                        cells[cy * m.W + cx] = c;
                    }
            }

            var sb = new StringBuilder();
            sb.Append("map ").Append(m.W).Append('x').Append(m.H)
              .Append(" (A=array M=machine B=battery o=other .=free #=blocked)\n");
            for (int y = 0; y < m.H; y++)
            {
                sb.Append("  ");
                sb.Append(y.ToString("00"));
                sb.Append(' ');
                for (int x = 0; x < m.W; x++) sb.Append(cells[y * m.W + x]);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Per-machine supply/demand table plus the battery runway.</summary>
        public static string Machines(GridModel m, int chargePerArray)
        {
            var sb = new StringBuilder();
            sb.Append("per-machine night balance (each adjacent array restores ")
              .Append(chargePerArray).Append("):\n");
            for (int i = 0; i < m.Items.Count; i++)
            {
                AreaItem it = m.Items[i];
                if (!it.IsMachine) continue;
                int arrays = 0;
                for (int j = 0; j < m.Items.Count; j++)
                {
                    AreaItem o = m.Items[j];
                    if (!o.IsSource) continue;
                    if (Adjacent(it, o)) arrays++;
                }
                int supply = arrays * chargePerArray;
                int net = supply - it.Demand;
                int need = it.Demand <= 0 ? 0 : (it.Demand + chargePerArray - 1) / chargePerArray;
                sb.Append("  ").Append(Pad(it.Key, 22))
                  .Append(Pad(it.Identifier, 18))
                  .Append(Pad(it.W + "x" + it.H, 6))
                  .Append(" draw=").Append(Pad(it.Demand.ToString(), 3))
                  .Append(it.BaseDemand > 0 && it.BaseDemand != it.Demand ? "(base " + it.BaseDemand + ")" : "")
                  .Append(" arrays=").Append(Pad(arrays.ToString(), 2))
                  .Append(" need=").Append(Pad(need.ToString(), 2))
                  .Append(" supply=").Append(Pad(supply.ToString(), 3))
                  .Append(" net=").Append(Pad((net >= 0 ? "+" : "") + net, 4))
                  .Append(it.Demand <= 0 ? " IDLE" : (net < 0 ? " SHORT " + (-net) : " OK"));
                if (it.MaxEnergy > 0)
                {
                    sb.Append(" battery=").Append(it.Energy).Append('/').Append(it.MaxEnergy);
                    if (net < 0)
                    {
                        int nights = it.MaxEnergy / Math.Max(1, -net);
                        sb.Append(" runway=").Append(nights).Append("n");
                    }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Item-by-item move list: where it was, where it goes.</summary>
        public static string Moves(Plan plan)
        {
            if (plan == null || plan.Moves.Count == 0) return "  (none)\n";
            var sb = new StringBuilder();
            for (int i = 0; i < plan.Moves.Count; i++)
            {
                Placement p = plan.Moves[i];
                sb.Append("  ").Append(p.Item.Key)
                  .Append(" (").Append(p.Item.X).Append(',').Append(p.Item.Y).Append(')')
                  .Append(" -> (").Append(p.X).Append(',').Append(p.Y).Append(')');
                if (p.Ori != null && p.Ori.Ori != p.Item.Ori)
                    sb.Append(" ori ").Append(p.Item.Ori).Append("->").Append(p.Ori.Ori);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Panel totals: demand vs supply across the whole grid.</summary>
        public static string Totals(GridModel m, int chargePerArray)
        {
            int draw = 0, supply = 0, arrays = 0, machines = 0, idle = 0;
            for (int i = 0; i < m.Items.Count; i++)
            {
                AreaItem it = m.Items[i];
                if (it.IsSource) { arrays++; continue; }
                if (!it.IsMachine) continue;
                if (it.Demand <= 0) { idle++; continue; }
                machines++;
                draw += it.Demand;
                int adj = 0;
                for (int j = 0; j < m.Items.Count; j++)
                {
                    AreaItem o = m.Items[j];
                    if (o.IsSource && Adjacent(it, o)) adj++;
                }
                supply += adj * chargePerArray;
            }
            int net = supply - draw;
            return "totals machines=" + machines + (idle > 0 ? "(+" + idle + " idle)" : "")
                   + " arrays=" + arrays
                   + " free=" + m.FreeCells
                   + " draw=" + draw + "/n"
                   + " supply=" + supply + "/n"
                   + " net=" + (net >= 0 ? "+" : "") + net + "/n";
        }

        /// <summary>Everything the grid holds, with the tags that decide how it is treated.</summary>
        public static string Inventory(GridModel m)
        {
            var sb = new StringBuilder();
            sb.Append("contents (").Append(m.Items.Count).Append(" item(s)):\n");
            for (int i = 0; i < m.Items.Count; i++)
            {
                AreaItem it = m.Items[i];
                string kind = it.IsSource ? "array" : (it.IsMachine ? "machine" : (it.IsBattery ? "battery" : "other"));
                sb.Append("  ").Append(Pad(it.Key, 22))
                  .Append(Pad(it.Identifier, 18))
                  .Append(Pad(it.W + "x" + it.H, 6))
                  .Append(" at(").Append(it.X).Append(',').Append(it.Y).Append(") ")
                  .Append(Pad(kind, 8));
                if (it.IsMachine) sb.Append(" draw=").Append(it.Demand);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Summary(Metrics r, int machines)
        {
            return "covered " + r.Covered + "/" + machines
                   + " | unpowered " + r.Uncovered
                   + " | deficit " + r.Deficit + "/night"
                   + " | contacts " + r.Contacts
                   + (r.Powerless > 0 ? " | idle " + r.Powerless : "")
                   + " | battery+" + r.BatteryGain;
        }

        /// <summary>Writes the plan's positions back into the model, so reports can read them.</summary>
        public static void ApplyPlanToModel(GridModel m, Plan plan)
        {
            if (plan == null) return;
            for (int i = 0; i < plan.Moves.Count; i++)
            {
                Placement p = plan.Moves[i];
                AreaItem it = p.Item;
                it.X = p.X;
                it.Y = p.Y;
                it.Ori = p.Ori.Ori;
                it.W = p.Ori.W;
                it.H = p.Ori.H;
                it.Mask = p.Ori.Mask;
            }
        }

        /// <summary>Independent score of a model's current positions (no solver state involved).</summary>
        public static Metrics EvaluateModel(GridModel m, int chargePerArray)
        {
            var r = new Metrics();
            for (int i = 0; i < m.Items.Count; i++)
            {
                AreaItem it = m.Items[i];
                if (!it.Target) continue;
                int arrays = 0;
                for (int j = 0; j < m.Items.Count; j++)
                {
                    AreaItem o = m.Items[j];
                    if (!o.IsSource) continue;
                    if (Adjacent(it, o)) arrays++;
                }
                int supply = arrays * chargePerArray;
                if (it.IsMachine)
                {
                    if (it.Demand <= 0)
                    {
                        // idle machines must not score contacts either: an array parked next to
                        // one is dead weight, and counting it made the cross-panel planner ship
                        // arrays into an empty panel (net=+1.2) instead of leaving them alone.
                        r.Powerless++;
                        continue;
                    }
                    r.Contacts += arrays;
                    r.Machines++;
                    if (supply >= it.Demand) r.Covered++;
                    else
                    {
                        r.Uncovered++;
                        int d = it.Demand - supply;
                        r.Deficit += d;
                        if (d > r.MaxDeficit) r.MaxDeficit = d;
                    }
                    if (supply > it.Demand) r.Wasted += supply - it.Demand;
                }
                else
                {
                    int room = it.MaxEnergy - it.Energy;
                    if (room > 0) r.BatteryGain += supply < room ? supply : room;
                }
            }
            return r;
        }

        private static bool Adjacent(AreaItem a, AreaItem b)
        {
            // edge-touching only, same rule the game uses (GameInventory.IsAdjacent)
            for (int dy = 0; dy < a.H; dy++)
                for (int dx = 0; dx < a.W; dx++)
                {
                    if (!a.Mask[dy * a.W + dx]) continue;
                    int cx = a.X + dx, cy = a.Y + dy;
                    if (Occupies(b, cx + 1, cy)) return true;
                    if (Occupies(b, cx - 1, cy)) return true;
                    if (Occupies(b, cx, cy + 1)) return true;
                    if (Occupies(b, cx, cy - 1)) return true;
                }
            return false;
        }

        private static bool Occupies(AreaItem it, int x, int y)
        {
            int dx = x - it.X, dy = y - it.Y;
            if (dx < 0 || dy < 0 || dx >= it.W || dy >= it.H) return false;
            return it.Mask[dy * it.W + dx];
        }

        private static string Pad(string s, int n)
        {
            if (s == null) s = "";
            if (s.Length >= n) return s;
            return s + new string(' ', n - s.Length);
        }
    }
}
