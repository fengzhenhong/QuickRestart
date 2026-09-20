using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

/// <summary>
/// 地图屏 SetMap 后记录幕起点快照（重启本层精确回滚的数据来源）。
/// 补丁目标：<c>NMapScreen.SetMap(ActMap map, ulong seed, bool clearDrawings)</c>。
/// </summary>
internal sealed class ActSnapshotPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_act_snapshot";
    public static string Description => "地图屏就绪时记录幕起点快照（重启本层精确回滚用）";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NMapScreen), "SetMap", new[] { typeof(ActMap), typeof(ulong), typeof(bool) })];

    public static void Postfix()
    {
        if (!Entry.Enabled) return;
        ActSnapshotStore.CaptureIfNeeded();   // 内部已 try-catch，绝不干扰游戏流程
        // 读档继续游戏时本次会话没有"点节点"记录 → 顺带补记"选路前"兜底快照（缺了才补，不覆盖正常快照）
        RoomEntryTracker.EnsurePreMapPointSnapshot();
    }
}
