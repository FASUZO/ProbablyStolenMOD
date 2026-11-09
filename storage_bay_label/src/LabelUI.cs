using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Sbl
{
    /// <summary>
    /// Adds label entries to the vanilla right-click menu by repurposing buttons the
    /// menu already hides for container items (Configure / Toggle / Unload / Pin).
    /// Borrowing a hidden serialized button avoids rebuilding GridPixelElement's
    /// parallel child arrays and keeps the native look.
    /// </summary>
    internal static class LabelUI
    {
        // Ordered by preference: container-ish buttons first (they are hidden for most
        // storage items), then the generic equip/activate/use slots, which stay hidden
        // on machines and containers. Machines show Configure/Toggle natively, so the
        // tail of this list is what keeps a fridge from having "no free button".
        private static readonly string[] CandidateFields =
        {
            "buttonConfigure", "buttonToggle", "buttonUnload", "buttonPin",
            "buttonActivate", "buttonEquip", "buttonUse"
        };

        private static readonly Dictionary<RichTextElement, string> OriginalTexts =
            new Dictionary<RichTextElement, string>();

        private static RichTextElement _active;   // edit / set tag
        private static RichTextElement _clear;    // clear tag (only when one exists)
        private static GameItem _activeItem;

        private static readonly bool Verbose = false;

        internal static void Refresh(ItemContextHandler handler)
        {
            try
            {
                if (handler == null) return;

                GameItem item = Reflect.GetField<GameItem>(handler, "currentItem");
                bool wanted = Labels.IsLabelable(item);

                if (!wanted)
                {
                    RestoreText(_active);
                    RestoreText(_clear);
                    _active = null;
                    _clear = null;
                    _activeItem = null;
                    return;
                }

                bool hasLabel = !string.IsNullOrEmpty(Labels.Get(item));
                _activeItem = item;

                if (_active == null || _active.handler == null)
                {
                    _active = BorrowFirstHidden(handler);
                    if (_active == null)
                    {
                        StorageBayLabelMod.SafeWarning("no hidden context button: " + DescribeButtons(handler));
                        return;
                    }
                }

                _active.handler.gameObject.SetActive(true);
                ApplyText(handler, _active, MenuWord(hasLabel));

                // A second hidden slot is optional: when it is not available the input
                // dialog's clear (X) button still removes the label.
                if (hasLabel)
                {
                    if (_clear == null || _clear.handler == null)
                    {
                        _clear = BorrowFirstHidden(handler, _active);
                    }
                    if (_clear != null && _clear.handler != null)
                    {
                        _clear.handler.gameObject.SetActive(true);
                        ApplyText(handler, _clear, ClearWord());
                    }
                }
                else if (_clear != null)
                {
                    RestoreText(_clear);
                    if (_clear.handler != null) _clear.handler.gameObject.SetActive(false);
                    _clear = null;
                }
            }
            catch (Exception e)
            {
                StorageBayLabelMod.SafeError("context menu refresh failed: " + e.Message);
            }
        }

        /// <summary>
        /// UpdateSelectButtonColor rewrites every caption from its localization key
        /// after UpdateConditions, so the borrowed captions have to be reapplied.
        /// </summary>
        internal static void Reapply(ItemContextHandler handler)
        {
            if (_active == null || _activeItem == null) return;

            bool hasLabel = !string.IsNullOrEmpty(Labels.Get(_activeItem));
            ApplyText(handler, _active, MenuWord(hasLabel));
            if (_clear != null) ApplyText(handler, _clear, ClearWord());
        }

        internal static bool TryHandleClick(ItemContextHandler handler, RichTextElement button)
        {
            if (button == null) return false;

            GameItem item = _activeItem;
            if (item == null || !Labels.IsLabelable(item)) return false;

            if (ReferenceEquals(button, _clear))
            {
                CloseMenu(handler);
                Apply(item, string.Empty);
                return true;
            }

            if (ReferenceEquals(button, _active))
            {
                CloseMenu(handler);
                BeginEdit(item);
                return true;
            }

            return false;
        }

        private static RichTextElement BorrowFirstHidden(ItemContextHandler handler, RichTextElement skip = null)
        {
            foreach (string field in CandidateFields)
            {
                RichTextElement button = Reflect.GetField<RichTextElement>(handler, field);
                if (button == null || ReferenceEquals(button, skip)) continue;

                // Vanilla hides a button with button.handler.gameObject.SetActive(false).
                // The node returned by GetText() is a CHILD of that root, so its own
                // activeSelf stays true and must not be used for this test.
                TreeNodeRender root = button.handler;
                if (root == null || root.gameObject.activeSelf) continue;

                RememberOriginal(button);
                root.gameObject.SetActive(true);
                return button;
            }

            return null;
        }

        private static void BeginEdit(GameItem item)
        {
            LabelerBlueUIManager ui = FindLabeler();
            if (ui == null)
            {
                StorageBayLabelMod.SafeError("labeler input panel is not loaded in this scene");
                return;
            }

            string current = Labels.Get(item) ?? string.Empty;

            ui.OpenUI(
                current,
                text => Apply(item, text),
                () => Apply(item, string.Empty),
                Sbl.Settings.LabelLimit());
        }

        private static LabelerBlueUIManager FindLabeler()
        {
            LabelerBlueUIManager ui = LabelerBlueUIManager.Instance;
            if (ui != null) return ui;

            try
            {
                LabelerBlueUIManager[] all = Resources.FindObjectsOfTypeAll<LabelerBlueUIManager>();
                if (all != null && all.Length > 0) return all[0];
            }
            catch
            {
                // report below
            }

            return null;
        }

        private static void Apply(GameItem item, string text)
        {
            string value = (text ?? string.Empty).Trim();
            int limit = Sbl.Settings.LabelLimit();
            if (value.Length > limit) value = value.Substring(0, limit);

            Labels.Set(item, value);

            GameItemElement element = item as GameItemElement;
            if (element != null) Overlay.Refresh(element);
            Overlay.RefreshAll();

            // Vanilla OnEditEnd leaves the panel open; closing it makes Enter read as "done".
            LabelerBlueUIManager ui = FindLabeler();
            if (ui != null) ui.CloseUI();

            if (Verbose) StorageBayLabelMod.SafeLog(value.Length == 0 ? "label cleared" : "label stored");
        }

        private static void CloseMenu(ItemContextHandler handler)
        {
            try
            {
                PixelWindow window = Reflect.GetField<PixelWindow>(handler, "contextWindow");
                if (window != null) window.Hide();
            }
            catch (Exception e)
            {
                StorageBayLabelMod.SafeError("hide context menu failed: " + e.Message);
            }

            try
            {
                Reflect.Invoke(handler, "OnEventReset");
            }
            catch (Exception e)
            {
                StorageBayLabelMod.SafeError("reset context handler failed: " + e.Message);
            }
        }

        private static bool IsChinese()
        {
            string locale = null;
            try { locale = LocHelper.GetCurrentLocaleCode(); } catch { }
            return !string.IsNullOrEmpty(locale) && locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private static string MenuWord(bool hasLabel)
        {
            if (IsChinese()) return hasLabel ? "编辑标签" : "设置标签";
            return hasLabel ? "Edit Tag" : "Set Tag";
        }

        private static string ClearWord()
        {
            return IsChinese() ? "清除标签" : "Clear Tag";
        }

        private static TreeNodeRender SafeNode(RichTextElement button)
        {
            try { return button.GetText(); } catch { return null; }
        }

        private static void RememberOriginal(RichTextElement button)
        {
            if (button == null || OriginalTexts.ContainsKey(button)) return;
            TreeNodeRender node = SafeNode(button);
            if (node != null && node.text != null) OriginalTexts[button] = node.text.text;
        }

        private static void RestoreText(RichTextElement button)
        {
            string original;
            if (button == null || !OriginalTexts.TryGetValue(button, out original)) return;

            TreeNodeRender node = SafeNode(button);
            if (node != null && node.text != null && original != null && node.text.text != original)
            {
                node.text.text = original;
            }
        }

        /// <summary>
        /// Prefers the native RichTextElement.SetText(text, maxWidth, palette) path so
        /// the caption keeps the menu's own font and enabled colour.
        /// </summary>
        private static void ApplyText(ItemContextHandler handler, RichTextElement button, string word)
        {
            try
            {
                FieldInfo paletteField = typeof(ItemContextHandler).GetField(
                    "textColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (paletteField != null)
                {
                    MethodInfo setText = typeof(RichTextElement).GetMethod(
                        "SetText",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(string), typeof(int), paletteField.FieldType },
                        null);

                    if (setText != null)
                    {
                        setText.Invoke(button, new[] { (object)word, (object)10000, paletteField.GetValue(handler) });
                        return;
                    }
                }
            }
            catch
            {
                // fall through to the raw TMP path
            }

            TreeNodeRender node = SafeNode(button);
            if (node == null || node.text == null) return;
            if (node.text.text != word) node.text.text = word;
        }

        /// <summary>Diagnostic summary for the "no free button" path.</summary>
        private static string DescribeButtons(ItemContextHandler handler)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string field in CandidateFields)
            {
                if (sb.Length > 0) sb.Append(' ');

                RichTextElement button = Reflect.GetField<RichTextElement>(handler, field);
                sb.Append(field.Replace("button", string.Empty).ToLowerInvariant()).Append('=');

                if (button == null) { sb.Append("null"); continue; }
                TreeNodeRender root = button.handler;
                sb.Append(root == null ? "noroot" : (root.gameObject.activeSelf ? "1" : "0"));
            }
            return sb.ToString();
        }
    }
}
