using System;
using System.Collections.Generic;

namespace ModuleSoloUpgrade
{
    // -------------------------------------------------------------------------
    //  The device's behaviour. Both actions are driven by the game's own
    //  self-learning-algorithm helpers, so the growth curve, the caps and the
    //  tooltip text stay exactly vanilla:
    //
    //    ModuleEffectHelper.AddEffectToModule(module, MODULE_EFFECT_LEARNING_ALGORITHM)
    //        -> writes MODULE_EFFECT1/2_TAG, respecting slot usage and the
    //           effect's incompatibleWithEffects list.
    //    ModuleEffectHelper.InitLearningAlgo(module)
    //        -> converts the module's current BONUS_PERCENTAGE_* into
    //           LEARNING_ALGO_MAX_*, then zeroes the current value, i.e. the
    //           module "forgets" its bonus and has to re-learn it.
    //    ModuleEffectHelper.OnUsedLearningAlgo(module)
    //        -> one learned step: raises each current BONUS_PERCENTAGE_* that is
    //           still below its max.
    //
    //  InitLearningAlgo is called explicitly after AddEffectToModule. It is
    //  idempotent: with the bonus already zeroed, every "base > 0" branch is
    //  skipped, so the max tags are never overwritten with 0.
    // -------------------------------------------------------------------------
    public static class DeviceLogic
    {
        // ---------------------------------------------------------------- implant
        public static void Implant(GameItem device)
        {
            if (device == null) return;

            var module = GetModule(device);
            if (module == null)
            {
                Warn(device, "implant", "no module in the bay");
                return;
            }
            if (HasLearning(module))
            {
                ModEntry.Log.Log("Implant skipped: module '" + module.identifier
                                 + "' already has the self-learning entry. Use 'Self-Learn +1'.");
                return;
            }
            if (!HasAnyBaseBonus(module))
            {
                Warn(device, "implant", "module has no performance/efficiency/quality bonus to learn");
                return;
            }
            if (BothEffectSlotsUsed(module))
            {
                Warn(device, "implant", "both module effect slots are already occupied");
                return;
            }

            int cost = ModEntry.PowerCost;
            GameItem battery;
            if (!TryReservePower(device, cost, "implant", out battery))
                return;

            ModuleEffectHelper.AddEffectToModule(module, ModuleEffectHelper.MODULE_EFFECT_LEARNING_ALGORITHM);
            if (!HasLearning(module))
            {
                // AddEffectToModule refuses silently when the effect is
                // incompatible with the one already installed. No power spent.
                Warn(device, "implant", "the self-learning entry was refused by the module (incompatible effect)");
                return;
            }

            // Snapshot the bonus BEFORE InitLearningAlgo zeroes it: the cap the
            // game will write is base * 2, and we may have to overwrite it.
            int baseP = ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT);
            int baseE = ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_EFFICIENCY_INT);
            int baseQ = ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_QUALITY_INT);

            ModuleEffectHelper.InitLearningAlgo(module);
            ApplyCapMultiplier(module, baseP, baseE, baseQ);
            WriteInt(module, ModEntry.TagUses, 0);
            module.SyncModifiedState();
            if (cost > 0) PowerHelper.DrawPowerSource(battery, cost);

            ModEntry.Log.Log("Implanted self-learning entry into '" + module.identifier
                             + "'. power=" + cost
                             + " uses=0/" + UsesCapText()
                             + " " + ProgressText(module));
        }

        // ------------------------------------------------------------------ learn
        public static void Learn(GameItem device)
        {
            if (device == null) return;

            var module = GetModule(device);
            if (module == null)
            {
                Warn(device, "learn", "no module in the bay");
                return;
            }
            if (!HasLearning(module))
            {
                ModEntry.Log.Warning("Learn skipped: module '" + module.identifier
                                     + "' has no self-learning entry. Use 'Implant' first.");
                return;
            }

            var progress = ReadProgress(module);

            if (progress.TotalMax <= 0)
            {
                Warn(device, "learn", "module has no self-learning capacity (all max tags are 0)");
                return;
            }
            if (progress.AtCap)
            {
                // Nothing to do, but still emit a line: a silent return reads as
                // "the button is broken".
                ModEntry.Log.Log("Learn skipped: '" + module.identifier
                                 + "' is already at the self-learning cap "
                                 + ProgressText(module));
                return;
            }

            int uses = ReadInt(module, ModEntry.TagUses);
            int maxUses = ModEntry.MaxUses;
            if (maxUses > 0 && uses >= maxUses)
            {
                ModEntry.Log.Log("Learn skipped: uses cap reached (" + uses + "/" + maxUses + ").");
                return;
            }

            int steps = ModEntry.StepPerUse;
            if (steps < 1) steps = 1;

            // A step raises EVERY stat that is still below its cap, so the number
            // of steps left is the LARGEST remaining gap. Clamping to it avoids
            // charging for steps that would do nothing.
            int need = Math.Max(
                Math.Max(progress.MaxP - progress.CurP, progress.MaxE - progress.CurE),
                progress.MaxQ - progress.CurQ);
            if (need <= 0)
            {
                ModEntry.Log.Log("Learn skipped: '" + module.identifier
                                 + "' is already at the self-learning cap " + ProgressText(module));
                return;
            }
            if (steps > need) steps = need;

            int cost = ModEntry.PowerCost * steps;
            GameItem battery;
            if (!TryReservePower(device, cost, "learn", out battery))
                return;

            // One press = N vanilla steps. OnUsedLearningAlgo itself only ever
            // adds 1 and stops at the cap, so looping it is the honest way to get
            // +N without changing the vanilla growth rule.
            for (int i = 0; i < steps; i++)
                ModuleEffectHelper.OnUsedLearningAlgo(module);

            WriteInt(module, ModEntry.TagUses, uses + steps);
            module.SyncModifiedState();
            if (cost > 0) PowerHelper.DrawPowerSource(battery, cost);

            ModEntry.Log.Log("Self-learn +" + steps + " on '" + module.identifier
                             + "'. power=" + cost
                             + " uses=" + (uses + steps) + "/" + UsesCapText()
                             + " " + ProgressText(module));
        }

        // ---------------------------------------------------------------- predict
        // Read-only mirror of the two handlers above, used by the UI so the
        // buttons can show what the NEXT press will actually cost. It writes no
        // tags and charges nothing; the clamping rules must stay in sync with
        // Learn() or the displayed number drifts from the real deduction.
        internal static void Predict(GameItem device, out int implantCost, out int learnSteps,
                                     out int learnCost, out int batteryPower, out bool hasBattery)
        {
            implantCost = 0;
            learnSteps = 0;
            learnCost = 0;
            batteryPower = 0;
            hasBattery = false;
            if (device == null) return;

            try
            {
                var battery = GetBattery(device);
                if (battery != null)
                {
                    hasBattery = true;
                    batteryPower = PowerHelper.CheckAvailablePower(battery);
                }
            }
            catch (Exception ex)
            {
                ModEntry.Log.Warning("Power reading failed: " + ex.Message);
            }

            var module = GetModule(device);
            if (module == null) return;

            if (!HasLearning(module) && HasAnyBaseBonus(module) && !BothEffectSlotsUsed(module))
                implantCost = ModEntry.PowerCost;

            if (!HasLearning(module)) return;

            var progress = ReadProgress(module);
            if (progress.TotalMax <= 0 || progress.AtCap) return;

            int need = Math.Max(
                Math.Max(progress.MaxP - progress.CurP, progress.MaxE - progress.CurE),
                progress.MaxQ - progress.CurQ);
            if (need <= 0) return;

            int steps = ModEntry.StepPerUse;
            if (steps < 1) steps = 1;
            if (steps > need) steps = need;

            learnSteps = steps;
            learnCost = ModEntry.PowerCost * steps;
        }

        // --------------------------------------------------------------- helpers
        private static GameItem GetBattery(GameItem device)
        {
            try
            {
                var slot = MachineHelper.GetBatterySlot(device);
                if (slot == null) return null;
                return slot.childItem;
            }
            catch (Exception ex)
            {
                ModEntry.Log.Error("Battery slot lookup failed: " + ex);
                return null;
            }
        }

        private static GameItem GetModule(GameItem device)
        {
            try
            {
                var modules = MachineHelper.GetModules(device);
                if (modules == null) return null;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (modules[i] != null) return modules[i];
                }
            }
            catch (Exception ex)
            {
                ModEntry.Log.Error("Module lookup failed: " + ex);
            }
            return null;
        }

        // Phase 1 of spending power: verify it is available so a refusal never
        // costs the player energy. Phase 2 (DrawPowerSource) runs only once the
        // module change actually happened.
        private static bool TryReservePower(GameItem device, int cost, string action, out GameItem battery)
        {
            battery = null;
            if (cost <= 0) return true;

            battery = GetBattery(device);
            if (battery == null)
            {
                Warn(device, action, "no battery in the power slot (needs " + cost + " power)");
                return false;
            }
            if (!PowerHelper.CanDrawPowerSource(battery, cost))
            {
                Warn(device, action, "not enough power in the battery (needs " + cost + ")");
                return false;
            }
            return true;
        }

        internal static bool HasLearning(GameItem module)
        {
            string e1 = ReadStr(module, ModuleEffectHelper.MODULE_EFFECT1_TAG);
            string e2 = ReadStr(module, ModuleEffectHelper.MODULE_EFFECT2_TAG);
            string algo = ModuleEffectHelper.MODULE_EFFECT_LEARNING_ALGORITHM;
            return e1 == algo || e2 == algo;
        }

        // AddEffectToModule writes effect1 first, then effect2, and returns
        // without touching anything when both are enabled.
        private static bool BothEffectSlotsUsed(GameItem module)
        {
            return module.IsTag(ModuleEffectHelper.MODULE_EFFECT1_TAG)
                && module.IsTag(ModuleEffectHelper.MODULE_EFFECT2_TAG);
        }

        private static bool HasAnyBaseBonus(GameItem module)
        {
            return ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT) > 0
                || ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_EFFICIENCY_INT) > 0
                || ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_QUALITY_INT) > 0;
        }

        // Snapshot of a module's learned / learnable bonuses. A struct keeps the
        // six tag reads in one place and makes "is it maxed out" readable.
        private struct Progress
        {
            public int CurP, CurE, CurQ;
            public int MaxP, MaxE, MaxQ;

            public int TotalMax { get { return MaxP + MaxE + MaxQ; } }

            // A stat whose max is 0 (module had no bonus there) counts as done.
            public bool AtCap { get { return CurP >= MaxP && CurE >= MaxE && CurQ >= MaxQ; } }

            public override string ToString()
            {
                return "P " + CurP + "/" + MaxP
                     + " E " + CurE + "/" + MaxE
                     + " Q " + CurQ + "/" + MaxQ;
            }
        }

        private static Progress ReadProgress(GameItem m)
        {
            var p = new Progress();
            p.CurP = ReadInt(m, ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT);
            p.CurE = ReadInt(m, ModuleHelper.BONUS_PERCENTAGE_EFFICIENCY_INT);
            p.CurQ = ReadInt(m, ModuleHelper.BONUS_PERCENTAGE_QUALITY_INT);
            p.MaxP = ReadInt(m, ModuleEffectHelper.LEARNING_ALGO_MAX_PERFORMANCE_INT);
            p.MaxE = ReadInt(m, ModuleEffectHelper.LEARNING_ALGO_MAX_EFFICIENCY_INT);
            p.MaxQ = ReadInt(m, ModuleEffectHelper.LEARNING_ALGO_MAX_QUALITY_INT);
            return p;
        }

        internal static string ProgressText(GameItem m)
        {
            return ReadProgress(m).ToString();
        }

        // Vanilla InitLearningAlgo has already written base * 2. Only step in
        // when CapMult says otherwise, so the default build stays bit-for-bit
        // vanilla and only an explicit constant change alters the balance.
        private static void ApplyCapMultiplier(GameItem module, int baseP, int baseE, int baseQ)
        {
            if (ModEntry.CapMult == 2) return;

            int mult = ModEntry.CapMult > 0 ? ModEntry.CapMult : 2;
            if (baseP > 0)
                WriteInt(module, ModuleEffectHelper.LEARNING_ALGO_MAX_PERFORMANCE_INT, baseP * mult);
            if (baseE > 0)
                WriteInt(module, ModuleEffectHelper.LEARNING_ALGO_MAX_EFFICIENCY_INT, baseE * mult);
            if (baseQ > 0)
                WriteInt(module, ModuleEffectHelper.LEARNING_ALGO_MAX_QUALITY_INT, baseQ * mult);
            module.SyncModifiedState();
        }

        // ---- tooltip text ----------------------------------------------------

        // Before implanting: what the cap WILL be. InitLearningAlgo writes
        // base * CapMult for each stat whose base > 0 and leaves the current
        // value at 0, so the cap is also the exact number of steps needed.
        internal static string CapPreviewText(GameItem module)
        {
            int p = ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT);
            int e = ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_EFFICIENCY_INT);
            int q = ReadInt(module, ModuleHelper.BONUS_PERCENTAGE_QUALITY_INT);
            if (p <= 0 && e <= 0 && q <= 0) return null;

            int mult = ModEntry.CapMult > 0 ? ModEntry.CapMult : 2;
            var parts = new List<string>();
            if (p > 0) parts.Add("性能 " + (p * mult));
            if (e > 0) parts.Add("效率 " + (e * mult));
            if (q > 0) parts.Add("品质 " + (q * mult));
            return "植入后自学习上限: " + string.Join(" / ", parts.ToArray())
                   + "（每次 +1）";
        }

        // After implanting: current / cap per stat, i.e. the real progress.
        internal static string LearnProgressText(GameItem module)
        {
            var pr = ReadProgress(module);
            if (pr.TotalMax <= 0) return null;

            var parts = new List<string>();
            if (pr.MaxP > 0) parts.Add("性能 " + pr.CurP + "/" + pr.MaxP);
            if (pr.MaxE > 0) parts.Add("效率 " + pr.CurE + "/" + pr.MaxE);
            if (pr.MaxQ > 0) parts.Add("品质 " + pr.CurQ + "/" + pr.MaxQ);
            if (parts.Count == 0) return null;

            return (pr.AtCap ? "自学习（已满）: " : "自学习: ")
                   + string.Join(" / ", parts.ToArray());
        }

        // 0 means "no limit imposed by this mod"; the vanilla max tags still stop it.
        private static string UsesCapText()
        {
            int cap = ModEntry.MaxUses;
            return cap > 0 ? cap.ToString() : "unlimited";
        }

        private static void Warn(GameItem device, string action, string reason)
        {
            string who = device != null && !string.IsNullOrEmpty(device.identifier)
                ? device.identifier : "device";
            ModEntry.Log.Warning(action + " skipped on '" + who + "': " + reason + ".");
        }

        // Tag reads must not be gated on IsTag: a disabled tag still carries its
        // value, and IsTag means IsEnabled on this game.
        private static int ReadInt(GameItem item, string tag)
        {
            try { return item.GetTagReadonly(tag).GetInt(); }
            catch { return 0; }
        }

        private static string ReadStr(GameItem item, string tag)
        {
            try
            {
                string v = item.GetTagReadonly(tag).GetString();
                return v ?? "";
            }
            catch { return ""; }
        }

        // Enable + SetInt in one write, then the caller runs SyncModifiedState:
        // ModifyTag writes `state`, readers use `modifiedState`.
        private static void WriteInt(GameItem item, string tag, int value)
        {
            item.ModifyTag(tag, ts => { ts.Enable(); ts.SetInt(value); }, true);
        }
    }
}
