using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickRestart;

public sealed class QuickRestartSettings
{
    // 四个重启快捷键（可在游戏内"快捷键设置"里改）。
    // ⚠️ 不要再往这里加"没有读取点的开关/键位"：老版本曾有 RetryCombatKey（战后重新挑战快捷键）
    //    和 EnableWinStreakProtection（预留）两个字段，前者从未被任何输入处理读取、后者无实现，
    //    但它们会出现在玩家可编辑的 settings.json 里 —— 玩家照着改自然"没反应"。
    //    要加字段请同时接上读取点，并在 README 的功能说明里写清效果。
    public string RestartRoomKey { get; set; } = "R";
    public string RestartFloorKey { get; set; } = "F";
    public string RestartRunKey { get; set; } = "T";
    public string NewRunKey { get; set; } = "N";

    public bool EnableDeathIntercept { get; set; } = true;
    public bool EnablePostCombatRetry { get; set; } = true;
    public bool ShowConfirmationDialog { get; set; } = true;

    /// <summary>
    /// ④ 直接路径开关：重启时跳过主菜单（Transition.FadeOut → CleanUp → 直接读档/开新局），
    /// 省掉主菜单资源预载 + 场景创建两步大开销。失败自动回退旧路径（ReturnToMainMenu 全流程）。
    /// </summary>
    public bool FastRestartSkipMenu { get; set; } = true;
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
            try
            {
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, SettingsPath, overwrite: true);
            }
            finally
            {
                // 失败路径清理临时文件（Move 成功后该文件已不存在，此处为幂等无操作）
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                    // 清理失败不致命（下次 Save 会覆盖）
                }
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 保存设置失败: {ex.Message}");
        }
    }
}