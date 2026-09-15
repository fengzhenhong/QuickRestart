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
/// 卡死在 NCombatRoom.Create 之前（日志实证：Preloading Complete 后无 Creating NCombatRoom）。</para>
/// <para>拦截成功后立即复活玩家（HP=0 会持续触发死亡判定）并 Pause 战斗，弹窗等玩家选择。</para>
/// </summary>
internal static class DeathInterceptCore
{
    private static bool _isIntercepting;

    /// <summary>返回 true = 已拦截（调用方应 return false 阻止原方法执行）。</summary>
    public static bool TryIntercept(string source)
    {
        if (!Entry.Enabled) return false;
        if (!SettingsManager.Current.EnableDeathIntercept) return false;

        // ★ 联机对局不拦截：① 本地回滚在联机下必然被校验和判定为状态分歧（详见 NetGuard）；
        //   ② 队友死亡与本地玩家无关（不应弹"刀下留人"）—— 联机下让游戏按官方流程处理。
        //   玩家反馈（2026-09-15）：联机模式下队友死亡也会触发刀下留人。
        if (NetGuard.IsMultiplayer()) return false;

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

            bool popupShown = ConfirmPopupHelper.ShowDeathInterceptPopup(
                onRetry: () =>
                {
                    _isIntercepting = false;
                    // ★ 回到"进入本房间之前"（选路前）并停在地图屏 —— 而不是原地重开战斗
                    _ = RestartService.RewindToMapAsync();
                },
                onAbandon: () =>
                {
                    _isIntercepting = false;
                    _ = RestartService.ExecuteRestart(RestartService.RestartType.RestartRun);
                });

            if (!popupShown)
            {
                // 弹窗不可用：玩家已被复活（HP≥1），复位拦截状态并解除暂停让战斗继续，
                // 避免战斗永久冻结卡死游戏；再次死亡会重新尝试拦截。
                Entry.Logger?.Warn("[QuickRestart] 刀下留人弹窗不可用，解除暂停继续战斗（跳过本次拦截）");
                _isIntercepting = false;
                try
                {
                    cm.Unpause();
                }
                catch (Exception ex)
                {
                    Entry.Logger?.Warn($"[QuickRestart] 解除战斗暂停失败: {ex.Message}");
                }
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
            RoomEntryTracker.RestorePreRoomState();
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
    public static bool Prefix()
    {
        return !DeathInterceptCore.TryIntercept("HandlePlayerDeath");
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
