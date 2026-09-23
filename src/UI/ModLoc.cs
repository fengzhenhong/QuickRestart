using MegaCrit.Sts2.Core.Localization;

namespace QuickRestart;

/// <summary>
/// 模组文案的本地化。
///
/// <para>背景：本模组所有面向玩家的文案原是**硬编码中文**，于是玩家把游戏切成英文后，
/// 游戏按钮是 Resume / Settings，我们的按钮仍是「重启房间 / 换角色开新局」——
/// 中英混排（见截图）。字体跟随解决不了这个：字体只管"用哪套字形"，不管"文字内容"。</para>
///
/// <para>做法：自带一张极小的键→译文表，按 <c>LocManager.Instance.Language</c> 选择。
/// 只有中英两套（其余语言一律回退英文），理由是这个模组的玩家面文字量很小、
/// 且中文与英文已覆盖绝大多数使用者；用游戏原生的 <c>LocString</c> 管线需要注册自定义
/// LocTable 与 .pck 资源，对一个纯 UI 文案的模组是不必要的复杂度。</para>
///
/// <para>语言代号用游戏的三字母码（<c>zhs</c> / <c>zht</c> / <c>eng</c> …），
/// 与 <c>LocManager.Language</c> 同源，避免自己维护一套映射。</para>
/// </summary>
internal static class ModLoc
{
    /// <summary>语言切换时由 <see cref="ModFonts"/> 的通知回调置位，触发常驻文案重建。</summary>
    private static string? _cachedLanguage;

    /// <summary>当前是否中文环境（zhs 简体 / zht 繁体都按中文处理）。</summary>
    private static bool IsChinese
    {
        get
        {
            try
            {
                string? lang = LocManager.Instance?.Language;
                return lang is "zhs" or "zht";
            }
            catch
            {
                // 取不到语言（极早期 / 异常）→ 按中文，保持与历史行为一致
                return true;
            }
        }
    }

