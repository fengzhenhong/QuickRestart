using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.addons.mega_text;

namespace QuickRestart;

/// <summary>
/// 快捷键设置面板：暂停菜单"快捷键设置"按钮打开。
/// 点击某一行进入等待状态，按任意键绑定（Esc 取消），立即写入 settings.json 并生效。
/// 面板打开期间按键由 RunInputPatch 转发到 <see cref="HandleKey"/>。
/// </summary>
internal static class KeyBindPanel
{
    private enum BindTarget
    {
        Room,
        Floor,
        Run,
        NewRun
    }

    private static readonly Dictionary<BindTarget, NPauseMenuButton> Rows = new();
    private static Control? _centerRoot;
    private static BindTarget? _awaiting;

    public static bool IsOpen => _centerRoot != null && GodotObject.IsInstanceValid(_centerRoot);

    /// <summary>是否正在等待玩家按下新按键。</summary>
    public static bool IsAwaiting => _awaiting != null;

    public static void ToggleFrom(NPauseMenu menu)
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        try
        {
            var buttonContainer = menu.GetNodeOrNull<Control>("%ButtonContainer");
            var template = buttonContainer?.GetNodeOrNull<NPauseMenuButton>("GiveUp");
            if (template == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 快捷键面板：找不到模板按钮");
                return;
            }

            // 全屏容器负责居中；本身不接收鼠标（面板自身接收）
            var center = new CenterContainer
            {
                Name = "QR_KeyBindCenter",
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            center.SetAnchorsPreset(Control.LayoutPreset.FullRect);

            var panel = new PanelContainer
            {
                MouseFilter = Control.MouseFilterEnum.Stop
            };
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.07f, 0.07f, 0.1f, 0.97f),
                BorderColor = new Color(0.5f, 0.45f, 0.3f, 0.9f),
                BorderWidthTop = 2,
                BorderWidthBottom = 2,
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                CornerRadiusTopLeft = 10,
                CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10,
                CornerRadiusBottomRight = 10,
                ContentMarginLeft = 22f,
                ContentMarginRight = 22f,
                ContentMarginTop = 16f,
                ContentMarginBottom = 16f
            });

            var vbox = new VBoxContainer { Name = "Rows" };
            vbox.AddThemeConstantOverride("separation", 8);
            panel.AddChild(vbox);

            var title = new MegaLabel { Text = "快捷键设置" };
            title.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(title);

            var hint = new MegaLabel { Text = "点击一行，再按下新按键（Esc 取消）" };
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Modulate = new Color(0.75f, 0.78f, 0.85f);
            vbox.AddChild(hint);

            Rows.Clear();
            AddRow(vbox, template, BindTarget.Room, "重启房间");
            AddRow(vbox, template, BindTarget.Floor, "重启本层");
            AddRow(vbox, template, BindTarget.Run, "重启本局");
            AddRow(vbox, template, BindTarget.NewRun, "重启新局");

            var closeBtn = template.Duplicate() as NPauseMenuButton;
            if (closeBtn != null)
            {
                closeBtn.Name = "Close";
                SetLabel(closeBtn, "关闭");
                closeBtn.Visible = true;
                closeBtn.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Close()));
                vbox.AddChild(closeBtn);
            }

            center.AddChild(panel);
            menu.AddChild(center);

            _centerRoot = center;
            _awaiting = null;
            RefreshRows();

            Entry.Logger?.Info("[QuickRestart] 快捷键设置面板已打开");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 打开快捷键设置面板失败: {ex}");
            Close();
        }
    }

    public static void Close()
    {
        _awaiting = null;
        Rows.Clear();

        if (_centerRoot != null && GodotObject.IsInstanceValid(_centerRoot))
        {
            _centerRoot.QueueFree();
        }
        _centerRoot = null;
    }

    /// <summary>
    /// 面板打开时由 RunInputPatch 转发按键。
    /// 返回 true = 按键已被消费（不再走重启快捷键逻辑）。
    /// </summary>
    public static bool HandleKey(InputEventKey key)
    {
        if (_awaiting is BindTarget target)
        {
            _awaiting = null;

            if (key.Keycode == Key.Escape)
            {
                RefreshRows();
                return true;
            }

            SetKey(target, KeyNameOf(key));
            SettingsManager.Save();
            Entry.Logger?.Info($"[QuickRestart] 快捷键已修改: {LabelOf(target)} = {GetKey(target)}");
            RefreshRows();
            return true;
        }

        // 非捕获状态：吞掉会触发重启的按键，避免面板打开时误触发
        return MatchesAnyRestartKey(key);
    }

    private static void AddRow(Container parent, NPauseMenuButton template, BindTarget target, string label)
    {
        var btn = template.Duplicate() as NPauseMenuButton;
        if (btn == null)
        {
            return;
        }

        btn.Name = "Bind_" + target;
        btn.Visible = true;
        var captured = target;
        btn.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => BeginCapture(captured)));
        parent.AddChild(btn);

        Rows[target] = btn;
    }

    private static void BeginCapture(BindTarget target)
    {
        _awaiting = target;
        RefreshRows();
    }

    private static void RefreshRows()
    {
        foreach (var (target, btn) in Rows)
        {
            string text = _awaiting == target
                ? $"{LabelOf(target)}    按下新按键…（Esc 取消）"
                : $"{LabelOf(target)}    {GetKey(target)}";
            SetLabel(btn, text);
        }
    }

    private static string LabelOf(BindTarget target) => target switch
    {
        BindTarget.Room => "重启房间",
        BindTarget.Floor => "重启本层",
        BindTarget.Run => "重启本局",
        BindTarget.NewRun => "重启新局",
        _ => target.ToString()
    };

    private static string GetKey(BindTarget target) => target switch
    {
        BindTarget.Room => SettingsManager.Current.RestartRoomKey,
        BindTarget.Floor => SettingsManager.Current.RestartFloorKey,
        BindTarget.Run => SettingsManager.Current.RestartRunKey,
        BindTarget.NewRun => SettingsManager.Current.NewRunKey,
        _ => string.Empty
    };

    private static void SetKey(BindTarget target, string key)
    {
        switch (target)
        {
            case BindTarget.Room:
                SettingsManager.Current.RestartRoomKey = key;
                break;
            case BindTarget.Floor:
                SettingsManager.Current.RestartFloorKey = key;
                break;
            case BindTarget.Run:
                SettingsManager.Current.RestartRunKey = key;
                break;
            case BindTarget.NewRun:
                SettingsManager.Current.NewRunKey = key;
                break;
        }
    }

    private static bool MatchesAnyRestartKey(InputEventKey key)
    {
        var s = SettingsManager.Current;
        return IsKeyMatch(key, s.RestartRoomKey)
            || IsKeyMatch(key, s.RestartFloorKey)
            || IsKeyMatch(key, s.RestartRunKey)
            || IsKeyMatch(key, s.NewRunKey);
    }

    /// <summary>按键名匹配：单字符（A-Z/0-9）用 ASCII 值比较；其余用 Godot Key 枚举名（如 Space/F1）。</summary>
    internal static bool IsKeyMatch(InputEventKey key, string? keyName)
    {
        if (string.IsNullOrEmpty(keyName))
        {
            return false;
        }

        if (keyName.Length == 1)
        {
            int code = (int)key.Keycode;
            char c = keyName.ToUpperInvariant()[0];
            if (c >= 'A' && c <= 'Z')
            {
                return code == c - 'A' + 65;
            }
            if (c >= '0' && c <= '9')
            {
                return code == c - '0' + 48;
            }
        }

        return string.Equals(key.Keycode.ToString(), keyName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按键 → 配置字符串：字母数字存单字符，其余存 Godot 枚举名。</summary>
    internal static string KeyNameOf(InputEventKey key)
    {
        int code = (int)key.Keycode;
        if (code >= 'A' && code <= 'Z')
        {
            return ((char)code).ToString();
        }
        if (code >= '0' && code <= '9')
        {
            return ((char)code).ToString();
        }
        return key.Keycode.ToString();
    }

    private static void SetLabel(NPauseMenuButton btn, string label)
    {
        if (!GodotObject.IsInstanceValid(btn))
        {
            return;
        }

        var megaLabel = btn.GetNodeOrNull<MegaLabel>("Label");
        if (megaLabel != null)
        {
            megaLabel.Text = label;
        }
    }
}
