using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace QuickRestart;

/// <summary>
/// 五个重启/回滚流程的编排入口：模式守卫、重入保护，以及"重启房间""重启本层"两条流程。
/// 其余部分按职责分居同目录下的 RestartService.*.cs（同一个 partial class）。
/// </summary>
internal static partial class RestartService
{
    public enum RestartType
    {
        RestartRoom,
        RestartFloor,
        RestartRun,
        NewRun,
        SwitchCharacter,
    }

    /// <summary>0=空闲 1=重启进行中。一次重启要跨多帧（回主菜单→等帧→读档，耗时数秒），
    /// 期间快捷键仍可触发 —— 无重入保护会让两条流程交错（场景切换/读档互相踩），黑屏或状态错乱。</summary>
    private static int _busy;

    /// <summary>
    /// 「回滚 / 读档 / 重启」类功能的统一模式守卫（所有入口共用：快捷键、暂停菜单按钮、
    /// 刀下留人弹窗回调、战后重新挑战）。命中时已给出屏幕提示，调用方直接 return。
    /// <para>⚠️ 这是**每个入口都必须调**的防线，不能只靠"联机下不注入按钮"——
    /// 按钮注入点与执行点之间的任何一条旁路（如重新挑战按钮、弹窗回调）都会绕过它。</para>
    /// </summary>
    /// <param name="allowDaily">
    /// 每日挑战局是否放行。<c>false</c>=整族"重启"（房间/本层/本局/新局/战后重挑战）一律拒绝 ——
    /// 重开一局拿不到每日结算依据（<c>DailyTime</c> 不在对局状态里），也等于无限重 roll 每日种子。
    /// <c>true</c>=允许"回到地图"这类**原地回滚**（不改种子、不重开局，刀下留人的出路之一）。
    /// </param>
    internal static bool IsBlockedByMode(string what, bool allowDaily = false)
    {
        // 联机：本地回滚必然被校验和判定为状态分歧（详见 NetGuard）
        if (NetGuard.IsMultiplayer())
        {
            Entry.Logger?.Warn($"[QuickRestart] 联机模式下不支持{what}，已忽略");
            ModToast.Show($"联机模式下不可用（{Entry.ModName}仅支持单人）");
            return true;
        }
        // 每日挑战：重开类操作拒绝（详见 NetGuard.IsDailyRun）
        if (!allowDaily && NetGuard.IsDailyRun())
        {
            Entry.Logger?.Warn($"[QuickRestart] 每日挑战局不支持{what}，已忽略");
            ModToast.Show($"每日挑战局不支持{what}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 所有对外入口（快捷键 / 暂停菜单按钮 / 弹窗回调 / 战后重新挑战）共用的壳子：
    /// 模式守卫 → 重入保护 → 过场黑幕 → 兜底捕获。
    /// <para>⚠️ 守卫必须放在这里而不是各入口：按钮注入点的"联机/每日不注入"只挡住了菜单这一条路，
    /// 快捷键与弹窗回调都是旁路（详见 <see cref="IsBlockedByMode"/>）。</para>
    /// <para>⚠️ 重入保护必须有：一次流程要跨多帧数秒，期间快捷键仍可触发，两条流程交错会互相踩场景切换与读档。</para>
    /// <para>⚠️ 兜底捕获必须有：入口全是 fire-and-forget（按钮回调里 <c>_ = ...()</c>），
    /// 异常逃逸会变成未观察任务异常，玩家只看到"按了没反应"。</para>
    /// </summary>
    private static async Task RunGuardedAsync(string what, Func<Task> body, bool allowDaily = false)
    {
        if (IsBlockedByMode(what, allowDaily))
            return;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            Entry.Logger?.Warn($"[QuickRestart] 已有流程进行中，忽略{what}");
            return;
        }
        try
        {
            RestartOverlay.Show();
            await body();
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] {what}失败: {ex}");
        }
        finally
        {
            RestartOverlay.Hide();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>重启类型 → 玩家可读名称（守卫提示、重入提示与兜底日志共用同一句人话）。</summary>
    private static string NameOf(RestartType type) => type switch
    {
        RestartType.RestartRoom => "重启房间",
        RestartType.RestartFloor => "重启本层",
        RestartType.RestartRun => "重启本局",
        RestartType.NewRun => "重启新局",
        _ => "换角色开新局",
    };

    public static Task ExecuteRestart(RestartType type) => RunGuardedAsync(NameOf(type), async () =>
    {
        Entry.Logger?.Info($"[QuickRestart] 执行重启 type={type}");
        switch (type)
        {
            case RestartType.RestartRoom:
                await RestartRoomAsync();
                break;
            case RestartType.RestartFloor:
                await RestartFloorAsync();
                break;
            case RestartType.RestartRun:
                await RestartRunAsync(sameSeed: true);
                break;
            case RestartType.NewRun:
                await RestartRunAsync(sameSeed: false);
                break;
            case RestartType.SwitchCharacter:
                await SwitchCharacterAsync();
                break;
        }
    });

    private static async Task RestartRoomAsync()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 无法获取 RunState，跳过重启房间");
            return;
        }

        var room = state.CurrentRoom;
        if (room == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 当前无房间，跳过重启房间");
            return;
        }

        Entry.Logger?.Info($"[QuickRestart] 重启房间 RoomId={room.Id} Type={room.GetType().Name}");

        ClosePauseMenu();

        // ★ 刀下留人场景：拦截时调用了 CombatManager.Pause()，战斗回合循环卡在
        //   `while (IsPaused && turnState.IsLive)`；不解除的话读档/退出流程永远等不到。
        EnsureCombatUnpaused();

        RoomType roomType = room.RoomType;
        // 本房间的地图坐标：回滚后用它"重新进入本节点"（等价于第一次点击该节点）
        MapCoord? roomCoord = state.CurrentMapCoord;

        // ★ 用「点击节点前」快照（PreMapPointSnapshot，捕获于 EnterMapCoord/AddVisitedMapCoord 之前）：
        //   此时房间类型**尚未 roll**（问号节点的类型由 Odds.UnknownMapPoint.Roll 决定并消耗 RNG）。
        //   回滚后走"重新进入本节点"会重演这次 roll、用的正是同一份 RNG → 问号房内容与首次完全一致。
        var preSnap = RoomEntryTracker.PreMapPointSnapshot;
        if (preSnap == null
            || !RoomEntryTracker.IsSnapshotForCurrentRun(preSnap, RoomEntryTracker.PreMapPointRunStart, "重启房间"))
        {
            // 兜底（快照缺失，或属于上一局——玩家用游戏自带流程开新局时旧快照残留）：
            // 新建房间重进 —— 只回退血量，牌序会重新随机
            Entry.Logger?.Warn("[QuickRestart] 无可用进房前快照（缺失或属于上一局），回退为新建房间重进（仅回退血量、牌序重新随机）");
            try
            {
                RoomEntryTracker.RestorePreRoomHp();
                await RunManager.Instance.EnterRoom(RebuildRoomInstance(room, state));
                Entry.Logger?.Info("[QuickRestart] 重启房间完成（回退路径）");
            }
            catch (Exception ex)
            {
                // 经"重新挑战"按钮进来时调用，异常若逃逸会变成未观察任务异常
                Entry.Logger?.Error($"[QuickRestart] 重启房间失败（回退路径）: {ex}");
            }
            return;
        }

        Entry.Logger?.Info("[QuickRestart] 重启房间：回滚到进房前（RNG 一并回滚，牌序将与首次进房一致）");
        if (await RollbackAsync("重启房间", preSnap, rs => EnterRoomAgainAsync(rs, roomCoord, "重启房间")) < 0)
            return;

        // ══════════════════════════════════════════════════════════════════
        // 房间内容由快照 RNG 决定 → 与首次进房完全一致（牌序相同、商店库存不刷新）。
        // "重新进入本节点"走 EnterRoomAgainAsync（AddVisitedMapCoord + EnterMapPointInternal，
        // 等价于第一次点击该节点）—— 不走"自动重进最后访问节点"，
        // 那条路会 AppendToMapPointHistory，让地图历史凭空前进一格。
        // ══════════════════════════════════════════════════════════════════
        var restoredRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
        Entry.Logger?.Info($"[QuickRestart] 重启房间完成：当前房间={restoredRoom?.RoomType.ToString() ?? "无"}（期望 {roomType}；内容与首次一致，商店不会刷新）");
    }

    private static async Task RestartFloorAsync()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 无法获取 RunState，跳过重启本层");
            return;
        }

