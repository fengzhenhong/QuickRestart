using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace QuickRestart;

internal static class RoomEntryTracker
{
    private sealed class PlayerSnapshot
    {
        public int CurrentHp;
    }

    private static PlayerSnapshot? _lastSnapshot;

    private static SerializableRun? _preMapPointSnapshot;
    // 捕获快照时"当前局"的开始时间戳（RunManager._startTime）—— 跨局校验用（比种子字符串可靠：
    // RunRngSet.StringSeed 在部分路径下可能为空，会让同局快照被误判为"上一局"）
    private static long _preMapPointRunStart;
    private static bool _suspended;

    // _startTime 反射缓存（直接字段访问运行期必炸，见 CurrentRunStart 注释）
    private static FieldInfo? _startTimeField;
    private static bool _startTimeFieldResolved;
    private static bool _startTimeReadFailed;

    /// <summary>
    /// "选路前"完整运行快照（RunManager.EnterMapCoord 前缀记录，早于 AddVisitedMapCoord）：
    /// 该地图坐标尚未被标记为已访问 → 回滚后玩家停在地图屏，可重新选路或重新进入本节点。
    /// 刀下留人弹窗"回到地图重新挑战"与"重启房间"的数据来源。
    /// </summary>
    public static SerializableRun? PreMapPointSnapshot => _preMapPointSnapshot;

    /// <summary>上述快照捕获时"当前局"的开始时间戳（0 = 未捕获）。</summary>
    public static long PreMapPointRunStart => _preMapPointRunStart;

    /// <summary>true = 暂停记录（重启房间的读档/自动重进期间，避免用中间状态覆盖快照）。</summary>
    public static bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
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
    /// 兜底补记"选路前"快照。两种情况下会缺：
    /// ① 读档继续游戏 —— 本次会话从未"点地图节点"（或只残留上一局的快照）；
    /// ② 本 mod 自己开的新局 —— "重启本局/新局"会 <see cref="Reset"/> 掉旧快照。
    /// 缺了会让「回到地图」退化成"重启房间"、「重启房间」退化成"重建房间（牌序重随机）"。
    /// 地图屏就绪（SetMap）时用当前状态补记；正常游玩时快照有效 → 不动它，
    /// 绝不覆盖真正的"点节点前"状态。
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
            Entry.Logger?.Info("[QuickRestart] 已用当前状态补记「选路前」快照"
                + "（原因：本次会话尚未经历真实选路 —— 读档继续游戏 / 刚开的新局；或旧快照被主动清空）");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 补记选路前快照失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 新局/清理：释放旧局对象引用（快照与进房前血量记录），避免静态字段长期持有旧 run 的对象图。
    /// 由 <see cref="RestartService"/> 在"重启本局/新局"前调用；玩家走游戏自带流程开新局时，
    /// 旧快照会在新一幕的地图屏就绪时被跨局判据（<see cref="IsSnapshotForCurrentRun"/>）自动作废。
    /// </summary>
    public static void Reset()
    {
        _lastSnapshot = null;
        _preMapPointSnapshot = null;
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
                CurrentHp = player.Creature.CurrentHp
            };

            Entry.Logger?.Info($"[QuickRestart] 记录进房前状态 HP={_lastSnapshot.CurrentHp} Room={state.CurrentRoom?.Id}");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 记录进房前状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 把**本地玩家血量**恢复到最近一次进房前的值（仅此一项 —— 金币/卡牌/遗物不回退）。
    /// <para>只在降级路径使用：正常"重启房间/回到地图"走完整快照回滚（回退全部状态），
    /// 本方法服务于「无选路前快照」（房间不是从地图节点进入）与「刀下留人先把人救活」两处。</para>
    /// </summary>
    public static void RestorePreRoomHp()
    {
        if (_lastSnapshot == null)
        {
            Entry.Logger?.Info("[QuickRestart] 无进房前状态记录，跳过血量恢复");
            return;
        }

        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null) return;

            var player = GetLocalPlayer(state);
            if (player == null) return;

            player.Creature.SetCurrentHpInternal(_lastSnapshot.CurrentHp);
            Entry.Logger?.Info($"[QuickRestart] 恢复进房前血量 HP={_lastSnapshot.CurrentHp}");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 恢复进房前血量失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 当前局的开始时间戳（RunManager 私有字段 `_startTime`：同局恒定、读档由存档复原、跨局必不同）。
    /// <para>⚠️ **必须反射读**：Krafs.Publicizer 只改**编译期**签名 —— 直接写
    /// <c>RunManager.Instance._startTime</c> 编译通过、运行期抛 FieldAccessException
    /// （与 <c>RunManager.ShouldSave</c> setter 的 MethodAccessException 同一类坑）。
    /// 该异常会让局标识恒 0 → 所有跨局校验恒失败 → 重启房间/本层/回到地图全部退化。
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
    /// <para>判据 = **局开始时间戳**（同局恒定、跨局必不同），不用种子字符串比对：
    /// <c>RunRngSet.StringSeed</c> 在部分路径下可能为空，会让同局快照被误判为"上一局"，
    /// 会让同局快照被误判为"上一局"、功能静默降级。</para>
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
        // ⚠️ 严格拒绝会让所有功能退化（标识恒 0 时：重启房间变"牌序重随机重开"、
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