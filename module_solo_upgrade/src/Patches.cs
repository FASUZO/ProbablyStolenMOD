using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ModuleSoloUpgrade
{
    // -------------------------------------------------------------------------
    //  NORMAL ACQUISITION — the Doctor's shelf.
    //
    //  TraderFactory.CreateDoctor() builds the medical trader and hands it
    //  TradeSheet.GetGenericMedicalShop(). That method news up a fresh
    //  List<GameItem> on every call (verified by decompiling it), so a postfix
    //  that appends exactly one device can never duplicate the entry.
    //
    //  GetGenericMedicalShop has a single, parameterless overload — the shape the
    //  skill requires for [HarmonyPatch] attributes, because a patch that cannot
    //  bind aborts activation instead of degrading silently.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(TradeSheet), "GetGenericMedicalShop")]
    public static class DoctorShopPatch
    {
        public static void Postfix(List<GameItem> __result)
        {
            if (__result == null) return;
            try
            {
                for (int i = 0; i < __result.Count; i++)
                {
                    if (__result[i] != null && __result[i].identifier == ModEntry.DeviceId) return;
                }

                ItemRegistration.Ensure();
                var dev = ModHelper.SpawnItem(ModEntry.DeviceId) ?? DeviceFactory.Create();
                // Vanilla shop goods are created through DirectoryMaster.Item(id, true),
                // i.e. owned. Match that so the barter UI treats ours the same way.
                ModHelper.SetItemOwnerShip(dev, true);
                dev.SetAmount(1);
                __result.Add(dev);

                ModEntry.Log.Log("Added the programmer to the Doctor's shop (unitValue="
                                 + dev.unitValue + ").");
            }
            catch (Exception ex)
            {
                ModEntry.Log.Error("Doctor shop patch failed: " + ex);
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Makes "self_learn_programmer" a legal directory id, so anything that asks
    //  DirectoryMaster for it — save reconstruction among others — gets a real
    //  device instead of a dummy GameItem.
    //
    //  The original method is skipped on purpose: it destroys child GameItems of
    //  the item it builds, which would tear the battery slot out of our window.
    //  Item has two overloads, so the parameter types are spelled out.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(DirectoryMaster), "Item", new[] { typeof(string), typeof(bool) })]
    public static class DirectoryItemPatch
    {
        public static bool Prefix(string __0, bool __1, ref GameItem __result)
        {
            if (__0 != ModEntry.DeviceId) return true;
            try
            {
                var dev = DeviceFactory.Create();
                ModHelper.SetItemOwnerShip(dev, __1);
                __result = dev;
                return false;
            }
            catch (Exception ex)
            {
                ModEntry.Log.Error("DirectoryMaster.Item patch failed: " + ex);
                return true;   // fall back to vanilla behaviour rather than break the call
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Tooltip: append the device's own counter. The vanilla effect tooltip only
    //  prints each effect's displayName + description from the moduleEffects
    //  table, so the learned progress is otherwise invisible.
    //
    //  CreateModuleEffectTooltip(RichTextBuilder, GameItem) is a single overload.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(ModuleEffectHelper), "CreateModuleEffectTooltip")]
    public static class ModuleTooltipPatch
    {
        public static void Postfix(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || item == null) return;

            // Only modules: ModuleHelper.CreateModuleTooltip early-returns for
            // anything without MODULE_TAG, so this is the same gate the vanilla
            // path uses.
            if (!item.IsTag(ModuleHelper.MODULE_TAG)) return;

            try
            {
                // Not implanted yet -> preview the cap the device would create
                // (vanilla writes base * 2). Already implanted -> show real
                // progress, which the vanilla tooltip never exposes.
                string line = DeviceLogic.HasLearning(item)
                    ? DeviceLogic.LearnProgressText(item)
                    : DeviceLogic.CapPreviewText(item);
                if (line == null) return;

                builder.AddLine(
                    line,
                    true,
                    RenderHandler.ColorPalette.None,
                    false, false, false, false,
                    RenderHandler.ColorPalette.None,
                    RenderHandler.ColorPalette.None,
                    RenderHandler.ColorPalette.None);
            }
            catch (Exception ex)
            {
                ModEntry.Log.Warning("Tooltip append failed: " + ex);
            }
        }
    }
}
