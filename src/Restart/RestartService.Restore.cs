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
/// 把回滚后的 RunState 交还给游戏自己的读档流程，以及**三条回滚流程共用的骨架**
/// <see cref="RollbackAsync"/>（无痕 + 暂停快照记录 + 直接路径失败自动回退主菜单）：
/// 恢复场景、按原坐标重进本节点、回填地图手绘标记、重建房间实例（降级路径）、落点兜底。
/// </summary>
internal static partial class RestartService
{
    /// <summary>
    /// 构造与当前房间等价的全新实例（清空战斗/宝箱/事件的可变状态）；不支持的房间类型原样返回。
    /// 各类型的构造分支与 <c>RunManager.CreateRoom</c> 保持一致。
    /// </summary>
    private static AbstractRoom RebuildRoomInstance(AbstractRoom room, RunState state)
    {
        switch (room)
        {
            case CombatRoom combat:
            {
                // ★ 不能复用旧战斗的 mutable Encounter：它的 _monstersWithSlots 已生成，
                //   里面的怪物已设过 RunRng，StartCombat → CreateCreature 会抛
                //   "RunRng has already been set!"。
                //   正确做法（同 RunManager.CreateRoom）：用规范 EncounterModel.ToMutable() 出全新 mutable
                //   → 进房时 GenerateMonstersWithSlots 重新生成干净怪物。
                var canonical = ModelDb.GetByIdOrNull<EncounterModel>(combat.Encounter.Id);
                return new CombatRoom(canonical != null ? canonical.ToMutable() : combat.Encounter, state);
            }
            case TreasureRoom:
                return new TreasureRoom(state.CurrentActIndex);
            case EventRoom eventRoom:
                // 规范事件重新触发
                return new EventRoom(eventRoom.CanonicalEvent);
            case MerchantRoom:
                return new MerchantRoom();
            case RestSiteRoom:
                return new RestSiteRoom();
            default:
                return room;
        }
    }

    /// <summary>
    /// 恢复对局场景（等价于 <c>NGame.LoadRun</c> 的前半段），**但不走 <c>LoadIntoLatestMapCoord</c>**。
    /// <para>⚠️ 为什么要复刻：官方 <c>LoadRun</c> 内部的 <c>LoadIntoLatestMapCoord</c> 会按
    /// <c>VisitedMapCoords[^1]</c> "重进最后访问的那个节点"，而 <c>EnterMapPointInternal</c> 在
    /// <c>preFinishedRoom == null</c> 时会 <c>AppendToMapPointHistory</c>（往地图点历史追加新的一格）
    /// 会 <c>AppendToMapPointHistory</c> 追加一格并按节点类型重新 roll/新建房间
    /// —— 表现就是"回滚一次，地图自己前进一层"。</para>
    /// <para>落点由调用方决定：<c>EnterRoom(new MapRoom())</c> 停在地图屏，或
    /// <c>EnterMapPointInternal(...)</c> 重新进入指定节点（见 <see cref="EnterRoomAgainAsync"/>）。</para>
    /// </summary>
    private static async Task RestoreRunSceneAsync(RunState runState)
    {
        // 游戏在切场景前会先等"进行中的存档写入"完成。直接路径跳过了主菜单，这道
        // 等待必须自己补 —— 否则切场景/换 run 会与正在写 current_run.save 的任务并发
        // （CleanUp 已把 State 置空，在途任务的后续步骤可能读到空状态）。
        await WaitForPendingSaveAsync();

        await PreloadManager.LoadRunAssets(runState.Players.Select(p => p.Character));
        await PreloadManager.LoadActAssets(runState.Act);

        // 预载收尾会 GC.Collect，而游戏的资源缓存卸载走 CallDeferred(Dispose)；
        // LoadRunAssets/LoadActAssets 里的 UnloadMissedCacheAssets() 又会把
        // NInputSettingsEntry 用的 input_settings_entry.tscn（不在任何预载集合里）摘掉并排上延迟释放。
        // 建 run 场景时 NInputSettingsPanel._Ready 会**同步**加载它，若释放已落地 → 空 SceneState →
        // Godot 原生 FATAL 崩进程（无 C# 异常）。
        // ★ 旧版只在这里等 2 帧"错开窗口"，真机日志证明兜不住（v0.1.7 按 F 重启本层仍崩，
        //   崩溃点就在下面这两次预载之后、同一批续体里）。
        //   现在改为**确定性预检**（见 RestartService.AssetGuard.cs）：用不会崩的 CanInstantiate()
        //   判定关键场景是否健康，不健康就让直接路径失败，由 RollbackAsync 回退到官方主菜单路径。
        if (!await EnsureCriticalScenesUsableAsync("读档恢复场景"))
            throw new InvalidOperationException(
                "关键场景资源不可用（run 场景同步加载所需），为避免 Godot 原生崩溃改走主菜单路径");

        RunManager.Instance.Launch();
        NGame.Instance!.RootSceneContainer.SetCurrentScene(NRun.Create(runState));
        await RunManager.Instance.GenerateMap();

        // 读档收尾的一步（GenerateMap 里 SetMap(clearDrawings:true) 会清空涂鸦，之后才回填）。
        // 漏了它 = 每次回滚都把玩家在地图上的手绘标记弄丢，且 MapDrawingsToLoad 一直挂在实例上没人消费。
        LoadMapDrawings();
    }

