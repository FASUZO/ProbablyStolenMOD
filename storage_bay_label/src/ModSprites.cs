using System.Collections.Generic;
using UnityEngine;

namespace Sbl
{
    /// <summary>
    /// Access to the sprites this mod ships in Textures/Items/*.png. The loader fills
    /// LoadedMod.Sprites with key = file name without extension (see
    /// ModLoader.LoadTexturesFromFolder, pixelArt = true).
    /// </summary>
    internal static class ModSprites
    {
        internal const string ModId = "storage_bay_label";

        private static LoadedMod _owner;
        private static Dictionary<string, Sprite> _sprites;

        internal static bool TryGet(out Dictionary<string, Sprite> sprites)
        {
            sprites = _sprites;
            if (sprites != null && sprites.Count > 0) return true;

            sprites = null;

            LoadedMod owner = Owner();
            if (owner == null || owner.Sprites == null || owner.Sprites.Count == 0) return false;

            _sprites = owner.Sprites;
            sprites = _sprites;
            return true;
        }

        internal static bool TryGet(string key, out Sprite sprite)
        {
            sprite = null;

            Dictionary<string, Sprite> sprites;
            if (!TryGet(out sprites)) return false;

            return sprites.TryGetValue(key, out sprite) && sprite != null;
        }

        private static LoadedMod Owner()
        {
            if (_owner != null && _owner.Sprites != null) return _owner;

            List<LoadedMod> mods = ModLoader.Mods;
            if (mods == null) return null;

            for (int i = 0; i < mods.Count; i++)
            {
                LoadedMod mod = mods[i];
                if (mod == null || mod.Manifest == null) continue;
                if (mod.Manifest.ID != ModId) continue;
                if (!mod.IsEnabled) continue;

                _owner = mod;
                return _owner;
            }

            return null;
        }
    }
}
