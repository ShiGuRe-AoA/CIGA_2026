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

    // 运行时状态
    private List<OrganUnit> organs = new List<OrganUnit>();
    private OrganUnit heartUnit;
    private List<OrganUnit> handOrgans = new List<OrganUnit>();
    private List<OrganUnit> footOrgans = new List<OrganUnit>();
    private List<OrganUnit> eyeOrgans = new List<OrganUnit>();
    private int activeFootIndex;
    private int activeEyeIndex;
    private bool handsGrabbing;

    // O(1) 位置索引，替代原 List<PushableObject> 的全表扫描
    private GridPositionIndex posIndex = new GridPositionIndex();

    // 蓄力踢后延时回拉的 Sequence（秒级延迟 + 动画）
    private Sequence pullBackSequence;

    public OrganUnit HeartUnit => heartUnit;
    public OrganUnit ActiveFoot => (footOrgans.Count > 0) ? footOrgans[activeFootIndex] : null;

    /// <summary>MapGrid 单例（公开给 PushContext 等使用）</summary>
    public MapGrid MapGrid => GameBootstrap.Instance?.MapGrid;
    private MapGrid Grid => MapGrid;

    // ─────────── 初始化 ───────────

    private void Start()
    {
        CollectAll();
    }

    private void OnDestroy()
    {
        // 取消所有位置变更事件订阅
        foreach (var pushable in posIndex.AllObjects)
            pushable.OnGridPositionChanged -= OnPushableMoved;
    }

    /// <summary>
    /// 收集所有 OrganUnit 和 PushableObject 子对象，注入引用并对齐网格、初始化摄像机。
    /// </summary>
    private void CollectAll()
    {
        organs.Clear();
        GetComponentsInChildren(organs);

        posIndex.Clear();

        if (organs.Count == 0)
        {
            Debug.LogWarning("[OrganController] 未找到任何 OrganUnit 子对象。");
            return;
        }

        // 找到心
        heartUnit = organs.Find(o => o.OrganType == OrganType.Heart);

        // 收集手、脚、眼
        handOrgans.Clear();
        footOrgans.Clear();
        eyeOrgans.Clear();
        foreach (var organ in organs)
        {
            switch (organ.OrganType)
            {
                case OrganType.Hand: handOrgans.Add(organ); break;
                case OrganType.Foot: footOrgans.Add(organ); break;
                case OrganType.Eye: if (organ.HasCamera) eyeOrgans.Add(organ); break;
            }

            // 订阅位置变更事件，保持索引同步
            organ.OnGridPositionChanged += OnPushableMoved;
        }

        if (heartUnit      == null) Debug.LogWarning("[OrganController] 未找到 Heart。");
        if (footOrgans.Count  == 0)   Debug.LogWarning("[OrganController] 未找到 Foot。");
        if (handOrgans.Count  == 0)   Debug.LogWarning("[OrganController] 未找到 Hand。");

        // 注入心引用 + 对齐网格
        foreach (var organ in organs)
            organ.HeartUnit = heartUnit;

        // 收集所有 ScenePushable 子对象并注册到索引
        var scenePushables = GetComponentsInChildren<ScenePushable>();
        foreach (var sp in scenePushables)
        {
            sp.SnapToGrid();
            sp.OnGridPositionChanged += OnPushableMoved;
        }

        // 批量注册到位置索引
        posIndex.RegisterAll(organs);
        foreach (var sp in scenePushables)
            posIndex.Register(sp);

        // 初始化摄像机
        InitCameras();

        Log($"[OrganController] 初始化: {organs.Count} 器官({footOrgans.Count} 脚/{eyeOrgans.Count} 眼), {scenePushables.Length} 场景物体");
    }

    /// <summary>位置变更回调，保持索引表同步。</summary>
    private void OnPushableMoved(PushableObject obj, Vector3Int oldPos, Vector3Int newPos)
    {
        posIndex.OnMoved(obj, oldPos, newPos);
    }

    /// <summary>
    /// 摄像机初始化：有眼则激活第一个眼的摄像机并关闭心，无眼则激活心的摄像机。
    /// </summary>
    private void InitCameras()
    {
        // 先关闭所有摄像机
        foreach (var organ in organs)
            organ.SetCameraActive(false);

        if (eyeOrgans.Count > 0)
        {
            activeEyeIndex = 0;
            eyeOrgans[0].SetCameraActive(true);
            Log($"[OrganController] 摄像机: 眼球 {eyeOrgans[0].name}");
        }
        else if (heartUnit != null && heartUnit.HasCamera)
        {
            heartUnit.SetCameraActive(true);
            Log("[OrganController] 摄像机: 心（无眼球）");
        }
    }

    // ─────────── Update ───────────

    private void Update()
    {
        if (organs.Count == 0 || Grid == null) return;

        HandleFootSwitch();
        HandleEyeCameraSwitch();
        HandleGrabInput();
        HandleMoveInput();
        CheckVictory();
    }

    // ─────────── 输入处理 ───────────

    /// <summary>LCtrl 在多个脚之间循环切换活动脚。</summary>
    private void HandleFootSwitch()
    {
        if (footOrgans.Count <= 1) return;
        if (!Input.GetKeyDown(footSwitchKey)) return;

        activeFootIndex = (activeFootIndex + 1) % footOrgans.Count;
        Log($"[OrganController] 切换到脚: {footOrgans[activeFootIndex].name} ({activeFootIndex + 1}/{footOrgans.Count})");
    }

    /// <summary>Tab 键在多个眼球摄像机之间循环切换。</summary>
    private void HandleEyeCameraSwitch()
    {
        if (eyeOrgans.Count <= 1) return;
        if (!Input.GetKeyDown(eyeSwitchKey)) return;

        eyeOrgans[activeEyeIndex].SetCameraActive(false);
        activeEyeIndex = (activeEyeIndex + 1) % eyeOrgans.Count;
        eyeOrgans[activeEyeIndex].SetCameraActive(true);

        Log($"[OrganController] 摄像机切换到眼球: {eyeOrgans[activeEyeIndex].name} ({activeEyeIndex + 1}/{eyeOrgans.Count})");
    }

    /// <summary>WASD / 方向键控制当前活动脚的移动。蓄力时阻止。</summary>
    private void HandleMoveInput()
    {
        Vector3Int dir = GetInputDirection();
        if (dir == Vector3Int.zero) return;
        if (ActiveFoot == null) return;

        // 蓄力踢期间不响应方向移动
        var kick = ActiveFoot.GetComponent<FootChargeKick>();
        if (kick != null && kick.IsCharging) return;

        ExecuteOrganMove(ActiveFoot, dir);
    }

    /// <summary>E 键切换所有手的抓取/释放，同时触发相邻 Interact 触发器。</summary>
    private void HandleGrabInput()
    {
        if (!Input.GetKeyDown(grabKey)) return;
        if (handOrgans.Count == 0) return;

        if (handsGrabbing)
        {
            // 释放
            foreach (var hand in handOrgans)
                hand.ReleaseAllGrabbed();
            handsGrabbing = false;
            Log("[OrganController] 所有 Hand 已释放。");
        }
        else
        {
            // 抓取：每只手扫描四方向
            handsGrabbing = true;
            int totalGrabbed = 0;

            foreach (var hand in handOrgans)
            {
                FireInteractTriggers(hand);

                foreach (var d in Direction4s)
                {
                    PushableObject obj = GetPushableAt(hand.GridPos + d);
                    if (obj != null && obj != hand)
                    {
                        hand.AddGrabbed(obj);
                        totalGrabbed++;
                    }
                }
            }

            Log($"[OrganController] 所有 Hand 共抓取 {totalGrabbed} 个物体。");
        }
    }

    private static readonly Vector3Int[] Direction4s = {
        Vector3Int.up, Vector3Int.down,
        Vector3Int.left, Vector3Int.right
    };

    private void FireInteractTriggers(OrganUnit hand)
    {
        var triggers = GameBootstrap.Instance?.AllTriggers;
        if (triggers == null) return;

        foreach (var d in Direction4s)
        {
            Vector3Int checkPos = hand.GridPos + d;
            foreach (var trigger in triggers)
            {
                if (trigger != null && trigger.GridPos == checkPos)
                    trigger.Fire();
            }
        }
    }

    // ─────────── 移动执行 ───────────

    /// <summary>
    /// 统一移动入口：任何物体位移都通过此方法。
    /// 脚移动、心拉回、蓄力踢均调用此方法，确保推链检测一致。
    /// </summary>
    /// <param name="obj">要移动的物体</param>
    /// <param name="dir">移动方向</param>
    /// <param name="canPush">是否允许推动前方阻挡物</param>
    /// <param name="bypassHeart">推链时跳过心距离检查（仅蓄力踢）</param>
    /// <returns>物体是否成功到达目标格</returns>
    private bool TryMoveWithPush(PushableObject obj, Vector3Int dir, bool canPush, bool bypassHeart = false)
    {
        var ctx = new PushContext(this, posIndex);
        return ctx.TryMoveWithPush(obj, dir, canPush, bypassHeart);
    }

    /// <summary>
    /// 脚 WASD 移动：脚可推动前方物体，推链受心距离约束。
    /// </summary>
    private void ExecuteOrganMove(OrganUnit foot, Vector3Int dir)
    {
        Vector3Int heartOldPos = heartUnit != null ? heartUnit.GridPos : Vector3Int.zero;
        Vector3Int oldPos = foot.GridPos;

        if (!TryMoveWithPush(foot, dir, canPush: true))
        {
            Log($"[OrganController] {foot.name} 移动受阻。");
            return;
        }

        // 心被推动后拉动超距器官
        if (heartUnit != null && heartUnit.GridPos != heartOldPos)
            PullOutOfRangeOrgans();

        Log($"[OrganController] {foot.name} {oldPos} → {foot.GridPos}");
    }

    /// <summary>
    /// 蓄力踢：沿方向逐格推飞物体，无视心距离约束。
    /// 物体停留在最远端约 1s，然后沿反方向逐格滑回，直至回到心范围。
    /// </summary>
    public bool ForceKick(OrganUnit foot, Vector3Int kickDir, int pushDistance)
    {
        if (pushDistance <= 0 || Grid == null) return false;

        bool hitAnything = false;

        for (int step = 0; step < pushDistance; step++)
        {
            Vector3Int scanPos = foot.GridPos + kickDir;
            PushableObject firstInLine = null;

            while (Grid.IsWalkable(scanPos))
            {
                firstInLine = GetPushableAt(scanPos);
                if (firstInLine != null) break;
                scanPos += kickDir;
            }

            if (firstInLine == null) break;

            if (!TryMoveWithPush(firstInLine, kickDir, canPush: true, bypassHeart: true))
                break;

            hitAnything = true;
        }

        if (hitAnything)
        {
            Log($"[OrganController] 蓄力踢! 方向 {kickDir} x{pushDistance}");

            // 1 秒后滑回
            pullBackSequence?.Kill();
            pullBackSequence = DOTween.Sequence()
                .AppendInterval(1f)
                .AppendCallback(() => SlideBackOutOfRangeOrgans());
        }

        return hitAnything;
    }

    // ─────────── 心拉动 / 滑回 ───────────

    /// <summary>
    /// 脚移动导致心位移后的立即回拉（保持原有步进逻辑）。
    /// </summary>
    private void PullOutOfRangeOrgans()
    {
        if (heartUnit == null || Grid == null) return;

        int safety = 0;
        bool anyPulled;

        do
        {
            anyPulled = false;

            var sortedOrgans = new List<OrganUnit>(organs);
            sortedOrgans.Remove(heartUnit);
            sortedOrgans.Sort((a, b) =>
            {
                int distA = Mathf.Abs(a.GridPos.x - heartUnit.GridPos.x) + Mathf.Abs(a.GridPos.y - heartUnit.GridPos.y);
                int distB = Mathf.Abs(b.GridPos.x - heartUnit.GridPos.x) + Mathf.Abs(b.GridPos.y - heartUnit.GridPos.y);
                return distB.CompareTo(distA);
            });

            foreach (var organ in sortedOrgans)
            {
                if (!organ.IsOutOfHeartRange()) continue;

                Vector3Int pullDir = organ.GetPullDirectionTowardHeart();
                if (pullDir == Vector3Int.zero) continue;

                if (TryMoveWithPush(organ, pullDir, canPush: true))
                    anyPulled = true;
            }

            safety++;
        }
        while (anyPulled && safety < 50);
    }

    /// <summary>
    /// 蓄力踢后滑回：所有超距器官沿反方向逐格滑回，每步播放 DOTween 动画。
    /// 不再由心推拉，而是直接计算步数 + 动画。
    /// </summary>
    private void SlideBackOutOfRangeOrgans()
    {
        if (heartUnit == null || Grid == null) return;

        // 收集超距器官（非心），按距离从远到近排序
        var outOfRange = new List<OrganUnit>();
        foreach (var organ in organs)
        {
            if (organ != heartUnit && organ.IsOutOfHeartRange())
                outOfRange.Add(organ);
        }
        outOfRange.Sort((a, b) =>
        {
            int distA = Mathf.Abs(a.GridPos.x - heartUnit.GridPos.x) + Mathf.Abs(a.GridPos.y - heartUnit.GridPos.y);
            int distB = Mathf.Abs(b.GridPos.x - heartUnit.GridPos.x) + Mathf.Abs(b.GridPos.y - heartUnit.GridPos.y);
            return distB.CompareTo(distA); // 远的在前
        });

        if (outOfRange.Count == 0) return;

        // 终止所有超距器官的现有 Tweens
        foreach (var organ in outOfRange)
            organ.transform.DOKill(complete: false);

        // 构建 Sequential 滑回动画
        var grid = Grid;
        var seq = DOTween.Sequence();

        int safety = 0;
        foreach (var organ in outOfRange)
        {
            // 每器官最多 50 步防止死循环
            for (int step = 0; step < 50 && organ.IsOutOfHeartRange(); step++)
            {
                Vector3Int pullDir = organ.GetPullDirectionTowardHeart();
                if (pullDir == Vector3Int.zero) break;

                Vector3Int newPos = organ.GridPos + pullDir;

                // 更新网格位置（直接改 gridPos + 通知索引，不启动 DOMove）
                Vector3Int oldGrid = organ.GridPos;
                organ.GridPos = newPos;
                posIndex.OnMoved(organ, oldGrid, newPos);

                Vector3 targetWorld = grid.CellToWorld(newPos);
                seq.Append(organ.transform
                    .DOMove(targetWorld, organ.MoveDuration)
                    .SetEase(Ease.OutQuad));
            }

            safety++;
            if (safety >= 50) break;
        }

        if (seq.Duration() > 0f)
        {
            seq.Play();
            Log($"[OrganController]  滑回 {outOfRange.Count} 个超距器官");
        }
    }

    // ─────────── 查询 ───────────

    /// <summary>O(1) 获取指定格子上任意可推动物体。可排除自身。</summary>
    public PushableObject GetPushableAt(Vector3Int cellPos, PushableObject exclude = null)
    {
        return posIndex.GetAt(cellPos, exclude);
    }

    /// <summary>
    /// 获取指定格子上的器官（内部仍用于器官专属逻辑）。
    /// </summary>
    public OrganUnit GetOrganAt(Vector3Int cellPos, PushableObject exclude = null)
    {
        foreach (var organ in organs)
        {
            if (organ == exclude) continue;
            if (organ.GridPos == cellPos) return organ;
        }
        return null;
    }

    // ─────────── 胜利判定 ───────────

    private void CheckVictory()
    {
        if (heartUnit == null) return;
        if (heartUnit.GridPos == goalGridPos)
        {
            Log("[OrganController] ★ 心已到达目标！胜利！");
        }
    }

    // ─────────── 输入解析 ───────────

    private Vector3Int GetInputDirection()
    {
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            return Vector3Int.up;
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            return Vector3Int.down;
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            return Vector3Int.left;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            return Vector3Int.right;

        return Vector3Int.zero;
    }

    // ─────────── Gizmos ───────────

    private void OnDrawGizmos()
    {
        if (Grid == null) return;

        if (Application.isPlaying)
        {
            Gizmos.color = Color.green;
            Vector3 goalWorld = Grid.CellToWorld(goalGridPos);
            Gizmos.DrawWireCube(goalWorld, Vector3.one * 0.9f);
            Gizmos.color = new Color(0, 1, 0, 0.15f);
            Gizmos.DrawCube(goalWorld, Vector3.one);
        }

        if (Application.isPlaying && ActiveFoot != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(ActiveFoot.transform.position, Vector3.one * 1.05f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying && Grid != null)
        {
            Gizmos.color = Color.green;
            Vector3 goalWorld = Grid.CellToWorld(goalGridPos);
            Gizmos.DrawWireCube(goalWorld, Vector3.one * 0.9f);
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawCube(goalWorld, Vector3.one * 0.9f);
        }
    }

    // ─────────── 工具 ───────────

    private void Log(string msg)
    {
        if (showDebugLog)
            Debug.Log(msg);
    }
}
