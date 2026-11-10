# 技术文档

本文件记录四个模组的实现方式、加载器契约与发布流水线，供后续维护或新增模组时参考。
配套文档：模块内 `README.md`（用户视角）、`WORKSHOP.md`（发布流程）、`LICENSE`。

- 目标游戏：**Probably Stolen 0.46D**（AppID `4348910`，Unity 2021.3 / Mono，`net48`）
- 加载器：游戏内置，模组 = `Mods\<id>\` 文件夹（`manifest.xml` + 实现 `IMod` 的 DLL）
- 无 MelonLoader / BepInEx，无前置框架

---

## 1. 运行模型

加载器在主菜单阶段初始化模组，顺序为：扫描 `manifest.xml` → 校验能力声明 → 加载 DLL →
自动对这些程序集执行 `Harmony.PatchAll()` → 调用 `Init(ModManifest)` → `OnEnable()`。
关闭时调用 `OnDisable()`；在 Mods 菜单里停用只在下次启动生效。

由此产生的四条硬性约束（违反就会踩坑）：

| 约束 | 后果 |
|---|---|
| `Init` / `OnEnable` 阶段**没有游戏系统** | 不能读存档、刷物品、访问 manager，这些对象还没被创建 |
| 日志必须是纯 ASCII | F8 控制台字体无 CJK 字形，中文会变成空格，整行像是空的 |
| 自造槽位里的物品不会被存档保存 | 必须自己编码进 `PlayerStore.modData` |
| 反射"运行时类型 / 字符串拼成员名"要声明 `reflection-external` | 未声明会被扫描器拒绝激活；而该能力 **2026-12-10 起被整体拒绝**（见 §7） |

`manifest.xml` 的关键字段：`<ID>`（决定文件夹名与存档标记，只允许 ASCII 字母数字 `_ - .`）、
`<Assemblies>`（必须与实际 DLL 逐项一致，多余 DLL 会拒绝激活）、`<Capabilities>`
（空标签表示不越界）、`<GameVersions>`（Mods 菜单据此标兼容性）。

---

## 2. 仓库布局与构建

```
<mod_id>/
├─ manifest.xml          # 清单：ID / 显示名 / 版本 / 兼容版本 / 能力
├─ <Mod>.csproj          # net48；引用游戏 Managed\ 下程序集，全部 Private="false"
├─ build.bat             # 校验清单 → 检查游戏未运行 → dotnet build → 复制到 Mods\<id>\
├─ src/                  # 源码
├─ Textures/             # 可选贴图（自绘）
└─ preview.png           # 512×512 工坊缩略图
```

- 通过 `GameDir` 属性定位游戏，`dotnet build -c Release -p:GameDir="<游戏目录>"`；
  找不到时 `build.bat` 会按常见 Steam 路径探测。
- 引用一律 `Private="false"`：**构建不复制、也不分发任何游戏本体文件**，仓库因此可以公开。
- 版本号三处必须一致：`manifest.xml` 的 `<ModVersion>`、`csproj` 的 `<Version>`、README 标注。

---

## 3. 模组实现

### 3.1 `auto_uv_lamp` — 紫外灯水龙头

**问题**：水龙头本体只是一个 `MiscItemDirectory.Faucet` 物品，`EmporiumEntry.faucetElement`
自带的 `GameSlotInventory` 里装的是水龙头自己，玩家塞不进第二件东西。

**做法**：用游戏自己的机械窗口工厂另造一个内容窗口并挂到水龙头上。

```
GameSlotInventory(5, 1, false, false)        // 尺寸按 uv_filter 的占用形状，5×1
MachineHelper.Panel(label, slot, "uv", true) // 造面板
MachineHelper.CreateMachineryWindow(true, panels)
faucet.SetContentWindow(window)
```

补丁：

| 目标 | 作用 |
|---|---|
| `WaterHelper.AddWater`（后置） | 槽内有灯时把出水提纯为基础水（≈98%）并杀一次菌 |
| `PlayerStore.SaveGame` | 把槽内物品编码进 `modData`（`EncodeItem` / `DecodeNodes` 往返） |

**取舍**：装配/卸下完全走游戏的拖放交互，不新增按钮；`GiveWaySeconds`（默认 3 秒）用于和
同样改水龙头窗口的模组错峰，避免抢同一个 `SetContentWindow`。

### 3.2 `storage_bay_label` — 存储舱标签

范围最广的一个，靠 12 处补丁把"标签"织进原版 UI：

| 目标 | 作用 |
|---|---|
| `GameItem.GetDisplayName` | 悬停介绍里追加上缀 |
| `GameItemElement.GetTooltipBasic` | 悬浮框正文 |
| `GameItemElement.Validate` / `UpdateRectTransform` / `ResolveSpriteByName` / `HandleInnateModSprite` | 图标角标的绘制与刷新 |
| `ItemContextHandler.TriggerButton` / `UpdateConditions` / `UpdateSelectButtonColor` | 右键菜单入口与可用性 |
| `GameSlotInventory.Validate` / `GameGridInventory.Validate` | 容器内容变化时重画 |

**角标实现**：1.6.0 曾尝试把角标做成物品节点的子物体，实际完全不显示，已回退到
**独立叠加层画布**（`Overlay.cs`）。文字阴影用"偏移一层的深色副本"实现，避免丢字。

**持久化**：标签文本存在 `modData` 的模组前缀键下，不改物品原名。

**能力声明**：`reflection-external` —— 因为 `Reflect.cs` 用 `target.GetType().GetField(name)`
按字符串取成员，`LabelUI.cs` 用 `typeof(ItemContextHandler).GetField` 取调色板字段。
这是唯一一个受 2026-12-10 规则影响的模组（见 §7）。

### 3.3 `machine_power_layout` — 机器区供电布局

**唯一一个不打 Harmony 补丁的模组**，全部走加载器提供的钩子：

```
ModHook.OnGoingSleepEarly   // 过夜结算前自动运行（可选）
ModHook.OnCreateTooltipLate // 在物品悬浮框里追加"净耗电 ±N/天"
```

- 触发：`Update` 里轮询 `Input.GetKeyDown(F7)`，热键可由 ModConfig 改；
  必须处于店铺场景（机器区可见）才执行。
- 求解：`Planner` / `GlobalPlanner` 在时间预算（默认 350 ms）内搜索阵列与机器的相邻排布，
  目标函数来自反编译得到的结算规则——单台阵列给相邻目标回 2 点（解锁商用供电为 4 点）。
- 落位：`Applier.ApplyGlobal` 先摘下再落位、逐格校验占用，失败自动回滚；**
  只移动物品，不修改、不销毁任何东西**。
- 结果反馈：屏幕 toast + 日志；`dry_run` 开关可只算不动。

### 3.4 `module_solo_upgrade` — 模组独自升级

新增一台设备（编程台），外形与交互对齐游戏里的瓶子打印机：电池槽 + 模块槽 + 两个按钮。

| 目标 | 作用 |
|---|---|
| `TradeSheet.GetGenericMedicalShop` | 把设备挂到博士（Dr. Jackson）的商店 |
| `DirectoryMaster.Item` | 注册设备条目 |
| `ModuleEffectHelper.CreateModuleEffectTooltip` | 模组词条（自学习）的悬浮说明 |

构造设备仍用机械窗口工厂：`MachineHelper.PowerPanel(batterySlot)` /
`Panel("模块", moduleGrid, "output", true)` / `CreateMachineryWindow(...)`，
再用 `SetupBatterySlot` / `SetupModuleBay` 接上原版电量与模组系统。

**要点**：设备注册进 `PreBuiltItemHelper.preBuiltItems`，`ModHelper.SpawnItem` 优先查这里；
自学习进度写 `modData`；一次只处理模块格里的第一件模组（原版限制）。

**界面上的电量显示（3.6.0）**：按钮标题与悬停提示由 `DeviceUi` 维护，数据来自
`DeviceLogic.Predict()` —— 一个与 `Implant`/`Learn` 同规则但**不写任何状态**的只读镜像。
之所以不在创建时算一次就完，是因为"学习"的扣费按 `PowerCost × min(StepPerUse, 剩余缺口)`
计算，接近满级时会自然变小；插拔模组或电池也会改变结果。刷新由一个
`DontDestroyOnLoad` 的 `UiDriver` 每 0.25 秒驱动；标题只在文本变化时通过
`MachineryButton.SetText()` 写入（`text` 属性为只读）。

---

## 4. 状态与持久化

| 数据 | 位置 | 备注 |
|---|---|---|
| 水龙头槽内的灯 | `PlayerStore.modData`（`EncodeItem`/`DecodeNodes`） | 自造槽位不会自动存档 |
| 容器标签文本 | `modData`，按模组前缀分键 | 不改物品原名 |
| 自学习进度 | `modData` | 随存档 |
| 求解器/UI 设置 | `ModConfigs\<mod_id>.json` | 玩家可手改，缺键回落默认 |

规则：**只写自己前缀下的键**，不要碰 `modTrust` / `modOrder` 之类的信任与排序键，
那些属于玩家点击与游戏本身。

---

## 5. 调试

首选日志文件，不要只看 F8 面板（Quantum Console 不镜像 `Debug.Log` 历史）：

```
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log
```

```bash
grep -a "\[ModLoader\] audit" "$LOG" | tail -30
```

audit 行给出的字段是本项目的判断依据：

- `source=Local` / `source=Workshop` + `workshop=<id>` + `owner=<steamid>`
- `running=True/False`、`trust=Match/Updated/NotChecked`、`caps=...`、`hash=...`

模组整条消失 = `manifest.xml` 解析失败（加载器静默跳过，不中断）。
激活被拒 = 能力声明与实际行为不一致，用 F8 控制台的 `mod-scan <mod_id>` 看扫描器的具体发现。

---

## 6. 发布流水线

```
源码 → dotnet build → 白名单打包 → GitHub Release（zip）
                              └→ steamcmd + .vdf → Steam 创意工坊
