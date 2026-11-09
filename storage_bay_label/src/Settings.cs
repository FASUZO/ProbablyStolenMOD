using System;

namespace Sbl
{
    /// <summary>
    /// Native loader settings. Registered in Init() through ModConfig.For(modId);
    /// the Mods panel then shows a settings button for this mod automatically.
    /// Every value is read live (no restart required) by the patch / overlay code.
    /// </summary>
    internal static class Settings
    {
        internal static ConfigEntry<bool> ShowBadge;
        internal static ConfigEntry<bool> ShowTooltipSuffix;
        internal static ConfigEntry<bool> LabelAllContainers;
        internal static ConfigEntry<string> BadgePosition;
        internal static ConfigEntry<int> FontScale;
        internal static ConfigEntry<int> BackgroundOpacity;
        internal static ConfigEntry<bool> TextShadow;
        internal static ConfigEntry<int> MaxLabelLength;
        internal static ConfigEntry<bool> BaySkins;

        private static bool _zh;

        internal static void Init(string modId)
        {
            string locale = null;
            try { locale = LocHelper.GetCurrentLocaleCode(); } catch { }
            _zh = !string.IsNullOrEmpty(locale) && locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

            ModConfig cfg = ModConfig.For(modId);
            string badge = L("角标", "Badge");
            string general = L("常规", "General");

            ShowBadge = cfg.Define(
                "show_badge", true, badge,
                () => L("在图标上显示标签", "Show label on icon"),
                () => L("关闭后标签仍会保存在存档里，只是不再画在图标上。", "Hidden labels are still saved."),
                false);

            ShowTooltipSuffix = cfg.Define(
                "show_tooltip_suffix", true, general,
                () => L("悬停介绍显示名称后缀", "Show suffix in tooltip"),
                () => L("把标签追加到悬停介绍的名称行后面。", "Append the label to the tooltip name line."),
                false);

            LabelAllContainers = cfg.Define(
                "label_all_containers", true, general,
                () => L("其它容器也可打标签", "Label other containers"),
                () => L("开启后，凡双击能打开窗口的物品（冰箱、酒架、储水罐、保险箱、补给箱等）都能打标签；关闭则只限存储舱。",
                       "Allow labels on every item that opens a container window, not just storage bays."),
                false);

            BadgePosition = cfg.Define(
                "badge_position", "bottom-left", badge,
                () => L("角标位置", "Badge position"),
                new[] { "bottom-left", "top-left", "bottom-right", "top-right" },
                () => L("左下 / 左上 / 右下 / 右上", "bottom-left / top-left / bottom-right / top-right"),
                false);

            FontScale = cfg.Define(
                "font_scale", 100, badge,
                () => L("字号缩放 (%)", "Font scale (%)"),
                (int?)60, (int?)160,
                () => L("100 = 跟随图标尺寸自动计算。", "100 = automatic size from the icon."),
                false);

            BackgroundOpacity = cfg.Define(
                "background_opacity", 55, badge,
                () => L("底板不透明度 (%)", "Badge opacity (%)"),
                (int?)0, (int?)100,
                () => L("0 = 完全透明，只留文字与描边。", "0 = transparent background."),
                false);

            TextShadow = cfg.Define(
                "text_shadow", true, badge,
                () => L("文字阴影", "Text shadow"),
                () => L("在文字下方偏移一层深色字做阴影，小字号在浅色贴图上更清楚。", "Offset dark copy behind the badge text."),
                false);

            MaxLabelLength = cfg.Define(
                "max_label_length", 12, general,
                () => L("标签最大字数", "Max label length"),
                (int?)4, (int?)16,
                () => L("超过时自动截断。", "Longer input is truncated."),
                false);

            BaySkins = cfg.Define(
                "bay_skins", true, general,
                () => L("存储舱外观贴图", "Storage bay skins"),
                () => L("三套外观变体贴图（军绿 / 木箱 / 钢蓝）。", "Three appearance variants."),
                false);

            cfg.Save();
        }

        internal static int LabelLimit()
        {
            int value = MaxLabelLength != null ? MaxLabelLength.Value : 12;
            if (value < 4) value = 4;
            if (value > 16) value = 16;
            return value;
        }

        internal static string L(string zh, string en)
        {
            return _zh ? zh : en;
        }
    }
}
