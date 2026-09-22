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
/// 原地回滚的两条流程：战后"重新挑战"与刀下留人的"回到地图"。
/// 两者都是"回滚 + 落地"，共用 <see cref="RollbackAsync"/>，差别只在落地动作与是否允许每日局。
/// </summary>
internal static partial class RestartService
{
    /// <summary>
    /// 战斗结算选牌界面的「重新挑战」：放弃本次奖励、整场重打。
    /// 语义 = 重启房间（回滚到进房前 + 自动重进本节点，问号房照原掷骰重演）。
    /// </summary>
    public static Task RetryCombatAsync() => RunGuardedAsync("重新挑战", RetryCombatCoreAsync);

    private static async Task RetryCombatCoreAsync()
    {
        Entry.Logger?.Info("[QuickRestart] 重新挑战当前战斗");
        await RestartRoomAsync();
    }

    /// <summary>
    /// 刀下留人「回到地图重新挑战」：回滚到**选路前**（进入本房间之前 —— 该地图坐标尚未被标记访问），
    /// 最终停在地图屏：血量 / 牌组 / 金币 / RNG 全部回退到进房前，玩家可重新选路，
    /// 也可以重新进入刚才那个节点再战。
    /// <para>与"重启房间"的区别：不自动重进房间（所以要额外校验落点）。</para>
    /// </summary>
    // ★ allowDaily：这是原地回滚（不改种子、不重开局），每日挑战局的"刀下留人"要留这条出路；
    //   联机下两个出路都不给，见 NetGuard。
    public static Task RewindToMapAsync() => RunGuardedAsync("回到地图", RewindToMapCoreAsync, allowDaily: true);

    private static async Task RewindToMapCoreAsync()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 无法获取 RunState，跳过回到地图");
            return;
        }

        var snap = RoomEntryTracker.PreMapPointSnapshot;
        if (snap == null
            || !RoomEntryTracker.IsSnapshotForCurrentRun(snap, RoomEntryTracker.PreMapPointRunStart, "回到地图"))
        {
            // 兜底（快照缺失或属于上一局；如本房间不是从地图节点进入、补丁未生效）：沿用"重启房间"
            Entry.Logger?.Warn("[QuickRestart] 无可用选路前快照（缺失或属于上一局），回退为重启房间（回滚到进房前并自动重进）");
            await RestartRoomAsync();
            return;
        }

        ClosePauseMenu();

        // 刀下留人场景：拦截时调用了 CombatManager.Pause()，先解除（否则读档流程会卡住）
        EnsureCombatUnpaused();

        Entry.Logger?.Info("[QuickRestart] 回到地图：回滚到选路前（进入本房间之前）");
        // 落点兜底：理论上读档后已在 MapRoom；不在就强制退回（本节点在快照中尚未标记访问，故可重新点击）
        long ms = await RollbackAsync("回到地图", snap,
            _ => RunManager.Instance.EnterRoom(new MapRoom()), ensureMapRoom: true);
        if (ms < 0)
            return;

        Entry.Logger?.Info($"[QuickRestart] 回到地图完成：可重新选路，或重新进入本节点再战（状态已回退到进房前） · 总耗时 {ms}ms");
    }
}
