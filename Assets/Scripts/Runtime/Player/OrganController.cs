using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// 器官控制器 —— WASD 控制脚，LCtrl 切脚，Tab 切眼球摄像机，E 键多手抓取/释放。
/// </summary>
public class OrganController : MonoBehaviour
{
    [Header("输入")]
    [SerializeField] private KeyCode footSwitchKey = KeyCode.LeftControl;
    [SerializeField] private KeyCode eyeSwitchKey = KeyCode.Tab;
    [SerializeField] private KeyCode grabKey = KeyCode.E;

    [Header("关卡目标")]
    [SerializeField] private Vector3Int goalGridPos;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    [Header("蓄力踢速度")]
    [Tooltip("踢击起始最快速度（最小 Duration，秒/格）。越小越快。")]
    [SerializeField, Min(0.001f)] private float minKickDuration = 0.015f;

    [Header("器官回缩逻辑")]
    [Tooltip(
        "关闭：使用原有遍历式回拉和顺序滑回逻辑。\n" +
        "开启：保留原版寻路规则，每轮先收集移动，再同步播放一格。"
    )]
    [SerializeField] private bool useNewPullLogic = false;

    [Header("手动注册")]
    [Tooltip("手动拖入 OrganUnit。为空时自动查找子对象。")]
    [SerializeField] private List<OrganUnit> manualOrgans;

    // ─────────── 运行时状态 ───────────

    private readonly List<OrganUnit> organs = new List<OrganUnit>();
    private OrganUnit heartUnit;

    private readonly List<OrganUnit> handOrgans = new List<OrganUnit>();
    private readonly List<OrganUnit> footOrgans = new List<OrganUnit>();
    private readonly List<OrganUnit> eyeOrgans = new List<OrganUnit>();

    private int activeFootIndex;
    private int activeEyeIndex;
    private bool handsGrabbing;

    // O(1) 位置索引，替代原 List<PushableObject> 的全表扫描。
    private readonly GridPositionIndex posIndex = new GridPositionIndex();

    // 蓄力踢后的延时或旧版顺序滑回动画。
    private Sequence pullBackSequence;

    [Header("新版回缩动画")]
    [Tooltip("新版同步回缩中，每一轮移动一格所使用的时间。小于等于 0 时使用器官自身 MoveDuration。")]
    [SerializeField, Min(0f)] private float newPullStepDuration = 0f;

    [Tooltip("新版同步回缩中，相邻两轮之间的停顿时间。设置为 0 时连续逐格移动。")]
    [SerializeField, Min(0f)] private float newPullRoundInterval = 0f;

    // 回缩动画播放期间锁定输入，防止逻辑位置与视觉位置错位时继续操作。
    private bool isPullingBack;

    /// <summary>
    /// 正在执行允许器官临时超距的移动，例如蓄力踢。
    /// 此期间位置变化不会触发普通的被动超距回弹。
    /// </summary>
    private bool suppressPassiveRangeRecovery;

    /// <summary>
    /// 已请求一次被动超距恢复，避免同一推动链中的多个位置事件重复启动。
    /// </summary>
    private bool passiveRecoveryPending;

    public OrganUnit HeartUnit => heartUnit;

    public OrganUnit ActiveFoot =>
        footOrgans.Count > 0
            ? footOrgans[activeFootIndex]
            : null;

    /// <summary>MapGrid 单例（公开给 PushContext 等使用）。</summary>
    public MapGrid MapGrid => GameBootstrap.Instance?.MapGrid;

    private MapGrid Grid => MapGrid;

    // ─────────── 初始化 ───────────

    private void Start()
    {
        CollectAll();
    }

    private void OnDestroy()
    {
        pullBackSequence?.Kill(complete: false);
        pullBackSequence = null;

        UnregisterAllActiveOrgans();

        // 取消所有位置变更事件订阅。
        foreach (var pushable in posIndex.AllObjects)
            pushable.OnGridPositionChanged -= OnPushableMoved;
    }