    /// <summary>等游戏自己那笔在途的对局存档写完（失败/超时都不拦流程，只记日志）。</summary>
    private static async Task WaitForPendingSaveAsync()
    {
        try
        {
            Task? pending = SaveManager.Instance?.CurrentRunSaveTask;
            if (pending == null)
                return;
            Entry.Logger?.Info($"[QuickRestart] 读档恢复场景：等待进行中的存档写入完成后再切场景");
            await pending;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 读档恢复场景：等待存档任务异常（继续流程）: {ex.Message}");
        }
    }

    /// <summary>回填存档里的地图手绘标记（与 <c>NGame.LoadRun</c> 的收尾同一步）。</summary>
    private static void LoadMapDrawings()
    {
        try
        {
            var drawings = RunManager.Instance.MapDrawingsToLoad;
            if (drawings == null)
                return;
            RunManager.Instance.MapDrawingsToLoad = null;   // 先摘走：别让下一次官方读档再消费一遍

            var mapScreen = NRun.Instance?.GlobalUi?.MapScreen;
            var drawingsNode = mapScreen?.Drawings;
            if (drawingsNode == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 读档恢复场景：地图屏未就绪，本次无法恢复地图手绘标记");
                return;
            }
            drawingsNode.LoadDrawings(drawings);
            Entry.Logger?.Info("[QuickRestart] 读档恢复场景：已恢复地图手绘标记");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 读档恢复场景：恢复地图手绘标记失败（不影响回滚）: {ex.Message}");
        }
    }

    /// <summary>
    /// 重新进入指定地图节点 —— 等价于"第一次点击该节点"的完整流程：
    /// <c>AddVisitedMapCoord</c>（标记访问）+ <c>EnterMapPointInternal</c>（按节点类型新建房间 +
    /// 追加地图点历史，<paramref name="saveGame"/>:false 不写存档）。
    /// 房间内容由快照 RNG 决定 → 与首次进入一致（商店不刷新、牌序相同）。
    /// 坐标无效或失败时退化为"停在地图屏"。
    /// </summary>
    private static async Task EnterRoomAgainAsync(RunState runState, MapCoord? coord, string tag)
    {
        try
        {
            if (coord.HasValue)
            {
                MapPoint? point = runState.Map.GetPoint(coord.Value);
                if (point != null)
                {
                    runState.AddVisitedMapCoord(coord.Value);
                    await RunManager.Instance.EnterMapPointInternal(coord.Value.row + 1, point.PointType, null, saveGame: false);
                    Entry.Logger?.Info($"[QuickRestart] {tag}：已重新进入节点 {coord.Value}（{point.PointType}）");
                    return;
                }
                Entry.Logger?.Warn($"[QuickRestart] {tag}：节点 {coord.Value} 在地图上不存在，改为停在地图屏");
            }
            else
            {
                Entry.Logger?.Warn($"[QuickRestart] {tag}：节点坐标缺失，改为停在地图屏");
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] {tag}：重新进入节点失败，改为停在地图屏: {ex.Message}");
        }
        await RunManager.Instance.EnterRoom(new MapRoom());
    }

