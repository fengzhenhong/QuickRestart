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
    /// 读回磁盘上的幕初快照。**局标识必须双方可用且相等**（任一为 0 即拒绝）+ 幕号一致 ——
    /// 防止把别的局的快照当本局幕初用（那会把当前局回滚成另一局的卡组/金币/遗物）；
    /// 种子作为第二道判据（两边都取得到且不同 → 拒绝）。旧局残留顺带清理（不占空间）。
    /// 任何异常都按"无快照"处理。
    /// </summary>
    public static SerializableRun? TryLoad(long currentRunStart, int currentActIndex, string? currentSeed)
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

            // ⚠️ 局标识不可用时**绝不**采用磁盘快照：`StartTime` 取自 RunManager._startTime 反射，
            //    该字段一旦被游戏重命名/改类型就会恒为 0（v0.1.4 前真实发生过）。此时若只比幕号，
            //    上一局同幕号的快照会被当成本局幕初 → 重启本层把当前局改成另一局的状态。
            //    宁可降级为"只能回到读档点"（功能变弱但绝不会改错状态）。
            if (currentRunStart == 0 || snap.StartTime == 0)
            {
                Entry.Logger?.Warn(
                    $"[QuickRestart] 局标识不可用（当前={currentRunStart}，磁盘快照={snap.StartTime}），"
                    + "拒绝采用磁盘幕初快照（重启本层最多回到当前状态）");
                return null;
            }
            if (snap.StartTime != currentRunStart)
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

            string? snapSeed = snap.SerializableRng?.Seed;
            if (!string.IsNullOrEmpty(snapSeed) && !string.IsNullOrEmpty(currentSeed) && snapSeed != currentSeed)
            {
                Entry.Logger?.Warn($"[QuickRestart] 磁盘幕初快照种子与当前局不符（快照={snapSeed}，当前={currentSeed}），已清理");
                Delete();
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
