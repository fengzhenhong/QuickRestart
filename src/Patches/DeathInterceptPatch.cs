using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Context;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

/// <summary>
/// 死亡/失败拦截核心（双重拦截，共享状态）：
/// <para>[1] HandlePlayerDeath —— 游戏的逐玩家死亡处理（多人清牌等；单人局近乎空转）。</para>
/// <para>[2] LoseCombat —— 设置 PendingLoss 的失败标记，**关键拦截点**：不拦它的话，
/// 回合循环会在 CheckWinCondition 里走完失败结算（IsInProgress=false + CombatEnded →
/// 游戏后台已终结这场战斗），"重新挑战"重进时与残留失败流程冲突，
/// 卡死在 NCombatRoom.Create 之前（预加载完成后不再创建战斗房间）。</para>
/// <para>拦截成功后立即复活玩家（HP=0 会持续触发死亡判定）并 Pause 战斗，弹窗等玩家选择。</para>
/// </summary>
internal static class DeathInterceptCore
{
    private static bool _isIntercepting;

    /// <summary>
    /// 一次性放行闩（按"局起始时间戳"记）：玩家在弹窗里选了"放弃本局"后置为本局标识，
    /// 此后本局内所有死亡调用一律放行、不再拦截弹窗；开新局（<c>_startTime</c> 变化）自动恢复。
    /// <para>⚠️ 为什么必须有它：每日挑战局的"放弃本局"只能走官方 <c>RunManager.Abandon()</c>
    /// （无痕重开会丢每日结算依据），而 <c>AbandonInternal</c> 内部会
    /// <c>GuaranteeKillAllPlayers → CreatureCmd.Kill(force:true)</c> 再触发一遍死亡流程 ——
    /// 不放行就会被我们自己重新拦下、再弹一次窗，永远结算不完。</para>
    /// </summary>
    private static long _releaseDeathRunStart;

    /// <summary>局标识不可用时（<c>RunManager._startTime</c> 反射失效）的放行兜底：本会话内持续放行。</summary>
    private static bool _releaseDeathWithoutId;

    /// <summary>放弃本局：解除拦截并让本局剩余死亡调用直接放行（配合官方 Abandon 使用）。</summary>
    private static void ReleaseDeathForCurrentRun()
    {
        _isIntercepting = false;
        _releaseDeathRunStart = RoomEntryTracker.CurrentRunStart();
        _releaseDeathWithoutId = _releaseDeathRunStart == 0;
        Entry.Logger?.Info($"[QuickRestart] 玩家选择放弃本局：本局后续死亡调用放行（局标识={_releaseDeathRunStart}）");
    }

    /// <summary>
    /// 本次死亡调用是否已被玩家主动放弃（应放行，让官方结算跑完）。
    /// <para>失效方式＝开新局（<c>_startTime</c> 变化），无需手动复位：官方流程开的局、
    /// 本 mod 重启出来的局都会拿到新的局标识，闩自然对不上。</para>
    /// </summary>
    private static bool IsDeathReleased()
    {
        if (_releaseDeathWithoutId)
            return true;            // 局标识取不到：朝"不拦截"一侧兜底（最坏是本次不救，绝不会卡死）
        long cur = RoomEntryTracker.CurrentRunStart();
        return _releaseDeathRunStart != 0 && cur == _releaseDeathRunStart;
    }

    /// <summary>返回 true = 已拦截（调用方应 return false 阻止原方法执行）。</summary>
    public static bool TryIntercept(string source)
    {
        if (!Entry.Enabled) return false;
        if (!SettingsManager.Current.EnableDeathIntercept) return false;

        // ★ 联机对局不拦截：① 本地回滚在联机下必然被校验和判定为状态分歧（详见 NetGuard）；
        //   ② 队友死亡与本地玩家无关（不应弹"刀下留人"）—— 联机下让游戏按官方流程处理。
        if (NetGuard.IsMultiplayer()) return false;

        // ★ 已选"放弃本局"：放行，让官方结算跑完（见 _releaseDeathRunStart）
        if (IsDeathReleased())
        {
            Entry.Logger?.Info($"[QuickRestart] 死亡调用按玩家选择放行（放弃本局结算中，来源={source}）");
            return false;
        }

        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return false;

            // 拦截进行中：继续拦截后续死亡相关调用（尤其 LoseCombat —— 放行会设置 PendingLoss）。
            // 但若弹窗已被外部关闭（Esc 等途经清空弹窗容器），回调不会触发 —— 必须复位，
            // 否则状态永卡 true，后续死亡会被静默拦截（玩家既不会死也看不到弹窗）。
            if (_isIntercepting)
            {
                if (ConfirmPopupHelper.IsDeathPopupOpen)
                {
                    return true;
                }

                Entry.Logger?.Warn("[QuickRestart] 刀下留人弹窗已关闭但拦截状态未复位，重置后按新死亡重新处理");
                _isIntercepting = false;
                // 弹窗关闭时 OnDismissed 已解除暂停；此处再兜一次（幂等），
                // 因为玩家已被复活 → 不会再产生死亡调用 → 没有这次兜底战斗就永久卡在暂停上。
                RestartService.EnsureCombatUnpaused();
                // 继续往下走：按当前致命伤害重新拦截并弹窗
            }

            if (!cm.IsInProgress || cm.IsEnding) return false;

            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null) return false;

            // ★ 本地玩家（而非"玩家列表第一个"）：联机/多玩家存档下列表含其他玩家，取第一个会把
            //   队友的死亡误判为"自己受到致命伤害"。LocalContext.GetMe 返回本地玩家；异常场景退回第一个。
            var localPlayer = LocalContext.GetMe(state) ?? state.Players.FirstOrDefault();
            if (localPlayer == null) return false;

