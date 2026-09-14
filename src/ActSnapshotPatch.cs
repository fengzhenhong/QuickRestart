using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

/// <summary>
/// 地图屏 SetMap 后记录幕起点快照（重启本层精确回滚的数据来源）。
/// SetMap 签名（反编译确认）：SetMap(ActMap map, ulong seed, bool clearDrawings)。
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
    }
}
