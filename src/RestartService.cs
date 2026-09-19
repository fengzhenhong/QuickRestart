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
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace QuickRestart;

internal static class RestartService
{
    public enum RestartType
    {
        RestartRoom,
        RestartFloor,
        RestartRun,
        NewRun
    }

    /// <summary>直接路径黑幕动画时长（秒）。NGame.Transition.FadeOut/FadeIn 默认各 0.8s —— 合计 1.6s 纯动画；
    /// 直接路径改成 0.22s 显著缩短黑屏，观感仍平滑。想更慢/更快只调这一个常量。</summary>
    private const float DirectFadeSec = 0.22f;

    /// <summary>0=空闲 1=重启进行中。一次重启要跨多帧（回主菜单→等帧→读档，耗时数秒），
    /// 期间快捷键仍可触发 —— 无重入保护会让两条流程交错（场景切换/读档互相踩），黑屏或状态错乱。</summary>
    private static int _busy;

    /// <summary>
    /// 「回滚 / 读档 / 重启」类功能的统一模式守卫（所有入口共用：快捷键、暂停菜单按钮、
    /// 刀下留人弹窗回调、战后重新挑战）。命中时已给出屏幕提示，调用方直接 return。
    /// <para>⚠️ 这是**每个入口都必须调**的防线，不能只靠"联机下不注入按钮"——
    /// 按钮注入点与执行点之间的任何一条旁路（如重新挑战按钮、弹窗回调）都会绕过它。</para>
    /// </summary>
    /// <param name="allowDaily">
    /// 每日挑战局是否放行。<c>false</c>=整族"重启"（房间/本层/本局/新局/战后重挑战）一律拒绝 ——
    /// 重开一局拿不到每日结算依据（<c>DailyTime</c> 不在对局状态里），也等于无限重 roll 每日种子。
    /// <c>true</c>=允许"回到地图"这类**原地回滚**（刀下留人的出路之一，用户明确要求每日局保留救场）。
    /// </param>
    internal static bool IsBlockedByMode(string what, bool allowDaily = false)
    {
        // 联机：本地回滚必然被校验和判定为状态分歧（详见 NetGuard）
        if (NetGuard.IsMultiplayer())
        {
            Entry.Logger?.Warn($"[QuickRestart] 联机模式下不支持{what}，已忽略");
            ModToast.Show($"联机模式下不可用（{Entry.ModName}仅支持单人）");
            return true;
        }
        // 每日挑战：重开类操作拒绝（详见 NetGuard.IsDailyRun）
        if (!allowDaily && NetGuard.IsDailyRun())
        {
            Entry.Logger?.Warn($"[QuickRestart] 每日挑战局不支持{what}，已忽略");
            ModToast.Show($"每日挑战局不支持{what}");
            return true;
        }
        return false;
    }

