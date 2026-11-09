using System;
using System.Collections.Generic;
using System.Text;

namespace AutoUvLamp
{
    /// <summary>
    /// 水龙头槽位的效果实现：从水龙头接水时，如果槽位里装着紫外线灯，
    /// 就把这一瓶提到基础水（≈98%），并顺手杀一次菌；没装灯则一律不碰。
    ///
    /// 触发来源：src\WaterFillPatch.cs 的 [HarmonyPatch] 后置补丁（WaterHelper.AddWater）。
    /// 为什么是 AddWater 而不是 TransferLiquid：反编译拖拽回调 InitLiquidContainerItem 可看到两条分支——
    /// 容器↔容器走 TransferLiquid，水龙头→容器走 AddWater。接水只走后者。
    ///
    /// 用到的接口（均已对照本机 Assembly-CSharp.dll 核对签名）：
    ///   WaterHelper.PurifyToBaseWater(GameItem)                      按比例削减非水成分到基础水
    ///   WaterHelper.HandleFilter(string, int, GameItem, GameItem)    返回处理后的剩余份数（纯函数）
    ///   WaterHelper.IsDrain / IsFaucet                               排除非容器
    ///   WineHelper.CanModifyLiquid                                   排除发酵中/已酿成的酒瓶
    ///   1 份 = 1 µl
    /// </summary>
    internal static class WaterUv
    {
        const string ContainerTag = "LIQUID_CONTAINER_TAG";
        const string CapacityTag = "LIQUID_CONTAINER_CAPACITY";
        const string WineTag = "WINE_CONTAINER_TAG";

        /// <summary>基础水的下限（万分比 98.00%）。达到这个值就不再对水龙头的水做提升。</summary>
        const int FaucetTargetPurity = 9800;

        // Liquid 的静态构造会用 LocHelper 固化液体显示名；如果在本地化就绪前碰到它，
        // 整局会话的液体名会停在英文。所以只在真正的游戏内时机读取，并且读一次就缓存。
        static string _microbePart;
        static string _waterPart;
        static bool _partsResolved;

        // ---------------------------------------------------------------- 一次处理的结果

        /// <summary>单个液体的变化。</summary>
        sealed class LiquidChange
        {
            internal string Id;
            internal int Before;
            internal int After;
        }

        /// <summary>单个容器的处理结果，份数单位 1 份 = 1 µl，纯度单位万分比。</summary>
        sealed class TreatResult
        {
            internal int TotalBefore;
            internal int TotalAfter;
            internal int WaterBefore;
            internal int WaterAfter;
            internal int PurityBefore;     // 优先取游戏自己的 WaterHelper.GetWaterPurity
            internal int PurityAfter;
            internal int Removed;          // 被去掉的总份数
            internal int AddedByUs;        // 本 mod 补回水的份数
            internal bool GameConverted;   // 游戏自己已经把去掉的量补成了水
            internal readonly List<LiquidChange> Changes = new List<LiquidChange>();
        }

        // ---------------------------------------------------------------- 接水后处理

        /// <summary>
        /// 接水（WaterHelper.AddWater）之后：只有「水龙头的紫外线灯槽位里装着灯」才处理这一瓶。
        ///
        /// AddWater 同时被倒酒（WineHelper.PourWineIntoContainer / PourSpirit）和若干 FillWithXxx 使用，
        /// 所以先排除酒瓶，避免把酒精一起削减掉。
        /// </summary>
        internal static void OnWaterAdded(GameItem container)
        {
            if (container == null) return;

            // 酒 / 烈酒一律不碰：PurifyToBaseWater 按比例削减所有非水成分，会把酒精也削掉。
            if (IsWineContainer(container)) return;

            GameItem lamp = FaucetWindow.Lamp();
            if (lamp == null)
            {
                if (Settings.Verbose)
                    AutoUvLampMod.Info("faucet: slot is empty, water left untouched");
                return;
            }

            BoostFaucetWater(container);
            TreatSingle(container, lamp);
        }

