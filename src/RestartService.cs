using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

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

    public static async Task ExecuteRestart(RestartType type)
    {
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

        // 重建蓝图：必须在回滚前提取（旧 run 的对象引用在清理后不可靠）
        RoomType roomType = room.RoomType;
        int actIndex = state.CurrentActIndex;
        EncounterModel? encounter = null;
        EventModel? evt = null;
        if (room is CombatRoom combatRoom)
        {
            encounter = ModelDb.GetByIdOrNull<EncounterModel>(combatRoom.Encounter.Id);
        }
        else if (room is EventRoom eventRoom)
        {
            evt = ModelDb.GetByIdOrNull<EventModel>(eventRoom.CanonicalEvent.Id);
        }

        var preSnap = RoomEntryTracker.PreRoomSnapshot;
        if (preSnap == null)
        {
            // 兜底（如进房早于补丁生效）：新建房间重进 —— 牌序会重新随机
            Entry.Logger?.Warn("[QuickRestart] 无进房前快照，回退为新建房间重进（牌序会重新随机）");
            RoomEntryTracker.RestorePreRoomState();
            await RunManager.Instance.EnterRoom(RebuildRoomInstance(room, state));
            Entry.Logger?.Info("[QuickRestart] 重启房间完成（回退路径）");
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

            await NGame.Instance!.ReturnToMainMenu();
            await WaitFrames(5);

            var runState = RunState.FromSerializable(preSnap);
            await RunManager.Instance.SetUpSavedSingleplayer(runState, preSnap);
            await NGame.Instance.LoadRun(runState, null);
            await WaitFrames(5);

            // ══════════════════════════════════════════════════════════════════
            // ★ 修复：重启后商店物品/怪物/事件"刷新"（同一房间被生成了两次）
            //   NGame.LoadRun 内部会调用 RunManager.LoadIntoLatestMapCoord —— 它依据
            //   State.VisitedMapCoords 的最后一个坐标，自动把玩家重新送回"本房间"。
            //   而进房前快照是在 RunManager.EnterRoom 之前捕获的，此时
            //   AddVisitedMapCoord 已经执行过（EnterMapCoord：先记录坐标，再 EnterRoom），
            //   所以快照里最后一个已访问坐标就是本房间 → 读档流程必然自动重进本房间，
            //   且用的是快照里的 RNG（与首次进房完全一致）。
            //   若此时再手动 EnterRoom，房间内容会被生成第二次 —— 第二次的 RNG
            //   已被第一次消耗（如 PlayerRng.Shops/Rewards 的计数器），于是商店库存
            //   凭空刷新（日志实证：同一房间被 Enter 两次，两次 "Card rarity: Rolled"
            //   数值不同）。故：官方读档已精确重进本房间时，不再手动进房。
            // ══════════════════════════════════════════════════════════════════
            var restoredRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
            if (restoredRoom != null && restoredRoom.RoomType == roomType)
            {
                Entry.Logger?.Info($"[QuickRestart] 重启房间完成：读档已自动重进本房间（{roomType}），跳过重复进房（内容与首次一致，商店不会刷新）");
                return;
            }

            Entry.Logger?.Info($"[QuickRestart] 读档未自动重进本房间（当前房间={restoredRoom?.RoomType.ToString() ?? "无"}），改为手动进入");
            var roomToEnter = BuildRoom(roomType, actIndex, encounter, evt, runState);
            if (roomToEnter == null)
            {
                Entry.Logger?.Warn($"[QuickRestart] 房间类型 {roomType} 暂不支持自动重进，已回滚到进房前（请手动进入）");
                return;
            }

            await RunManager.Instance.EnterRoom(roomToEnter);
            Entry.Logger?.Info("[QuickRestart] 重启房间完成：已回滚到进房前并重新进入（牌序与首次一致）");
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
    /// 按蓝图构造全新房间（回滚完成后调用，runState 为回滚出的新 RunState）。
    /// 路径与游戏 <c>RunManager.CreateRoom</c> 官方实现一致。
    /// </summary>
    private static AbstractRoom? BuildRoom(RoomType roomType, int actIndex, EncounterModel? encounter, EventModel? evt, RunState runState)
    {
        try
        {
            switch (roomType)
            {
                case RoomType.Monster:
                case RoomType.Elite:
                case RoomType.Boss:
                    // 规范遭遇克隆出全新 mutable（怪物重新生成，RunRng 重新设置）
                    return encounter != null ? new CombatRoom(encounter.ToMutable(), runState) : null;
                case RoomType.Treasure:
                    return new TreasureRoom(actIndex);
                case RoomType.Shop:
                    return new MerchantRoom();
                case RoomType.RestSite:
                    return new RestSiteRoom();
                case RoomType.Event:
                    return evt != null ? new EventRoom(evt) : null;
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 重建房间失败: {ex.Message}");
            return null;
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

        // 快照缺失/不属于当前幕（如刚读档进入幕中途）→ 回退旧行为：仅重进本幕（不回滚物品）
        if (snapshot == null || ActSnapshotStore.SnapshotActIndex != actIndex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 无第 {actIndex + 1} 幕起点快照，回退为仅重进本幕");
            ClosePauseMenu();
            await RunManager.Instance.EnterAct(actIndex);
            return;
        }

        Entry.Logger?.Info($"[QuickRestart] 重启本层：回滚到第 {actIndex + 1} 幕起点（本幕获得的金币/卡牌/遗物/药水/血量将全部回退）");
        ClosePauseMenu();

        bool prevShouldSave = RunManager.Instance.ShouldSave;
        bool suppressed = TrySetShouldSave(false);

        try
        {
            // 官方读档流程（与主菜单"继续游戏"同一条路径，反编译确认）：
            //   ReturnToMainMenu → CleanUp（内含 State=null）→
            //   RunState.FromSerializable 重建 → SetUpSavedSingleplayer（要求 State==null）→ NGame.LoadRun
            await NGame.Instance!.ReturnToMainMenu();
            await WaitFrames(5);

            var runState = RunState.FromSerializable(snapshot);
            await RunManager.Instance.SetUpSavedSingleplayer(runState, snapshot);
            await NGame.Instance.LoadRun(runState, null);

            Entry.Logger?.Info("[QuickRestart] 重启本层完成：已回滚到本幕起点");
        }
        catch (Exception ex)
        {
            if (suppressed)
                TrySetShouldSave(prevShouldSave);
            Entry.Logger?.Error($"[QuickRestart] 重启本层失败: {ex}");
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

        string seed = sameSeed
            ? state.Rng.StringSeed
            : SeedHelper.GetRandomSeed();

        Entry.Logger?.Info($"[QuickRestart] 重启本局 sameSeed={sameSeed} seed={seed} character={character.Id} ascension={ascension} acts={acts.Count} modifiers={modifiers.Count}");

        ClosePauseMenu();

        // ══════════════════════════════════════════════════════════════════
        // 无痕重启（用户要求）：旧局不得写入 run 历史、不得中断连胜、不记成就。
        // RunManager.OnEnded() 的全部写入（UpdateProgressWithRunData 连胜 /
        // CreateRunHistoryEntry 历史 / AchievementsHelper 成就 / 指标上传 / 删档）
        // 都包在 `if (ShouldSave)` 里 —— 重开期间把 ShouldSave 置 false，旧局即"无痕消失"。
        // 新局由 SetUpNewSingleplayer(state, shouldSave: true) 自动恢复为 true。
        // ══════════════════════════════════════════════════════════════════
        bool prevShouldSave = RunManager.Instance.ShouldSave;
        bool suppressed = TrySetShouldSave(false);
        Entry.Logger?.Info($"[QuickRestart] 无痕标记 ShouldSave=false → {suppressed}（prev={prevShouldSave}）");

        // 新局：旧局的幕快照作废（新局 Act 0 的地图屏就绪后会重新记录）
        ActSnapshotStore.Reset();

        try
        {
            await NGame.Instance!.ReturnToMainMenu();
            // 场景切换留帧：回主菜单 → 开新局不能在同一帧内连续执行（否则画面停在旧场景 = 黑屏）
            await WaitFrames(5);

            await NGame.Instance.StartNewSingleplayerRun(
                character, true, acts, modifiers, seed, gameMode, ascension);

            Entry.Logger?.Info("[QuickRestart] 重启本局完成：同种子从头开始，旧局未写入历史/未中断连胜");
        }
        catch (Exception ex)
        {
            // 保护：恢复动作绝不能抛异常 —— 上一版在这里直接写 ShouldSave（运行时 MethodAccessException），
            // 二次异常覆盖了原始异常，导致"黑屏但看不到根因"。
            if (suppressed)
                TrySetShouldSave(prevShouldSave);
            Entry.Logger?.Error($"[QuickRestart] 重启本局失败: {ex}");
        }
    }

    public static async Task RetryCombatAsync()
    {
        Entry.Logger?.Info("[QuickRestart] 重新挑战当前战斗");
        await RestartRoomAsync();
    }

    /// <summary>
    /// 刀下留人「回到地图重新挑战」：回滚到**选路前**（进入本房间之前 —— 该地图坐标尚未被标记访问），
    /// 最终停在地图屏：血量 / 牌组 / 金币 / RNG 全部回退到进房前，玩家可重新选路，
    /// 也可以重新进入刚才那个节点再战。
    /// <para>与"重启房间"的区别：不自动重进房间（所以要额外退回地图屏）。</para>
    /// </summary>
    public static async Task RewindToMapAsync()
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
            if (snap == null)
            {
                // 兜底（如本房间不是从地图节点进入、补丁未生效）：沿用"重启房间"
                Entry.Logger?.Warn("[QuickRestart] 无选路前快照，回退为重启房间（回滚到进房前并自动重进）");
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

                await NGame.Instance!.ReturnToMainMenu();
                await WaitFrames(5);

                var runState = RunState.FromSerializable(snap);
                await RunManager.Instance.SetUpSavedSingleplayer(runState, snap);
                await NGame.Instance.LoadRun(runState, null);
                await WaitFrames(5);

                // LoadRun 内部 LoadIntoLatestMapCoord 会把玩家送回"最后一个已访问坐标"的房间（上一节点）；
                // 这里再退回地图屏 —— 本节点在快照中尚未被标记访问，因此地图上可以重新点击它。
                var restoredRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
                if (restoredRoom is not MapRoom)
                {
                    await RunManager.Instance.EnterRoom(new MapRoom());
                    await WaitFrames(2);
                }

                Entry.Logger?.Info("[QuickRestart] 回到地图完成：可重新选路，或重新进入本节点再战（状态已回退到进房前）");
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
    /// </summary>
    private static void EnsureCombatUnpaused()
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