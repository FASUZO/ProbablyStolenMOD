using System;
using System.Collections.Generic;

namespace ModuleSoloUpgrade
{
    // =========================================================================
    //  模组独自升级 / Module Solo Upgrade
    //
    //  Adds a machine ("Self-Learning Module Programmer") that behaves like the
    //  vanilla bottle printer: a battery slot, a module bay and two buttons.
    //  It implants the vanilla self-learning entry into a module and then spends
    //  electricity to perform one extra self-learning step at a time.
    //
    //  Everything below follows the project rules in the probably-stolen-modding
    //  skill: ASCII-only log text, the manifest.Name log-prefix trick, vanilla
    //  tag writes (Enable + SetInt + SyncModifiedState), and patches that bind to
    //  single-overload methods so PatchAll can never fail activation.
    // =========================================================================

    public class ModEntry : IMod
    {
        // --- identity -------------------------------------------------------
        internal const string ModId = "module_solo_upgrade";
        internal const string DeviceId = "self_learn_programmer";
        internal const string SpriteKey = "self_learn_programmer";
        internal const string DeviceName = "自学习模块编程台";
        internal const string DeviceFlavor =
            "放入模组、装好电池，消耗电量即可为其植入「自学习」词条，并一次次推进自学习进度。";

        // --- custom tags ----------------------------------------------------
        // Skip-validate writes are used for both: an unknown tag needs no shape
        // recompute, and the value survives ComputeModifiedShapeState, so it is
        // serialized together with the item.
        internal const string TagUses = "SELF_LEARN_USES_INT";
        internal const string TagDevice = "SELF_LEARN_DEVICE_TAG";

        internal static ModManifest Manifest { get; private set; }
        internal static ModLog Log { get; private set; }

        // --- fixed balance values -------------------------------------------
        // Deliberately NOT ModConfig entries. The mod ships one tuned set of
        // numbers, so there is nothing for the player to configure — and no
        // config file to go stale between versions.
        internal const int PowerCost = 10;    // electricity per action
        internal const int MaxUses = 0;       // 0 = only the vanilla cap applies
        internal const int ShopPrice = 250;   // base value at the Doctor

        // The self-learning cap is computed by the game as base * 2 (see
        // ModuleEffectHelper.InitLearningAlgo: "ldc.i4.2; mul"). This is the only
        // value left tunable: 2 reproduces vanilla exactly, and anything higher
        // makes the device strictly stronger than the original mechanic.
        // A property (not a const) also keeps the override branch reachable,
        // avoiding CS0162 in ApplyCapMultiplier.
        internal static ConfigEntry<int> CfgCapMult;
        internal static int CapMult { get { return CfgCapMult != null ? CfgCapMult.Value : 2; } }

        // How many vanilla steps one press performs. 1 is the exact vanilla pace
        // (one +1, one step of the cap). Raising it only saves clicks — the
        // electricity cost is multiplied by the same number.
        internal static ConfigEntry<int> CfgStepPerUse;
        internal static int StepPerUse { get { return CfgStepPerUse != null ? CfgStepPerUse.Value : 1; } }

        public void Init(ModManifest manifest)
        {
            Manifest = manifest;

            // ModLog copies manifest.Name into its private prefix, and the F8
            // console font (OfficeCodePro) has no CJK glyphs. Swap the ASCII name
            // in only while the log object is built, then restore the display name.
            string displayName = manifest.Name;
            manifest.Name = "ModSoloUpgrade";
            Log = new ModLog(manifest);
            manifest.Name = displayName;

            // The only ModConfig entry on purpose: power cost, shop price and the
            // step count stay fixed constants.
            var cfg = ModConfig.For(manifest.ID);
            CfgCapMult = cfg.Define("cap_mult", 2, "Balance",
                (Func<string>)(() => "自学习上限倍率"),
                (int?)1, (int?)10,
                (Func<string>)(() => "上限 = 模组原加成 × 该值，原版为 2。只影响之后新植入的模组，已植入的不变。"),
                false);
            CfgStepPerUse = cfg.Define("step_per_use", 1, "Balance",
                (Func<string>)(() => "每次点击推进步数"),
                (int?)1, (int?)10,
                (Func<string>)(() => "一次「自学习 +1」推进几步。1 = 原版每次 +1；耗电按步数累计。"),
                false);
            cfg.Save();

            Log.Log("Init OK. game=" + JoinVersions(manifest.GameVersions)
                    + " device=" + DeviceId
                    + " power_cost=" + PowerCost
                    + " max_uses=" + MaxUses
                    + " shop_price=" + ShopPrice
                    + " cap_mult=" + CapMult
                    + " step_per_use=" + StepPerUse);

            // Registered here, not in OnEnable: the entry list behind the panel
            // is a plain static List, so it is safe this early, and Init is the
            // one callback the loader is guaranteed to run. OnEnable re-registers
            // anyway (idempotent) in case the panel was rebuilt in between.
            RegisterDebugButton("init");
        }

