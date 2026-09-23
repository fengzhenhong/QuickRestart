using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace QuickRestart;

/// <summary>
/// 轻量屏幕提示（居中偏下一行短句，淡入 → 停留 → 淡出）。
///
/// <para>用于"按下按键但什么都没发生"的场景必须给玩家反馈的情况（否则会被当成卡死/失灵），
/// 例如联机模式下按重启快捷键：应明确提示"联机模式下不可用"，而不是静默无反应。</para>
///
/// <para>实现：挂在 <see cref="SceneTree.Root"/> 下的 CanvasLayer（layer=99，不抢鼠标，
/// ProcessMode=Always 保证暂停/失焦也可见）。字体跟随游戏当前语言（见 <see cref="ModFonts"/>）。
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

            // 本层是常驻 CanvasLayer，而游戏换语言时不会碰模组的层 ——
            // 每次显示前重解析字体，玩家切完语言后的下一条提示就用新字体（见 ModFonts）。
            ModFonts.RefreshGameFont(_label!, ResolveProbe());

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

    /// <summary>彻底移除提示层（Mod 被停用时调用，避免留下常驻 CanvasLayer）。</summary>
    public static void Shutdown()
    {
        try
        {
            if (_tween != null && _tween.IsValid())
                _tween.Kill();
            _tween = null;
            if (_layer != null && GodotObject.IsInstanceValid(_layer))
                _layer.QueueFree();
            _layer = null;
            _label = null;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 屏幕提示卸载失败: {ex.Message}");
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
        // 字体跟随游戏当前语言（游戏按语言替换字体文件；见 ModFonts 注释）。
        ModFonts.ApplyGameFont(_label, ResolveProbe());
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

    /// <summary>当前可用的字体探测点（取游戏场景里的 Label；取不到交给 ModFonts 兜底）。</summary>
    private static Control? ResolveProbe()
        => ModFonts.FindProbe(NGame.Instance?.RootSceneContainer)
           ?? ModFonts.FindProbe(Engine.GetMainLoop() is SceneTree t ? t.Root : null);
}
