using HarmonyLib;

namespace AutoUvLamp
{
    /// <summary>
    /// 接水拦截。
    ///
    /// 之前只拦 TransferLiquid 是不够的：反编译 WaterHelper/&lt;&gt;c__DisplayClass10_0
    /// 的拖拽回调 &lt;InitLiquidContainerItem&gt;b__9 可以看到两条分支——
    ///   容器 ↔ 容器：调用 WaterHelper.TransferLiquid(sourceContainer, targetContainer)
    ///   水龙头 → 容器：调用 WaterHelper.AddWater(waterContainer, grade, amount, useRange, low, high, initFeature)
    /// 所以从水龙头接水时 TransferLiquid 根本不会被调用，必须单独拦 AddWater。
    ///
    /// AddWater 的签名（7 个参数，只有一个重载）：
    ///   void AddWater(GameItem waterContainer, int grade, int addAmount, bool useRange,
    ///                 int lowPara, int highPara, bool initWaterFeature)
    /// 后置只取第一个参数（目标容器），其余不关心。
    /// </summary>
    [HarmonyPatch(typeof(WaterHelper), "AddWater")]
    internal static class WaterFillPatch
    {
        [HarmonyPostfix]
        static void Postfix(GameItem waterContainer)
        {
            WaterUv.OnWaterAdded(waterContainer);
        }
    }
}
