using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

internal sealed class RoomEnterTrackPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_room_enter_track";
    public static string Description => "进入房间前记录玩家状态用于重启房间时恢复";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(RunManager), nameof(RunManager.EnterRoom), new[] { typeof(AbstractRoom) })];

    [HarmonyPriority(Priority.First)]
    public static void Prefix(AbstractRoom room)
    {
        if (!Entry.Enabled) return;
        try
        {
            RoomEntryTracker.RecordOnRoomEnter();
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 记录进房前状态失败: {ex.Message}");
        }
    }
}