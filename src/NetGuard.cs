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
/// <para>死亡拦截按同一判据处理：队友死亡与本地玩家无关，不该弹"刀下留人"；
/// 而即便取对本地玩家，联机回滚同样不可行，故联机下整体禁用。</para>
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

    /// <summary>
    /// 当前是否为**每日挑战局**（<c>GameMode.Daily</c>）：本 mod 的重启/回滚一律拒绝。
    ///
    /// <para>原因：① 每日局的结算依据 <c>RunManager.DailyTime</c> 是 <c>StartNewSingleplayerRun</c>
    /// 的独立参数（默认 null），而它不在 <c>RunState</c> 里 —— 用"重启本局"重开后会得到
    /// 「模式仍是 Daily、但 DailyTime 为空」的混合态，排行与分数结算都会错位；
    /// ② 「重启新局」等于重 roll 每日种子，本身就是每日规则不允许的；
    /// ③ 反复回滚 = 用同一份每日种子无限重试，与每日挑战"一次机会"的定义冲突。</para>
    ///
    /// <para>普通局（Standard）与自定义局（Custom）不受影响 —— 前者是设计目标，
    /// 后者的无痕行为由玩家自己承担。取不到状态时按"非每日"处理（各入口仍有自己的状态守卫）。</para>
    /// </summary>
    public static bool IsDailyRun()
    {
        try
        {
            return RunManager.Instance.DebugOnlyGetState()?.GameMode == GameMode.Daily;
        }
        catch
        {
            return false;
        }
    }
}
