using HarmonyLib;
using UnityEngine;

namespace Sbl
{
    /// <summary>
    /// Storage bay skin. Uses a postfix so it never fights the water-tank prefix that
    /// sits on the same method: by the time we run, __result already holds whatever the
    /// engine (or that prefix) resolved, and we only overwrite it for bay sprite names.
    /// </summary>
    [HarmonyPatch(typeof(GameItemElement), "ResolveSpriteByName")]
    internal static class PatchResolveStorageBaySkin
    {
        private static void Postfix(GameItemElement __instance, string name, ref Sprite __result)
        {
            if (!Labels.IsStorageBay(__instance)) return;

            // Sprite resolution is the one hook that reliably runs for every rendered
            // item, so it doubles as the overlay's refresh point.
            Overlay.Refresh(__instance);

            // Only the three cyclable appearance sprites; large/gun/chem bays keep vanilla art.
            string key = BaySkin.KeyFor(name);
            if (key == null) return;

            BaySkin.LogOriginalOnce(name, __result);

            Sprite skin;
            if (!BaySkin.TryResolve(name, out skin)) return;

            __result = skin;
            BaySkin.LogAppliedOnce(name);
        }
    }
}
