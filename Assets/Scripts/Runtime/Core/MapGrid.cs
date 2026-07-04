using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 地图网格划分组件。
///
/// 根据 Tilemap 中绘制的 Tile 分析地图布局：
/// 有 Tile 的格子视为墙壁，无 Tile 的格子视为空地。
///
/// 此外负责维护：
/// 1. Impact Trigger 注册的动态阻挡格；
/// 2. 场景中的坑洞格；
/// 3. 普通行走与特殊可推动物进入坑洞的不同判定；
/// 4. 当前活动器官列表与活动器官描边。
///
/// Tilemap 仅用于定义地图数据，不参与实际游戏渲染。
/// </summary>
public class MapGrid : MonoBehaviour
{
    public enum CellType
    {
        Empty,
        Wall,
    }

    // ─────────── Shader 属性 ───────────

    private static readonly int ActiveOutlineEnabledId =
        Shader.PropertyToID("_ActiveOutlineEnabled");

    private static readonly int ActiveOutlineColorId =
        Shader.PropertyToID("_ActiveOutlineColor");

    [Header("地图数据")]
    [SerializeField]
    private Tilemap tilemap;

    [Header("活动器官描边")]
    [Tooltip("由 RegisterActiveOrgan 注册的器官所使用的描边颜色。")]
    [SerializeField]
    private Color activeOrganOutlineColor =
        Color.white;

    [Header("Gizmos 可视化")]
    [SerializeField]
    private bool showGizmos = true;

    [SerializeField]
    private Color wallColor =
        new Color(1f, 0f, 0f, 0.3f);

    [SerializeField]
    private Color emptyColor =
        new Color(0f, 1f, 0f, 0.06f);

    [SerializeField]
    private Color gridLineColor =
        new Color(0.5f, 0.5f, 0.5f, 0.4f);

    [SerializeField]
    private Color unfilledPitColor =
        new Color(0.25f, 0.1f, 0.05f, 0.55f);

    [SerializeField]
    private bool showWireframe = true;

    [Header("器官持有数量")]
    [SerializeField, Min(0)] private int heldHeartCount = 1;
    [SerializeField, Min(0)] private int heldFootCount = 1;
    [SerializeField, Min(0)] private int heldHandCount = 1;
    [SerializeField, Min(0)] private int heldEyeCount = 1;

    // ─────────── 基础网格数据 ───────────

    private CellType[,] cells;
    private BoundsInt gridBounds;
    private bool initialized;
    private Grid grid;

    // ─────────── 动态格子数据 ───────────

    /// <summary>
    /// 由 Impact Trigger 等组件注册的动态障碍格。
    /// </summary>
    private readonly HashSet<Vector3Int> blockerCells =
        new HashSet<Vector3Int>();

    /// <summary>
    /// 坑洞格索引。
    ///
    /// Key：坑洞所在格子；
    /// Value：负责该坑状态的 PitMechanism。
    /// </summary>
    private readonly Dictionary<Vector3Int, PitMechanism> pits =
        new Dictionary<Vector3Int, PitMechanism>();

    // ─────────── 活动器官数据 ───────────

    /// <summary>
    /// 当前被标记为活动的器官。
    ///
    /// 必须通过 RegisterActiveOrgan、
    /// UnregisterActiveOrgan 或 ClearActiveOrgans 修改，
    /// 以保证列表状态和描边状态同步。
    /// </summary>
    private readonly List<OrganUnit> activeOrgans =
        new List<OrganUnit>();

    /// <summary>
    /// 缓存每个器官下的 SpriteRenderer。
    /// </summary>
    private readonly Dictionary<OrganUnit, SpriteRenderer[]>
        organRendererCache =
            new Dictionary<OrganUnit, SpriteRenderer[]>();

    /// <summary>
    /// 重复使用的 MaterialPropertyBlock。
    /// 修改前会先读取目标 Renderer 已有的属性块，
    /// 因此不会覆盖 Hover 描边等其他逐 Renderer 参数。
    /// </summary>
    private MaterialPropertyBlock outlinePropertyBlock;

