using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

/// <summary>
/// 进房前（RunManager.EnterRoom Prefix）记录完整运行快照 —— "重启房间精确回滚"的数据来源。
/// Prefix 时世界状态 = 进房前：RNG 未被本场战斗消耗、牌组/血量/金币为进房前值。
/// </summary>
internal sealed class PreRoomSnapshotPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_pre_room_snapshot";
    public static string Description => "进房前记录完整快照（重启房间精确回滚用）";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(RunManager), nameof(RunManager.EnterRoom), new[] { typeof(AbstractRoom) })];

    [HarmonyPriority(Priority.First)]
    public static void Prefix()
    {
        if (!Entry.Enabled) return;
        RoomEntryTracker.CapturePreRoomSnapshot();
    }
}
