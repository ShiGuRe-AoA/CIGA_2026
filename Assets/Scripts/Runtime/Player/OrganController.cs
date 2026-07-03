using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 器官控制器 —— 玩家输入路由与多器官/可推动物体协调中心。
/// WASD 控制活动脚的移动，LCtrl 在多个脚之间切换，Tab 切换眼球摄像机，空格触发手的抓取。
/// </summary>
public class OrganController : MonoBehaviour
{
    [Header("输入")]
    [SerializeField] private KeyCode footSwitchKey = KeyCode.LeftControl;
    [SerializeField] private KeyCode eyeSwitchKey = KeyCode.Tab;
    [SerializeField] private KeyCode specialKey = KeyCode.Space;

    [Header("关卡目标")]
    [SerializeField] private Vector3Int goalGridPos;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    // 运行时状态
    private List<OrganUnit> organs = new List<OrganUnit>();
    private List<PushableObject> allPushables = new List<PushableObject>();
    private OrganUnit heartUnit;
    private OrganUnit handUnit;
    private List<OrganUnit> footOrgans = new List<OrganUnit>();
    private List<OrganUnit> eyeOrgans = new List<OrganUnit>();
    private int activeFootIndex;
    private int activeEyeIndex;

    public OrganUnit HeartUnit => heartUnit;
    public OrganUnit ActiveFoot => (footOrgans.Count > 0) ? footOrgans[activeFootIndex] : null;

    /// <summary>便捷访问 MapGrid 单例</summary>
    private MapGrid Grid => GameBootstrap.Instance?.MapGrid;

    // ─────────── 初始化 ───────────

    private void Start()
    {
        CollectAll();
    }