```

| 环节 | 工具 | 说明 |
|---|---|---|
| 白名单打包 | `tools/make-workshop.ps1` | 只复制 manifest、`<Assemblies>` 内 DLL、`Textures`、`preview.png`；发现 `.bat`/`.ps1`/`.zip` 即中断；复用 `publishedfileid` 与可见性 |
| 缩略图 | `tools/make-previews.py` | 512×512 像素风，32 格逻辑像素 + 最近邻放大，不引用游戏美术 |
| 简介 | `tools/workshop-descriptions/<id>.txt` + `tools/workshop-description.txt` | BBCode，压成单行（VDF 不支持换行） |
| 上传 | `steamcmd +workshop_build_item` | 首次创建、之后更新；隐藏条目无法被订阅 |
| 验证 | `steamcmd` 日志 + Steam API + `Player.log` | 三层，详见 `WORKSHOP.md` §7 |

已发布条目与 ID 见 `WORKSHOP.md`。

---

## 7. 已知限制与遗留风险

1. **`storage_bay_label` 的 `reflection-external` 必须在 2026-12-10 前移除**。
   届时该能力被游戏整体拒绝，已订阅玩家也会停止加载。
   改法：`Reflect.cs` 的 `GetType().GetField(name)` 改为直连字段访问；
   `LabelUI.cs` 的 `typeof(ItemContextHandler).GetField` / `typeof(RichTextElement).GetMethod`
   优先换成公开 API 或 `ModReflect` 提供的帮助方法。改完用 `mod-scan storage_bay_label` 验证。
2. **实机验证程度不一**：`auto_uv_lamp` 的部分交互点、`module_solo_upgrade` 的整体流程
   仍标注为"待实机确认"，各模组 README 的"已知限制"一节为准。
3. **与其他模组的重叠**：改水龙头窗口的模组（如 FaucetFilter）与 `auto_uv_lamp` 会抢同一个内容窗口。
   1.8.0 起已移除储水罐分层贴图，不再与工坊「储水罐贴图优化」冲突。
4. **本地优先于工坊**：`Mods\<id>` 存在时工坊副本被忽略（官方设计），
   测试工坊版本必须把本地目录移出 `Mods\`。

---

## 8. 经验表

| 现象 | 原因 | 处理 |
|---|---|---|
| 模组整条不显示 | `manifest.xml` 非法 | 用 XML 解析器校验；`build.bat` 已内置该校验 |
| 激活被拒，提示能力不符 | 用了未声明的能力 | 补声明，或改掉该行为（更优） |
| 自造槽位里的物品重开游戏后消失 | 未写入 `modData` | `EncodeItem` / `DecodeNodes` 往返 |
| 角标不显示 | 挂在物品节点子物体上 | 改用独立叠加层画布 |
| 工坊简介挤成一坨 | VDF 里写了 `\n` | 压成单行 + BBCode 分块 |
| 订阅了但游戏里没有工坊模组 | 条目隐藏，或本地同名目录优先 | 先公开；再把本地目录移出 `Mods\` |
| 打包第二个模组时脚本中断 | `Remove-Item -Recurse` 被安全策略拦 | 改用 `[System.IO.Directory]::Delete` |
| 预览图标题出现乱码 | Pillow 错解 `msyhbd.ttc` 的 cmap | 换 `Dengb.ttf` / `simhei.ttf` |
