using Godot;

namespace QuickRestart;

/// <summary>
/// 重启过场遮罩（①）：重启开始时全屏黑幕淡入，结束后淡出。
/// 目的：把「场景销毁/重建期间的无内容黑屏 + 主菜单闪现」变成有意的过场动画 —— 只改观感，不碰任何游戏状态。
/// 直接路径与回退主菜单路径都需要它。
/// 挂在 SceneTree.Root 下的 CanvasLayer（layer=100），跨场景切换存活；任何失败都静默降级（无遮罩不影响功能）。
/// </summary>
internal static class RestartOverlay
{
    private const float FadeInSec = 0.12f;
    private const float FadeOutSec = 0.15f;

    private static CanvasLayer? _layer;
    private static ColorRect? _rect;
    private static Tween? _tween;

    public static void Show()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree)
                return;

            if (_layer == null || !GodotObject.IsInstanceValid(_layer)
                || _rect == null || !GodotObject.IsInstanceValid(_rect))
            {
                KillTween();
                _layer?.QueueFree();
                _layer = new CanvasLayer { Layer = 100, Name = "QR_RestartOverlay" };
                _rect = new ColorRect
                {
                    Color = Colors.Black,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    // 重启涉及场景切换与暂停态，遮罩必须不受树暂停影响
                    ProcessMode = Node.ProcessModeEnum.Always
                };
                _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                _layer.AddChild(_rect);
                tree.Root.AddChild(_layer);
            }

            var rect = _rect;
            if (rect == null || !GodotObject.IsInstanceValid(rect))
                return;

            KillTween();
            rect.Visible = true;
            rect.Modulate = new Color(1f, 1f, 1f, 0f);
            _tween = rect.CreateTween();
            _tween.TweenProperty(rect, "modulate:a", 1.0f, FadeInSec);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 过场遮罩淡入失败（不影响功能）: {ex.Message}");
        }
    }

    /// <summary>淡出并隐藏（补间异步播放，不阻塞流程）；在 finally 中调用，绝不抛异常。</summary>
    public static void Hide()
    {
        try
        {
            if (_rect == null || !GodotObject.IsInstanceValid(_rect) || !_rect.Visible)
                return;

            KillTween();
            _tween = _rect.CreateTween();
            _tween.TweenProperty(_rect, "modulate:a", 0.0f, FadeOutSec);
            _tween.TweenCallback(Callable.From(() =>
            {
                if (_rect != null && GodotObject.IsInstanceValid(_rect))
                    _rect.Visible = false;
            }));
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 过场遮罩淡出失败（不影响功能）: {ex.Message}");
            if (_rect != null && GodotObject.IsInstanceValid(_rect))
                _rect.Visible = false;
        }
    }

    private static void KillTween()
    {
        if (_tween != null && _tween.IsValid())
        {
            _tween.Kill();
        }
        _tween = null;
    }
}