    /// <summary>
    /// 收集所有 OrganUnit 和 PushableObject 子对象，注入引用并对齐网格、初始化摄像机。
    /// </summary>
    private void CollectAll()
    {
        organs.Clear();
        GetComponentsInChildren(organs);

        allPushables.Clear();
        GetComponentsInChildren(allPushables);

        if (organs.Count == 0)
        {
            Debug.LogWarning("[OrganController] 未找到任何 OrganUnit 子对象。");
            return;
        }

        // 找到心、手
        heartUnit = organs.Find(o => o.OrganType == OrganType.Heart);
        handUnit  = organs.Find(o => o.OrganType == OrganType.Hand);

        // 收集所有脚
        footOrgans.Clear();
        foreach (var organ in organs)
        {
            if (organ.OrganType == OrganType.Foot)
                footOrgans.Add(organ);
        }

        // 收集所有眼球
        eyeOrgans.Clear();
        foreach (var organ in organs)
        {
            if (organ.OrganType == OrganType.Eye && organ.HasCamera)
                eyeOrgans.Add(organ);
        }

        if (heartUnit    == null) Debug.LogWarning("[OrganController] 未找到 Heart。");
        if (footOrgans.Count == 0) Debug.LogWarning("[OrganController] 未找到 Foot。");
        if (handUnit     == null) Debug.LogWarning("[OrganController] 未找到 Hand。");

        // 注入心引用 + 对齐网格
        foreach (var organ in organs)
            organ.HeartUnit = heartUnit;
        foreach (var pushable in allPushables)
            pushable.SnapToGrid();

        // 初始化摄像机
        InitCameras();

        Log($"[OrganController] 初始化: {organs.Count} 器官({footOrgans.Count} 脚/{eyeOrgans.Count} 眼), {allPushables.Count - organs.Count} 场景物体");
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
        HandleMoveInput();
        HandleSpecialInput();
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

    /// <summary>空格键触发手的抓取/释放。</summary>
    private void HandleSpecialInput()
    {
        if (!Input.GetKeyDown(specialKey)) return;
        if (handUnit == null) return;

        HandleHandGrab(handUnit);
    }

    // ─────────── 移动执行 ───────────

    /// <summary>
    /// 执行器官移动，包含推进链（可推器官和场景物体）和抓取跟随。
    /// </summary>
    private void ExecuteOrganMove(OrganUnit organ, Vector3Int dir)
    {
        Vector3Int targetPos = organ.GridPos + dir;

        // 1. 自身能力检查
        if (!organ.CanMoveTo(targetPos))
        {
            Grid?.NotifyBlocked(targetPos);
            Log($"[OrganController] {organ.name} 无法移动到 {targetPos}。");
            return;
        }

        // 记录心跳动前的位置（必须在推进链之前，因为链中可能推动心）
        Vector3Int heartOldPos = heartUnit != null ? heartUnit.GridPos : Vector3Int.zero;

        // 2. 检查目标格是否被其他可推动物体占据
        PushableObject occupier = GetPushableAt(targetPos, exclude: organ);
        if (occupier != null)
        {
            if (organ.OrganType != OrganType.Foot)
            {
                Log($"[OrganController] {targetPos} 被 {occupier.name} 占据，无法移动。");
                return;
            }

            if (!TryPushChain(occupier, dir))
            {
                Log($"[OrganController] 推进链受阻。");
                return;
            }
        }

        // 3. 执行移动
        Vector3Int oldPos = organ.GridPos;
        organ.MoveTo(targetPos);

        // 4. 抓取跟随
        if (organ.OrganType == OrganType.Hand && organ.IsGrabbing)
        {
            MoveGrabbedWithHand(organ, dir, oldPos);
        }

        // 5. 心移动后拉动超距器官
        if (heartUnit != null && heartUnit.GridPos != heartOldPos)
        {
            PullOutOfRangeOrgans();
        }

        Log($"[OrganController] {organ.name} {oldPos} → {targetPos}");
    }

    // ─────────── 推进链 ───────────

    /// <summary>
    /// 尝试沿方向推进整条链（可包含器官和场景物体）。
    /// </summary>
    /// <param name="bypassHeart">为 true 时跳过心距离检查（蓄力踢等强推力场景）。</param>
    private bool TryPushChain(PushableObject firstPushed, Vector3Int dir, bool bypassHeart = false)
    {
        List<PushableObject> chain = new List<PushableObject>();
        Vector3Int checkPos = firstPushed.GridPos;

        while (true)
        {
            PushableObject obj = GetPushableAt(checkPos);
            if (obj == null) break;
            if (chain.Contains(obj)) break;

            chain.Add(obj);
            checkPos += dir;

            if (chain.Count > 100) return false;
        }

        if (!Grid.IsWalkable(checkPos))
        {
            Grid?.NotifyBlocked(checkPos);
            Log($"[OrganController] 推进链终点 {checkPos} 为墙壁。");
            return false;
        }

        // 心距离检查（蓄力踢时跳过）
        if (!bypassHeart)
        {
            foreach (var pushed in chain)
            {
                Vector3Int newPos = pushed.GridPos + dir;
                if (pushed is OrganUnit organ && !organ.IsWithinHeartRange(newPos))
                {
                    Log($"[OrganController] {organ.name} 被推到 {newPos} 将超出心的范围。");
                    return false;
                }
            }
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            chain[i].ApplyPush(dir);
            Log($"[OrganController]  {chain[i].name} 被推动 → {chain[i].GridPos}");
        }

        return true;
    }

    /// <summary>
    /// 蓄力踢：沿指定方向推动所有阻挡物体若干格，忽略心距离约束。
    /// 踢完后自动将超距器官拉回。
    /// </summary>
    /// <param name="foot">发起踢的脚</param>
    /// <param name="kickDir">踢击方向</param>
    /// <param name="pushDistance">推动格数（由蓄力程度决定）</param>
    /// <returns>是否踢到了任何物体</returns>
    public bool ForceKick(OrganUnit foot, Vector3Int kickDir, int pushDistance)
    {
        if (pushDistance <= 0 || Grid == null) return false;

        bool hitAnything = false;

        for (int step = 0; step < pushDistance; step++)
        {
            // 每次向前扫描找到最近物体（上一次推动已使其位移）
            Vector3Int scanPos = foot.GridPos + kickDir;
            PushableObject firstInLine = null;

            // 沿踢方向扫描，跳过已空出的格子
            while (Grid.IsWalkable(scanPos))
            {
                firstInLine = GetPushableAt(scanPos);
                if (firstInLine != null) break;
                scanPos += kickDir;
            }

            if (firstInLine == null) break;  // 扫描到底都没找到物体

            if (!TryPushChain(firstInLine, kickDir, bypassHeart: true))
                break;  // 遇墙卡住

            hitAnything = true;
        }

        if (hitAnything)
        {
            Log($"[OrganController] 蓄力踢! 方向 {kickDir} x{pushDistance}");
            PullOutOfRangeOrgans();
        }

        return hitAnything;
    }

    // ─────────── 心拉动 ───────────

    /// <summary>
    /// 心移动后，将超出范围的器官逐步拉向心。仅作用于 OrganUnit。
    /// </summary>
    private void PullOutOfRangeOrgans()
    {
        if (heartUnit == null || Grid == null) return;

        int safety = 0;
        bool anyPulled;

        do
        {
            anyPulled = false;

            foreach (var organ in organs)
            {
                if (organ == heartUnit) continue;
                if (!organ.IsOutOfHeartRange()) continue;

                Vector3Int pullDir = organ.GetPullDirectionTowardHeart();
                if (pullDir == Vector3Int.zero) continue;

                Vector3Int target = organ.GridPos + pullDir;

                if (!Grid.IsWalkable(target)) continue;
                if (GetPushableAt(target, exclude: organ) != null) continue;

                organ.MoveTo(target);
                anyPulled = true;
                Log($"[OrganController]  心拉动 {organ.name} → {target}");
            }

            safety++;
        }
        while (anyPulled && safety < 50);
    }

    // ─────────── 手抓取 ───────────

    /// <summary>
    /// 处理手的抓取/释放。扫描相邻四方向，抓取任意 PushableObject。
    /// </summary>
    private void HandleHandGrab(OrganUnit hand)
    {
        if (hand.IsGrabbing)
        {
            hand.ReleaseGrabbed();
            Log("[OrganController] Hand 已释放。");
            return;
        }

        Vector3Int[] dirs = {
            Vector3Int.up, Vector3Int.down,
            Vector3Int.left, Vector3Int.right
        };

        foreach (var d in dirs)
        {
            PushableObject target = GetPushableAt(hand.GridPos + d);
            if (target != null && target != hand)
            {
                hand.GrabTarget(target);
                Log($"[OrganController] Hand 抓取了 {target.name}");
                return;
            }
        }

        Log("[OrganController] Hand 相邻格无物体可抓取。");
    }

    /// <summary>
    /// 手移动时，被抓物体跟随移动。若被抓的是 OrganUnit，还需检查心距离。
    /// </summary>
    private void MoveGrabbedWithHand(OrganUnit hand, Vector3Int dir, Vector3Int handOldPos)
    {
        PushableObject grabbed = hand.GrabbedTarget;
        if (grabbed == null) return;

        Vector3Int offset = grabbed.GridPos - handOldPos;
        Vector3Int grabbedNewPos = hand.GridPos + offset;

        // 目标格检查
        if (!Grid.IsWalkable(grabbedNewPos))
        {
            hand.ReleaseGrabbed();
            Log($"[OrganController] 抓取目标路径为墙壁，自动释放。");
            return;
        }

        PushableObject occupier = GetPushableAt(grabbedNewPos, exclude: grabbed);
        if (occupier != null && occupier != hand)
        {
            hand.ReleaseGrabbed();
            Log($"[OrganController] 抓取目标路径被 {occupier.name} 占据，自动释放。");
            return;
        }

        // 如果是器官，检查心距离
        if (grabbed is OrganUnit grabbedOrgan)
        {
            if (!grabbedOrgan.IsWithinHeartRange(grabbedNewPos))
            {
                hand.ReleaseGrabbed();
                Log($"[OrganController] 抓取移动将超出心的范围，自动释放。");
                return;
            }
        }

        grabbed.MoveTo(grabbedNewPos);
        Log($"[OrganController]  抓取的 {grabbed.name} 跟随移动 → {grabbedNewPos}");
    }

    // ─────────── 查询 ───────────

    /// <summary>
    /// 获取指定格子上任意可推动物体。可排除自身。
    /// </summary>
    public PushableObject GetPushableAt(Vector3Int cellPos, PushableObject exclude = null)
    {
        foreach (var p in allPushables)
        {
            if (p == exclude) continue;
            if (p.GridPos == cellPos) return p;
        }
        return null;
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