        public void OnEnable()
        {
            // Nothing in here may be allowed to abort the callback: the loader
            // swallows OnEnable exceptions, which is exactly how a missing debug
            // button turns into "the hotkey does nothing" with no trace in the log.
            try
            {
                // Register the factory first: ModHelper.SpawnItem(id), the debug
                // panel's generic "add item" list and save reconstruction all look
                // the id up in PreBuiltItemHelper.preBuiltItems.
                ItemRegistration.Ensure();
                RegisterDebugButton("enable");
                Log.Log("Enabled. Buy the programmer from the Doctor. Dev copy: F9 > Spawn > Self-Learning Programmer (Grant).");
            }
            catch (Exception ex)
            {
                Log.Error("OnEnable failed: " + ex);
            }
        }

        public void OnDisable()
        {
            try
            {
                ModHelper.RemoveDebugFunctions(ModId);
            }
            catch (Exception ex)
            {
                Log.Warning("Could not remove debug functions: " + ex);
            }
        }

        // Idempotent: Remove(source) drops every entry this mod registered, so
        // calling it twice (Init + OnEnable) still leaves exactly one button.
        private static void RegisterDebugButton(string stage)
        {
            try
            {
                ModHelper.RemoveDebugFunctions(ModId);
                ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN,
                    "Self-Learning Programmer (Grant)",
                    GrantFromDebugPanel,
                    false);
                Log.Log("Debug button registered (" + stage
                        + "): F9 -> Spawn -> Self-Learning Programmer (Grant).");
            }
            catch (Exception ex)
            {
                Log.Error("Debug button registration failed (" + stage + "): " + ex);
            }
        }

        // Kept outside the lambda so an exception surfaces in the log instead of
        // dying silently inside the debug panel's own try/catch.
        private static void GrantFromDebugPanel()
        {
            try
            {
                ItemRegistration.Ensure();
                var dev = ModHelper.SpawnItem(DeviceId);
                if (dev == null)
                {
                    Log.Error("Debug grant: SpawnItem returned null for " + DeviceId);
                    return;
                }

                // Both of these are load-bearing. MoveItemToInventory is just
                // GraphUtils.TryAcceptAll(inv, item, -1) with the return value
                // discarded, and -1 means "item.unitCount" — so a device with a
                // zero count is refused without a word. Ownership matters because
                // the inventory filters on GeneralHelper.IsItemOwned.
                dev.SetAmount(1);
                ModHelper.SetItemOwnerShip(dev, true);

                var emp = EmporiumEntry.Instance;
                if (emp == null || emp.invElement == null)
                {
                    Log.Error("Debug grant: main inventory not ready.");
                    return;
                }

                int placed = GraphUtils.TryAcceptAll(emp.invElement, dev, -1);
                if (placed > 0)
                {
                    Log.Log("Debug grant: placed (units=" + placed
                            + " size=" + dev.GetModifiedItemSize() + ").");
                }
                else
                {
                    Log.Warning("Debug grant: inventory refused it (units=" + dev.unitCount
                                + " size=" + dev.GetModifiedItemSize()
                                + "). Main inventory may be full.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Debug grant failed: " + ex);
            }
        }

        private static string JoinVersions(List<string> versions)
        {
            if (versions == null || versions.Count == 0) return "unknown";
            return string.Join(",", versions.ToArray());
        }
    }
}
