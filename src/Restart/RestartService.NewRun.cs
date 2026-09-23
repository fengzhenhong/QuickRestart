using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace QuickRestart;

/// <summary>
/// 重开整局的两条流程：同种子/新种子开新局，以及"换角色开新局"（送到游戏自己的角色选择屏）。
/// </summary>
internal static partial class RestartService
{
    private static async Task RestartRunAsync(bool sameSeed)
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 无法获取 RunState，跳过重启本局");
            return;
        }

        var player = state.Players.FirstOrDefault();
        if (player == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 无法获取 Player，跳过重启本局");
            return;
        }

        // ══════════════════════════════════════════════════════════════════
        // canonical 模型：RunState 里的 Acts / Modifiers / Character 是 **mutable** 实例，
        // 而 StartNewSingleplayerRun 会对入参再 ToMutable()（要求规范实例），
        // 直接传 mutable 会抛 MutableModelException。
        // 正确取法：ModelDb.GetByIdOrNull<T>(id)。
        // ══════════════════════════════════════════════════════════════════
        var character = ModelDb.GetByIdOrNull<CharacterModel>(player.Character.Id);
        if (character == null)
        {
            Entry.Logger?.Error($"[QuickRestart] 找不到 canonical 角色 {player.Character.Id}，取消重启");
            return;
        }

        var acts = new List<ActModel>();
        foreach (var act in state.Acts)
        {
            var canonical = ModelDb.GetByIdOrNull<ActModel>(act.Id);
            if (canonical != null)
                acts.Add(canonical);
        }
        if (acts.Count == 0)
        {
            Entry.Logger?.Error("[QuickRestart] 无法解析 canonical Acts，取消重启");
            return;
        }

        var modifiers = new List<ModifierModel>();
        foreach (var mod in state.Modifiers)
        {
            var canonical = ModelDb.GetByIdOrNull<ModifierModel>(mod.Id);
            if (canonical != null)
                modifiers.Add(canonical);
        }

        // 刀下留人等场景可能把战斗置于暂停态，先解除（无暂停时为无操作）
        EnsureCombatUnpaused();

        var gameMode = state.GameMode;
        int ascension = state.AscensionLevel;

        string? sameRunSeed = sameSeed ? state.Rng.StringSeed : null;
        if (sameSeed && string.IsNullOrEmpty(sameRunSeed))
        {
            // ⚠️ RunRngSet.StringSeed 在部分路径下为空：
            //   此时"同种子重开"无从谈起，用新随机种子顶上并如实告知，别把空串当种子传给新局。
            Entry.Logger?.Warn("[QuickRestart] 当前局种子为空，无法同种子重开 → 本次改用新随机种子");
            ModToast.Show(ModLoc.ToastNoSeed);
            sameRunSeed = null;
        }

        string seed = sameRunSeed ?? SeedHelper.GetRandomSeed();

        Entry.Logger?.Info($"[QuickRestart] 重启本局 sameSeed={sameSeed} seed={seed} character={character.Id} ascension={ascension} acts={acts.Count} modifiers={modifiers.Count}");

        ClosePauseMenu();

        // ══════════════════════════════════════════════════════════════════
        // 无痕重启：旧局不得写入 run 历史、不得中断连胜、不记成就。
        // RunManager.OnEnded() 的战绩写入（UpdateProgressWithRunData 连胜 /
        // CreateRunHistoryEntry 历史 / AchievementsHelper 成就 / 指标上传 / 删档）
        // 都包在 `if (ShouldSave)` 里 —— 重开期间把 ShouldSave 置 false，旧局即"无痕消失"。
        // 新局由 SetUpNewSingleplayer(state, shouldSave: true) 自动恢复为 true。
        //
        // ⚠️ 两处「无痕」之外的既定事实（官方读档/开局流程自带，改不了，只能如实告知）：
        //   1) "重启房间/本层/回到地图"走 SetUpSavedSingleplayer → 内部 IncrementNumReloads
        //      会把 NumReloads +1 并立刻写 current_run.save（它不看 ShouldSave）；
        //      结算时该计数会随 RunMetrics 上传。即"读档次数"会记上这些次回滚。
        //   2) OnEnded 里战败的敌人图鉴解锁（CheckUpdateEnemyDiscoveryAfterLoss）在 ShouldSave
        //      判据之外 —— 但本 mod 从不调用 OnEnded，所以实际不会触发。
        // ══════════════════════════════════════════════════════════════════
        bool prevShouldSave = RunManager.Instance.ShouldSave;
        bool suppressed = TrySetShouldSave(false);
        Entry.Logger?.Info($"[QuickRestart] 无痕标记 ShouldSave=false → {suppressed}（prev={prevShouldSave}）");
        if (!suppressed)
        {
            // 反射失败 = 无痕保护失效：旧局会正常写入历史并可能断连胜。静默降级是玩家最难察觉的一种坏，
            // 必须当场说清楚（日志 + 屏幕提示）。
            Entry.Logger?.Error("[QuickRestart] 无法关闭 ShouldSave：本次重启的无痕保护不会生效（旧局可能写入历史/断连胜）");
            ModToast.Show(ModLoc.ToastNoTraceProtectionFailed);
        }

        // 新局：旧局的幕快照与进房前快照作废（新局 Act 0 的地图屏就绪后会重新记录）
        ActSnapshotStore.Reset();
        RoomEntryTracker.Reset();

        try
        {
            string tag = sameSeed ? "重启本局" : "重启新局";
            long t0 = (long)Time.GetTicksMsec();
            bool direct = false;

            if (SettingsManager.Current.FastRestartSkipMenu)
            {
                // 直接路径：FadeOut + CleanUp 后直接开新局 —— 跳过主菜单资源预载与场景创建。
                //    CleanUp 幂等（State==null 直接 return）、从不调用 OnEnded（无痕靠的就是不走它）、自带 ShouldSave=false。
                //    任何异常 → 落回下方主菜单路径（ReturnToMainMenu 全流程），玩家不会卡死。
                try
                {
                    await NGame.Instance!.Transition.FadeOut(DirectFadeSec);
                    LogStep(tag, t0, "FadeOut");
                    RunManager.Instance.CleanUp();
                    LogStep(tag, t0, "CleanUp");

                    await NGame.Instance.StartNewSingleplayerRun(
                        character, true, acts, modifiers, seed, gameMode, ascension);
                    await FadeInSafeAsync(tag);
                    direct = true;
                    LogStep(tag, t0, "StartRun");
                }
                catch (Exception ex)
                {
                    Entry.Logger?.Error($"[QuickRestart] 直接路径失败（{tag}），回退主菜单路径: {ex.Message}");
                }
            }

            if (!direct)
            {
                await NGame.Instance!.ReturnToMainMenu();
                // 场景切换留帧：回主菜单 → 开新局不能在同一帧内连续执行（否则画面停在旧场景 = 黑屏）
                await WaitFrames(5);

                await NGame.Instance.StartNewSingleplayerRun(
                    character, true, acts, modifiers, seed, gameMode, ascension);
            }

            // 等 2 帧确认即可：新场景已在 StartRun 内 SetCurrentScene 并完成 EnterAct(0)
            await WaitFrames(2);
            Entry.Logger?.Info($"[QuickRestart] {tag}完成：旧局未写入历史/未中断连胜（无痕生效={suppressed}） · 总耗时 {(long)Time.GetTicksMsec() - t0}ms（{(direct ? "直接" : "主菜单")}路径）");
        }
        catch (Exception ex)
        {
            // 保护：恢复动作绝不能抛异常 —— 二次异常会覆盖原始异常，
            // 导致"黑屏但看不到根因"。
            if (suppressed)
                TrySetShouldSave(prevShouldSave);
            Entry.Logger?.Error($"[QuickRestart] 重启本局失败: {ex}");

            // 开局前的 ActSnapshotStore/RoomEntryTracker.Reset 已把旧局快照作废；若两条路径都没能开出新局、
            // 旧局还活着，就得立刻用当前状态补一份幕快照与选路前快照 —— 否则"重启本层"会一直静默降级成
            // "仅重进本幕"、"重启房间"会一直降级成"新建房间重进（牌序重随机）"。
            try
            {
                if (RunManager.Instance.DebugOnlyGetState() != null)
                {
                    ActSnapshotStore.CaptureIfNeeded();
                    RoomEntryTracker.EnsurePreMapPointSnapshot();
                }
            }
            catch (Exception ex2)
            {
                Entry.Logger?.Warn($"[QuickRestart] 重启本局失败后的幕快照补记未成功: {ex2.Message}");
            }
        }
    }

    /// <summary>
    /// 换角色开新局：无痕丢弃当前局 → 回主菜单 → 停在游戏自己的角色选择屏。
    /// <para>**为什么走主菜单而不是就地开局**：换角色要连带换掉"该角色专属"的幕列表与进阶上限
    /// （<see cref="RestartRunAsync"/> 能直接复用 <c>state.Acts</c> / <c>state.AscensionLevel</c>，
    /// 前提正是角色没变）。自己复刻角色选择屏的那套组装 = 又一次押注游戏内部结构，游戏一更新就静默出错。
    /// 这里只调游戏本体的那三步（GetSubmenuType → InitializeSingleplayer → Push），
    /// 与玩家手点「单人游戏 → 新游戏 → 选角色」完全同一条路径。</para>
    /// <para>代价：走一次主菜单（比直接路径慢约 1 秒），且当前局存档被删除 ——
    /// 之后主菜单上不会有「继续游戏」能把这局捞回来。</para>
    /// </summary>
    private static async Task SwitchCharacterAsync()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 当前无进行中的对局，跳转换角色");
            return;
        }

        const string tag = "换角色开新局";
        long t0 = (long)Time.GetTicksMsec();

        ClosePauseMenu();
        EnsureCombatUnpaused();

        // 无痕：与"重启本局"同一道标记（OnEnded 的历史/连胜/成就写入全在它后面）。
        // 本流程不开新局，ShouldSave 由下一次开局/读档自己恢复；只有失败且旧局还活着时才复位。
        bool prevShouldSave = RunManager.Instance.ShouldSave;
        bool suppressed = TrySetShouldSave(false);
        Entry.Logger?.Info($"[QuickRestart] {tag}：无痕抑制 ShouldSave（成功={suppressed}，原值={prevShouldSave}）");
        if (!suppressed)
        {
            Entry.Logger?.Error($"[QuickRestart] {tag}：无法关闭 ShouldSave，本局可能写入历史/断连胜");
            ModToast.Show(ModLoc.ToastNoTraceProtectionFailedThisRun);
        }

        ActSnapshotStore.Reset();
        RoomEntryTracker.Reset();

        try
        {
            // 顺序不能调：先等存档任务、再 CleanUp、再删档，最后才回主菜单 ——
            // 主菜单在创建时读 current_run.save 决定「继续游戏」是否出现，
            // 存档留着等于在菜单里摆一个指向已丢弃局面的入口。
            await WaitForPendingSaveAsync();
            RunManager.Instance.CleanUp();
            // 游戏会无条件删 current_run.save 与它的 .backup；.backup 只是崩溃安全副本、不一定存在，
            // 缺了它游戏会记一条 "[ERROR] Error deleting path …current_run.save.backup: Failed"
            // （只记不抛，删主存档那步已成功、流程照常继续）。看到那条 ERROR 不必当故障查。
            Entry.Logger?.Info("[QuickRestart] 删除本局存档（无 .backup 副本时游戏会记一条 delete ERROR，属正常）");
            SaveManager.Instance?.DeleteCurrentRun();
            LogStep(tag, t0, "清理旧局");

            await NGame.Instance!.ReturnToMainMenu();
            LogStep(tag, t0, "回主菜单");

            // 场景切换留帧：主菜单尚未 SetCurrentScene 就推子菜单会踩空（同 RestartRunAsync 主菜单路径）
            await WaitFrames(5);
            OpenCharacterSelectScreen();

            Entry.Logger?.Info($"[QuickRestart] {tag}完成：已丢弃本局（未写入历史/未断连胜，无痕生效={suppressed}） · 总耗时 {(long)Time.GetTicksMsec() - t0}ms");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] {tag}失败: {ex}");
            // CleanUp 之后 State 已空 ⇒ 旧局确实丢了，只能提示玩家重开游戏；
            // 之前失败则旧局完好，把无痕标记还回去，别让它变成"静默不存档"。
            bool runStillAlive = RunManager.Instance.DebugOnlyGetState() != null;
            if (suppressed && runStillAlive)
                TrySetShouldSave(prevShouldSave);
            ModToast.Show(runStillAlive
                ? ModLoc.ToastSwitchCharacterFailedKeptRun
                : ModLoc.ToastSwitchCharacterFailedRunLost);
        }
        finally
        {
            // ReturnToMainMenu 已经 FadeOut，任何一步抛异常都必须补淡入，否则永久黑屏
            await FadeInSafeAsync(tag);
        }
    }

    /// <summary>打开游戏本体的角色选择子菜单（= 玩家点「单人游戏 → 新游戏」的那三步）。</summary>
    private static void OpenCharacterSelectScreen()
    {
        var stack = NGame.Instance?.MainMenu?.SubmenuStack;
        if (stack == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 主菜单未就绪，停在主菜单（请手动进入 单人游戏→新游戏）");
            ModToast.Show(ModLoc.ToastMainMenuNotReady);
            return;
        }

        var screen = stack.GetSubmenuType<NCharacterSelectScreen>();
        screen.InitializeSingleplayer();
        stack.Push(screen);
        Entry.Logger?.Info("[QuickRestart] 已打开游戏的角色选择屏");
    }
}