    public static async Task ExecuteRestart(RestartType type)
    {
        if (IsBlockedByMode("重启/回滚"))
            return;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            Entry.Logger?.Warn($"[QuickRestart] 已有重启流程进行中，忽略本次触发 type={type}");
            return;
        }
        RestartOverlay.Show();
        try
        {
            Entry.Logger?.Info($"[QuickRestart] 执行重启 type={type}");
            switch (type)
            {
                case RestartType.RestartRoom:
                    await RestartRoomAsync();
                    break;
                case RestartType.RestartFloor:
                    await RestartFloorAsync();
                    break;
                case RestartType.RestartRun:
                    await RestartRunAsync(sameSeed: true);
                    break;
                case RestartType.NewRun:
                    await RestartRunAsync(sameSeed: false);
                    break;
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 重启失败 type={type}: {ex}");
        }
        finally
        {
            RestartOverlay.Hide();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static async Task RestartRoomAsync()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 无法获取 RunState，跳过重启房间");
            return;
        }

        var room = state.CurrentRoom;
        if (room == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 当前无房间，跳过重启房间");
            return;
        }

        Entry.Logger?.Info($"[QuickRestart] 重启房间 RoomId={room.Id} Type={room.GetType().Name}");

        ClosePauseMenu();

        // ★ 刀下留人场景：拦截时调用了 CombatManager.Pause()，战斗回合循环卡在
        //   `while (IsPaused && turnState.IsLive)`；不解除的话读档/退出流程永远等不到。
        EnsureCombatUnpaused();

        RoomType roomType = room.RoomType;
        // 本房间的地图坐标：回滚后用它"重新进入本节点"（等价于第一次点击该节点）
        MapCoord? roomCoord = state.CurrentMapCoord;

        // ★ 用「点击节点前」快照（PreMapPointSnapshot，捕获于 EnterMapCoord/AddVisitedMapCoord 之前）：
        //   此时房间类型**尚未 roll**（问号节点的类型由 Odds.UnknownMapPoint.Roll 决定并消耗 RNG）。
        //   回滚后走"重新进入本节点"会重演这次 roll、用的正是同一份 RNG → 问号房内容与首次完全一致。
        //   ⚠️ 旧实现用 EnterRoom 前缀快照（= roll 之后的状态）→ 重启时触发二次 roll → 问号房内容会变化。
        var preSnap = RoomEntryTracker.PreMapPointSnapshot;
        if (preSnap == null
            || !RoomEntryTracker.IsSnapshotForCurrentRun(preSnap, RoomEntryTracker.PreMapPointRunStart, "重启房间"))
        {
            // 兜底（快照缺失，或属于上一局——玩家用游戏自带流程开新局时旧快照残留）：
            // 新建房间重进 —— 只回退血量，牌序会重新随机
            Entry.Logger?.Warn("[QuickRestart] 无可用进房前快照（缺失或属于上一局），回退为新建房间重进（仅回退血量、牌序重新随机）");
            try
            {
                RoomEntryTracker.RestorePreRoomHp();
                await RunManager.Instance.EnterRoom(RebuildRoomInstance(room, state));
                Entry.Logger?.Info("[QuickRestart] 重启房间完成（回退路径）");
            }
            catch (Exception ex)
            {
                // 本兜底路径此前无 try（经"重新挑战"按钮进入时异常会变成未观察任务异常）
                Entry.Logger?.Error($"[QuickRestart] 重启房间失败（回退路径）: {ex}");
            }
            return;
        }

        bool prevShouldSave = RunManager.Instance.ShouldSave;
        bool suppressed = TrySetShouldSave(false);
        bool prevSuspended = RoomEntryTracker.Suspended;
        RoomEntryTracker.Suspended = true;   // 回滚与自动重进期间不记录新快照
        try
        {
            // ★ 精确重现：先经官方读档流程回滚到"进房前"（RNG 一并回滚），
            //   再自动重进房间 —— 洗牌/怪物与首次进房完全一致（牌序相同，适合练习同一场战斗）。
            Entry.Logger?.Info("[QuickRestart] 重启房间：回滚到进房前（RNG 一并回滚，牌序将与首次进房一致）");
            long t0 = (long)Time.GetTicksMsec();
            bool direct = false;
            RunState? runState = null;

            if (SettingsManager.Current.FastRestartSkipMenu)
            {
                // ④ 直接路径：FadeOut + CleanUp 后直接读档（SetUpSavedSingleplayer 只要求 State==null，
                //    CleanUp 即满足）—— 跳过主菜单资源预载与场景创建；异常自动回退旧路径。
                try
                {
                    // 反序列化与淡出并行：FromSerializable 是纯 CPU 构建（不碰 RunManager 状态），
                    // 放在淡出动画推进期间执行，等于把它藏进 0.22s 黑幕里（省 10~100ms 黑屏）。
                    var fadeTask = NGame.Instance!.Transition.FadeOut(DirectFadeSec);
                    try
                    {
                        runState = RunState.FromSerializable(preSnap);
                    }
                    finally
                    {
                        // 反序列化失败也不留孤儿淡出任务（详见 AwaitFadeSafeAsync 注释）
                        await AwaitFadeSafeAsync(fadeTask, "重启房间");
                    }
                    LogStep("重启房间", t0, "FadeOut");
                    RunManager.Instance.CleanUp();
                    LogStep("重启房间", t0, "CleanUp");

                    await RunManager.Instance.SetUpSavedSingleplayer(runState, preSnap);
                    await RestoreRunSceneAsync(runState);
                    await EnterRoomAgainAsync(runState, roomCoord, "重启房间");
                    await FadeInSafeAsync("重启房间");
                    direct = true;
                    LogStep("重启房间", t0, "读档");
                }
                catch (Exception ex)
                {
                    Entry.Logger?.Error($"[QuickRestart] 直接路径失败（重启房间），回退主菜单路径: {ex.Message}");
                }
            }

            if (!direct)
            {
                await NGame.Instance!.ReturnToMainMenu();
                await WaitFrames(5);

                runState = RunState.FromSerializable(preSnap);
                await RunManager.Instance.SetUpSavedSingleplayer(runState, preSnap);
                await RestoreRunSceneAsync(runState);
                await EnterRoomAgainAsync(runState, roomCoord, "重启房间");
            }

            // ③ 等帧优化：LoadRun 内部已完成场景切换与本房间自动重进，2 帧确认即可
            //   （旧路径的 5 帧是给"主菜单场景切换"留的，直接路径没有这次切换）
            await WaitFrames(2);
            Entry.Logger?.Info($"[QuickRestart] ⏱ 重启房间 · 回滚+读档 总耗时 {(long)Time.GetTicksMsec() - t0}ms（{(direct ? "直接" : "主菜单")}路径）");

            // ══════════════════════════════════════════════════════════════════
            // 房间内容由快照 RNG 决定 → 与首次进房完全一致（牌序相同、商店库存不刷新）。
            // "重新进入本节点"走 EnterRoomAgainAsync（AddVisitedMapCoord + EnterMapPointInternal，
            // 等价于第一次点击该节点）—— 不再依赖 LoadRun 的"自动重进最后访问节点"：
            // 那条路会 AppendToMapPointHistory 让地图历史凭空前进一格（玩家反馈的"前进一层"）。
            // ══════════════════════════════════════════════════════════════════
            var restoredRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
            Entry.Logger?.Info($"[QuickRestart] 重启房间完成：当前房间={restoredRoom?.RoomType.ToString() ?? "无"}（期望 {roomType}；内容与首次一致，商店不会刷新）");
        }
        catch (Exception ex)
        {
            if (suppressed)
                TrySetShouldSave(prevShouldSave);
            Entry.Logger?.Error($"[QuickRestart] 重启房间失败: {ex}");
        }
        finally
        {
            RoomEntryTracker.Suspended = prevSuspended;
        }
    }

