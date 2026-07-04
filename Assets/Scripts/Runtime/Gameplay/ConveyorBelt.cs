using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 传动带 — 占据一格，周期性将本格上的 PushableObject 向指定方向推动一格。
///
/// 同一帧内每个物体只被推一次：多个连续放置的传动带不会在同周期内反复推动同一物体。
/// </summary>
public class ConveyorBelt : Mechanism
{
    [Header("方向")]
    [SerializeField] private Direction4 pushDirection = Direction4.Right;

    [Header("视觉")]
    [Tooltip("随方向旋转的子物体（初始朝上）。")]
    [SerializeField] private Transform visualSprite;

    [Tooltip("传送带滚动动画机。内部只需要一个循环动画。")]
    [SerializeField] private Animator beltAnimator;

    [Header("节奏")]
    [SerializeField] private bool initiallyActive = true;
    [SerializeField, Min(0.1f)] private float pushInterval = 1.5f;
    [SerializeField, Min(0f)]    private float initialDelay  = 0f;

    [Header("动画")]
    [Tooltip("每推动画时长，<=0 则使用被推物体自身 MoveDuration。")]
    [SerializeField, Min(0f)] private float pushDuration = 0f;

    // ── 同一帧内防止多传动带重复推动同一个物体 ──
    private static int lastPushFrame = -1;
    private static readonly HashSet<PushableObject> pushedThisCycle = new();

    private OrganController controller;
    private MapGrid grid;
    private float timer;
    private Vector3Int myCell;         // 本格坐标，每帧更新

    private void Start()
    {
        var bootstrap = GameBootstrap.Instance;
        controller = bootstrap?.OrganController;
        grid       = bootstrap?.MapGrid;

        if (grid != null)
        {
            myCell = grid.WorldToCell(transform.position);
            transform.position = grid.CellToWorld(myCell);
        }

        timer = initialDelay;
        isActive = initiallyActive;
        RefreshAnimatorState();
        SyncVisualRotation();
    }

    private void Update()
    {
        if (!isActive) return;
        if (controller == null || grid == null) return;

        myCell = grid.WorldToCell(transform.position);

        // 新的一帧 → 清空本轮已推集合
        if (lastPushFrame != Time.frameCount)
        {
            lastPushFrame = Time.frameCount;
            pushedThisCycle.Clear();
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        timer = pushInterval;
        TryPush();
    }

    // ─────────── Mechanism 入口 ───────────

    /// <summary>
    /// 每次触发时在启动/关闭之间切换。
    /// </summary>
    public override void OnTriggered(Trigger source)
    {
        SetActive(!isActive);
    }

    /// <summary>
    /// 压力板释放时关闭传动带。
    /// </summary>
    public override void OnClosed(Trigger source)
    {
        SetActive(false);
    }

    /// <summary>开启传动带。</summary>
    public void Open()
    {
        SetActive(true);
    }

    /// <summary>关闭传动带。</summary>
    public void Close()
    {
        SetActive(false);
    }

    private void SetActive(bool active)
    {
        if (isActive == active)
            return;

        isActive = active;
        timer = pushInterval;
        RefreshAnimatorState();
    }

    private void RefreshAnimatorState()
    {
        if (beltAnimator == null)
            return;

        beltAnimator.enabled = true;
        beltAnimator.speed = isActive ? 1f : 0f;
    }

    // ─────────── 推动逻辑 ───────────

    private void TryPush()
    {
        Vector3Int dir = DirectionToVector(pushDirection);

        PushableObject obj = controller.GetPushableAt(myCell);
        if (obj == null || obj == this) return;

        // 本轮已推过 → 跳过，实现"一下一下"的节奏
        if (pushedThisCycle.Contains(obj)) return;

        if (controller.InvokeTryMoveWithPush(obj, dir, canPush: true, pushDuration))
        {
            // 标记被推物体
            pushedThisCycle.Add(obj);

            // 同时标记推进链中所有受影响的物体（沿方向连续的非空物体）
            MarkChainObjects(myCell + dir, dir);
        }
    }

    /// <summary>
    /// 从 startCell 沿 dir 扫描，将连续紧邻的 PushableObject 全部标记为已推。
    /// 遇空格则停止（推进链不跨空隙）。
    /// </summary>
    private void MarkChainObjects(Vector3Int startCell, Vector3Int dir)
    {
        Vector3Int scan = startCell;
        for (int i = 0; i < 50; i++)
        {
            PushableObject found = controller.GetPushableAt(scan);
            if (found == null) break;       // 空格 → 链结束
            pushedThisCycle.Add(found);
            scan += dir;
        }
    }

    // ─────────── 工具 ───────────

    private static Vector3Int DirectionToVector(Direction4 dir)
    {
        return dir switch
        {
            Direction4.Up    => Vector3Int.up,
            Direction4.Down  => Vector3Int.down,
            Direction4.Left  => Vector3Int.left,
            Direction4.Right => Vector3Int.right,
            _                => Vector3Int.right
        };
    }

    /// <summary>将视觉子物体旋转到与 pushDirection 对齐（初始朝向为 Up）。</summary>
    private void SyncVisualRotation()
    {
        if (visualSprite == null) return;

        float angle = pushDirection switch
        {
            Direction4.Up    => 0f,
            Direction4.Down  => 180f,
            Direction4.Left  => 90f,
            Direction4.Right => -90f,
            _                => 0f
        };
        visualSprite.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ─────────── Gizmos ───────────

    private void OnDrawGizmos()
    {
        Gizmos.color = pushDirection switch
        {
            Direction4.Up    => Color.green,
            Direction4.Down  => Color.red,
            Direction4.Left  => Color.blue,
            Direction4.Right => Color.yellow,
            _                => Color.white
        };

        Vector3 center = transform.position;
        Vector3 dir    = DirectionToVector(pushDirection);

        Gizmos.DrawWireCube(center, Vector3.one * 0.85f);

        // 箭头
        Vector3 arrowBase = center + (Vector3)dir * 0.15f;
        Vector3 arrowTip  = center + (Vector3)dir * 0.55f;
        Gizmos.DrawLine(arrowBase, arrowTip);

        float arrowSize = 0.12f;
        Vector3 perp = pushDirection is Direction4.Up or Direction4.Down
            ? Vector3.right * arrowSize
            : Vector3.up    * arrowSize;

        Gizmos.DrawLine(arrowTip, arrowTip - (Vector3)dir * arrowSize + perp);
        Gizmos.DrawLine(arrowTip, arrowTip - (Vector3)dir * arrowSize - perp);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            var g = GameBootstrap.Instance?.MapGrid;
            if (g != null)
            {
                Vector3Int cell = g.WorldToCell(transform.position);
                transform.position = g.CellToWorld(cell);
            }
            SyncVisualRotation();
        }
    }
#endif
}
