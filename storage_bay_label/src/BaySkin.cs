using System.Collections.Generic;
using UnityEngine;

namespace Sbl
{
    /// <summary>
    /// Optional skin for storage bays. Purely data driven: drop PNG files into
    /// <mod folder>\Textures\Items\ and they are picked up on next launch.
    ///
    ///   bay_skin.png    - replaces storage_bay   (appearance variant 1)
    ///   bay_skin_2.png  - replaces storage_bay1  (variant 2)
    ///   bay_skin_3.png  - replaces storage_bay2  (variant 3)
    ///
    /// Missing variant files fall back to bay_skin. With no files present nothing
    /// changes, so the mod stays vanilla by default.
    ///
    /// Only the rendered sprite is swapped; the item's spritePath/shape are untouched,
    /// so inventory footprint, grid alignment and saves are unaffected.
    /// </summary>
    internal static class BaySkin
    {
        internal const string BaseKey = "bay_skin";

        private static readonly HashSet<string> Logged = new HashSet<string>();

        /// <summary>Maps a vanilla sprite name to the file name of our replacement.</summary>
        internal static string KeyFor(string spriteName)
        {
            switch (spriteName)
            {
                case "storage_bay": return BaseKey;
                case "storage_bay1": return BaseKey + "_2";
                case "storage_bay2": return BaseKey + "_3";
                default: return null;
            }
        }

        internal static bool TryResolve(string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (spriteName == null) return false;
            if (Settings.BaySkins != null && !Settings.BaySkins.Value) return false;

            string key = KeyFor(spriteName);
            if (key == null) return false;

            if (ModSprites.TryGet(key, out sprite)) return true;

            // Variant 2/3 may not exist; let them share the base skin.
            if (key != BaseKey && ModSprites.TryGet(BaseKey, out sprite)) return true;

            sprite = null;
            return false;
        }

        internal static bool HasAnySkin()
        {
            Dictionary<string, Sprite> sprites;
            if (!ModSprites.TryGet(out sprites)) return false;

            return sprites.ContainsKey(BaseKey)
                || sprites.ContainsKey(BaseKey + "_2")
                || sprites.ContainsKey(BaseKey + "_3");
        }

        /// <summary>Logs each sprite once: the vanilla pixel size is what custom art must match.</summary>
        internal static void LogOriginalOnce(string spriteName, Sprite vanilla)
        {
            if (vanilla == null || spriteName == null) return;
            if (!Logged.Add("orig:" + spriteName)) return;

            StorageBayLabelMod.SafeLog("vanilla '" + spriteName + "' sprite = "
                + (int)vanilla.rect.width + "x" + (int)vanilla.rect.height + " px");
        }

        internal static void LogAppliedOnce(string spriteName)
        {
            if (spriteName == null) return;
            if (!Logged.Add("applied:" + spriteName)) return;

            StorageBayLabelMod.SafeLog("skin applied for '" + spriteName + "'");
        }
    }
}
