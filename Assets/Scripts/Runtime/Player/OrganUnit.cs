using UnityEngine;
using Cinemachine;

/// <summary>
/// 四方向枚举，用于表示器官和指针的朝向。
/// </summary>
public enum Direction4
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// 器官容器组件。继承 PushableObject，添加器官类型、心的距离约束、
/// 朝向标记、抓取/释放（手）、摄像机（眼/心）等器官特有行为。
/// </summary>
public class OrganUnit : PushableObject
{
    [Header("器官设定")]
    [SerializeField] private OrganType organType;

    [Header("朝向")]
    [SerializeField] private Transform pointerPivot;  // 子物体偏心指针，初始朝上

    [Header("约束")]
    [SerializeField] private int maxHeartDistance = 8;

    [Header("视觉")]
    [SerializeField] private OrganSpriteConfig spriteConfig;

    // ─────────── 运行时状态 ───────────
    private PushableObject grabbedTarget;
    private SpriteRenderer spriteRenderer;
    //private LineRenderer lineRenderer;

    // 代替lineRenderer
    private OrganLink heartLinkRenderer;
    private OrganUnit heartUnit;

    private CinemachineVirtualCamera vcam;
    private Direction4 facingDirection = Direction4.Up;

    /// <summary>器官类型</summary>
    public OrganType OrganType => organType;

    /// <summary>心的引用（由 OrganController 注入）</summary>
    public OrganUnit HeartUnit
    {
        get => heartUnit;

        set
        {
            heartUnit = value;
            RefreshHeartLink();
        }
    }

    /// <summary>当前被抓取的目标（仅 Hand），可以是任何可推动物体</summary>
    public PushableObject GrabbedTarget => grabbedTarget;

    /// <summary>是否正在抓取</summary>
    public bool IsGrabbing => grabbedTarget != null;

    /// <summary>当前朝向（由最后一次移动方向决定）</summary>
    public Direction4 FacingDirection => facingDirection;

    /// <summary>子对象 SpriteRenderer 的世界位置</summary>
    public override Vector3 VisualCenter =>
        spriteRenderer != null ? spriteRenderer.transform.position : transform.position;

    /// <summary>该器官距离心的最大曼哈顿距离限制</summary>
    public int MaxHeartDistance => maxHeartDistance;

    // ─────────── 摄像机 ───────────

    /// <summary>
    /// 开关该器官绑定的 CinemachineVirtualCamera。
    /// 仅当子对象中存在 vcam 且此器官为 Eye 或 Heart 时有效。
    /// </summary>
    public void SetCameraActive(bool active)
    {
        if (vcam == null) return;
        if (organType != OrganType.Eye && organType != OrganType.Heart) return;
        vcam.gameObject.SetActive(active);
    }

    /// <summary>该器官是否配有可用摄像机</summary>
    public bool HasCamera => vcam != null;

    // ─────────── 生命周期 ───────────

    protected override void Awake()
    {
        base.Awake();

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        //if (lineRenderer == null)
        //    lineRenderer = GetComponentInChildren<LineRenderer>();
        if(heartLinkRenderer == null)
            heartLinkRenderer = GetComponentInChildren<OrganLink>(true);    // 代替 linkRenderer
        if (vcam == null)
            vcam = GetComponentInChildren<CinemachineVirtualCamera>();
        if (pointerPivot == null)
            pointerPivot = transform.Find("Pointer");  // 默认约定子物体名 "Pointer"

        if (vcam != null)
            vcam.gameObject.SetActive(false);
    }

    protected override void Start()
    {
        base.Start();
        UpdatePointerRotation();  // 初始状态朝上
    }

    /// <summary>
    /// 覆写基类 MoveTo，额外记录移动方向作为朝向并更新指针旋转。
    /// </summary>
    public override void MoveTo(Vector3Int newPos)
    {
        Vector3Int delta = newPos - gridPos;
        base.MoveTo(newPos);

        if (delta != Vector3Int.zero)
        {
            facingDirection = DeltaToDirection(delta);
            UpdatePointerRotation();
        }
    }

    /// <summary>将移动向量转换为四方向枚举。</summary>
    private static Direction4 DeltaToDirection(Vector3Int delta)
    {
        if (delta.y > 0) return Direction4.Up;
        if (delta.y < 0) return Direction4.Down;
        if (delta.x < 0) return Direction4.Left;
        if (delta.x > 0) return Direction4.Right;
        return Direction4.Up; // 兜底
    }

    /// <summary>将 pointerPivot 子物体的旋转对齐到当前 facingDirection。</summary>
    private void UpdatePointerRotation()
    {
        if (pointerPivot == null) return;

        float angle = facingDirection switch
        {
            Direction4.Up    => 0f,
            Direction4.Down  => 180f,
            Direction4.Left  => 90f,
            Direction4.Right => -90f,
            _ => 0f
        };

        pointerPivot.localRotation = Quaternion.Euler(0, 0, angle);
    }

    protected override void Update()
    {
        base.Update();
        //UpdateHeartLine();
    }

    private void OnValidate()
    {
        if (spriteConfig == null) return;

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.sprite = spriteConfig.GetSprite(organType);
    }

    // ─────────── 能力判断 ───────────

