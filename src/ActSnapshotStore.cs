using System;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace QuickRestart;

/// <summary>
/// 幕起点快照：进入新幕（地图屏首次就绪）时保存一份 SerializableRun（含地图）。
/// <para>
/// "重启本层"用它经**游戏官方读档流程**（RunState.FromSerializable → SetUpSavedSingleplayer → NGame.LoadRun）
/// 精确回滚到本幕开始状态 —— 本幕获得的金币 / 卡牌 / 遗物 / 药水 / 血量全部回退，
/// 且 RNG、地图、房间进度与官方"继续游戏"语义完全一致（不做手工 diff，避免状态残缺）。
/// </para>
/// </summary>
internal static class ActSnapshotStore
{
    private static SerializableRun? _snapshot;
    private static int _actIndex = -1;

    public static SerializableRun? Snapshot => _snapshot;
    public static int SnapshotActIndex => _actIndex;

    /// <summary>
    /// 地图屏就绪时尝试记录：同一局同一幕只记一次（语义 = "本幕起点"）；换幕后自动重记。
    /// SetMap 在 EnterAct（新局 / 换幕）时触发；读档回幕 / 重复 SetMap 时 CurrentActIndex 未变 → 不覆盖。
    /// 跨局防护：同幕号但种子不同（游戏自带流程开的上局残留）→ 重记。
    /// </summary>
    public static void CaptureIfNeeded()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null)
                return;
            // 同幕且快照确实属于当前局才沿用；同幕号的旧局残留快照必须重记，
            // 否则玩家用游戏自带流程开新局后按"重启本层"会回滚到旧局状态。
            if (_snapshot != null && _actIndex == state.CurrentActIndex
                && RoomEntryTracker.IsSnapshotForCurrentRun(_snapshot))
                return;
            _snapshot = RunManager.Instance.ToSave(null);
            _actIndex = state.CurrentActIndex;
            Entry.Logger?.Info($"[QuickRestart] 已记录第 {_actIndex + 1} 幕起点快照（重启本层回滚用）");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 幕快照记录失败: {ex.Message}");
        }
    }

    /// <summary>新局时清空（旧局快照作废；新局 Act 0 的地图屏就绪后会重新记录）。</summary>
    public static void Reset()
    {
        _snapshot = null;
        _actIndex = -1;
    }
}