    /// <summary>
    /// 收集所有 OrganUnit 和 ScenePushable 子对象，
    /// 注入引用并对齐网格、初始化位置索引与摄像机。
    /// </summary>
    private void CollectAll()
    {
        organs.Clear();

        // 手动注册优先，为空时自动查找子对象
        if (manualOrgans != null && manualOrgans.Count > 0)
        {
            organs.AddRange(manualOrgans);
        }
        else
        {
            GetComponentsInChildren(organs);
        }

        posIndex.Clear();

        if (organs.Count == 0)
        {
            Debug.LogWarning("[OrganController] 未找到任何 OrganUnit 子对象。");
            return;
        }

        heartUnit = organs.Find(
            organ => organ.OrganType == OrganType.Heart
        );

        handOrgans.Clear();
        footOrgans.Clear();
        eyeOrgans.Clear();

        foreach (var organ in organs)
        {
            switch (organ.OrganType)
            {
                case OrganType.Hand:
                    handOrgans.Add(organ);
                    break;

                case OrganType.Foot:
                    footOrgans.Add(organ);
                    break;

                case OrganType.Eye:
                    if (organ.HasCamera)
                        eyeOrgans.Add(organ);
                    break;
            }

            // 避免 CollectAll 被重复调用时产生重复订阅。
            organ.OnGridPositionChanged -= OnPushableMoved;
            organ.OnGridPositionChanged += OnPushableMoved;
        }

        if (heartUnit == null)
            Debug.LogWarning("[OrganController] 未找到 Heart。");

        if (footOrgans.Count == 0)
            Debug.LogWarning("[OrganController] 未找到 Foot。");

        if (handOrgans.Count == 0)
            Debug.LogWarning("[OrganController] 未找到 Hand。");

        // 注入心引用 + 对齐网格（必须在注册索引之前）
        foreach (var organ in organs)
        {
            organ.HeartUnit = heartUnit;
            organ.SnapToGrid();
        }

        // 收集所有普通场景可推动物。
        var scenePushables =
            GetComponentsInChildren<ScenePushable>();

        foreach (var scenePushable in scenePushables)
        {
            scenePushable.SnapToGrid();

            scenePushable.OnGridPositionChanged -= OnPushableMoved;
            scenePushable.OnGridPositionChanged += OnPushableMoved;
        }

        // 批量注册到位置索引（此时 gridPos 已初始化）
        posIndex.RegisterAll(organs);

        foreach (var scenePushable in scenePushables)
            posIndex.Register(scenePushable);

        InitCameras();
        RefreshActiveOrganRegistrations();

        Log(
            $"[OrganController] 初始化: {organs.Count} 器官" +
            $"({footOrgans.Count} 脚/{eyeOrgans.Count} 眼), " +
            $"{scenePushables.Length} 场景物体"
        );
    }

    /// <summary>
    /// 位置变化回调：
    /// 1. 同步位置索引；
    /// 2. 检查任意器官是否因外力移动而超出心距；
    /// 3. 超距时在本帧结束后触发回缩。
    /// </summary>
    private void OnPushableMoved(
        PushableObject obj,
        Vector3Int oldPos,
        Vector3Int newPos)
    {
        posIndex.OnMoved(obj, oldPos, newPos);

        if (suppressPassiveRangeRecovery)
            return;

        if (isPullingBack)
            return;

        if (obj is not OrganUnit movedOrgan)
            return;

        /*
         * 心脏移动后，可能导致多个器官同时超距。
         * 普通器官移动后，则检查该器官自身是否超距。
         */
        bool needsRecovery =
            movedOrgan == heartUnit
                ? HasAnyOutOfRangeOrgan()
                : movedOrgan.IsOutOfHeartRange();

        if (!needsRecovery)
            return;

        RequestPassiveRangeRecovery();
    }

    /// <summary>
    /// 检查是否存在超出自身最大心距的非心脏器官。
    /// </summary>
    private bool HasAnyOutOfRangeOrgan()
    {
        foreach (OrganUnit organ in organs)
        {
            if (organ == null || organ == heartUnit)
                continue;

            if (organ.IsOutOfHeartRange())
                return true;
        }

        return false;
    }

    /// <summary>
    /// 请求一次被动超距恢复。
    /// 同一帧内多次位置变化只会产生一个恢复请求。
    /// </summary>
    private void RequestPassiveRangeRecovery()
    {
        if (passiveRecoveryPending)
            return;

        passiveRecoveryPending = true;

        StartCoroutine(
            RecoverPassiveOutOfRangeOrgansAtEndOfFrame()
        );
    }

    private System.Collections.IEnumerator
        RecoverPassiveOutOfRangeOrgansAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();

        passiveRecoveryPending = false;

        if (suppressPassiveRangeRecovery)
            yield break;

        if (isPullingBack)
            yield break;

        if (!HasAnyOutOfRangeOrgan())
            yield break;

