using System;

namespace AutoUvLamp
{
    /// <summary>
    /// 模组入口。游戏自带的模组载入器会先对本程序集做一次 Harmony PatchAll
    /// （接水拦截是属性补丁，见 WaterFillPatch，在那时挂上），再调用 Init / OnEnable；
    /// 停用或退出时调用 OnDisable。
    ///
    /// 注意：Init 与 OnEnable 都在主菜单里执行，那时还没有任何存档相关的对象
    /// （EmporiumEntry.Instance 还是 null），所以这里绝不去碰存档数据，
    /// 水龙头槽位交给 FaucetWindow 在 Update 里等到有商店了再建。
    /// </summary>
    public sealed class AutoUvLampMod : IMod
    {
        /// <summary>与 manifest.xml 的 ID 一致；ModConfig、ModHelper 都按它找这个 mod。</summary>
        internal const string ModId = "auto_uv_lamp";

        /// <summary>
        /// 日志前缀。游戏的 F8 控制台用 OfficeCodePro 字体，**没有中文字形**，
        /// 中文前缀会被渲染成一串空格（看起来像没有输出），所以日志侧固定用纯 ASCII。
        /// Mods 列表里显示的名字仍是 manifest.xml 里的中文名。
        /// </summary>
        const string LogName = "UV Faucet";

        internal static ModLog Log;

        public void Init(ModManifest manifest)
        {
            // ModLog 的构造函数会把 manifest.Name 复制进自己的 modName 字段（之后不再读 manifest），
            // 所以这里临时换成 ASCII 名构造日志，再立刻恢复 —— 列表里的中文名不受影响。
            string displayName = null;
            try
            {
                displayName = manifest.Name;
                manifest.Name = LogName;
            }
            catch { }

            Log = new ModLog(manifest);

            try
            {
                if (displayName != null) manifest.Name = displayName;
            }
            catch { }

            try
            {
                Settings.Init();
            }
            catch (Exception e)
            {
                Warning("Failed to load settings, using defaults: " + e.Message);
            }

            Log.Log("Loaded. " + Settings.Describe());
        }

        public void OnEnable()
        {
            // 水龙头槽位：驱动在 Update 里等到有存档的水龙头再建窗口，主菜单阶段是安全的。
            FaucetWindow.Attach();
        }

        public void OnDisable()
        {
            FaucetWindow.Detach();
        }

        internal static void Info(string message)
        {
            if (Log != null) Log.Log(message);
        }

        internal static void Warning(string message)
        {
            if (Log != null) Log.Warning(message);
        }
    }
}