        int actIndex = state.CurrentActIndex;

        // 刀下留人等场景可能把战斗置于暂停态，先解除（无暂停时为无操作）
        EnsureCombatUnpaused();

        var snapshot = ActSnapshotStore.Snapshot;

        // 快照缺失/不属于当前幕/属于上一局（跨局残留）→ 回退旧行为：仅重进本幕（不回滚物品）
        if (snapshot == null || ActSnapshotStore.SnapshotActIndex != actIndex
            || !RoomEntryTracker.IsSnapshotForCurrentRun(snapshot, ActSnapshotStore.SnapshotRunStart, "重启本层"))
        {
            Entry.Logger?.Warn($"[QuickRestart] 无第 {actIndex + 1} 幕起点快照（缺失/非本幕/属于上一局），回退为仅重进本幕");
            ClosePauseMenu();
            await RunManager.Instance.EnterAct(actIndex);
            return;
        }

        if (ActSnapshotStore.SnapshotFromResume)
        {
            Entry.Logger?.Warn($"[QuickRestart] 注意：本幕快照来自**读档恢复点**（本次会话直接读档继续、未经历真实幕初）—— 重启本层将回滚到「读档时」的状态，而不是本幕开场");
            // 如实告知玩家（游戏存档里没有"本幕开场"数据，读档进入的当幕无法回滚到幕初，只能到读档点）
            RestartOverlay.SetNote("本幕快照来自读档点：只能回到「读档时」的状态（游戏未保存本幕开场数据）");
        }
        Entry.Logger?.Info($"[QuickRestart] 重启本层：回滚到第 {actIndex + 1} 幕快照点（本幕获得的金币/卡牌/遗物/药水/血量将全部回退）");
        ClosePauseMenu();

        // ★ 落点：本幕起点就是地图屏（不自动重进上一格），异常落点强制退回（在暂停快照的窗口内校验）
        long ms = await RollbackAsync("重启本层", snapshot,
            _ => RunManager.Instance.EnterRoom(new MapRoom()), ensureMapRoom: true);
        if (ms < 0)
            return;

        Entry.Logger?.Info($"[QuickRestart] 重启本层完成：已回滚到第 {actIndex + 1} 幕快照点、落点=地图屏 · 总耗时 {ms}ms{(ActSnapshotStore.SnapshotFromResume ? " ⚠ 快照=读档点（非真实幕初）" : "")}");
    }
}
