using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 地图网格划分组件。
/// 根据 TileMap 中绘制的 Tile 分析地图布局，将每个格子划分为"墙"或"空地"，
/// 并通过 Gizmos 在 Scene 视图中可视化网格划分结果。
/// TileMap 仅作为关卡数据定义工具，不参与实际游戏渲染。
/// </summary>
public class MapGrid : MonoBehaviour
{
    public enum CellType
    {
        Empty,  // 空地，器官可通行
        Wall,   // 墙壁，不可通行
    }

    [SerializeField] private Tilemap tilemap;

    [Header("Gizmos 可视化")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color wallColor = new Color(1f, 0f, 0f, 0.3f);
    [SerializeField] private Color emptyColor = new Color(0f, 1f, 0f, 0.06f);
    [SerializeField] private Color gridLineColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
    [SerializeField] private bool showWireframe = true;

    private CellType[,] cells;
    private BoundsInt gridBounds;
    private bool initialized;
    private Grid grid;

    /// <summary>地图宽度（格子数）</summary>
    public int Width { get; private set; }

    /// <summary>地图高度（格子数）</summary>
    public int Height { get; private set; }

    /// <summary>包围盒原点（Grid 坐标系）</summary>
    public Vector3Int Origin => gridBounds.min;

    private void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// 从 TileMap 读取 Tile 数据并初始化网格。
    /// 自动计算包围盒，有 Tile 的格子 = 墙壁，无 Tile 的格子 = 空地。
    /// </summary>
    public void Initialize()
    {
        if (tilemap == null)
            tilemap = GetComponentInChildren<Tilemap>();

        if (tilemap == null)
        {
            Debug.LogError("[MapGrid] 未找到 Tilemap 组件，请在 Inspector 中指定。");
            return;
        }

        grid = tilemap.layoutGrid;
        if (grid == null)
        {
            Debug.LogError("[MapGrid] 父对象上未找到 Grid 组件。");
            return;
        }

        // 获取所有已绘制 Tile 的包围盒
        gridBounds = tilemap.cellBounds;

        if (gridBounds.size.x == 0 || gridBounds.size.y == 0)
        {
            Debug.LogWarning("[MapGrid] TileMap 中未绘制任何 Tile，网格为空。");
            return;
        }

        Width = gridBounds.size.x;
        Height = gridBounds.size.y;
        cells = new CellType[Width, Height];

        // 遍历包围盒内所有格子，有 Tile → 墙，无 Tile → 空地
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector3Int cellPos = new Vector3Int(
                    gridBounds.xMin + x,
                    gridBounds.yMin + y,
                    0);

                TileBase tile = tilemap.GetTile(cellPos);
                cells[x, y] = (tile != null) ? CellType.Wall : CellType.Empty;
            }
        }

        initialized = true;
        Debug.Log($"[MapGrid] 初始化完成 —— {Width}x{Height}，包围盒: {gridBounds}");
    }

    /// <summary>
    /// 获取指定格子坐标处的类型。超出地图范围返回 Wall。
    /// </summary>
    /// <param name="cellPos">Grid 坐标系下的格子坐标</param>
    public CellType GetCell(Vector3Int cellPos)
    {
        if (!initialized) Initialize();
        if (cells == null) return CellType.Wall;

        int x = cellPos.x - gridBounds.xMin;
        int y = cellPos.y - gridBounds.yMin;

        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return CellType.Wall;

        return cells[x, y];
    }

    /// <summary>
    /// 判断指定格子是否可行走（即空地）。
    /// </summary>
    public bool IsWalkable(Vector3Int cellPos)
    {
        return GetCell(cellPos) == CellType.Empty;
    }

    /// <summary>
    /// 将世界坐标转换为 Grid 格子坐标。
    /// </summary>
    public Vector3Int WorldToCell(Vector3 worldPos)
    {
        return grid != null
            ? grid.WorldToCell(worldPos)
            : Vector3Int.FloorToInt(worldPos);
    }

    /// <summary>
    /// 将格子坐标转换为世界坐标（格子中心）。
    /// </summary>
    public Vector3 CellToWorld(Vector3Int cellPos)
    {
        return grid != null
            ? grid.GetCellCenterWorld(cellPos)
            : (Vector3Int)cellPos + new Vector3(0.5f, 0.5f, 0);
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos || !initialized || cells == null || grid == null)
            return;

        Vector3 cellSize = grid.cellSize;

        for (int px = 0; px < Width; px++)
        {
            for (int py = 0; py < Height; py++)
            {
                Vector3Int cellPos = new Vector3Int(
                    gridBounds.xMin + px,
                    gridBounds.yMin + py,
                    0);

                Vector3 center = grid.GetCellCenterWorld(cellPos);
                bool isWall = cells[px, py] == CellType.Wall;

                // 填充色：墙壁用红色，空地用浅绿色
                Gizmos.color = isWall ? wallColor : emptyColor;
                Gizmos.DrawCube(center, cellSize);

                // 线框：勾勒格子边界
                if (showWireframe)
                {
                    Gizmos.color = gridLineColor;
                    DrawWireCubeGizmo(center, cellSize);
                }
            }
        }
    }

    private static void DrawWireCubeGizmo(Vector3 center, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        Vector3 tl = center + new Vector3(-h.x,  h.y, 0);
        Vector3 tr = center + new Vector3( h.x,  h.y, 0);
        Vector3 bl = center + new Vector3(-h.x, -h.y, 0);
        Vector3 br = center + new Vector3( h.x, -h.y, 0);

        Gizmos.DrawLine(tl, tr);
        Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl);
        Gizmos.DrawLine(bl, tl);
    }
}
