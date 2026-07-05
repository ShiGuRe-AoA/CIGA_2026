using UnityEngine;

/// <summary>
/// 门机关。
///
/// 门默认关闭（在 MapGrid 中注册为动态阻挡格），
/// 被 Trigger 触发后打开：注销阻挡格、替换贴图为打开状态。
/// 不含物理碰撞箱，阻挡通过推箱子 Grid 系统实现。
/// </summary>
public class DoorMechanism : Mechanism
{
    [Header("视觉")]
    [Tooltip("关闭状态贴图（初始）。")]
    [SerializeField] private SpriteRenderer closedSprite;

    [Tooltip("打开状态贴图（触发后替换）。")]
    [SerializeField] private SpriteRenderer openSprite;

    [Header("触发事件")]
    [Tooltip(
        "门从未打开变为打开时，调用该 Trigger.Fire() 激活后续机关。"
    )]
    [SerializeField] private Trigger openedTrigger;

    [Header("外部触发模式")]
    [Tooltip(
        "开启后，其他 Trigger 触发时会打开门。\n" +
        "关闭后需要手动调用 Open()。"
    )]
    [SerializeField] private bool openWhenTriggered = true;

    [Header("初始状态")]
    [Tooltip("场景开始时门是否已打开。")]
    [SerializeField] private bool initiallyOpen;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    // ─────────── 运行时状态 ───────────

    private Vector3Int gridPos;
    private bool isOpen;
    private bool blockerRegistered;

    /// <summary>门所在格子。</summary>
    public Vector3Int GridPos => gridPos;

    /// <summary>门当前是否已打开。</summary>
    public bool IsOpen => isOpen;

    private MapGrid Grid =>
        GameBootstrap.Instance?.MapGrid;

    // ─────────── 生命周期 ───────────

    private void Start()
    {
        InitializeDoor();
    }

    private void OnDestroy()
    {
        RemoveBlocker();
    }

    private void InitializeDoor()
    {
        if (Grid != null)
        {
            gridPos = Grid.WorldToCell(transform.position);
            transform.position = Grid.CellToWorld(gridPos);
        }

        isOpen = initiallyOpen;

        // 初始关闭 → 注册为 Grid 阻挡格
        if (!isOpen)
            AddBlocker();

        RefreshVisual();
    }

    // ─────────── Grid 阻挡 ───────────

    private void AddBlocker()
    {
        if (Grid == null || blockerRegistered) return;
        Grid.RegisterBlocker(gridPos);
        blockerRegistered = true;
    }

    private void RemoveBlocker()
    {
        if (Grid == null || !blockerRegistered) return;
        Grid.UnregisterBlocker(gridPos);
        blockerRegistered = false;
    }

    // ─────────── Mechanism 入口 ───────────

    public override void OnTriggered(Trigger source)
    {
        base.OnTriggered(source);

        if (!openWhenTriggered) return;
        Open();
    }

    /// <summary>
    /// 压力板释放时关闭门。
    /// </summary>
    public override void OnClosed(Trigger source)
    {
        base.OnClosed(source);
        Close();
    }

    // ─────────── 状态控制 ───────────

    /// <summary>打开门：注销 Grid 阻挡格，替换贴图。</summary>
    public void Open()
    {
        SetOpen(true);
    }

    /// <summary>关闭门：注册 Grid 阻挡格，恢复贴图。</summary>
    public void Close()
    {
        SetOpen(false);
    }

    /// <summary>切换门状态。</summary>
    public void Toggle()
    {
        SetOpen(!isOpen);
    }

    private void SetOpen(bool open)
    {
        if (isOpen == open) return;

        bool wasOpen = isOpen;
        isOpen = open;

        if (isOpen)
            RemoveBlocker();
        else
            AddBlocker();

        RefreshVisual();

        // 门首次打开时触发后续机关
        if (!wasOpen && isOpen && openedTrigger != null)
            openedTrigger.Fire();

        if (!wasOpen && isOpen)
            AudioPlayer.PlayOneShot("SFX_DoorOpen");

        Log(isOpen ? "门已打开" : "门已关闭");
    }

    /// <summary>同步贴图到当前状态。</summary>
    private void RefreshVisual()
    {
        if (closedSprite != null)
            closedSprite.enabled = !isOpen;

        if (openSprite != null)
            openSprite.enabled = isOpen;
    }

    // ─────────── Gizmos ───────────

    private void OnDrawGizmos()
    {
        Color color = isOpen
            ? new Color(0.2f, 0.8f, 0.3f, 0.3f)
            : new Color(0.8f, 0.3f, 0.2f, 0.4f);

        Gizmos.color = color;
        Gizmos.DrawCube(transform.position, Vector3.one * 0.85f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.9f);
    }

    private void Log(string message)
    {
        if (!showDebugLog) return;
        Debug.Log($"[DoorMechanism] {name}：{message}", this);
    }
}
