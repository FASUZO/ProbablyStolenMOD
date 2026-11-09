# Probably Stolen 自制模组集

给《深空当铺：可能是偷的》（**Probably Stolen**，AppID `4348910`，Unity 2021.3 Mono）写的四个原生模组。

游戏自带模组加载器：一个模组就是一个 `Mods\<id>\` 文件夹，内含 `manifest.xml` + 实现了 `IMod` 的 DLL。
**不需要 MelonLoader / BepInEx**，也不需要任何前置框架。

- 目标游戏版本：**0.46D**（以 `Player.log` 中 `[ModLoader] audit: game …` 行为准）
- 源码语言：C# / `net48`，Harmony 补丁走游戏加载器的 `PatchAll`
- 全部模组只读写本机存档与本地文件，无任何联网行为

---

## 模组清单

| 模组 | ID | 版本 | 一句话说明 |
|---|---|---|---|
| [紫外灯水龙头](auto_uv_lamp/README.md) | `auto_uv_lamp` | 2.0.0 | 水龙头加一个紫外线灯槽位，装上即可把接出的水提纯为基础水（≈98%）并杀菌；拖出即恢复原版 |
| [存储舱标签](storage_bay_label/README.md) | `storage_bay_label` | 1.7.1 | 右键给任意容器打自定义文字标签（悬停 + 图标角标），另含储水罐分层贴图与存储舱外观变体 |
| [机器区供电布局](machine_power_layout/README.md) | `machine_power_layout` | 3.1.0 | 求解能量阵列的最优相邻摆位，最大化机器夜间回电覆盖，支持整体重排 |
| [模组独自升级](module_solo_upgrade/README.md) | `module_solo_upgrade` | 3.5.0 | 新增「自学习模块编程台」设备：耗电为模组植入自学习词条并推进进度，博士处出售 |

四个模组功能互不重叠，可同时启用；彼此没有硬依赖。

---

## 安装（用现成包）

1. 到 [Releases](../../releases) 下载需要的 `*.zip`。
2. 解压，把里面的 `<mod_id>` 整个文件夹放进游戏的 `Mods\` 目录：

   ```
   <Steam>\steamapps\common\Probably Stolen\Mods\<mod_id>\
       manifest.xml
       <Mod>.dll
       Textures\        （部分模组有）
   ```

3. 启动游戏 → 主菜单 **Mods** → 在未启用一栏选中该模组 → **Activate**。
4. **再重启一次游戏**（启用和重启是两个步骤，不能省）。
5. 回到 **Mods** 面板确认版本号与上表一致。

排查问题只看日志文件，不要只看 F8 面板（Quantum Console 不镜像 `Debug.Log` 历史）：

```
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log
```

搜索 `[ModLoader] audit:` 行，`source=Local running=True` 即加载成功；若模组整条消失，多半是 `manifest.xml` 解析失败（加载器会直接跳过，且不会报错中断）。

---

## 从源码构建

需要 .NET SDK 6.0+（`dotnet --list-sdks` 能看到版本）。每个模组目录下都有独立的 `build.bat`：

```bat
cd auto_uv_lamp
build.bat "D:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
```

脚本会依次：校验 `manifest.xml` 是否合法 → 检查游戏是否已关闭 → `dotnet build -c Release` →
把 DLL、manifest 与 `Textures\` 复制到 `Mods\<id>\`。不传参数时按常见 Steam 路径自动探测。

工程通过 `GameDir` 属性引用游戏 `Managed\` 下的程序集，全部 `Private="false"`——
**编译不会复制、也不会分发任何游戏本体文件**。

---

## 发布到 Steam 创意工坊

游戏内**没有**上传按钮。官方规定模组必须由作者用 `steamcmd` + `.vdf` 发布。完整步骤、包内容白名单、
合规红线与排查表见 **[WORKSHOP.md](WORKSHOP.md)**；打包用：

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-workshop.ps1 -ModDir auto_uv_lamp
```

脚本只复制白名单文件（`manifest.xml`、`<Assemblies>` 内列出的 DLL、`Textures/`、`preview.png`），
发现 `.bat`/`.ps1`/`.zip` 等会被扫描器拒绝的文件时直接中断，并生成填好路径的 `.vdf`。

## 仓库结构

```
ProbablyStolenMOD/
├─ auto_uv_lamp/           # 紫外灯水龙头
│  ├─ manifest.xml         # 加载器清单（ID / 显示名 / 版本 / 能力声明）
│  ├─ AutoUvLamp.csproj
│  ├─ build.bat            # 构建 + 安装
│  ├─ README.md            # 该模组的详细说明与已知限制
│  ├─ src/                 # 源码
│  ├─ tools/               # 开发期用的 API dump 脚本（不参与构建）
│  └─ Textures/            # 自定义贴图（若有）
├─ storage_bay_label/
├─ machine_power_layout/
├─ module_solo_upgrade/
├─ tools/
│  └─ make-workshop.ps1    # 生成工坊上传目录 + .vdf（白名单 + 违规拦截）
├─ WORKSHOP.md             # 发布到 Steam 创意工坊的完整流程
├─ LICENSE                 # MIT（源码与自绘贴图）
└─ README.md
```

`bin/`、`obj/`、游戏本体程序集、反编译产物已在 `.gitignore` 中排除。

---

## 版本与发布约定

- 版本号**三处必须一致**：`manifest.xml` 的 `<ModVersion>`、`*.csproj` 的 `<Version>`、该模组 `README.md` 中标注的版本。
- 加载器只在启动时扫描与缓存清单，**改完必须重启游戏**才会生效。
- 二进制走 GitHub Releases；发布到创意工坊见 `WORKSHOP.md`（用 `steamcmd` + `.vdf`）。仓库本身保持纯源码。

---

## 第三方模组参考与致谢

四个模组在开发时都读过现成模组，用来确认 API 用法、操作时序与设计取舍。**只参考思路与调用路径，未复制代码**；每个模组的具体参考点记录在其 `README.md` 末尾的「参考的第三方模组」一节。

| 本项目模组 | 参考的第三方模组 |
|---|---|
| `auto_uv_lamp` | 更好的水龙头（Faucet Filter）v1.4.5 — Henry Æveries |
| `storage_bay_label` | 存储名称显示（StorageNameDisplay）v1.3.0 — Nexus `probablystolen/mods/273`；储水罐贴图优化 v1.1.7 — chiqingsan |
| `machine_power_layout` | 智能排列 模组（ModuleSort）v0.16.0 — Power Layout；Smart Inventory Arrange v3.17.55 — xiepeixuan |
| `module_solo_upgrade` | 水瓶打印机增强（BetterBottlePrinter）v2.0.7 — n1ght、好景不长；模组堆叠升级（ModuleStack）v1.4.1 — sdjybn；店铺助手（Shop Assist）v1.4.0 — loyce |

感谢以上作者的公开作品。若原作者对记录方式或表述有异议，指出后会立即修正。

## 免责声明与版权

- 本项目与 Questing Goose Studio 无隶属关系，属于第三方非官方模组。
- `Textures/` 下的 PNG 均为本项目**自行绘制**的美术资源，与源码同为 MIT 许可，可随模组自由分发。
- 仓库内**不包含**任何游戏本体文件（`Assembly-CSharp.dll`、`0Harmony.dll`、Unity 模块、`.assets`、解包贴图等）。
- 使用模组可能影响存档，请自行备份存档目录后再启用。

## 许可

源码与自绘贴图均采用 [MIT](LICENSE)。

