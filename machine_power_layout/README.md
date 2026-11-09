# 机器区供电布局（Machine Power Layout）

给《深空当铺：可能是偷的》（Probably Stolen 0.46D）写的 mod：**一键把机器区的能量阵列搬到最优相邻位**，让机器的夜间回电覆盖最大化；也可以把机器一起重排。

对应你给的参考工程 `最优解布局/`：那份 `layout_config.json` 的"场地 = 14×11 网格、A = 能量阵列、其余物品带负基础分、每条接触 +2"就是本 mod 要解的问题在游戏里的真实形态。

---

## 1. 游戏机制（全部来自反编译，非猜测）

| 事实 | 证据 |
|---|---|
| 能量阵列（PowerBloc，精灵 `powerblock`）在**过夜结算**时触发 | `FurnitureItemDirectory.CreatePowerBlock` 把 `onCycleEndEarlySlotItemFunc` 指向 `<>c__DisplayClass5_0::<CreatePowerBlock>b__0` |
| 单台阵列给**相邻**目标充 **2** 点；解锁商用供电（`NetworkUpgrade.IsCommercialPowerUnlocked()`）后为 **4** | 同上 IL：`ldc.i4.2` / 分支后 `ldc.i4.4` |
| 目标只有两类：散装电池（`power_source_item`）与可充电机器（`MACHINE_RECHARGABLE_TAG`） | 同上 IL 的两个分支：`PowerHelper.ChargeBattery` / `MachineHelper.RechargeMachineBattery` |
| 多台阵列**逐台独立结算**（可叠加），充能器不算目标 | 同上，循环内每台阵列各自调用一次 |
| "相邻"= 同一格子容器内、**上下左右共边**（对角不算），一对物品只算一次 | `GameItem.FindAdjacentType<T>()` → `GameInventory.IsAdjacent(SlotMarker, SlotMarker)`，其实现在 `GameGridInventory.IsAdjacent` 中把形状平移 ±1 后做相交测试 |
| 机器每晚耗电写在物品标签 `machinery_drawn_power`（整数） | `ToolDirectory.CreateTurboBoosterAdv` 等创建流程里 `ModifyTag("machinery_drawn_power", …)` |
| 机器区就是一个 `GameGridInventory`；阵列与机器必须同容器才会互相判定相邻 | `IsAdjacent` 先要求双方都在本容器的 `childItems` 中 |
| 断电状态（欠费）下不充电 | 回调开头 `PlayerStore.Instance.isPowerOn` 为假时只切回暗色精灵并直接返回 |

因此计分与优化目标就是：**每台机器"相邻阵列数 × 每台充电量"要 ≥ 它每晚的耗电**。

---

## 2. 安装与激活

