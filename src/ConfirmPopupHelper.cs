using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace QuickRestart;

internal static class ConfirmPopupHelper
{
    private static readonly LocString _confirmLoc = new("main_menu_ui", "GENERIC_POPUP.confirm");
    private static readonly LocString _cancelLoc = new("main_menu_ui", "GENERIC_POPUP.cancel");

    private static NAbandonRunConfirmPopup? _deathPopup;

    /// <summary>死亡拦截弹窗是否仍在显示（被外部关闭/销毁后为 false）。</summary>
    public static bool IsDeathPopupOpen => _deathPopup != null && GodotObject.IsInstanceValid(_deathPopup);

    public static void ShowConfirm(string title, string message, Action onConfirm, Action? onCancel = null)
    {
        if (!SettingsManager.Current.ShowConfirmationDialog)
        {
            onConfirm();
            return;
        }

        try
        {
            var popup = NAbandonRunConfirmPopup.Create(null);
            if (popup == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 创建确认弹窗失败，直接执行");
                onConfirm();
                return;
            }

            var container = NModalContainer.Instance;
            if (container == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 弹窗容器不可用，直接执行");
                onConfirm();
                return;
            }
            container.Add(popup);

            var vp = popup.GetNode<NVerticalPopup>("VerticalPopup");
            vp.DisconnectSignals();
            vp.SetText(title, message);
            vp.InitYesButton(_confirmLoc, _ =>
            {
                try { onConfirm(); }
                catch (Exception ex) { Entry.Logger?.Error($"[QuickRestart] 确认操作失败: {ex}"); }
            });
            vp.InitNoButton(_cancelLoc, _ =>
            {
                try { onCancel?.Invoke(); }
                catch (Exception ex) { Entry.Logger?.Error($"[QuickRestart] 取消操作失败: {ex}"); }
            });

            Entry.Logger?.Info($"[QuickRestart] 显示原生确认弹窗: {title}");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 显示确认弹窗失败: {ex}");
            try { onConfirm(); } catch { }
        }
    }

    public static void ShowDeathInterceptPopup(Action onRetry, Action onAbandon)
    {
        try
        {
            var popup = NAbandonRunConfirmPopup.Create(null);
            if (popup == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 创建死亡拦截弹窗失败");
                return;
            }

            var container = NModalContainer.Instance;
            if (container == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 弹窗容器不可用，无法显示死亡拦截弹窗");
                return;
            }
            container.Add(popup);
            _deathPopup = popup;

            var vp = popup.GetNode<NVerticalPopup>("VerticalPopup");
            vp.DisconnectSignals();
            vp.SetText("致命一击拦截", "生命归零！\n【确认】重新挑战当前战斗\n【取消】放弃本局（同一种子从头重开）");
            vp.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), _ =>
            {
                Entry.Logger?.Info("[QuickRestart] 死亡弹窗：点击【确认=重新挑战】");
                try { onRetry(); }
                catch (Exception ex) { Entry.Logger?.Error($"[QuickRestart] 重新挑战失败: {ex}"); }
            });
            vp.InitNoButton(_cancelLoc, _ =>
            {
                Entry.Logger?.Info("[QuickRestart] 死亡弹窗：点击【取消=放弃本局（同种子重开）】");
                try { onAbandon(); }
                catch (Exception ex) { Entry.Logger?.Error($"[QuickRestart] 放弃本局失败: {ex}"); }
            });

            Entry.Logger?.Info("[QuickRestart] 显示原生死亡拦截弹窗");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Error($"[QuickRestart] 显示死亡拦截弹窗失败: {ex}");
        }
    }
}
