# 紫外灯水龙头（UV Faucet）—《深空当铺：可能是偷的》

给水龙头加一个紫外线灯槽位。**把灯拖进槽位就装上，拖出来就卸下**，全程在游戏里用鼠标操作，不需要改任何设置。

| 水龙头槽位 | 接出来的水 |
|---|---|
| 装着紫外线灯（`uv_filter`） | 直接提到**基础水（≈98%）**，并顺手杀一次菌 |
| 空着 / 放了别的东西 | **完全不处理**，就是原版的水 |

官方格式 mod（`manifest.xml` + `IMod`），由游戏自带载入器加载，**不需要 MelonLoader**。

> 内部 ID 与安装目录仍是 `auto_uv_lamp`（沿用旧版，便于自动覆盖升级），只有显示名改成了「紫外灯水龙头」。

---

## 0. 三步用起来

1. **装**：装 .NET SDK 8 → 双击本文件夹的 `build.bat`（已默认指向 `D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen`）。它会编译并把 `manifest.xml` + `AutoUvLamp.dll` 放进 `Mods\auto_uv_lamp\`。
   > 这一步不能跳过：**没编译过的源码目录不会出现在游戏 Mods 列表里**（列表只认 `Mods\<id>\manifest.xml` + DLL）。
2. **开**：启动游戏 → 主菜单 **Mods** → 在 Inactive 列表里选中 **紫外灯水龙头** → **Activate** → 按提示**再重启一次**。
3. **玩**：双击水龙头打开窗口 → 把背包里的紫外线灯拖进 **紫外线灯** 格 → 去接水。把灯拖出来就恢复原版的水。

进店后要等约 3 秒窗口才出现（那段延迟是给「更好的水龙头」让路，见第 8 节），之后只要灯装着就一直有效。

---

## 1. 它做什么

一条路径，一个效果：

| 环节 | 说明 |
|---|---|
| 触发 | 从水龙头接水（拦截 `WaterHelper.AddWater` 的后置补丁） |
| 前置 | 水龙头槽位里装着 `uv_filter` |
| 提纯 | `WaterHelper.PurifyToBaseWater(该容器)`：按比例削减非水成分直到约 98%，并更新瓶子的「推定水质」 |
| 杀菌 | 对该容器调一次紫外线灯（`WaterHelper.HandleFilter`，只对 `microbe` 类液体） |
| 消耗 | **没有**。不用净水器、不耗耐久、不吃净水片、不占背包格 |

**不做什么**（都是有意为之）：

| 不做 | 原因 |
|---|---|
| 不再在倒水 / 睡前 / 醒来自动扫全店 | 2.0.0 已移除，现在只有「水龙头装灯」这一条路径 |
| 不刷新瓶子上的水质标签（紫外线那一步） | 原版就必须重新用试纸 / AquaScan 才更新；mod 不替你改标签，也不会冲掉伪装标签。注意提纯那一步会更新「推定水质」，因为游戏自己的净水器也这么做 |
| 不动酒瓶 | `AddWater` 也服务倒酒，`PurifyToBaseWater` 会把酒精一起削减 |
| 不自动吃净水片 | `ChemicalHelper.DissolveInto` 内部会 `GameItem.Destroy()` 销毁净水片，等于擅自消耗你的库存 |
| 不改界面 / 不加升级项 | 不碰 `NetworkUpgrade`（会污染王尔德的升级界面，出现空卡片） |

---

## 2. 槽位是怎么造出来的

原版水龙头**没有可放东西的地方**，这一格是自己造的：

| 步骤 | 用什么 | 说明 |
|---|---|---|
| 1. 造内容窗口 | `MachineHelper.Panel(label, slot, role, true)` → `MachineHelper.CreateMachineryWindow(true, panels)` → `faucet.SetContentWindow(window)` | 与售水机同一套 `MachineryPanel` 渲染。水龙头本体只是个带 sprite 和标签的纯 `GameItem`（`MiscItemDirectory.Faucet`） |
| 2. 造格子 | `new GameSlotInventory(5, 1, false, false)` | 5×1 是因为 `uv_filter` 的占用形状就是 5×1，尺寸对上才能拖进去 |
| 3. 只让灯放得进来 | `slot.mayInventoryAddItemFunc = (item, inv) => item.identifier == "uv_filter"` | 这一行就是「装配门控」，别的物品拖上去会被拒绝 |
| 4. 状态存在哪 | **就是格子里的实物** | 不写配置、不占设置项；`Lamp()` 读的就是 `slot.childItems` 里的第一个 `uv_filter` |

为什么不能直接用现有槽位：`EmporiumEntry.faucetElement` 那个 `GameSlotInventory` **装的是水龙头本体自己**（`AddAlwayItems` 里 `TryAcceptAll(faucetElement, faucet)`，与 dossier / trashcan / drain 同一套路），已经占满。

这套做法是照 `steamapps\workshop\content\4348910\3833421581\src\` 里那个模组的源码实现的（它把 `.cs` 和 PDB 一起发布了）。

---

## 3. 存档怎么处理

水龙头是固定设施、**不在存档的物品列表里**，所以它窗口里的东西游戏不会帮你保存。不做处理的话，返回主菜单再进来，装好的灯就没了。所以：

| 环节 | 做法 |
|---|---|
| 存 | `PlayerStore.EncodeItem(灯)` 得到引擎自己的物品 JSON，写进 `ModHelper.SetModData("auto_uv_lamp", "faucet_lamp", json)`——它落在 `PlayerStore.modData`，随存档槽文件序列化，**天然按存档隔离** |
| 时机 | 每 2 秒检查一次（内容变了才写）；另加 `PlayerStore.SaveGame` 的**前置补丁**，保证过夜流程里 `EndNight → SaveGame` 那一下之前把状态写进去 |
| 读 | 窗口建好时 `SaveManager.DecodeNodes` 还原，`GraphUtils.TryAcceptAll` 放回格子；放不进去就销毁并打警告（不会静默吞物品） |
| 防串档 | 用 `PlayerStore.saveSlotId` 比对；切换/新建存档的瞬间清空槽位并跳过写入 |

编码时的一个坑已处理：`EncodeItem` 会把 itemFeatures 追加进 `PlayerStore.savedItemFeatureList`（SaveGame 专用列表，存档开头会整体重置）。我们不在存档流程内，所以快照长度后截断，不污染整档。

> ⚠️ **卸载本 mod 前，请先把灯从水龙头格里拖出来。** 那盏灯是 mod 自己存的；mod 不在了就没人还原它，等于丢一件物品。

---

## 4. 构建与安装

需要 **.NET SDK 6.0 或更高**（`dotnet --list-sdks` 能列出；只有运行时不行）。

```bat
cd /d <本文件夹>
build.bat
```

脚本会构建并把 `AutoUvLamp.dll` + `manifest.xml` 复制到
`D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen\Mods\auto_uv_lamp\`。
游戏装在别处时：`build.bat "另一个路径"`。

手动构建：

```bat
dotnet build -c Release -p:GameDir="D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
```

目录结构必须是（`manifest.xml` 与 DLL 同级，不能多一层嵌套）：

```
Probably Stolen\Mods\auto_uv_lamp\
├── manifest.xml
└── AutoUvLamp.dll
```

启动游戏 → 主菜单 **Mods** → Inactive 里选中 **紫外灯水龙头** → Activate → 按提示**再重启一次**。

---

## 5. 设置项

主菜单 Mods → 本模组详情页 → 设置（存在存档目录 `ModConfigs\auto_uv_lamp.json`）。

| 键 | 默认 | 含义 |
|---|---|---|
| `mode` | `auto` | 杀菌方式：`auto` = 先走游戏滤芯函数、无效则直接清 `microbe`；`filter` = 只用游戏逻辑；`direct` = 直接清 |
| `convert_to_water` | 开 | 微生物转成水（水量不减）；关掉则直接消失、水量略减 |
| `verbose_log` | 关 | 追加逐液体 `before -> after` 明细 |

> **装配/卸下、是否提纯都不在这里**：完全由「水龙头槽位里有没有灯」决定，把灯拖进拖出即可。

---

## 6. 日志

> **为什么日志是英文**：游戏的 F8 控制台用 `OfficeCodePro` 字体，**没有中文字形**，中文会被逐个替换成空格（`Player.log` 里能看到一堆 `The character with Unicode value \u5904 was not found ...`），看起来就像「没有输出」。所以日志前缀固定为 `[UV Faucet]`（纯 ASCII），正文也一律英文；Mods 列表与设置面板仍是中文（那里用的是游戏主字体）。

**接水（槽位里装着灯）**——先一行提纯结果，再一行该瓶的杀菌结果：

```
[UV Faucet]: faucet boost: large_bottled_water (2000 ml cap) | purity 91.01% -> 98.00% | water 1250 -> 1250 ml | total 1250 -> 1211 ml
[UV Faucet]:   fill: large_bottled_water (2000 ml cap): microbes -3.77 ml | purity 98.00% -> 98.31% | water 1184 -> 1187 ml | restored +3.77 ml
```

`water` 不变而 `total` 略降是正常的——被削减的是非水成分（杂质 / 矿物质）。

打开 `verbose_log` 后追加逐液体明细，这是「只杀了微生物、没误删其它成分」的直接依据（除 `microbe` 外都应是 `x -> x`）：

```
[UV Faucet]:     liquids: microbe 3.77 -> 0 ml; water 1184 -> 1187 ml
```

槽位空着时（`verbose_log` 打开才打）：

```
[UV Faucet]: faucet: slot is empty, water left untouched
```

**F8 面板不镜像日志**：截图里那个 `Enter Command...` 面板是 Quantum Console（QFSW.QC），默认不显示完整的 `Debug.Log` 历史。可靠的方式是看 `Player.log`，或实时跟踪：

```powershell
Get-Content "$env:USERPROFILE\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log" -Wait -Tail 20 |
  Select-String "UV Faucet"
