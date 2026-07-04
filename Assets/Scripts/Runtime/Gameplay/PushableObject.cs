using System;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// 可被推动物体的基类。
/// 提供网格定位、DOTween 平滑移动、被推动等所有可移动场景物体的共有能力。
/// OrganUnit 和 ScenePushable 均继承自此基类。
/// MapGrid 引用通过 GameBootstrap.Instance 获取，无需手动绑定。
/// </summary>
public class PushableObject : MonoBehaviour
{
    [Header("移动")]
    [SerializeField] protected float moveDuration = 0.08f;
    //[SerializeField] protected Ease moveEase = Ease.OutQuad;

    protected Vector3Int gridPos;

    /// <summary>当前格子坐标</summary>
    public Vector3Int GridPos
    {
        get => gridPos;
        set => gridPos = value;
    }

    /// <summary>视觉中心世界坐标（子类可覆写）</summary>
    public virtual Vector3 VisualCenter => transform.position;

    /// <summary>单格移动动画时长</summary>
    public float MoveDuration => moveDuration;

    /// <summary>快捷访问 MapGrid（若未初始化则返回 null）</summary>
    protected MapGrid SafeGrid => GameBootstrap.Instance?.MapGrid;

    /// <summary>
    /// 格子位置变化事件。参数: (物体, 旧位置, 新位置)。
    /// GridPositionIndex 等外部组件通过此事件保持同步。
    /// </summary>
    public event Action<PushableObject, Vector3Int, Vector3Int> OnGridPositionChanged;

    protected virtual void Awake() { }

    protected virtual void Start()
    {
        SnapToGrid();
    }

    /// <summary>
    /// 将当前世界坐标吸附到最近网格中心，初始化格子位置。
    /// </summary>
    public void SnapToGrid()
    {
        var grid = SafeGrid;
        if (grid == null) return;

        gridPos = grid.WorldToCell(transform.position);
        Vector3 worldCenter = grid.CellToWorld(gridPos);
        transform.DOKill();
        transform.position = worldCenter;
    }

    /// <summary>
    /// 更新格子坐标，并用 DOTween 平滑移动到新格子中心。
    /// 会触发 OnGridPositionChanged 事件。
    /// </summary>
    /// <param name="duration">动画时长，null 时使用物体自身 moveDuration</param>
    public virtual void MoveTo(Vector3Int newPos, Ease ease = Ease.OutQuad, float? duration = null)
    {
        Vector3Int oldPos = gridPos;
        gridPos = newPos;

        OnGridPositionChanged?.Invoke(this, oldPos, newPos);

        var grid = SafeGrid;
        if (grid != null)
        {
            Vector3 target = grid.CellToWorld(newPos);
            transform.DOKill();
            transform.DOMove(target, duration ?? moveDuration).SetEase(ease);
        }
    }

    /// <summary>
    /// 被推动时调用。直接将位置沿方向位移一格。
    /// </summary>
    /// <param name="duration">动画时长，null 时使用物体自身 moveDuration</param>
    public void ApplyPush(Vector3Int pushDir, Ease ease = Ease.OutQuad, float? duration = null)
    {
        MoveTo(gridPos + pushDir, ease, duration);
    }

    /// <summary>
    /// 判断是否可沿指定方向被推动（仅检查目标格是否为墙壁）。
    /// </summary>
    public virtual bool CanBePushed(Vector3Int pushDir)
    {
        var grid = SafeGrid;
        if (grid == null) return false;

        Vector3Int pushedTarget = gridPos + pushDir;
        return grid.IsWalkable(pushedTarget);
    }

    // ─────────── Gizmos ───────────

    protected virtual void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}