    /// <summary>
    /// 构造与当前房间等价的全新实例（清空战斗/宝箱/事件的可变状态）；不支持的房间类型原样返回（沿用旧行为）。
    /// 路径与游戏 <c>RunManager.CreateRoom</c> 官方实现保持一致。
    /// </summary>
    private static AbstractRoom RebuildRoomInstance(AbstractRoom room, RunState state)
    {
        switch (room)
        {
            case CombatRoom combat:
            {
                // ★ 不能复用旧战斗的 mutable Encounter：它的 _monstersWithSlots 已生成，
                //   里面的怪物已设过 RunRng，StartCombat → CreateCreature 会抛
                //   "RunRng has already been set!"（日志实证）。
                //   官方路径（RunManager.CreateRoom）：规范 EncounterModel.ToMutable() 出全新 mutable
                //   → 进房时 GenerateMonstersWithSlots 重新生成干净怪物。
                var canonical = ModelDb.GetByIdOrNull<EncounterModel>(combat.Encounter.Id);
                return new CombatRoom(canonical != null ? canonical.ToMutable() : combat.Encounter, state);
            }
            case TreasureRoom:
                return new TreasureRoom(state.CurrentActIndex);
            case EventRoom eventRoom:
                // 规范事件重新触发（与官方 CreateRoom 的 Event 分支同款）
                return new EventRoom(eventRoom.CanonicalEvent);
            case MerchantRoom:
                return new MerchantRoom();
            case RestSiteRoom:
                return new RestSiteRoom();
            default:
                return room;
        }
    }

