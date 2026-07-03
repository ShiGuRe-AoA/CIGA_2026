using UnityEngine;

/// <summary>
/// 可被推动物体的基类。
/// 提供网格定位、平滑移动、被推动等所有可移动场景物体的共有能力。
/// OrganUnit 和 ScenePushable 均继承自此基类。
/// </summary>
public class PushableObject : MonoBehaviour
{
    [Header("移动")]
    [SerializeField] protected float moveSmoothTime = 0.08f;

    [Header("引用")]
    [SerializeField] protected MapGrid mapGrid;

    protected Vector3Int gridPos;
    protected Vector3 targetWorldPos;
    protected Vector3 moveVelocity;

    /// <summary>当前格子坐标</summary>
    public Vector3Int GridPos
    {
        get => gridPos;
        set => gridPos = value;
    }

    /// <summary>视觉中心世界坐标（子类可覆写）</summary>
    public virtual Vector3 VisualCenter => transform.position;

    protected virtual void Awake()
    {
        if (mapGrid == null)
            mapGrid = FindObjectOfType<MapGrid>();
    }

    protected virtual void Start()
    {
        SnapToGrid();
    }

    protected virtual void Update()
    {
        SmoothMoveToTarget();
    }

    /// <summary>
    /// 平滑移动到目标世界坐标。
    /// </summary>
    private void SmoothMoveToTarget()
    {
        if (targetWorldPos.sqrMagnitude > 0.01f)
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetWorldPos,
                ref moveVelocity,
                moveSmoothTime);
        }
    }

    /// <summary>
    /// 将当前世界坐标吸附到最近网格中心，初始化格子位置。
    /// </summary>
    public void SnapToGrid()
    {
        if (mapGrid == null) return;

        gridPos = mapGrid.WorldToCell(transform.position);
        targetWorldPos = mapGrid.CellToWorld(gridPos);
        transform.position = targetWorldPos;
    }

    /// <summary>
    /// 更新格子坐标并设定平滑移动目标到新格子的世界中心。
    /// </summary>
    public virtual void MoveTo(Vector3Int newPos)
    {
        gridPos = newPos;
        if (mapGrid != null)
            targetWorldPos = mapGrid.CellToWorld(newPos);
    }

    /// <summary>
    /// 被推动时调用。直接将位置沿方向位移一格。
    /// </summary>
    public void ApplyPush(Vector3Int pushDir)
    {
        MoveTo(gridPos + pushDir);
    }

    /// <summary>
    /// 判断是否可沿指定方向被推动（仅检查目标格是否为墙壁）。
    /// </summary>
    public virtual bool CanBePushed(Vector3Int pushDir)
    {
        if (mapGrid == null) return false;

        Vector3Int pushedTarget = gridPos + pushDir;
        return mapGrid.IsWalkable(pushedTarget);
    }

    // ─────────── Gizmos ───────────

    protected virtual void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}
