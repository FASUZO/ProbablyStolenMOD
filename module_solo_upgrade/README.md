# 模组独自升级 · Module Solo Upgrade

给 **Probably Stolen**（深空当铺）加一台新设备：**自学习模块编程台**。
外形与操作逻辑都对齐游戏里的「瓶子打印机」：**电池槽 + 模块槽 + 两个按钮**。

- 模组版本：`3.5.0`
- 目标游戏版本：`0.46D`（以 `Player.log` 里 `[ModLoader] audit: game …` 为准）
- 模组 ID：`module_solo_upgrade`（安装目录与存档标记都以它为准，不要改）

---

## 1. 它做什么

| 按钮 | 作用 | 代价 |
|---|---|---|
| **植入自学习** | 给模块装上游戏原生的「自学习算法」词条 | 一次电量 |
| **自学习 +1** | 推进一次自学习进度（相当于模组被正常使用一次） | 一次电量 |

两个按钮都依赖游戏自己的 `ModuleEffectHelper`，不是本 mod 自己编的数值系统，所以**单次增益（+1）与 tooltip 文案都与原版一致**；上限默认也走原版算法（`base × 2`），只有把 `cap_mult` 调高时才会偏离。

---

## 2. 怎么拿到（正常途径：博士商店）

**在博士 Dr. Jackson 的诊所商店购买**，默认 **250** 信用点。不用合成、不用调试命令。

- 商店货架在医商生成时重建（`TraderFactory.CreateDoctor()` → `TradeSheet.GetGenericMedicalShop()`），
  所以如果你把它卖了 / 丢掉了，**重新进一次诊所或过一天**，货架上会再出现一台。
- 每次生成都会重新挂一台，**不会重复堆积**（该方法是每次 `new List<GameItem>()`）。

**开发/应急途径**：按 **`F9`** 打开调试面板（不是 `Ctrl+L`；游戏里没有这个绑定）→ `SPAWN` 分类 → `Self-Learning Programmer (Grant)`，直接进背包。

> `F9` 需要游戏根目录存在 `dev.txt`（本机已有，日志会打印 `[DevTools] Developer tools enabled: F9 debug panel and F8 console.`）。`Shift+F9` 是旧版面板。
这只是调试捷径，不是正常设定。

---

## 3. 怎么用

1. 背包里**双击**设备，打开机械窗口。
2. 左侧**电源**面板放一块电池。
3. 中间**模块**格放一个模组（只接受模组类物品）。
4. 点 **植入自学习** → 之后反复点 **自学习 +1**。

> 模块格是 **3×3**，但一次只处理**第一个**模组；同格放多个时，只有排在最前面的那个会被操作。

### 上限预览（鼠标悬浮模组即可看到）

模组 tooltip 末尾会自动多出一行，**放不放进机器都能看**：

| 模组状态 | tooltip 显示 |
|---|---|
| 还没植入 | `植入后自学习上限: 性能 24 / 效率 24 / 品质 24（每次 +1）` |
| 已植入 | `自学习: 性能 12/24 / 效率 4/24 / 品质 0/24` |
| 已练满 | `自学习（已满）: 性能 24/24 / 效率 24/24 / 品质 24/24` |

上限的算法是游戏写死的：**`上限 = 植入那一刻的该项加成 × 2`**，之后每次自学习只 **+1**。
所以原加成 +16 的模组上限是 32，而 +8 的上限只有 16（各模组的实测值见第 4 节）。
三项各自独立，加成是 0 的那一项不会出现在预览里（它学不动）。

### 关于「植入后数值变低」——这是原版机制，不是 bug

游戏的 `ModuleEffectHelper.InitLearningAlgo` 会把模组**当前**的性能/效率/品质加成转成**自学习上限**，
然后**把当前值清零**；之后靠一次次自学习把它加回去，最终可超过原值。

所以：

- 植入后立刻看，数值是 0，这是正常的；
- 反复点「自学习 +1」，数值会一格格涨到上限；
- 三项基础加成都是 0 的模组没有可学内容，植入时会直接提示并**不扣电**。

---

## 4. 数值（两个可配项）

耗电、次数、售价是写死的常量；可配的是**上限倍率**和**每次推进步数**。

写死的：

| 数值 | 值 | 源码位置 |
|---|---|---|
| 每次耗电 | `10` / 步 | `ModEntry.PowerCost` |
| 次数上限 | 不限（只受游戏原生 `LEARNING_ALGO_MAX_*` 约束） | `ModEntry.MaxUses = 0` |
| 博士售价 | `250` | `ModEntry.ShopPrice` → 物品 `unitValue` |

