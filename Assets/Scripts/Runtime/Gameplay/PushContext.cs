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

    /// <summary>
    /// 沿推动方向排列的推动链。
    /// 离推动源最近的物体位于列表前面。
    /// </summary>
    private readonly List<PushableObject> chain =
        new List<PushableObject>();

    /// <summary>
    /// 推动链中的手所抓取的跟随物。
    /// 跟随物不重复作为推动链成员。
    /// </summary>
    private readonly List<(
        OrganUnit hand,
        PushableObject grabbed
    )> grabbedFollowers =
        new List<(OrganUnit, PushableObject)>();

    /// <summary>
    /// 当前事务中会发生移动的全部物体。
    /// 用于区分链内占用和链外阻挡。
    /// </summary>
    private readonly HashSet<PushableObject> allMoved =
        new HashSet<PushableObject>();

    /// <summary>当前扫描得到的推动链。</summary>
    public IReadOnlyList<PushableObject> Chain => chain;

    /// <summary>当前是否扫描到了推动链。</summary>
    public bool HasChain => chain.Count > 0;

    public PushContext(
        OrganController controller,
        GridPositionIndex posIndex)
    {
        this.controller = controller;
        this.posIndex = posIndex;
    }

    // ─────────── 完整移动入口 ───────────

    /// <summary>
    /// 尝试将指定物体沿方向移动一格。
    ///
    /// 如果目标格被其他物体占据且 canPush 为 true，
    /// 则先推动目标格前方的完整推动链。
    /// </summary>
    public bool TryMoveWithPush(
        PushableObject movingObject,
        Vector3Int dir,
        bool canPush,
        bool bypassHeart = false,
        Ease ease = Ease.InOutQuad,
        float? duration = null)
    {
        if (movingObject == null)
            return false;

        if (!IsCardinalDirection(dir))
            return false;

        MapGrid grid = controller?.MapGrid;

        if (grid == null || posIndex == null)
            return false;

        Vector3Int targetPos =
            movingObject.GridPos + dir;

        // 检查当前移动物能否进入目标地形。
        // 普通器官不能进入坑；实现 IPitFiller 的箱子可以进入未填平坑。
        if (!grid.CanPushInto(
                movingObject,
                targetPos))
        {
            grid.NotifyBlocked(targetPos);
            return false;
        }

        if (!bypassHeart &&
            movingObject is OrganUnit movingOrgan &&
            !movingOrgan.IsWithinHeartRange(targetPos))
        {
            return false;
        }

        PushableObject occupier =
            posIndex.GetAt(
                targetPos,
                movingObject
            );

        if (occupier != null)
        {
            if (!canPush)
                return false;

            if (!CanPush(
                    occupier.GridPos,
                    dir,
                    bypassHeart))
            {
                return false;
            }

            Execute(dir, ease, duration);
        }

        // 推动链执行后，目标格应当已经腾空。
        PushableObject remainingOccupier =
            posIndex.GetAt(
                targetPos,
                movingObject
            );

        if (remainingOccupier != null)
            return false;

        movingObject.MoveTo(targetPos, ease, duration);

        // movingObject 本身也可能是被蓄力踢直接推入坑中的箱子。
        ResolveSpecialCellEntry(
            movingObject,
            targetPos
        );

        // 抓取跟随：如果移动方是手且有抓取物，未在推链中的抓取物一并移动
        MoveGrabbedIfHandler(movingObject, dir, ease, duration);

        return true;
    }

    // ─────────── 扫描 ───────────

    /// <summary>
    /// 从 startPos 开始沿 dir 扫描连续推动链。
    /// </summary>
    public void ScanChain(
        Vector3Int startPos,
        Vector3Int dir)
    {
        chain.Clear();
        grabbedFollowers.Clear();
        allMoved.Clear();

        Vector3Int checkPos = startPos;

        while (chain.Count < MaxPushChainLength)
        {
            PushableObject obj =
                posIndex.GetAt(checkPos);

            if (obj == null)
                break;

            if (chain.Contains(obj))
                break;

            // 如果该物体已经被推动链中的手抓取，
            // 它属于跟随物，不再加入推动链。
            if (IsGrabbedByHandInChain(obj))
            {
                checkPos += dir;
                continue;
            }

            chain.Add(obj);
            allMoved.Add(obj);

            checkPos += dir;

            if (obj is not OrganUnit hand)
                continue;

            if (hand.OrganType != OrganType.Hand ||
                !hand.IsGrabbing)
            {
                continue;
            }

            foreach (PushableObject grabbed
                     in hand.GrabbedTargets)
            {
                if (grabbed == null)
                    continue;

                if (allMoved.Contains(grabbed))
                    continue;

                grabbedFollowers.Add(
                    (hand, grabbed)
                );

                allMoved.Add(grabbed);
            }
        }
    }

    /// <summary>
    /// 判断指定物体是否正被推动链中的手抓取。
    /// </summary>
    private bool IsGrabbedByHandInChain(
        PushableObject obj)
    {
        foreach (PushableObject chainObject in chain)
        {
            if (chainObject is not OrganUnit hand)
                continue;

            if (hand.OrganType != OrganType.Hand)
                continue;

            if (hand.GrabbedTargets.Contains(obj))
                return true;
        }

        return false;
    }

    // ─────────── 验证 ───────────

    /// <summary>
    /// 验证当前推动链是否合法。
    ///
    /// 检查：
    /// 1. 链尾是否能进入目标地形；
    /// 2. 链内物体目标格是否被链外物体阻挡；
    /// 3. 器官是否满足心距离；
    /// 4. 抓取跟随物是否能够同步移动。
    /// </summary>
    public bool Validate(
        Vector3Int dir,
        bool bypassHeart = false)
    {
        if (chain.Count == 0)
            return false;

        if (!IsCardinalDirection(dir))
            return false;

        MapGrid grid = controller?.MapGrid;

        if (grid == null)
            return false;

        // ───── 1. 链尾地形检查 ─────

        PushableObject chainEnd =
            chain[chain.Count - 1];

        Vector3Int chainEndTarget =
            chainEnd.GridPos + dir;

        if (!grid.CanPushInto(
                chainEnd,
                chainEndTarget))
        {
            grid.NotifyBlocked(chainEndTarget);
            return false;
        }

        // ───── 2. 目标格占用检查 ─────

        foreach (PushableObject pushed in chain)
        {
            Vector3Int targetPos =
                pushed.GridPos + dir;

            PushableObject occupier =
                posIndex.GetAt(targetPos);

            if (occupier != null &&
                !allMoved.Contains(occupier))
            {
                return false;
            }
        }

        // ───── 3. 心距离检查 ─────

        if (!bypassHeart)
        {
            foreach (PushableObject pushed in chain)
            {
                if (pushed is not OrganUnit organ)
                    continue;

                Vector3Int targetPos =
                    organ.GridPos + dir;

                if (!organ.IsWithinHeartRange(targetPos))
                    return false;
            }
        }

        // ───── 4. 抓取跟随物检查 ─────

        foreach (var (_, grabbed)
                 in grabbedFollowers)
        {
            if (grabbed == null)
                return false;

            Vector3Int targetPos =
                grabbed.GridPos + dir;

            // 当前规则：手抓取的跟随物不能通过拖拽方式进入坑洞。
            if (!grid.IsWalkable(targetPos))
                return false;

            PushableObject occupier =
                posIndex.GetAt(targetPos);

            if (occupier != null &&
                !allMoved.Contains(occupier))
            {
                return false;
            }

            if (!bypassHeart &&
                grabbed is OrganUnit organ &&
                !organ.IsWithinHeartRange(targetPos))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 扫描并验证推动链。
    /// </summary>
    public bool CanPush(
        Vector3Int startPos,
        Vector3Int dir,
        bool bypassHeart = false)
    {
        ScanChain(startPos, dir);

        return Validate(
            dir,
            bypassHeart
        );
    }

    // ─────────── 执行 ───────────

    /// <summary>
    /// 执行已经扫描并验证通过的推动链。
    ///
    /// 从链尾向链头倒序推动，
    /// 随后移动抓取跟随物，
    /// 最后结算链尾进入的特殊格子。
    /// </summary>
    /// <param name="duration">动画时长，null 时使用物体自身 MoveDuration</param>
    public void Execute(Vector3Int dir, Ease ease = Ease.OutQuad, float? duration = null)
    {
        if (chain.Count == 0)
            return;

        PushableObject chainEnd =
            chain[chain.Count - 1];

        Vector3Int chainEndTarget =
            chainEnd.GridPos + dir;

        // 从链尾向链头移动，避免覆盖前方位置。
        for (int i = chain.Count - 1;
             i >= 0;
             i--)
        {
            chain[i].ApplyPush(dir, ease, duration);
        }

        foreach (var (_, grabbed)
                 in grabbedFollowers)
        {
            if (grabbed == null)
                continue;

            grabbed.MoveTo(
                grabbed.GridPos + dir,
                ease,
                duration
            );
        }

        // 只有链尾会进入原本空出的新格子。
        ResolveSpecialCellEntry(
            chainEnd,
            chainEndTarget
        );
    }

    // ─────────── 特殊格子结算 ───────────

    /// <summary>
    /// 处理可推动物进入坑洞等特殊格子的结果。
    /// </summary>
    private void ResolveSpecialCellEntry(
        PushableObject pushable,
        Vector3Int cellPos)
    {
        if (pushable == null)
            return;

        MapGrid grid = controller?.MapGrid;

        if (grid == null)
            return;

        bool consumed =
            grid.ResolvePushableEnteredCell(
                pushable,
                cellPos
            );

        if (!consumed)
            return;

        // 填坑箱子已经成为地形的一部分，
        // 不再作为可推动物占据该格。
        posIndex.Unregister(pushable);

        // PitMechanism 自己负责显示 filledVisual。
        // 原箱子直接隐藏。
        pushable.gameObject.SetActive(false);
    }

    // ─────────── 抓取跟随 ───────────

    /// <summary>
    /// 若 obj 是手且有抓取目标，将未被推链处理过的抓取物沿同方向移动一格。
    /// </summary>
    private void MoveGrabbedIfHandler(PushableObject obj, Vector3Int dir, Ease ease, float? duration)
    {
        if (!(obj is OrganUnit hand) || hand.OrganType != OrganType.Hand || !hand.IsGrabbing)
            return;

        var grid = controller.MapGrid;
        if (grid == null) return;

        foreach (var grabbed in hand.GrabbedTargets)
        {
            if (grabbed == null) continue;

            // 已被推链处理的不重复移动
            if (allMoved.Contains(grabbed)) continue;

            Vector3Int grabbedTarget = grabbed.GridPos + dir;
            if (!grid.IsWalkable(grabbedTarget)) continue;

            PushableObject blocker = posIndex.GetAt(grabbedTarget, grabbed);
            if (blocker != null && !allMoved.Contains(blocker)) continue;

            grabbed.MoveTo(grabbedTarget, ease, duration);
        }
    }

    // ─────────── 工具 ───────────

    private static bool IsCardinalDirection(
        Vector3Int dir)
    {
        if (dir.z != 0)
            return false;

        int length =
            Mathf.Abs(dir.x) +
            Mathf.Abs(dir.y);

        return length == 1;
    }
}
