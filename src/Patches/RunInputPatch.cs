using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

internal sealed class RunInputPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_input_handler";
    public static string Description => "处理快速重启快捷键";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NGame), "_Input", new[] { typeof(InputEvent) })];

    private static float _lastTriggerTime;
    private const float TriggerCooldown = 0.5f;

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(InputEvent inputEvent)
    {
        try
        {
            HandleKeyInput(inputEvent);
        }
        catch (Exception ex)
        {
            // 兜底：补丁异常绝不能中断游戏自身的输入处理链
            Entry.Logger?.Warn($"[QuickRestart] 快捷键处理异常: {ex.Message}");
        }
    }

    private static void HandleKeyInput(InputEvent inputEvent)
    {
        if (!Entry.Enabled) return;
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key) return;

        // ★ 控制台打开时不响应（玩家在控制台里打字，如输入含 r/f/t/n 的命令会误触发重启）。
        //   游戏自身的输入处理（NHotkeyManager 等）统一用同一守卫；IsConsoleVisible 在控制台
        if (NDevConsole.IsConsoleVisible) return;

        // 快捷键设置面板：捕获/拦截按键（面板打开时由它决定是否消费）
        if (KeyBindPanel.IsOpen && KeyBindPanel.HandleKey(key)) return;

        float now = Time.GetTicksMsec() / 1000f;
        if (now - _lastTriggerTime < TriggerCooldown) return;

        var settings = SettingsManager.Current;
        string? action = null;
        RestartService.RestartType? restartType = null;

        if (KeyBindPanel.IsKeyMatch(key, settings.RestartRoomKey))
        {
            action = ModLoc.RestartRoom;
            restartType = RestartService.RestartType.RestartRoom;
        }
        else if (KeyBindPanel.IsKeyMatch(key, settings.RestartFloorKey))
        {
            action = ModLoc.RestartFloor;
            restartType = RestartService.RestartType.RestartFloor;
        }
        else if (KeyBindPanel.IsKeyMatch(key, settings.RestartRunKey))
        {
            action = ModLoc.RestartRun;
            restartType = RestartService.RestartType.RestartRun;
        }
        else if (KeyBindPanel.IsKeyMatch(key, settings.NewRunKey))
        {
            action = ModLoc.NewRun;
            restartType = RestartService.RestartType.NewRun;
        }

        if (action == null || restartType == null) return;

        // ★ 输入环境守卫：本补丁挂在 NGame._Input（比游戏的 hotkey 处理更早、且不经它的闸门），
        //   单键 + 无修饰符 + "重启房间免确认"，一旦误触发就是不可逆的状态回滚。
        //   与游戏自身的守卫对齐（NHotkeyManager._UnhandledInput 判焦点与控制台；
        //   设置页判 TextEdit/LineEdit 焦点；弹窗用 AddBlockingScreen 屏蔽全部 hotkey）。
        if (IsInputEnvBlocked(out string? reason))
        {
            Entry.Logger?.Info($"[QuickRestart] 快捷键 {action} 已忽略：{reason}");
            return;
        }

        // ★ 模式守卫（联机 / 每日挑战局一律拒绝，命中时内部已给出屏幕提示；详见 NetGuard 与 RestartService）
        if (RestartService.IsBlockedByMode(action)) return;

        // 不再要求暂停菜单打开 —— 战斗中直接可用；仍要求存在进行中的 run。
        if (RunManager.Instance.DebugOnlyGetState() == null) return;

        _lastTriggerTime = now;
        Entry.Logger?.Info($"[QuickRestart] 快捷键触发 {action}");

        var type = restartType.Value;

        // ★ 重启房间免确认：练习场景高频连按，直接执行；
        //   防误触改由 0.5s 冷却承担。重启本层/本局/新局仍弹确认框。
        if (type == RestartService.RestartType.RestartRoom)
        {
            _ = RestartService.ExecuteRestart(type);
            return;
        }

        ConfirmPopupHelper.ShowConfirm(action, ModLoc.ConfirmAction(action),
            () => _ = RestartService.ExecuteRestart(type));
    }

    /// <summary>
    /// 按键环境是否不该触发重启。命中时给出人话原因（写日志，便于"按了没反应"时定位）。
    /// </summary>
    private static bool IsInputEnvBlocked(out string? reason)
    {
        // 1) 窗口失焦：Alt-Tab 出去在别的程序里打字，Godot 仍可能把按键送进 _Input
        if (!NGame.IsGameFocusedWindow())
        {
            reason = ModLoc.ReasonWindowNotFocused;
            return true;
        }

        // 2) 文本框正在吃键盘（输入种子/昵称等）：字母键属于内容，不是快捷键
        Control? focus = NGame.Instance?.GetWindow()?.GuiGetFocusOwner();
        if (focus is TextEdit or LineEdit)
        {
            reason = ModLoc.ReasonTextFocus;
            return true;
        }

        // 3) 有模态弹窗开着（含我方"刀下留人"与确认框）：此时触发重启会与弹窗选择互相打架
        if (NModalContainer.Instance?.OpenModal != null)
        {
            reason = ModLoc.ReasonModalOpen;
            return true;
        }

        reason = null;
        return false;
    }
}