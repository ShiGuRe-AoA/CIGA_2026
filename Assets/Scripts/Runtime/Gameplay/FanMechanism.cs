using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 吹风机机关。
/// 被 Trigger 触发时切换开启/关闭状态；开启后周期性推动风向两格内的可推动物体。
/// </summary>
public class FanMechanism : Mechanism
{
    [Header("方向")]
    [SerializeField] private Direction4 windDirection = Direction4.Right;

    [Header("视觉")]
    [Tooltip("随风向旋转的子物体（初始朝上）。")]
    [SerializeField] private Transform visualSprite;

    [Tooltip("开启状态显示物。为空则不处理。")]
    [SerializeField] private GameObject activeVisual;

    [Tooltip("关闭状态显示物。为空则不处理。")]
    [SerializeField] private GameObject inactiveVisual;

    [Header("节奏")]
    [SerializeField] private bool initiallyActive;
    [SerializeField, Min(0.1f)] private float interval = 1f;
    [SerializeField, Min(0f)] private float initialDelay = 0f;

    [Header("动画")]
    [Tooltip("每推动画时长，<=0 则使用被推物体自身 MoveDuration。")]
    [SerializeField, Min(0f)] private float pushDuration = 0f;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    private readonly List<PushableObject> scanBuffer = new List<PushableObject>();
    private readonly HashSet<PushableObject> pushedThisGust = new HashSet<PushableObject>();

    private OrganController controller;
    private MapGrid grid;
    private Vector3Int gridPos;
    private float timer;

    /// <summary>吹风机所在格子。</summary>
    public Vector3Int GridPos => gridPos;

    private void Start()
    {
        var bootstrap = GameBootstrap.Instance;
        controller = bootstrap?.OrganController;
        grid = bootstrap?.MapGrid;

        if (grid != null)
        {
            gridPos = grid.WorldToCell(transform.position);
            transform.position = grid.CellToWorld(gridPos);
        }

        timer = initialDelay;
        SetActive(initiallyActive);

        SyncVisualRotation();
    }

    private void Update()
    {
        if (!isActive || controller == null || grid == null)
            return;

        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        timer = interval;
        BlowOnce();
    }

    /// <summary>
    /// Trigger 触发时切换吹风机开关状态。
    /// </summary>
    public override void OnTriggered(Trigger source)
    {
        Toggle();
    }

    /// <summary>
    /// 压力板释放时关闭吹风机。
    /// </summary>
    public override void OnClosed(Trigger source)
    {
        base.OnClosed(source);
        Close();
    }

    /// <summary>开启吹风机。</summary>
    public void Open()
    {
        SetActive(true);
    }

    /// <summary>关闭吹风机。</summary>
    public void Close()
    {
        SetActive(false);
    }

    /// <summary>切换吹风机开关状态。</summary>
    public void Toggle()
    {
        SetActive(!isActive);
    }

    private void SetActive(bool active)
    {
        if (isActive == active)
            return;

        isActive = active;
        timer = interval;
        RefreshVisual();

        Log(isActive ? "已开启" : "已关闭");
    }

    private void BlowOnce()
    {
        Vector3Int dir = DirectionToVector(windDirection);
        scanBuffer.Clear();
        pushedThisGust.Clear();

        // 从远到近处理，避免近处物体先把远处物体顶走后又重复推动。
        for (int distance = 2; distance >= 1; distance--)
        {
            PushableObject obj = controller.GetPushableAt(
                gridPos + dir * distance
            );

            if (obj != null && !scanBuffer.Contains(obj))
                scanBuffer.Add(obj);
        }

        foreach (PushableObject obj in scanBuffer)
        {
            if (obj == null || pushedThisGust.Contains(obj))
                continue;

            if (controller.InvokeTryMoveWithPush(
                    obj,
                    dir,
                    canPush: true,
                    pushDuration))
            {
                pushedThisGust.Add(obj);
            }
        }
    }

    private void RefreshVisual()
    {
        if (activeVisual != null)
            activeVisual.SetActive(isActive);

        if (inactiveVisual != null)
            inactiveVisual.SetActive(!isActive);
    }

    private void SyncVisualRotation()
    {
        if (visualSprite == null)
            return;

        float angle = windDirection switch
        {
            Direction4.Up => 0f,
            Direction4.Down => 180f,
            Direction4.Left => 90f,
            Direction4.Right => -90f,
            _ => 0f
        };

        visualSprite.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private static Vector3Int DirectionToVector(Direction4 dir)
    {
        return dir switch
        {
            Direction4.Up => Vector3Int.up,
            Direction4.Down => Vector3Int.down,
            Direction4.Left => Vector3Int.left,
            Direction4.Right => Vector3Int.right,
            _ => Vector3Int.right
        };
    }

    private void OnDrawGizmos()
    {
        Vector3Int dir = DirectionToVector(windDirection);
        Vector3 center = transform.position;

        Gizmos.color = isActive
            ? new Color(0.2f, 0.8f, 1f, 0.35f)
            : new Color(0.45f, 0.45f, 0.45f, 0.25f);

        Gizmos.DrawWireCube(center, Vector3.one * 0.85f);

        for (int distance = 1; distance <= 2; distance++)
        {
            Vector3 cellCenter = center + (Vector3)dir * distance;
            Gizmos.DrawWireCube(cellCenter, Vector3.one * 0.75f);
        }

        Vector3 arrowStart = center + (Vector3)dir * 0.2f;
        Vector3 arrowEnd = center + (Vector3)dir * 2.45f;
        Gizmos.DrawLine(arrowStart, arrowEnd);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        interval = Mathf.Max(0.1f, interval);
        initialDelay = Mathf.Max(0f, initialDelay);
        pushDuration = Mathf.Max(0f, pushDuration);

        if (!Application.isPlaying)
            SyncVisualRotation();
    }
#endif

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log($"[FanMechanism] {name}：{message}", this);
    }
}
