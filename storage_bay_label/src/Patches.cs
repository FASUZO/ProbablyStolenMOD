using System.Collections.Generic;
using HarmonyLib;

namespace Sbl
{
    /// <summary>
    /// Records "the tooltip of this item is being built right now" so the
    /// GetDisplayName patch only appends the suffix inside item tooltips and
    /// never in event log lines / negotiation text.
    /// </summary>
    internal static class TooltipScope
    {
        private static readonly List<GameItem> Stack = new List<GameItem>(4);

        internal static void Enter(GameItem item)
        {
            Stack.Add(item);
        }

        internal static void Exit()
        {
            int last = Stack.Count - 1;
            if (last >= 0) Stack.RemoveAt(last);
        }

        internal static bool IsTop(GameItem item)
        {
            int last = Stack.Count - 1;
            return item != null && last >= 0 && ReferenceEquals(Stack[last], item);
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "GetTooltipBasic")]
    internal static class PatchTooltipBasic
    {
        private static void Prefix(GameItemElement __instance)
        {
            TooltipScope.Enter(__instance);
        }

        private static void Postfix()
        {
            TooltipScope.Exit();
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetDisplayName))]
    internal static class PatchDisplayName
    {
        private static void Postfix(GameItem __instance, ref string __result)
        {
            if (Settings.ShowTooltipSuffix != null && !Settings.ShowTooltipSuffix.Value) return;
            if (!TooltipScope.IsTop(__instance)) return;

            string label = Labels.Get(__instance);
            if (string.IsNullOrEmpty(label)) return;

            __result = __result + " · " + label;
        }
    }

    [HarmonyPatch(typeof(ItemContextHandler), "UpdateConditions")]
    internal static class PatchUpdateConditions
    {
        private static void Postfix(ItemContextHandler __instance)
        {
            LabelUI.Refresh(__instance);
        }
    }

    // Runs after UpdateConditions in OnEventPress and rewrites every caption from its
    // localization key, so the borrowed button's caption has to be restored afterwards.
    [HarmonyPatch(typeof(ItemContextHandler), "UpdateSelectButtonColor")]
    internal static class PatchUpdateSelectButtonColor
    {
        private static void Postfix(ItemContextHandler __instance)
        {
            LabelUI.Reapply(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemContextHandler), "TriggerButton")]
    internal static class PatchTriggerButton
    {
        private static bool Prefix(ItemContextHandler __instance, RichTextElement button)
        {
            return !LabelUI.TryHandleClick(__instance, button);
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "Validate")]
    internal static class PatchElementValidate
    {
        private static void Postfix(GameItemElement __instance)
        {
            Overlay.Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "UpdateRectTransform")]
    internal static class PatchElementRectTransform
    {
        private static void Postfix(GameItemElement __instance)
        {
            Overlay.Refresh(__instance);
        }
    }

    // Containers re-validate their contents whenever an item is added, removed or
    // swapped. Refreshing the whole child list here makes the badge track moves
    // immediately instead of waiting for the next poll.
    [HarmonyPatch(typeof(GameGridInventory), "Validate")]
    internal static class PatchGridValidate
    {
        private static void Postfix(GameGridInventory __instance)
        {
            Overlay.RefreshItems(__instance.childItems);
        }
    }

    [HarmonyPatch(typeof(GameSlotInventory), "Validate")]
    internal static class PatchSlotValidate
    {
        private static void Postfix(GameSlotInventory __instance)
        {
            Overlay.RefreshItems(__instance.childItems);
        }
    }
}