    private static async Task RestartFloorAsync()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 无法获取 RunState，跳过重启本层");
            return;
        }

        int actIndex = state.CurrentActIndex;

        // 刀下留人等场景可能把战斗置于暂停态，先解除（无暂停时为无操作）
        EnsureCombatUnpaused();

        var snapshot = ActSnapshotStore.Snapshot;

        // 快照缺失/不属于当前幕/属于上一局（跨局残留）→ 回退旧行为：仅重进本幕（不回滚物品）
        if (snapshot == null || ActSnapshotStore.SnapshotActIndex != actIndex
            || !RoomEntryTracker.IsSnapshotForCurrentRun(snapshot, ActSnapshotStore.SnapshotRunStart, "重启本层"))
        {
            Entry.Logger?.Warn($"[QuickRestart] 无第 {actIndex + 1} 幕起点快照（缺失/非本幕/属于上一局），回退为仅重进本幕");
            ClosePauseMenu();
            await RunManager.Instance.EnterAct(actIndex);
            return;
        }

        if (ActSnapshotStore.SnapshotFromResume)
        {
            Entry.Logger?.Warn($"[QuickRestart] 注意：本幕快照来自**读档恢复点**（本次会话直接读档继续、未经历真实幕初）—— 重启本层将回滚到「读档时」的状态，而不是本幕开场");
            // 如实告知玩家（游戏存档里没有"本幕开场"数据，读档进入的当幕无法回滚到幕初，只能到读档点）
            RestartOverlay.SetNote("本幕快照来自读档点：只能回到「读档时」的状态（游戏未保存本幕开场数据）");
        }
        Entry.Logger?.Info($"[QuickRestart] 重启本层：回滚到第 {actIndex + 1} 幕快照点（本幕获得的金币/卡牌/遗物/药水/血量将全部回退）");
        ClosePauseMenu();

        bool prevShouldSave = RunManager.Instance.ShouldSave;
        bool suppressed = TrySetShouldSave(false);
        // 与"重启房间/回到地图"一致：回滚期间暂停快照记录。
        // 目前 GenerateMap→SetMap 的补记路径靠"同局同幕"判据恰好不会写坏东西，但那种正确性
        // 依赖字段相等（_startTime 一旦取不到就破防），显式暂停才可靠。
        bool prevSuspended = RoomEntryTracker.Suspended;
        RoomEntryTracker.Suspended = true;

        try
        {
            // 官方读档流程：CleanUp（内含 State=null）→ RunState.FromSerializable 重建 →
            //   SetUpSavedSingleplayer（要求 State==null）→ NGame.LoadRun。
            // ④ 直接路径跳过其中的主菜单环节（FadeOut 后直接 CleanUp）；异常自动回退完整旧路径。
            long t0 = (long)Time.GetTicksMsec();
            bool direct = false;

            if (SettingsManager.Current.FastRestartSkipMenu)
            {
                try
                {
                    // 反序列化与淡出并行（同"重启房间"）
                    var fadeTask = NGame.Instance!.Transition.FadeOut(DirectFadeSec);
                    RunState runStateDirect;
                    try
                    {
                        runStateDirect = RunState.FromSerializable(snapshot);
                    }
                    finally
                    {
                        await AwaitFadeSafeAsync(fadeTask, "重启本层");
                    }
                    LogStep("重启本层", t0, "FadeOut");
                    RunManager.Instance.CleanUp();
                    LogStep("重启本层", t0, "CleanUp");

                    await RunManager.Instance.SetUpSavedSingleplayer(runStateDirect, snapshot);
                    await RestoreRunSceneAsync(runStateDirect);
                    await RunManager.Instance.EnterRoom(new MapRoom());   // ★ 本幕起点 = 地图屏（不再自动重进上一格）
                    await FadeInSafeAsync("重启本层");
                    direct = true;
                    LogStep("重启本层", t0, "读档");
                }
                catch (Exception ex)
                {
                    Entry.Logger?.Error($"[QuickRestart] 直接路径失败（重启本层），回退主菜单路径: {ex.Message}");
                }
            }

            if (!direct)
            {
                await NGame.Instance!.ReturnToMainMenu();
                await WaitFrames(5);

                var runState = RunState.FromSerializable(snapshot);
                await RunManager.Instance.SetUpSavedSingleplayer(runState, snapshot);
                await RestoreRunSceneAsync(runState);
                await RunManager.Instance.EnterRoom(new MapRoom());
            }

            // ③ 等帧优化：LoadRun 已完成场景切换与房间自动重进，2 帧确认即可
            await WaitFrames(2);

            // 落点兜底：本层快照回滚后应停在地图屏（本幕起点只能从地图继续）；异常落点强制退回，
            // 并记录实际落点 —— 旧版此处无日志，"按了没回去"无从判断（2026-09-15 玩家反馈）。
            try
            {
                var restoredRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
                if (restoredRoom is not MapRoom)
                {
                    Entry.Logger?.Warn($"[QuickRestart] 重启本层：落点异常（{restoredRoom?.RoomType.ToString() ?? "无"}），强制退回地图屏");
                    await RunManager.Instance.EnterRoom(new MapRoom());
                    await WaitFrames(2);
                }
            }
            catch (Exception ex)
            {
                Entry.Logger?.Warn($"[QuickRestart] 重启本层：落点检查/退回地图屏失败: {ex.Message}");
            }

            Entry.Logger?.Info($"[QuickRestart] 重启本层完成：已回滚到第 {actIndex + 1} 幕快照点、落点=地图屏 · 总耗时 {(long)Time.GetTicksMsec() - t0}ms（{(direct ? "直接" : "主菜单")}路径）{(ActSnapshotStore.SnapshotFromResume ? " ⚠ 快照=读档点（非真实幕初）" : "")}");
        }
        catch (Exception ex)
        {
            if (suppressed)
                TrySetShouldSave(prevShouldSave);
            Entry.Logger?.Error($"[QuickRestart] 重启本层失败: {ex}");
        }
        finally
        {
            RoomEntryTracker.Suspended = prevSuspended;
        }
    }

    /// <summary>
    /// 反射设置 RunManager.ShouldSave。
    /// ⚠️ 不能用直接赋值：Krafs.Publicizer 只改**编译期**签名 —— 直接写编译通过，
    /// 但运行时抛 MethodAccessException（日志实证：Attempt by method '...&lt;RestartRunAsync&gt;d__4.MoveNext()'
    /// to access method 'MegaCrit.Sts2.Core.Runs.RunManager.set_ShouldSave(Boolean)' failed）。
    /// 反射调用私有 setter 则始终可用。本方法自身不抛异常。
    /// </summary>
    private static bool TrySetShouldSave(bool value)
    {
        try
        {
            PropertyInfo? prop = typeof(RunManager).GetProperty("ShouldSave",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetSetMethod(nonPublic: true) == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 未找到 RunManager.ShouldSave 的 setter");
                return false;
            }
            prop.SetValue(RunManager.Instance, value);
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 设置 ShouldSave={value} 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 恢复对局场景（复刻官方 <c>NGame.LoadRun</c> 的前半段），**但跳过 LoadIntoLatestMapCoord**。
    /// <para>⚠️ 为什么要复刻：官方 <c>LoadRun</c> 内部的 <c>LoadIntoLatestMapCoord</c> 会按
    /// <c>VisitedMapCoords[^1]</c> "重进最后访问的那个节点"，而 <c>EnterMapPointInternal</c> 在
    /// <c>preFinishedRoom == null</c> 时会 <c>AppendToMapPointHistory</c>（往地图点历史追加新的一格）
    /// 并按节点类型重新 roll/新建房间 —— 玩家实测反馈的"每次刀下留人都会前进一层"即由此而来
    /// （地图位置/历史被凭空推进一格）。</para>
    /// <para>落点由调用方决定：<c>EnterRoom(new MapRoom())</c> 停在地图屏，或
    /// <c>EnterMapPointInternal(...)</c> 重新进入指定节点（见 <see cref="EnterRoomAgainAsync"/>）。</para>
    /// </summary>
    private static async Task RestoreRunSceneAsync(RunState runState)
    {
        // 官方 NGame.LoadMainMenu 在切场景前会先等"进行中的存档写入"完成
        //（日志原话："Saving in progress, waiting for it to be finished before loading the main menu"）。
        // 直接路径跳过了主菜单，这道等待必须自己补 —— 否则切场景/换 run 会与游戏正在写的
        // current_run.save 并发（CleanUp 已把 State 置空，在途任务的后续步骤可能读到空状态）。
        await WaitForPendingSaveAsync();

        await PreloadManager.LoadRunAssets(runState.Players.Select(p => p.Character));
        await PreloadManager.LoadActAssets(runState.Act);
        RunManager.Instance.Launch();
        NGame.Instance!.RootSceneContainer.SetCurrentScene(NRun.Create(runState));
        await RunManager.Instance.GenerateMap();

        // 官方 LoadRun 的收尾一步（GenerateMap 里 SetMap(clearDrawings:true) 会清空涂鸦，之后才回填）。
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

    /// <summary>回填存档里的地图手绘标记（与官方 NGame.LoadRun 同一处理）。</summary>
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
    /// 等待淡出任务完成并观察其异常（绝不抛出）—— 防止孤儿任务停留在后台，
    /// 与回退路径的转场调用并发操作同一 NTransition 节点（阈值/MouseFilter 互相覆盖）。
    /// </summary>
    private static async Task AwaitFadeSafeAsync(Task fadeTask, string tag)
    {
        try
        {
            await fadeTask;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 转场淡出等待异常（{tag}）: {ex.Message}");
        }
    }

    /// <summary>④ 分段耗时日志（验收直接路径收益用；t0=起点 GetTicksMsec）。</summary>
    private static void LogStep(string tag, long startMs, string name)
    {
        Entry.Logger?.Info($"[QuickRestart] ⏱ {tag} · {name} = {(long)Time.GetTicksMsec() - startMs}ms");
    }

    /// <summary>
    /// 直接路径收尾：恢复全屏转场（去掉黑幕）。
    /// ⚠️ LoadRun / StartNewSingleplayerRun **自身不负责淡入** —— 官方调用点全部在之后手动
    /// `Transition.FadeIn()`（如 NMainMenu 继续游戏：FadeOut → LoadRun → FadeIn）。
    /// 旧路径之所以不黑屏，是因为主菜单场景 _Ready 里会自己 FadeIn；跳过主菜单后必须由我们补齐，
    /// 否则全屏黑幕（Transition 的 threshold=1）永久不恢复 = 一直黑屏（2026-09-15 实测踩坑）。
    /// 失败只记警告：读档本身已成功，不回退。
    /// </summary>
    private static async Task FadeInSafeAsync(string tag)
    {
        try
        {
            // 与游戏淡入重叠：先收起我方遮罩（0.15s 淡出）再启动 FadeIn（0.22s），
            // 两个动画并行 → 省掉遮罩收尾的额外黑屏时间，观感也更连贯。
            RestartOverlay.Hide();
            await NGame.Instance!.Transition.FadeIn(DirectFadeSec);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 恢复转场（FadeIn）失败（{tag}）: {ex.Message}");
        }
    }

    /// <summary>等待 N 个渲染帧（主线程）。场景切换（回主菜单 → 开新局）之间需要留帧，
    /// 同一帧内连续切换会导致场景容器（NSceneContainer.SetCurrentScene）状态错乱、黑屏。</summary>
    private static async Task WaitFrames(int count)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree)
                return;
            for (int i = 0; i < count; i++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        catch
        {
            // 等帧失败不致命，继续流程
        }
    }

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
        // 而 StartNewSingleplayerRun 内部会对入参做 ToMutable()（要求规范实例），
        // 直接传 mutable 会抛 MutableModelException —— 这是旧版"重启本局"点击即失败的根因。
        // 正确取法：ModelDb.GetByIdOrNull<T>(id)（游戏内部同款用法，见 ActModel.FromSave）。
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
            // ⚠️ RunRngSet.StringSeed 在部分路径下为空（同 RoomEntryTracker 里跨局判据踩过的坑）：
            //   此时"同种子重开"无从谈起，用新随机种子顶上并如实告知，别把空串当种子传给新局。
            Entry.Logger?.Warn("[QuickRestart] 当前局种子为空，无法同种子重开 → 本次改用新随机种子");
            ModToast.Show("未能取到本局种子，已改用新种子开局");
            sameRunSeed = null;
        }

        string seed = sameRunSeed ?? SeedHelper.GetRandomSeed();

        Entry.Logger?.Info($"[QuickRestart] 重启本局 sameSeed={sameSeed} seed={seed} character={character.Id} ascension={ascension} acts={acts.Count} modifiers={modifiers.Count}");

        ClosePauseMenu();

        // ══════════════════════════════════════════════════════════════════
        // 无痕重启（用户要求）：旧局不得写入 run 历史、不得中断连胜、不记成就。
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
            // 必须当场说清楚（日志 + 屏幕提示），让反馈里能一眼看到根因。
            Entry.Logger?.Error("[QuickRestart] 无法关闭 ShouldSave：本次重启的无痕保护不会生效（旧局可能写入历史/断连胜）");
            ModToast.Show("警告：无痕保护未生效，旧局可能计入战绩");
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
                // ④ 直接路径：FadeOut + CleanUp 后直接开新局 —— 跳过主菜单资源预载与场景创建。
                //    CleanUp 幂等（State==null 直接 return）、从不调用 OnEnded（无痕靠的就是不走它）、自带 ShouldSave=false。
                //    任何异常 → 落回下方旧路径（ReturnToMainMenu 全流程），玩家不会卡死。
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

            // ③ 等帧优化：新场景已在 StartRun 内 SetCurrentScene 并完成 EnterAct(0)，2 帧确认即可
            await WaitFrames(2);
            Entry.Logger?.Info($"[QuickRestart] {tag}完成：旧局未写入历史/未中断连胜（无痕生效={suppressed}） · 总耗时 {(long)Time.GetTicksMsec() - t0}ms（{(direct ? "直接" : "主菜单")}路径）");
        }
        catch (Exception ex)
        {
            // 保护：恢复动作绝不能抛异常 —— 上一版在这里直接写 ShouldSave（运行时 MethodAccessException），
            // 二次异常覆盖了原始异常，导致"黑屏但看不到根因"。
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

    public static async Task RetryCombatAsync()
    {
        // ★ 与其它入口同一道模式守卫：这里必须自己判，"联机下不注入按钮"只挡住了注入点、
        //   挡不住任何一条旁路调用（本方法就是由选牌界面按钮回调进来的）。
        if (IsBlockedByMode("重新挑战"))
            return;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            Entry.Logger?.Warn("[QuickRestart] 已有重启流程进行中，忽略重新挑战");
            return;
        }
        RestartOverlay.Show();
        try
        {
            Entry.Logger?.Info("[QuickRestart] 重新挑战当前战斗");
            await RestartRoomAsync();
        }
        catch (Exception ex)
        {
            // 补齐兜底：本方法由按钮回调 fire-and-forget 调用，异常若逃逸会成为未观察任务异常
            Entry.Logger?.Error($"[QuickRestart] 重新挑战失败: {ex}");
        }
        finally
        {
            RestartOverlay.Hide();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// 刀下留人「回到地图重新挑战」：回滚到**选路前**（进入本房间之前 —— 该地图坐标尚未被标记访问），
    /// 最终停在地图屏：血量 / 牌组 / 金币 / RNG 全部回退到进房前，玩家可重新选路，
    /// 也可以重新进入刚才那个节点再战。
    /// <para>与"重启房间"的区别：不自动重进房间（所以要额外退回地图屏）。</para>
    /// </summary>
    public static async Task RewindToMapAsync()
    {
        // ★ 联机拒绝；每日挑战局**放行**（allowDaily）—— 这是原地回滚，不改种子、不重开局，
        //   且每日局的"刀下留人"要保留这个出路（用户决定）。联机下两个出路都不给，见 NetGuard。
        if (IsBlockedByMode("回到地图", allowDaily: true))
            return;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            Entry.Logger?.Warn("[QuickRestart] 已有重启流程进行中，忽略回到地图");
            return;
        }
        RestartOverlay.Show();
        try
        {
            await RewindToMapCoreAsync();
        }
        finally
        {
            RestartOverlay.Hide();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static async Task RewindToMapCoreAsync()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 无法获取 RunState，跳过回到地图");
                return;
            }

            var snap = RoomEntryTracker.PreMapPointSnapshot;
            if (snap == null
                || !RoomEntryTracker.IsSnapshotForCurrentRun(snap, RoomEntryTracker.PreMapPointRunStart, "回到地图"))
            {
                // 兜底（快照缺失或属于上一局；如本房间不是从地图节点进入、补丁未生效）：沿用"重启房间"
                Entry.Logger?.Warn("[QuickRestart] 无可用选路前快照（缺失或属于上一局），回退为重启房间（回滚到进房前并自动重进）");
                await RestartRoomAsync();
                return;
            }

            ClosePauseMenu();

            // 刀下留人场景：拦截时调用了 CombatManager.Pause()，先解除（否则读档流程会卡住）
            EnsureCombatUnpaused();

            bool prevShouldSave = RunManager.Instance.ShouldSave;
            bool suppressed = TrySetShouldSave(false);
            bool prevSuspended = RoomEntryTracker.Suspended;
            RoomEntryTracker.Suspended = true;   // 读档与退回地图期间不记录新快照
            try
            {
                Entry.Logger?.Info("[QuickRestart] 回到地图：回滚到选路前（进入本房间之前）");
                long t0 = (long)Time.GetTicksMsec();
                bool direct = false;

                if (SettingsManager.Current.FastRestartSkipMenu)
                {
                    // ④ 直接路径：FadeOut + CleanUp 后直接读档，跳过主菜单；异常自动回退旧路径。
                    try
                    {
                        // 反序列化与淡出并行（同"重启房间"）
                        var fadeTask = NGame.Instance!.Transition.FadeOut(DirectFadeSec);
                        RunState runStateDirect;
                        try
                        {
                            runStateDirect = RunState.FromSerializable(snap);
                        }
                        finally
                        {
                            await AwaitFadeSafeAsync(fadeTask, "回到地图");
                        }
                        LogStep("回到地图", t0, "FadeOut");
                        RunManager.Instance.CleanUp();
                        LogStep("回到地图", t0, "CleanUp");

                        await RunManager.Instance.SetUpSavedSingleplayer(runStateDirect, snap);
                        await RestoreRunSceneAsync(runStateDirect);
                        await RunManager.Instance.EnterRoom(new MapRoom());   // ★ 停在地图屏（不再"自动重进上一格"）
                        await FadeInSafeAsync("回到地图");
                        direct = true;
                        LogStep("回到地图", t0, "读档");
                    }
                    catch (Exception ex)
                    {
                        Entry.Logger?.Error($"[QuickRestart] 直接路径失败（回到地图），回退主菜单路径: {ex.Message}");
                    }
                }

                if (!direct)
                {
                    await NGame.Instance!.ReturnToMainMenu();
                    await WaitFrames(5);

                    var runState = RunState.FromSerializable(snap);
                    await RunManager.Instance.SetUpSavedSingleplayer(runState, snap);
                    await RestoreRunSceneAsync(runState);
                    await RunManager.Instance.EnterRoom(new MapRoom());
                }

                // ③ 等帧优化：LoadRun 已完成场景切换，2 帧确认即可
                await WaitFrames(2);

                // 兜底：落点异常（理论上已在 MapRoom）时强制退回地图屏 ——
                // 本节点在快照中尚未被标记访问，因此地图上可以重新点击它。
                var restoredRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
                if (restoredRoom is not MapRoom)
                {
                    Entry.Logger?.Warn($"[QuickRestart] 回到地图：落点异常（{restoredRoom?.RoomType.ToString() ?? "无"}），强制退回地图屏");
                    await RunManager.Instance.EnterRoom(new MapRoom());
                    await WaitFrames(2);
                }

                Entry.Logger?.Info($"[QuickRestart] 回到地图完成：可重新选路，或重新进入本节点再战（状态已回退到进房前）· 总耗时 {(long)Time.GetTicksMsec() - t0}ms（{(direct ? "直接" : "主菜单")}路径）");
            }
            catch (Exception ex)
            {
                if (suppressed)
                    TrySetShouldSave(prevShouldSave);
                Entry.Logger?.Error($"[QuickRestart] 回到地图失败: {ex}");
            }
            finally
            {
                RoomEntryTracker.Suspended = prevSuspended;
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 回到地图异常: {ex}");
        }
    }

    /// <summary>
    /// 解除战斗暂停。刀下留人时会调用 CombatManager.Pause() 冻结战斗（弹窗选择期间），
    /// 解除前必须确保玩家已脱离 HP=0 状态（否则 Unpause 后死亡检测立即再次触发）。
    /// 非暂停时为无操作，可安全重复调用。
    /// <para>供 <see cref="DeathInterceptPatch"/> 共用：弹窗"未被选择就被关闭"时也必须解除暂停，
    /// 否则战斗永久冻结（玩家已复活 → 不会再产生死亡调用 → 没有第二次机会解除）。</para>
    /// </summary>
    internal static void EnsureCombatUnpaused()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm != null && cm.IsPaused)
            {
                cm.Unpause();
                Entry.Logger?.Info("[QuickRestart] 已解除战斗暂停（Unpause）");
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 解除战斗暂停失败: {ex.Message}");
        }
    }

    private static void ClosePauseMenu()
    {
        try
        {
            var capstone = MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance;
            if (capstone != null)
            {
                capstone.Close();
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 关闭暂停菜单失败: {ex.Message}");
        }
    }
}