可配的（游戏内 主菜单 → **Mods** → 本模组 → **设置**，或直接改 json）：

```
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModConfigs\module_solo_upgrade.json
```

| 键 | 默认 | 范围 | 说明 |
|---|---|---|---|
| `cap_mult` | `2` | 1 – 10 | 上限 = 模组原加成 × 该值。`2` = 原版 |
| `step_per_use` | `1` | 1 – 10 | 一次「自学习 +1」推进几步。`1` = 原版每次 +1；**耗电按步数累计**，所以调高只省点击不省电 |

`cap_mult = 2` 时走纯原版路径，**一行代码都不会覆盖游戏算好的上限**；
大于 2 时 `ApplyCapMultiplier` 会在 `InitLearningAlgo` 之后重写三项 `LEARNING_ALGO_MAX_*`。

`step_per_use` 的实现方式是**循环调用 N 次原版 `OnUsedLearningAlgo`**，不是自己写数值——
所以每次仍然只 +1，只是点一下算 N 步；剩几步就到满会自动按实际步数结算，不会多扣电。

> 改 `cap_mult` **只影响之后新植入的模组**——已植入的那些，上限在植入那一刻就写进存档了。
> `step_per_use` 是即时生效的。

### 各模组的原加成与上限（游戏硬编码，实测值）

| 模组 | 原加成 | 默认上限（×2） | 练满需要 |
|---|---|---|---|
| `system_module_performance` 系统·性能 | 性能 **16** | 32 | 32 步 |
| `system_module_efficiency` 系统·效率 | 效率 **16** | 32 | 32 步 |
| `system_module_quality` 系统·品质 | 品质 **8** | 16 | 16 步 |
| `equipment_module_performance` 装备·性能 | 性能 **30** | 60 | 60 步 |
| `equipment_module_efficiency` 装备·效率 | 效率 **20** | 40 | 40 步 |

> 「系统·品质」的上限天生只有 16，因为它原加成就是 8。想要 32 的上限，用**性能**或**效率**系统模组。

嫌点击次数多：把 `step_per_use` 设成 `2`，一次点击就推进 2 步（耗电也变成 20/次），
总耗电不变但点击次数减半。

---

## 5. 贴图

本 mod 自带一张自定义贴图，**不再沿用瓶子打印机的图**（避免和瓶子打印机混淆）：

```
<游戏>\Mods\module_solo_upgrade\
├── manifest.xml
├── ModuleSoloUpgrade.dll
└── Textures\Items\self_learn_programmer.png   ← 32×48，像素风
```

图案：深灰机壳 + 上方青绿色进度屏（2 亮 1 暗，表示自学习进度）+ 中间金色芯片（带引脚）+ 下方电池。

### 尺寸为什么是 32×48

这张图**同时决定物品的网格占位**。游戏按 `UISettings.gridSize`（本作是 **16px**）切格，
逐格取「每格中心像素的 alpha」决定哪一格算被占用：

| 尺寸 | 占格 | 参照 |
|---|---|---|
| `32×48` | **2×3** | 瓶子打印机（`bottle_printer` 原图正是 32×48） |
| `48×48` | 3×3 | 存储舱（`storage_bay`） |

保持 2×3 才能和原版机器共用同一套摆放/槽位假设。想让设备变小或变大，改图尺寸即可，
但要注意：**已存在于存档里的设备不会跟着变**（形状在创建时就算进了物品数据）。

### 换自己的图

- 文件名必须正好是 `self_learn_programmer`（文件名去掉扩展名即 sprite key）。
- 必须是 PNG，透明背景；想保持 2×3 就用 32×48，且六个格心（8,8）(24,8)(8,24)(24,24)(8,40)(24,40) 都要不透明。
- **不要在 `Textures/` 里放预览图/草稿**：该目录下每一个 PNG 都会被注册成可用 sprite。
- 没有这张图时，自动回退到原版瓶子打印机的贴图，不影响功能。

---

## 6. 存档行为

设备是**正经的目录物品**（由 `DirectoryMaster.Item("self_learn_programmer", …)` 重建），
所以电池/模组会随设备一起进存档，**不需要额外兜底**：

- `GameItem.get_child()` 直接返回 `contentWindow`，
  而 `SaveManager.EncodeNodes` 正是按 `children` 遍历，`SaveItemNode` 里也带
  `childItems` / `childItemInventoryNode`（子物品 → 槽位索引）。
- 因此**窗口面板顺序是存档契约**：`0 = 电源`、`1 = 模块`。
  改代码时若调换顺序，读档会把内容塞进错误的槽。

