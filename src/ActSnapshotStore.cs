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
    private static long _runStart;   // 捕获时"当前局"的开始时间戳（跨局校验用）
    private static bool _fromResume; // 快照来自"读档恢复点"（非真实幕初）
    private static long _diskCheckedRunStart = -1;   // 已为该 [局,幕] 尝试过磁盘加载（避免每次打开地图屏都读盘）
    private static int _diskCheckedActIndex = -1;

    public static SerializableRun? Snapshot => _snapshot;
    public static int SnapshotActIndex => _actIndex;

    /// <summary>快照捕获时"当前局"的开始时间戳（0 = 未捕获）。</summary>
    public static long SnapshotRunStart => _runStart;

    /// <summary>快照是否来自"读档恢复点"（本次会话直接读档继续、未经历真实幕初）：重启本层将回滚到读档时状态。</summary>
    public static bool SnapshotFromResume => _fromResume;

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
            long runStart = RoomEntryTracker.CurrentRunStart();
            int actIndex = state.CurrentActIndex;

            // "本幕是否已有走过的记录" = 不是幕初。判据用 MapPointHistory 而不是 ActFloor/VisitedMapCoords：
            // 后两者跨幕累积（第 2 幕起 visited 必然 >0），无法区分"换幕瞬间"与"读档进入幕中"。
            bool midAct = state.MapPointHistory.Count > actIndex
                && state.MapPointHistory[actIndex].Count > 0;

            // ★ 磁盘优先：读档继续游戏时内存里没有幕快照（或只有"读档恢复点"）。
            //   若磁盘上存着**同一局同一幕的真·幕初**（上次进入本幕时写盘），用它 —— 重启本层才能真回到幕初。
            //   ⚠️ 同一 [局,幕] 只尝试一次：老存档无磁盘快照时，反复打开地图屏会触发多次 SetMap，
            //      不缓存就会反复读+解析 50KB JSON（功能正确但纯浪费）。
            bool diskUnchecked = _diskCheckedRunStart != runStart || _diskCheckedActIndex != actIndex;
            if (diskUnchecked && (_snapshot == null || _fromResume))
            {
                _diskCheckedRunStart = runStart;
                _diskCheckedActIndex = actIndex;
                var disk = ActSnapshotPersistence.TryLoad(runStart, actIndex);
                if (disk != null)
                {
                    _snapshot = disk;
                    _actIndex = actIndex;
                    _runStart = runStart;
                    _fromResume = false;
                    Entry.Logger?.Info($"[QuickRestart] 已从磁盘恢复第 {actIndex + 1} 幕起点快照（读档继续也能回滚到真·幕初）");
                    return;
                }
            }

            // 同一局同一幕只记一次（语义 = "本幕起点"）；跨局残留（局时间戳不同）或换幕必须重记。
            // ⚠️ 判据用"局开始时间戳"而非种子字符串（后者可能为空 → 导致每次 SetMap 都重记，
            // 幕快照退化成"最近一次打开地图时"的状态 → 重启本层看起来"没回到本幕起点"）。
            // ⚠️ 标识不可用（两侧都为 0）时**保守沿用**已有快照，避免每次 SetMap 都覆盖。
            if (_snapshot != null && _actIndex == actIndex && _runStart == runStart)
                return;

            if (_snapshot != null)
            {
                Entry.Logger?.Info($"[QuickRestart] 幕快照重记：第 {_actIndex + 1} 幕（局 {_runStart}）→ 第 {actIndex + 1} 幕（局 {runStart}）");
            }
            _snapshot = RunManager.Instance.ToSave(null);
            _actIndex = actIndex;
            _runStart = runStart;
            _fromResume = midAct;   // 幕中捕获 = 读档恢复点（非真实幕初）

            if (_fromResume)
            {
                Entry.Logger?.Info($"[QuickRestart] 已记录第 {actIndex + 1} 幕快照（重启本层回滚用）※ 来自读档恢复点（本幕已走 {state.MapPointHistory[actIndex].Count} 个节点）：不是真实幕初，重启本层将回滚到读档时状态");
            }
            else
            {
                Entry.Logger?.Info($"[QuickRestart] 已记录第 {actIndex + 1} 幕起点快照（重启本层回滚用）");
                ActSnapshotPersistence.Save(_snapshot!, _actIndex);   // 真·幕初 → 写盘（跨会话保留）
            }
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
        _runStart = 0;
        _fromResume = false;
        _diskCheckedRunStart = -1;
        _diskCheckedActIndex = -1;
        ActSnapshotPersistence.Delete();   // 旧局的磁盘幕初作废（新局第 1 幕会重新写盘）
    }
}
