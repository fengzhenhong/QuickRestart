# 回溯之镜（QuickRestart）

《杀戮尖塔 2》练习与救场模组 —— 把「重来」做到极致：从重开一个房间到重开整局，全部基于**完整运行快照回滚**，牌序与随机数精确重现。

> 适用于游戏 v0.111+，依赖 [STS2-RitsuLib](https://github.com/)（≥ 0.5.18）。

## 功能

### 四类重启（暂停菜单，实时显示当前幕数）

| 按钮 | 效果 |
|---|---|
| **重启房间** | 回滚到**进房前**并自动重新进入 —— RNG 一并回滚，手牌/抽牌堆/怪物与首次进房**完全一致**（练习同一场战斗的不同打法）|
| **重启本层（第 N 幕）** | 回滚到**本幕起点** —— 本幕获得的金币 / 卡牌 / 遗物 / 药水 / 血量全部回退 |
| **重启本局** | 同一种子从头重开（无痕：不写入历史、不中断连胜）|
| **重启新局** | 同角色、新随机种子全新开局 |

### 暴毙拦截

生命归零时弹出拦截弹窗，战斗冻结：

- **重新挑战当前战斗** —— 回滚到进房前并重进（同上"重启房间"）
- **放弃本局** —— 同一种子从头重开

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

> 快捷键在战斗中直接可用（无需打开菜单）；防误触由确认弹窗承担。

## 安装

1. 安装 [STS2-RitsuLib](https://github.com/)（前置库 mod）
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
| `EnableDeathIntercept` | 暴毙拦截开关 |
| `EnablePostCombatRetry` | 战后重新挑战按钮开关 |
| `ShowConfirmationDialog` | 确认弹窗开关 |

## 工作原理（简述）

- **重启 = 快照回滚**：进房前 / 幕起点 / 开局自动保存完整运行快照（含 RNG 状态），重启经游戏官方读档流程（`RunState.FromSerializable → SetUpSavedSingleplayer → LoadRun`）精确回滚 —— 与"继续游戏"同一条路径，不做手工状态修补
- **死亡拦截 = 双重拦截**：同时拦截 `HandlePlayerDeath` 与失败标记 `LoseCombat`，拦截后立即复活玩家并冻结战斗，等待玩家选择
- **无痕**：重启过程中旧局面不写入 run 历史 / 不中断连胜

## 从源码构建

- 需要 .NET 9 SDK
- 复制 `local.props.example` 为 `local.props`，修改为你的游戏路径
- 构建：

```powershell
dotnet build QuickRestart.csproj -c Release
```

构建产物自动复制到 `$(ModOutputDir)`（默认：游戏目录下 `mods/QuickRestart`）。

## 许可

<!-- 待作者添加：如 MIT / GPL-3.0 等 -->
