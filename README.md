<img width="359" height="486" alt="回溯之镜" src="https://github.com/user-attachments/assets/726aa7fe-eddc-4964-92b1-6d6344c1bc03" />
# 回溯之镜（QuickRestart）

《杀戮尖塔 2》练习与救场模组 —— 把「重来」做到极致：从重开一个房间到重开整局，全部基于**完整运行快照回滚**，牌序与随机数精确重现。

> 适用于游戏 v0.111+，依赖 [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib)（≥ 0.5.18，兼容 0.6.x）。
>
> 玩家向的完整功能说明与已知限制：[docs/Features.md](docs/Features.md)
> 版本历史（新增 / 变更 / 修复 / 移除，最新在最上）：[CHANGELOG.md](CHANGELOG.md)

## 功能

### 四类重启（暂停菜单，实时显示当前幕数）

| 按钮 | 效果 |
|---|---|
| **重启房间** | 回滚到**进房前**并自动重新进入 —— RNG 一并回滚，手牌/抽牌堆/怪物与首次进房**完全一致**（练习同一场战斗的不同打法）；**商店库存 / 事件 / 宝箱内容同样不会刷新**，反复重启都是同一批 |
| **重启本层（第 N 幕）** | 回滚到**本幕起点** —— 本幕获得的金币 / 卡牌 / 遗物 / 药水 / 血量全部回退；进入每一幕时自动保存幕初快照，**读档继续游戏后依然能回到真正的幕初** |
| **重启本局** | 同一种子从头重开 —— 无痕保护（见下）|
| **重启新局** | 同角色、新随机种子全新开局 |

### 换角色开新局

暂停菜单里的第五个按钮：**无痕丢弃当前局 → 回主菜单 → 直接停在游戏自己的角色选择屏**，在那里换角色 / 进阶 / 幕数配置重开一局。"怎么开局"完全交给游戏本体（与手点「单人游戏 → 新游戏」同一条路径），模组只负责送你过去。

> 代价：走一次主菜单，比直接路径慢约 1 秒；**当前局的存档会被删除**，之后无法用「继续游戏」回到这一局。无快捷键；每日挑战局与联机对局不显示该按钮。

### 刀下留人

生命归零时弹出「刀下留人」弹窗，战斗冻结：

- **回到地图重新挑战** —— 回滚到**进入本房间之前（选路前）**并停在地图界面：血量 / 牌组 / 金币全部回退，可重新选路，也可重新进入刚才那个节点再战
- **放弃本局** —— 同一种子从头重开

> 「回到地图重新挑战」= 这一格当作还没走过：连地图上的访问记录一并回退，所以能重新进入同一个节点。

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

## 无痕保护

所有重启操作（房间 / 本层 / 本局 / 新局）、换角色开新局与刀下留人，**都不会污染你的战绩**：

- **不增加失败** —— 被重启的旧局面不会写入挑战历史，不会被记为一次失败
- **不断连胜** —— 连胜（win streak）不会因重启而中断
- **不记录成就** —— 旧局面静默消失，仿佛从未发生

> 实现方式：重启期间临时关闭游戏的存档写入开关（`RunManager.ShouldSave`），新局自动恢复 —— 与游戏正常情况下"放弃本局并结算"的流程完全隔离。
>
> ⚠️ **一处如实说明（v0.1.6 起写清）**：回滚走的是游戏**官方读档流程**，而该流程自带"读档次数 +1 并写一次存档"（`SaveManager.IncrementNumReloads`，不受 `ShouldSave` 约束）。
> 所以战绩里的**读档次数（NumReloads）会把每次重启/回滚计入**，结算时该计数也会随对局指标上传；历史、连胜、成就这三项不受影响。
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

| 字段 | 说明 |
|---|---|
| `RestartRoomKey` 等四项 | 快捷键（字母/数字/功能键，如 `R`、`Space`、`F1`）|
| `EnableDeathIntercept` | 刀下留人开关 |
| `EnablePostCombatRetry` | 战后重新挑战按钮开关 |
| `ShowConfirmationDialog` | 确认弹窗开关（重启本层/本局/新局与重新挑战；**重启房间不受此开关影响，始终直接执行**）|
| `FastRestartSkipMenu` | 重启跳过主菜单中转（默认开启，显著缩短黑屏；异常时可关闭回退旧流程）|

> 旧版本 settings.json 里的 `RetryCombatKey`、`EnableWinStreakProtection` 两个字段已删除：
> 前者从未接上任何按键读取（改了没反应）、后者没有实现。残留在这两个字段上的旧配置值会被自动忽略。