> 若将来游戏版本改成不遍历窗口，症状会是「读档后机器空了」。
> 那时的修法是在 `PlayerStore.SaveGame()` 前缀里用 `PlayerStore.EncodeItem` 编码到 `ModHelper.SetModData`，
> 并在重建时用 `SaveManager.DecodeNodes` 还原（skill 的 pitfall 清单里有完整配方）。本版**没有**加这条，
> 因为额外的双写会在「玩家清空机器后存档」时造出幽灵物品。

---

## 7. 日志与排查

日志文件（**不要**只看 F8 面板，它不镜像 `Debug.Log` 历史）：

```
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log
```

正常应看到（前缀是 ASCII 的 `ModSoloUpgrade`，因为 F8 控制台字体没有中文字形）：

```
[ModLoader]   Initialized: 模组独自升级 v3.0.0
[ModSoloUpgrade]: Init OK. game=0.46D device=self_learn_programmer power_cost=10 max_uses=0 shop_price=250
[ModSoloUpgrade]: Debug button registered (init): F9 -> Spawn -> Self-Learning Programmer (Grant).
[ModSoloUpgrade]: Debug button registered (enable): F9 -> Spawn -> Self-Learning Programmer (Grant).
[ModSoloUpgrade]: Enabled. Buy the programmer from the Doctor. Dev copy: F9 > Spawn > Self-Learning Programmer (Grant).
[ModSoloUpgrade]: Added the programmer to the Doctor's shop (unitValue=250).
[ModSoloUpgrade]: Debug grant: placed (units=1 size=4).
[ModSoloUpgrade]: Implanted self-learning entry into 'module_performance'. power=10 uses=0/unlimited P 0/24 E 0/24 Q 0/24
[ModSoloUpgrade]: Self-learn +1 on 'module_performance'. power=10 uses=1/unlimited P 3/24 E 3/24 Q 3/24
```

| 症状 | 原因 | 处理 |
|---|---|---|
| 按 `Ctrl+L` 没反应 | 游戏没有这个绑定，调试面板是 **`F9`** | 按 `F9`；`Shift+F9` 是旧版面板 |
| `F9` 打不开面板 | 缺 `dev.txt`（`DevTools` 标记文件），或处于 showcase 模式 | 在游戏根目录（`Probably Stolen.exe` 同级）放一个空的 `dev.txt`；日志会打印 `[DevTools] Developer tools enabled: F9 debug panel and F8 console.` |
| `F9` 能开，但 `Spawn` 里没有 `Self-Learning Programmer (Grant)` | `OnEnable` 中途异常被加载器吞掉，按钮没注册上 | 日志必须出现 `Debug button registered (init)`；若只有 `Init OK` 没有它，把 `OnEnable failed` / `Debug button registration failed` 那行贴出来 |
| 双击打开后窗口压在主区域下面、拖不动 | 窗口以 `canMove=false` 创建。`PixelWindow.Show()` 在 `canMove==false` 时会**直接返回**，跳过 `BringWindowsToFront`（置顶）、`EnsureFloatingDock`（浮动窗）、位置写入——于是既不置顶也不是浮动窗 | 3.1.2 起改用 `CreateMachineryWindow(true, panels)`（原版瓶子打印机传的也是 `isDraggable=true`） |
| 点了 `Grant` 按钮，日志有反应但背包里啥也没有 | 物品 `unitCount=0`：`TryAcceptAll(..., -1)` 会把 `-1` 解析成 `unitCount`，为 0 时**直接返回 0**，等于什么都没做 | 3.1.1 起工厂里已 `SetAmount(1)`；日志应显示 `Debug grant: placed (units=1 size=N)`，出现 `inventory refused it` 就是格子满了 |
| 博士商店没有这台设备 | 货架是进入诊所/过天时重建的 | 重新进诊所或睡一觉；并确认日志里有 `Added the programmer to the Doctor's shop` |
| Mods 面板里根本没有这个模组 | `manifest.xml` 解析失败，加载器会整包跳过 | 看日志有没有 `Failed to parse manifest.xml`；用 `build.bat` 重建（它会先校验 XML） |
| 点按钮没反应 | 缺电池 / 缺模组 / 电量不足 / 模块已满级 | 日志里必定有一行 `... skipped on '...': 原因` |
| 提示 `both module effect slots are already occupied` | 模组两个词条位都占了 | 先用原版手段拆掉一个词条再植入 |
| 提示 `refused by the module (incompatible effect)` | 已有词条与自学习算法互斥 | 换模组；本次**没有扣电** |
| 提示 `already at the self-learning cap` | 三项都到上限了 | 正常，说明这个模组练满了 |
| 中文在 F8 面板里是空白 | OfficeCodePro 字体无中文字形 | 属正常现象，看 `Player.log` |
| 装了库存类 Mod 后出现异常排布 | 例如 *Smart Inventory Arrange* 会接管插入 | 临时禁用该 Mod 再试 |

