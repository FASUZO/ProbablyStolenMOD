using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sbl
{
    /// <summary>
    /// Water-tank sprite redraw: replaces the vanilla single "water_tank" body with
    /// tank_body + a coloured liquid layer chosen from six capacity buckets.
    ///
    /// Verified game API (0.46D):
    ///   WaterTankHelper.Create()                      - no overload, safe to patch
    ///   SpriteHelper.UpdateWaterContainerSprite(item) - called whenever liquid changes
    ///   SpriteHelper.MapCapacity(int)                 - vanilla bucket key ("10".."100")
    ///   WaterHelper.GetCurrentCapacity / GetLiquidContainerColor (hex string)
    ///   Liquid.GetColorBase(hex) / MetaHelper.HexToColor(hex, alpha)
    ///   ItemSpriteModifier(Sprite, Vector2, int, bool, int, bool, Color)
    ///   GameItemFunc.getItemSpriteModifierForItemFunc
    ///   ModLoader.Mods / LoadedMod.Sprites (filled by ModLoader.LoadTextures from
    ///   this mod's Textures/Items/*.png, key = file name without extension)
    /// </summary>
    internal static class WaterTank
    {
        private const string CapacityTag = "LIQUID_CONTAINER_CAPACITY";
        private const string TankSpriteName = "water_tank";
        private const string TankAtlas = "Items/items_tool2";
        private const string BodyKey = "tank_body";

        internal static readonly Func<GameItem, List<ItemSpriteModifier>> LayerFunc = Layers;

        // ------------------------------------------------------------------ public

        internal static bool IsManaged(GameItem item)
        {
            if (Settings.WaterTankSprites != null && !Settings.WaterTankSprites.Value) return false;
            if (!IsOriginal(item)) return false;
            if (!HasSprites()) return false;

            Func<GameItem, List<ItemSpriteModifier>> current = item.getItemSpriteModifierForItemFunc;
            return current == null || current == LayerFunc;
        }

        /// <summary>
        /// Vanilla tank, and nobody has retextured it yet. Mirrors what the engine
        /// itself uses to decide whether a tank still shows the built-in artwork.
        /// </summary>
        internal static bool IsOriginal(GameItem item)
        {
            if (item == null) return false;
            if (item.identifier != "water_tank") return false;
            if (item.useSpriteFromMod) return false;
            if (item.spriteAtlasPath != TankAtlas) return false;
            return item.spritePath == TankSpriteName;
        }

        internal static List<ItemSpriteModifier> Layers(GameItem item)
        {
            if (item == null) return null;

            Dictionary<string, Sprite> sprites;
            if (!TrySprites(out sprites)) return null;

            int capacity = item.GetTagReadonly(CapacityTag).GetInt();
            if (capacity <= 0) return null;

            int current = WaterHelper.GetCurrentCapacity(item);
            if (current <= 0) return null;

            string level = SpriteHelper.MapCapacity(current);
            if (string.IsNullOrEmpty(level)) return null;

            string color = WaterHelper.GetLiquidContainerColor(item);
            if (string.IsNullOrEmpty(color)) return null;

            string key = "tank_" + Liquid.GetColorBase(color) + "_" + level;

            Sprite sprite;
            if (!sprites.TryGetValue(key, out sprite) || sprite == null) return null;

            List<ItemSpriteModifier> layers = new List<ItemSpriteModifier>(1);
            layers.Add(new ItemSpriteModifier(
                sprite,
                Vector2.zero,
                0,
                false,
                0,
                false,
                MetaHelper.HexToColor(color, 1f)));
            return layers;
        }

        internal static bool TryBody(out Sprite sprite)
        {
            sprite = null;

            Dictionary<string, Sprite> sprites;
            if (!TrySprites(out sprites)) return false;

            return sprites.TryGetValue(BodyKey, out sprite) && sprite != null;
        }

        internal static bool HasSpritesForLog()
        {
            Dictionary<string, Sprite> sprites;
            return TrySprites(out sprites);
        }

        // ----------------------------------------------------------------- private

        private static bool HasSprites()
        {
            Dictionary<string, Sprite> sprites;
            return TrySprites(out sprites);
        }

        private static bool TrySprites(out Dictionary<string, Sprite> sprites)
        {
            sprites = null;

            if (!ModSprites.TryGet(out sprites)) return false;

            if (!sprites.ContainsKey(BodyKey))
            {
                sprites = null;
                return false;
            }

            return true;
        }
    }
}
