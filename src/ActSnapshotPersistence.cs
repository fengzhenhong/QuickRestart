using System.Text.Json;
using MegaCrit.Sts2.Core.Saves;

namespace QuickRestart;

/// <summary>
/// 幕初快照的磁盘持久化 —— 突破「读档进入的当幕无法回到幕初」的数据限制。
///
/// <para>**为什么需要**：游戏存档 current_run.save 只保存**当前状态**，不含"本幕开场"数据。
/// 玩家用「继续游戏」读档进入后，内存里没有任何幕初快照，重启本层只能退回读档点（玩家感受"按了没动"）。</para>
///
/// <para>**做法**：mod 在每次**真实进入新幕**（地图屏就绪、且本幕尚无已走节点）时，把完整运行快照写盘；
/// 之后无论读档继续、关开游戏，都能凭 [局标识 StartTime + 幕号] 校验后读回 —— 真正回到本幕起点。</para>
///
/// <para>**落盘**：%APPDATA%\SlayTheSpire2\mod_data\QuickRestart\act_start_snapshot.json
/// （约 60KB，仅保留最新一份；换幕覆盖、新局清理）。序列化用游戏自身的
/// <see cref="JsonSerializationUtility"/>（源生成器上下文），与 current_run.save 同格式。</para>
///
/// <para>任何失败只记日志，绝不影响游戏与本模块其它功能。</para>
/// </summary>
internal static class ActSnapshotPersistence
{
    private static readonly string SnapshotDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2", "mod_data", Entry.ModId);

    private static readonly string SnapshotPath = Path.Combine(SnapshotDir, "act_start_snapshot.json");

    /// <summary>保存"真·幕初"快照（只在确认非读档恢复点时调用；原子写，避免写一半损坏）。</summary>
    public static void Save(SerializableRun snapshot, int actIndex)
    {
        try
        {
            Directory.CreateDirectory(SnapshotDir);
            string json = JsonSerializer.Serialize(snapshot, JsonSerializationUtility.GetTypeInfo<SerializableRun>());

            string tmp = SnapshotPath + ".tmp";
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, SnapshotPath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                        File.Delete(tmp);
                }
                catch
                {
                    // 清理失败不致命（下次 Save 会覆盖）
                }
            }

            Entry.Logger?.Info($"[QuickRestart] 幕初快照已写盘（第 {actIndex + 1} 幕，{json.Length / 1024}KB）—— 之后读档继续也能回滚到本幕起点");
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 幕初快照写盘失败（不影响功能）: {ex.Message}");
        }
    }

    /// <summary>
    /// 读回磁盘上的幕初快照。必须同时满足：局标识（StartTime）与幕号均匹配 —— 防止跨局/跨幕误用；
    /// 旧局残留顺带清理（不占空间）。任何异常都按"无快照"处理。
    /// </summary>
    public static SerializableRun? TryLoad(long currentRunStart, int currentActIndex)
    {
        try
        {
            if (!File.Exists(SnapshotPath))
                return null;

            string json = File.ReadAllText(SnapshotPath);
            var snap = JsonSerializer.Deserialize(json, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
            if (snap == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 磁盘幕初快照解析为 null，已清理");
                Delete();
                return null;
            }

            if (currentRunStart != 0 && snap.StartTime != 0 && snap.StartTime != currentRunStart)
            {
                Entry.Logger?.Info($"[QuickRestart] 磁盘幕初快照属于上一局（快照局起始={snap.StartTime}，当前={currentRunStart}），已清理");
                Delete();
                return null;
            }
            if (snap.CurrentActIndex != currentActIndex)
            {
                Entry.Logger?.Info($"[QuickRestart] 磁盘幕初快照为第 {snap.CurrentActIndex + 1} 幕（当前第 {currentActIndex + 1} 幕），忽略");
                return null;
            }

            return snap;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 读取磁盘幕初快照失败（已清理，不影响功能）: {ex.Message}");
            Delete();
            return null;
        }
    }

    /// <summary>删除磁盘快照（新局/重启本局时调用；失败不致命）。</summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(SnapshotPath))
                File.Delete(SnapshotPath);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 清理磁盘幕初快照失败: {ex.Message}");
        }
    }
}