---

## 8. 构建与安装

```bat
build.bat
build.bat "D:\你的\Probably Stolen 路径"
```

脚本会：校验 .NET SDK → **用 XML 解析器校验 `manifest.xml`**（坏清单不会编译失败，但会让整包不加载）→
检查游戏已关闭 → `dotnet build -c Release` → 复制 DLL / manifest / 可选 `Textures/` 到
`Mods\module_solo_upgrade\` → 列目录。

装好之后：

1. 启动游戏 → 主菜单 **Mods** → 选中本模组 → **Activate**；
2. **再重启一次游戏**（激活与重启是两步，只做一步最常见的后果就是「没效果」）；
3. 回到 Mods 面板确认版本显示 **3.0.0**；
4. 进存档，去博士那里买。

---

## 9. 实现要点（给后来改代码的人）

| 点 | 依据 |
|---|---|
| 设备窗口三种面板顺序 | 对齐 `MachineBottlePrinter.CreateMachineInventoryWindow`：`PowerPanel` / `output` / `control` |
| 电池槽 | `MachineHelper.SetupBatterySlot(slot, machine)`，`GetBatterySlot` 读 body 0 |
| 模块槽 | `MachineHelper.SetupModuleBay(grid, machine, null, [MODULE_TYPE_UNIVERSAL])`，`GetModules` 读 body 1；`onChanged` 传 `null` 与 `MachineHydroponic` 一致 |
| 植入词条 | `AddEffectToModule(…, MODULE_EFFECT_LEARNING_ALGORITHM)` + `InitLearningAlgo`（后者幂等） |
| 推进一次 | `OnUsedLearningAlgo(module)` |
| 电量 | `PowerHelper.CanDrawPowerSource` 先验证，确认词条真的写进去之后才 `DrawPowerSource` |
| 次数计数 | 自定义标签 `SELF_LEARN_USES_INT`，写入用 `Enable() + SetInt()` 再 `SyncModifiedState()` |
| 读档钩子 | `GameItemFunc.onLoaded`（`PlayerStore.DecodeSaveItem` 会对每个重建的物品调用） |
| 商店注入 | `[HarmonyPatch(typeof(TradeSheet), "GetGenericMedicalShop")]`（单重载、零参数） |
| 目录工厂 | `[HarmonyPatch(typeof(DirectoryMaster), "Item", new[]{typeof(string), typeof(bool)})]` 前缀 |
| 物品生成 | 工厂注册进 `PreBuiltItemHelper.preBuiltItems`，`ModHelper.SpawnItem` 优先查它 |
| 权限 | `<Capabilities />` 为空：没有对运行时类型做反射，也不直接读写文件 |

所有 patch 目标均已用 Cecil 核实为**单重载**（或显式指定参数类型），避免 `PatchAll` 绑定失败导致整个模组无法激活。

---

## 10. 已知限制

- **本版尚未实机验证**：编译 0 错误 0 警告、patch 目标已核对，但沙箱没有游戏 GUI，运行期行为需你进游戏确认。
- 模块格一次只操作**第一个**模组。
- 「植入」会按原版逻辑把当前加成清零换成上限，这是设计而非缺陷（见 §3）。
- 不做 modData 双写（见 §6 的理由）。
- 旧版本（1.x/2.x）自动发放或旧商店条目遗留的设备不会自动清除，可自行卖掉/丢弃。

---

## 参考的第三方模组

| 模组 | 作者 | 来源 | 参考了什么 |
|---|---|---|---|
| 水瓶打印机增强 / BetterBottlePrinter v2.0.7 | n1ght、好景不长 | Steam 创意工坊 `3829069448` | 最接近的类比（机器产出物品 + 电力消耗）。用 `dump_mod_patches.ps1` / `dump_mod.ps1` 读其 Harmony 目标与类型，确认「给现成机器加面板、加槽位」的安全做法，作为本模组机型与面板构造的对照。 |
| 模组堆叠升级 / ModuleStack v1.4.1 | sdjybn | Steam 创意工坊 `3829042297` | 参考其模组槽的读写方式与 `modData` 持久化边界，据此决定本模组只写自己前缀下的键。 |
| 店铺助手 / Shop Assist v1.4.0 | loyce | Steam 创意工坊 `3829106910` | 参考「向商店注入自定义商品」的做法，用于把编程台挂到博士处售卖。 |

边界说明：只读其 Harmony 补丁目标与类型结构，未复制代码；本模组的设备逻辑与 UI 为自研。

