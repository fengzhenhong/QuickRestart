using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace QuickRestart;

/// <summary>
/// 字体解析：让模组的文字用**游戏当前语言实际生效的字体**，而不是代码里写死的系统字体。
///
/// <para>为什么需要它：游戏不是"切一个字体"，而是**按语言替换字体文件**。
/// <c>FontManager._languageFontPathSets</c> 只对
/// jpn / kor / pol / rus / tha / zhs / zht 这几种语言生效；其余语言（eng / spa / fra …）
/// <c>NeedsFontSubstitution</c> 返回 false，用主题里的默认字体。
/// 游戏自己的 <c>MegaLabel</c> 在 <c>_Ready</c> 里调 <c>ApplyLocaleFontSubstitution</c>
/// 走的就是这条判断。</para>
///
/// <para>我们此前在 <see cref="RestartOverlay"/> / <see cref="ModToast"/> 里写死了
/// <c>SystemFont{"Microsoft YaHei UI", …}</c>，有两个后果：
/// ① 玩家把游戏切成日文/韩文时我们仍显示雅黑 —— 缺字形；
/// ② 机器上没装雅黑（英文 Windows / Steam Deck / Linux）时会 fallback，中文变方块。</para>
///
/// <para>现在的做法按优先级两段：
/// <b>①</b> 直接问游戏的 <c>FontManager</c> 要当前语言的替换字体（与游戏同源、最准）；
/// <b>②</b> 该语言不需要替换时，借一个游戏控件当前生效的字体（即主题默认字体）。
/// 两段都拿不到才回退系统 CJK 字体。</para>
/// </summary>
internal static class ModFonts
{
    /// <summary>兜底：系统 CJK 字体（雅黑优先）。仅在游戏两条路径都拿不到字体时使用。</summary>
    private static SystemFont? _fallback;

    /// <summary>回退告警只记一次，避免逐个控件刷日志。</summary>
    private static bool _warnedFallback;

    /// <summary>语言切换订阅状态（全局一份，避免重复订阅）。</summary>
    private static bool _subscribed;
    private static Action? _localeChanged;

    /// <summary>主题字体槽位名（Godot 里 Label 的字体槽就是 "font"）。</summary>
    private const string FontSlot = "font";

