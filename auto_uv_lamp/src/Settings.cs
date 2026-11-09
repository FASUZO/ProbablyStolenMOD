using System;

namespace AutoUvLamp
{
    /// <summary>
    /// 设置项，走游戏官方的 ModConfig（存在存档目录的 ModConfigs\auto_uv_lamp.json，
    /// 游戏「模组」菜单里本模组的详情页有设置面板）。
    ///
    /// 只保留「接水后的杀菌方式」相关三项：
    /// 是否接水提纯、水龙头槽位里装什么，全部由玩家在游戏里操作，不放在这里。
    ///
    /// 标签与说明直接给中文字面量：段落标题游戏不翻译，只有标签是 Func&lt;string&gt;，
    /// 用字面量最稳，不依赖 Localization 的 csv 格式。
    /// </summary>
    internal static class Settings
    {
        const string Behaviour = "Behaviour / 处理方式";

        static ConfigEntry<bool> _convert;
        static ConfigEntry<bool> _verbose;
        static ConfigEntry<string> _mode;

        internal static void Init()
        {
            var cfg = ModConfig.For(AutoUvLampMod.ModId);

            _mode = cfg.Define("mode", "auto", Behaviour,
                () => "杀菌方式",
                new[] { "auto", "filter", "direct" },
                () => "auto：先用游戏自己的紫外线灯逻辑（WaterHelper.HandleFilter），它没清掉东西时改为直接清微生物。" +
                      "filter：只用游戏逻辑。direct：直接清微生物，不经过游戏的滤芯函数。");

            _convert = cfg.Define("convert_to_water", true, Behaviour,
                () => "微生物转成水（水量不减）",
                () => "开：微生物被杀掉后按等量算回水，水量不减，和把灯直接拖到瓶子上一样。" +
                      "关：微生物直接消失、水量略减，和在滤水壶里用灯一样。");

            _verbose = cfg.Define("verbose_log", false, Behaviour,
                () => "详细日志",
                () => "把每个被处理的容器写进日志（F8 控制台或存档目录的 Player.log），排查问题时打开。");
        }

        // 设置窗口还没建好时按默认值走。
        internal static bool ConvertToWater => _convert == null || _convert.Value;
        internal static bool Verbose => _verbose != null && _verbose.Value;

        internal static string Mode
        {
            get
            {
                if (_mode == null || string.IsNullOrEmpty(_mode.Value)) return "auto";
                string m = _mode.Value;
                if (m == "filter" || m == "direct") return m;
                return "auto";
            }
        }

        internal static string Describe()
        {
            return "faucetSlot=on | mode=" + Mode + " | convert=" + On(ConvertToWater);
        }

        static string On(bool value) => value ? "on" : "off";
    }
}