    /// <summary>当前语言代号（用于检测是否变化）；取不到返回空串。</summary>
    public static string CurrentLanguage
    {
        get
        {
            try
            {
                return LocManager.Instance?.Language ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>语言是否与上次调用时不同（用于决定要不要重建常驻控件）。</summary>
    public static bool LanguageChanged()
    {
        string now = CurrentLanguage;
        if (now == _cachedLanguage)
            return false;
        _cachedLanguage = now;
        return true;
    }

    /// <summary>取文案：中文环境用 <paramref name="zh"/>，其余用 <paramref name="en"/>。</summary>
    public static string T(string en, string zh) => IsChinese ? zh : en;

    // ─────────────────────────────────────────────────────────────
    // 暂停菜单按钮
    // ─────────────────────────────────────────────────────────────
    public static string RestartRoom => T("Restart Room", "重启房间");
    public static string RestartFloor => T("Restart Act", "重启本层");
    public static string RestartRun => T("Restart Run", "重启本局");
    public static string NewRun => T("New Run", "重启新局");
    public static string SwitchCharacter => T("Switch Character", "换角色开新局");
    public static string KeyBinds => T("Key Bindings", "快捷键设置");

    /// <summary>重启本层按钮：带当前幕数（英文用 "Act N"，中文用「第 N 幕」）。</summary>
    public static string RestartFloorWithAct(int actOneBased)
    {
        if (actOneBased <= 0)
            return RestartFloor;
        return IsChinese
            ? $"重启本层（第 {actOneBased} 幕）"
            : $"Restart Act (Act {actOneBased})";
    }

    // ─────────────────────────────────────────────────────────────
    // 快捷键设置面板
    // ─────────────────────────────────────────────────────────────
    public static string KeyBindTitle => T("Key Bindings", "快捷键设置");
    public static string KeyBindHint => T("Click a row, then press a new key (Esc to cancel)",
                                          "点击一行，再按下新按键（Esc 取消）");
    public static string Close => T("Close", "关闭");
    public static string PressNewKey => T("press a new key… (Esc to cancel)", "按下新按键…（Esc 取消）");

    /// <summary>重新绑定行的显示（英文如 "Restart Room    T"）。</summary>
    public static string BindRow(string actionLabel, string key)
        => $"{actionLabel}    {key}";

    // ─────────────────────────────────────────────────────────────
    // 确认弹窗
    // ─────────────────────────────────────────────────────────────
    public static string ConfirmTitle(RestartKind kind) => kind switch
    {
        RestartKind.RestartRoom => RestartRoom,
        RestartKind.RestartFloor => RestartFloor,
        RestartKind.RestartRun => RestartRun,
        RestartKind.NewRun => NewRun,
        RestartKind.SwitchCharacter => SwitchCharacter,
        RestartKind.PostCombatRetry => T("Retry Fight", "重新挑战"),
        _ => T("Confirm", "确认")
    };

    /// <summary>「确认{action}？」这类通用确认。</summary>
    public static string ConfirmAction(string action)
        => IsChinese ? $"确认{action}？" : $"Confirm: {action}?";

    public static string ConfirmRestartFloor(int actOneBased)
        => IsChinese
            ? $"确认回到第 {actOneBased} 幕起点重新选路？\n（本幕获得的金币/卡牌/遗物/药水/血量将全部回退）"
            : $"Return to the start of Act {actOneBased} and re-route?\n"
              + "(Gold, cards, relics, potions and HP gained this act will all be rolled back.)";

    public static string ConfirmRestartFloorNoAct
        => T("Return to the start of this act and re-route?",
             "确认回到当前幕起点重新选路？");

    public static string ConfirmRestartRun
        => T("Restart this run with the same character and seed?",
             "确认同角色同种子重新开局？");

    public static string ConfirmNewRun
        => T("Start a brand new run with the same character and a new random seed?",
             "确认同角色新随机种子全新开局？");

    public static string ConfirmSwitchCharacter
        => T("Discard the current run and return to character select?\n"
             + "(This run won't be recorded, but its save file will be deleted — "
             + "you cannot come back via \"Continue\".)",
             "确认丢弃当前局面、回到角色选择界面？\n"
             + "（本局不写入战绩，但存档会被删除，无法用「继续游戏」找回）");

    public static string ConfirmPostCombatRetry
        => T("Discard the rewards and retry this fight?",
             "确认放弃战利品，重新挑战本场战斗？");

    /// <summary>「回到地图」动作名（每日局提示会用到）。</summary>
    public static string BackToMap => T("Back to Map", "回到地图");

    // ─────────────────────────────────────────────────────────────
    // 刀下留人（死亡拦截）
    // ─────────────────────────────────────────────────────────────
    public static string DeathInterceptTitle => T("Second Chance", "刀下留人");

    public static string DeathInterceptNormal
        => T("HP reached zero!\n[Confirm] Back to map and retry (before entering this room)\n"
             + "[Cancel] Give up this run (restart from the same seed)",
             "生命归零！\n【确认】回到地图重新挑战（进本房间前）\n【取消】放弃本局（同一种子从头重开）");

    public static string DeathInterceptDaily
        => T("HP reached zero!\n[Confirm] Back to map and retry (before entering this room)\n"
             + "[Cancel] Give up this run (resolved as a normal loss)",
             "生命归零！\n【确认】回到地图重新挑战（进本房间前）\n"
             + "【取消】放弃本局（按游戏正常流程结算本次失败）");

    // ─────────────────────────────────────────────────────────────
    // 屏幕提示 / 过场提示
    // ─────────────────────────────────────────────────────────────
    public static string ToastMultiplayerUnavailable(string modName)
        => T($"Unavailable in multiplayer ({modName} is single-player only)",
             $"联机模式下不可用（{modName}仅支持单人）");

    public static string ToastDailyUnsupported(string what)
        => T($"Daily runs do not support: {what}", $"每日挑战局不支持{what}");

    public static string ToastNoSeed
        => T("Could not read this run's seed — starting with a new seed instead",
             "未能取到本局种子，已改用新种子开局");

    public static string ToastNoTraceProtectionFailed
        => T("Warning: trace-free protection failed — this run may be recorded",
             "警告：无痕保护未生效，旧局可能计入战绩");

    public static string ToastNoTraceProtectionFailedThisRun
        => T("Warning: trace-free protection failed — this run may be recorded",
             "警告：无痕保护未生效，本局可能计入战绩");

    /// <summary>换角色失败但仍在本局：本局保持不变。</summary>
    public static string ToastSwitchCharacterFailedKeptRun
        => T("Switch character failed — this run is unchanged",
             "换角色未成功，本局保持不变");

    /// <summary>换角色失败且本局已被丢弃。</summary>
    public static string ToastSwitchCharacterFailedRunLost
        => T("Switch character failed and this run was discarded — please restart the game to continue",
             "换角色未成功，本局已丢弃，请重启游戏继续");

    /// <summary>主菜单未就绪时的兜底提示。</summary>
    public static string ToastMainMenuNotReady
        => T("Returned to the main menu — open Singleplayer → New Game to pick a character",
             "已回到主菜单，请点「单人游戏 → 新游戏」选角色");

    public static string NoteSnapshotFromResume
        => T("This act's snapshot came from a save load: can only roll back to the load point "
             + "(the game never saved this act's start)",
             "本幕快照来自读档点：只能回到「读档时」的状态（游戏未保存本幕开场数据）");

    // ─────────────────────────────────────────────────────────────
    // 快捷键被忽略的原因（写日志，便于"按了没反应"时定位）
    // ─────────────────────────────────────────────────────────────
    public static string ReasonWindowNotFocused => T("game window not focused", "游戏窗口未获焦");
    public static string ReasonTextFocus => T("focus is in a text field", "焦点在文本输入框");
    public static string ReasonModalOpen => T("a modal popup is open", "有模态弹窗打开");

    // ─────────────────────────────────────────────────────────────
    // 重启类型名（用于日志与通用确认框）
    // ─────────────────────────────────────────────────────────────
    public static string NameOf(RestartKind kind) => ConfirmTitle(kind);
}

/// <summary>重启类型（与 <see cref="RestartService.RestartType"/> 对应，供本地化取名字用）。</summary>
internal enum RestartKind
{
    RestartRoom,
    RestartFloor,
    RestartRun,
    NewRun,
    SwitchCharacter,
    PostCombatRetry
}