        /// <summary>
        /// 把这一瓶直接提到基础水（约 98%）。
        ///
        /// 用的是游戏自己给便携式净水器用的函数 WaterHelper.PurifyToBaseWater：
        /// 它只改液体数据（按比例削减非水成分，直到水质达标），顺手更新瓶子的「推定水质」，
        /// 不消耗任何耐久、也不需要背包里有净水器 —— 耐久是在物品回调
        /// AmenitiesItemDirectory.OnPortableWaterPurifierUsed 里扣的，我们不走那条回调。
        ///
        /// 副作用要说清楚：它会连矿物质一起按比例削减。要拿这批水做水培的话，把灯从槽里拖出来即可。
        /// </summary>
        internal static void BoostFaucetWater(GameItem target)
        {
            if (target == null) return;

            try
            {
                if (!ResolveLiquidParts()) return;
                if (!IsWaterContainer(target)) return;

                int total, water;
                Measure(target, out total, out water);
                if (total <= 0) return;

                int before = PurityOf(target, water, total);
                if (before >= FaucetTargetPurity)
                {
                    if (Settings.Verbose)
                        AutoUvLampMod.Info("faucet boost: skipped " + Name(target) + ", already " + Pct(before) + "%");
                    return;
                }

                WaterHelper.PurifyToBaseWater(target);

                int totalAfter, waterAfter;
                Measure(target, out totalAfter, out waterAfter);
                int after = PurityOf(target, waterAfter, totalAfter);

                AutoUvLampMod.Info("faucet boost: " + Name(target) + " | purity " + Pct(before) + "% -> " + Pct(after)
                                   + "% | water " + Ml(water) + " -> " + Ml(waterAfter)
                                   + " ml | total " + Ml(total) + " -> " + Ml(totalAfter) + " ml");
            }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("faucet boost failed: " + e.Message);
            }
        }

