using UnityEngine;

/// <summary>
/// 线机关。
/// 自身作为墙体占据一个网格，触发时切换为贴图 B，关闭时切换为贴图 A。
/// </summary>
public class Line_Mechanism : Mechanism
{
    [Header("视觉")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("关闭状态贴图。")]
    [SerializeField] private Sprite spriteA;

    [Tooltip("触发状态贴图。")]
    [SerializeField] private Sprite spriteB;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    private Vector3Int gridPos;
    private bool blockerRegistered;

    /// <summary>线机关所在格子。</summary>
    public Vector3Int GridPos => gridPos;

    private MapGrid Grid => GameBootstrap.Instance?.MapGrid;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void Start()
    {
        InitializeLine();
    }

    private void OnDestroy()
    {
        UnregisterBlocker();
    }

    private void InitializeLine()
    {
        if (Grid != null)
        {
            gridPos = Grid.WorldToCell(transform.position);
            transform.position = Grid.CellToWorld(gridPos);
            RegisterBlocker();
        }

        ApplySprite(spriteA);
    }

    /// <summary>
    /// Trigger 触发时切换为贴图 B。
    /// </summary>
    public override void OnTriggered(Trigger source)
    {
        base.OnTriggered(source);
        ApplySprite(spriteB);
        Log("切换为贴图 B");
    }

    /// <summary>
    /// 压力板释放或关闭时切换为贴图 A。
    /// </summary>
    public override void OnClosed(Trigger source)
    {
        base.OnClosed(source);
        ApplySprite(spriteA);
        Log("切换为贴图 A");
    }

    private void RegisterBlocker()
    {
        if (Grid == null || blockerRegistered)
            return;

        Grid.RegisterBlocker(gridPos);
        blockerRegistered = true;
    }

    private void UnregisterBlocker()
    {
        if (Grid == null || !blockerRegistered)
            return;

        Grid.UnregisterBlocker(gridPos);
        blockerRegistered = false;
    }

    private void ApplySprite(Sprite sprite)
    {
        if (spriteRenderer == null || sprite == null)
            return;

        spriteRenderer.sprite = sprite;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = isActive
            ? new Color(0.2f, 0.8f, 1f, 0.35f)
            : new Color(0.3f, 0.3f, 0.9f, 0.25f);

        Gizmos.DrawCube(transform.position, Vector3.one * 0.85f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.95f);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }
#endif

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log($"[Line_Mechanism] {name}：{message}", this);
    }
}
