using System;

namespace Sbl
{
    internal static class Labels
    {
        // Verified sprite names (Items/items_ship):
        //   storage_bay, storage_bay1, storage_bay2 (appearance variants),
        //   storage_bay_large, storage_bay_gun, storage_bay_chem
        // The expansion kit is Items/items_tool2:storage_bay_expansion_kit and must
        // NOT be labelable, hence the "expansion" exclusion below.
        internal static bool IsStorageBay(GameItem item)
        {
            if (item == null) return false;

            string key = null;
            try { key = item.spritePath; } catch { }
            if (string.IsNullOrEmpty(key))
            {
                try { key = item.identifier; } catch { }
            }
            if (string.IsNullOrEmpty(key)) return false;

            if (key.IndexOf("expansion", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (key.IndexOf("kit", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return key.StartsWith("storage_bay", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether this item can carry a label at all.
        ///
        /// Storage bays always qualify. Everything else follows the game's own notion of
        /// "openable container": `GameItem.contentWindow != null`, which is exactly the
        /// test `ItemMouseDoubleClickHandler.OpenContentAction` uses before showing a
        /// window. That single check covers the fridge, wine rack, water tank, secured
        /// containers, crates, backpacks and every storage machine, without a sprite
        /// whitelist that would rot on the next content patch.
        ///
        /// The window is assigned inside the `*ItemDirectory` factory methods at creation
        /// time (`SetContentWindow` has no lazy call sites), so the test is stable from
        /// the first frame the item exists.
        /// </summary>
        internal static bool IsLabelable(GameItem item)
        {
            if (item == null) return false;
            if (IsStorageBay(item)) return true;

            bool everyContainer = Settings.LabelAllContainers == null || Settings.LabelAllContainers.Value;
            if (!everyContainer) return false;

            return HasContentWindow(item);
        }

        internal static bool HasContentWindow(GameItem item)
        {
            if (item == null) return false;
            try { return item.contentWindow != null; } catch { return false; }
        }

        internal static string Get(GameItem item)
        {
            if (item == null) return null;
            try
            {
                // Deliberately not gated on IsTag(): that returns TagState.IsEnabled(),
                // so a tag that exists with a value but was written without Enable()
                // would read back as "no label". The value itself is what matters here.
                TagState state = item.GetTagReadonly(StorageBayLabelMod.LabelTag);
                if (state == null) return null;

                string value = state.GetString();
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        internal static void Set(GameItem item, string label)
        {
            if (item == null) return;
            try
            {
                string tag = StorageBayLabelMod.LabelTag;
                if (string.IsNullOrEmpty(label))
                {
                    item.DisableTag(tag, true);
                }
                else
                {
                    // Vanilla GeneralHelper.SetCustomName does exactly these two steps:
                    // Enable() first, then SetString(). Without Enable() the tag is stored
                    // but reads back as absent (IsTag == IsEnabled).
                    item.ModifyTag(tag, state =>
                    {
                        state.Enable();
                        state.SetString(label);
                    }, true);
                }

                // ModifyTag writes the base `state`, but every reader (GetTagReadonly,
                // and therefore both the tooltip and the sprite overlay) reads
                // `modifiedState`, which is only recomputed on demand. Without this the
                // change shows up seconds later, whenever the game next syncs.
                item.SyncModifiedState();
            }
            catch (Exception e)
            {
                StorageBayLabelMod.SafeError("write label failed: " + e.Message);
            }
        }
    }
}
