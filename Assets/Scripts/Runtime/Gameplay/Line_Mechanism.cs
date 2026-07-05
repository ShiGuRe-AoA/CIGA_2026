using UnityEngine;

/// <summary>
/// 线机关。
/// 自身作为墙体占据一个网格，触发时显示 SpriteB 物体，关闭时隐藏 SpriteB 物体。
/// </summary>
public class Line_Mechanism : Mechanism
{
    [Header("视觉")]
    [Tooltip("SpriteB 所在物体。OnTriggered 时启用，OnClosed 时关闭。")]
    [SerializeField] private GameObject spriteBObject;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    private Vector3Int gridPos;
    private bool blockerRegistered;

    /// <summary>线机关所在格子。</summary>
    public Vector3Int GridPos => gridPos;

    private MapGrid Grid => GameBootstrap.Instance?.MapGrid;

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

        SetSpriteBActive(false);
    }

    /// <summary>
    /// Trigger 触发时启用 SpriteB 物体。
    /// </summary>
    public override void OnTriggered(Trigger source)
    {
        base.OnTriggered(source);
        SetSpriteBActive(true);
        Log("显示 SpriteB 物体");
    }

    /// <summary>
    /// 压力板释放或关闭时关闭 SpriteB 物体。
    /// </summary>
    public override void OnClosed(Trigger source)
    {
        base.OnClosed(source);
        SetSpriteBActive(false);
        Log("隐藏 SpriteB 物体");
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

    private void SetSpriteBActive(bool active)
    {
        if (spriteBObject == null)
            return;

        spriteBObject.SetActive(active);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = isActive
            ? new Color(0.2f, 0.8f, 1f, 0.35f)
            : new Color(0.3f, 0.3f, 0.9f, 0.25f);

        Gizmos.DrawCube(transform.position, Vector3.one * 0.85f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.95f);
    }

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log($"[Line_Mechanism] {name}：{message}", this);
    }
}
