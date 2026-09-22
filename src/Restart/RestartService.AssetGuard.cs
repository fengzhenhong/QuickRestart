using Godot;

namespace QuickRestart;

/// <summary>
/// 建 run 场景前的**资源可用性保证**。
///
/// <para>背景 —— 为什么不能只"等两帧"：</para>
/// <para>直接路径（跳过主菜单）里，<c>PreloadManager.LoadRunAssets</c> / <c>LoadActAssets</c> 会调用
/// <c>AssetCache.UnloadMissedCacheAssets()</c> 与 <c>UnloadAssets(...)</c>，把"本轮预载不再需要"的资源
/// 从缓存里摘掉，并对每个资源排一个 <c>CallDeferred(resource.Dispose)</c>。而游戏自己的
/// <c>NInputSettingsEntry.Create</c>（run 场景里 <c>NSettingsScreen → NInputSettingsPanel._Ready</c> 会同步调用）
/// 用的是裸 <c>ResourceLoader.Load&lt;PackedScene&gt;("res://scenes/screens/settings_screen/input_settings_entry.tscn", …)</c>，
/// 且该场景**没有被并进任何预载集合**（CommonAssets / RunSet / Act 里都没有它）
/// —— 于是它正好处在"被摘掉 + 排上延迟释放"的名单里。</para>
///
/// <para>真机日志（2026-09-22）显示崩溃点紧跟在
/// <c>Preloading 'characters=…'</c> / <c>Preloading 'Act=…'</c> 之后、**同一批续体里**就发生了，
/// 中间没有任何帧间隔 —— 所以那个延迟 Dispose 与随后的同步 Load 撞在了一起：
/// <c>Instantiate</c> 在一份空 <c>SceneState</c> 上读越界，Godot 在原生层直接
/// <c>FATAL: Index p_index = 9 is out of bounds (size() = 0)</c> 终止进程 —— 连 C# 异常都来不及抛。
/// 之前补的 <c>WaitFrames(2)</c> 只是把这个窗口"大概率"错开，日志证明它没兜住（按 F 重启本层仍然崩）。</para>
///
/// <para>于是切场景时若那个延迟 Dispose 已经落地而资源又没被重新读回，<c>Instantiate</c> 会在一份空
/// <c>SceneState</c> 上读越界，Godot 在原生层直接
/// <c>FATAL: Index p_index = 9 is out of bounds (size() = 0)</c> 终止进程 —— 连 C# 异常都来不及抛。
/// 之前补的 <c>WaitFrames(2)</c> 只是把这个窗口"大概率"错开，v0.1.7 真机日志证明它没兜住
/// （2026-09-22 按 F 重启本层仍然崩）。</para>
///
/// <para>这里的做法是**把"大概率"换成"确定"**：等延迟释放落地后，用
/// <c>PackedScene.CanInstantiate()</c> 对关键场景做一次**不会崩**的健康校验——
/// 通过则说明后续游戏自己的同步加载一定能拿到健康资源；不通过就当作直接路径失败，
/// 交由上层回退到官方主菜单路径（那条路天然隔着菜单驻留帧，不会踩这个竞态）。
/// <b>宁可慢一次，也不要静默崩进程。</b></para>
/// </summary>
internal static partial class RestartService
{
    /// <summary>
    /// 建 run 场景时会被**同步加载**、且不在任何预载集合里（因此会被 <c>UnloadMissedCacheAssets</c> 摘掉）
    /// 的关键场景。清单来自对 <c>NInputSettingsPanel._Ready</c> 调用链的核对；
    /// 若将来游戏新增同类同步加载点，崩溃日志的栈顶就是新条目该加的地方。
    /// </summary>
    private static readonly string[] BarelyLoadedScenes =
    [
        "res://scenes/screens/settings_screen/input_settings_entry.tscn",
    ];

    /// <summary>
    /// 确保 <see cref="BarelyLoadedScenes"/> 里的场景当前都可用（加载得到、且能安全实例化）。
    /// 返回 <c>true</c> = 可以安全建 run 场景；<c>false</c> = 资源不可用，调用方必须回退主菜单路径。
    /// </summary>
    private static async Task<bool> EnsureCriticalScenesUsableAsync(string tag)
    {
        // 让上一批 CallDeferred 的 Dispose 真正落地，我们才是在**确定的终态**上做校验。
        // 这几帧不是"赌时序"，而是为了让被检验的对象稳定下来 —— 真正的防线是下面的校验。
        await WaitFrames(2);

        foreach (string path in BarelyLoadedScenes)
        {
            try
            {
                // CacheMode.Reuse：缓存里有健康实例就复用，没有才真正去读 pck。
                // ★ 刻意不用 Replace/ReplaceDeep：那条路会强制 Dispose 掉现存实例，
                //   而该场景的实例可能正被上一份场景引用着 —— 为了修一个竞态去强拆别人的引用，
                //   是拿一种崩溃换另一种。Reuse 只"补"不"拆"，是最小侵入。
                var res = ResourceLoader.Load<Resource>(path, null, ResourceLoader.CacheMode.Reuse);
                if (res is not PackedScene packed || !GodotObject.IsInstanceValid(packed))
                {
                    Entry.Logger?.Error($"[QuickRestart] {tag}：关键场景不可用（加载结果={res?.GetType().Name ?? "null"}）path={path} → 回退主菜单路径");
                    return false;
                }

                // ★★ 关键一步：用 CanInstantiate() 做**不会崩**的健康检查。
                //    它内部就是判断 SceneState 是否可用，正好等价于"Instantiate 会不会在原生层读越界"，
                //    但它只返回 bool，不会真的去建节点 —— 所以哪怕资源是坏的，我们也只是拿到 false，
                //    不会像直接调 Instantiate 那样把进程带走。
                //    这正是旧版 WaitFrames 缺失的那道"确定性判据"。
                if (!packed.CanInstantiate())
                {
                    Entry.Logger?.Error($"[QuickRestart] {tag}：关键场景的 SceneState 不可用（CanInstantiate=false）path={path} → 回退主菜单路径");
                    return false;
                }

                // 再看一眼状态里是否有实际内容：空状态正是 cowdata 越界的成因。
                SceneState? state = packed.GetState();
                if (state == null || state.GetNodeCount() == 0)
                {
                    Entry.Logger?.Error($"[QuickRestart] {tag}：关键场景的 SceneState 为空（nodeCount={(state == null ? "null" : "0")}）path={path} → 回退主菜单路径");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Entry.Logger?.Error($"[QuickRestart] {tag}：关键场景预检异常 path={path} → 回退主菜单路径: {ex.Message}");
                return false;
            }
        }

        // ★ 通过时也要留一行日志：v0.1.7 的教训正是"这道防线到底有没有生效"在日志里查不到，
        //   只能靠"有没有崩"反推。写一行就能把"守卫通过"与"守卫根本没跑"区分开。
        Entry.Logger?.Info($"[QuickRestart] {tag}：关键场景预检通过（{BarelyLoadedScenes.Length} 项），继续快速路径");
        return true;
    }
}
