using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace QuickRestart;

internal sealed class PauseMenuReadyPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_pause_menu_ready";
    public static string Description => "暂停菜单就绪后注入四类重启按钮";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NPauseMenu), "_Ready", Type.EmptyTypes)];

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(NPauseMenu __instance)
    {
        if (!Entry.Enabled) return;
        // ★ 联机对局不注入按钮：联机下所有回滚/重启功能均禁用（详见 NetGuard），
        //   按钮不出现比"点了才提示不可用"更直观（点击处的守卫是第二道防线）。
        if (NetGuard.IsMultiplayer()) return;
        try
        {
            PauseMenuButtonInjector.InjectButtons(__instance);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 注入暂停菜单按钮失败: {ex}");
        }
    }
}

internal sealed class PauseMenuExitPatch : IPatchMethod
{
    public static string PatchId => "quick_restart_pause_menu_exit";
    public static string Description => "暂停菜单关闭时清理按钮引用";

    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(NPauseMenu), "_ExitTree", Type.EmptyTypes)];

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(NPauseMenu __instance)
    {
        PauseMenuButtonInjector.Cleanup();
    }
}

internal static class PauseMenuButtonInjector
{
    private static NPauseMenu? _currentMenu;
    private static NPauseMenuButton? _keyBindBtn;
    private static NPauseMenuButton? _restartRoomBtn;
    private static NPauseMenuButton? _restartFloorBtn;
    private static NPauseMenuButton? _restartRunBtn;
    private static NPauseMenuButton? _newRunBtn;

    public static void InjectButtons(NPauseMenu menu)
    {
        _currentMenu = menu;

        var buttonContainer = menu.GetNode<Control>("%ButtonContainer");
        if (buttonContainer == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 找不到 ButtonContainer");
            return;
        }

        var templateBtn = buttonContainer.GetNodeOrNull<NPauseMenuButton>("GiveUp");
        if (templateBtn == null)
        {
            Entry.Logger?.Warn("[QuickRestart] 找不到 GiveUp 模板按钮");
            return;
        }

        _restartRoomBtn = EnsureButton(templateBtn, "RestartRoom", "重启房间", buttonContainer, OnRestartRoomPressed);
        _restartFloorBtn = EnsureButton(templateBtn, "RestartFloor", GetFloorLabel(), buttonContainer, OnRestartFloorPressed);
        _restartRunBtn = EnsureButton(templateBtn, "RestartRun", "重启本局", buttonContainer, OnRestartRunPressed);
        _newRunBtn = EnsureButton(templateBtn, "NewRun", "重启新局", buttonContainer, OnNewRunPressed);
        _keyBindBtn = EnsureButton(templateBtn, "KeyBinds", "快捷键设置", buttonContainer, OnKeyBindsPressed);

        Entry.Logger?.Info("[QuickRestart] 暂停菜单重启按钮与快捷键设置已注入");
    }

    /// <summary>"重启本层"按钮文本：实时带当前幕数（如"重启本层（第 2 幕）"）。</summary>
    private static string GetFloorLabel()
    {
        int act = TryGetCurrentActIndex();
        return act >= 0 ? $"重启本层（第 {act + 1} 幕）" : "重启本层";
    }

    /// <summary>当前幕索引（0 基）；非 run 状态或失败返回 -1。</summary>
    private static int TryGetCurrentActIndex()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            return state?.CurrentActIndex ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 取出（或创建）注入按钮：每次打开暂停菜单都会 _Ready → 重新注入，
    /// 已有同名按钮时只刷新文本，避免重复 AddChild / 重复连接信号。
    /// </summary>
    private static NPauseMenuButton EnsureButton(NPauseMenuButton template, string name, string label, Control container, Action<NButton> handler)
    {
        var existing = container.GetNodeOrNull<NPauseMenuButton>(name);
        if (existing != null)
        {
            SetLabel(existing, label);
            return existing;
        }

        var btn = CreateButton(template, name, label, container);
        btn.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(handler));
        return btn;
    }

    private static NPauseMenuButton CreateButton(NPauseMenuButton template, string name, string label, Control container)
    {
        var btn = template.Duplicate() as NPauseMenuButton;
        if (btn == null)
        {
            Entry.Logger?.Warn($"[QuickRestart] 克隆按钮失败 name={name}");
            return template;
        }

        btn.Name = name;
        btn.Visible = true;

        SetLabel(btn, label);

        container.AddChild(btn);

        return btn;
    }

    private static void SetLabel(NPauseMenuButton btn, string label)
    {
        var megaLabel = btn.GetNodeOrNull<MegaLabel>("Label");
        if (megaLabel != null)
        {
            megaLabel.Text = label;
        }
    }

    private static void OnRestartRoomPressed(NButton btn)
    {
        Entry.Logger?.Info("[QuickRestart] 点击重启房间");
        // ★ 免确认直接执行（用户要求）：重启房间在练习场景高频使用
        _ = RestartService.ExecuteRestart(RestartService.RestartType.RestartRoom);
    }

    private static void OnRestartFloorPressed(NButton btn)
    {
        Entry.Logger?.Info("[QuickRestart] 点击重启本层");
        int act = TryGetCurrentActIndex();
        string text = act >= 0
            ? $"确认回到第 {act + 1} 幕起点重新选路？\n（本幕获得的金币/卡牌/遗物/药水/血量将全部回退）"
            : "确认回到当前幕起点重新选路？";
        ConfirmPopupHelper.ShowConfirm("重启本层", text,
            () => _ = RestartService.ExecuteRestart(RestartService.RestartType.RestartFloor));
    }

    private static void OnRestartRunPressed(NButton btn)
    {
        Entry.Logger?.Info("[QuickRestart] 点击重启本局");
        ConfirmPopupHelper.ShowConfirm("重启本局", "确认同角色同种子重新开局？",
            () => _ = RestartService.ExecuteRestart(RestartService.RestartType.RestartRun));
    }

    private static void OnNewRunPressed(NButton btn)
    {
        Entry.Logger?.Info("[QuickRestart] 点击重启新局");
        ConfirmPopupHelper.ShowConfirm("重启新局", "确认同角色新随机种子全新开局？",
            () => _ = RestartService.ExecuteRestart(RestartService.RestartType.NewRun));
    }

    private static void OnKeyBindsPressed(NButton btn)
    {
        Entry.Logger?.Info("[QuickRestart] 点击快捷键设置");
        if (_currentMenu == null || !GodotObject.IsInstanceValid(_currentMenu))
        {
            Entry.Logger?.Warn("[QuickRestart] 快捷键面板：菜单引用缺失或已失效");
            return;
        }
        KeyBindPanel.ToggleFrom(_currentMenu);
    }

    public static void Cleanup()
    {
        KeyBindPanel.Close();
        _currentMenu = null;
        _keyBindBtn = null;
        _restartRoomBtn = null;
        _restartFloorBtn = null;
        _restartRunBtn = null;
        _newRunBtn = null;
    }
}