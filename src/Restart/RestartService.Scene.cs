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
/// 过场与计时：黑幕淡入淡出（任何异常都不能漏掉淡入）、等渲染帧、分段耗时日志。
/// </summary>
internal static partial class RestartService
{
    /// <summary>直接路径的黑幕时长（秒）。游戏的淡入淡出默认各 0.8s，这里压到 0.22s；想调只改这一个常量。</summary>
    private const float DirectFadeSec = 0.22f;

    /// <summary>
    /// 等待淡出任务完成并观察其异常（绝不抛出）—— 防止孤儿任务停留在后台，
    /// 与回退路径的转场调用并发操作同一 NTransition 节点（阈值/MouseFilter 互相覆盖）。
    /// </summary>
    private static async Task AwaitFadeSafeAsync(Task fadeTask, string tag)
    {
        try
        {
            await fadeTask;
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 转场淡出等待异常（{tag}）: {ex.Message}");
        }
    }

    /// <summary>分段耗时日志（用于确认直接路径的收益；t0 = 起点 GetTicksMsec）。</summary>
    private static void LogStep(string tag, long startMs, string name)
    {
        Entry.Logger?.Info($"[QuickRestart] ⏱ {tag} · {name} = {(long)Time.GetTicksMsec() - startMs}ms");
    }

    /// <summary>
    /// 直接路径收尾：恢复全屏转场（去掉黑幕）。
    /// ⚠️ LoadRun / StartNewSingleplayerRun **自身不负责淡入**，调用点都在之后手动
    /// <c>Transition.FadeIn()</c>（继续游戏即 FadeOut → LoadRun → FadeIn）。
    /// 走主菜单时由主菜单场景自己 FadeIn；跳过主菜单就必须由我们补齐，
    /// 否则全屏黑幕（Transition 的 threshold=1）永不恢复 = 一直黑屏。
    /// 失败只记警告：读档本身已成功，不回退。
    /// </summary>
    private static async Task FadeInSafeAsync(string tag)
    {
        try
        {
            // 与游戏淡入重叠：先收起我方遮罩（0.15s 淡出）再启动 FadeIn（0.22s），
            // 两个动画并行 → 省掉遮罩收尾的额外黑屏时间，观感也更连贯。
            RestartOverlay.Hide();
            await NGame.Instance!.Transition.FadeIn(DirectFadeSec);
        }
        catch (Exception ex)
        {
            Entry.Logger?.Warn($"[QuickRestart] 恢复转场（FadeIn）失败（{tag}）: {ex.Message}");
        }
    }

    /// <summary>等待 N 个渲染帧（主线程）。场景切换（回主菜单 → 开新局）之间需要留帧，
    /// 同一帧内连续切换会导致场景容器（NSceneContainer.SetCurrentScene）状态错乱、黑屏。</summary>
    private static async Task WaitFrames(int count)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree)
                return;
            for (int i = 0; i < count; i++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        catch
        {
            // 等帧失败不致命，继续流程
        }
    }
}