            if (localPlayer.Creature.CurrentHp > 0) return false;

            Entry.Logger?.Info($"[QuickRestart] 检测到玩家致命伤害（{source}），拦截死亡流程");
            _isIntercepting = true;

            // 冻结战斗（弹窗选择期间）
            try
            {
                cm.Pause();
            }
            catch (Exception ex)
            {
                Entry.Logger?.Warn($"[QuickRestart] 暂停战斗失败: {ex.Message}");
            }

            // 立即复活：保持战斗状态有效，避免 HP=0 持续触发死亡相关判定
            RevivePlayer(localPlayer);

            // 每日挑战局：两个出路的语义不同 —— "回到地图"照给（原地回滚，不改种子），
            // "放弃本局"改为走游戏官方放弃结算，而不是普通局的"无痕同种子重开"。
            bool daily = state.GameMode == GameMode.Daily;

            bool popupShown = ConfirmPopupHelper.ShowDeathInterceptPopup(
                daily
                    ? "生命归零！\n【确认】回到地图重新挑战（进本房间前）\n【取消】放弃本局（按游戏正常流程结算本次失败）"
                    : "生命归零！\n【确认】回到地图重新挑战（进本房间前）\n【取消】放弃本局（同一种子从头重开）",
                onRetry: () =>
                {
                    _isIntercepting = false;
                    // ★ 回到"进入本房间之前"（选路前）并停在地图屏 —— 而不是原地重开战斗
                    _ = RestartService.RewindToMapAsync();
                },
                onAbandon: () =>
                {
                    _isIntercepting = false;
                    if (daily)
                    {
                        // 每日局不能无痕重开（重开局拿不到 DailyTime 结算依据）→ 用游戏自己的
                        // "放弃本局"（暂停菜单 GiveUp 走的就是它）正常结算。
                        // ⚠️ 必须先放行：Abandon 内部会再强制杀一次玩家，不放行会被我们自己拦下反复弹窗。
                        ReleaseDeathForCurrentRun();
                        RunManager.Instance.Abandon();
                        return;
                    }
                    _ = RestartService.ExecuteRestart(RestartService.RestartType.RestartRun);
                },
                // 弹窗未经选择即被关闭：玩家已被复活、不会再产生死亡调用，
                // 不解除暂停 = 战斗永久冻结（玩家以为游戏卡死）。这里成对收尾。
                onDismissed: () =>
                {
                    _isIntercepting = false;
                    RestartService.EnsureCombatUnpaused();
                });

            if (!popupShown)
            {
                // 弹窗不可用：玩家已被复活（HP≥1），复位拦截状态并解除暂停让战斗继续，
                // 避免战斗永久冻结卡死游戏；再次死亡会重新尝试拦截。
                Entry.Logger?.Warn("[QuickRestart] 刀下留人弹窗不可用，解除暂停继续战斗（跳过本次拦截）");
                _isIntercepting = false;
                RestartService.EnsureCombatUnpaused();
            }

            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 刀下留人失败（{source}）: {ex}");
            _isIntercepting = false;
            return false;
        }
    }

    private static void RevivePlayer(Player player)
    {
        try
        {
            RoomEntryTracker.RestorePreRoomHp();
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 复活（恢复进房前血量）失败: {ex.Message}");
        }

        if (player.Creature.CurrentHp <= 0)
        {
            try
            {
                player.Creature.SetCurrentHpInternal(1);
                Entry.Logger?.Info("[QuickRestart] 复活保底：血量置 1");
            }
            catch (Exception ex)
            {
                Entry.Logger?.Warn($"[QuickRestart] 复活保底失败: {ex.Message}");
            }
        }
        else
        {
            Entry.Logger?.Info($"[QuickRestart] 玩家已复活 HP={player.Creature.CurrentHp}/{player.Creature.MaxHp}");
        }
    }
}

internal sealed class PlayerDeathInterceptPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_death_intercept";
    public static string Description => "拦截玩家致命伤害（HandlePlayerDeath），弹出重新挑战/放弃选择";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CombatManager), nameof(CombatManager.HandlePlayerDeath), new[] { typeof(CombatId?), typeof(Player) })];

    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref Task __result)
    {
        if (!DeathInterceptCore.TryIntercept("HandlePlayerDeath"))
            return true;

        // ★★ 必须补 __result —— 这是 async Task 方法，跳过时 Harmony 会把返回值留成 null，
        //    而调用方是 `await CombatManager.Instance.HandlePlayerDeath(...)`
        //    （CreatureCmd.KillWithoutCheckingWinCondition 内），await null 直接抛
        //    NullReferenceException，异常沿 Kill → Damage → 回合循环一路上抛，
        //    把整场战斗的 turn loop 打死（日志：`Combat #N turn loop died ... the combat is
        //    stuck until the room is restarted`）。2026-09-22 实测：每次刀下留人都必然触发一次。
        //    返回一个已完成任务，语义 = "死亡处理照常做完（只是没做事）"。
        __result = Task.CompletedTask;
        return false;
    }
}

internal sealed class LoseCombatInterceptPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_lose_combat_intercept";
    public static string Description => "拦截战斗失败标记（LoseCombat），防止游戏后台走完失败结算";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CombatManager), nameof(CombatManager.LoseCombat), Type.EmptyTypes)];

    [HarmonyPriority(Priority.First)]
    public static bool Prefix()
    {
        return !DeathInterceptCore.TryIntercept("LoseCombat");
    }
}
