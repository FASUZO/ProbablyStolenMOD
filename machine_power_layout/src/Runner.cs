using System;
using UnityEngine;

namespace MachinePowerLayout
{
    /// <summary>
    /// Runtime driver: hotkey trigger, optional pre-sleep run, screen toast.
    /// Nothing here touches save data, so no persistence is required.
    /// </summary>
    public class Runner : MonoBehaviour
    {
        private bool hooked;
        private bool busy;
        private float rearmTimer;
        private string toast = "";
        private float toastUntil;
        private GUIStyle toastStyle;

        private string cachedKeyName = "";
        private KeyCode cachedKey = KeyCode.None;

        private void Awake() { Hook(); }
        private void OnEnable() { Hook(); }
        private void OnDestroy() { Unhook(); }

        private void Hook()
        {
            if (hooked) return;
            try
            {
                ModHook.OnGoingSleepEarly += OnSleepEarly;
                ModHook.OnCreateTooltipLate += OnTooltipLate;
                hooked = true;
            }
            catch (Exception e) { ModMain.Log.Warning("hook failed: " + e.GetType().Name); }
        }

        private void Unhook()
        {
            if (!hooked) return;
            try { ModHook.OnGoingSleepEarly -= OnSleepEarly; }
            catch (Exception) { }
            try { ModHook.OnCreateTooltipLate -= OnTooltipLate; }
            catch (Exception) { }
            hooked = false;
        }

        private void OnTooltipLate(RichTextBuilder builder, GameItem item)
        {
            try { TooltipCache.Append(builder, item); }
            catch (Exception) { }
        }

        private void OnSleepEarly()
        {
            if (ModMain.Cfg == null) return;
            if (!ModMain.Cfg.AutoBeforeSleep.Value) return;
            Run(true);
        }

        private void Update()
        {
            if (ModMain.Cfg == null || busy) return;

            // ModHook's static events can be reset when the scene reloads (ship travel,
            // returning from the store). Re-arm idempotently every few seconds.
            rearmTimer += Time.unscaledDeltaTime;
            if (rearmTimer > 5f)
            {
                rearmTimer = 0f;
                Hook();
                try
                {
                    ModHook.OnGoingSleepEarly -= OnSleepEarly;
                    ModHook.OnGoingSleepEarly += OnSleepEarly;
                    ModHook.OnCreateTooltipLate -= OnTooltipLate;
                    ModHook.OnCreateTooltipLate += OnTooltipLate;
                }
                catch (Exception) { }
            }

            KeyCode key = ParseKey(ModMain.Cfg.Hotkey.Value);
            if (key != KeyCode.None && Input.GetKeyDown(key)) Run(false);
        }

        private KeyCode ParseKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return KeyCode.None;
            if (name == cachedKeyName) return cachedKey;
            KeyCode k = KeyCode.None;
            try { k = (KeyCode)Enum.Parse(typeof(KeyCode), name, true); }
            catch (Exception) { k = KeyCode.None; }
            cachedKeyName = name;
            cachedKey = k;
            return k;
        }

        // ------------------------------------------------------------------