        /// <summary>只对这一个容器杀一次菌（不扫全店）。</summary>
        static void TreatSingle(GameItem container, GameItem lamp)
        {
            try
            {
                if (!HasMicrobes(container)) return;

                bool usedFallback;
                TreatResult result = Process(container, lamp, out usedFallback);
                if (result == null) return;

                AutoUvLampMod.Info("  fill: " + Describe(container, result, usedFallback));
                if (Settings.Verbose) AutoUvLampMod.Info("    liquids: " + DescribeChanges(result));
            }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("faucet UV pass failed: " + e.Message);
            }
        }

        /// <summary>酒瓶（含发酵中与已酿成的）。</summary>
        static bool IsWineContainer(GameItem item)
        {
            try { return item.IsTag(WineTag); }
            catch { return false; }
        }

        /// <summary>把单个容器的处理结果拼成一行：识别值、微生物、纯度、水量、补水来源。</summary>
        static string Describe(GameItem item, TreatResult r, bool usedFallback)
        {
            var sb = new StringBuilder();
            sb.Append(Name(item));
            sb.Append(": microbes -").Append(Ml(r.Removed)).Append(" ml");
            sb.Append(" | purity ").Append(Pct(r.PurityBefore)).Append("% -> ").Append(Pct(r.PurityAfter)).Append("%");
            sb.Append(" | water ").Append(Ml(r.TotalBefore)).Append(" -> ").Append(Ml(r.TotalAfter)).Append(" ml");

            if (r.AddedByUs > 0)
                sb.Append(" | restored +").Append(Ml(r.AddedByUs)).Append(" ml");
            else if (r.GameConverted)
                sb.Append(" | game converted microbes to water");
            else if (!Settings.ConvertToWater)
                sb.Append(" | no restore (by setting)");

            if (usedFallback) sb.Append(" | direct");
            return sb.ToString();
        }

        /// <summary>逐液体的 before -> after，只在 verbose 时打印。</summary>
        static string DescribeChanges(TreatResult r)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < r.Changes.Count; i++)
            {
                var c = r.Changes[i];
                if (i > 0) sb.Append("; ");
                sb.Append(c.Id).Append(' ').Append(Ml(c.Before)).Append(" -> ").Append(Ml(c.After)).Append(" ml");
            }
            if (sb.Length == 0) sb.Append("(no liquid change)");
            return sb.ToString();
        }

        /// <summary>容器标识 + 容量，方便在日志里认出是哪一个。</summary>
        static string Name(GameItem item)
        {
            string id = item.identifier;
            try
            {
                var cap = item.GetTagReadonly(CapacityTag);
                if (cap != null && cap.GetInt() > 0) return id + " (" + Ml(cap.GetInt()) + " ml cap)";
            }
            catch { }
            return id;
        }

        /// <summary>份数 -> 毫升，1 份 = 1 µl，所以除以 1000。</summary>
        static string Ml(int parts)
        {
            return (parts / 1000.0).ToString("0.##");
        }

        /// <summary>万分比 -> 百分比文本。</summary>
        static string Pct(int permyriad)
        {
            return (permyriad / 100.0).ToString("0.00");
        }

        /// <summary>水容器：带液体容器标记与容量，排除排水口/水龙头，排除发酵中或已酿好的酒瓶。</summary>
        static bool IsWaterContainer(GameItem item)
        {
            try
            {
                if (!item.IsTag(ContainerTag)) return false;

                var cap = item.GetTagReadonly(CapacityTag);
                if (cap == null || cap.GetInt() <= 0) return false;

                if (WaterHelper.IsDrain(item) || WaterHelper.IsFaucet(item)) return false;

                if (item.IsTag(WineTag) && !WineHelper.CanModifyLiquid(item)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool HasMicrobes(GameItem item)
        {
            try
            {
                return !string.IsNullOrEmpty(_microbePart)
                       && item.IsTag(_microbePart)
                       && ReadParts(item, _microbePart) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 是不是微生物类液体。原版识别值是 microbe（词条键 liquid_microbes），
        /// 这里按前缀匹配，兼容以后可能出现的 microbe_xxx 之类变体。
        /// </summary>
        static bool IsMicrobeLiquid(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            return identifier.IndexOf("microb", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>处理一个容器，返回结果；什么都没清掉时返回 null。</summary>
        static TreatResult Process(GameItem container, GameItem lamp, out bool usedFallback)
        {
            usedFallback = false;
            string mode = Settings.Mode;

            if (mode == "direct")
            {
                usedFallback = true;
                return KillMicrobesDirectly(container);
            }

            TreatResult result = KillWithGameFilter(container, lamp);
            if (result != null) return result;

            // auto：游戏的滤芯函数什么都没清（例如它要求容器是滤水壶），改为直接清微生物。
            if (mode == "auto" && HasMicrobes(container))
            {
                usedFallback = true;
                return KillMicrobesDirectly(container);
            }
            return null;
        }

        /// <summary>
        /// 用游戏自己的滤芯逻辑（紫外线灯作为 filter 传入）处理微生物。
        /// HandleFilter 是纯函数：返回处理之后剩下的份数，自己不改物品，所以扣减与补水都由这里做。
        ///
        /// 只把「微生物」这一类液体交给它，其它成分一概不碰——社区里装过滤设备的模组被反馈最多的坑就是
        /// 「滤芯把矿物质/其它成分也一起去掉了，水培反而受损」。紫外线灯在原版只杀微生物，
        /// 这里就不给它机会去动别的东西。
        /// </summary>
        static TreatResult KillWithGameFilter(GameItem container, GameItem lamp)
        {
            int totalBefore, waterBefore;
            Measure(container, out totalBefore, out waterBefore);

            var r = new TreatResult { TotalBefore = totalBefore, WaterBefore = waterBefore };
            r.PurityBefore = PurityOf(container, waterBefore, totalBefore);

            foreach (var liquid in Liquid.Liquids)
            {
                if (liquid == null) continue;
                if (!IsMicrobeLiquid(liquid.identifier)) continue;

                string part = liquid.currentPart;
                if (string.IsNullOrEmpty(part)) continue;
                if (!container.IsTag(part)) continue;

                int amount = ReadParts(container, part);
                if (amount <= 0) continue;

                int remaining;
                try
                {
                    remaining = WaterHelper.HandleFilter(liquid.identifier, amount, container, lamp);
                }
                catch (Exception e)
                {
                    AutoUvLampMod.Warning("HandleFilter(" + liquid.identifier + ", " + Ml(amount) + " ml) failed: " + e.Message);
                    continue;
                }

                if (remaining < 0) remaining = 0;
                if (remaining >= amount) continue;

                if (!WriteParts(container, part, remaining)) continue;

                r.Changes.Add(new LiquidChange { Id = liquid.identifier, Before = amount, After = remaining });
                r.Removed += amount - remaining;
            }

            if (r.Removed <= 0) return null;

            int totalAfter, waterAfter;
            Measure(container, out totalAfter, out waterAfter);

            // 游戏若已经自己把被去掉的量补成了水，总量就不会按 Removed 下降，这时不要再补一次。
            r.GameConverted = totalBefore - totalAfter < r.Removed;

            if (Settings.ConvertToWater && !r.GameConverted)
            {
                r.AddedByUs = AddWater(container, r.Removed);
                Measure(container, out totalAfter, out waterAfter);
            }

            r.TotalAfter = totalAfter;
            r.WaterAfter = waterAfter;
            r.PurityAfter = PurityOf(container, waterAfter, totalAfter);
            return r;
        }

        /// <summary>直接清掉微生物液体（不经过游戏的滤芯函数）。</summary>
        static TreatResult KillMicrobesDirectly(GameItem container)
        {
            if (string.IsNullOrEmpty(_microbePart)) return null;
            if (!container.IsTag(_microbePart)) return null;

            int amount = ReadParts(container, _microbePart);
            if (amount <= 0) return null;

            int totalBefore, waterBefore;
            Measure(container, out totalBefore, out waterBefore);
            int purityBefore = PurityOf(container, waterBefore, totalBefore);

            if (!WriteParts(container, _microbePart, 0)) return null;

            var r = new TreatResult
            {
                TotalBefore = totalBefore,
                WaterBefore = waterBefore,
                PurityBefore = purityBefore,
                Removed = amount
            };
            r.Changes.Add(new LiquidChange { Id = _microbePart, Before = amount, After = 0 });

            int totalAfter, waterAfter;
            Measure(container, out totalAfter, out waterAfter);

            if (Settings.ConvertToWater)
            {
                r.AddedByUs = AddWater(container, amount);
                Measure(container, out totalAfter, out waterAfter);
            }

            r.TotalAfter = totalAfter;
            r.WaterAfter = waterAfter;
            r.PurityAfter = PurityOf(container, waterAfter, totalAfter);
            return r;
        }

        /// <summary>
        /// 纯度（万分比）：优先用游戏自己的 WaterHelper.GetWaterPurity，保证与试纸/扫描仪口径一致；
        /// 调用失败时退回「水占比」估算。
        /// </summary>
        static int PurityOf(GameItem container, int waterParts, int totalParts)
        {
            try
            {
                return WaterHelper.GetWaterPurity(container);
            }
            catch
            {
                return totalParts <= 0 ? 0 : (int)((long)waterParts * 10000 / totalParts);
            }
        }

        /// <summary>往容器里补 water 液体，返回实际补进去的份数。</summary>
        static int AddWater(GameItem container, int parts)
        {
            if (parts <= 0 || string.IsNullOrEmpty(_waterPart)) return 0;
            int now = container.IsTag(_waterPart) ? ReadParts(container, _waterPart) : 0;
            return WriteParts(container, _waterPart, now + parts) ? parts : 0;
        }

        // ---------------------------------------------------------------- 液体标签读写

        /// <summary>
        /// 取出 water / microbe 两种液体对应的标签名。Liquid 的静态构造会固化显示名，
        /// 所以这一步只在游戏内第一次真正需要时才做，并缓存结果。
        /// </summary>
        static bool ResolveLiquidParts()
        {
            if (_partsResolved) return !string.IsNullOrEmpty(_microbePart) && !string.IsNullOrEmpty(_waterPart);
            _partsResolved = true;

            try
            {
                foreach (var liquid in Liquid.Liquids)
                {
                    if (liquid == null || string.IsNullOrEmpty(liquid.identifier)) continue;
                    if (liquid.identifier == "water") _waterPart = liquid.currentPart;
                    else if (string.IsNullOrEmpty(_microbePart) && IsMicrobeLiquid(liquid.identifier))
                        _microbePart = liquid.currentPart;
                }
            }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("Reading liquid table failed: " + e.Message);
            }

            if (string.IsNullOrEmpty(_waterPart))
            {
                try { _waterPart = Liquid.GetCurrentPartFromId("water"); } catch { }
            }

            if (string.IsNullOrEmpty(_microbePart) || string.IsNullOrEmpty(_waterPart))
            {
                _partsResolved = false; // 失败时允许下次重试
                return false;
            }
            return true;
        }

        /// <summary>读某个液体标签的份数。先 IsTag 再读，避免给干净物品插入一堆 0 值标签。</summary>
        static int ReadParts(GameItem item, string part)
        {
            try
            {
                if (string.IsNullOrEmpty(part)) return 0;
                if (!item.IsTag(part)) return 0;
                var tag = item.GetTagReadonly(part);
                return tag == null ? 0 : tag.GetInt();
            }
            catch
            {
                return 0;
            }
        }

        static bool WriteParts(GameItem item, string part, int value)
        {
            try
            {
                item.ModifyTag(part, state => state.Enable().SetInt(value), false);
                return true;
            }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("Write tag " + part + " = " + value + " failed: " + e.Message);
                return false;
            }
        }

        /// <summary>统计一个容器的总份数与水份数（都只读已有标签）。</summary>
        static void Measure(GameItem item, out int total, out int water)
        {
            total = 0;
            water = 0;
            try
            {
                foreach (var liquid in Liquid.Liquids)
                {
                    if (liquid == null) continue;
                    string part = liquid.currentPart;
                    if (string.IsNullOrEmpty(part)) continue;
                    int value = ReadParts(item, part);
                    if (value <= 0) continue;
                    total += value;
                    if (liquid.identifier == "water") water += value;
                }
            }
            catch { }
        }
    }
}
