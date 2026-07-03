using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 统一的推动上下文。
/// 将推进链扫描、抓取跟随、验证和执行合并为一个事务性流程，
/// 消除原 OrganController 中 CollectPushChain / TryPushChain / ValidateGrabbedInChain / MoveGrabbedAlongChain 的分散与重复逻辑。
///
/// 使用方式：
///   var ctx = new PushContext(controller, posIndex);
///   ctx.ScanChain(firstBlockedPos, dir);
///   if (!ctx.CanExecute(dir)) return;
///   ctx.Execute(dir);
/// </summary>
public class PushContext
{
    private readonly OrganController controller;
    private readonly GridPositionIndex posIndex;

    // 沿推动方向顺序排列的物体链（离推动源最近的在前面）
    private readonly List<PushableObject> chain = new();
    // 跟随链中抓取手移动的物体（不在链内）
    private readonly List<(OrganUnit hand, PushableObject grabbed)> grabbedFollowers = new();
    // 已验证不会重复移动的物体
    private readonly HashSet<PushableObject> allMoved = new();

    /// <summary>推动链中的物体列表（扫描后才有效）。</summary>
    public IReadOnlyList<PushableObject> Chain => chain;

    /// <summary>是否扫描到了有效链。</summary>
    public bool HasChain => chain.Count > 0;

    public PushContext(OrganController controller, GridPositionIndex posIndex)
    {
        this.controller = controller;
        this.posIndex = posIndex;
    }

    // ─────────── 扫描 ───────────

    /// <summary>
    /// 从 firstBlockedPos 开始沿 dir 方向扫描推进链。
    /// 链条中手抓取的物体会自动收集为跟随者而非链成员。
    /// </summary>
    public void ScanChain(Vector3Int startPos, Vector3Int dir)
    {
        chain.Clear();
        grabbedFollowers.Clear();
        allMoved.Clear();
        Vector3Int checkPos = startPos;

        while (chain.Count < 100)
        {
            PushableObject obj = posIndex.GetAt(checkPos);
            if (obj == null) break;
            if (chain.Contains(obj)) break;

            // 若这个物体正被链中某只手抓取 → 它属于跟随者，不是链成员
            if (IsGrabbedByHandInChain(obj))
            {
                checkPos += dir;
                continue;
            }

            chain.Add(obj);
            allMoved.Add(obj);
            checkPos += dir;

            // 若新加入链的物体是手且有抓取物，记录为跟随者
            if (obj is OrganUnit hand && hand.OrganType == OrganType.Hand && hand.IsGrabbing)
            {
                foreach (var grabbed in hand.GrabbedTargets)
                {
                    if (!allMoved.Contains(grabbed))
                    {
                        grabbedFollowers.Add((hand, grabbed));
                        allMoved.Add(grabbed);
                    }
                }
            }
        }
    }

    /// <summary>检查 obj 是否正被已扫描到的某只手抓取。</summary>
    private bool IsGrabbedByHandInChain(PushableObject obj)
    {
        foreach (var c in chain)
        {
            if (c is OrganUnit hand && hand.OrganType == OrganType.Hand
                && hand.GrabbedTargets.Contains(obj))
                return true;
        }
        return false;
    }

    // ─────────── 验证 ───────────

    /// <summary>
    /// 验证整条推动链是否合法。
    /// 检查：链终点为空地、链内物体可被推动（心距离等）、抓取跟随者目标为空地。
    /// </summary>
    /// <param name="dir">推动方向</param>
    /// <param name="bypassHeart">true 时跳过心距离检查（蓄力踢专用）</param>
    public bool Validate(Vector3Int dir, bool bypassHeart = false)
    {
        if (chain.Count == 0) return false;

        var grid = controller.MapGrid;
        if (grid == null) return false;

        // 1. 链终点必须可行走
        Vector3Int chainEndPos = chain[chain.Count - 1].GridPos + dir;
        if (!grid.IsWalkable(chainEndPos))
        {
            grid.NotifyBlocked(chainEndPos);
            return false;
        }

        // 2. 链内每个物体的目标必须可行走（不能被链外物体占据）
        foreach (var pushed in chain)
        {
            Vector3Int newPos = pushed.GridPos + dir;
            PushableObject occupier = posIndex.GetAt(newPos);
            if (occupier != null && !allMoved.Contains(occupier))
                return false;
        }

        // 3. 心距离检查
        if (!bypassHeart)
        {
            foreach (var pushed in chain)
            {
                if (pushed is OrganUnit organ && !organ.IsWithinHeartRange(pushed.GridPos + dir))
                    return false;
            }
        }

        // 4. 抓取跟随者目标检查
        foreach (var (hand, grabbed) in grabbedFollowers)
        {
            Vector3Int newPos = grabbed.GridPos + dir;
            if (!grid.IsWalkable(newPos)) return false;
            PushableObject occupier = posIndex.GetAt(newPos);
            if (occupier != null && !allMoved.Contains(occupier)) return false;
        }

        return true;
    }

    /// <summary>
    /// 简便方法：扫描 + 验证一步完成。
    /// </summary>
    public bool CanPush(Vector3Int startPos, Vector3Int dir, bool bypassHeart = false)
    {
        ScanChain(startPos, dir);
        return Validate(dir, bypassHeart);
    }

    // ─────────── 执行 ───────────

    /// <summary>
    /// 执行推动：从链尾向链头倒序推动，然后移动抓取跟随者。
    /// 使用先前 ScanChain 收集的链。
    /// </summary>
    public void Execute(Vector3Int dir)
    {
        // 1. 从链尾向链头推动（避免前方物体覆盖后方位置）
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            chain[i].ApplyPush(dir);
        }

        // 2. 抓取跟随者同步移动
        foreach (var (_, grabbed) in grabbedFollowers)
        {
            grabbed.MoveTo(grabbed.GridPos + dir);
        }
    }
}
