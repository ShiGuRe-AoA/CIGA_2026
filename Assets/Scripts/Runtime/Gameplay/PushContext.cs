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

        if (!BuildMovePlan(movingObject, dir, canPush, bypassHeart, grid))
            return false;

        Execute(dir, ease, duration);
        return true;
    }

    // ─────────── 规划 ───────────

    private bool BuildMovePlan(
        PushableObject root,
        Vector3Int dir,
        bool canPush,
        bool bypassHeart,
        MapGrid grid)
    {
        chain.Clear();
        allMoved.Clear();

        if (!AddConnectedObject(root))
            return false;

        bool changed;
        int safety = 0;

        do
        {
            changed = false;

            for (int i = 0; i < chain.Count; i++)
            {
                PushableObject obj = chain[i];
                if (obj == null)
                    return false;

                Vector3Int targetPos = obj.GridPos + dir;

                if (!grid.CanPushInto(obj, targetPos))
                {
                    grid.NotifyBlocked(targetPos);
                    return false;
                }

                if (!bypassHeart &&
                    obj is OrganUnit organ &&
                    !IsWithinPlannedHeartRange(organ, targetPos, dir))
                {
                    return false;
                }

                PushableObject occupier = posIndex.GetAt(targetPos, obj);
                if (occupier == null || allMoved.Contains(occupier))
                    continue;

                if (!canPush)
                    return false;

                if (!AddConnectedObject(occupier))
                    return false;

                changed = true;
            }
        }
        while (changed && ++safety < MaxPushChainLength);

        return safety < MaxPushChainLength;
    }

    private bool AddConnectedObject(PushableObject obj)
    {
        if (obj == null)
            return false;

        if (allMoved.Contains(obj))
            return true;

        if (chain.Count >= MaxPushChainLength)
            return false;

        allMoved.Add(obj);
        chain.Add(obj);

        return AddGrabTargetsIfHand(obj) &&
               AddHandsGrabbingObject(obj);
    }

    private bool AddGrabTargetsIfHand(PushableObject obj)
    {
        if (obj is not OrganUnit hand ||
            hand.OrganType != OrganType.Hand ||
            !hand.IsGrabbing)
        {
            return true;
        }

        foreach (PushableObject grabbed in hand.GrabbedTargets)
        {
            if (grabbed != null && !AddConnectedObject(grabbed))
                return false;
        }

        return true;
    }

    private bool AddHandsGrabbingObject(PushableObject obj)
    {
        foreach (PushableObject candidate in posIndex.AllObjects)
        {
            if (candidate is not OrganUnit hand)
                continue;

            if (hand.OrganType != OrganType.Hand ||
                !hand.IsGrabbing ||
                !hand.GrabbedTargets.Contains(obj))
            {
                continue;
            }

            if (!AddConnectedObject(hand))
                return false;
        }

        return true;
    }

    // ─────────── 执行 ───────────

    public void Execute(Vector3Int dir, Ease ease = Ease.OutQuad, float? duration = null)
    {
        if (chain.Count == 0) return;

        chain.Sort(
            (a, b) => GetDirectionOrder(b, dir)
                .CompareTo(GetDirectionOrder(a, dir))
        );

        var movedObjects = new List<PushableObject>(chain);

        foreach (PushableObject obj in movedObjects)
            obj.MoveTo(obj.GridPos + dir, ease, duration);

        foreach (PushableObject obj in movedObjects)
            ResolveSpecialCellEntry(obj, obj.GridPos);
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

    // ─────────── 工具 ───────────

    private static int GetDirectionOrder(PushableObject obj, Vector3Int dir)
    {
        Vector3Int pos = obj != null ? obj.GridPos : Vector3Int.zero;
        return pos.x * dir.x + pos.y * dir.y;
    }

    private bool IsWithinPlannedHeartRange(
        OrganUnit organ,
        Vector3Int targetPos,
        Vector3Int dir)
    {
        if (organ == null || organ.OrganType == OrganType.Heart)
            return true;

        OrganUnit heart = controller?.HeartUnit;
        int maxDist = organ.MaxHeartDistance;
        if (heart == null || maxDist <= 0)
            return true;

        Vector3Int heartPos = allMoved.Contains(heart)
            ? heart.GridPos + dir
            : heart.GridPos;

        int dist = Mathf.Abs(targetPos.x - heartPos.x) +
                   Mathf.Abs(targetPos.y - heartPos.y);

        return dist <= maxDist;
    }

    private static bool IsCardinalDirection(Vector3Int dir)
    {
        if (dir.z != 0) return false;
        int length = Mathf.Abs(dir.x) + Mathf.Abs(dir.y);
        return length == 1;
    }
}