    /// <summary>
    /// 回滚类流程（重启房间 / 重启本层 / 回到地图）的共同骨架：无痕 + 回滚期间暂停快照记录 +
    /// 官方读档流程，落地动作交给 <paramref name="landAsync"/>（重进本节点 / 停在地图屏）。
    /// <para>默认走「直接路径」：CleanUp（内含 State=null）后即可 <c>SetUpSavedSingleplayer</c>，
    /// 跳过主菜单的资源预载与场景创建（省约 1~2 秒黑屏）。它任何一步异常都自动回退到
    /// 完整的「回主菜单 → 读档」官方流程 —— 两条路径执行同样的落地动作，故回退不改变结果。</para>
    /// </summary>
    /// <param name="ensureMapRoom">落地后校验/强制停在地图屏。<b>必须在暂停快照的窗口内做</b> ——
    /// 兜底的 <c>EnterRoom</c> 同样会触发 <c>SetMap</c> 后缀，出了窗口就可能在"读到一半"的状态上补记错误快照。</param>
    /// <returns>本次回滚的耗时毫秒；失败返回 -1（异常已记 ERROR，调用方据此直接返回）。</returns>
    private static async Task<long> RollbackAsync(string tag, SerializableRun snapshot,
        Func<RunState, Task> landAsync, bool ensureMapRoom = false)
    {
        long t0 = (long)Time.GetTicksMsec();

        bool prevShouldSave = RunManager.Instance.ShouldSave;
        bool suppressed = TrySetShouldSave(false);
        bool prevSuspended = RoomEntryTracker.Suspended;
        // 回滚与落地期间暂停快照记录：否则"读到一半的中间状态"会被当成新快照覆盖掉正确数据。
        // （GenerateMap→SetMap 的补记路径目前靠"同局同幕"字段相等恰好不会写坏，但那份正确性
        //  依赖 _startTime 反射可用 —— 一旦取不到就破防，所以显式暂停而不是赌字段相等。）
        RoomEntryTracker.Suspended = true;
        try
        {
            bool direct = false;
            if (SettingsManager.Current.FastRestartSkipMenu)
            {
                try
                {
                    // 反序列化与淡出并行：FromSerializable 是纯 CPU 构建（不碰 RunManager 状态），
                    // 放进淡出动画的推进期间执行 = 把它藏进黑幕里（省 10~100ms 黑屏）。
                    var fadeTask = NGame.Instance!.Transition.FadeOut(DirectFadeSec);
                    RunState runState;
                    try
                    {
                        runState = RunState.FromSerializable(snapshot);
                    }
                    finally
                    {
                        // 反序列化失败也不留孤儿淡出任务（详见 AwaitFadeSafeAsync 注释）
                        await AwaitFadeSafeAsync(fadeTask, tag);
                    }
                    LogStep(tag, t0, "FadeOut");
                    RunManager.Instance.CleanUp();
                    LogStep(tag, t0, "CleanUp");

                    await RunManager.Instance.SetUpSavedSingleplayer(runState, snapshot);
                    await RestoreRunSceneAsync(runState);
                    await landAsync(runState);
                    await FadeInSafeAsync(tag);
                    direct = true;
                    LogStep(tag, t0, "读档");
                }
                catch (Exception ex)
                {
                    Entry.Logger?.Error($"[QuickRestart] 直接路径失败（{tag}），回退主菜单路径: {ex.Message}");
                }
            }

            if (!direct)
            {
                await NGame.Instance!.ReturnToMainMenu();
                // 场景切换留帧：回主菜单与读档不能同帧连续执行（否则画面停在旧场景 = 黑屏）
                await WaitFrames(5);

                var runState = RunState.FromSerializable(snapshot);
                await RunManager.Instance.SetUpSavedSingleplayer(runState, snapshot);
                await RestoreRunSceneAsync(runState);
                await landAsync(runState);
            }

            // 等 2 帧确认即可：读档内部已完成场景切换与落地动作
            await WaitFrames(2);
            if (ensureMapRoom)
                await EnsureMapRoomAsync(tag);

            long elapsed = (long)Time.GetTicksMsec() - t0;
            Entry.Logger?.Info($"[QuickRestart] ⏱ {tag} · 回滚+读档 总耗时 {elapsed}ms（{(direct ? "直接" : "主菜单")}路径）");
            return elapsed;
        }
        catch (Exception ex)
        {
            // 读档没成 → 旧局还原样在跑，把无痕标记还回去，别让它静默变成"这局不存档"
            if (suppressed)
                TrySetShouldSave(prevShouldSave);
            Entry.Logger?.Error($"[QuickRestart] {tag}失败: {ex}");
            return -1;
        }
        finally
        {
            RoomEntryTracker.Suspended = prevSuspended;
        }
    }

    /// <summary>落点兜底：回滚后应停在地图屏（本节点在快照里尚未标记访问，所以在地图上能重新点它）。</summary>
    private static async Task EnsureMapRoomAsync(string tag)
    {
        try
        {
            var room = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
            if (room is not MapRoom)
            {
                Entry.Logger?.Warn($"[QuickRestart] {tag}：落点异常（{room?.RoomType.ToString() ?? "无"}），强制退回地图屏");
                await RunManager.Instance.EnterRoom(new MapRoom());
                await WaitFrames(2);
            }
        }
        catch (Exception ex)
        {
            // 读档本身已成功，落点检查失败不该把整次回滚判成失败
            Entry.Logger?.Warn($"[QuickRestart] {tag}：落点检查/退回地图屏失败: {ex.Message}");
        }
    }
}
