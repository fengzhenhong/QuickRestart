using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.addons.mega_text;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

internal sealed class CardRewardScreenReadyPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_card_reward_ready";
    public static string Description => "选牌界面就绪后注入重新挑战按钮";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCardRewardSelectionScreen), "_Ready", Type.EmptyTypes)];

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(NCardRewardSelectionScreen __instance)
    {
        if (!Entry.Enabled) return;
        if (!SettingsManager.Current.EnablePostCombatRetry) return;
        try
        {
            RetryButtonInjector.Inject(__instance);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 注入重新挑战按钮失败: {ex}");
        }
    }
}

internal sealed class CardRewardScreenExitPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_card_reward_exit";
    public static string Description => "选牌界面关闭时清理按钮引用";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NCardRewardSelectionScreen), "_ExitTree", Type.EmptyTypes)];

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(NCardRewardSelectionScreen __instance)
    {
        RetryButtonInjector.Cleanup();
    }
}

internal static class RetryButtonInjector
{
    private static Button? _retryButton;

    public static void Inject(NCardRewardSelectionScreen screen)
    {
        var parent = screen.GetParent();
        if (parent == null) return;

        // 防御：界面复用导致 _Ready 重复触发时，先清理旧按钮避免叠加
        var existing = parent.GetNodeOrNull<Button>("RetryCombatButton");
        if (existing != null && GodotObject.IsInstanceValid(existing))
        {
            existing.QueueFree();
        }

        _retryButton = new Button
        {
            Name = "RetryCombatButton",
            Text = "重新挑战",
            // Always：战斗结算/暂停状态下按钮也要可点击。
            // ⚠️ 勿用整型字面量：Godot 枚举实际为 Inherit=0/Pausable=1/WhenPaused=2/Always=3/Disabled=4，
            //    旧代码写 (Node.ProcessModeEnum)4 = Disabled —— Control 收不到任何输入，按钮永远点不动。
            ProcessMode = Node.ProcessModeEnum.Always
        };

        _retryButton.Pressed += OnRetryPressed;

        var container = screen.GetNodeOrNull<Control>("..");
        if (container != null)
        {
            container.AddChild(_retryButton);
        }
        else
        {
            parent.AddChild(_retryButton);
        }

        Entry.Logger?.Info("[QuickRestart] 重新挑战按钮已注入选牌界面");
    }

    private static void OnRetryPressed()
    {
        Entry.Logger?.Info("[QuickRestart] 点击重新挑战");
        ConfirmPopupHelper.ShowConfirm("重新挑战", "确认放弃战利品，重新挑战本场战斗？",
            () => _ = RestartService.RetryCombatAsync());
    }

    public static void Cleanup()
    {
        if (_retryButton != null && GodotObject.IsInstanceValid(_retryButton))
        {
            _retryButton.QueueFree();
        }
        _retryButton = null;
    }
}