    /// <summary>
    /// 判断能否向指定目标格移动。检查走步 + 心距离限制。
    /// 心本身不可独立移动。
    /// </summary>
    public bool CanMoveTo(Vector3Int targetPos)
    {
        if (mapGrid == null) return false;

        if (organType == OrganType.Heart)
            return false;

        if (!mapGrid.IsWalkable(targetPos))
            return false;

        if (!IsWithinHeartRange(targetPos))
            return false;

        return true;
    }

    /// <summary>
    /// 器官被推动时，除了基类的墙壁检查，还需要检查心距离约束。
    /// 在推进链中由 OrganController 调用 IsWithinHeartRange 额外校验。
    /// </summary>
    public override bool CanBePushed(Vector3Int pushDir)
    {
        return base.CanBePushed(pushDir);
    }

    // ─────────── 心距离相关 ───────────

    /// <summary>
    /// 当前是否超出心的距离范围。
    /// </summary>
    public bool IsOutOfHeartRange()
    {
        if (organType == OrganType.Heart) return false;
        if (HeartUnit == null || maxHeartDistance <= 0) return false;

        int dist = Mathf.Abs(gridPos.x - HeartUnit.gridPos.x)
                 + Mathf.Abs(gridPos.y - HeartUnit.gridPos.y);
        return dist > maxHeartDistance;
    }

    /// <summary>
    /// 获取向心拉动的方向（优先差距大的轴）。
    /// </summary>
    public Vector3Int GetPullDirectionTowardHeart()
    {
        if (HeartUnit == null) return Vector3Int.zero;

        int dx = HeartUnit.gridPos.x - gridPos.x;
        int dy = HeartUnit.gridPos.y - gridPos.y;

        if (Mathf.Abs(dx) >= Mathf.Abs(dy))
            return new Vector3Int(dx > 0 ? 1 : (dx < 0 ? -1 : 0), 0, 0);
        else
            return new Vector3Int(0, dy > 0 ? 1 : (dy < 0 ? -1 : 0), 0);
    }

    /// <summary>
    /// 检查指定位置是否在心的约束范围内（曼哈顿距离）。
    /// </summary>
    public bool IsWithinHeartRange(Vector3Int checkPos)
    {
        if (organType == OrganType.Heart) return true;
        if (HeartUnit == null || maxHeartDistance <= 0) return true;

        int dist = Mathf.Abs(checkPos.x - HeartUnit.gridPos.x)
                 + Mathf.Abs(checkPos.y - HeartUnit.gridPos.y);
        return dist <= maxHeartDistance;
    }

    // ─────────── 手抓取 ───────────

    public void DoSpecial()
    {
        switch (organType)
        {
            case OrganType.Hand:
                if (grabbedTarget != null)
                    ReleaseGrabbed();
                else
                    Debug.Log("[OrganUnit] Hand：请先移动到相邻格再抓取。");
                break;
            case OrganType.Eye:
                Debug.Log("[OrganUnit] Eye 特殊动作（暂未实现）。");
                break;
        }
    }

    /// <summary>手抓取目标（可以是任何 PushableObject）。</summary>
    public void GrabTarget(PushableObject target)
    {
        if (organType != OrganType.Hand) return;
        grabbedTarget = target;
        Debug.Log($"[OrganUnit] Hand 抓取了 {target.name}");
    }

    /// <summary>手释放当前抓取目标。</summary>
    public void ReleaseGrabbed()
    {
        if (grabbedTarget == null) return;
        Debug.Log($"[OrganUnit] Hand 释放了 {grabbedTarget.name}");
        grabbedTarget = null;
    }

    // ─────────── 连线 ───────────

    //private void UpdateHeartLine()
    //{
    //    if (lineRenderer == null || organType == OrganType.Heart) return;

    //    if (HeartUnit != null)
    //    {
    //        lineRenderer.enabled = true;
    //        lineRenderer.SetPosition(0, VisualCenter);
    //        lineRenderer.SetPosition(1, HeartUnit.VisualCenter);
    //    }
    //    else
    //    {
    //        lineRenderer.enabled = false;
    //    }
    //}

    private void RefreshHeartLink()
    {
        if (heartLinkRenderer == null)
            return;

        // 心脏自身不连接自己。
        if (organType == OrganType.Heart || HeartUnit == null)
        {
            heartLinkRenderer.Clear();
            return;
        }

        heartLinkRenderer.Bind(this, HeartUnit);
    }

    // ─────────── Gizmos ───────────

    protected override void OnDrawGizmos()
    {
        Color typeColor = organType switch
        {
            OrganType.Heart => Color.red,
            OrganType.Foot  => Color.blue,
            OrganType.Hand  => Color.green,
            OrganType.Eye   => Color.yellow,
            _               => Color.gray
        };
        Gizmos.color = typeColor;
        Gizmos.DrawWireSphere(transform.position, 0.35f);

        // 手抓取连线（无论目标是器官还是场景物体）
        if (organType == OrganType.Hand && grabbedTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, grabbedTarget.transform.position);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (mapGrid == null || HeartUnit == null) return;

        Gizmos.color = organType == OrganType.Heart
            ? Color.red
            : new Color(1f, 0.5f, 0.2f, 0.5f);

        Gizmos.DrawLine(transform.position, HeartUnit.transform.position);

        if (organType != OrganType.Heart && maxHeartDistance > 0)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            float r = maxHeartDistance;
            Vector3 size = new Vector3(r * 2f + 1f, r * 2f + 1f, 0.01f);
            Gizmos.DrawWireCube(HeartUnit.transform.position, size);
        }
    }
}
