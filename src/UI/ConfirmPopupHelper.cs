using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace QuickRestart;

internal static class ConfirmPopupHelper
{
    private static NAbandonRunConfirmPopup? _deathPopup;

    /// <summary>刀下留人弹窗是否仍在显示（被外部关闭/销毁后为 false）。</summary>
    public static bool IsDeathPopupOpen => _deathPopup != null && GodotObject.IsInstanceValid(_deathPopup);

    /// <summary>容器当前持有的 modal 是不是我们这个弹窗。</summary>
    private static bool IsOwnedByContainer(Control? popup)
        => popup != null && ReferenceEquals(NModalContainer.Instance?.OpenModal, popup);

    /// <summary>
    /// 原生确认弹窗。<paramref name="onConfirm"/> 一定执行一次：弹窗显示成功 → 点【确认】时执行；
    /// 弹窗没显示（容器被占/创建失败/接线异常）→ 当场执行并记日志。
    /// <para>宁可"少一次确认框"，也不要"按了键毫无反应"（后者会被当成卡死且无从排查）。</para>
    /// </summary>
    /// <returns>true = 弹窗已接管玩家选择；false = 未显示（已直接执行）。</returns>
    public static bool ShowConfirm(string title, string message, Action onConfirm, Action? onCancel = null)
    {
        if (!SettingsManager.Current.ShowConfirmationDialog)
        {
            onConfirm();
            return false;
        }

        var locConfirm = new LocString("main_menu_ui", "GENERIC_POPUP.confirm");
        var locCancel = new LocString("main_menu_ui", "GENERIC_POPUP.cancel");

        NAbandonRunConfirmPopup? popup = null;
        bool wired = false;
        try
        {
            popup = NAbandonRunConfirmPopup.Create(null);
            var container = NModalContainer.Instance;
            if (popup != null && container != null)
            {
                container.Add(popup);
                if (IsOwnedByContainer(popup))
                {
                    // ⚠️ 必须先 Add 再接线：NAbandonRunConfirmPopup._Ready 会用游戏自己的
                    //   "放弃本局"文案与回调初始化按钮（容器已 ready 时 AddChild 是同步的）。
                    //   先接线后 Add 会覆盖我们的文案，并把游戏回调与我们的回调同时连上。
                    Wire(popup, title, message, locConfirm, locCancel, onConfirm, onCancel);
                    wired = true;
                    Entry.Logger?.Info($"[QuickRestart] 显示原生确认弹窗: {title}");
                }
                else
                {
                    Entry.Logger?.Warn($"[QuickRestart] 已有其它弹窗占用容器，确认框无法显示（{title}）");
                }
            }
            else
            {
                Entry.Logger?.Warn($"[QuickRestart] 确认弹窗不可用（popup={popup != null} container={container != null}），直接执行: {title}");
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 显示确认弹窗失败（{title}）: {ex}");
        }

        if (wired)
            return true;            // 由弹窗按钮负责执行 onConfirm

        // 没显示成：收掉孤儿/半残弹窗，然后当场执行 —— 宁可少一次确认，也不要"按了键毫无反应"
        Discard(popup);
        try { onConfirm(); }
        catch (Exception ex) { Entry.Logger?.Error($"[QuickRestart] 直接执行失败: {ex}"); }
        return false;
    }

    /// <summary>
    /// 刀下留人弹窗。返回 true = 弹窗已被容器接管且按钮接线完成；
    /// false = 未显示或接线失败（调用方必须善后：复位拦截状态并解除战斗暂停）。
    /// </summary>
    /// <param name="message">正文文案 —— 由调用方按对局模式给（每日局的"取消"语义不同）。</param>
    public static bool ShowDeathInterceptPopup(string message, Action onRetry, Action onAbandon, Action onDismissed)
    {
        NAbandonRunConfirmPopup? popup = null;
        bool shown = false;
        try
        {
            popup = NAbandonRunConfirmPopup.Create(null);
            var container = NModalContainer.Instance;
            if (popup == null || container == null)
            {
                Entry.Logger?.Warn($"[QuickRestart] 刀下留人弹窗不可用（popup={popup != null} container={container != null}）");
                return false;
            }

            container.Add(popup);
            if (!IsOwnedByContainer(popup))
            {
                Entry.Logger?.Warn("[QuickRestart] 已有其它弹窗占用容器，刀下留人弹窗无法显示");
                return false;
            }

            Wire(popup, ModLoc.DeathInterceptTitle, message,
                new LocString("main_menu_ui", "GENERIC_POPUP.confirm"),
                new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
                onRetry, onAbandon);

            // 弹窗可能不经任何按钮就被关掉（Esc、CleanUp 里容器 Clear、被别的弹窗顶掉）：
            // 那种情况下按钮回调永不触发，必须由 TreeExiting 复位 —— 否则战斗永久冻结、玩家既不死也救不了。
            popup.TreeExiting += () =>
            {
                if (!ReferenceEquals(_deathPopup, popup))
                    return;                 // 玩家已作出选择（按钮里已释放引用）
                _deathPopup = null;
                Entry.Logger?.Warn("[QuickRestart] 刀下留人弹窗未作选择即被关闭，复位拦截状态并解除战斗暂停");
                try { onDismissed(); }
                catch (Exception ex) { Entry.Logger?.Error($"[QuickRestart] 弹窗关闭善后失败: {ex}"); }
            };
            _deathPopup = popup;            // 全部接线成功后才持有引用
            shown = true;

            Entry.Logger?.Info("[QuickRestart] 显示原生刀下留人弹窗");
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 显示刀下留人弹窗失败: {ex}");
            return false;
        }
        finally
        {
            if (!shown)
                Discard(popup);             // 绝不留半残弹窗占着容器（会永久挡住后续所有弹窗）
        }
    }

    /// <summary>
    /// 丢弃一个不会（或不能）再被点击的弹窗：已占住容器的必须连容器一起清（否则它会永久挡住后续弹窗），
    /// 从未被接管的只释放自己的场景副本。
    /// </summary>
    private static void Discard(Control? popup)
    {
        try
        {
            if (IsOwnedByContainer(popup))
            {
                _deathPopup = null;
                NModalContainer.Instance?.Clear();   // 同时释放该弹窗并复位 OpenModal
                return;
            }
            if (popup != null && GodotObject.IsInstanceValid(popup))
                popup.QueueFree();
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 清理未生效弹窗失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 用我们的文案与回调接管一个已挂树的弹窗：先断开游戏自带的"放弃本局"回调，再连自己的。
    /// <para>游戏 <c>InitYesButton/InitNoButton</c> 会额外连上内置 <c>Close</c>（点完清容器），
    /// 重新调用一遍即可保留同样的自动关闭行为。</para>
    /// </summary>
    private static void Wire(NAbandonRunConfirmPopup popup, string title, string message,
        LocString locConfirm, LocString locCancel, Action onConfirm, Action? onCancel)
    {
        var vp = popup.GetNode<NVerticalPopup>("VerticalPopup");
        vp.DisconnectSignals();
        vp.SetText(title, message);
        vp.InitYesButton(locConfirm, _ =>
        {
            _deathPopup = null;   // 已作出选择：释放引用（避免静态字段长期持有已释放节点）
            Entry.Logger?.Info($"[QuickRestart] 弹窗【确认】: {title}");
            try { onConfirm(); }
            catch (Exception ex) { Entry.Logger?.Error($"[QuickRestart] 确认操作失败: {ex}"); }
        });
        vp.InitNoButton(locCancel, _ =>
        {
            _deathPopup = null;
            Entry.Logger?.Info($"[QuickRestart] 弹窗【取消】: {title}");
            try { onCancel?.Invoke(); }
            catch (Exception ex) { Entry.Logger?.Error($"[QuickRestart] 取消操作失败: {ex}"); }
        });
    }
}
