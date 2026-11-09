using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sbl
{
    /// <summary>
    /// Independent screen-space overlay for the storage-bay label.
    ///
    /// Why an own canvas (this is the 1.5.0 design, restored in 1.6.1): attaching the
    /// badge as a child of the item's own RectTransform was tried in 1.6.0 and the
    /// badge never rendered -- item sprites in this game are not plain UI Graphics, so
    /// a child node of `middle` is drawn *under* the sprite no matter its sibling
    /// index, and any mask on the cell can clip it as well. A separate overlay canvas
    /// with explicit corner maths is the only layout that was verified to show.
    ///
    /// Layering: the canvas uses the item canvas's sortingOrder + 1 instead of a huge
    /// hardcoded value, so floating/settings windows on higher canvases naturally
    /// render above the badge.
    ///
    /// The driver is created in Refresh(), never inside Tick() -- a driver cannot
    /// create itself from the method it is supposed to call.
    /// </summary>
    internal static class Overlay
    {
        private sealed class Item
        {
            internal GameItemElement Element;
            internal string Value;
            internal TextMeshProUGUI Text;
            internal TextMeshProUGUI Shadow;
            internal Image Plate;
            internal int Index;

            internal string LastText;
            internal int LastFont;
            internal float LastX;
            internal float LastY;
            internal float LastW;
            internal float LastH;
            internal string LastCorner;
            internal int LastOpacity;
            internal bool LastShadow;
        }

        private static readonly List<Item> Items = new List<Item>(8);
        private static readonly Dictionary<GameItemElement, Item> ByElement =
            new Dictionary<GameItemElement, Item>();

        private static readonly Vector3[] Corners = new Vector3[4];

        private static Canvas _canvas;
        private static RectTransform _root;
        private static TMP_FontAsset _font;
        private static Sprite _badgeSprite;

        private static int _itemIndex;
        private static int _scanTick;
        private static bool _suppressed;
        private static bool _restoredLogged;

        // Diagnostics are off during normal play; switch to true only when debugging.
        private static readonly bool Verbose = false;

        // ------------------------------------------------------------------ public

        internal static void Reset()
        {
            foreach (Item item in Items) Destroy(item);
            Items.Clear();
            ByElement.Clear();
        }

        internal static void RefreshAll()
        {
            for (int i = Items.Count - 1; i >= 0; i--) Refresh(Items[i].Element);
        }

        internal static void Refresh(GameItemElement element)
        {
            if (element == null) return;

            try
            {
                if (!Labels.IsLabelable(element)) return;
                if (ByElement.ContainsKey(element)) return;

                EnsureCanvas();

                Item item = new Item { Element = element, Index = ++_itemIndex };
                ByElement[element] = item;
                Items.Add(item);

                // One-shot save/restore check: a bay that already carries a label when
                // it is first registered proves the tag survived the save load.
                string existing = Labels.Get(element);
                if (!string.IsNullOrEmpty(existing) && !_restoredLogged)
                {
                    _restoredLogged = true;
                    StorageBayLabelMod.SafeLog("label present at registration (save ok): "
                        + StorageBayLabelMod.Ascii(existing));
                }

                if (Verbose) StorageBayLabelMod.SafeLog("overlay tracked #" + item.Index);
            }
            catch (System.Exception e)
            {
                StorageBayLabelMod.SafeError("overlay refresh failed: " + e.Message);
            }
        }

        /// <summary>
        /// Container contents changed: register every child item so the badge appears
        /// in the same frame the item is moved in, instead of on the next poll.
        /// Already-tracked elements are ignored by Refresh itself.
        /// </summary>
        internal static void RefreshItems(List<GameItem> items)
        {
            if (items == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                GameItemElement element = items[i] as GameItemElement;
                if (element != null) Refresh(element);
            }
        }

        internal static void Tick()
        {
            if (Items.Count == 0 || _canvas == null || _root == null) return;

            if (Settings.ShowBadge != null && !Settings.ShowBadge.Value)
            {
                foreach (Item item in Items)
                {
                    Hide(item);
                    ClearDirty(item);
                }
                return;
            }

            bool blocked = UpdateLayering();

            for (int i = Items.Count - 1; i >= 0; i--)
            {
                Item item = Items[i];

                if (IsDead(item.Element))
                {
                    Destroy(item);
                    ByElement.Remove(item.Element);
                    Items.RemoveAt(i);
                    continue;
                }

                if (blocked)
                {
                    Hide(item);
                    ClearDirty(item);
                    continue;
                }

                item.Value = Labels.Get(item.Element);
                if (string.IsNullOrEmpty(item.Value))
                {
                    Hide(item);
                    ClearDirty(item);
                    continue;
                }

                RectTransform rect = PickRect(item.Element);
                if (rect == null || !rect.gameObject.activeInHierarchy)
                {
                    Hide(item);
                    ClearDirty(item);
                    continue;
                }

                rect.GetWorldCorners(Corners);

                Canvas elementCanvas = rect.GetComponentInParent<Canvas>();
                Camera camera = elementCanvas != null ? elementCanvas.worldCamera : null;
                if (elementCanvas != null)
                {
                    int target = Mathf.Clamp(elementCanvas.sortingOrder + 1, 1, 1000);
                    if (_canvas.sortingOrder != target) _canvas.sortingOrder = target;
                }

                Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
                Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, Corners[2]);

                float iconWidth = topRight.x - bottomLeft.x;
                float iconHeight = topRight.y - bottomLeft.y;
                if (iconWidth < 3f || iconHeight < 3f)
                {
                    Hide(item);
                    ClearDirty(item);
                    continue;
                }

                // Config: font scale, badge corner, opacity, shadow.
                int scale = Settings.FontScale != null ? Settings.FontScale.Value : 100;
                int fontSize = Mathf.Clamp(Mathf.RoundToInt(iconHeight * 0.18f * scale / 100f), 7, 32);
                while (fontSize > 7 && RawBadgeWidth(item.Value, fontSize) > iconWidth - 4f) fontSize--;

                float boxWidth = Mathf.Clamp(RawBadgeWidth(item.Value, fontSize), 20f, Mathf.Max(20f, iconWidth - 4f));
                float boxHeight = fontSize * 1.25f;

                string corner = Settings.BadgePosition != null ? Settings.BadgePosition.Value : "bottom-left";
                Vector2 badgePos = BadgeCorner(corner, bottomLeft, topRight, boxWidth, boxHeight);
                int opacity = Settings.BackgroundOpacity != null ? Settings.BackgroundOpacity.Value : 55;
                bool shadow = Settings.TextShadow == null || Settings.TextShadow.Value;

                if (SameFrame(item, badgePos, boxWidth, boxHeight, fontSize, corner, opacity, shadow)) continue;

                if (item.Text == null || item.Plate == null)
                {
                    CreateNodes(item);
                    if (item.Text == null) continue;
                }

                item.Text.text = item.Value;
                item.Text.fontSize = fontSize;
                item.Text.gameObject.SetActive(true);

                SetScreenRect(item.Text.rectTransform, badgePos, boxWidth, boxHeight);

                if (item.Shadow != null)
                {
                    item.Shadow.gameObject.SetActive(shadow);
                    if (shadow)
                    {
                        item.Shadow.text = item.Value;
                        item.Shadow.fontSize = fontSize;
                        SetScreenRect(item.Shadow.rectTransform,
                            new Vector2(badgePos.x + 1f, badgePos.y - 1f), boxWidth, boxHeight);
                    }
                }

                if (item.Plate != null)
                {
                    item.Plate.gameObject.SetActive(true);
                    item.Plate.color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity / 100f));
                    SetScreenRect(item.Plate.rectTransform, badgePos, boxWidth, boxHeight);
                }

                item.LastText = item.Value;
                item.LastFont = fontSize;
                item.LastX = badgePos.x;
                item.LastY = badgePos.y;
                item.LastW = boxWidth;
                item.LastH = boxHeight;
                item.LastCorner = corner;
                item.LastOpacity = opacity;
                item.LastShadow = shadow;

                if (Verbose)
                {
                    StorageBayLabelMod.SafeLog("overlay placed #" + item.Index
                        + " screen=" + (int)bottomLeft.x + "," + (int)bottomLeft.y
                        + " badge=" + (int)boxWidth + "x" + (int)boxHeight
                        + " font=" + fontSize);
                }
            }
        }

        // ----------------------------------------------------------------- private

        /// <summary>
        /// Hide every badge while a genuine floating window is open. Deliberately NOT a
        /// generic "any canvas with a higher sortingOrder" test: the game keeps several
        /// UI canvases alive permanently, which would suppress badges all the time.
        /// The tooltip window is excluded so the badge stays visible while hovering.
        /// </summary>
        private static bool UpdateLayering()
        {
            if ((_scanTick++ % 15) != 0) return _suppressed;

            bool blocked = false;

            try
            {
                GameOptionUIManager options = GameOptionUIManager.Instance;
                if (options != null && options.optionPanel != null && options.optionPanel.activeInHierarchy)
                {
                    blocked = true;
                }

                if (!blocked)
                {
                    OverlayHandler handler = OverlayHandler.current;
                    if (handler != null)
                    {
                        string[] windows =
                        {
                            "confirmWindow", "itemChooseWindow", "barterWindow", "navWindow", "logWindow"
                        };

                        foreach (string name in windows)
                        {
                            PixelWindow window = Reflect.GetField<PixelWindow>(handler, name)
                                                 ?? Reflect.GetProperty<PixelWindow>(handler, name);
                            if (window != null && window.handler != null && window.handler.gameObject.activeInHierarchy)
                            {
                                blocked = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // do not suppress on an inspection failure
            }

            _suppressed = blocked;
            return _suppressed;
        }

        private static bool SameFrame(Item item, Vector2 pos, float w, float h, int font, string corner, int opacity, bool shadow)
        {
            return item.Text != null
                && item.Text.gameObject.activeSelf
                && item.LastText == item.Value
                && item.LastFont == font
                && item.LastCorner == corner
                && item.LastOpacity == opacity
                && item.LastShadow == shadow
                && Mathf.Abs(item.LastX - pos.x) < 0.5f
                && Mathf.Abs(item.LastY - pos.y) < 0.5f
                && Mathf.Abs(item.LastW - w) < 0.5f
                && Mathf.Abs(item.LastH - h) < 0.5f;
        }

        private static Vector2 BadgeCorner(string corner, Vector2 bottomLeft, Vector2 topRight, float badgeW, float badgeH)
        {
            const float inset = 2f;

            switch (corner)
            {
                case "top-left":
                    return new Vector2(bottomLeft.x + inset, topRight.y - badgeH - inset);
                case "bottom-right":
                    return new Vector2(topRight.x - badgeW - inset, bottomLeft.y + inset);
                case "top-right":
                    return new Vector2(topRight.x - badgeW - inset, topRight.y - badgeH - inset);
                default:
                    return new Vector2(bottomLeft.x + inset, bottomLeft.y + inset);
            }
        }

        private static void ClearDirty(Item item)
        {
            item.LastText = null;
            item.LastFont = 0;
            item.LastW = 0f;
            item.LastH = 0f;
            item.LastCorner = null;
            item.LastOpacity = -1;
        }

        private static bool IsDead(GameItemElement element)
        {
            try { return element.IsDestroyed(); } catch { return true; }
        }

        private static void Destroy(Item item)
        {
            if (item.Text != null) Object.Destroy(item.Text.gameObject);
            if (item.Shadow != null) Object.Destroy(item.Shadow.gameObject);
            if (item.Plate != null) Object.Destroy(item.Plate.gameObject);
            item.Text = null;
            item.Shadow = null;
            item.Plate = null;
        }

        private static void Hide(Item item)
        {
            if (item.Text != null) item.Text.gameObject.SetActive(false);
            if (item.Shadow != null) item.Shadow.gameObject.SetActive(false);
            if (item.Plate != null) item.Plate.gameObject.SetActive(false);
        }

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;

            GameObject go = new GameObject("StorageBayLabelOverlay");
            Object.DontDestroyOnLoad(go);

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1; // raised to itemCanvas + 1 inside Tick

            _root = go.GetComponent<RectTransform>();
            if (_root != null)
            {
                _root.anchorMin = Vector2.zero;
                _root.anchorMax = Vector2.one;
                _root.offsetMin = Vector2.zero;
                _root.offsetMax = Vector2.zero;
            }

            go.AddComponent<OverlayDriver>();
        }

        private static void CreateNodes(Item item)
        {
            try
            {
                // Text first: a failure must never leave an empty dark plate behind.
                GameObject textGo = new GameObject("StorageLabelText", typeof(RectTransform), typeof(TextMeshProUGUI));
                RectTransform textRt = textGo.GetComponent<RectTransform>();
                textRt.SetParent(_root, false);
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.zero;
                textRt.pivot = Vector2.zero;

                TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
                text.enabled = true;
                text.raycastTarget = false;
                // Midline keeps the glyphs centred in the compact badge; the old
                // BottomLeft + tall box pushed them to the very bottom edge.
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.enableWordWrapping = false;
                text.enableAutoSizing = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.lineSpacing = 0f;
                text.characterSpacing = 0f;
                text.margin = new Vector4(3f, 0f, 3f, 0f);
                text.color = Color.white;
                text.alpha = 1f;

                TMP_FontAsset font = Font();
                if (font != null) text.font = font;

                try
                {
                    Material material = new Material(text.fontMaterial);
                    if (material.HasProperty("_FaceColor")) material.SetColor("_FaceColor", Color.white);
                    if (material.HasProperty("_OutlineColor")) material.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0.9f));
                    if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 0.18f);
                    text.fontMaterial = material;
                }
                catch
                {
                    // keep the shared material
                }

                if (text.canvasRenderer != null) text.canvasRenderer.SetAlpha(1f);
                item.Text = text;

                GameObject plateGo = new GameObject("StorageLabelPlate", typeof(RectTransform), typeof(Image));
                RectTransform plateRt = plateGo.GetComponent<RectTransform>();
                plateRt.SetParent(_root, false);
                plateRt.anchorMin = Vector2.zero;
                plateRt.anchorMax = Vector2.zero;
                plateRt.pivot = Vector2.zero;

                Image plate = plateGo.GetComponent<Image>();
                plate.raycastTarget = false;
                plate.sprite = BadgeSprite();
                plate.type = Image.Type.Sliced;
                plate.color = Color.white;
                item.Plate = plate;

                // Shadow as a separate, offset copy of the text. This is deliberately
                // NOT a TMP underlay/outline shader keyword: geometry can only be in
                // the right place or not, whereas a wrong keyword combination on the
                // game's font asset can silently drop the whole glyph run.
                GameObject shadowGo = new GameObject("StorageLabelShadow", typeof(RectTransform), typeof(TextMeshProUGUI));
                RectTransform shadowRt = shadowGo.GetComponent<RectTransform>();
                shadowRt.SetParent(_root, false);
                shadowRt.anchorMin = Vector2.zero;
                shadowRt.anchorMax = Vector2.zero;
                shadowRt.pivot = Vector2.zero;

                TextMeshProUGUI shadowText = shadowGo.GetComponent<TextMeshProUGUI>();
                shadowText.enabled = true;
                shadowText.raycastTarget = false;
                shadowText.alignment = TextAlignmentOptions.MidlineLeft;
                shadowText.enableWordWrapping = false;
                shadowText.enableAutoSizing = false;
                shadowText.overflowMode = TextOverflowModes.Overflow;
                shadowText.lineSpacing = 0f;
                shadowText.characterSpacing = 0f;
                shadowText.margin = new Vector4(3f, 0f, 3f, 0f);
                shadowText.color = new Color(0f, 0f, 0f, 0.85f);
                shadowText.alpha = 1f;
                if (font != null) shadowText.font = font;
                if (shadowText.canvasRenderer != null) shadowText.canvasRenderer.SetAlpha(1f);
                item.Shadow = shadowText;

                // Draw order: plate, then shadow, then the white text.
                item.Plate.transform.SetAsFirstSibling();
                item.Shadow.transform.SetSiblingIndex(1);
                item.Text.transform.SetAsLastSibling();
            }
            catch (System.Exception e)
            {
                StorageBayLabelMod.SafeError("overlay nodes failed: " + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>A small pixel-style framed badge: 1px light border, translucent fill.</summary>
        private static Sprite BadgeSprite()
        {
            if (_badgeSprite != null) return _badgeSprite;

            const int size = 8;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            // Subtle dark plate: no bright grey border competing with the pixel art.
            Color32 fill = new Color32(10, 12, 18, 165);
            Color32 edge = new Color32(48, 54, 64, 220);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    texture.SetPixel(x, y, border ? edge : fill);
                }
            }
            texture.Apply();

            _badgeSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(1f, 1f, 1f, 1f));

            return _badgeSprite;
        }

        private static void SetScreenRect(RectTransform rect, Vector2 bottomLeft, float width, float height)
        {
            rect.sizeDelta = new Vector2(width, height);
            rect.position = new Vector3(bottomLeft.x, bottomLeft.y, 0f);
        }

        private static float RawBadgeWidth(string value, int fontSize)
        {
            float width = 8f;
            foreach (char c in value) width += c >= 0x2E80 ? fontSize : fontSize * 0.55f;
            return width;
        }

        private static RectTransform PickRect(GameItemElement element)
        {
            string[] names = { "inner", "middle", "rotator" };
            foreach (string name in names)
            {
                TreeNodeRender node = Reflect.GetPropThenField<TreeNodeRender>(element, name);
                if (node == null || node.rectTransform == null) continue;
                Rect r = node.rectTransform.rect;
                if (r.width > 1f && r.height > 1f) return node.rectTransform;
            }

            TreeNodeRender handler = element.handler;
            if (handler != null && handler.rectTransform != null) return handler.rectTransform;
            return null;
        }

        /// <summary>
        /// Font resolution. The context-menu button font is the one verified in 1.5.0,
        /// but that handler only exists while a right-click menu is open, so the label
        /// scanner keeps `null` for the first badge of a session otherwise. The CJK
        /// asset scan and the TMP default are pure fallbacks and never override a font
        /// that already worked.
        /// </summary>
        private static TMP_FontAsset Font()
        {
            if (_font != null) return _font;

            try
            {
                ItemContextHandler handler = ItemContextHandler.current;
                if (handler != null)
                {
                    RichTextElement button = Reflect.GetField<RichTextElement>(handler, "buttonOpen");
                    if (button != null)
                    {
                        TreeNodeRender node = button.GetText();
                        if (node != null && node.text != null && node.text.font != null)
                        {
                            _font = node.text.font;
                            return _font;
                        }
                    }
                }
            }
            catch
            {
                // fall through
            }

            try
            {
                TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        TMP_FontAsset asset = all[i];
                        if (asset == null || string.IsNullOrEmpty(asset.name)) continue;

                        string name = asset.name;
                        if (name.IndexOf("SC", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("CN", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("Chinese", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("Han", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _font = asset;
                            StorageBayLabelMod.SafeLog("badge font (CJK): " + name);
                            return _font;
                        }
                    }
                }
            }
            catch
            {
                // fall through
            }

            try { _font = TMP_Settings.defaultFontAsset; } catch { }
            return _font;
        }
    }

    internal sealed class OverlayDriver : MonoBehaviour
    {
        private static bool _warned;

        private void LateUpdate()
        {
            try
            {
                Overlay.Tick();
            }
            catch (System.Exception e)
            {
                if (_warned) return;
                _warned = true;
                StorageBayLabelMod.SafeError("overlay tick failed: " + e.GetType().Name + " " + e.Message);
            }
        }
    }
}