```

---

## 7. 排查

| 现象 | 可能原因 | 怎么处理 |
|---|---|---|
| 双击水龙头没有「紫外线灯」格 | mod 未激活 / 未重启；或另一个龙头模组已建窗口 | Mods 面板确认已 Activate 并重启过；日志里若有 `faucet already has a content window from another mod` 就把灯装到那个窗口里 |
| 灯拖不进去，松手弹回背包 | 槽位尺寸与 `uv_filter` 形状不符 | 日志会有 `slot refused`；改 `FaucetWindow.BuildWindow` 里的 `new GameSlotInventory(5, 1, …)` |
| 接水没变化 | 槽位空着；或这瓶水已经 ≥98% | 打开 `verbose_log` 看 `slot is empty` / `already …%` |
| 重进游戏后灯不见了 | 还原失败 | 日志有 `could not restore the installed lamp`，把 `Player.log` 里 `[UV Faucet]` 的行贴出来 |
| 激活被拒 | 补丁目标对不上 | F8 里执行 `mod-scan auto_uv_lamp`，按提示处理 |

本 mod 的属性补丁只有两处：`WaterHelper.AddWater`（后置）与 `PlayerStore.SaveGame`（前置，无参）。若游戏更新改了签名，`PatchAll` 会在**激活阶段**直接失败（而不是静默降级），这是属性补丁的代价。

---

## 8. 与「更好的水龙头 / Faucet Filter」共存

`steamcommunity.com/sharedfiles/filedetails/?id=3833421581`（作者 Henry Æveries，v1.2.2，游戏 0.46D）。

两者的装配方式**互不冲突**：

- 它有更完整的改装体系（滤芯、第二滤芯位、管道检测器）。它给水龙头建窗口后，本 mod 检测到 `contentWindow` 已存在就**不重建、不接管**，只做只读兜底 —— 在它的槽位里找 `uv_filter`，所以**它装的灯本 mod 同样认**。
- 反过来，为了避免本 mod 抢先建窗口把它挤掉（水龙头只能有一个 `contentWindow`，谁先建谁赢），本 mod 进店后会**先等 3 秒**再建自己的窗口。它是在进店后立刻建的，所以总是它先。

若你没有装它，3 秒后本 mod 自己建窗口；这 3 秒是唯一能感知到的代价。

它声明了 `files` 与 `reflection-external` 两项能力（早期版本写外部存档文件、并反射读 `PlayerStore.networkUpgrade`）；本 mod 两项都不需要，安装时的权限提示更少。

---

## 9. 已对照本机程序集核对的接口

代码按 `Probably Stolen_Data\Managed\Assembly-CSharp.dll` 的实际签名写，并用 Cecil 读过参考模组 DLL 的 TypeRef / MemberRef 双向确认：

| 用途 | 实际签名 |
|---|---|
| 造窗口 | `MachineHelper.Panel(String, PixelElement, String, Boolean)` → `MachineryPanel`；`MachineHelper.CreateMachineryWindow(Boolean, MachineryPanel[])` → `PixelWindow`；`GameItem.SetContentWindow(PixelWindow)` |
| 槽位 | `new GameSlotInventory(int w, int h, bool stealUI, bool isUIDown)`（`base=GameInventory`，`childItems` 是 `List<GameItem>`）；`SetMachineryStyle()`；`SetBackgroundFadeSprite(String, String, Single, Single)` |
| 槽里的物品存档 | `PlayerStore.EncodeItem(GameItem)` → String；`SaveManager.DecodeNodes(List<SaveItemNode>)` → `GameItem`；`ModHelper.SetModData/GetModData`；`PlayerStore.saveSlotId`（字段） |
| 接水 | `WaterHelper.AddWater(GameItem, int, int, bool, int, int, bool)`（唯一重载） |
| 提纯 | `WaterHelper.PurifyToBaseWater(GameItem)`（纯数据操作，不扣耐久） |
| 杀菌 | `WaterHelper.HandleFilter(string identifier, int amount, GameItem sourceContainer, GameItem filter)` → 剩余份数 |
| 容器判定 | tag `LIQUID_CONTAINER_TAG`、`LIQUID_CONTAINER_CAPACITY`；`WaterHelper.IsDrain/IsFaucet`；`WineHelper.CanModifyLiquid` |
| 液体 | `Liquid.Liquids`（`List<Liquid>`）、`Liquid.currentPart`、`Liquid.GetCurrentPartFromId`；1 份 = 1 µl |
| 标签读写 | `GameItem.IsTag / GetTagReadonly(t).GetInt() / ModifyTag(t, Action<TagState>, bool skipValidate)` |
| 日志 | `ModLog(ModManifest)` 构造时把 `manifest.Name` **复制**进 `modName`，格式 `"[{0}]: {1}"` |

- 游戏版本：`0.46D`（本机 8 个创意工坊 mod 的 `manifest.xml` 一致；Unity 2021.3.45f2，Mono 后端）。
- `uv_filter` 在程序集里**没有任何字面量**，属于数据驱动物品，挂 `WATER_FILTER` 标记走通用滤芯函数。

---

## 10. 尚未实机验证的点

源码已在本机 `dotnet build -c Release` 编译通过（0 错误 0 警告，签名另行逐条核对），但**尚未进游戏实机验证**，以下各点仍需跑一次确认：

| 点 | 若不成立的表现 |
|---|---|
| 5×1 槽位能塞进 `uv_filter` | 灯拖不进格子、松手弹回，日志有 `slot refused` |
| `EncodeItem` → `DecodeNodes` 能往返 | 重进游戏后灯不在格里，日志有 `could not restore` |
| 3 秒让路是否够 | 日志有 `window built` 而对方模组功能失效，此时把 `GiveWaySeconds` 调大 |
| 提纯后纯度是否真到 98% | 日志里 `faucet boost` 那一行的 `purity ... -> ...` 不到 98，说明瓶里有 `PurifyToBaseWater` 不处理的特例成分 |

编译报错请把输出贴回来，我按行修。

---

## 参考的第三方模组

| 模组 | 作者 | 来源 | 参考了什么 |
|---|---|---|---|
| 更好的水龙头（Faucet Filter）v1.4.5 | Henry Æveries | Steam 创意工坊 `3833421581` | ① 确认「UV 灯并入原生 `HandleFilter` 过滤路径」这一做法可行——其说明明确写着行为等同于容器内直接装 `uv_filter`；② 其发布包附带的 `src` 与 DLL 给出了 `GameSlotInventory(5,1,false,false)`、`MachineHelper.Panel` / `CreateMachineryWindow` 的真实调用签名，本模组据此确定造窗口与造槽位的参数；③ 其社区反馈里记录的两类坑（只接收 `microbe` 类液体、需跳过酒类容器）写进了本模组的过滤判断。 |

边界说明：本模组**独立重写**，未复制上述模组的任何源码文件；其 `src` 仅用于校对 API 签名与调用顺序。

