using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace QuickRestart;

/// <summary>
/// 联机模式守卫：本 mod 的"回滚 / 读档 / 重启"类功能在**联机对局中不可用**。
///
/// <para>原因（为什么不是"适配联机"）：这些功能通过官方的单机读档流程改写**本地**对局状态，
/// 而联机对局由输入同步 + 校验和分歧检测（<c>RunManager.ChecksumTracker</c>）保护 ——
/// 本地状态单方面"回退"必然被判定为分歧（StateDiverged），轻则不同步、重则掉线报错。
/// 游戏自身也没有"把回滚同步给所有玩家"的协议。<b>因此联机下统一拒绝执行并提示</b>，
/// 绝不冒险改写状态。</para>
///
/// <para>触发背景（2026-09-15 玩家反馈）：联机模式下队友死亡也弹"刀下留人"—— 经查死亡拦截
/// 取的是"玩家列表第一个"而非本地玩家；且即便取对，联机回滚同样不可行，故一并禁用。</para>
/// </summary>
internal static class NetGuard
{
    /// <summary>
    /// 当前是否为联机对局（host 或 client）。安全：NetService 尚未初始化 / 非对局中均返回 false，
    /// 任何异常也按"非联机"处理（各功能入口仍有各自的状态守卫，不依赖本方法做唯一防线）。
    /// </summary>
    public static bool IsMultiplayer()
    {
        try
        {
            var net = RunManager.Instance?.NetService;
            return net != null && net.Type.IsMultiplayer();
        }
        catch
        {
            return false;
        }
    }
}
