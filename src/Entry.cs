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
    public const string ModVersion = "0.1.8";

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

        if (Enabled)
        {
            Logger.Info($"[QuickRestart] 初始化完成 version={ModVersion}");
            Logger.Info("[QuickRestart] 快速重启已启用。暂停菜单将显示重启按钮、快捷键设置与幕数提示。");
            Logger.Info($"[QuickRestart] 快捷键（可在暂停菜单→快捷键设置中修改）: 重启房间={SettingsManager.Current.RestartRoomKey} 重启本层={SettingsManager.Current.RestartFloorKey} 重启本局={SettingsManager.Current.RestartRunKey} 重启新局={SettingsManager.Current.NewRunKey}");
        }
    }

    private static void DisableMod()
    {
        Enabled = false;
        // 补丁未能全部装上：把我方常驻 UI 收掉，避免留下没人操作的遮罩/提示层
        RestartOverlay.Shutdown();
        ModToast.Shutdown();
    }
}