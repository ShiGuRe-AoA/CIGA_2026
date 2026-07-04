using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 统一的推动上下文。
///
/// 负责：
/// 1. 扫描连续推动链；
/// 2. 收集手抓取的跟随物；
/// 3. 验证地图、占用和心距离；
/// 4. 从链尾向链头执行推动；
/// 5. 处理箱子进入坑洞等特殊格子的结算。
/// </summary>
public class PushContext
{
    private const int MaxPushChainLength = 100;

    private readonly OrganController controller;
    private readonly GridPositionIndex posIndex;

    private readonly List<PushableObject> chain = new();
    private readonly List<(OrganUnit hand, PushableObject grabbed)> grabbedFollowers = new();
    private readonly HashSet<PushableObject> allMoved = new();

    public IReadOnlyList<PushableObject> Chain => chain;
    public bool HasChain => chain.Count > 0;

    public PushContext(OrganController controller, GridPositionIndex posIndex)
    {
        this.controller = controller;
        this.posIndex = posIndex;
    }

    // ─────────── 完整移动入口 ───────────

    public bool TryMoveWithPush(
        PushableObject movingObject,
        Vector3Int dir,
        bool canPush,
        bool bypassHeart = false,
        Ease ease = Ease.InOutQuad,
        float? duration = null)
    {
        if (movingObject == null) return false;
        if (!IsCardinalDirection(dir)) return false;

        MapGrid grid = controller?.MapGrid;
        if (grid == null || posIndex == null) return false;

        Vector3Int targetPos = movingObject.GridPos + dir;

        // 地形检查（坑洞等）
        if (!grid.CanPushInto(movingObject, targetPos))
        {
            grid.NotifyBlocked(targetPos);
            return false;
        }

        // 心距离检查
        if (!bypassHeart &&
            movingObject is OrganUnit movingOrgan &&
            !movingOrgan.IsWithinHeartRange(targetPos))
        {
            return false;
        }

        PushableObject occupier = posIndex.GetAt(targetPos, movingObject);

        if (occupier != null)
        {
            if (!canPush) return false;

            if (!CanPush(occupier.GridPos, dir, bypassHeart))
                return false;

            Execute(dir, ease, duration);
        }

        // 推动链执行后目标格应已腾空
        PushableObject remaining = posIndex.GetAt(targetPos, movingObject);
        if (remaining != null) return false;

        movingObject.MoveTo(targetPos, ease, duration);

        // 箱子入坑等特殊结算
        ResolveSpecialCellEntry(movingObject, targetPos);

        // 手抓取物跟随
        MoveGrabbedIfHandler(movingObject, dir, ease, duration);

        return true;
    }

    // ─────────── 扫描 ───────────

    public void ScanChain(Vector3Int startPos, Vector3Int dir)
    {
        chain.Clear();
        grabbedFollowers.Clear();
        allMoved.Clear();

        Vector3Int checkPos = startPos;

        while (chain.Count < MaxPushChainLength)
        {
            PushableObject obj = posIndex.GetAt(checkPos);
            if (obj == null) break;
            if (chain.Contains(obj)) break;

            if (IsGrabbedByHandInChain(obj))
            {
                checkPos += dir;
                continue;
            }

            chain.Add(obj);
            allMoved.Add(obj);
            checkPos += dir;

            if (obj is not OrganUnit hand) continue;
            if (hand.OrganType != OrganType.Hand || !hand.IsGrabbing) continue;

            foreach (PushableObject grabbed in hand.GrabbedTargets)
            {
                if (grabbed == null) continue;
                if (allMoved.Contains(grabbed)) continue;
                grabbedFollowers.Add((hand, grabbed));
                allMoved.Add(grabbed);
            }
        }
    }

    private bool IsGrabbedByHandInChain(PushableObject obj)
    {
        foreach (PushableObject c in chain)
        {
            if (c is not OrganUnit hand) continue;
            if (hand.OrganType != OrganType.Hand) continue;
            if (hand.GrabbedTargets.Contains(obj)) return true;
        }
        return false;
    }

    // ─────────── 验证 ───────────