        RecoverPassiveOutOfRangeOrgans();
    }

    /// <summary>
    /// 处理传送带、推动链等外力造成的器官超距。
    ///
    /// 与蓄力踢不同：
    /// 被动超距只回到 MaxHeartDistance 范围内，
    /// 不回缩到 KickReturnHeartDistance。
    /// </summary>
    private void RecoverPassiveOutOfRangeOrgans()
    {
        if (useNewPullLogic)
        {
            List<Dictionary<OrganUnit, Vector3Int>> rounds =
                BuildPullRounds_New(
                    pullToTightState: false
                );

            PlayPullRounds_New(
                rounds,
                "被动超距回弹"
            );

            return;
        }

        isPullingBack = true;

        try
        {
            PullOutOfRangeOrgans();
        }
        finally
        {
            isPullingBack = false;
        }
    }

    /// <summary>
    /// 摄像机初始化：
    /// 有眼球时激活第一个眼球摄像机；
    /// 没有眼球时尝试启用心脏摄像机。
    /// </summary>
    private void InitCameras()
    {
        foreach (var organ in organs)
            organ.SetCameraActive(false);

        if (eyeOrgans.Count > 0)
        {
            activeEyeIndex = 0;
            eyeOrgans[0].SetCameraActive(true);

            Log(
                $"[OrganController] 摄像机: 眼球 " +
                $"{eyeOrgans[0].name}"
            );
        }
        else if (heartUnit != null && heartUnit.HasCamera)
        {
            heartUnit.SetCameraActive(true);
            Log("[OrganController] 摄像机: 心（无眼球）");
        }
    }

    /// <summary>
    /// 运行时重新评估摄像机：有眼则用眼，无眼则用心。
    /// </summary>
    private void ReevaluateCamera()
    {
        // 关闭所有摄像机
        foreach (var organ in organs)
            organ.SetCameraActive(false);

        if (eyeOrgans.Count > 0)
        {
            // 矫正索引后激活第一个可用的眼
            if (activeEyeIndex >= eyeOrgans.Count)
                activeEyeIndex = 0;
            eyeOrgans[activeEyeIndex].SetCameraActive(true);
        }
        else if (heartUnit != null && heartUnit.HasCamera)
        {
            heartUnit.SetCameraActive(true);
        }

        RefreshActiveOrganRegistrations();
    }

    /// <summary>
    /// 重新同步 MapGrid 中的激活器官列表。
    /// </summary>
    private void RefreshActiveOrganRegistrations()
    {
        UnregisterAllActiveOrgans();

        SetOrganActive(ActiveFoot, true);

        if (eyeOrgans.Count > 0 &&
            activeEyeIndex >= 0 &&
            activeEyeIndex < eyeOrgans.Count)
        {
            SetOrganActive(eyeOrgans[activeEyeIndex], true);
        }

        foreach (var hand in handOrgans)
            SyncHandActiveState(hand);
    }

    private void SyncHandActiveState(OrganUnit hand)
    {
        if (hand == null)
            return;

        SetOrganActive(hand, hand.IsGrabbing);
    }

    private void SetOrganActive(OrganUnit organ, bool active)
    {
        if (Grid == null || organ == null)
            return;

        if (active)
            Grid.RegisterActiveOrgan(organ);
        else
            Grid.UnregisterActiveOrgan(organ);
    }

    private void UnregisterAllActiveOrgans()
    {
        if (Grid == null)
            return;

        foreach (var organ in organs)
            Grid.UnregisterActiveOrgan(organ);
    }

    // ─────────── Update ───────────

    private void Update()
    {
        if (organs.Count == 0 || Grid == null)
            return;

        if (!isPullingBack)
        {
            HandleFootSwitch();
            HandleEyeCameraSwitch();
            HandleGrabInput();
            HandleMoveInput();
        }

        CheckVictory();
    }

    // ─────────── 输入处理 ───────────

    /// <summary>LCtrl 在多个脚之间循环切换活动脚。</summary>
    private void HandleFootSwitch()
    {
        if (footOrgans.Count <= 1)
            return;

        if (!Input.GetKeyDown(footSwitchKey))
            return;

        OrganUnit oldFoot = ActiveFoot;

        activeFootIndex =
            (activeFootIndex + 1) % footOrgans.Count;

        SetOrganActive(oldFoot, false);
        SetOrganActive(ActiveFoot, true);

        Log(
            $"[OrganController] 切换到脚: " +
            $"{footOrgans[activeFootIndex].name} " +
            $"({activeFootIndex + 1}/{footOrgans.Count})"
        );
    }

    /// <summary>Tab 在多个眼球摄像机之间循环切换。</summary>
    private void HandleEyeCameraSwitch()
    {
        if (eyeOrgans.Count <= 1)
            return;

        if (!Input.GetKeyDown(eyeSwitchKey))
            return;

        OrganUnit oldEye = eyeOrgans[activeEyeIndex];
        oldEye.SetCameraActive(false);
        SetOrganActive(oldEye, false);

        activeEyeIndex =
            (activeEyeIndex + 1) % eyeOrgans.Count;

        OrganUnit newEye = eyeOrgans[activeEyeIndex];
        newEye.SetCameraActive(true);
        SetOrganActive(newEye, true);

        Log(
            $"[OrganController] 摄像机切换到眼球: " +
            $"{eyeOrgans[activeEyeIndex].name} " +
            $"({activeEyeIndex + 1}/{eyeOrgans.Count})"
        );
    }

    /// <summary>
    /// WASD / 方向键控制当前活动脚。
    /// 脚正在蓄力时不响应移动输入。
    /// </summary>
    private void HandleMoveInput()
    {
        Vector3Int dir = GetInputDirection();

        if (dir == Vector3Int.zero)
            return;

        if (ActiveFoot == null)
            return;

        var kick =
            ActiveFoot.GetComponent<FootChargeKick>();

        if (kick != null && kick.IsCharging)
            return;

        ExecuteOrganMove(ActiveFoot, dir);
    }

    /// <summary>
    /// E 键切换所有手的抓取/释放，
    /// 抓取时同时触发相邻 Interact 触发器。
    /// </summary>
    private void HandleGrabInput()
    {
        if (!Input.GetKeyDown(grabKey))
            return;

        if (handOrgans.Count == 0)
            return;

        if (handsGrabbing)
        {
            foreach (var hand in handOrgans)
            {
                hand.ReleaseAllGrabbed();
                SyncHandActiveState(hand);
            }

            handsGrabbing = false;
            Log("[OrganController] 所有 Hand 已释放。");
            return;
        }

        handsGrabbing = true;
        int totalGrabbed = 0;

        foreach (var hand in handOrgans)
        {
            FireInteractTriggers(hand);

            foreach (var direction in Direction4s)
            {
                PushableObject obj =
                    GetPushableAt(hand.GridPos + direction);

                if (obj == null || obj == hand)
                    continue;

                hand.AddGrabbed(obj);
                totalGrabbed++;
            }

            SyncHandActiveState(hand);
        }

        Log(
            $"[OrganController] 所有 Hand 共抓取 " +
            $"{totalGrabbed} 个物体。"
        );
    }

    private static readonly Vector3Int[] Direction4s =
    {
        Vector3Int.up,
        Vector3Int.down,
        Vector3Int.left,
        Vector3Int.right,
    };

    private void FireInteractTriggers(OrganUnit hand)
    {
        var triggers =
            GameBootstrap.Instance?.AllTriggers;

        if (triggers == null)
            return;

        foreach (var direction in Direction4s)
        {
            Vector3Int checkPos =
                hand.GridPos + direction;

            foreach (var trigger in triggers)
            {
                if (trigger != null &&
                    trigger.GridPos == checkPos)
                {
                    trigger.Fire();
                }
            }
        }
    }

    // ─────────── 移动执行 ───────────

    /// <summary>
    /// 统一移动入口。
    /// 脚移动、旧版心拉回和蓄力踢均通过 PushContext 处理，
    /// 以保持推动链、抓取跟随和占用检查一致。
    /// </summary>
    /// <param name="obj">需要移动的物体。</param>
    /// <param name="dir">单格移动方向。</param>
    /// <param name="canPush">是否允许推动目标格前方物体。</param>
    /// <param name="thisDuration">动画时长，小于等于 0 时使用物体自身 MoveDuration。</param>
    /// <param name="bypassHeart">
    /// 推动链验证时是否忽略心距离限制，蓄力踢使用 true。
    /// </param>
    /// <returns>物体是否成功移动。</returns>
    private bool TryMoveWithPush(
    PushableObject obj,
    Vector3Int dir,
    bool canPush,
    float thisDuration,
    bool bypassHeart = false,
    Ease ease = Ease.InOutQuad)
    {
        var context =
            new PushContext(this, posIndex);

        float? duration =
            thisDuration > 0f
                ? thisDuration
                : null;

        return context.TryMoveWithPush(
            obj,
            dir,
            canPush,
            bypassHeart,
            ease,
            duration
        );
    }


    /// <summary>
    /// 外部推动入口 — 供传动带等场景物体调用。
    /// </summary>
    public bool InvokeTryMoveWithPush(
        PushableObject obj,
        Vector3Int dir,
        bool canPush,
        float thisDuration)
    {
        if (isPullingBack) return false;
        return TryMoveWithPush(obj, dir, canPush, thisDuration);
    }

    /// <summary>
    /// 脚的普通 WASD 移动。
    /// 脚可以推动前方物体，推动链受到心距离约束。
    /// </summary>
    private void ExecuteOrganMove(
        OrganUnit foot,
        Vector3Int dir)
    {
        Vector3Int oldPos = foot.GridPos;

        if (!TryMoveWithPush(
                foot,
                dir,
                canPush: true,
                thisDuration: 0f))
        {
            Log(
                $"[OrganController] {foot.name} 移动受阻。"
            );
            return;
        }

        // 心被动移动的检测已统一在 TryMoveWithPush 中处理

        Log(
            $"[OrganController] {foot.name} " +
            $"{oldPos} → {foot.GridPos}"
        );
    }

    /// <summary>
    /// 蓄力踢：
    /// 沿指定方向逐格推飞前方物体，忽略心距离约束。
    /// 速度从 minKickDuration（最快）逐渐衰减到物体自身的 moveDuration。
    /// 命中后停留约 1 秒，再按照当前选定的新旧逻辑滑回。
    /// </summary>
    public bool ForceKick(
        OrganUnit foot,
        Vector3Int kickDir,
        int pushDistance)
    {
        if (pushDistance <= 0 || Grid == null)
            return false;

        bool hitAnything = false;
        float totalSteps = pushDistance;

        suppressPassiveRangeRecovery = true;

        try
        {
            for (int step = 0; step < pushDistance; step++)
            {
                Vector3Int scanPos =
                    foot.GridPos + kickDir;

                PushableObject firstInLine = null;

                while (Grid.IsWalkable(scanPos))
                {
                    firstInLine =
                        GetPushableAt(scanPos);

                    if (firstInLine != null)
                        break;

                    scanPos += kickDir;
                }

                if (firstInLine == null)
                    break;

                float t = totalSteps > 1f
                    ? step / (totalSteps - 1f)
                    : 0f;

                float stepDuration = Mathf.Lerp(
                    minKickDuration,
                    firstInLine.MoveDuration,
                    t
                );

                if (!TryMoveWithPush(
                        firstInLine,
                        kickDir,
                        canPush: true,
                        thisDuration: stepDuration,
                        bypassHeart: true))
                {
                    break;
                }

                hitAnything = true;
            }
        }
        finally
        {
            suppressPassiveRangeRecovery = false;
        }

        if (!hitAnything)
            return false;

        Log(
            $"[OrganController] 蓄力踢! " +
            $"方向 {kickDir} x{pushDistance}"
        );

        pullBackSequence?.Kill(complete: false);
        pullBackSequence = null;

        pullBackSequence = DOTween.Sequence()
            .AppendInterval(1f)
            .AppendCallback(
                SlideBackOutOfRangeOrgansByCurrentMode
            );

        return true;
    }

    // ─────────── 心拉动 / 滑回模式分流 ───────────

    /// <summary>
    /// 普通心拉动统一入口。
    /// Old：使用原有遍历式逐步回拉。
    /// New：使用位置快照规划最终布局后同步提交。
    /// </summary>
    private void PullOutOfRangeOrgansByCurrentMode()
    {
        if (useNewPullLogic)
            PullOutOfRangeOrgans_New();
        else
            PullOutOfRangeOrgans();
    }

    /// <summary>
    /// 蓄力踢滑回统一入口。
    /// Old：使用原有逐器官、逐步顺序滑回动画。
    /// New：使用同步规划结果，让所有器官在同一帧开始回缩。
    /// </summary>
    private void SlideBackOutOfRangeOrgansByCurrentMode()
    {
        if (useNewPullLogic)
            SlideBackOutOfRangeOrgans_New();
        else
            SlideBackOutOfRangeOrgans();
    }

    // ─────────── New：保留原寻路的同步逐格回缩 ───────────

    /// <summary>
    /// 新版普通回拉逻辑。
    /// 保留原版“远处器官优先、沿差值更大的轴朝心移动”的寻路方式，
    /// 只将每一轮的真实移动延后到统一播放阶段。
    /// </summary>
    private void PullOutOfRangeOrgans_New()
    {
        List<Dictionary<OrganUnit, Vector3Int>> rounds =
            BuildPullRounds_New(
                pullToTightState: false
            );

        PlayPullRounds_New(
            rounds,
            "普通回拉"
        );
    }

    /// <summary>
    /// 新版蓄力踢滑回逻辑。
    /// 每轮中的所有器官同时移动一格，当前轮完成后再进入下一轮。
    /// </summary>
    private void SlideBackOutOfRangeOrgans_New()
    {
        List<Dictionary<OrganUnit, Vector3Int>> rounds =
            BuildPullRounds_New(
                pullToTightState: true
            );

        PlayPullRounds_New(
            rounds,
            "蓄力踢紧绷回弹"
        );
    }

    /// <summary>
    /// 使用原版寻路规则构建同步逐格回缩轮次。
    /// pullToTightState 为 true 时，回缩到蓄力踢后的紧绷距离；
    /// false 时只回缩到普通最大心距范围。
    /// </summary>
    private List<Dictionary<OrganUnit, Vector3Int>> BuildPullRounds_New(bool pullToTightState)
    {
        var rounds =
            new List<Dictionary<OrganUnit, Vector3Int>>();

        if (heartUnit == null || Grid == null)
            return rounds;

        // 整个规划过程使用的模拟位置。
        var simulatedPositions =
            new Dictionary<OrganUnit, Vector3Int>();

        foreach (var organ in organs)
            simulatedPositions[organ] = organ.GridPos;

        for (int safety = 0; safety < 50; safety++)
        {
            // 本轮开始时的占用快照。
            var occupied =
                new Dictionary<Vector3Int, PushableObject>();

            // 场景物体视为固定障碍。
            foreach (var pushable in posIndex.AllObjects)
            {
                if (pushable == null || pushable is OrganUnit)
                    continue;

                occupied[pushable.GridPos] = pushable;
            }

            // 器官使用模拟位置加入占用表。
            foreach (var pair in simulatedPositions)
                occupied[pair.Value] = pair.Key;

            var sortedOrgans =
                new List<OrganUnit>(organs);

            sortedOrgans.Remove(heartUnit);

            Vector3Int heartPosition =
                simulatedPositions[heartUnit];

            sortedOrgans.Sort((a, b) =>
            {
                int distA = ManhattanDistance_New(
                    simulatedPositions[a],
                    heartPosition
                );

                int distB = ManhattanDistance_New(
                    simulatedPositions[b],
                    heartPosition
                );

                // 与原版一致：离心脏更远的器官先处理。
                return distB.CompareTo(distA);
            });

            var roundMoves =
                new Dictionary<OrganUnit, Vector3Int>();

            foreach (var organ in sortedOrgans)
            {
                Vector3Int current =
                    simulatedPositions[organ];

                int targetHeartDistance = pullToTightState ? organ.KickReturnHeartDistance : organ.MaxHeartDistance;

                if (IsWithinTargetHeartDistance_New(current, heartPosition, targetHeartDistance))
                    continue;

                // 与原版 GetPullDirectionTowardHeart 相同的方向规则。
                Vector3Int pullDir =
                    GetPullDirectionTowardHeart_New(
                        current,
                        heartPosition
                    );

                if (pullDir == Vector3Int.zero)
                    continue;

                Vector3Int target =
                    current + pullDir;

                if (!Grid.IsWalkable(target))
                    continue;

                // 保留顺序规划的直觉：
                // 前面器官在临时占用表中先腾格，后面器官再读取更新后的结果。
                if (occupied.TryGetValue(
                        target,
                        out PushableObject blocker) &&
                    blocker != organ)
                {
                    continue;
                }

                roundMoves[organ] = target;

                occupied.Remove(current);
                occupied[target] = organ;
                simulatedPositions[organ] = target;
            }

            if (roundMoves.Count == 0)
                break;

            rounds.Add(roundMoves);
        }

        return rounds;
    }

    /// <summary>
    /// 播放同步逐格回缩轮次。
    /// 同一轮中的 Tween 使用 Join 并行播放；不同轮使用 Append 顺序播放。
    /// </summary>
    private void PlayPullRounds_New(
        List<Dictionary<OrganUnit, Vector3Int>> rounds,
        string reason)
    {
        if (rounds == null || rounds.Count == 0)
        {
            pullBackSequence = null;
            return;
        }

        pullBackSequence?.Kill(complete: false);
        pullBackSequence = DOTween.Sequence();
        isPullingBack = true;

        foreach (var roundMoves in rounds)
        {
            if (roundMoves == null || roundMoves.Count == 0)
                continue;

            Sequence roundSequence = DOTween.Sequence();
            bool hasMovement = false;

            foreach (var pair in roundMoves)
            {
                OrganUnit capturedOrgan = pair.Key;
                Vector3Int capturedTarget = pair.Value;

                if (capturedOrgan == null ||
                    capturedOrgan == heartUnit)
                {
                    continue;
                }

                float stepDuration =
                    newPullStepDuration > 0f
                        ? newPullStepDuration
                        : capturedOrgan.MoveDuration;

                // 本轮开始时统一提交逻辑位置。
                roundSequence.InsertCallback(
                    0f,
                    () =>
                    {
                        Vector3Int oldPos =
                            capturedOrgan.GridPos;

                        if (oldPos == capturedTarget)
                            return;

                        capturedOrgan.GridPos =
                            capturedTarget;

                        posIndex.OnMoved(
                            capturedOrgan,
                            oldPos,
                            capturedTarget
                        );
                    }
                );

                roundSequence.Join(
                    capturedOrgan.transform
                        .DOMove(
                            Grid.CellToWorld(capturedTarget),
                            stepDuration
                        )
                        .SetEase(Ease.Linear)
                );

                hasMovement = true;
            }

            if (!hasMovement)
            {
                roundSequence.Kill(complete: false);
                continue;
            }

            pullBackSequence.Append(roundSequence);

            if (newPullRoundInterval > 0f)
            {
                pullBackSequence.AppendInterval(
                    newPullRoundInterval
                );
            }
        }

        if (pullBackSequence.Duration() <= 0f)
        {
            pullBackSequence.Kill(complete: false);
            pullBackSequence = null;
            isPullingBack = false;
            return;
        }

        pullBackSequence
            .OnKill(() =>
            {
                isPullingBack = false;
            })
            .OnComplete(() =>
            {
                isPullingBack = false;
                pullBackSequence = null;
            })
            .Play();

        Log(
            $"[OrganController] New {reason}：" +
            $"共 {rounds.Count} 轮同步逐格移动。"
        );
    }

    /// <summary>
    /// 判断模拟位置是否已到达本次回缩所要求的目标心距。
    /// </summary>
    private static bool IsWithinTargetHeartDistance_New(
        Vector3Int organPosition,
        Vector3Int heartPosition,
        int targetDistance)
    {
        if (targetDistance <= 0)
            return true;

        return ManhattanDistance_New(
            organPosition,
            heartPosition
        ) <= targetDistance;
    }

    /// <summary>
    /// 与 OrganUnit.GetPullDirectionTowardHeart 相同：
    /// 优先沿当前位置与心脏差值更大的轴移动一格。
    /// </summary>
    private static Vector3Int GetPullDirectionTowardHeart_New(
        Vector3Int current,
        Vector3Int heartPosition)
    {
        int dx = heartPosition.x - current.x;
        int dy = heartPosition.y - current.y;

        if (Mathf.Abs(dx) >= Mathf.Abs(dy))
        {
            return new Vector3Int(
                dx > 0 ? 1 : dx < 0 ? -1 : 0,
                0,
                0
            );
        }

        return new Vector3Int(
            0,
            dy > 0 ? 1 : dy < 0 ? -1 : 0,
            0
        );
    }

    private static int ManhattanDistance_New(
        Vector3Int a,
        Vector3Int b)
    {
        return Mathf.Abs(a.x - b.x) +
               Mathf.Abs(a.y - b.y);
    }

    // ─────────── Old：原有遍历回拉 / 顺序滑回 ───────────

    /// <summary>
    /// 脚移动导致心位移后的立即回拉。
    ///
    /// 原有逻辑完整保留：
    /// 按距离从远到近遍历超距器官，
    /// 每次移动一格并立即更新真实占用状态，
    /// 直到无法继续移动或全部回到心距离范围。
    /// </summary>
    private void PullOutOfRangeOrgans()
    {
        if (heartUnit == null || Grid == null)
            return;

        int safety = 0;
        bool anyPulled;

        do
        {
            anyPulled = false;

            var sortedOrgans =
                new List<OrganUnit>(organs);

            sortedOrgans.Remove(heartUnit);

            sortedOrgans.Sort((a, b) =>
            {
                int distA =
                    Mathf.Abs(
                        a.GridPos.x - heartUnit.GridPos.x
                    ) +
                    Mathf.Abs(
                        a.GridPos.y - heartUnit.GridPos.y
                    );

                int distB =
                    Mathf.Abs(
                        b.GridPos.x - heartUnit.GridPos.x
                    ) +
                    Mathf.Abs(
                        b.GridPos.y - heartUnit.GridPos.y
                    );

                // 原逻辑：距离心脏更远的器官优先处理。
                return distB.CompareTo(distA);
            });

            foreach (var organ in sortedOrgans)
            {
                if (!organ.IsOutOfHeartRange())
                    continue;

                Vector3Int pullDir =
                    organ.GetPullDirectionTowardHeart();

                if (pullDir == Vector3Int.zero)
                    continue;

                if (TryMoveWithPush(
                        organ,
                        pullDir,
                        canPush: true,
                        thisDuration: 0f,
                        bypassHeart: false,
                        ease: Ease.Linear))
                {
                    anyPulled = true;
                }
            }

            safety++;
        }
        while (anyPulled && safety < 50);
    }

    /// <summary>
    /// 蓄力踢后的原有滑回逻辑。
    ///
    /// 原有逻辑完整保留：
    /// 收集所有超距器官，按距离从远到近处理；
    /// 每个器官逐格修改逻辑位置并依次 Append Tween，
    /// 因此动画表现为一个器官完成后再处理下一个器官。
    ///
    /// 该方法保留用于对照和快速回退。
    /// </summary>
    private void SlideBackOutOfRangeOrgans()
    {
        if (heartUnit == null || Grid == null)
            return;

        // 收集超距器官，排除心脏。
        var outOfRange =
            new List<OrganUnit>();

        foreach (var organ in organs)
        {
            if (organ != heartUnit &&
                organ.IsOutOfHeartRange())
            {
                outOfRange.Add(organ);
            }
        }

        outOfRange.Sort((a, b) =>
        {
            int distA =
                Mathf.Abs(
                    a.GridPos.x - heartUnit.GridPos.x
                ) +
                Mathf.Abs(
                    a.GridPos.y - heartUnit.GridPos.y
                );

            int distB =
                Mathf.Abs(
                    b.GridPos.x - heartUnit.GridPos.x
                ) +
                Mathf.Abs(
                    b.GridPos.y - heartUnit.GridPos.y
                );

            // 原逻辑：距离更远的器官优先滑回。
            return distB.CompareTo(distA);
        });

        if (outOfRange.Count == 0)
        {
            pullBackSequence = null;
            return;
        }

        // 终止这些器官当前正在播放的位移动画。
        foreach (var organ in outOfRange)
            organ.transform.DOKill(complete: false);

        var grid = Grid;

        // 使用字段保存旧版完整滑回 Sequence，
        // 使下一次蓄力踢能够 Kill 当前顺序滑回动画。
        pullBackSequence = DOTween.Sequence();
        isPullingBack = true;

        int processedOrganCount = 0;

        foreach (var organ in outOfRange)
        {
            // 每个器官最多向心滑动 50 格，防止异常配置导致死循环。
            for (int step = 0;
                 step < 50 &&
                 organ.IsOutOfHeartRange();
                 step++)
            {
                Vector3Int pullDir =
                    organ.GetPullDirectionTowardHeart();

                if (pullDir == Vector3Int.zero)
                    break;

                Vector3Int newPos =
                    organ.GridPos + pullDir;

                // 原逻辑保留：
                // 直接更新 GridPos 和位置索引，
                // 不通过 MoveTo 创建每一步 Tween。
                Vector3Int oldGrid =
                    organ.GridPos;

                organ.GridPos = newPos;

                posIndex.OnMoved(
                    organ,
                    oldGrid,
                    newPos
                );

                Vector3 targetWorld =
                    grid.CellToWorld(newPos);

                // 原逻辑使用 Append，因此所有步进严格顺序播放。
                pullBackSequence.Append(
                    organ.transform
                        .DOMove(
                            targetWorld,
                            organ.MoveDuration
                        )
                        .SetEase(Ease.OutQuad)
                );
            }

            processedOrganCount++;

            // 保留原有最多处理 50 个器官的保护逻辑。
            if (processedOrganCount >= 50)
                break;
        }

        if (pullBackSequence.Duration() <= 0f)
        {
            pullBackSequence.Kill(complete: false);
            pullBackSequence = null;
            isPullingBack = false;
            return;
        }

        pullBackSequence
            .OnKill(() =>
            {
                isPullingBack = false;
            })
            .OnComplete(() =>
            {
                isPullingBack = false;
                pullBackSequence = null;
            })
            .Play();

        Log(
            $"[OrganController] 使用 Old 逻辑顺序滑回 " +
            $"{outOfRange.Count} 个超距器官。"
        );
    }

    // ─────────── 查询 ───────────

    /// <summary>
    /// O(1) 获取指定格子上的可推动物体，
    /// 可通过 exclude 排除指定对象。
    /// </summary>
    public PushableObject GetPushableAt(
        Vector3Int cellPos,
        PushableObject exclude = null)
    {
        return posIndex.GetAt(cellPos, exclude);
    }

    /// <summary>
    /// 获取指定格子上的器官。
    /// 该方法保留供器官专属逻辑使用。
    /// </summary>
    public OrganUnit GetOrganAt(
        Vector3Int cellPos,
        PushableObject exclude = null)
    {
        foreach (var organ in organs)
        {
            if (organ == exclude)
                continue;

            if (organ.GridPos == cellPos)
                return organ;
        }

        return null;
    }

    /// <summary>
    /// 运行时切换器官类型，同步更新 OrganController 的分类列表、
    /// 活动脚/眼球索引，以及 OrganUnit 自身的精灵显示。
    /// Heart 类型不可被切换。
    /// </summary>
    public void SwitchOrganType(OrganUnit organ, OrganType newType)
    {
        if (organ == null || organ.OrganType == OrganType.Heart)
            return;

        OrganType oldType = organ.OrganType;
        if (oldType == newType) return;

        // 从旧分类列表移除
        switch (oldType)
        {
            case OrganType.Hand: handOrgans.Remove(organ); break;
            case OrganType.Foot: footOrgans.Remove(organ); break;
            case OrganType.Eye:  eyeOrgans.Remove(organ);  break;
        }

        // OrganUnit 自身更新
        organ.SwitchOrganType(newType);

        // 添加到新分类列表
        switch (newType)
        {
            case OrganType.Hand: handOrgans.Add(organ); break;
            case OrganType.Foot: footOrgans.Add(organ); break;
            case OrganType.Eye:  eyeOrgans.Add(organ);  break;
        }

        // 修正活动脚索引（若切换走了活动脚，切换到下一个可用脚）
        if (oldType == OrganType.Foot && activeFootIndex >= footOrgans.Count)
            activeFootIndex = footOrgans.Count > 0 ? 0 : 0;

        // 眼数量变化 → 重新评估摄像机
        if (oldType == OrganType.Eye || newType == OrganType.Eye)
            ReevaluateCamera();
        else
            RefreshActiveOrganRegistrations();

        Log($"[OrganController] {organ.name} 从 {oldType} 切换为 {newType}");
    }

    // ─────────── 胜利判定 ───────────

    private void CheckVictory()
    {
        if (heartUnit == null)
            return;

        if (heartUnit.GridPos == goalGridPos)
            Log("[OrganController] ★ 心已到达目标！胜利！");
    }

    // ─────────── 输入解析 ───────────

    private Vector3Int GetInputDirection()
    {
        if (Input.GetKeyDown(KeyCode.W) ||
            Input.GetKeyDown(KeyCode.UpArrow))
        {
            return Vector3Int.up;
        }

        if (Input.GetKeyDown(KeyCode.S) ||
            Input.GetKeyDown(KeyCode.DownArrow))
        {
            return Vector3Int.down;
        }

        if (Input.GetKeyDown(KeyCode.A) ||
            Input.GetKeyDown(KeyCode.LeftArrow))
        {
            return Vector3Int.left;
        }

        if (Input.GetKeyDown(KeyCode.D) ||
            Input.GetKeyDown(KeyCode.RightArrow))
        {
            return Vector3Int.right;
        }

        return Vector3Int.zero;
    }

    // ─────────── Gizmos ───────────

    private void OnDrawGizmos()
    {
        if (Grid == null)
            return;

        if (Application.isPlaying)
        {
            Gizmos.color = Color.green;

            Vector3 goalWorld =
                Grid.CellToWorld(goalGridPos);

            Gizmos.DrawWireCube(
                goalWorld,
                Vector3.one * 0.9f
            );

            Gizmos.color =
                new Color(0f, 1f, 0f, 0.15f);

            Gizmos.DrawCube(
                goalWorld,
                Vector3.one
            );
        }

        if (Application.isPlaying &&
            ActiveFoot != null)
        {
            Gizmos.color = Color.cyan;

            Gizmos.DrawWireCube(
                ActiveFoot.transform.position,
                Vector3.one * 1.05f
            );
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying || Grid == null)
            return;

        Gizmos.color = Color.green;

        Vector3 goalWorld =
            Grid.CellToWorld(goalGridPos);

        Gizmos.DrawWireCube(
            goalWorld,
            Vector3.one * 0.9f
        );

        Gizmos.color =
            new Color(0f, 1f, 0f, 0.2f);

        Gizmos.DrawCube(
            goalWorld,
            Vector3.one * 0.9f
        );
    }

    // ─────────── 工具 ───────────

    private void Log(string message)
    {
        if (showDebugLog)
            Debug.Log(message);
    }
}