    // ─────────── 公开属性 ───────────

    /// <summary>地图宽度，单位为格。</summary>
    public int Width { get; private set; }

    /// <summary>地图高度，单位为格。</summary>
    public int Height { get; private set; }

    /// <summary>地图包围盒最小格坐标。</summary>
    public Vector3Int Origin => gridBounds.min;

    /// <summary>当前活动器官的只读列表。</summary>
    public IReadOnlyList<OrganUnit> ActiveOrgans =>
        activeOrgans;

    /// <summary>
    /// 当移动被某格阻挡时触发。
    /// 参数为造成阻挡的格子坐标。
    /// </summary>
    public event System.Action<Vector3Int> OnCellBlocked;

    /// <summary>
    /// 获取指定器官类型当前持有数量。
    /// </summary>
    public int GetHeldOrganCount(OrganType type)
    {
        return type switch
        {
            OrganType.Heart => heldHeartCount,
            OrganType.Foot => heldFootCount,
            OrganType.Hand => heldHandCount,
            OrganType.Eye => heldEyeCount,
            _ => 0
        };
    }

    /// <summary>
    /// 增加指定器官类型的持有数量。
    /// </summary>
    public void AddHeldOrganCount(OrganType type, int amount = 1)
    {
        if (amount <= 0)
            return;

        switch (type)
        {
            case OrganType.Heart:
                heldHeartCount += amount;
                break;
            case OrganType.Foot:
                heldFootCount += amount;
                break;
            case OrganType.Hand:
                heldHandCount += amount;
                break;
            case OrganType.Eye:
                heldEyeCount += amount;
                break;
        }
    }

    private void Awake()
    {
        outlinePropertyBlock =
            new MaterialPropertyBlock();

        Initialize();
    }

    private void OnDisable()
    {
        ClearActiveOrgans();
    }

    private void OnDestroy()
    {
        ClearActiveOrgans();
        organRendererCache.Clear();
    }

    // ─────────── 活动器官注册 ───────────

    /// <summary>
    /// 将器官注册为活动器官，并开启绿色活动描边。
    /// </summary>
    public void RegisterActiveOrgan(OrganUnit organ)
    {
        if (organ == null)
            return;

        if (activeOrgans.Contains(organ))
            return;

        activeOrgans.Add(organ);

        SetActiveOrganOutline(
            organ,
            enabled: true
        );
    }

    /// <summary>
    /// 将器官从活动列表移除，并关闭活动描边。
    ///
    /// 这里只关闭 _ActiveOutlineEnabled，
    /// 不会影响 OrganSwitchSlot 设置的 Hover 描边。
    /// </summary>
    public void UnregisterActiveOrgan(OrganUnit organ)
    {
        if (organ == null)
            return;

        if (!activeOrgans.Remove(organ))
            return;

        SetActiveOrganOutline(
            organ,
            enabled: false
        );
    }

    /// <summary>
    /// 关闭所有活动器官的活动描边并清空列表。
    /// </summary>
    public void ClearActiveOrgans()
    {
        for (int i = activeOrgans.Count - 1;
             i >= 0;
             i--)
        {
            SetActiveOrganOutline(
                activeOrgans[i],
                enabled: false
            );
        }

        activeOrgans.Clear();
    }

    /// <summary>
    /// 判断器官是否处于活动列表中。
    /// </summary>
    public bool IsActiveOrgan(OrganUnit organ)
    {
        return organ != null &&
               activeOrgans.Contains(organ);
    }

