using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

/// <summary>
/// 选路前（RunManager.EnterMapCoord Prefix，早于 AddVisitedMapCoord）记录完整运行快照 ——
/// 刀下留人弹窗「回到地图重新挑战」的数据来源。
/// <para>该时点：本坐标尚未被标记为已访问、地图点历史未写入本房间记录，</para>
/// <para>回滚后玩家停在地图屏，可以重新选路，也可以重新进入刚才那个节点再战。</para>
/// </summary>
internal sealed class MapPointSnapshotPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_pre_map_point_snapshot";
    public static string Description => "选路前记录完整快照（刀下留人回到地图用）";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(RunManager), nameof(RunManager.EnterMapCoord), new[] { typeof(MapCoord) })];

    [HarmonyPriority(Priority.First)]
    public static void Prefix()
    {
        if (!Entry.Enabled) return;
        RoomEntryTracker.CapturePreMapPointSnapshot();
    }
}
