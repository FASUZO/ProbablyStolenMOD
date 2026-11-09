using HarmonyLib;

namespace AutoUvLamp
{
    /// <summary>
    /// 游戏序列化 PlayerStore 之前，先把「水龙头槽位里装了什么」写进 modData。
    ///
    /// 为什么需要：槽位状态靠每 2 秒一次的定时 Save() 落到 PlayerStore.modData，
    /// 而存档是随时可能发生的 —— 尤其是过夜流程里 EndNight() 之后紧接着就是 SaveGame()。
    /// 定时器没赶上时，刚装配/卸下的灯就不会进盘，重进后状态回滚。
    ///
    /// 这是无参前置补丁，不注入任何目标方法参数，匹配失败的风险最低。
    /// </summary>
    [HarmonyPatch(typeof(PlayerStore), "SaveGame")]
    internal static class FaucetSaveGamePatch
    {
        static void Prefix()
        {
            FaucetWindow.SaveNow();
        }
    }
}