    /// <summary>
    /// 取当前游戏 UI 该用的字体。
    /// <para><paramref name="probe"/> 是"借字体"的探测控件：当当前语言不需要替换字体时，
    /// 从它身上取主题默认字体。传 null 时若也不需要替换，则走兜底。</para>
    /// </summary>
    public static Font GetGameFont(Control? probe = null)
    {
        // ① 当前语言有专属字体 → 直接取（与游戏 MegaLabel 完全同源）
        Font? substituted = TryGetSubstituteFont();
        if (substituted != null)
            return substituted;

        // ② 该语言不需要替换（英文等）→ 借游戏控件的主题默认字体
        Font? borrowed = TryBorrowFrom(probe);
        if (borrowed != null)
            return borrowed;

        // ③ 异常路径：系统 CJK 字体
        if (!_warnedFallback)
        {
            _warnedFallback = true;
            Entry.Logger?.Warn("[QuickRestart] 未能解析到游戏字体，回退系统 CJK 字体（雅黑优先）");
        }
        return _fallback ??= new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" }
        };
    }

    /// <summary>
    /// 给控件的字体槽挂上"与游戏一致"的字体。
    /// <para>对 <c>MegaLabel</c> 这一步是**必须**的：它 <c>_Ready</c> 里的
    /// <c>MegaLabelHelper.AssertThemeFontOverride</c> 在没有 theme 字体覆盖时会直接抛异常
    /// （游戏为规避一个 Godot 退出期挂起 bug 刻意加的断言）。
    /// 游戏自己的 MegaLabel 靠 .tscn 场景文件携带字体覆盖才通过，代码手建的必须自己补。</para>
    /// </summary>
    /// <returns>是否用上了游戏字体（false = 走了兜底系统字体）。</returns>
    public static bool ApplyGameFont(Control control, Control? probe = null)
        => ApplyGameFont(control, FontSlot, probe);

    /// <summary>指定字体槽名（如 RichTextLabel 的 "normal_font"）。</summary>
    public static bool ApplyGameFont(Control control, StringName slot, Control? probe = null)
    {
        // 顺带补挂语言切换订阅（初始化时 LocManager 可能还没就绪；这里每次调用都是一次重试机会）
        TrySubscribe();

        Font? substituted = TryGetSubstituteFont();
        if (substituted != null)
        {
            control.AddThemeFontOverride(slot, substituted);
            return true;
        }

        Font? borrowed = TryBorrowFrom(probe);
        if (borrowed != null)
        {
            control.AddThemeFontOverride(slot, borrowed);
            return true;
        }

        if (!_warnedFallback)
        {
            _warnedFallback = true;
            Entry.Logger?.Warn("[QuickRestart] 未能解析到游戏字体，回退系统 CJK 字体（雅黑优先）");
        }
        control.AddThemeFontOverride(slot, _fallback ??= new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" }
        });
        return false;
    }

    /// <summary>
    /// 重新解析并覆盖字体。**每次显示提示/遮罩时都该调一次**：
    /// 我们的 CanvasLayer 是常驻的（建一次反复用），游戏换语言时
    /// <c>NGame.Relocalize</c> 只管主菜单与它自己的两个屏幕，不会碰模组的层
    /// —— 只在创建时解析一次的话，玩家切语言后旧节点会一直用旧字体。
    /// <para>成本可忽略：<c>FontManager</c> 内部有 <c>_localeFonts</c> 字典缓存，
    /// 第二次起是字典命中，无资源 IO。</para>
    /// </summary>
    public static void RefreshGameFont(Control control, Control? probe = null)
        => ApplyGameFont(control, FontSlot, probe);

    /// <summary>
    /// 订阅游戏的语言切换通知（<c>LocManager.SubscribeToLocaleChange</c>）。
    /// <para>幂等：已订阅时直接返回。模组初始化时 <c>LocManager</c> 可能尚未就绪 ——
    /// 那种情况这里不订阅，改由 <see cref="RefreshGameFont"/>（每次显示提示时调用）再次尝试，
    /// 保证最终一定会挂上，而不是静默丢失。</para>
    /// </summary>
    public static void SubscribeLocaleChange(Action onLocaleChanged)
    {
        _localeChanged = onLocaleChanged;
        TrySubscribe();
    }

    /// <summary>真正挂载订阅；未就绪时返回 false，留给下次调用重试。</summary>
    private static bool TrySubscribe()
    {
        if (_subscribed || _localeChanged == null)
            return _subscribed;

        try
        {
            if (LocManager.Instance == null)
                return false;

            LocManager.Instance.SubscribeToLocaleChange(OnLocaleChanged);
            _subscribed = true;
            Entry.Logger?.Info($"[QuickRestart] 已订阅语言切换通知（当前语言={LocManager.Instance.Language}）");
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 订阅语言切换失败（不影响功能）: {ex.Message}");
            return false;
        }
    }

    /// <summary>取消订阅（Mod 停用时调用，避免回调指向已失效的宿主对象）。</summary>
    public static void UnsubscribeLocaleChange()
    {
        try
        {
            if (!_subscribed || LocManager.Instance == null)
                return;
            LocManager.Instance.UnsubscribeToLocaleChange(OnLocaleChanged);
        }
        catch
        {
            // 停用路径不抛
        }
        finally
        {
            _subscribed = false;
            _localeChanged = null;
        }
    }

    private static void OnLocaleChanged()
    {
        try
        {
            _localeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 语言切换回调异常（不影响功能）: {ex.Message}");
        }
    }

    /// <summary>
    /// 问游戏的 FontManager 要"当前语言"的替换字体；该语言不需要替换时返回 null。
    /// 全部包在 try 里：语言表 / LocManager 未就绪、或未来游戏改签名，都不该拖垮模组功能。
    /// </summary>
    private static Font? TryGetSubstituteFont()
    {
        try
        {
            LocManager? loc = LocManager.Instance;
            if (loc == null)
                return null;

            string language = loc.Language;
            if (string.IsNullOrEmpty(language) || !FontManager.NeedsFontSubstitution(language))
                return null;

            Font? f = FontManager.GetSubstituteFont(language, FontType.Regular);
            return f != null && GodotObject.IsInstanceValid(f) ? f : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从一个游戏控件上借它当前生效的字体（含主题继承）。</summary>
    private static Font? TryBorrowFrom(Control? probe)
    {
        try
        {
            if (probe is not Label label || !GodotObject.IsInstanceValid(label))
                return null;

            Font? f = label.GetThemeFont(FontSlot);
            return f != null && GodotObject.IsInstanceValid(f) ? f : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从给定节点沿祖先链找一个游戏自己的 <c>Label</c> / <c>MegaLabel</c> 作探测点。
    /// 只走祖先链（O(深度)），不做全树遍历 —— 探测不到就走兜底，不值得为它遍历场景。
    /// </summary>
    public static Control? FindProbe(Node? from)
    {
        try
        {
            if (from == null || !GodotObject.IsInstanceValid(from))
                return null;

            Node? cur = from;
            while (cur != null)
            {
                if (cur is Label lb && HasUsableFont(lb))
                    return lb;
                cur = cur.GetParent();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>该 Label 当前是否解析得到一个有效字体。</summary>
    private static bool HasUsableFont(Label label)
    {
        try
        {
            Font? f = label.GetThemeFont(FontSlot);
            return f != null && GodotObject.IsInstanceValid(f);
        }
        catch
        {
            return false;
        }
    }
}
