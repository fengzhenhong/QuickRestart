using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickRestart;

public sealed class QuickRestartSettings
{
    public string RestartRoomKey { get; set; } = "R";
    public string RestartFloorKey { get; set; } = "F";
    public string RestartRunKey { get; set; } = "T";
    public string NewRunKey { get; set; } = "N";
    public string RetryCombatKey { get; set; } = "R";

    public bool EnableDeathIntercept { get; set; } = true;
    public bool EnableWinStreakProtection { get; set; } = false;
    public bool EnablePostCombatRetry { get; set; } = true;
    public bool ShowConfirmationDialog { get; set; } = true;
}

public static class SettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2", "mod_data", Entry.ModId);

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static QuickRestartSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<QuickRestartSettings>(json) ?? new QuickRestartSettings();
            }
            else
            {
                // 首次运行：生成默认配置（也便于玩家直接编辑 settings.json）
                Save();
                Entry.Logger?.Info($"[QuickRestart] 已生成默认配置文件: {SettingsPath}");
            }

            Normalize();
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 加载设置失败: {ex.Message}");
        }
    }

    /// <summary>规范化：空/缺失的键位回退默认值（配置文件被手改坏时也不会影响功能）。</summary>
    private static void Normalize()
    {
        var s = Current;
        if (string.IsNullOrEmpty(s.RestartRoomKey)) s.RestartRoomKey = "R";
        if (string.IsNullOrEmpty(s.RestartFloorKey)) s.RestartFloorKey = "F";
        if (string.IsNullOrEmpty(s.RestartRunKey)) s.RestartRunKey = "T";
        if (string.IsNullOrEmpty(s.NewRunKey)) s.NewRunKey = "N";
        if (string.IsNullOrEmpty(s.RetryCombatKey)) s.RetryCombatKey = "R";
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            // 原子写：先写临时文件再替换 —— 避免写入中途中断导致 settings.json 损坏
            string tmpPath = SettingsPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 保存设置失败: {ex.Message}");
        }
    }
}