## 工作原理（简述）

- **重启 = 快照回滚**：进房前 / 幕起点 / 开局自动保存完整运行快照（含 RNG 状态），重启经游戏官方读档流程（`RunState.FromSerializable → SetUpSavedSingleplayer → LoadRun`）精确回滚 —— 与"继续游戏"同一条路径，不做手工状态修补
- **幕初快照持久化**：每次真实进入新幕时，幕初快照同时写入磁盘（`%APPDATA%\SlayTheSpire2\mod_data\QuickRestart\act_start_snapshot.json`，仅保留最新一份，约 45~100KB）—— 因此"读档继续游戏"后按 `F` 也能回滚到**真正的幕初**，而不是读档那一刻
- **刀下留人 = 双重拦截**：同时拦截 `HandlePlayerDeath` 与失败标记 `LoseCombat`，拦截后立即复活玩家并冻结战斗，等待玩家选择
- **无痕**：重启过程中旧局面不写入 run 历史、不中断连胜、不记失败（详见上文"无痕保护"）

## 使用限制

- **联机对局**：完全不可用（快捷键 / 按钮 / 死亡拦截全部禁用）—— 本地单方面回滚必然被校验和判定为状态分歧
- **每日挑战局**：四类"重启"（房间 / 本层 / 本局 / 新局）与战后"重新挑战"按钮全部不可用，按了会提示"每日挑战局不支持" —— 重开一局拿不到每日结算依据（`DailyTime` 不在对局状态里），而"重启新局"等于对同一份每日种子无限重 roll。
  **但「刀下留人」在每日局照常生效**，只是两个出路的语义不同：【确认】回到地图重新挑战 = 照常原地回滚（不改种子、不重开局）；【取消】放弃本局 = 走游戏官方的"放弃本局"正常结算（不再是普通局那个"无痕同种子重开"，那会丢每日结算依据）
- **输入环境**：以下情况快捷键不触发（避免误触造成不可逆回滚）—— 游戏窗口失焦、焦点在文本输入框、有模态弹窗打开、开发控制台打开
- **降级路径**：若"点击节点前"快照不可用（例如房间不是从地图节点进入），重启房间退化为"新建同名房间重进"，此时**只回退血量**、牌序重新随机
- **刀下留人只在战斗中生效**：两个拦截点都在 `CombatManager`（逐玩家死亡处理与战斗失败标记），所以事件房扣血、诅咒、药水反噬等**战斗外**致死不会被救 —— 这类死亡按游戏官方流程正常结算，不是漏拦截
- **重启耗时不是一个数**：纯回滚（重启本层 / 回到地图）实测约 0.7 秒；重启房间约 0.9~1.0 秒（还要重建并重进本节点）；重开整局（重启本局 / 新局）约 2.8~3.2 秒，耗时大头是游戏的角色/幕资源预载与地图生成，不是黑幕动画

## 更新记录

完整版本历史已独立成 **[CHANGELOG.md](CHANGELOG.md)**（按版本倒序、分「新增 / 变更 / 修复 / 移除」类别）。
本文件不再重复记录，避免两处说明各说各话；当前版本：**0.1.8**（2026-09-22）。

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
> ⚠️ **游戏版本更新后构建失败**（报"找不到 RitsuLib"）多半是 `local.props` 里的
> `RitsuLibDir` 还指着旧的 compat 目录。RitsuLib 0.6.x 按游戏 API 版本分目录，
> 改成新版本即可：`mods/STS2-RitsuLib/compat/<游戏API版本>`（例如 `0.111.0`）。
> 工程里的 `ValidateSts2GameInstall` 会在校验不到 `sts2.dll` / `STS2-RitsuLib.dll` 时
> 直接终止构建，属于刻意设计（宁可编译不也不要装一个跑不起来的 DLL）。
>
> ℹ️ 反射依赖提示：本工程用 Krafs.Publicizer 放开游戏内部成员，但它**只改编译期可见性** ——
> 私有成员直调会在运行期抛 `MethodAccessException` / `FieldAccessException`。
> 因此 `RunManager.ShouldSave` 的 setter 与 `RunManager._startTime` 一律走反射
>（见 `RestartService.TrySetShouldSave` 与 `RoomEntryTracker.CurrentRunStart` 的注释），
> 新增反射目标时请沿用这个约定并核对游戏更新后的字段名。

## 许可

MIT © 2026 CodeArts —— 详见 [LICENSE](LICENSE)。
前置库 [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) 同为 MIT，本模组与它许可兼容。
