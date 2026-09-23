<img width="359" height="486" alt="Mirror of Retrospection / 回溯之镜" src="https://github.com/user-attachments/assets/726aa7fe-eddc-4964-92b1-6d6344c1bc03" />

# 回溯之镜（QuickRestart）

**中文** ｜ [English](#english)

《杀戮尖塔 2》练习与救场模组 —— 把「重来」做到极致：从重开一个房间到重开整局，全部基于**完整运行快照回滚**，牌序与随机数精确重现。

**当前版本 0.1.9** ｜ 适用于游戏 **v0.111+** ｜ 依赖 [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) **≥ 0.5.18**（兼容 0.6.x）

> - 玩家向的完整功能说明与已知限制：[docs/Features.md](docs/Features.md)
> - 版本历史（新增 / 变更 / 修复 / 移除，最新在最上）：[CHANGELOG.md](CHANGELOG.md)
> - 下载：见 [Releases](https://github.com/fengzhenhong/QuickRestart/releases)

---

## 目录

- [功能](#功能)
  - [四类重启](#四类重启暂停菜单实时显示当前幕数)
  - [换角色开新局](#换角色开新局)
  - [刀下留人](#刀下留人)
  - [战后重新挑战](#战后重新挑战)
  - [快捷键](#快捷键游戏内可自定义)
- [语言与字体](#语言与字体)
- [无痕保护](#无痕保护)
- [安装](#安装)
- [配置](#配置)
- [工作原理](#工作原理简述)
- [使用限制](#使用限制)
- [更新记录](#更新记录)
- [从源码构建](#从源码构建)
- [许可](#许可)

---

## 功能

### 四类重启（暂停菜单，实时显示当前幕数）

| 按钮 | 效果 |
|---|---|
| **重启房间** | 回滚到**进房前**并自动重新进入 —— RNG 一并回滚，手牌 / 抽牌堆 / 怪物与首次进房**完全一致**（练习同一场战斗的不同打法）；**商店库存 / 事件 / 宝箱内容同样不会刷新**，反复重启都是同一批 |
| **重启本层（第 N 幕）** | 回滚到**本幕起点** —— 本幕获得的金币 / 卡牌 / 遗物 / 药水 / 血量全部回退；进入每一幕时自动保存幕初快照，**读档继续游戏后依然能回到真正的幕初** |
| **重启本局** | 同一种子从头重开 —— 无痕保护（见下） |
| **重启新局** | 同角色、新随机种子全新开局 |

### 换角色开新局

暂停菜单里的第五个按钮：**无痕丢弃当前局 → 回主菜单 → 直接停在游戏自己的角色选择屏**，在那里换角色 / 进阶 / 幕数配置重开一局。

「怎么开局」完全交给游戏本体（与手点「单人游戏 → 新游戏」同一条路径），模组只负责送你过去 —— 这样游戏更新后不会因为模组自己复刻开局流程而静默出错。

> **代价两条**：走一次主菜单，比直接路径慢约 1 秒；**当前局的存档会被删除**，之后无法用「继续游戏」回到这一局。
> 无快捷键（低频且不可逆）；每日挑战局与联机对局不显示该按钮。

### 刀下留人

生命归零时弹出「刀下留人」弹窗，战斗冻结：

- **回到地图重新挑战** —— 回滚到**进入本房间之前（选路前）**并停在地图界面：血量 / 牌组 / 金币全部回退，可重新选路，也可重新进入刚才那个节点再战
- **放弃本局** —— 同一种子从头重开

> 「回到地图重新挑战」= 这一格当作还没走过：连地图上的访问记录一并回退，所以能重新进入同一个节点。
>
> ⚠️ 只在**战斗中**生效：事件房扣血、诅咒、药水反噬等战斗外致死不会被救，按游戏官方流程正常结算。

### 战后重新挑战

战斗结算的选牌界面提供「重新挑战」按钮：不满意战利品时放弃奖励、整场重打。

### 快捷键（游戏内可自定义）

暂停菜单 → **快捷键设置** → 点击一行后按下新按键（Esc 取消），**即时生效**并写入配置。

默认键位：

| 操作 | 默认键 |
|---|---|
| 重启房间 | `R` |
| 重启本层 | `F` |
| 重启本局 | `T` |
| 重启新局 | `N` |

> 快捷键在战斗中直接可用（无需打开菜单）。**重启房间免确认**，可连按（适合反复练习同一场战斗）；重启本层 / 本局 / 新局仍会弹确认框防误触。

## 语言与字体

模组的界面文字**跟随游戏的当前语言**，字体也跟随游戏切换 —— 不需要重启游戏，下次打开对应界面即生效。

### 界面文案

| 游戏语言 | 模组文案 |
|---|---|
| 简体中文 `zhs` / 繁体中文 `zht` | 中文 |
| 其余全部语言（英 / 日 / 韩 / 俄 …） | 英文 |

> 目前只提供中英两套文案，非中文环境一律显示英文（而不是保留中文）。原因是这个模组的玩家面文字量很小，中英覆盖绝大多数使用者；如果你希望补上某门语言，欢迎提 Issue 或 PR。

文案随游戏切换的生效时机：

- **暂停菜单按钮、快捷键设置面板、各种确认弹窗** —— 下次打开时即用新语言（它们每次打开都重新取值）
- **过场提示与屏幕提示** —— 常驻控件，在下一次显示时刷新

### 字体

字体不从系统里挑，而是**问游戏自己的字体系统要**（`FontManager`），因此：

- 中文环境用游戏内置的中文字体，日文 / 韩文环境用各自字体 —— 不会出现缺字形
- 不需要玩家机器上装有微软雅黑之类的系统字体（对 Steam Deck / Linux / 英文 Windows 尤其重要）

> 实现上优先取 `FontManager.GetSubstituteFont(当前语言, Regular)`；该语言不需要字体替换时（英 / 西 / 法等），退一步借用游戏控件当前生效的主题字体。

## 无痕保护

所有重启操作（房间 / 本层 / 本局 / 新局）、换角色开新局与刀下留人，**都不会污染你的战绩**：

- **不增加失败** —— 被重启的旧局面不会写入挑战历史，不会被记为一次失败
- **不断连胜** —— 连胜（win streak）不会因重启而中断
- **不记录成就** —— 旧局面静默消失，仿佛从未发生

> 实现方式：重启期间临时关闭游戏的存档写入开关（`RunManager.ShouldSave`），新局自动恢复 —— 与游戏正常情况下「放弃本局并结算」的流程完全隔离。
>
> ⚠️ **一处如实说明（v0.1.6 起写清）**：回滚走的是游戏**官方读档流程**，而该流程自带「读档次数 +1 并写一次存档」（`SaveManager.IncrementNumReloads`，不受 `ShouldSave` 约束）。
> 所以战绩里的**读档次数（NumReloads）会把每次重启 / 回滚计入**，结算时该计数也会随对局指标上传；历史、连胜、成就这三项不受影响。
> 如果哪天这条实现变了（例如游戏把该写入也纳入 `ShouldSave`），本段需要同步更新。

## 安装

1. 安装 [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib)（前置库 mod）
2. 将 `QuickRestart` 文件夹（含 `QuickRestart.dll` 与 `QuickRestart.json`）放入游戏目录的 `mods/` 下
3. 启动游戏即可

## 配置

首次运行自动生成：

```
%APPDATA%\SlayTheSpire2\mod_data\QuickRestart\settings.json
```

| 字段 | 默认 | 说明 |
|---|---|---|
| `RestartRoomKey` | `R` | 重启房间快捷键 |
| `RestartFloorKey` | `F` | 重启本层快捷键 |
| `RestartRunKey` | `T` | 重启本局快捷键 |
| `NewRunKey` | `N` | 重启新局快捷键 |
| `EnableDeathIntercept` | `true` | 刀下留人开关 |
| `EnablePostCombatRetry` | `true` | 战后重新挑战按钮开关 |
| `ShowConfirmationDialog` | `true` | 确认弹窗开关（重启本层 / 本局 / 新局与重新挑战；**重启房间不受此开关影响，始终直接执行**） |
| `FastRestartSkipMenu` | `true` | 重启跳过主菜单中转，显著缩短黑屏；异常时可关闭回退旧流程 |

> 键位可用字母 / 数字 / 功能键（如 `R`、`Space`、`F1`）。在游戏里改键会立即写回本文件。
>
> 旧版本 settings.json 里的 `RetryCombatKey`、`EnableWinStreakProtection` 两个字段已删除：
> 前者从未接上任何按键读取（改了没反应）、后者没有实现。残留在这两个字段上的旧配置值会被自动忽略。

## 工作原理（简述）

- **重启 = 快照回滚**：进房前 / 幕起点 / 开局自动保存完整运行快照（含 RNG 状态），重启经游戏官方读档流程（`RunState.FromSerializable` → `SetUpSavedSingleplayer`）精确回滚 —— 与「继续游戏」共用同一套状态恢复逻辑，不做手工状态修补
- **复刻读档但不走 `LoadRun`**：恢复场景时复刻 `NGame.LoadRun` 的前半段，**刻意跳过它内部的 `LoadIntoLatestMapCoord`** —— 那一步会「重进最后访问的节点」并向地图点历史追加一格，表现就是「回滚一次，地图自己前进一层」。落点改由流程自己决定：重启本层 / 回到地图停在**地图屏**，重启房间按原坐标**重新进入本节点**
- **幕初快照持久化**：每次真实进入新幕时，幕初快照同时写入磁盘（`%APPDATA%\SlayTheSpire2\mod_data\QuickRestart\act_start_snapshot.json`，仅保留最新一份，约 45~100KB）—— 因此「读档继续游戏」后按 `F` 也能回滚到**真正的幕初**，而不是读档那一刻
- **刀下留人 = 双重拦截**：同时拦截逐玩家死亡处理 `HandlePlayerDeath` 与失败标记 `LoseCombat`，拦截后立即复活玩家并冻结战斗，等待玩家选择
  > 拦截 `HandlePlayerDeath` 时会返回一个已完成任务。它是 `async Task` 方法，跳过时不补返回值会让调用方的 `await` 拿到 `null` 并抛异常、进而打死整场战斗的回合循环（v0.1.8 修复）。
- **无痕**：重启过程中旧局面不写入 run 历史、不中断连胜、不记失败（详见[无痕保护](#无痕保护)）

## 使用限制

### 功能不可用的场景

| 场景 | 四类重启 | 换角色开新局 | 战后重新挑战 | 刀下留人 |
|---|:---:|:---:|:---:|:---:|
| 单人普通局 | ✅ | ✅ | ✅ | ✅ |
| 每日挑战局 | ❌ | ❌ | ❌ | ✅ |
| 联机对局 | ❌ | ❌ | ❌ | ❌ |

- **联机对局完全静默**：快捷键不响应（有文字提示）、菜单不显示按钮、生命归零不拦截 —— 本地单方面回滚必然被校验和判定为状态分歧。
- **每日挑战局禁用整族「重启」**：按了会提示「每日挑战局不支持」。原因是重开一局拿不到每日结算依据（`DailyTime` 不在对局状态里），而「重启新局」等于对同一份每日种子无限重 roll。
  **但「刀下留人」照常生效**，只是两个出路的语义不同：【确认】回到地图重新挑战 = 照常原地回滚（不改种子、不重开局）；【取消】放弃本局 = 走游戏官方的「放弃本局」正常结算（不再是普通局那个「无痕同种子重开」，那会丢每日结算依据）。

### 不触发的情况

- **输入环境**：以下情况快捷键会被主动忽略（原因写进日志），避免误触造成不可逆回滚 —— 游戏窗口失焦、焦点在文本输入框、有模态弹窗打开、开发控制台打开。
- **刀下留人只在战斗中生效**：两个拦截点都在 `CombatManager`（逐玩家死亡处理与战斗失败标记），所以事件房扣血、诅咒、药水反噬等**战斗外**致死不会被救 —— 这类死亡按游戏官方流程正常结算，不是漏拦截。

### 降级与耗时

- **降级路径**：若「点击节点前」快照不可用（例如房间不是从地图节点进入），重启房间会退化为「新建同名房间重进」，此时**只回退血量**、牌序重新随机。日志里会记一条 WARN 说明走了这条路。
- **重启耗时不是一个数**（真机实测，v0.1.8）：

  | 操作 | 实测耗时 | 说明 |
  |---|---|---|
  | 重启本层 / 回到地图 | **0.6 ~ 0.8 秒** | 纯回滚 |
  | 重启房间 | **0.8 ~ 0.9 秒** | 除回滚外还要重建并重新进入本节点 |
  | 重启本局 / 重启新局 | **2.5 ~ 2.6 秒** | 大头是游戏的角色与幕资源预载 + 地图生成 |
  | 换角色开新局 | **约 1.35 秒** | 走一次主菜单 |

  耗时大头是游戏的资源预载与地图生成，不是黑幕动画。首次重启（尚未缓存过资源）会明显偏慢。

## 更新记录

完整版本历史见 **[CHANGELOG.md](CHANGELOG.md)** —— 按版本倒序，分「新增 / 变更 / 修复 / 移除」四类。
本文件不重复记录，避免两处说明各说各话。

## 从源码构建

### 目录结构

```
QuickRestart/
├── QuickRestart.csproj      ← 工程文件必须在仓库根（见下）
├── QuickRestart.json        ← 加载器清单（MOD 身份是英文 id "QuickRestart"，显示名「回溯之镜」在 json 里）
├── README.md / CHANGELOG.md / LICENSE / .gitignore / .gitattributes / local.props.example
├── docs/Features.md         ← 玩家向功能说明与已知限制
└── src/                     ← 全部 C# 源码，统一单一命名空间 QuickRestart
    ├── Entry.cs                 mod 入口：装补丁、启停、日志里的版本号常量
    ├── SettingsManager.cs       配置读写（settings.json，原子写）
    ├── NetGuard.cs              联机 / 每日挑战局守卫
    ├── Patches/                 挂在游戏方法上的 Harmony 补丁（含死亡拦截、菜单与按钮注入、快照记录钩子）
    ├── Restart/                 重启与回滚流程（RestartService 的 partial 分工，见下）
    ├── Snapshots/               选路前 / 幕初快照 + 磁盘持久化
    └── UI/                      过场黑幕、屏幕提示、快捷键面板、原生确认弹窗
        ├── ModLoc.cs            界面文案（中英双语，跟随游戏语言）
        └── ModFonts.cs          字体解析（跟随游戏字体）+ 语言切换订阅
```

> `RestartService` 是 `internal static partial class`，按职责分成七个文件：`RestartService.cs`（守卫 + 房间 / 本层）、
> `.Restore.cs`（回滚骨架 + 把 RunState 交回游戏读档）、`.NewRun.cs`（重开整局与换角色）、`.Rewind.cs`（战后重挑战与回到地图）、
> `.Scene.cs`（淡入淡出与计时）、`.GameApi.cs`（ShouldSave 反射等最小封装）、
> `.AssetGuard.cs`（建 run 场景前的资源预检 —— 直接路径跳过主菜单，这一步的竞态会让 Godot 原生崩进程）。
> **新增流程请开新 partial 文件**，别把 `RestartService.cs` 重新写成大杂烩。

> ⚠️ **别把 `QuickRestart.csproj` 挪进 `src/`** —— 本工程用 `Godot.NET.Sdk`，它以 csproj 所在目录为项目根
> （`.godot/` 与编译中间目录都挂在那儿），另外 csproj 里有两处按仓库根算的相对路径：
> `Import Project="./local.props"` 与 `CopyMod` 里的 `$(MSBuildProjectDirectory)/QuickRestart.json`。
> 移进 `src/` 要么直接编译失败，要么构建悄悄不再部署 json。要改请连带这三处一起改并重新验证构建。

- 需要 .NET 9 SDK
- 复制 `local.props.example` 为 `local.props`，修改为你的游戏路径
- 构建：

```powershell
dotnet build QuickRestart.csproj -c Release
```

构建产物自动复制到 `$(ModOutputDir)`（默认：游戏目录下 `mods/QuickRestart`）。

| 想做什么 | 命令 |
|---|---|
| 只验证能否编译、**不碰** `mods/` | `dotnet build -c Release -p:CopyModOnBuild=false` |
| 正常构建并部署到 `mods/` | `dotnet build -c Release` |

> ⚠️ 不要用 `dotnet build -t:Compile` 做验证 —— 它绕过完整构建链、破坏 `obj/` 增量，
> 会让嵌入资源与后续复制步骤丢失（要恢复得删 `obj/`、`bin/` 重新构建）。
>
> ⚠️ **游戏版本更新后构建失败**（报「找不到 RitsuLib」）多半是 `local.props` 里的
> `RitsuLibDir` 还指着旧的 compat 目录。RitsuLib 0.6.x 按游戏 API 版本分目录，
> 改成新版本即可：`mods/STS2-RitsuLib/compat/<游戏API版本>`（例如 `0.111.0`）。
> 工程里的 `ValidateSts2GameInstall` 会在校验不到 `sts2.dll` / `STS2-RitsuLib.dll` 时
> 直接终止构建，属于刻意设计（宁可构建失败，也不要装一个跑不起来的 DLL）。
>
> ℹ️ 反射依赖提示：本工程用 Krafs.Publicizer 放开游戏内部成员，但它**只改编译期可见性** ——
> 私有成员直调会在运行期抛 `MethodAccessException` / `FieldAccessException`。
> 因此 `RunManager.ShouldSave` 的 setter 与 `RunManager._startTime` 一律走反射
>（见 `RestartService.TrySetShouldSave` 与 `RoomEntryTracker.CurrentRunStart` 的注释），
> 新增反射目标时请沿用这个约定并核对游戏更新后的字段名。

## 许可

MIT © 2026 CodeArts —— 详见 [LICENSE](LICENSE)。
前置库 [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) 同为 MIT，本模组与它许可兼容。

---
---

<a id="english"></a>

# QuickRestart (Mirror of Retrospection)

[中文](#回溯之镜quickrestart) ｜ **English**

A practice & rescue mod for *Slay the Spire 2* — restarting, taken to its limit. Every kind of restart, from re-entering a single room to restarting a whole run, is built on a **full run-state snapshot rollback**, so card order and RNG are reproduced exactly.

**Current version 0.1.9** | Requires game **v0.111+** | Depends on [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) **≥ 0.5.18** (0.6.x compatible)

> - Full player-facing feature list & known limitations: [docs/Features.md](docs/Features.md) (Chinese)
> - Version history (newest first): [CHANGELOG.md](CHANGELOG.md) (Chinese)
> - Download: see [Releases](https://github.com/fengzhenhong/QuickRestart/releases)

---

## Table of Contents

- [Features](#features)
  - [Four kinds of restart](#four-kinds-of-restart-pause-menu-shows-the-current-act)
  - [Switch character](#switch-character)
  - [Second Chance](#second-chance-death-interception)
  - [Post-combat retry](#post-combat-retry)
  - [Hotkeys](#hotkeys-rebindable-in-game)
- [Language & fonts](#language--fonts)
- [Trace-free protection](#trace-free-protection)
- [Installation](#installation)
- [Configuration](#configuration)
- [How it works](#how-it-works)
- [Limitations](#limitations)
- [Building from source](#building-from-source)
- [License](#license)

---

## Features

### Four kinds of restart (pause menu, shows the current act)

| Button | Effect |
|---|---|
| **Restart Room** | Rolls back to **before you entered the room** and re-enters it automatically — RNG is rolled back too, so your hand, draw pile and monsters are **identical** to the first entry (perfect for practising one fight a different way). **Shop stock, event outcomes and chest contents do not reroll either** — every restart gives the same batch |
| **Restart Act (Act N)** | Rolls back to **the start of this act** — gold, cards, relics, potions and HP gained during the act are all reverted. An act-start snapshot is saved automatically each time you enter an act, so pressing `F` **after loading a save still returns you to the real act start** |
| **Restart Run** | Restarts from the beginning with the same seed — trace-free protection (see below) |
| **New Run** | Same character, brand new random seed |

### Switch character

The fifth button in the pause menu: **discard the current run without a trace → back to the main menu → land directly on the game's own character select screen**, where you pick a character / ascension / act count and start fresh.

"How a run starts" is left entirely to the game itself (the same path as clicking *Singleplayer → New Game*); the mod only takes you there — that way a game update can't silently break a re-implementation of the start-up flow.

> **Two costs, stated plainly**: it goes through the main menu, so it's about 1 second slower than the direct path; and **the current run's save file is deleted**, so you can't come back to it via *Continue* afterwards.
> No hotkey (low frequency, irreversible); the button is hidden in daily runs and multiplayer.

### Second Chance (death interception)

When your HP hits zero, a popup appears and combat freezes:

- **Back to Map and Retry** — rolls back to **before you entered this room (before routing)** and stops at the map: HP, deck and gold are all reverted. You can re-route, or re-enter that same node and fight again
- **Give Up This Run** — restart from the beginning with the same seed

> "Back to Map and Retry" treats that node as never visited: map visit history is rolled back too, which is what lets you re-enter the same node.
>
> ⚠️ Only works **in combat**: HP loss in event rooms, curses, potion backlash and other out-of-combat deaths are not intercepted — those resolve normally through the game's own flow.

### Post-combat retry

The card-reward screen after a fight gets a **Retry Fight** button: discard the rewards and replay the whole fight.

### Hotkeys (rebindable in game)

Pause menu → **Key Bindings** → click a row, then press the new key (Esc to cancel). Takes effect **immediately** and is written to the config file.

Default bindings:

| Action | Default key |
|---|---|
| Restart Room | `R` |
| Restart Act | `F` |
| Restart Run | `T` |
| New Run | `N` |

> Hotkeys work directly in combat (no need to open the menu). **Restart Room skips confirmation** and can be spammed (ideal for drilling one fight); Restart Act / Restart Run / New Run still show a confirmation dialog to prevent misclicks.

## Language & fonts

The mod's interface text and fonts **follow the game's current language** — no game restart required; it applies the next time you open the relevant screen.

### Interface text

| Game language | Mod text |
|---|---|
| Simplified Chinese `zhs` / Traditional Chinese `zht` | Chinese |
| Every other language (English / Japanese / Korean / Russian …) | English |

> Only Chinese and English are provided. Non-Chinese environments show English (not Chinese). The mod has very little player-facing text, and these two cover the vast majority of users — if you'd like another language, feel free to open an Issue or a PR.

When the switch takes effect:

- **Pause menu buttons, the key-bind panel, and all confirmation popups** — use the new language the next time they're opened (they re-read the text every time)
- **Transition and on-screen toasts** — resident controls, refreshed the next time they're shown

### Fonts

Fonts are not picked from the system — the mod **asks the game's own font system** (`FontManager`), which means:

- Chinese uses the game's built-in Chinese font; Japanese / Korean use their own — no missing glyphs
- Players don't need system fonts like Microsoft YaHei installed (especially important on Steam Deck, Linux, and English Windows)

> Implementation: it prefers `FontManager.GetSubstituteFont(currentLanguage, Regular)`; when the language needs no font substitution (English / Spanish / French …), it falls back to borrowing the theme font currently in effect on a game control.

## Trace-free protection

All restart operations (Room / Act / Run / New Run), Switch Character, and Second Chance **never pollute your record**:

- **No extra losses** — the discarded run is not written to run history and is not counted as a loss
- **No broken win streak** — your win streak is not interrupted by a restart
- **No achievements recorded** — the old state vanishes silently, as if it never happened

> How: the game's save-write switch (`RunManager.ShouldSave`) is temporarily turned off during a restart and restored for the new run — completely isolated from the game's normal "give up and settle" flow.
>
> ⚠️ **One honest caveat (spelled out since v0.1.6)**: rollback goes through the game's **official load-save flow**, which by itself does "reload count +1 and write the save once" (`SaveManager.IncrementNumReloads`, not gated by `ShouldSave`).
> So the **reload count (NumReloads) in your record does count every restart / rollback**, and that number is uploaded with the run's metrics at settlement. History, win streak and achievements are not affected.
> If that implementation ever changes (e.g. the game puts that write behind `ShouldSave`), this section needs updating.

## Installation

1. Install [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) (the required library mod)
2. Put the `QuickRestart` folder (containing `QuickRestart.dll` and `QuickRestart.json`) into the game's `mods/` directory
3. Launch the game

## Configuration

Generated automatically on first run:

```
%APPDATA%\SlayTheSpire2\mod_data\QuickRestart\settings.json
```

| Field | Default | Description |
|---|---|---|
| `RestartRoomKey` | `R` | Restart Room hotkey |
| `RestartFloorKey` | `F` | Restart Act hotkey |
| `RestartRunKey` | `T` | Restart Run hotkey |
| `NewRunKey` | `N` | New Run hotkey |
| `EnableDeathIntercept` | `true` | Second Chance on/off |
| `EnablePostCombatRetry` | `true` | Post-combat retry button on/off |
| `ShowConfirmationDialog` | `true` | Confirmation dialog on/off (Restart Act / Run / New Run and retry; **Restart Room ignores this and always executes directly**) |
| `FastRestartSkipMenu` | `true` | Skip the main-menu detour when restarting, greatly shortening the black screen; disable to fall back to the old flow if you hit problems |

> Keys can be letters / digits / function keys (`R`, `Space`, `F1` …). Rebinding in game writes back to this file immediately.
>
> Two fields from older settings.json files, `RetryCombatKey` and `EnableWinStreakProtection`, have been removed:
> the former was never wired to any key read (changing it did nothing), the latter was never implemented. Leftover values are ignored automatically.

## How it works

- **Restart = snapshot rollback**: a full run-state snapshot (including RNG state) is saved before entering a room, at an act start, and at run start. Restarting rolls back precisely through the game's official load flow (`RunState.FromSerializable` → `SetUpSavedSingleplayer`) — the same state-restoration logic *Continue* uses, with no hand-patched state
- **Load flow replicated, but `LoadRun` deliberately avoided**: restoring the scene replicates the first half of `NGame.LoadRun` while **deliberately skipping its `LoadIntoLatestMapCoord`** — that step re-enters the last visited node and appends to the map-point history, which shows up as "one rollback, and the map advances a floor by itself". The landing spot is decided by each flow instead: Restart Act / Back to Map stop at the **map screen**, Restart Room **re-enters the same node** by its original coordinate
- **Act-start snapshot persistence**: each time you genuinely enter a new act, the act-start snapshot is also written to disk (`%APPDATA%\SlayTheSpire2\mod_data\QuickRestart\act_start_snapshot.json`, only the latest kept, roughly 45–100 KB) — which is why pressing `F` **after loading a save** still rolls back to the **real act start** rather than to the moment you loaded
- **Second Chance = double interception**: it intercepts both the per-player death handler `HandlePlayerDeath` and the loss flag `LoseCombat`, then immediately revives the player and freezes combat while you choose
  > Intercepting `HandlePlayerDeath` returns a completed task. It's an `async Task` method, and skipping it without supplying a return value makes the caller's `await` receive `null`, throw, and take down the whole combat turn loop (fixed in v0.1.8).
- **Trace-free**: during a restart the old state is not written to run history, does not break the win streak, and is not recorded as a loss (see [Trace-free protection](#trace-free-protection))

## Limitations

### Where features are unavailable

| Scenario | Four restarts | Switch Character | Post-combat retry | Second Chance |
|---|:---:|:---:|:---:|:---:|
| Singleplayer normal run | ✅ | ✅ | ✅ | ✅ |
| Daily run | ❌ | ❌ | ❌ | ✅ |
| Multiplayer | ❌ | ❌ | ❌ | ❌ |

- **Multiplayer is completely silent**: hotkeys don't respond (with a text hint), the menu doesn't show the buttons, and death is not intercepted — a local one-sided rollback would inevitably be flagged as state divergence by the checksum.
- **Daily runs disable the whole "restart" family**: pressing a hotkey shows "daily runs do not support …". The reason is that restarting a run can't obtain the daily settlement basis (`DailyTime` is not part of the run state), and "New Run" amounts to endlessly rerolling the same daily seed.
  **But Second Chance still works**, only with different semantics for its two options: [Confirm] Back to Map and Retry = the usual in-place rollback (seed unchanged, run not restarted); [Cancel] Give Up This Run = the game's official "give up" settlement (no longer the normal run's "trace-free same-seed restart", which would lose the daily settlement basis).

### When it doesn't trigger

- **Input environment**: in these cases hotkeys are deliberately ignored (the reason is logged) to avoid a misclick causing an irreversible rollback — game window unfocused, focus in a text field, a modal popup open, or the dev console open.
- **Second Chance only works in combat**: both interception points are in `CombatManager` (the per-player death handler and the combat loss flag), so **out-of-combat** deaths such as event-room HP loss, curses or potion backlash are not rescued — those settle normally through the game's own flow. This is by design, not a missed interception.

### Degradation & timings

- **Degraded path**: if the "before clicking the node" snapshot is unavailable (e.g. the room wasn't entered from a map node), Restart Room degrades to "create a fresh room of the same kind and re-enter"; in that case **only HP is rolled back** and card order is rerandomised. A WARN line in the log notes that this path was taken.
- **Restart timing is not a single number** (measured on real hardware, v0.1.8):

  | Action | Measured | Notes |
  |---|---|---|
  | Restart Act / Back to Map | **0.6 – 0.8 s** | Pure rollback |
  | Restart Room | **0.8 – 0.9 s** | Also rebuilds and re-enters the node |
  | Restart Run / New Run | **2.5 – 2.6 s** | Mostly the game's character & act asset preload plus map generation |
  | Switch Character | **~1.35 s** | Goes through the main menu |

  Most of the time is the game's asset preload and map generation, not the fade animation. The first restart (before assets are cached) is noticeably slower.

## Changelog

The full version history lives in **[CHANGELOG.md](CHANGELOG.md)** — newest first, split into Added / Changed / Fixed / Removed (in Chinese).
This file doesn't duplicate it, so the two can't drift apart.

## Building from source

### Directory layout

```
QuickRestart/
├── QuickRestart.csproj      ← project file must stay at the repo root (see below)
├── QuickRestart.json        ← loader manifest (mod id is the English "QuickRestart"; display name lives in the json)
├── README.md / CHANGELOG.md / LICENSE / .gitignore / .gitattributes / local.props.example
├── docs/Features.md         ← player-facing feature list & known limitations (Chinese)
└── src/                     ← all C# source, single namespace QuickRestart
    ├── Entry.cs                 mod entry: registers patches, enable/disable, version constant used in logs
    ├── SettingsManager.cs       settings read/write (settings.json, atomic write)
    ├── NetGuard.cs              multiplayer / daily-run guards
    ├── Patches/                 Harmony patches on game methods (death interception, menu & button injection, snapshot hooks)
    ├── Restart/                 restart & rollback flows (RestartService partials, see below)
    ├── Snapshots/               pre-route / act-start snapshots + disk persistence
    └── UI/                      transition overlay, toasts, key-bind panel, native confirm popups
        ├── ModLoc.cs            interface text (Chinese/English, follows game language)
        └── ModFonts.cs          font resolution (follows the game's font) + locale-change subscription
```

> `RestartService` is an `internal static partial class` split by responsibility across seven files: `RestartService.cs` (guards + Room / Act),
> `.Restore.cs` (rollback skeleton + handing RunState back to the game's load flow), `.NewRun.cs` (new run & switch character), `.Rewind.cs` (post-combat retry & back to map),
> `.Scene.cs` (fades and timing), `.GameApi.cs` (minimal wrappers such as the ShouldSave reflection),
> `.AssetGuard.cs` (asset pre-check before building the run scene — the direct path skips the main menu, and a race there makes Godot crash natively).
> **Add a new partial file for new flows** rather than turning `RestartService.cs` back into a grab bag.

> ⚠️ **Don't move `QuickRestart.csproj` into `src/`** — this project uses `Godot.NET.Sdk`, which treats the csproj's directory as the project root
> (`.godot/` and the build intermediates hang off it), and the csproj has two root-relative paths:
> `Import Project="./local.props"` and `$(MSBuildProjectDirectory)/QuickRestart.json` in the `CopyMod` target.
> Moving it into `src/` either fails to build outright or silently stops deploying the json. If you must, change all three together and re-verify the build.

- Requires the .NET 9 SDK
- Copy `local.props.example` to `local.props` and set your game path
- Build:

```powershell
dotnet build QuickRestart.csproj -c Release
```

The build output is copied automatically to `$(ModOutputDir)` (default: `mods/QuickRestart` under your game directory).

| Goal | Command |
|---|---|
| Verify it compiles **without touching** `mods/` | `dotnet build -c Release -p:CopyModOnBuild=false` |
| Normal build and deploy to `mods/` | `dotnet build -c Release` |

> ⚠️ Don't use `dotnet build -t:Compile` to verify — it bypasses the full build chain and breaks `obj/` incrementality,
> losing embedded resources and the later copy steps (recover by deleting `obj/` and `bin/` and rebuilding).
>
> ⚠️ **If the build fails after a game update** ("can't find RitsuLib"), it's usually because `RitsuLibDir` in `local.props`
> still points at an old compat directory. RitsuLib 0.6.x splits directories by game API version —
> just point it at the new one: `mods/STS2-RitsuLib/compat/<game API version>` (e.g. `0.111.0`).
> The `ValidateSts2GameInstall` target aborts the build when it can't find `sts2.dll` / `STS2-RitsuLib.dll`;
> that's deliberate (better to fail the build than ship a DLL that can't run).
>
> ℹ️ Reflection note: this project uses Krafs.Publicizer to open up game internals, but it **only changes compile-time visibility** —
> calling private members directly throws `MethodAccessException` / `FieldAccessException` at runtime.
> That's why the `RunManager.ShouldSave` setter and `RunManager._startTime` always go through reflection
> (see the comments on `RestartService.TrySetShouldSave` and `RoomEntryTracker.CurrentRunStart`).
> Follow that convention for new reflection targets, and re-check field names after game updates.

## License

MIT © 2026 CodeArts — see [LICENSE](LICENSE).
The required library [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) is also MIT, and the licenses are compatible.
