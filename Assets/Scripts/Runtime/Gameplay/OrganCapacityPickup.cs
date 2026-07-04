using UnityEngine;

/// <summary>
/// 器官持有数量拾取物。
/// 当心脏移动到本物体所在格时，增加指定器官类型的持有数量并销毁自身。
/// </summary>
public class OrganCapacityPickup : MonoBehaviour
{
    [Header("奖励")]
    [SerializeField] private OrganType organType = OrganType.Hand;

    [SerializeField, Min(1)] private int amount = 1;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    private MapGrid grid;
    private OrganController controller;
    private Vector3Int gridPos;
    private bool collected;

    /// <summary>拾取物所在格子。</summary>
    public Vector3Int GridPos => gridPos;

    private void Start()
    {
        var bootstrap = GameBootstrap.Instance;
        grid = bootstrap?.MapGrid;
        controller = bootstrap?.OrganController;

        if (grid != null)
        {
            gridPos = grid.WorldToCell(transform.position);
            transform.position = grid.CellToWorld(gridPos);
        }
    }

    private void Update()
    {
        if (collected || grid == null || controller == null)
            return;

        OrganUnit heart = controller.HeartUnit;
        if (heart == null || heart.GridPos != gridPos)
            return;

        Collect();
    }

    private void Collect()
    {
        collected = true;

        grid.AddHeldOrganCount(organType, amount);

        Log(
            $"{organType} 持有数量 +{amount}，当前为 " +
            $"{grid.GetHeldOrganCount(organType)}"
        );

        Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = organType switch
        {
            OrganType.Heart => Color.red,
            OrganType.Foot => Color.blue,
            OrganType.Hand => Color.green,
            OrganType.Eye => Color.yellow,
            _ => Color.white
        };

        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.75f);
        Gizmos.DrawSphere(transform.position, 0.12f);
    }

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log($"[OrganCapacityPickup] {name}：{message}", this);
    }
}
