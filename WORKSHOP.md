# 发布到 Steam 创意工坊

本文针对《深空当铺：可能是偷的》（AppID **4348910**）。结论与步骤全部依据官方文档
[`dragonpan2/probably-stolen-modding`](https://github.com/dragonpan2/probably-stolen-modding) 的
`ModdingGuide.md` § Steam Workshop 整理，并已对应到本仓库四个模组的实际情况。

## 结论先行

- **游戏内没有「上传」按钮**。官方原文：*"Mods are published by their authors with steamcmd, never by the game on a player's account."*
- 工坊条目**就是一个 mod 文件夹**——`manifest.xml` 在根，加 DLL 与 `Textures/`。代码一行都不用改。
- 发布 = `steamcmd` + 一个 `.vdf` 描述文件。首次上传需在 Steam 网页接受一次工坊法律协议。
- 游戏的 Mods 菜单会读工坊条目：显示你上传的预览图、提供 Steam 按钮与「取消订阅」，订阅后仍需玩家手动 **Activate + 重启**。

## 一、准备工具

`steamcmd` 已安装在 `D:\SteamCMD\`（2026-11-10 完成自更新，并通过匿名登录自检）。
换一台机器时：

1. 下载 <https://developer.valvesoftware.com/wiki/SteamCMD>（Windows 版是个 zip），解压到固定目录，例如 `D:\SteamCMD`。
2. 首次运行会自动更新自身：`D:\SteamCMD\steamcmd.exe`。
3. 登录账号必须**拥有本游戏**：

```
steamcmd +login <你的Steam账号> +quit
```

首次 `workshop_build_item` 上传时，Steam 会要求在网页上接受一次创意工坊法律协议，接受后才能成功。

## 二、包内容：只放这几样

官方扫描器会检查整个工坊条目文件夹，**本地开发目录不能直接上传**。合规白名单：

| 可放 | 说明 |
|---|---|
| `manifest.xml` | 必须在根目录，缺失则整包被跳过 |
| `<你声明的>.dll` | 必须与 `<Assemblies>` 逐项对应 |
| `Textures/`、图片、音频、字体、`.txt`/`.md` | 允许，无体积上限（条目文件数上限 50,000） |
| `preview.png` | 可选，**小于 1 MB**；工坊有 `previewfile` 时优先用后者 |

| 禁止 | 原因 |
|---|---|
| `.bat` `.ps1` `.js` `.lnk` `.exe` | 脚本/可执行文件一律被扫描器拒绝 |
| `.zip`、安装包、`.psd`/`.aseprite` 源文件 | 官方明确要求排除 |
| `.dll` 未在 `<Assemblies>` 列出 | 会**拒绝整模组激活** |
| `.git` `.vs` `.idea` | 本地目录会被跳过，但**工坊条目整包检查**，必须排除 |
| `.pdb` | 允许但无必要，别传 |

本仓库的 `build.bat`、`tools/` 都在模组目录内，属于**开发用文件**——不能进包，用下面的脚本自动剔除。

## 三、打包

用仓库自带脚本生成合规包，它只复制白名单文件，并在发现违规文件时直接报错：

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-workshop.ps1 -ModDir auto_uv_lamp
```

输出：

```
workshop\auto_uv_lamp\        ← 干净的上传目录（manifest + DLL + Textures + preview.png）
workshop\auto_uv_lamp.vdf     ← 描述文件，路径已填好
```

### 预览图

四个模组各有一张 **512×512** 的 `preview.png`（约 30–33 KB），像素风、与游戏 UI 同配色，
由 `tools/make-previews.py` 生成，改了文案或配色可重新跑：

```powershell
python tools\make-previews.py            # 四个全出
python tools\make-previews.py auto_uv_lamp
```

版式为「主图标 + 改前 / 改后对比条」：上半是放大的主体物件，下半左右各一格，
左灰右亮、中间箭头，让缩略图在小尺寸下也能表达"装上之后变了什么"，而不只是"这是什么"。

脚本用 `DengXian Bold` / `SimHei` 渲染中文标题——**不要改用 `msyhbd.ttc`**：
Pillow 在这里会错解它的 cmap，把「灯/水/标」等常用字渲染成 `.notdef` 乱码。

## 四、上传

`.vdf` 内容形如（`make-workshop.ps1` 已自动生成，下面供核对）：

```
"workshopitem"
{
    "appid"            "4348910"
    "publishedfileid"  "0"
    "contentfolder"    "D:\\Repository\\ProbablyStolenMOD\\workshop\\auto_uv_lamp"
    "previewfile"      "D:\\Repository\\ProbablyStolenMOD\\workshop\\auto_uv_lamp\\preview.png"
    "visibility"       "0"
    "title"            "紫外灯水龙头"
    "description"      "给水龙头加紫外线灯槽位……"
    "changenote"       "首次发布"
}
```

- `publishedfileid` 填 `0` = **新建**条目；成功后 steamcmd 会把真实 ID 写回该文件，**务必保存**。
- `visibility`：`0` 公开 / `1` 仅好友 / `2` 隐藏。**首次发布建议先 `2` 隐藏**，自己订阅验证无误后再改 `0`。

### 简介里的 GitHub 链接

每个条目的简介 = `manifest.xml` 的 `<Description>` + `tools/workshop-description.txt` 的内容，
后者含 **GitHub 仓库链接**、安装步骤与反馈指引，`{url}` / `{id}` / `{version}` 会被自动替换：

```
源码、说明与更新日志：https://github.com/FASUZO/ProbablyStolenMOD

安装：解压得到 auto_uv_lamp 文件夹，整个放进游戏的 Mods 目录，进游戏后到 Mods 菜单激活，然后重启一次游戏生效。
```

因此**改简介不要改 VDF**——直接编辑 `tools/workshop-description.txt` 再跑一次 `make-workshop.ps1` 即可。
换仓库地址时用 `-RepoUrl` 覆盖，或改脚本默认值。

执行：

```
D:\SteamCMD\steamcmd.exe +login <账号> +workshop_build_item "D:\Repository\ProbablyStolenMOD\workshop\auto_uv_lamp.vdf" +quit
```

## 五、更新已发布条目

把 `.vdf` 里的 `publishedfileid` 保持为首次上传写回的 ID，改好 `changenote` 再跑同一条命令即可——已有订阅者会自动收到更新，不需要重新发条目。注意三点：

- 改功能就**递增 `manifest.xml` 的 `<ModVersion>`**，玩家 Mods 菜单靠它显示版本。
- 游戏版本变了要更新 `<GameVersions>`，否则 Mods 菜单的兼容性颜色会标红。
- 工坊**不会**自动订阅 `<Prerequisites>` 里的依赖，需在描述里写明让玩家自己订阅。

## 六、本仓库四个模组的发布状态

| 模组 | 声明能力 | 能否直接发 | 说明 |
|---|---|---|---|
| `auto_uv_lamp` | 空 | ✅ 可以 | 无反射、无文件/网络访问 |
| `machine_power_layout` | 空 | ✅ 可以 | 仅有 `e.GetType().Name`（异常名，非反射） |
| `module_solo_upgrade` | 空 | ✅ 可以 | 无反射、无文件访问 |
| `storage_bay_label` | `reflection-external` | ⚠️ **先改代码** | 该能力**自 2026-12-10 起被游戏拒绝**，届时激活会失败 |

`storage_bay_label` 的反射点在 `src/Reflect.cs`（`target.GetType().GetField(name)`）与
`src/LabelUI.cs`（`typeof(ItemContextHandler).GetField`、`typeof(RichTextElement).GetMethod`）。
官方规则：`typeof(游戏类型)` 本身免费，**运行时用字符串拼成员名**才需要该能力；而该能力已在废弃通道上。
所以发布前要做的不是「声明它」，而是**把这两处改写成直连访问**（游戏内这几个成员的类型是明确的），
改完用 F8 控制台 `mod-scan storage_bay_label` 确认不再出现 `reflection-external`。

## 七、合规红线（均出自官方文档）

- **能力扫描**：用了但没声明 → 激活被拒；声明了 → 玩家激活前会看到提示。自查命令 `mod-scan <mod_id>`。
- **`files` / `network` 需开发者验证**：2026-12-10 起，工坊模组若声明这两项，必须先进开发者的签名白名单
  （仓库里的 `trust-list.json`），否则无法激活。申请方式是到社区 modding 频道提供工坊条目与被读写/联网的对象。
  **本仓库四个模组都不需要这两项**，无需排队。
- **补丁目标限制**：不得对 `ModLoader`、`ModTrust*`、`ModScanner`、`ModWorkshop`、`SteamUGC` 等加载器/信任/Steam 类型打补丁。
- **不得自带 `0Harmony.dll`**，用游戏自带的那份。
- **不得用 `Harmony.UnpatchAll(null)`**。
- 玩家首次打开 Mods 菜单会被告知「模组可能含代码」——行为不端的模组会损害整个社区的口碑。

## 八、排查

| 现象 | 原因 |
|---|---|
| 工坊条目订阅后 Mods 菜单里不出现 | 订阅发生在游戏运行中，需重启游戏 |
| 条目在列表里但激活被拒 | 能力未声明，或包内有未列入 `<Assemblies>` 的 DLL |
| 条目整条不显示 | `manifest.xml` 解析失败，加载器静默跳过 |
| Mods 菜单没有横幅图 | 未上传 `previewfile`，且文件夹里没有 `preview.png` |
| 上传报协议未接受 | 首次发布需先在 Steam 网页接受工坊法律协议 |