1. 关闭游戏。
2. 双击 `build.bat`（或 `build.bat "D:\...\Probably Stolen"`）编译并安装到 `<游戏>\Mods\machine_power_layout\`。
   - 已经编译过、只想更新安装（例如游戏占用过 DLL）：**先关游戏**，再双击 `install.bat`。
   - 也可手工复制：`bin\Release\MachinePowerLayout.dll` + `manifest.xml` 两个文件放在同一目录。
3. 启动游戏 → 主菜单 **Mods** → 选中「机器区供电布局」→ **Activate** → **再重启一次**（激活与重启是两步）。
4. 进店后按 **F7**（默认）。

日志核对（F8 控制台不镜像历史日志，请看文件）：

```
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log
```

搜 `PowerLayout` 即可。

---

## 3. 使用

在店里按 F7：

- 自动定位"机器区"网格（在所有候选格子容器里选机器数最多的那个）。
- 读取每台机器的耗电、电池余量、每台阵列的充电量。
- 求一个布局，把阵列搬到收益最高的位置；`FullRelayout` 模式下机器也会被重排。
- 应用时先全部移出、再按"大件优先（机器先、阵列后）"重放；任何一步被游戏拒绝就**整体回滚**。
- 完成后左上角 5 秒提示 + `Player.log` 里输出前后对比与逐台机器明细。

`ArraysOnly`（默认）只搬阵列，安全、瞬间；机器摆得太满（机器之间只隔 1 格）时阵列挤不进去，收益会有限——这时才需要 `FullRelayout`。

### 日志样例

```
[run] grid 14x11 (candidate #2) machines=7 arrays=10 charge=2 mode=ArraysOnly auto=False
[before] covered 3/7 | unpowered 4 | deficit 22/night | waste 0 | battery+0
per-machine night balance (each adjacent array restores 2):
  furnace                draw=2   arrays=0  supply=0   net=-2   battery=8/40 runway=16n
  ...
[plan] restarts=71 kicks=1772 ms=401 | covered 6/7 | unpowered 1 | deficit 4/night | moves=9
[apply] moved 9 items in 431ms
[verify] covered 6/7 | unpowered 1 | deficit 4/night
```

`[verify]` 是**重新从实际网格读取**后独立算出的结果，不依赖求解器内部状态；`apply` 后若有个别物品没落到目标格，会额外打印 `N of M items did not land on the requested cell`。

---

## 4. 配置

游戏内 Mods →「机器区供电布局」的设置页（`ModConfigs/machine_power_layout.json`）。

| 键 | 默认 | 说明 |
|---|---|---|
| `hotkey` | `F7` | 触发键（F5–F11） |
| `mode` | `ArraysOnly` | `ArraysOnly` 只挪阵列；`FullRelayout` 连机器一起重排 |
| `dry_run` | `false` | 只算不落地，方案写日志 |
| `allow_rotation` | `false` | 允许矩形机器旋转 90°（净水器 3×4 ↔ 4×3） |
| `charge_per_array` | `0` | 0 = 自动（2 或商用 4）；可强制覆盖 |
| `budget_ms` | `350` | 单次求解墙钟上限，超时输出当前最优 |
| `include_batteries` | `true` | 散装电池也算目标（吃余量） |
| `auto_before_sleep` | `false` | 每晚 `OnGoingSleepEarly` 自动跑一次 |
| `verbose` | `true` | 输出布局图与逐台明细 |
| `show_toast` | `true` | 屏幕提示 |

---

## 5. 算法

目标函数（一个尺度内实现字典序，量级刻意拉开）：

```
value = -(1e6 × 未覆盖机器数) - (1e3 × 每晚总缺口) - 0.5 × 溢出 + 0.8 × 电池充电量 - 0.02 × 移动件数
```

> 与参考脚本的对应：`neg_count` → 未覆盖机器数；`neg_penalty_sum` → 每晚总缺口；`-total_score` → 溢出/电池项；额外加"移动件数"作为稳定项，避免为了 +0.5 分把整个仓库抖一遍。

搜索：

1. **基线**：先以当前布局（churn = 0）作为候选最优解，任何方案都必须严格优于它，"已是最优"时不会白搬。
2. **构造**：`ArraysOnly` 按面积降序贪心；`FullRelayout` 按耗电降序排机器，并交替用两种种子——
   - 贪心：每件取当前最优位置；
   - 货架式：机器之间留 **2 格走廊**后铺开，再把阵列塞进走廊（2×2 阵列只有贴着机器放才可能同时贴到 2–4 台机器）。
   - 构造全部失败（格子太满）时不再报错，而是退化为"以当前布局为种子就地优化"。
3. **局部搜索**：逐件穷举所有可放位置（含旋转姿态）做最优改进；`FullRelayout` 追加同尺寸机器的 **2-opt 互换**（"哪台机器占哪个位置"影响很大）。
4. **迭代局部搜索**：随机踢动 2–4 件（70% 贴着随机目标机器找位置，30% 全图随机）→ 重新局部搜索 → 只留改进，最多 25 次踢动/轮（就地模式 150 次），多轮重启到时间预算耗尽。
5. **应用与回滚**：见下。

复杂度上：14×11 网格、10 台机器 + 10 台阵列时，默认 350 ms 预算内可跑数十轮重启；测试里 100 ms→4000 ms 的解质量曲线已经收敛。

应用流程（与创意工坊 `ModuleSort` 的做法一致，这是唯一被验证过的落位路径）：

```csharp
inv.Expel(item);                                  // 先全部移出
item.modifiedShape = new GridShapeBuilder(item.modifiedShape)
                        .SetOrientation(ori).SetPosition(gx, gy).shape;
if (!inv.CheckShapePlacement(item, shape, new List<GameItem>())) → 回滚
inv.UncheckedAccept(item);
inv.Validate();
```

---

## 6. 离线验证（无需开游戏）

`tools/solver_test` 是独立的控制台工程，链接同一份 `src/*.cs`，用合成网格跑 7 个场景：

```bash
cd tools/solver_test
dotnet build -c Release -p:GameDir="D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
./bin/Release/SolverTest.exe          # 场景回归：合法性 + 单调改善
./bin/Release/SolverTest.exe --bench  # 时间预算 vs 解质量
```

断言覆盖：所有机器仍在、布局无越界/无重叠/不压障碍格、未覆盖机器数与总缺口都不增加、结果是"最优或改进"。

当前结果（合成场景）：真实耗电（1–3/晚）下 `FullRelayout` 能做到 **5/5 覆盖、缺口 0**；`ArraysOnly` 受机器固定摆位限制，3/5。手工构造的高耗电场景（8–16/晚）物理上无法全清零，符合预期。

---

## 7. 已知限制

- **只能优化"单个格子容器"内的布局。** 跨容器（例如把机器分散到两个容器）不产生相邻，游戏本身也不认。
- `ArraysOnly` 的收益受机器摆位限制；**格子全满时它无法改进**（没有空位，阵列只能在等价位置之间互换，见下节）。这不是失败，是物理限制——请改用 `FullRelayout`。
- `FullRelayout` 需要足够空格容纳"移出—重放"过程；空间不足会回滚并提示，不会留下半成品。
- 求解为启发式，不保证全局最优（与参考脚本同样的声明）。
- 正在运行的机器被移动不会中断其进度（移动只改格子位置，不动内部库存），但建议在开店前或夜晚前执行。
- 修改的是游戏自己的网格，**随存档正常保存**，无需 mod 侧持久化。

---

## 8. v1.1.0 / v1.2.0：修复 "grid too full"

### 问题

旧版把"重建布局"当作唯一路径：先把所有可动件全部移出，再按贪心重新摆回。只要重建失败就整体放弃并提示 `grid too full`——即使当前布局本身合法、且周围还有 27 个空格。

### 根因：错把 `inventoryShape` 当"可用格掩码"

诊断输出给出了决定性证据：

```
grid 11x14 origin=(0,0) items=13 movable=13
cells=154 usable=0          <-- 所有格子都被判为不可用
```

`GameGridInventory.inventoryShape.Get(x, y)` 对**店内网格的每一个格子都返回 0**。它不是"可用格掩码"，而是别的东西（`1` 的含义只对物品形状成立）。旧版用 `Get(...) == 0 → 阻挡`，于是整张网格变成禁区：任何位置都不合法 → 构造瞬间失败 → 退化成"原地优化" → 全满假象下 0 移动。

`[diag]` 里 `powerblock_on at(9,8) 2x2` 之类的行同时证明物品是矩形、朝向正常，问题纯粹出在可用区判定。

### 修改（三处）

| 修改 | 说明 |
|---|---|
| 规划区域 = **物品实际占据的包络** | 包络内每一格都有物品坐实，必然可用；不再依赖 `inventoryShape` 的范围（它 `globalHeight=14`，实际只有 11 行有物品） |
| 掩码**仅在它确实带数据时**才采信 | `filled = Σ(Get != 0)`；`filled == 0` 时忽略掩码，整块矩形视为可用。另有否决项：若掩码把"已有物品的格子"标成阻挡，直接丢弃该掩码 |
| 构造失败仍有兜底 | `SeedCurrent()` 以当前布局为种子就地优化；`Solve()` 先评估"什么都不动"的基线，避免空转 |

`[diag]` 现在额外打印 `shape w/h / global / min / max / filled`，下次任何几何异常都能直接定位。

### 用你的真实存档验证

从 `Player.log` 复刻布局（11 宽、6 机器、7 阵列、商用供电 +4/台），分别按"日志里的假象（27 格被误判阻挡）"和"修复后的真实模型（11×11，27 格可用）"各跑一遍：

| 场景 | 旧判定 | 修复后 |
|---|---|---|
| H：假象模型 + ArraysOnly | `5/6, deficit 4, waste 40`，0 移动 | 0 移动（无误报，允许"不动") |
| I：假象模型 + FullRelayout | 0 移动 | **`6/6, deficit 0`** |
| J：真实模型 + ArraysOnly | — | **`6/6, deficit 0`**，4 次移动，221 ms |
| K：真实模型 + FullRelayout | — | **`6/6, deficit 0`**，7 次移动，406 ms |

结论：你的 `waste 40` 是**供给总量过剩（76 供给 / 40 需求）但分布错位**（投影仪差 4，其余机器溢出 13/8/4/11/4）。修复后普通模式（只搬阵列）即可把投影仪补满，达到 6/6。

---

## 9. v1.3.0：多面板 + 交换算子

### 两个机器区面板（"和优化布局严重不符"的主因）

游戏里存在多个「机器区（扩展）」面板，每个都是**独立的格子容器**，相邻判定只在同一容器内成立。旧版 `Locate()` 只返回"机器数最多的那一个"，另一个面板从头到尾没被处理。

现在 `GridModel.MachineAreas()` 返回**所有含机器的网格**（按机器数排序），`Runner` 逐个优化，预算按面板数分摊（`budget / 面板数`，下限 120 ms），每块面板单独输出带 `[panel i/N]` 前缀的 `before/plan/apply/verify`，最后汇总一行 `[done] panels=N covered X/Y`。

### 阵列交换算子（满格下唯一可行的移动）

旧版只有"单件迁移"，必须有空格子。你的机器区几乎没有连续空位，所以第二次按 F7 必然 `moves=0`。新增 `SwapMovables()`：互换两个**同尺寸**可动件的位置——不需要任何空格、天然合法，并且能把"贴满某台机器"的阵列调去补另一台。

局部搜索每轮顺序：单件最优迁移 → **同尺寸互换** → 机器 2-opt（仅 FullRelayout）；ILS 踢动有 45% 概率改为随机交换。

### 目标函数

新增 `MaxDeficit`（单台最严重缺口）作为次级平局判据：

```
value = -(1e6 × 未覆盖台数) - (1e3 × 每晚总缺口) - (1e2 × 最大单台缺口)
        - 0.5 × 溢出 + 0.8 × 电池充电量 - 0.02 × 移动件数
```

### 用你卡住的状态验证

复刻日志里"已搬过、停在 5/6"的真实布局（11x11、13 件、投影仪 3 阵列差 4）作为用例 L/M：

| 场景 | 结果 |
|---|---|
| L：ArraysOnly | **仍 `moves=0`，这是真实的物理上限**：投影仪四周已无可用 2x2 空格，单靠挪阵列不可能给它第 4 个相邻 |
| M：FullRelayout | **`6/6, deficit 0`**，8 次移动（挪开机器给投影仪腾位置） |

结论：你的布局里投影仪被机器 (8,4) 与阵列同时夹住，**只有动机器才能补满**——正确操作是切到 FullRelayout 再按 F7。

### 实时数据说明

每次运行都重新采集，无任何硬编码：

| 数据 | 来源 |
|---|---|
| 场地数量 | 每次枚举所有候选容器（`EmporiumEntry` 公开属性 + `GraphUtils.FindAllChildrenType` 展开嵌套），筛出含机器的 |
| 场地大小 | 该容器内**物品包围盒**（每格都有物品坐实，必然可用） |
| 每台机器耗电 | 物品整数标签 `machinery_drawn_power` |
| 每台阵列充电量 | `NetworkUpgrade.IsCommercialPowerUnlocked()` → 2 或 4（可用配置覆盖） |
| 电池余量/容量 | `power_source_item_energy` / `power_source_item_max_energy` |
| 哪台机器算"目标" | `MACHINE_RECHARGABLE_TAG` / `power_source_item` |

---

## 10. v1.4.0：目标函数对齐参考算法的三级优先级

### 之前错在哪

v1.3.0 的目标里写的是 `-0.5 × 溢出(waste)`，方向与参考算法**相反**：参考算法第三优先级是"最大化总分（= 接触数）"，而"溢出"在数值上等价于"多余的接触"，于是覆盖达标后求解器会主动**减少**接触，把阵列推到角落——就是你截图中那种散乱布局。

### 现在的目标（严格对应你给的三条）

| 优先级 | 参考算法 | 本 mod 的实现 | 权重 |
|---|---|---|---|
| 1 最少负数 | `neg_count` | 未覆盖（供给<耗电）的机器台数 | 1e6 |
| 2 最少亏损 | `neg_penalty_sum` | 每晚总缺口（缺电单位之和） | 1e3 |
| 2b 缺口分布 | — | 单台最严重缺口（避免把缺口摊到多台） | 1e2 |
| 3 最大总分 | `-total_score` = `-Σ(+2×贴A数)` | **总接触数 = Σ每台机器相邻的阵列数** | **+1.0/条** |
| 次要 | — | 散装电池充电量、移动件数（稳定性） | 0.8 / 0.02 |

```csharp
value = -(1e6*Uncovered) - (1e3*Deficit) - (1e2*MaxDeficit)
        + 1.0*Contacts + 0.8*BatteryGain - 0.02*Moves;
```

**溢出不再扣分**（这是刻意的）：在参考模型里它本来就是得分项，多一条接触就多一份余量。

### 顺带修掉的统计错误

面板 3 是 24×10、只有 1 台**不耗电**的机器（`machinery_drawn_power = 0`）却报 `covered 1/1`，把 `12/12` 抬高了。现在：

- 耗电为 0 的机器计入 `idle`，**不进入覆盖率分母**；
- 这类面板直接跳过（`skipped: no power-drawing machine here (idle=N)`）；
- 汇总行显示 `covered X/Y | idle Z`。

### 验证（用你卡住的那块 11×11 面板）

| 场景 | 覆盖率 | 接触数 |
|---|---|---|
| L：ArraysOnly（投影仪四周无空位，覆盖率已到物理上限） | `5/6`，缺口 4（不变） | **12 → 19**（阵列改为紧贴机器） |
| M：FullRelayout | **`6/6`，缺口 0** | 18 |

即：优先级 1/2 不受影响，优先级 3 现在确实在最大化；阵列由"散落"变成"贴着机器成组"。

---

## 11. v1.5.0：多个机器区之间可以互相调货

### 为什么必须搬运

阵列的"相邻"只在**同一个格子容器**内成立（`GameGridInventory.IsAdjacent` 先要求双方同属该容器的 `childItems`）。所以 A 面板多出来的阵列，对 B 面板的机器**毫无帮助**——唯一的办法是把物品**物理搬过去**。

旧版每块面板独立求解，因此"某块面板缺电、另一块面板阵列富余"这种局面无解。

### 两个阶段

1. **阶段一（原有）**：每块面板各自求解并在原地应用。
2. **阶段二（新增）**：全部面板求解完成后，重新读取各面板现状，做**跨面板调度**。

### 调度怎么决策

对每个三元组（供体阵列 × 接收面板 × 落点）做完整模拟，用与求解器**同一个目标函数**算净收益：

```
net = [供体移走该阵列后的价值变化] + [接收面板放入该阵列后的价值变化]
```

- 落点只取"与某台**仍缺电**机器相邻的空位"（沿机器四周每个格子尝试锚定，含旋转姿态）；
- 供体移走若导致自己出现未覆盖机器，净收益必然是大负数，自动被排除；
- 每轮取净收益最大的一个执行，然后**在模型上提交**（供体减一件、接收面板加一件），再继续下一轮，最多 8 次；
- 净收益 ≤ 0 立即停止。

### 应用与安全

跨容器移动走 `Applier.CrossMove`：`Expel`（源）→ `CheckShapePlacement`（目标）→ 设 `modifiedShape` → `UncheckedAccept` → 双方 `Validate`。目标格被游戏拒绝时**立即把物品放回原位**；任何一次搬运失败会取消剩余搬运并如实写日志，绝不留下半成品。

### 验证

`tools/solver_test --xfer`：A 面板 2 台机器 + 6 阵列（富余），B 面板 2 台机器 + 0 阵列（全缺）。

```
A before: covered 2/2 | deficit 0/night | contacts 5
B before: covered 0/2 | deficit 8/night | contacts 0
planned transfers: 1
  powerblock -> (3,0) gain 8 net 2008402.0
B after : covered 2/2 | deficit 0/night | contacts 2
[ok] starved panel became fully powered
[ok] donor panel stayed fully powered
[ok] no items were lost
[ok] layout still legal
```

日志里可见 `[xfer] P1 -> P2 powerblock -> (3,0) receiver_deficit -8`，汇总行带 `transfers=N`，提示语变成 `11/11 powered in 3 areas, 2 moved across`。

### 限制

- 目前只搬运**能量阵列**，不搬运机器（机器的位置代表你的生产线规划，跨面板挪机器风险更大）。若需要也可以加开关。
- 搬运不做"接收面板整体重排"，只在空位里挑最优；若接收面板已排满且无空位，则无法接货。

---

## 12. v1.6.0：只处理打开的窗口 + 机器也能跨区

### 只针对当前打开的机器区

新增配置 `open_windows_only`（默认**开**）。每次运行先判断每个机器区窗口是否在屏幕上（`GameGridInventory.rectTransform.gameObject.activeInHierarchy`），只对打开的窗口做布局与跨区调度：

```
[areas] found=3 open=2 using=2 open_only=True
[panel 1/2] grid 11x11 machines=6 arrays=7 ... open
[panel 2/2] grid 9x10  machines=5 arrays=4 ... open
```

- 若一个都没检测到打开（例如窗口用别的方式隐藏），**自动回退为处理全部**并在日志注明，热键不会“什么都没发生”。
- 关掉该配置则回到旧行为：处理所有含机器的面板。

### 机器也能跨区重排

`FullRelayout` 模式下，跨区调度不再只搬阵列，也可以搬**机器**（配置 `allow_machine_transfer`，默认开）：

- 典型场景：A 面板机器缺电且没有空位放阵列，而 B 面板阵列富余 → 把这台机器搬到 B；
- 机器的落点取“能贴到最多阵列的空位”（按 `阵列数` 排序取前 48 个候选）；
- 机器是同一个 `GameItem`，内部电池/原料/进度随物品一起搬走，不会丢；
- 仍然用同一目标函数评估净收益，只接受正收益，最多 8 次，任一失败即回滚并取消剩余搬运。

`ArraysOnly` 模式仍然只搬阵列，不动机器。

### 验证

`--xfer` 第二组用例：A 面板 1 台机器（耗电 8，仅 1 个阵列，且四周满格放不下新阵列），B 面板 2 台机器 + 6 个富余阵列。

```
A before: covered 0/1 | deficit 4/night
B before: covered 2/2 | deficit 0
planned transfers: 1
  purifier_w -> (1,5) net 1004401
A after : covered 0/0 | deficit 0
B after : covered 3/3 | deficit 0 | contacts 7
[ok] a machine crossed panels / starved machine no longer uncovered / no items lost
```

---

## 13. v1.7.0：详细日志 + 悬停显示供电结果

### 悬停（tooltip）里多出一段"供电布局"

挂在 `ModHook.OnCreateTooltipLate` 上，只做字典查询（几何在运行时算好缓存），悬停零卡顿。以你截图里那台 **净水器 9/天（基础 4/天）** 为例，优化后会显示：

```
── 供电布局 ──
相邻能量阵列 4 台 × 4 = 16/天
耗电 9/天（基础 4/天）
净收支 +7/天（满供）          ← 绿色；缺电时红色并显示"缺电"
电池 88/100
上次优化 2 台（净 -1） → 4 台（净 +7）    ← 只有与上次相比有变化时才出现
```

- 数据来自**上一次运行**的前/后两次快照，随每次按 F7 刷新；
- `ArraysOnly` / `FullRelayout` / 跨区搬运都会更新它；
- 不耗电的设备显示"此设备不消耗电力"。

### 日志更详细

每块面板新增/增强：

```
[panel 1/3] grid 11x11 machines=6 arrays=7 charge=4 (commercial) mode=FullRelayout open
[panel 1/3] layout ...            (布局图)
[panel 1/3] detail ...            (逐台：draw=9(base 4) arrays=3 supply=12 net=+3 battery=44/100 runway=11n)
[panel 1/3] [plan] restarts=.. kicks=.. ms=.. | covered 6/6 | ... | contacts 21 | moves=4
[panel 1/3] moves
  powerblock (1,6) -> (9,8)
  purifier_w (6,8) -> (3,8)
[panel 1/3] [apply] moved 4 items
[panel 1/3] [verify] covered 6/6 | unpowered 0 | deficit 0/night | contacts 21
[panel 1/N] totals machines=6 arrays=7 draw=40/n supply=44/n net=+4/n
```

- `draw=9(base 4)`：模块加成后的实际耗电与基础耗电（对应游戏里"所需能量 9/天（基础 4/天）"）；
- `moves` 段逐件列出"从哪格到哪格"（含旋转变化）；
- `totals` 段给出整块面板的总需求 / 总供给 / 净收支。

---

## 14. v1.8.0：跨区搬运不再要求"对方缺电"

### 之前的门槛写错了

`TransferPlanner` 原来有一条规则：**阵列只在接收面板存在缺电机器时才考虑**（`arr.IsSource && before.Uncovered == 0 → skip`）。你的两块面板在阶段一之后都是 `0 unpowered`，于是阶段二每次都在第一轮就判定"没有可改进的搬运"：

```
[panel 1/2] [before] covered 7/7 | unpowered 0 | contacts 18
[panel 2/2] [before] covered 4/4 | unpowered 0 | contacts 14
[xfer] no cross-panel move improves the total
```

但按三级优先级，第 3 位是**最大化接触数**：把阵列从"只贴 1 台"的位置搬到一个"能贴 2 台"的面板，本身就是正收益。门槛把这类搬运全部挡掉了。

### 现在的判定

- 阵列候选落点 = "缺电机器旁边的空位" ∪ **"贴着最多机器的空位"**（各取前 48，去重）；
- 不再有"对方必须先缺电"的前置条件，一律由目标函数算净收益：
  `net = 供体移走后的价值变化 + 接收方放入后的价值变化`；
- 上限从 8 次提到 12 次，并带 400 ms 时间预算；
- 找不到搬运时日志会说明原因与尝试规模：

```
[xfer] no move: no net improvement (tried 23 placements, best net -2.0) | panels=2 arrays=11
```

### 验证

新增用例：**两块面板都已满供**，A 面板一个阵列被杂物夹死只能贴 1 台，B 面板有一处能贴 2 台的空位。

```
A before: covered 2/2 | contacts 3
B before: covered 2/2 | contacts 2
planned transfers: 1
  powerblock -> (3,2) net 1.0
A after : covered 2/2 | contacts 2
B after : covered 2/2 | contacts 4      ← 总接触 5 → 6
[ok] moved an array even though nothing was short
[ok] total contacts increased
[ok] both panels still fully powered
```

---

## 15. v1.9.0：规划区改为「面板格子范围」，不再砍掉空行空列

### 问题：日志里的 11x12 不是面板尺寸

```
grid 11x12 origin=(0,2) items=13 movable=13
shape w/h=11/14 global=11/14 min=(0,0) max=(10,13) filled=0
```

面板 `inventoryShape` 是 **11 宽 × 14 高**，但规划区只有 11×12：`origin=(0,2)` 说明 **第 0、1 两行因为完全没放东西，被直接排除在规划之外**。这是 v1.2.0 引入的副作用——当时为了绕开「`inventoryShape.Get()` 对所有格子返回 0 → 整块被判成禁区」的坑，改用「物品包围盒」当规划区，代价就是空的整行/整列永远用不上。

对求解器来说这很致命：明明有两行空地可以铺阵列，却完全看不见。

### 修复

规划区 = **面板格子范围（`inventoryShape` 的 min/max）∪ 物品包围盒**：

```csharp
GridModel.PlanRect(itemX0, itemY0, itemX1, itemY1,
                   shapeMinX, shapeMinY, shapeMaxX, shapeMaxY, ...)
```

- 面板范围可靠时以面板为准（尺寸上限 64×64 的合理性检查，异常值忽略并退回物品包围盒）；
- 掩码策略不变：`filled == 0` 视为整块矩形可用，`filled > 0` 时按掩码挖洞。

### 应用阶段的新保护

规划区变大后，非矩形（L 形）面板会包含游戏其实不放行的格子。因此 `Applier.Apply` 改为：

1. 单件落位被拒时**跳过该件**继续（不再整批回滚）；
2. 全部放完后**重新读实际网格**，用同一目标函数打分；
3. 如果实际结果低于基线，**整体回滚**到原位。

即：部分实现可以接受，变差绝不允许。日志会写明 `N of M items had no legal target cell`。

### 日志变化

```
[panel 1/2] grid 11x14 (panel 11x14, items from (0,2)) machines=6 arrays=7 ...
[panel 1/2] [diag] grid 11x14 origin=(0,0) panel=11x14 items_from=(0,2) items=13 movable=13
```

一眼就能核对「规划区 / 面板 / 物品范围」三者是否一致。

### 验证

`tools/solver_test --rect`：

```
[ok] 11x14 panel with items in the lower 11x12 keeps the full panel
[ok] a shape with no extent falls back to the item box
[ok] an absurd shape extent is ignored
[ok] union grows outward in every direction
[ok] contacts improved by using the empty top rows      (1 -> 2)
[ok] the extra array sits in rows 0-1
[ok] layout is legal
```

最后一组是行为验证：11×14 面板、物品只占 y=2..13，一个阵列原本在死角贴 0 台，修复后求解器把它搬到 `(1,0)`（顶部空行），接触数 1 → 2。旧版根本不会考虑 y<2。

---

## 16. v2.0.0：跨区改成"单步搬运 + 双方重排"的循环

### 之前为什么只搬一个就停

旧版跨区是**一次性规划**：`TransferPlanner.Plan()` 在内存里连续决策若干次，然后逐个应用。问题出在候选落点的定义——只接受"**当前已经空着、并且已经贴着某台机器**"的格子。

而一块排得很满的面板恰恰没有这种格子：想再塞一个阵列，必须先挪开别的阵列给它腾位置。于是第二轮永远找不到落点，日志表现为"搬了 1 个就结束"。

### 现在的循环

```
第 1 轮：重新读取所有面板 → 选出最优的单次搬运 → 应用 → 重排供体 → 重排接收面板
第 2 轮：重新读取所有面板 → 再选一次 → ...
（最多 6 轮，或直到没有正收益）
```

关键在 **"搬完重排接收面板"**：新阵列落地后，接收面板会以新布局为种子重新求解，从而为它腾出空间。只有这样才能连续搬运，而不是卡在第一个。

### 供体门槛

不再要求"供体必须完全满供"（那会挡掉"把缺电机器搬到富余面板"这种正解），改为**搬走后不得变差**：

```csharp
if (dAfter.Uncovered > dm.Uncovered) continue;   // 缺电台数不能增加
if (dAfter.Deficit  > dm.Deficit ) continue;     // 缺口总量不能增加
```

### 每次搬运都有落地确认

```
[xfer r1] P2* -> P1* powerblock_on (3,7) ori=0 net=2008400.1 | donor was 5/5 contacts=15 | receiver was 5/6 deficit=1 contacts=12 | best net 2008400.1 of 90 placements
[xfer] P1* holds powerblock_on at (3,7) - confirmed
[refit P1*] moved 2 (0 refused) | covered 6/6 ... -> covered 6/6 contacts 14
[refit P2*] already optimal covered 5/5 | ... contacts 13
```

`confirmed` 来自**搬运后重新读取接收面板**，不是内存里的计划；如果游戏把物品放到了别处，会打印实际坐标并警告。

### 日志增加了什么

| 行 | 新增内容 |
|---|---|
| `[areas]` | 每个面板的 `m=` / `a=` / `free=` / `open|closed` |
| `[start]` | 优化前的总覆盖、总阵列数、模式、预算、机器跨区开关 |
| `[panel] grid` | `free=`（可用空格数） |
| `[panel] detail` | 每台机器新增 `need=N`（按充电量算需要几个阵列）与 `OK / SHORT n / IDLE` 标记 |
| `[panel] totals` | 新增 `free=` |
| `[xfer rN]` | 供体/接收方各自的覆盖率、接触数、缺口，以及"试了多少个落点、最优净收益" |
| `[xfer]` | 落点坐标确认 |
| `[refit]` | 搬运后双方重排的前后对比 |
| `[done]` | `covered 10/11 -> 11/11 deficit=0/n arrays=4->5 transfers=N` |

### 验证

新增 `--xfer` 第三组用例：A 面板 6 台机器只有 2 个阵列（0/6），B 面板 4 台机器有 8 个阵列（4/4）。

```
A before: covered 0/6 | deficit 32/night | contacts 4
B before: covered 4/4 | deficit 0/night    | contacts 11
round 1: powerblock -> (3,7) net 2008002.0
round 2: powerblock -> (3,9) net 2008400.0
round 3: stop - no net improvement (tried 90 placements, best net 0.0)
A after : covered 6/6 | deficit 0 | contacts 12
B after : covered 4/4 | deficit 0 | contacts 14
[ok] several arrays crossed instead of stopping at one
[ok] the starved panel reaches full coverage
[ok] the donor keeps full coverage
```

**2 个阵列连续搬过去**（旧版只会搬 1 个），A 由 0/6 变 6/6，B 保持 4/4，总接触 15 → 26。第 3 轮明确报告"试了 90 个落点、最优净收益 0"，说明是真的没有改进空间而不是提前放弃。

---

## 17. v2.1.0：修正「什么算机器」的判定

### 之前的错误结论

v2.0.0 我写过："走私者暗格和水瓶打印机都不算耗电机器"。**这是错的**。查存档 `save_0.es3`（明文 JSON）后的事实：

| 物品 | itemTypes | 标签 | 事实 |
|---|---|---|---|
| `smuggler_bay` 走私者暗格 | `["STORAGE"]` | 只有 `STORAGE` / `CONTAINER_TAG` / `SYSTEM_TAG`，**没有** `MACHINE_RECHARGABLE_TAG` | 存储容器，本来就不会被当成机器 |
| `bottle_printer` 水瓶打印机 | `[]` | 有 `MACHINE_RECHARGABLE_TAG`，**没有** `machinery_drawn_power` | **是充电设备**，只是放在主页、不在机械区 |
| `furnace` 熔炉 | `["MACHINE"]` | 两个标签都有 | 正常夜间耗电机器 |
| `water_purifier` 净水器 | `["MACHINE"]` | 两个标签都有 | 正常夜间耗电机器 |

所以「有没有 `machinery_drawn_power`」不能用来判断「是不是机器」——那样会把水瓶打印机这类设备错误排除。

### 现在的判定

```
机器 = 带 MACHINE_RECHARGABLE_TAG 且不是能量阵列
```

- 能量阵列自己也带这个标签，必须显式排除（`!IsSource`）；
- 暗格没有这个标签，天然不在机器集合里；
- 水瓶打印机是机器，但缺 `machinery_drawn_power` → `Demand = 0`，运行时归入 **idle**，不进入覆盖率分母；
- 只有含**耗电机器**（`Demand > 0`）的面板才参与跨区调度，主页面板不会被当成搬运目的地。

### 新增日志：面板内容清单

```
[panel 3/3] contents (6 item(s)):
  water_purifier        water_purifier    3x4    at(12,3) machine  draw=4
  bottle_printer        bottle_printer    2x3    at(0,0)  machine  draw=0
  smuggler_bay          smuggler_bay      3x3    at(5,5)  other
```

`other` 表示既不是阵列、也不是机器、也不是散装电池——暗格会显示成 `other`。这样以后任何"看起来奇怪"的面板，看这一段就能确认它里面到底是什么。

### 已确认的参数映射

| 参考算法 | 采用 | 说明 |
|---|---|---|
| `contact_weight` | **实时读取** | 商用已解锁 = 4，否则 2 |
| `boards` | **实时扫描** | 只取当前打开的机械区，尺寸取物品包围盒 ∪ 面板范围 |
| `count` / 尺寸 | **实时扫描** | 每件物品的真实形状与朝向 |
| `initial_score` | **实时读取** | `-machinery_drawn_power` |
| `neg_count` / `neg_penalty_sum` | 沿用 | 未覆盖台数 / 每晚总缺口 |
| 不耗电设备 | 归 idle | 不计分、不占覆盖率、不吸引阵列 |

---

## 18. v3.0.0：全局联合求解（按参考算法重构）

### 为什么之前的跨区永远"没效果"

旧结构是「每块面板独立求解 → 再尝试跨区搬几件」。这在数学上只能到**局部最优**：每块面板都已经对自己最优了，任何单件搬运都只会让总接触数下降，于是跨区阶段每次都报 `no net improvement`。

参考算法从一开始就不是这么做的——它的 `boards` 是一个数组，`layout` 里每个物品都带场地编号，`neighbor()` 可以**把物品放到任意场地**。也就是说它是**一个池子、一起搜索**。

### 现在的结构

```
所有打开的机械区 ──► 共享物品池（机器 + 能量阵列）
                      │
                      ├─ 模拟退火：随机重插 1 件 / 同尺寸互换（可跨面板）
                      ├─ 局部精修：逐件穷举「所有面板 × 所有格子 × 所有朝向」
                      └─ 多起点重启（第一次从当前布局出发，保证不退化）
                      │
                      ▼
              跨面板移动清单 → 逐件应用 → 重新读网格验证
```

目标函数与参考算法逐项对应：

| 参考算法 | 本实现 |
|---|---|
| `非A最终分 = initial_score + cw × 接触数` | `final = -draw + charge × 相邻阵列数` |
| `neg_count` | 净收支为负的机器台数 |
| `neg_penalty_sum` | 每晚缺口总和 |
| `total_score = base + cw × N` | `charge × 总接触数 − 总耗电` |
| `obj = neg×1e7 + pen×1e4 − total` | 完全相同 |

### 关键细节

- **只规划打开的机械区**：`open_windows_only` 仍然生效，且只有含耗电机器（`draw > 0`）的面板进入物品池；
- **不耗电设备固定不动**：水瓶打印机这类 `draw = 0` 的机器、散装电池、储物容器都当成固定障碍，不会被搬来搬去；
- **同分时选移动更少的方案**：目标值相同则比较移动件数，避免"大搬家零收益"；
- **一次成批应用**：先把所有要动的物品从原面板取出，再逐件放到目标格（可能跨面板）；任何一件被游戏拒绝就整体回滚，不会留下半成品；
- **独立验证**：应用后重新读取每块面板，重新计算覆盖率与接触数，不信任求解器内存里的结果。

### 日志

```
[global] P1 covered 5/6 | ... free=60
[global] P2 covered 5/5 | ... free=84
[global] boards=2 pool=22 (machines=11, arrays=11) charge=4 budget=700ms
[global] before: uncovered 1 | deficit 4 | contacts 31 | score=58
[global] after : uncovered 0 | deficit 0 | contacts 38 | score=86 (restarts=41 ms=698)
[global] moves 9 (across 3)
  powerblock           P1(3,4) -> P2(6,2)  CROSS
  water_purifier       P2(0,0) -> P1(5,5)  CROSS
  ...
[global] applied: covered 11/11 contacts=38
[global] verified: covered 11/11 contacts=38
```

### 验证

`tools/solver_test --global`：两个 11×14 面板，P1 有 4 台机器只有 2 个阵列（0/4 覆盖、缺 16），P2 有 2 台机器 6 个阵列（2/2）。

```
before: uncovered=4 penalty=16 contacts=8  score=-16
after : uncovered=0 penalty=0  contacts=17 score=20
moves=14 across=11
[ok] no machine was left worse off
[ok] total score did not drop
[ok] both panels still legal
[ok] no item lost
```

**11 件物品跨面板移动**，未覆盖从 4 台降到 0 台，总接触从 8 升到 17。这就是参考算法 C10「场地分配自由」的效果——旧的逐面板结构无论如何都做不到。

---

## 19. v3.1.0：悬停改为实时计算，只显示净耗电

### 之前为什么移动后不更新

v1.7.0 的 tooltip 用的是**快照**：只在按 F7 运行时抓一次"优化前 / 优化后"，悬停时查字典。所以你在游戏里手动拖动阵列之后，数字仍是上次运行的结果。

### 现在

悬停时**当场计算**：

1. 读该物品的 `machinery_drawn_power`（没有这个标签就不显示，例如储物容器）；
2. 取它的 `modifiedShape`（当前位置）和 `parentInventory.childItems`；
3. 逐个判断同容器内的能量阵列是否**共边相邻**（对角不算，与游戏规则一致）；
4. `净耗电 = 相邻阵列数 × 充电量 − 耗电`，充电量实时读商用供电状态（4 或 2）。

所以拖动阵列、旋转机器之后，**下一次悬停就是新数值**，不需要再按 F7。

### 显示内容

只保留一行（按你的要求，其他结果不再显示）：

```
净耗电 +3/天            ← 绿色：供大于耗
净耗电 -1/天（缺电）     ← 红色：还差 1/天
```

耗电为 0 的设备不显示这一行。

### 顺带清理

`TooltipCache.Capture()` 不再被调用（它每次运行会额外遍历一遍所有面板，现在纯属浪费），类保留为空壳，`Append()` 变成纯计算函数。