    /// <summary>
    /// 设置器官下所有 SpriteRenderer 的活动描边参数。
    ///
    /// MaterialPropertyBlock 不会实例化材质，
    /// 也不会影响共用同一材质的其他器官。
    /// </summary>
    private void SetActiveOrganOutline(
        OrganUnit organ,
        bool enabled)
    {
        if (organ == null)
            return;

        SpriteRenderer[] renderers =
            GetOrganRenderers(organ);

        if (renderers == null ||
            renderers.Length == 0)
        {
            return;
        }

        foreach (SpriteRenderer spriteRenderer in renderers)
        {
            if (spriteRenderer == null)
                continue;

            Material sharedMaterial =
                spriteRenderer.sharedMaterial;

            if (sharedMaterial == null)
                continue;

            if (!sharedMaterial.HasProperty(
                    ActiveOutlineEnabledId))
            {
                continue;
            }

            /*
             * 必须先读取已有属性块，保留：
             * _HoverOutlineEnabled
             * _HoverOutlineColor
             * 以及其他脚本设置的 Renderer 参数。
             */
            outlinePropertyBlock.Clear();

            spriteRenderer.GetPropertyBlock(
                outlinePropertyBlock
            );

            outlinePropertyBlock.SetFloat(
                ActiveOutlineEnabledId,
                enabled ? 1f : 0f
            );

            if (sharedMaterial.HasProperty(
                    ActiveOutlineColorId))
            {
                outlinePropertyBlock.SetColor(
                    ActiveOutlineColorId,
                    activeOrganOutlineColor
                );
            }

            spriteRenderer.SetPropertyBlock(
                outlinePropertyBlock
            );
        }

        outlinePropertyBlock.Clear();
    }

    /// <summary>
    /// 获取并缓存器官下的全部 SpriteRenderer。
    ///
    /// includeInactive 为 true，
    /// 因为器官切换类型时可能通过启用、禁用子物体
    /// 改变当前显示的 Sprite。
    /// </summary>
    private SpriteRenderer[] GetOrganRenderers(
        OrganUnit organ)
    {
        if (organ == null)
            return null;

        if (organRendererCache.TryGetValue(
                organ,
                out SpriteRenderer[] cachedRenderers))
        {
            return cachedRenderers;
        }

        SpriteRenderer[] renderers =
            organ.GetComponentsInChildren<SpriteRenderer>(
                includeInactive: true
            );

        organRendererCache[organ] =
            renderers;

        return renderers;
    }

    /// <summary>
    /// 清除指定器官的 Renderer 缓存。
    ///
    /// 如果运行时新增、删除或替换了 SpriteRenderer，
    /// 可以在切换完成后调用该方法。
    /// </summary>
    public void RefreshOrganRendererCache(
        OrganUnit organ)
    {
        if (organ == null)
            return;

        organRendererCache.Remove(organ);

        if (activeOrgans.Contains(organ))
        {
            SetActiveOrganOutline(
                organ,
                enabled: true
            );
        }
    }

    /// <summary>
    /// 清理已经被销毁的活动器官。
    /// </summary>
    public void CleanupActiveOrgans()
    {
        for (int i = activeOrgans.Count - 1;
             i >= 0;
             i--)
        {
            if (activeOrgans[i] == null)
                activeOrgans.RemoveAt(i);
        }

        List<OrganUnit> invalidKeys = null;

        foreach (var pair in organRendererCache)
        {
            if (pair.Key != null)
                continue;

            invalidKeys ??=
                new List<OrganUnit>();

            invalidKeys.Add(pair.Key);
        }

        if (invalidKeys == null)
            return;

        foreach (OrganUnit key in invalidKeys)
            organRendererCache.Remove(key);
    }

    // ─────────── 初始化 ───────────

