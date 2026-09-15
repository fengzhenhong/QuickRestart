using Godot;

namespace QuickRestart;

/// <summary>
/// 轻量屏幕提示（居中偏下一行短句，淡入 → 停留 → 淡出）。
///
/// <para>用于"按下按键但什么都没发生"的场景必须给玩家反馈的情况（否则会被当成卡死/失灵），
/// 例如联机模式下按重启快捷键：应明确提示"联机模式下不可用"，而不是静默无反应。</para>
///
/// <para>实现：挂在 <see cref="SceneTree.Root"/> 下的 CanvasLayer（layer=99，不抢鼠标，
/// ProcessMode=Always 保证暂停/失焦也可见）。系统字体雅黑（Godot 默认字体无 CJK 字形）。
/// 任何失败都静默降级（无提示不影响功能）。</para>
/// </summary>
internal static class ModToast
{
    private const float FadeInSec = 0.12f;
    private const float HoldSec = 1.5f;
    private const float FadeOutSec = 0.4f;

    private static CanvasLayer? _layer;
    private static Label? _label;
    private static Tween? _tween;

    /// <summary>显示一行提示（重复调用会重启动画并替换文字，不会堆叠节点）。</summary>
    public static void Show(string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text) || !EnsureBuilt())
                return;

            if (_tween != null && _tween.IsValid())
                _tween.Kill();
            _tween = null;

            _label!.Text = text;
            _label.Visible = true;
            _label.Modulate = new Color(1f, 1f, 1f, 0f);

            var tween = _label.CreateTween();
            tween.TweenProperty(_label, "modulate:a", 1.0f, FadeInSec);
            tween.TweenInterval(HoldSec);
            tween.TweenProperty(_label, "modulate:a", 0.0f, FadeOutSec);
            tween.TweenCallback(Callable.From(() =>
            {
                if (_label != null && GodotObject.IsInstanceValid(_label))
                    _label.Visible = false;
            }));
            _tween = tween;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 屏幕提示显示失败（不影响功能）: {ex.Message}");
        }
    }

    private static bool EnsureBuilt()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return false;

        if (_layer != null && GodotObject.IsInstanceValid(_layer)
            && _label != null && GodotObject.IsInstanceValid(_label))
            return true;

        if (_tween != null && _tween.IsValid())
            _tween.Kill();
        _tween = null;
        // Godot 已释放的对象调用任何方法都会抛异常 —— 必须先验证
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
            _layer.QueueFree();

        _layer = new CanvasLayer { Layer = 99, Name = "QR_ModToast" };

        _label = new Label
        {
            Text = string.Empty,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ProcessMode = Node.ProcessModeEnum.Always,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        // 中文必须用系统字体（Godot 默认字体不含 CJK 字形）
        _label.AddThemeFontOverride("font",
            new SystemFont { FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" } });
        _label.AddThemeFontSizeOverride("font_size", 22);
        _label.AddThemeColorOverride("font_color", new Color(1.0f, 0.9f, 0.55f));   // 暖黄，与过场提示的白色区分
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _label.AddThemeConstantOverride("outline_size", 6);   // 描边保证亮底场景可读
        _label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // 居中偏下：避开手牌区上方的主要战斗视野
        _label.OffsetTop = -120f;
        _label.OffsetBottom = -120f;
        _layer.AddChild(_label);

        tree.Root.AddChild(_layer);
        return true;
    }
}
