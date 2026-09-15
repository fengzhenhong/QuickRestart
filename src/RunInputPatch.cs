using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
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

        // 快捷键设置面板：捕获/拦截按键（面板打开时由它决定是否消费）
        if (KeyBindPanel.IsOpen && KeyBindPanel.HandleKey(key)) return;

        float now = Time.GetTicksMsec() / 1000f;
        if (now - _lastTriggerTime < TriggerCooldown) return;

        var settings = SettingsManager.Current;
        string? action = null;
        RestartService.RestartType? restartType = null;

        if (KeyBindPanel.IsKeyMatch(key, settings.RestartRoomKey))
        {
            action = "重启房间";
            restartType = RestartService.RestartType.RestartRoom;
        }
        else if (KeyBindPanel.IsKeyMatch(key, settings.RestartFloorKey))
        {
            action = "重启本层";
            restartType = RestartService.RestartType.RestartFloor;
        }
        else if (KeyBindPanel.IsKeyMatch(key, settings.RestartRunKey))
        {
            action = "重启本局";
            restartType = RestartService.RestartType.RestartRun;
        }
        else if (KeyBindPanel.IsKeyMatch(key, settings.NewRunKey))
        {
            action = "重启新局";
            restartType = RestartService.RestartType.NewRun;
        }

        if (action == null || restartType == null) return;

        // 不再要求暂停菜单打开 —— 战斗中直接可用；仍要求存在进行中的 run。
        if (RunManager.Instance.DebugOnlyGetState() == null) return;

        _lastTriggerTime = now;
        Entry.Logger?.Info($"[QuickRestart] 快捷键触发 {action}");

        var type = restartType.Value;

        // ★ 重启房间免确认（用户要求）：练习场景高频连按，直接执行；
        //   防误触改由 0.5s 冷却承担。重启本层/本局/新局仍弹确认框。
        if (type == RestartService.RestartType.RestartRoom)
        {
            _ = RestartService.ExecuteRestart(type);
            return;
        }

        ConfirmPopupHelper.ShowConfirm(action, $"确认{action}？",
            () => _ = RestartService.ExecuteRestart(type));
    }

}