    public bool Validate(Vector3Int dir, bool bypassHeart = false)
    {
        if (chain.Count == 0) return false;
        if (!IsCardinalDirection(dir)) return false;

        MapGrid grid = controller?.MapGrid;
        if (grid == null) return false;

        // 1. 链尾地形
        PushableObject chainEnd = chain[chain.Count - 1];
        Vector3Int chainEndTarget = chainEnd.GridPos + dir;
        if (!grid.CanPushInto(chainEnd, chainEndTarget))
        {
            grid.NotifyBlocked(chainEndTarget);
            return false;
        }

        // 2. 占用检查
        foreach (PushableObject pushed in chain)
        {
            Vector3Int targetPos = pushed.GridPos + dir;
            PushableObject occ = posIndex.GetAt(targetPos);
            if (occ != null && !allMoved.Contains(occ)) return false;
        }

        // 3. 心距离
        if (!bypassHeart)
        {
            foreach (PushableObject pushed in chain)
            {
                if (pushed is not OrganUnit organ) continue;
                if (!organ.IsWithinHeartRange(organ.GridPos + dir)) return false;
            }
        }

        // 4. 抓取跟随物
        foreach (var (_, grabbed) in grabbedFollowers)
        {
            if (grabbed == null) return false;

            Vector3Int targetPos = grabbed.GridPos + dir;
            if (!grid.IsWalkable(targetPos)) return false;

            PushableObject occ = posIndex.GetAt(targetPos);
            if (occ != null && !allMoved.Contains(occ)) return false;

            if (!bypassHeart && grabbed is OrganUnit gOrgan &&
                !gOrgan.IsWithinHeartRange(targetPos)) return false;
        }

        return true;
    }

    public bool CanPush(Vector3Int startPos, Vector3Int dir, bool bypassHeart = false)
    {
        ScanChain(startPos, dir);
        return Validate(dir, bypassHeart);
    }

    // ─────────── 执行 ───────────

    public void Execute(Vector3Int dir, Ease ease = Ease.OutQuad, float? duration = null)
    {
        if (chain.Count == 0) return;

        PushableObject chainEnd = chain[chain.Count - 1];
        Vector3Int chainEndTarget = chainEnd.GridPos + dir;

        for (int i = chain.Count - 1; i >= 0; i--)
            chain[i].ApplyPush(dir, ease, duration);

        foreach (var (_, grabbed) in grabbedFollowers)
        {
            if (grabbed == null) continue;
            grabbed.MoveTo(grabbed.GridPos + dir, ease, duration);
        }

        ResolveSpecialCellEntry(chainEnd, chainEndTarget);
    }

    // ─────────── 特殊格子结算 ───────────

    private void ResolveSpecialCellEntry(PushableObject pushable, Vector3Int cellPos)
    {
        if (pushable == null) return;

        MapGrid grid = controller?.MapGrid;
        if (grid == null) return;

        bool consumed = grid.ResolvePushableEnteredCell(pushable, cellPos);
        if (!consumed) return;

        posIndex.Unregister(pushable);
        pushable.gameObject.SetActive(false);
    }

    // ─────────── 抓取跟随 ───────────

    private void MoveGrabbedIfHandler(PushableObject obj, Vector3Int dir, Ease ease, float? duration)
    {
        if (obj is not OrganUnit hand || hand.OrganType != OrganType.Hand || !hand.IsGrabbing)
            return;

        var grid = controller.MapGrid;
        if (grid == null) return;

        foreach (var grabbed in hand.GrabbedTargets)
        {
            if (grabbed == null) continue;
            if (allMoved.Contains(grabbed)) continue;

            Vector3Int grabbedTarget = grabbed.GridPos + dir;
            if (!grid.IsWalkable(grabbedTarget)) continue;

            PushableObject blocker = posIndex.GetAt(grabbedTarget, grabbed);
            if (blocker != null && !allMoved.Contains(blocker)) continue;

            grabbed.MoveTo(grabbedTarget, ease, duration);
        }
    }

    // ─────────── 工具 ───────────

    private static bool IsCardinalDirection(Vector3Int dir)
    {
        if (dir.z != 0) return false;
        int length = Mathf.Abs(dir.x) + Mathf.Abs(dir.y);
        return length == 1;
    }
}
