using System.Reflection;
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
        patcher.RegisterPatch<PreRoomSnapshotPatch>();
        patcher.RegisterPatch<RunInputPatch>();
        patcher.RegisterPatch<ActSnapshotPatch>();

        RitsuLibFramework.ApplyRequiredPatcher(patcher, DisableMod);

        if (Enabled)
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version
                ?? new Version(0, 1, 0);
            Logger.Info($"[QuickRestart] 初始化完成 version={version.ToString(3)}");
            Logger.Info("[QuickRestart] 快速重启已启用。暂停菜单将显示重启按钮、快捷键设置与幕数提示。");
            Logger.Info($"[QuickRestart] 快捷键（可在暂停菜单→快捷键设置中修改）: 重启房间={SettingsManager.Current.RestartRoomKey} 重启本层={SettingsManager.Current.RestartFloorKey} 重启本局={SettingsManager.Current.RestartRunKey} 重启新局={SettingsManager.Current.NewRunKey}");
        }
    }

    private static void DisableMod()
    {
        Enabled = false;
    }
}