using Godot;

namespace QuickRestart;

/// <summary>
/// 重启过场遮罩：重启开始时全屏黑幕淡入，结束后淡出。
/// 目的：把「场景销毁/重建期间的无内容黑屏 + 主菜单闪现」变成有意的过场动画 —— 只改观感，不碰任何游戏状态。
/// <see cref="SetNote"/> 可在黑幕上追加一行说明文字（随黑幕一起淡入淡出），例如如实告知
/// "本幕快照来自读档点 → 只能回滚到读档时状态"（读档进入的当幕没有真实幕初数据，任何 mod 都无法还原）。
/// 挂在 SceneTree.Root 下的 CanvasLayer（layer=100），跨场景切换存活；任何失败都静默降级（无遮罩不影响功能）。
/// </summary>
internal static class RestartOverlay
{
    private const float FadeInSec = 0.12f;
    private const float FadeOutSec = 0.15f;

    private static CanvasLayer? _layer;
    private static ColorRect? _rect;
    private static Label? _note;
    private static Tween? _rectTween;
    private static Tween? _noteTween;

    public static void Show()
    {
        try
        {
            if (!EnsureBuilt())
                return;

            KillTween(ref _rectTween);
            ClearNote();

            _rect!.Visible = true;
            _rect.Modulate = new Color(1f, 1f, 1f, 0f);
            _rectTween = _rect.CreateTween();
            _rectTween.TweenProperty(_rect, "modulate:a", 1.0f, FadeInSec);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 过场遮罩淡入失败（不影响功能）: {ex.Message}");
        }
    }

    /// <summary>
    /// 在黑幕上显示一行说明（居中偏下，随黑幕一起淡出；<see cref="Show"/> 会清空上一条）。
    /// 用于"本次读档进入：只能回到读档点"这类如实告知 —— 玩家无需翻日志即可知道回滚目标。
    /// </summary>
    public static void SetNote(string text)
    {
        try
        {
            if (!EnsureBuilt() || string.IsNullOrEmpty(text))
                return;

            KillTween(ref _noteTween);
            _note!.Text = text;
            _note.Visible = true;
            _note.Modulate = new Color(1f, 1f, 1f, 0f);
            _noteTween = _note.CreateTween();
            _noteTween.TweenProperty(_note, "modulate:a", 1.0f, FadeInSec);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 过场提示显示失败（不影响功能）: {ex.Message}");
        }
    }

    /// <summary>淡出并隐藏（补间异步播放，不阻塞流程）；在 finally 中调用，绝不抛异常。</summary>
    public static void Hide()
    {
        try
        {
            if (_rect != null && GodotObject.IsInstanceValid(_rect) && _rect.Visible)
            {
                KillTween(ref _rectTween);
                _rectTween = FadeOutNode(_rect);
            }
            if (_note != null && GodotObject.IsInstanceValid(_note) && _note.Visible)
            {
                KillTween(ref _noteTween);
                _noteTween = FadeOutNode(_note);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 过场遮罩淡出失败（不影响功能）: {ex.Message}");
            HideNow();
        }
    }

    /// <summary>彻底移除遮罩层（Mod 被停用时调用，避免留下没人操作的常驻 CanvasLayer）。</summary>
    public static void Shutdown()
    {
        try
        {
            KillTween(ref _rectTween);
            KillTween(ref _noteTween);
            if (_layer != null && GodotObject.IsInstanceValid(_layer))
                _layer.QueueFree();
            _layer = null;
            _rect = null;
            _note = null;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 过场遮罩卸载失败: {ex.Message}");
        }
    }

    private static Tween FadeOutNode(Control node)
    {
        var tween = node.CreateTween();
        tween.TweenProperty(node, "modulate:a", 0.0f, FadeOutSec);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(node))
                node.Visible = false;
        }));
        return tween;
    }

    private static void HideNow()
    {
        if (_rect != null && GodotObject.IsInstanceValid(_rect))
            _rect.Visible = false;
        if (_note != null && GodotObject.IsInstanceValid(_note))
            _note.Visible = false;
    }

    private static void ClearNote()
    {
        if (_note == null || !GodotObject.IsInstanceValid(_note))
            return;
        KillTween(ref _noteTween);
        _note.Visible = false;
        _note.Text = string.Empty;
    }

    private static bool EnsureBuilt()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return false;

        if (_layer != null && GodotObject.IsInstanceValid(_layer)
            && _rect != null && GodotObject.IsInstanceValid(_rect))
            return true;

        KillTween(ref _rectTween);
        KillTween(ref _noteTween);
        // 注意：Godot 已释放的对象调用任何方法都会抛 ObjectDisposedException —— 必须先验证
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
        {
            _layer.QueueFree();
        }

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

        _note = new Label
        {
            Text = string.Empty,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ProcessMode = Node.ProcessModeEnum.Always,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        // 中文必须用系统字体：Godot 默认字体不含 CJK 字形（雅黑优先，黑体兜底）
        _note.AddThemeFontOverride("font",
            new SystemFont { FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" } });
        _note.AddThemeFontSizeOverride("font_size", 26);
        _note.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
        _note.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _note.OffsetTop = 140f;      // 整体下移 140px：居中偏下，避开场景 UI
        _note.OffsetBottom = 140f;
        _layer.AddChild(_note);

        tree.Root.AddChild(_layer);
        return true;
    }

    private static void KillTween(ref Tween? tween)
    {
        if (tween != null && tween.IsValid())
        {
            tween.Kill();
        }
        tween = null;
    }
}