        private void Run(bool auto)
        {
            if (busy) return;
            if (EmporiumEntry.Instance == null)
            {
                if (!auto) Toast("PowerLayout: open the store first");
                return;
            }

            busy = true;
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                bool verbose = ModMain.Cfg.Verbose.Value;
                bool openOnly = ModMain.Cfg.OpenWindowsOnly.Value;
                System.Collections.Generic.List<GameGridInventory> all = GridModel.MachineAreas();
                System.Collections.Generic.List<GameGridInventory> areas = GridModel.MachineAreas(openOnly);
                if (verbose) ModMain.Log.Log("[scan]" + GridModel.LastScan);
                if (areas.Count == 0)
                {
                    ModMain.Log.Warning("no machine area found. scanned:" + GridModel.LastScan);
                    if (!auto) Toast("PowerLayout: machine area not found");
                    return;
                }

                int openCount = 0;
                for (int i = 0; i < all.Count; i++) if (GridModel.IsWindowOpen(all[i])) openCount++;
                bool allowMachineTransfer = ModMain.Cfg.FullMode && ModMain.Cfg.AllowMachineTransfer.Value;

                // Snapshot everything BEFORE phase 1, so the final line can show a real delta.
                var scanNote = new System.Text.StringBuilder();
                int beforeCovered = 0, beforeMachines = 0, beforeArrays = 0;
                for (int i = 0; i < areas.Count; i++)
                {
                    GridModel mb = GridModel.Build(areas[i], ModMain.Cfg.AllowRotation.Value);
                    if (mb == null) { scanNote.Append(" P").Append(i + 1).Append("(unreadable)"); continue; }
                    Metrics mbm = Report.EvaluateModel(mb, mb.ChargePerArray);
                    beforeCovered += mbm.Covered;
                    beforeMachines += mbm.Machines;
                    beforeArrays += mb.ArrayCount;
                    scanNote.Append(" P").Append(i + 1)
                            .Append("(m=").Append(mb.MachineCount)
                            .Append(",a=").Append(mb.ArrayCount)
                            .Append(",free=").Append(mb.FreeCells)
                            .Append(',')
                            .Append(GridModel.IsWindowOpen(areas[i]) ? "open" : "closed")
                            .Append(')');
                }

                ModMain.Log.Log("[areas] found=" + all.Count + " open=" + openCount
                                + " using=" + areas.Count + " open_only=" + openOnly
                                + (openOnly && openCount == 0 ? " (nothing reported open -> all)" : "")
                                + scanNote);
                ModMain.Log.Log("[start] covered " + beforeCovered + "/" + beforeMachines
                                + " arrays=" + beforeArrays
                                + " mode=" + (ModMain.Cfg.FullMode ? "FullRelayout" : "ArraysOnly")
                                + " budget=" + ModMain.Cfg.BudgetMs.Value + "ms/panel"
                                + (allowMachineTransfer ? " machine_transfer=on" : " machine_transfer=off"));

                int perPanel = Math.Max(120, ModMain.Cfg.BudgetMs.Value / areas.Count);
                bool dryRun = !auto && ModMain.Cfg.DryRun.Value;
                bool suggestFull = false;
                int transferred = 0;

                // FullRelayout solved jointly: one pool of machines and arrays across every open
                // panel, exactly like the reference algorithm's "items may be assigned to any
                // board". Falls back to the per-panel pass when it cannot run (dry run, or a
                // panel with nothing movable).
                bool joint = false;
                if (ModMain.Cfg.FullMode && !dryRun && areas.Count >= 1)
                {
                    joint = RunGlobal(areas, verbose, out transferred);
                    if (joint && transferred == 0)
                    {
                        // The joint pass may still have rearranged items inside each panel; the
                        // per-panel pass would only undo that work, so it is skipped either way.
                    }
                }

                if (!joint)
                {
                    for (int i = 0; i < areas.Count; i++)
                    {
                        PanelResult r = OptimisePanel(areas[i], i + 1, areas.Count, perPanel, auto, verbose);
                        if (r.SuggestFull) suggestFull = true;
                    }

                    // Phase 2: hand spare arrays from one panel to another. Adjacency does not
                    // cross inventories, so this is the only way surplus can reach a starved panel.
                    if (areas.Count > 1 && !dryRun)
                        transferred = Rebalance(areas, verbose, allowMachineTransfer);
                }

                // Final numbers come from a fresh read of every panel, not from the solver.
                int totalMachines = 0;
                int totalCovered = 0;
                int totalArrays = 0;
                int totalDeficit = 0;
                int unreachable = 0;
                for (int i = 0; i < areas.Count; i++)
                {
                    GridModel m = GridModel.Build(areas[i], ModMain.Cfg.AllowRotation.Value);
                    if (m == null) { unreachable++; continue; }
                    Metrics mt = Report.EvaluateModel(m, m.ChargePerArray);
                    totalMachines += mt.Machines;
                    totalCovered += mt.Covered;
                    totalArrays += m.ArrayCount;
                    totalDeficit += mt.Deficit;
                    if (verbose) ModMain.Log.Log("[panel " + (i + 1) + "/" + areas.Count + "] "
                                                 + Report.Totals(m, m.ChargePerArray));
                    if (mt.Uncovered > 0 && !ModMain.Cfg.FullMode) suggestFull = true;
                }

                ModMain.Log.Log("[done] panels=" + areas.Count
                                + (unreachable > 0 ? "(" + unreachable + " unreadable)" : "")
                                + " covered " + beforeCovered + "/" + beforeMachines
                                + " -> " + totalCovered + "/" + totalMachines
                                + " deficit=" + totalDeficit + "/n"
                                + " arrays=" + beforeArrays + "->" + totalArrays
                                + " transfers=" + transferred
                                + " in " + swAll.ElapsedMilliseconds + "ms");
                if (totalCovered < totalMachines)
                    ModMain.Log.Warning("[done] " + (totalMachines - totalCovered)
                                        + " machine(s) still short - see the per-panel detail above");
                if (!auto)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("PowerLayout  ").Append(totalCovered).Append('/').Append(totalMachines).Append(" powered");
                    if (beforeCovered != totalCovered) sb.Append(" (was ").Append(beforeCovered).Append(')');
                    sb.Append('\n');
                    sb.Append("areas=").Append(areas.Count)
                      .Append("  moved=").Append(transferred)
                      .Append("  arrays=").Append(totalArrays)
                      .Append("  ").Append(swAll.ElapsedMilliseconds).Append("ms");
                    sb.Append('\n');
                    if (totalDeficit > 0)
                        sb.Append("still short ").Append(totalDeficit).Append("/night")
                          .Append(suggestFull ? " - switch to FullRelayout" : "");
                    else
                        sb.Append("every power-drawing machine is supplied");
                    Toast(sb.ToString());
                }
            }
            catch (Exception e)
            {
                ModMain.Log.Error("[run] exception " + e.GetType().Name + ": " + e.Message);
                if (!auto) Toast("PowerLayout: error " + e.GetType().Name);
            }
            finally
            {
                busy = false;
            }
        }

        private static int CountArrays(System.Collections.Generic.List<GridModel> models)
        {
            int n = 0;
            for (int i = 0; i < models.Count; i++)
                for (int j = 0; j < models[i].Items.Count; j++)
                    if (models[i].Items[j].IsSource) n++;
            return n;
        }

        private class PanelResult
        {
            public int Machines;
            public int Covered;
            public bool SuggestFull;
        }

        /// <summary>
        /// Joint optimisation across every open panel. This mirrors the reference algorithm:
        /// all machines and arrays form one pool, items may be assigned to any panel, and the
        /// objective is lexicographic (uncovered count, then total deficit, then total score).
        /// </summary>
        private bool RunGlobal(System.Collections.Generic.List<GameGridInventory> areas, bool verbose, out int movedAcross)
        {
            movedAcross = 0;
            var models = new System.Collections.Generic.List<GridModel>();
            var labels = new System.Collections.Generic.List<string>();

            for (int i = 0; i < areas.Count; i++)
            {
                GridModel m = GridModel.Build(areas[i], ModMain.Cfg.AllowRotation.Value);
                if (m == null) continue;
                Metrics mt = Report.EvaluateModel(m, m.ChargePerArray);
                if (mt.Machines == 0)
                {
                    ModMain.Log.Log("[global] P" + (i + 1) + " skipped: no power-drawing machine"
                                    + (mt.Powerless > 0 ? " (idle=" + mt.Powerless + ")" : ""));
                    continue;
                }
                models.Add(m);
                labels.Add("P" + (i + 1));
                if (verbose) ModMain.Log.Log("[global] P" + (i + 1) + " " + Report.Summary(mt, mt.Machines)
                                             + " free=" + m.FreeCells);
            }

            if (models.Count == 0)
            {
                ModMain.Log.Log("[global] no panel holds a power-drawing machine");
                return false;
            }

            int charge = models[0].ChargePerArray;
            var planner = new GlobalPlanner(models, labels, charge, false, Environment.TickCount);
            if (planner.PoolSize == 0)
            {
                ModMain.Log.Log("[global] nothing movable in those panels");
                return true;
            }

            int budget = Math.Max(600, ModMain.Cfg.BudgetMs.Value * models.Count);
            ModMain.Log.Log("[global] boards=" + models.Count
                            + " pool=" + planner.PoolSize
                            + " (machines=" + planner.MachineCount + ", arrays=" + planner.ArrayCount + ")"
                            + " charge=" + charge + " budget=" + budget + "ms");

            GlobalPlan plan = planner.Solve(budget);

            ModMain.Log.Log("[global] before: " + GlobalLine(plan.NegBefore, plan.PenBefore, plan.ContactsBefore, plan.ScoreBefore));
            ModMain.Log.Log("[global] after : " + GlobalLine(plan.NegAfter, plan.PenAfter, plan.ContactsAfter, plan.ScoreAfter)
                            + " (" + plan.Note + ")");

            if (plan.Moves.Count == 0)
            {
                ModMain.Log.Log("[global] no improving move - the current layout is already the best found");
                return true;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < plan.Moves.Count; i++)
            {
                Remap r = plan.Moves[i];
                int fi = models.IndexOf(r.From);
                int ti = models.IndexOf(r.To);
                sb.Append("  ").Append(Pad(r.Item.Key, 20))
                  .Append(fi >= 0 ? labels[fi] : "?").Append('(').Append(r.OldX).Append(',').Append(r.OldY).Append(')')
                  .Append(" -> ")
                  .Append(ti >= 0 ? labels[ti] : "?").Append('(').Append(r.X).Append(',').Append(r.Y).Append(')');
                if (r.Crosses) sb.Append("  CROSS");
                sb.Append('\n');
            }
            ModMain.Log.Log("[global] moves " + plan.Moves.Count + " (across " + plan.MovesAcross + ")\n" + sb);

            string error;
            int skipped, mismatches;
            if (!Applier.ApplyGlobal(plan.Moves, out error, out skipped, out mismatches))
            {
                ModMain.Log.Error("[global] apply failed: " + (error == null ? "?" : error));
                return false;
            }
            if (mismatches > 0)
                ModMain.Log.Warning("[global] " + mismatches + " item(s) did not land on the requested cell");

            movedAcross = plan.MovesAcross;

            int cov = 0, tot = 0, con = 0;
            for (int i = 0; i < areas.Count; i++)
            {
                GridModel chk = GridModel.Build(areas[i], ModMain.Cfg.AllowRotation.Value);
                if (chk == null) continue;
                Metrics mt = Report.EvaluateModel(chk, chk.ChargePerArray);
                cov += mt.Covered;
                tot += mt.Machines;
                con += mt.Contacts;
                if (verbose)
                {
                    ModMain.Log.Log("[global verify] P" + (i + 1) + " " + Report.Summary(mt, mt.Machines));
                    ModMain.Log.Log(Report.Map(chk, null));
                }
            }
            ModMain.Log.Log("[global] verified: covered " + cov + "/" + tot + " contacts=" + con);
            return true;
        }

        private static string GlobalLine(int neg, double pen, int contacts, double score)
        {
            return "uncovered " + neg + " | deficit " + pen + " | contacts " + contacts
                   + " | score " + score.ToString("F0");
        }

        private static string Pad(string s, int n)
        {
            if (s == null) s = "";
            return s.Length >= n ? s : s + new string(' ', n - s.Length);
        }

        /// <summary>
        /// Second pass over all panels together. One transfer at a time: plan the best single
        /// move, apply it to the game, then re-solve BOTH panels. Re-solving the receiver is
        /// what makes more than one transfer possible - a packed panel has no free cell that
        /// already touches a machine, but it can make room once the solver is allowed to
        /// rearrange it around the newcomer.
        /// </summary>
        private int Rebalance(System.Collections.Generic.List<GameGridInventory> areas, bool verbose, bool allowMachines)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rounds = new System.Collections.Generic.List<string>();
            int done = 0;
            const int maxRounds = 6;

            for (int round = 1; round <= maxRounds; round++)
            {
                // Always rebuild from the live game: the panels were rearranged since the
                // last round, so any cached model would be lying.
                var models = new System.Collections.Generic.List<GridModel>();
                var labels = new System.Collections.Generic.List<string>();
                for (int i = 0; i < areas.Count; i++)
                {
                    GridModel m = GridModel.Build(areas[i], ModMain.Cfg.AllowRotation.Value);
                    if (m == null) continue;
                    // Only panels that actually contain a power-drawing machine take part in
                    // cross-panel rebalancing. A panel holding just a charging device that never
                    // runs at night (or an idle shelf) must not become a destination.
                    if (Report.EvaluateModel(m, m.ChargePerArray).Machines == 0) continue;
                    models.Add(m);
                    labels.Add("P" + (i + 1) + (GridModel.IsWindowOpen(areas[i]) ? "*" : ""));
                }
                if (models.Count < 2)
                {
                    ModMain.Log.Log("[xfer] stopped: only " + models.Count + " panel(s) hold machines");
                    break;
                }

                // A machine on its own is useless in a panel without spare arrays, and spare
                // arrays are useless in a panel without machines. Try the machine + its arrays
                // as one bundle first - that is the only way a crowded panel can shed load
                // into an empty one.
                if (allowMachines)
                {
                    Bundle bundle = TransferPlanner.NextBestBundle(models, 300);
                    if (bundle != null && bundle.Net > 1e-6)
                    {
                        int bFrom = models.IndexOf(bundle.Machine.From);
                        int bTo = models.IndexOf(bundle.Machine.To);
                        string bFromTag = bFrom >= 0 ? labels[bFrom] : "?";
                        string bToTag = bTo >= 0 ? labels[bTo] : "?";

                        ModMain.Log.Log("[bundle r" + round + "] " + bFromTag + " -> " + bToTag + " "
                                        + bundle.Machine.Item.Key + " (" + bundle.Machine.X + "," + bundle.Machine.Y + ")"
                                        + " + " + bundle.Extras.Count + " array(s) net=" + bundle.Net.ToString("F1")
                                        + " | " + TransferPlanner.LastBundleReason);

                        string be;
                        if (!ExecuteBundle(bundle, out be))
                        {
                            ModMain.Log.Warning("[bundle r" + round + "] failed: " + (be == null ? "?" : be)
                                                + " - stopping");
                            break;
                        }

                        done += 1 + bundle.Extras.Count;
                        VerifyLanded(bundle.Machine, bFromTag, bToTag);

                        int bms = Math.Max(80, ModMain.Cfg.BudgetMs.Value / 3);
                        Refit(bundle.Machine.From.Inv, "[refit " + bFromTag + "]", bms, verbose);
                        Refit(bundle.Machine.To.Inv, "[refit " + bToTag + "]", bms, verbose);
                        continue;
                    }
                    ModMain.Log.Log("[bundle] " + TransferPlanner.LastBundleReason);
                }

                string why;
                Transfer t = TransferPlanner.NextBest(models, allowMachines, out why);
                if (t == null)
                {
                    ModMain.Log.Log("[xfer] round " + round + ": no move - " + why
                                    + " | panels=" + models.Count
                                    + " arrays=" + CountArrays(models));
                    break;
                }

                int from = models.IndexOf(t.From);
                int to = models.IndexOf(t.To);
                string fromTag = from >= 0 ? labels[from] : "?";
                string toTag = to >= 0 ? labels[to] : "?";

                Metrics dm = Report.EvaluateModel(t.From, t.From.ChargePerArray);
                Metrics rm = Report.EvaluateModel(t.To, t.To.ChargePerArray);

                string error;
                if (!Applier.CrossMove(t, out error))
                {
                    ModMain.Log.Warning("[xfer r" + round + "] " + fromTag + " -> " + toTag + " FAILED: "
                                        + (error == null ? "unknown" : error) + " - stopping");
                    break;
                }
                done++;

                ModMain.Log.Log("[xfer r" + round + "] " + fromTag + " -> " + toTag + " " + t.Item.Key
                                + " (" + t.X + "," + t.Y + ") ori=" + t.Ori.Ori
                                + " net=" + t.Net.ToString("F1")
                                + " | donor was " + dm.Covered + "/" + dm.Machines + " contacts=" + dm.Contacts
                                + " | receiver was " + rm.Covered + "/" + rm.Machines
                                + " deficit=" + rm.Deficit + " contacts=" + rm.Contacts
                                + " | " + why);

                VerifyLanded(t, fromTag, toTag);

                // Give both panels a chance to adapt to the new item counts.
                int refit = Math.Max(80, ModMain.Cfg.BudgetMs.Value / 3);
                Refit(t.From.Inv, "[refit " + fromTag + "]", refit, verbose);
                Refit(t.To.Inv, "[refit " + toTag + "]", refit, verbose);

                rounds.Add(fromTag + "->" + toTag);
            }

            if (done > 0)
            {
                ModMain.Log.Log("[xfer] done transfers=" + done + " rounds=" + done
                                + " in " + sw.ElapsedMilliseconds + "ms");
                if (verbose)
                {
                    for (int i = 0; i < areas.Count; i++)
                    {
                        GridModel m = GridModel.Build(areas[i], ModMain.Cfg.AllowRotation.Value);
                        if (m == null) continue;
                        Metrics mt = Report.EvaluateModel(m, m.ChargePerArray);
                        ModMain.Log.Log("[xfer final] P" + (i + 1) + " " + Report.Summary(mt, mt.Machines)
                                        + " arrays=" + m.ArrayCount + " free=" + m.FreeCells);
                    }
                }
            }
            else if (areas.Count > 1)
            {
                ModMain.Log.Log("[xfer] nothing crossed panels this run");
            }
            return done;
        }

        /// <summary>
        /// Moves a bundle: the machine first, then its arrays. If any array is refused the
        /// whole bundle is unwound, so a panel never ends up with a machine it cannot power.
        /// </summary>
        private bool ExecuteBundle(Bundle b, out string error)
        {
            error = null;
            var doneList = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Transfer, GridShape>>();

            GridShape keep = null;
            try { keep = b.Machine.Item.Item.modifiedShape; }
            catch (Exception) { }
            if (!Applier.CrossMove(b.Machine, out error)) return false;
            doneList.Add(new System.Collections.Generic.KeyValuePair<Transfer, GridShape>(b.Machine, keep));

            for (int i = 0; i < b.Extras.Count; i++)
            {
                GridShape s = null;
                try { s = b.Extras[i].Item.Item.modifiedShape; }
                catch (Exception) { }

                if (Applier.CrossMove(b.Extras[i], out error))
                {
                    doneList.Add(new System.Collections.Generic.KeyValuePair<Transfer, GridShape>(b.Extras[i], s));
                    continue;
                }

                string cause = error;
                for (int k = doneList.Count - 1; k >= 0; k--)
                    Applier.MoveBack(doneList[k].Key, doneList[k].Value);
                error = "array " + (i + 1) + "/" + b.Extras.Count + " refused (" + cause
                        + "); " + doneList.Count + " item(s) moved back";
                return false;
            }
            return true;
        }

        /// <summary>Re-read the receiving panel and confirm the item really sits on the target cell.</summary>
        private void VerifyLanded(Transfer t, string fromTag, string toTag)
        {
            GridModel check = GridModel.Build(t.To.Inv, ModMain.Cfg.AllowRotation.Value);
            if (check == null)
            {
                ModMain.Log.Warning("[xfer] " + fromTag + " -> " + toTag + " could not re-read the receiver");
                return;
            }
            for (int i = 0; i < check.Items.Count; i++)
            {
                AreaItem it = check.Items[i];
                if (it.Item != t.Item.Item) continue;
                bool ok = it.X == t.X && it.Y == t.Y;
                if (ok)
                    ModMain.Log.Log("[xfer] " + toTag + " holds " + it.Key + " at (" + it.X + "," + it.Y + ") - confirmed");
                else
                    ModMain.Log.Warning("[xfer] " + toTag + " reports " + it.Key + " at (" + it.X + "," + it.Y
                                        + ") but (" + t.X + "," + t.Y + ") was requested");
                return;
            }
            ModMain.Log.Warning("[xfer] " + toTag + " does not contain " + t.Item.Key + " after the move");
        }

        /// <summary>Re-solve one panel in place; used after a cross-panel transfer.</summary>
        private void Refit(GameGridInventory inv, string tag, int budgetMs, bool verbose)
        {
            GridModel m = GridModel.Build(inv, ModMain.Cfg.AllowRotation.Value);
            if (m == null || m.MachineCount == 0) return;
            int L = m.ChargePerArray;
            Metrics before = Report.EvaluateModel(m, L);

            var planner = new Planner(m, ModMain.Cfg.FullMode);
            if (!planner.HasWork)
            {
                ModMain.Log.Log(tag + " nothing movable");
                return;
            }
            Plan plan = planner.Solve(budgetMs);
            if (!plan.Feasible)
            {
                ModMain.Log.Warning(tag + " no feasible arrangement (" + plan.Note + ")");
                return;
            }
            if (plan.Moves.Count == 0)
            {
                ModMain.Log.Log(tag + " already optimal " + Report.Summary(before, before.Machines));
                return;
            }

            string error;
            int mismatches, skipped;
            if (!Applier.Apply(m, plan, out error, out mismatches, out skipped))
            {
                ModMain.Log.Warning(tag + " apply failed: " + (error == null ? "?" : error));
                return;
            }

            GridModel chk = GridModel.Build(inv, ModMain.Cfg.AllowRotation.Value);
            Metrics after = chk != null ? Report.EvaluateModel(chk, chk.ChargePerArray) : plan.After;
            ModMain.Log.Log(tag + " moved " + plan.Moves.Count
                            + (skipped > 0 ? " (" + skipped + " refused)" : "")
                            + " | " + Report.Summary(before, before.Machines)
                            + " -> " + Report.Summary(after, after.Machines));
            if (verbose && chk != null) ModMain.Log.Log(tag + " layout\n" + Report.Map(chk, null));
        }

        /// <summary>One machine-area grid: measure, solve, apply, then verify independently.</summary>
        private PanelResult OptimisePanel(GameGridInventory inv, int index, int total, int budgetMs, bool auto, bool verbose)
        {
            var result = new PanelResult();
            string tag = "[panel " + index + "/" + total + "] ";

            GridModel model = GridModel.Build(inv, ModMain.Cfg.AllowRotation.Value);
            if (model == null)
            {
                ModMain.Log.Log(tag + "skipped: grid could not be read");
                return result;
            }

            int L = model.ChargePerArray;
            bool full = ModMain.Cfg.FullMode;
            bool dry = !auto && ModMain.Cfg.DryRun.Value;
            result.Machines = model.MachineCount;
            ModMain.Log.Log(tag + "grid " + model.W + "x" + model.H
                            + " (panel " + (model.ShapeW > 0 ? model.ShapeW + "x" + model.ShapeH : "?")
                            + ", items from (" + model.ItemX0 + "," + model.ItemY0 + "))"
                            + " machines=" + model.MachineCount
                            + " arrays=" + model.ArrayCount
                            + " free=" + model.FreeCells
                            + " charge=" + L + (model.Commercial ? " (commercial)" : "")
                            + " mode=" + (full ? "FullRelayout" : "ArraysOnly")
                            + (GridModel.IsWindowOpen(inv) ? " open" : " closed"));
            // Always list the contents: this is how an item that "looks like a machine" but
            // draws nothing (smuggler_bay, backpacks) becomes visible in the log.
            if (verbose) ModMain.Log.Log(tag + Report.Inventory(model));

            Metrics before = Report.EvaluateModel(model, L);
            result.Covered = before.Covered;
            result.Machines = before.Machines;          // power-drawing machines only
            if (before.Machines == 0)
            {
                ModMain.Log.Log(tag + "skipped: no power-drawing machine here"
                                + (before.Powerless > 0 ? " (idle=" + before.Powerless + ")" : ""));
                return result;
            }
            if (verbose)
            {
                ModMain.Log.Log(tag + "layout\n" + Report.Map(model, null));
                ModMain.Log.Log(tag + "detail\n" + Report.Machines(model, L));
            }
            ModMain.Log.Log(tag + "[before] " + Report.Summary(before, before.Machines));

            var planner = new Planner(model, full);
            if (!planner.HasWork)
            {
                ModMain.Log.Log(tag + "no movable power array in this grid");
                return result;
            }

            Plan plan = planner.Solve(budgetMs);
            if (!plan.Feasible)
            {
                ModMain.Log.Warning(tag + "[plan] " + plan.Note + " - nothing changed");
                ModMain.Log.Warning(tag + "[diag]\n" + planner.Diagnostics());
                return result;
            }

            ModMain.Log.Log(tag + "[plan] " + plan.Note + " | " + Report.Summary(plan.After, plan.After.Machines)
                            + " | moves=" + plan.Moves.Count);
            if (verbose && plan.Note.IndexOf("rebuilt=False", StringComparison.Ordinal) >= 0)
                ModMain.Log.Log(tag + "[diag] grid too packed to rebuild, optimised in place");

            if (plan.Moves.Count == 0)
            {
                ModMain.Log.Log(tag + "no improving move found; layout left as it is");
                if (verbose) ModMain.Log.Log(tag + "[diag]\n" + planner.Diagnostics());
                result.Covered = plan.After.Covered;
                if (plan.After.Uncovered > 0 && !full) result.SuggestFull = true;
                return result;
            }

            if (dry)
            {
                Report.ApplyPlanToModel(model, plan);
                if (verbose) ModMain.Log.Log(tag + "planned layout\n" + Report.Map(model, null));
                Metrics dryRun = Report.EvaluateModel(model, L);
                ModMain.Log.Log(tag + "[dry-run] nothing moved; " + Report.Summary(dryRun, dryRun.Machines));
                result.Covered = dryRun.Covered;
                return result;
            }

            string error;
            int mismatches;
            int skipped;
            if (!Applier.Apply(model, plan, out error, out mismatches, out skipped))
            {
                ModMain.Log.Error(tag + "[apply] failed: " + (error == null ? "unknown" : error) + " - rolled back");
                return result;
            }
            if (skipped > 0)
                ModMain.Log.Warning(tag + "[apply] " + skipped + " of " + plan.Moves.Count
                                    + " items had no legal target cell and stayed where they were"
                                    + (error == null ? "" : " (" + error + ")"));
            if (mismatches > 0)
                ModMain.Log.Warning(tag + "[apply] " + mismatches + " of " + (plan.Moves.Count - skipped)
                                    + " moved items did not land on the requested cell");

            // Independent verification: rebuild from the live grid, do not trust solver state.
            GridModel check = GridModel.Build(inv, ModMain.Cfg.AllowRotation.Value);
            if (check == null)
            {
                ModMain.Log.Warning(tag + "[verify] could not re-read the grid");
                return result;
            }

            Metrics after = Report.EvaluateModel(check, L);
            result.Covered = after.Covered;
            ModMain.Log.Log(tag + "[apply] moved " + plan.Moves.Count + " items");
            if (verbose) ModMain.Log.Log(tag + "moves\n" + Report.Moves(plan));
            ModMain.Log.Log(tag + "[verify] " + Report.Summary(after, after.Machines));
            if (verbose)
            {
                ModMain.Log.Log(tag + "layout\n" + Report.Map(check, null));
                ModMain.Log.Log(tag + "detail\n" + Report.Machines(check, L));
            }
            if (after.Uncovered > 0 && !full) result.SuggestFull = true;
            return result;
        }

        // ------------------------------------------------------------------

        private void Toast(string msg)
        {
            if (ModMain.Cfg == null || !ModMain.Cfg.ShowToast.Value) return;
            toast = msg;
            toastUntil = Time.unscaledTime + 6f;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(toast)) return;
            if (Time.unscaledTime > toastUntil) { toast = ""; return; }

            if (toastStyle == null)
            {
                toastStyle = new GUIStyle(GUI.skin.label);
                toastStyle.fontSize = 15;
                toastStyle.alignment = TextAnchor.UpperLeft;
                toastStyle.wordWrap = true;
            }

            int lines = 1;
            for (int i = 0; i < toast.Length; i++) if (toast[i] == '\n') lines++;
            float height = 20f * lines + 16f;
            var box = new Rect(16f, 16f, 620f, height);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;

            toastStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(box.x + 12f, box.y + 8f, box.width - 24f, box.height - 12f), toast, toastStyle);
        }
    }
}
