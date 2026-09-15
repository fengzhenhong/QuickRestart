using System.Reflection;
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
    // 捕获快照时"当前局"的开始时间戳（RunManager._startTime）—— 跨局校验用（比种子字符串可靠：
    // RunRngSet.StringSeed 在部分路径下可能为空，会让同局快照被误判为"上一局"）
    private static long _preRoomRunStart;
    private static long _preMapPointRunStart;
    private static bool _suspended;

    // _startTime 反射缓存（直接字段访问运行期必炸，见 CurrentRunStart 注释）
    private static FieldInfo? _startTimeField;
    private static bool _startTimeFieldResolved;
    private static bool _startTimeReadFailed;

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

    /// <summary>上述快照捕获时"当前局"的开始时间戳（0 = 未捕获）。</summary>
    public static long PreRoomRunStart => _preRoomRunStart;
    public static long PreMapPointRunStart => _preMapPointRunStart;

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
            _preRoomRunStart = CurrentRunStart();
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
            _preMapPointRunStart = CurrentRunStart();
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 选路前快照记录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 兜底补记"选路前"快照。场景：**读档继续游戏** —— 本次会话从未"点地图节点"，
    /// <see cref="PreMapPointSnapshot"/> 为空（或残留上一局），此时「回到地图」会退化成"重启房间"、
    /// 「重启房间」会退化成"重建房间（牌序重随机）"。在地图屏就绪（SetMap）时若快照缺失或失效，
    /// 就用当前状态（读档恢复点）补记。正常游玩时快照有效 → 本方法不动它（不覆盖真正的"点节点前"状态）。
    /// </summary>
    public static void EnsurePreMapPointSnapshot()
    {
        if (_suspended)
            return;
        try
        {
            if (RunManager.Instance.DebugOnlyGetState() == null)
                return;

            // null → 补记（读档场景）；局标识变化（跨局）→ 重记。
            // ⚠️ 标识不可用（两侧都为 0）时**不重记**（相等）—— 否则每次 SetMap 都会覆盖真正"点节点前"的快照。
            bool needCapture = _preMapPointSnapshot == null
                || _preMapPointRunStart != CurrentRunStart();
            if (!needCapture)
                return;

            _preMapPointSnapshot = RunManager.Instance.ToSave(null);
            _preMapPointRunStart = CurrentRunStart();
            Entry.Logger?.Info("[QuickRestart] 已用当前状态补记「选路前」快照（读档继续游戏：本次会话未经历真实选路）");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 补记选路前快照失败: {ex.Message}");
        }
    }

    public static void ClearPreRoomSnapshot()
    {
        _preRoomSnapshot = null;
        _preRoomRunStart = 0;
    }

    /// <summary>新局/清理：释放旧局对象引用（房间实例与快照），避免静态字段长期持有旧 run 的对象。</summary>
    public static void Reset()
    {
        _lastSnapshot = null;
        _lastRoom = null;
        _preRoomSnapshot = null;
        _preMapPointSnapshot = null;
        _preRoomRunStart = 0;
        _preMapPointRunStart = 0;
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

    /// <summary>
    /// 当前局的开始时间戳（RunManager 私有字段 `_startTime`：同局恒定、读档由存档复原、跨局必不同）。
    /// <para>⚠️ **必须反射读**：Krafs.Publicizer 只改**编译期**签名 —— 直接写
    /// <c>RunManager.Instance._startTime</c> 编译通过、运行期抛 FieldAccessException
    /// （与 <c>RunManager.ShouldSave</c> setter 的 MethodAccessException 同一坑；2026-09-15 日志实证
    /// "Attempt by method '...CurrentRunStart()' to access field '...RunManager._startTime' failed"）。
    /// 该异常曾让局标识恒 0 → 所有跨局校验恒失败 → 重启房间/本层/回到地图全部退化。反射则始终可用。</para>
    /// 字段缺失或读取失败返回 0（调用方走降级路径，绝不因此让功能整体失效）。
    /// </summary>
    internal static long CurrentRunStart()
    {
        try
        {
            if (!_startTimeFieldResolved)
            {
                _startTimeFieldResolved = true;
                _startTimeField = typeof(RunManager).GetField("_startTime",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_startTimeField == null)
                {
                    Entry.Logger?.Warn("[QuickRestart] 未找到 RunManager._startTime 字段：局标识不可用（跨局校验降级为种子比对）");
                }
            }
            if (_startTimeField == null || RunManager.Instance == null)
                return 0;
            return _startTimeField.GetValue(RunManager.Instance) is long l ? l : 0;
        }
        catch (Exception ex)
        {
            if (!_startTimeReadFailed)
            {
                _startTimeReadFailed = true;   // 只报一次，避免刷屏
                Entry.Logger?.Warn($"[QuickRestart] 读取局起始时间失败（后续静默）: {ex.Message}");
            }
            return 0;
        }
    }

    /// <summary>
    /// 校验快照是否属于当前局。跨局残留防护：快照只在 mod 主动"重启本局/新局"时 Reset，
    /// 玩家走游戏自带流程（死亡/通关后重开）开新局时旧局快照仍残留 —— 不校验会把新局回滚到旧局状态。
    /// <para>判据 = **局开始时间戳**（同局恒定、跨局必不同）。⚠️ 不再用种子字符串比对：
    /// <c>RunRngSet.StringSeed</c> 在部分路径下可能为空，会让同局快照被误判为"上一局"，
    /// 导致"重启本层"静默降级为"仅重进本幕"（2026-09-15 实测踩坑）。</para>
    /// <para>失败时打印两边实际值（诊断），避免静默降级难以排查。</para>
    /// </summary>
    public static bool IsSnapshotForCurrentRun(SerializableRun? snapshot, long capturedRunStart, string what)
    {
        if (snapshot == null)
        {
            Entry.Logger?.Warn($"[QuickRestart] {what}：快照缺失");
            return false;
        }

        long current = CurrentRunStart();
        if (capturedRunStart != 0 && current != 0)
        {
            if (capturedRunStart == current)
                return true;
            Entry.Logger?.Warn($"[QuickRestart] {what}：快照属于上一局（捕获时局起始={capturedRunStart}，当前={current}）");
            return false;
        }

        // 局标识不可用（任一侧为 0）→ 降级为种子比对；种子也取不到则放行（功能优先）。
        // ⚠️ 严格拒绝会让所有功能退化（2026-09-15 实测：标识恒 0 → 重启房间变"牌序重随机重开"、
        //    重启本层变"仅重进本幕"、刀下留人变"重启房间"）；放行的残余风险（跨局残留）只在
        //    "标识不可用 + 种子缺失"双重异常下才出现，远小于功能全废的代价。
        string? snapSeed = snapshot.SerializableRng?.Seed;
        string? currentSeed = null;
        try { currentSeed = RunManager.Instance.DebugOnlyGetState()?.Rng.StringSeed; }
        catch { /* 取不到走放行 */ }

        if (string.IsNullOrEmpty(snapSeed) || string.IsNullOrEmpty(currentSeed))
        {
            Entry.Logger?.Warn($"[QuickRestart] {what}：局标识不可用（{capturedRunStart}/{current}）且种子缺失，跳过跨局校验（放行）");
            return true;
        }
        if (snapSeed == currentSeed)
        {
            Entry.Logger?.Info($"[QuickRestart] {what}：局标识不可用，种子一致 → 判定为当前局快照");
            return true;
        }
        Entry.Logger?.Warn($"[QuickRestart] {what}：种子不一致（快照={snapSeed}，当前={currentSeed}），判定为上一局");
        return false;
    }

    public static Player? GetLocalPlayer(RunState state)
    {
        return state.Players.FirstOrDefault();
    }
}