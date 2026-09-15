using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace QuickRestart;

internal static class RoomEntryTracker
{
    private sealed class PlayerSnapshot
    {
        public int CurrentHp;
        public int MaxHp;
        public int Gold;
    }

    private static PlayerSnapshot? _lastSnapshot;
    private static AbstractRoom? _lastRoom;

    private static SerializableRun? _preRoomSnapshot;
    private static SerializableRun? _preMapPointSnapshot;
    private static bool _suspended;

    /// <summary>
    /// 进房前的完整运行快照（EnterRoom 前缀记录）："重启房间"经官方读档流程回滚到该时点，
    /// RNG 一并回滚 → 重新进入时洗牌/怪物与首次完全一致（牌序精确重现）。
    /// </summary>
    public static SerializableRun? PreRoomSnapshot => _preRoomSnapshot;

    /// <summary>
    /// "选路前"完整运行快照（RunManager.EnterMapCoord 前缀记录，早于 AddVisitedMapCoord）：
    /// 该地图坐标尚未被标记为已访问 → 回滚后玩家停在地图屏，可重新选路或重新进入本节点。
    /// 刀下留人弹窗"回到地图重新挑战"的数据来源。
    /// </summary>
    public static SerializableRun? PreMapPointSnapshot => _preMapPointSnapshot;

    /// <summary>true = 暂停记录（重启房间的读档/自动重进期间，避免用中间状态覆盖快照）。</summary>
    public static bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    /// <summary>进房前（RunManager.EnterRoom 前）记录完整快照。</summary>
    public static void CapturePreRoomSnapshot()
    {
        if (_suspended)
            return;
        try
        {
            if (RunManager.Instance.DebugOnlyGetState() == null)
                return;
            _preRoomSnapshot = RunManager.Instance.ToSave(null);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 进房前快照记录失败: {ex.Message}");
        }
    }

    /// <summary>选路前（RunManager.EnterMapCoord 前，坐标尚未标记已访问）记录完整快照。</summary>
    public static void CapturePreMapPointSnapshot()
    {
        if (_suspended)
            return;
        try
        {
            if (RunManager.Instance.DebugOnlyGetState() == null)
                return;
            _preMapPointSnapshot = RunManager.Instance.ToSave(null);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 选路前快照记录失败: {ex.Message}");
        }
    }

    public static void ClearPreRoomSnapshot()
    {
        _preRoomSnapshot = null;
    }

    /// <summary>新局/清理：释放旧局对象引用（房间实例与快照），避免静态字段长期持有旧 run 的对象。</summary>
    public static void Reset()
    {
        _lastSnapshot = null;
        _lastRoom = null;
        _preRoomSnapshot = null;
        _preMapPointSnapshot = null;
    }

    public static void RecordOnRoomEnter()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null) return;

            var player = GetLocalPlayer(state);
            if (player == null) return;

            _lastSnapshot = new PlayerSnapshot
            {
                CurrentHp = player.Creature.CurrentHp,
                MaxHp = player.Creature.MaxHp,
                Gold = player.Gold
            };
            _lastRoom = state.CurrentRoom;

            Entry.Logger?.Info($"[QuickRestart] 记录进房前状态 HP={_lastSnapshot.CurrentHp}/{_lastSnapshot.MaxHp} Gold={_lastSnapshot.Gold} Room={_lastRoom?.Id}");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 记录进房前状态失败: {ex.Message}");
        }
    }

    public static void RestorePreRoomState()
    {
        if (_lastSnapshot == null)
        {
            Entry.Logger?.Info("[QuickRestart] 无进房前状态记录，跳过恢复");
            return;
        }

        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null) return;

            var player = GetLocalPlayer(state);
            if (player == null) return;

            player.Creature.SetCurrentHpInternal(_lastSnapshot.CurrentHp);
            Entry.Logger?.Info($"[QuickRestart] 恢复进房前血量 HP={_lastSnapshot.CurrentHp}/{_lastSnapshot.MaxHp}");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 恢复进房前状态失败: {ex.Message}");
        }
    }

    public static AbstractRoom? GetLastRoom() => _lastRoom;

    public static Player? GetLocalPlayer(RunState state)
    {
        return state.Players.FirstOrDefault();
    }
}