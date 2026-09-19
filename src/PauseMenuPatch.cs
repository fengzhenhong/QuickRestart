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
        // ★ 联机对局 / 每日挑战局不注入按钮：这两种模式下所有回滚/重启功能均禁用（详见 NetGuard），
        //   按钮不出现比"点了才提示不可用"更直观（执行点的 IsBlockedByMode 是第二道防线）。
        if (NetGuard.IsMultiplayer() || NetGuard.IsDailyRun()) return;
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
    /// <summary>注入用按钮名（同时是"已注入"的判据：重复 _Ready 时按名字复用，不叠加）。</summary>
    private static readonly (string Name, string Label, Action<NButton> Handler)[] Buttons =
    [
        ("RestartRoom", "重启房间", OnRestartRoomPressed),
        ("RestartFloor", "重启本层", OnRestartFloorPressed),
        ("RestartRun", "重启本局", OnRestartRunPressed),
        ("NewRun", "重启新局", OnNewRunPressed),
        ("KeyBinds", "快捷键设置", OnKeyBindsPressed),
    ];

    private static NPauseMenu? _currentMenu;

    public static void InjectButtons(NPauseMenu menu)
    {
        _currentMenu = menu;

        // 用 GetNodeOrNull：GetNode 找不到节点是**抛异常**而不是返回 null，那样下面的
        // "ButtonContainer 缺失"降级分支永远走不到（异常会被 Postfix 的 catch 当 Error 记掉）。
        var buttonContainer = menu.GetNodeOrNull<Control>("%ButtonContainer");
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

        int ok = 0;
        foreach (var (name, label, handler) in Buttons)
        {
            // "重启本层"按钮文本实时带当前幕数（如"重启本层（第 2 幕）"）
            string text = name == "RestartFloor" ? GetFloorLabel() : label;
            if (EnsureButton(templateBtn, name, text, buttonContainer, handler) != null)
                ok++;
        }

        Entry.Logger?.Info($"[QuickRestart] 暂停菜单重启按钮与快捷键设置已注入（{ok}/{Buttons.Length}）");
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
    /// 返回 null = 克隆失败（调用方跳过该按钮）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 克隆失败时**绝不能**退化成"用模板按钮顶上"：那是游戏自己的 GiveUp（放弃游戏），
    /// 把重启回调连到它上面 = 玩家点"放弃游戏"却执行了重启，而且每次开菜单还会多连一条。
    /// </remarks>
    private static NPauseMenuButton? EnsureButton(NPauseMenuButton template, string name, string label,
        Control container, Action<NButton> handler)
    {
        var existing = container.GetNodeOrNull<NPauseMenuButton>(name);
        if (existing != null)
        {
            SetLabel(existing, label);
            return existing;
        }

        var btn = CreateButton(template, name, label, container);
        if (btn == null)
        {
            Entry.Logger?.Warn($"[QuickRestart] 按钮注入失败，跳过 name={name}");
            return null;
        }
        btn.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(handler));
        return btn;
    }

    private static NPauseMenuButton? CreateButton(NPauseMenuButton template, string name, string label, Control container)
    {
        if (template.Duplicate() as NPauseMenuButton is not NPauseMenuButton btn)
        {
            Entry.Logger?.Warn($"[QuickRestart] 克隆按钮失败 name={name}");
            return null;
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
        // 注入的按钮是本菜单节点的子节点，菜单被释放时一起释放；这里只需放掉我方引用
        KeyBindPanel.Close();
        _currentMenu = null;
    }
}