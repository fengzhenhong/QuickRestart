using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;

namespace QuickRestart;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public const string ModId = "QuickRestart";

    /// <summary>
    /// 玩家可见的中文显示名 —— 单一出处（加载器 json、屏幕提示、日志都用它）。
    /// MOD 目录与 DLL 名保持英文 QuickRestart（加载器要求与 json 的 id 一致）。
    /// </summary>
    public const string ModName = "回溯之镜";

    /// <summary>
    /// 当前代码版本 —— 与 QuickRestart.json 的 version 同步维护。
    /// <para>⚠️ 不能用 <c>Assembly.GetName().Version</c>：本工程 <c>GenerateAssemblyInfo=false</c>
    /// （csproj 关了程序集信息生成），产物 AssemblyVersion 恒为 0.0.0.0，
    /// 日志里永远看不到"线上跑的到底是哪一版"。改代码必须同时改这一个常量与 json。</para>
    /// </summary>
    public const string ModVersion = "0.1.9";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; private set; } = null!;
    public static bool Enabled { get; private set; } = true;

    public static void Initialize()
    {
        Logger = RitsuLibFramework.CreateLogger(ModId);

        SettingsManager.Load();

        var patcher = RitsuLibFramework.CreatePatcher(ModId, "quick-restart", "快速重启");
        patcher.RegisterPatch<PauseMenuReadyPatch>();
        patcher.RegisterPatch<PauseMenuExitPatch>();
        patcher.RegisterPatch<CardRewardScreenReadyPatch>();
        patcher.RegisterPatch<CardRewardScreenExitPatch>();
        patcher.RegisterPatch<PlayerDeathInterceptPatch>();
        patcher.RegisterPatch<LoseCombatInterceptPatch>();
        patcher.RegisterPatch<RoomEnterTrackPatch>();
        patcher.RegisterPatch<MapPointSnapshotPatch>();
        patcher.RegisterPatch<RunInputPatch>();
        patcher.RegisterPatch<ActSnapshotPatch>();

        RitsuLibFramework.ApplyRequiredPatcher(patcher, DisableMod);

        // 语言切换订阅：常驻的过场提示/屏幕提示需要在切语言后换字体与文案。
        // 游戏不给模组广播，用 LocManager.SubscribeToLocaleChange（游戏自己的机制）。
        // 模组初始化时 LocManager 可能尚未就绪，ModFonts 内部会容忍并跳过（下次显示时仍按需刷新）。
        ModFonts.SubscribeLocaleChange(OnLocaleChanged);

        if (Enabled)
        {
            Logger.Info($"[QuickRestart] 初始化完成 version={ModVersion}");
            Logger.Info($"[QuickRestart] 快速重启已启用。暂停菜单将显示重启按钮、快捷键设置与幕数提示。");
            Logger.Info($"[QuickRestart] 快捷键（可在暂停菜单→快捷键设置中修改）: 重启房间={SettingsManager.Current.RestartRoomKey} 重启本层={SettingsManager.Current.RestartFloorKey} 重启本局={SettingsManager.Current.RestartRunKey} 重启新局={SettingsManager.Current.NewRunKey}");
        }
    }

    /// <summary>
    /// 玩家切换游戏语言时：把常驻控件的文案/字体标记为待刷新。
    /// <para>大多数界面（暂停菜单按钮、快捷键面板、各种弹窗）都是**每次打开时重新读
    /// <see cref="ModLoc"/>**，天然跟随；这里只处理常驻的过场提示与屏幕提示 ——
    /// 它们的内容在下一次显示时才会重算，届时 <c>RefreshGameFont</c> 会取到新语言字体。</para>
    /// </summary>
    private static void OnLocaleChanged()
    {
        try
        {
            Logger?.Info($"[QuickRestart] 检测到游戏语言切换 → {ModLoc.CurrentLanguage}（常驻提示将在下次显示时刷新）");
            ModLoc.LanguageChanged();
        }
        catch (Exception ex)
        {
            Logger?.Warn($"[QuickRestart] 语言切换处理失败（不影响功能）: {ex.Message}");
        }
    }

    private static void DisableMod()
    {
        Enabled = false;
        // 补丁未能全部装上：把我方常驻 UI 收掉，避免留下没人操作的遮罩/提示层
        ModFonts.UnsubscribeLocaleChange();
        RestartOverlay.Shutdown();
        ModToast.Shutdown();
    }
}