    /// <summary>
    /// 从 Tilemap 读取基础地图数据。
    ///
    /// Tilemap 包围盒内：
    /// 有 Tile → Wall；
    /// 无 Tile → Empty。
    /// </summary>
    public void Initialize()
    {
        if (tilemap == null)
        {
            tilemap =
                GetComponentInChildren<Tilemap>();
        }

        if (tilemap == null)
        {
            Debug.LogError(
                "[MapGrid] 未找到 Tilemap 组件，" +
                "请在 Inspector 中指定。"
            );

            return;
        }

        grid =
            tilemap.layoutGrid;

        if (grid == null)
        {
            Debug.LogError(
                "[MapGrid] Tilemap 所属层级中未找到 Grid 组件。"
            );

            return;
        }

        gridBounds =
            tilemap.cellBounds;

        if (gridBounds.size.x == 0 ||
            gridBounds.size.y == 0)
        {
            Debug.LogWarning(
                "[MapGrid] Tilemap 中未绘制任何 Tile，网格为空。"
            );

            return;
        }

        Width =
            gridBounds.size.x;

        Height =
            gridBounds.size.y;

        cells =
            new CellType[Width, Height];

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector3Int cellPos =
                    new Vector3Int(
                        gridBounds.xMin + x,
                        gridBounds.yMin + y,
                        0
                    );

                TileBase tile =
                    tilemap.GetTile(cellPos);

                cells[x, y] =
                    tile != null
                        ? CellType.Wall
                        : CellType.Empty;
            }
        }

        initialized = true;

        Debug.Log(
            $"[MapGrid] 初始化完成 —— " +
            $"{Width}x{Height}，包围盒: {gridBounds}"
        );
    }

    // ─────────── 基础格子查询 ───────────

    /// <summary>
    /// 获取指定格子的基础类型。
    /// 超出地图范围时返回 Wall。
    /// </summary>
    public CellType GetCell(Vector3Int cellPos)
    {
        if (!initialized)
            Initialize();

        if (cells == null)
            return CellType.Wall;

        int x =
            cellPos.x - gridBounds.xMin;

        int y =
            cellPos.y - gridBounds.yMin;

        if (x < 0 ||
            x >= Width ||
            y < 0 ||
            y >= Height)
        {
            return CellType.Wall;
        }

        return cells[x, y];
    }

    /// <summary>
    /// 判断普通单位能否进入指定格子。
    /// </summary>
    public bool IsWalkable(Vector3Int cellPos)
    {
        if (blockerCells.Contains(cellPos))
            return false;

        if (IsUnfilledPit(cellPos))
            return false;

        return GetCell(cellPos) ==
               CellType.Empty;
    }

    /// <summary>
    /// 判断指定可推动物能否进入目标格。
    ///
    /// 普通空地允许进入；
    /// 未填平坑洞只允许实现 IPitFiller 的可推动物进入。
    /// </summary>
    public bool CanPushInto(
        PushableObject pushable,
        Vector3Int targetPos)
    {
        if (pushable == null)
            return false;

        if (GetCell(targetPos) !=
            CellType.Empty)
        {
            return false;
        }

        if (blockerCells.Contains(targetPos))
            return false;

        if (!TryGetPit(
                targetPos,
                out PitMechanism pit))
        {
            return true;
        }

        if (pit.IsFilled)
            return true;

        if (pushable is not IPitFiller filler)
            return false;

        return filler.CanFillPit(pit);
    }

    // ─────────── 动态障碍 ───────────

    /// <summary>
    /// 注册一个动态阻挡格。
    /// </summary>
    public void RegisterBlocker(Vector3Int cell)
    {
        blockerCells.Add(cell);
    }

    /// <summary>
    /// 注销一个动态阻挡格。
    /// </summary>
    public void UnregisterBlocker(Vector3Int cell)
    {
        blockerCells.Remove(cell);
    }

    /// <summary>
    /// 通知外部系统指定格子的移动被阻挡。
    /// </summary>
    public void NotifyBlocked(Vector3Int cell)
    {
        OnCellBlocked?.Invoke(cell);
    }

    // ─────────── 坑洞注册 ───────────

    /// <summary>
    /// 注册一个坑洞。
    /// </summary>
    public void RegisterPit(PitMechanism pit)
    {
        if (pit == null)
            return;

        Vector3Int cell =
            pit.GridPos;

        if (pits.TryGetValue(
                cell,
                out PitMechanism existingPit) &&
            existingPit != null &&
            existingPit != pit)
        {
            Debug.LogWarning(
                $"[MapGrid] 格子 {cell} 已经存在坑洞 " +
                $"{existingPit.name}，将替换为 {pit.name}。"
            );
        }

        pits[cell] =
            pit;
    }

    /// <summary>
    /// 注销一个坑洞。
    /// </summary>
    public void UnregisterPit(PitMechanism pit)
    {
        if (pit == null)
            return;

        Vector3Int cell =
            pit.GridPos;

        if (!pits.TryGetValue(
                cell,
                out PitMechanism registeredPit))
        {
            return;
        }

        if (registeredPit == pit)
            pits.Remove(cell);
    }

    /// <summary>
    /// 尝试获取指定格子上的坑洞。
    /// </summary>
    public bool TryGetPit(
        Vector3Int cellPos,
        out PitMechanism pit)
    {
        if (!pits.TryGetValue(
                cellPos,
                out pit))
        {
            return false;
        }

        if (pit != null)
            return true;

        pits.Remove(cellPos);

        return false;
    }

    /// <summary>
    /// 判断指定格子是否存在尚未填平的坑洞。
    /// </summary>
    public bool IsUnfilledPit(Vector3Int cellPos)
    {
        return TryGetPit(
                   cellPos,
                   out PitMechanism pit
               ) &&
               !pit.IsFilled;
    }

    /// <summary>
    /// 处理可推动物进入特殊格子后的结算。
    ///
    /// 返回 true 表示可推动物已被特殊格子消耗。
    /// </summary>
    public bool ResolvePushableEnteredCell(
        PushableObject pushable,
        Vector3Int cellPos)
    {
        if (pushable == null)
            return false;

        if (!TryGetPit(
                cellPos,
                out PitMechanism pit))
        {
            return false;
        }

        if (pit.IsFilled)
            return false;

        if (pushable is not IPitFiller filler)
            return false;

        if (!filler.CanFillPit(pit))
            return false;

        return pit.FillWith(pushable);
    }

    // ─────────── 坐标转换 ───────────

    /// <summary>
    /// 将世界坐标转换为格子坐标。
    /// </summary>
    public Vector3Int WorldToCell(Vector3 worldPos)
    {
        return grid != null
            ? grid.WorldToCell(worldPos)
            : Vector3Int.FloorToInt(worldPos);
    }

    /// <summary>
    /// 将格子坐标转换为格子中心世界坐标。
    /// </summary>
    public Vector3 CellToWorld(Vector3Int cellPos)
    {
        return grid != null
            ? grid.GetCellCenterWorld(cellPos)
            : (Vector3Int)cellPos +
              new Vector3(0.5f, 0.5f, 0f);
    }

    // ─────────── Gizmos ───────────

    private void OnDrawGizmos()
    {
        if (!showGizmos ||
            !initialized ||
            cells == null ||
            grid == null)
        {
            return;
        }

        Vector3 cellSize =
            grid.cellSize;

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector3Int cellPos =
                    new Vector3Int(
                        gridBounds.xMin + x,
                        gridBounds.yMin + y,
                        0
                    );

                Vector3 center =
                    grid.GetCellCenterWorld(cellPos);

                bool isWall =
                    cells[x, y] ==
                    CellType.Wall;

                if (isWall)
                {
                    Gizmos.color =
                        wallColor;
                }
                else if (IsUnfilledPit(cellPos))
                {
                    Gizmos.color =
                        unfilledPitColor;
                }
                else
                {
                    Gizmos.color =
                        emptyColor;
                }

                Gizmos.DrawCube(
                    center,
                    cellSize
                );

                if (!showWireframe)
                    continue;

                Gizmos.color =
                    gridLineColor;

                DrawWireCubeGizmo(
                    center,
                    cellSize
                );
            }
        }
    }

    private static void DrawWireCubeGizmo(
        Vector3 center,
        Vector3 size)
    {
        Vector3 half =
            size * 0.5f;

        Vector3 topLeft =
            center +
            new Vector3(
                -half.x,
                half.y,
                0f
            );

        Vector3 topRight =
            center +
            new Vector3(
                half.x,
                half.y,
                0f
            );

        Vector3 bottomLeft =
            center +
            new Vector3(
                -half.x,
                -half.y,
                0f
            );

        Vector3 bottomRight =
            center +
            new Vector3(
                half.x,
                -half.y,
                0f
            );

        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        Gizmos.DrawLine(bottomLeft, topLeft);
    }
}
