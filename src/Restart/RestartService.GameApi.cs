using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace QuickRestart;

/// <summary>
/// 游戏内部状态的最小封装：ShouldSave 反射写、解除战斗暂停、关掉暂停菜单。
/// </summary>
internal static partial class RestartService
{
    /// <summary>
    /// 反射设置 RunManager.ShouldSave。
    /// ⚠️ 不能直接赋值：Krafs.Publicizer 只改**编译期**可见性 —— 直调私有 setter
    /// 编译得通过、运行期抛 MethodAccessException。反射调用私有 setter 则始终可用，
    /// 且本方法自身不抛异常。
    /// </summary>
    private static bool TrySetShouldSave(bool value)
    {
        try
        {
            PropertyInfo? prop = typeof(RunManager).GetProperty("ShouldSave",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetSetMethod(nonPublic: true) == null)
            {
                Entry.Logger?.Warn("[QuickRestart] 未找到 RunManager.ShouldSave 的 setter");
                return false;
            }
            prop.SetValue(RunManager.Instance, value);
            return true;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 设置 ShouldSave={value} 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 解除战斗暂停。刀下留人时会调用 CombatManager.Pause() 冻结战斗（弹窗选择期间），
    /// 解除前必须确保玩家已脱离 HP=0 状态（否则 Unpause 后死亡检测立即再次触发）。
    /// 非暂停时为无操作，可安全重复调用。
    /// <para>供 <see cref="DeathInterceptPatch"/> 共用：弹窗"未被选择就被关闭"时也必须解除暂停，
    /// 否则战斗永久冻结（玩家已复活 → 不会再产生死亡调用 → 没有第二次机会解除）。</para>
    /// </summary>
    internal static void EnsureCombatUnpaused()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm != null && cm.IsPaused)
            {
                cm.Unpause();
                Entry.Logger?.Info("[QuickRestart] 已解除战斗暂停（Unpause）");
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 解除战斗暂停失败: {ex.Message}");
        }
    }

    private static void ClosePauseMenu()
    {
        try
        {
            var capstone = MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance;
            if (capstone != null)
            {
                capstone.Close();
            }
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 关闭暂停菜单失败: {ex.Message}");
        }
    }
}
