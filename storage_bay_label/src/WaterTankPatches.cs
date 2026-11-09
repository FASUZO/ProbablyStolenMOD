using System;
using HarmonyLib;
using UnityEngine;

namespace Sbl
{
    /// <summary>
    /// Body sprite: vanilla resolves "water_tank" through GameItemElement.ResolveSpriteByName,
    /// so replacing the result there covers every path (creation, refresh, drag ghost).
    /// </summary>
    [HarmonyPatch(typeof(GameItemElement), "ResolveSpriteByName")]
    internal static class PatchResolveWaterTankBody
    {
        private static bool Prefix(GameItemElement __instance, string name, ref Sprite __result)
        {
            if (name != "water_tank") return true;
            if (!WaterTank.IsManaged(__instance)) return true;

            Sprite body;
            if (!WaterTank.TryBody(out body)) return true;

            __result = body;
            return false;
        }
    }

    /// <summary>
    /// Liquid layer: the engine asks the item for innate sprite modifiers while
    /// HandleInnateModSprite runs. Hand it our func for the duration of that call and
    /// clear it afterwards so the item object stays untouched (it is also what makes
    /// "is the func still ours" a reliable ownership test).
    /// </summary>
    [HarmonyPatch(typeof(GameItemElement), "HandleInnateModSprite")]
    internal static class PatchWaterTankLayers
    {
        private static void Prefix(GameItemElement __instance, ref bool __state)
        {
            __state = false;
            if (!WaterTank.IsManaged(__instance)) return;
            if (__instance.getItemSpriteModifierForItemFunc != null) return;

            __instance.getItemSpriteModifierForItemFunc = WaterTank.LayerFunc;
            __state = true;
        }

        private static Exception Finalizer(GameItemElement __instance, bool __state, Exception __exception)
        {
            if (__state && __instance.getItemSpriteModifierForItemFunc == WaterTank.LayerFunc)
            {
                __instance.getItemSpriteModifierForItemFunc = null;
            }
            return __exception;
        }
    }

    /// <summary>A freshly built tank has not validated its sprite yet.</summary>
    [HarmonyPatch(typeof(WaterTankHelper), nameof(WaterTankHelper.Create))]
    internal static class PatchWaterTankCreated
    {
        private static void Postfix(GameItem __result)
        {
            GameItemElement element = __result as GameItemElement;
            if (element == null) return;
            if (!WaterTank.IsManaged(__result)) return;

            element.ValidatePartialSprite();
        }
    }

    /// <summary>Filling / pouring / purifying all funnel through here.</summary>
    [HarmonyPatch(typeof(SpriteHelper), nameof(SpriteHelper.UpdateWaterContainerSprite))]
    internal static class PatchWaterTankLiquidChanged
    {
        private static void Postfix(GameItem gameItem)
        {
            if (!WaterTank.IsManaged(gameItem)) return;

            GameItemElement element = gameItem as GameItemElement;
            if (element == null) return;

            element.HandleInnateModSprite();
        }
    }
}
