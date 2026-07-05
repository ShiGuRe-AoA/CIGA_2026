using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 基于 MapGrid 的高性能 2D 网格视野系统。
///
/// 特点：
/// 1. 不依赖 Collider2D；
/// 2. 不使用 Physics2D.Raycast；
/// 3. 使用 DDA 算法逐格检测墙体；
/// 4. 仅在视点跨格或摄像机切换时重建；
/// 5. 黑暗 Mesh 仅在摄像机状态变化时重建；
/// 6. 复用 Mesh 数据列表，降低 GC；
/// 7. 根据暴露墙角生成精确硬阴影。
/// </summary>
[DisallowMultipleComponent]
public class CameraVisionMask2D : MonoBehaviour
{
    [Header("引用")]

    [Tooltip("负责切换当前摄像机器官的 OrganController。")]
    [SerializeField]
    private OrganController organController;

    [Tooltip("用于读取墙体格子的 MapGrid。")]
    [SerializeField]
    private MapGrid mapGrid;

    [Tooltip("向 Stencil 写入可见区域的材质。")]
    [SerializeField]
    private Material visionStencilMaterial;

    [Tooltip("绘制不可见区域的黑暗材质。")]
    [SerializeField]
    private Material darknessMaterial;

    [Header("视野范围")]

    [Tooltip("最大视野距离，单位为世界坐标。")]
    [SerializeField, Min(0.1f)]
    private float viewDistance = 15f;

    [Tooltip(
        "圆周基础射线数量。" +
        "墙角射线负责精确阴影，基础射线主要决定圆形边缘精度。"
    )]
    [SerializeField, Range(8, 256)]
    private int baseRayCount = 48;

    [Tooltip(
        "墙角左右射线的微小角度偏移。" +
        "用于形成墙角后方的阴影边缘。"
    )]
    [SerializeField, Range(0.0001f, 0.5f)]
    private float cornerAngleOffset = 0.03f;

    [Tooltip(
        "视点相对当前摄像机器官的位置偏移。"
    )]
    [SerializeField]
    private Vector2 originOffset = Vector2.zero;

    [Header("重建规则")]

    [Tooltip(
        "开启后每帧重建视野。" +
        "通常应关闭，网格游戏只需要在跨格时重建。"
    )]
    [SerializeField]
    private bool rebuildEveryFrame = false;

    [Tooltip(
        "视点没有跨格，但世界位置移动超过该距离时也重建。" +
        "设为 0 表示只根据格子变化。"
    )]
    [SerializeField, Min(0f)]
    private float rebuildWorldMoveThreshold = 0f;

    [Tooltip(
        "地图墙体会运行时变化时开启。" +
        "开启后可以调用 RequestWallCacheRebuild 刷新墙角缓存。"
    )]
    [SerializeField]
    private bool allowRuntimeWallChanges = false;

    [Header("显示")]

    [Tooltip("不可见区域的颜色。")]
    [SerializeField]
    private Color darknessColor =
        new Color(
            0.01f,
            0.015f,
            0.04f,
            0.88f
        );

    [Tooltip("黑暗矩形在摄像机画面之外的额外边距。")]
    [SerializeField, Min(0f)]
    private float darknessPadding = 2f;

    [Tooltip("运行时生成 Mesh 的本地 Z 坐标。")]
    [SerializeField]
    private float meshLocalZ = 0f;

    [Tooltip("Stencil Mesh 的 Sorting Layer。")]
    [SerializeField]
    private string sortingLayerName = "Default";

    [Tooltip("视野 Stencil 的排序值。")]
    [SerializeField]
    private int visionSortingOrder = 32000;

    [Tooltip("黑暗遮罩的排序值。")]
    [SerializeField]
    private int darknessSortingOrder = 32001;

    [Header("调试")]

    [SerializeField]
    private bool drawDebugRays = false;

    [SerializeField]
    private Color debugRayColor = Color.yellow;

    [SerializeField]
    private bool showPerformanceLog = false;

    // ─────────── 当前状态 ───────────

    private OrganUnit currentVisionOrigin;
    private Camera activeCamera;

    private GameObject visionMeshObject;
    private GameObject darknessMeshObject;

    private Mesh visionMesh;
    private Mesh darknessMesh;

    private MeshRenderer visionRenderer;
    private MeshRenderer darknessRenderer;

    private bool forceVisionRebuild = true;
    private bool forceDarknessRebuild = true;
    private bool wallCacheDirty = true;

    private Vector3Int lastOriginCell =
        new Vector3Int(
            int.MinValue,
            int.MinValue,
            int.MinValue
        );

    private Vector2 lastOriginWorld =
        new Vector2(
            float.PositiveInfinity,
            float.PositiveInfinity
        );

    private Camera lastDarknessCamera;
    private Vector3 lastCameraPosition =
        new Vector3(
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity
        );

    private Quaternion lastCameraRotation =
        new Quaternion(
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity
        );

    private float lastCameraOrthographicSize =
        float.PositiveInfinity;

    private float lastCameraFieldOfView =
        float.PositiveInfinity;

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;

    // ─────────── 缓存容器 ───────────

    private readonly List<float> rayAngles =
        new List<float>(1024);

    private readonly List<RayPoint> rayPoints =
        new List<RayPoint>(1024);

    private readonly List<Vector3> visionVertices =
        new List<Vector3>(1025);

    private readonly List<int> visionTriangles =
        new List<int>(3072);

    private readonly List<Vector3> darknessVertices =
        new List<Vector3>(4);

    private readonly List<int> darknessTriangles =
        new List<int>(6);

    /*
     * 保存当前地图所有暴露墙角。
     * 只有墙体边缘角点才需要生成额外射线。
     */
    private readonly List<Vector2> cachedExposedWallCorners =
        new List<Vector2>(2048);

    /*
     * 用整数坐标去重墙角。
     * 每个格子的角点通过“格子边界坐标”编码，
     * 避免相邻墙格反复产生相同角点。
     */
    private readonly HashSet<Vector2Int> cachedCornerKeys =
        new HashSet<Vector2Int>();

    private static readonly int DarknessColorId =
        Shader.PropertyToID("_DarknessColor");

    private const float DirectionEpsilon = 0.000001f;
    private const float HitDistanceOffset = 0.001f;

    private struct RayPoint
    {
        public float Angle;
        public Vector2 WorldPoint;

        public RayPoint(
            float angle,
            Vector2 worldPoint)
        {
            Angle = angle;
            WorldPoint = worldPoint;
        }
    }

    // ─────────── Unity 生命周期 ───────────

    private void Awake()
    {
        ResolveReferences();
        CreateRuntimeObjects();
        ApplyMaterialsAndSorting();
    }

    private void OnEnable()
    {
        SubscribeController();
    }

    private void Start()
    {
        ResolveReferences();
        SubscribeController();

        if (mapGrid != null &&
            !mapGrid.IsInitialized)
        {
            mapGrid.Initialize();
        }

        RebuildWallCornerCache();

        if (organController == null)
        {
            Debug.LogWarning(
                "[CameraVisionMask2D] 未找到 OrganController。"
            );

            SetVisionOrigin(null);
            return;
        }

        SetVisionOrigin(
            organController.ActiveCameraOrgan
        );
    }

    private void OnDisable()
    {
        UnsubscribeController();
    }

    private void OnDestroy()
    {
        UnsubscribeController();

        DestroyRuntimeObject(
            visionMeshObject,
            visionMesh
        );

        DestroyRuntimeObject(
            darknessMeshObject,
            darknessMesh
        );
    }

    private void LateUpdate()
    {
        if (currentVisionOrigin == null ||
            mapGrid == null)
        {
            SetRenderersEnabled(false);
            return;
        }

        RefreshActiveCamera();
        SetRenderersEnabled(true);

        if (wallCacheDirty)
            RebuildWallCornerCache();

        Vector2 originWorld =
            GetCurrentOriginWorld();

        Vector3Int currentOriginCell =
            mapGrid.WorldToCell(originWorld);

        bool originCellChanged =
            currentOriginCell != lastOriginCell;

        bool originMovedEnough =
            rebuildWorldMoveThreshold > 0f &&
            Vector2.Distance(
                originWorld,
                lastOriginWorld
            ) >= rebuildWorldMoveThreshold;

        if (rebuildEveryFrame ||
            forceVisionRebuild ||
            originCellChanged ||
            originMovedEnough)
        {
            RebuildVisionMesh(originWorld);

            lastOriginCell =
                currentOriginCell;

            lastOriginWorld =
                originWorld;

            forceVisionRebuild = false;
        }

        if (ShouldRebuildDarknessMesh())
        {
            RebuildDarknessMesh();
            CacheCurrentCameraState();

            forceDarknessRebuild = false;
        }
    }

    private void OnValidate()
    {
        viewDistance =
            Mathf.Max(0.1f, viewDistance);

        baseRayCount =
            Mathf.Clamp(
                baseRayCount,
                8,
                256
            );

        cornerAngleOffset =
            Mathf.Max(
                0.0001f,
                cornerAngleOffset
            );

        darknessPadding =
            Mathf.Max(
                0f,
                darknessPadding
            );

        rebuildWorldMoveThreshold =
            Mathf.Max(
                0f,
                rebuildWorldMoveThreshold
            );

        ApplyMaterialsAndSorting();

        forceVisionRebuild = true;
        forceDarknessRebuild = true;
        wallCacheDirty = true;
    }

    // ─────────── 外部接口 ───────────

    /// <summary>
    /// 强制下一帧重新生成视野。
    /// </summary>
    public void RequestVisionRebuild()
    {
        forceVisionRebuild = true;
    }

    /// <summary>
    /// 地图墙体运行时改变后调用。
    /// 下一帧重新缓存墙角并重建视野。
    /// </summary>
    public void RequestWallCacheRebuild()
    {
        if (!allowRuntimeWallChanges)
            return;

        wallCacheDirty = true;
        forceVisionRebuild = true;
    }

    /// <summary>
    /// 立即重新缓存墙体角点。
    /// </summary>
    public void RebuildWallCornerCache()
    {
        cachedExposedWallCorners.Clear();
        cachedCornerKeys.Clear();

        wallCacheDirty = false;

        if (mapGrid == null ||
            mapGrid.LayoutGrid == null)
        {
            return;
        }

        BoundsInt bounds =
            mapGrid.GridBounds;

        for (int x = bounds.xMin;
             x < bounds.xMax;
             x++)
        {
            for (int y = bounds.yMin;
                 y < bounds.yMax;
                 y++)
            {
                Vector3Int cell =
                    new Vector3Int(
                        x,
                        y,
                        0
                    );

                if (!mapGrid.BlocksVision(cell))
                    continue;

                CacheExposedCornersForWall(cell);
            }
        }

        forceVisionRebuild = true;

        if (showPerformanceLog)
        {
            Debug.Log(
                $"[CameraVisionMask2D] 墙角缓存完成：" +
                $"{cachedExposedWallCorners.Count} 个暴露角点。"
            );
        }
    }

    // ─────────── 引用与事件 ───────────

    private void ResolveReferences()
    {
        if (organController == null)
        {
            organController =
                FindFirstObjectByType<OrganController>();
        }

        if (mapGrid == null)
        {
            mapGrid =
                GameBootstrap.Instance?.MapGrid;
        }

        if (mapGrid == null)
        {
            mapGrid =
                FindFirstObjectByType<MapGrid>();
        }
    }

    private void SubscribeController()
    {
        if (organController == null)
            return;

        organController.OnActiveCameraOrganChanged -=
            HandleActiveCameraOrganChanged;

        organController.OnActiveCameraOrganChanged +=
            HandleActiveCameraOrganChanged;
    }

    private void UnsubscribeController()
    {
        if (organController == null)
            return;

        organController.OnActiveCameraOrganChanged -=
            HandleActiveCameraOrganChanged;
    }

    private void HandleActiveCameraOrganChanged(
        OrganUnit newCameraOrgan)
    {
        SetVisionOrigin(newCameraOrgan);
    }

    private void SetVisionOrigin(
        OrganUnit newVisionOrigin)
    {
        currentVisionOrigin =
            newVisionOrigin;

        lastOriginCell =
            new Vector3Int(
                int.MinValue,
                int.MinValue,
                int.MinValue
            );

        lastOriginWorld =
            new Vector2(
                float.PositiveInfinity,
                float.PositiveInfinity
            );

        forceVisionRebuild = true;
        forceDarknessRebuild = true;

        RefreshActiveCamera();

        SetRenderersEnabled(
            currentVisionOrigin != null
        );
    }

    private Vector2 GetCurrentOriginWorld()
    {
        Vector3 position =
            currentVisionOrigin.transform.position;

        return new Vector2(
            position.x + originOffset.x,
            position.y + originOffset.y
        );
    }

    /// <summary>
    /// 获取当前摄像机器官下真正启用的 Camera。
    /// </summary>
    private void RefreshActiveCamera()
    {
        Camera previousCamera =
            activeCamera;

        activeCamera = null;

        if (currentVisionOrigin != null)
        {
            Camera[] cameras =
                currentVisionOrigin
                    .GetComponentsInChildren<Camera>(
                        includeInactive: true
                    );

            for (int i = 0;
                 i < cameras.Length;
                 i++)
            {
                Camera candidate =
                    cameras[i];

                if (candidate == null ||
                    !candidate.enabled ||
                    !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                activeCamera = candidate;
                break;
            }
        }

        if (activeCamera == null)
            activeCamera = Camera.main;

        if (previousCamera != activeCamera)
            forceDarknessRebuild = true;
    }

    // ─────────── 运行时对象 ───────────

    private void CreateRuntimeObjects()
    {
        visionMeshObject =
            new GameObject(
                "Vision Stencil Mesh"
            );

        visionMeshObject.transform.SetParent(
            transform,
            false
        );

        MeshFilter visionFilter =
            visionMeshObject.AddComponent<MeshFilter>();

        visionRenderer =
            visionMeshObject.AddComponent<MeshRenderer>();

        visionMesh =
            new Mesh
            {
                name = "Runtime Vision Mesh"
            };

        visionMesh.MarkDynamic();

        visionFilter.sharedMesh =
            visionMesh;

        darknessMeshObject =
            new GameObject(
                "Vision Darkness Mesh"
            );

        darknessMeshObject.transform.SetParent(
            transform,
            false
        );

        MeshFilter darknessFilter =
            darknessMeshObject.AddComponent<MeshFilter>();

        darknessRenderer =
            darknessMeshObject.AddComponent<MeshRenderer>();

        darknessMesh =
            new Mesh
            {
                name = "Runtime Darkness Mesh"
            };

        darknessMesh.MarkDynamic();

        darknessFilter.sharedMesh =
            darknessMesh;

        ApplyMaterialsAndSorting();
    }

    private void ApplyMaterialsAndSorting()
    {
        if (visionRenderer != null)
        {
            if (visionStencilMaterial != null)
            {
                visionRenderer.sharedMaterial =
                    visionStencilMaterial;
            }

            visionRenderer.sortingLayerName =
                sortingLayerName;

            visionRenderer.sortingOrder =
                visionSortingOrder;
        }

        if (darknessRenderer != null)
        {
            if (darknessMaterial != null)
            {
                darknessRenderer.sharedMaterial =
                    darknessMaterial;

                darknessMaterial.SetColor(
                    DarknessColorId,
                    darknessColor
                );
            }

            darknessRenderer.sortingLayerName =
                sortingLayerName;

            darknessRenderer.sortingOrder =
                darknessSortingOrder;
        }
    }

    private void SetRenderersEnabled(bool enabled)
    {
        if (visionRenderer != null)
            visionRenderer.enabled = enabled;

        if (darknessRenderer != null)
            darknessRenderer.enabled = enabled;
    }

    // ─────────── 墙角缓存 ───────────

    /// <summary>
    /// 缓存某个墙格真正暴露在空地侧的角点。
    ///
    /// 完全被其他墙格包围的内部角点不参与射线计算。
    /// </summary>
    private void CacheExposedCornersForWall(
        Vector3Int cell)
    {
        bool leftOpen =
            !mapGrid.BlocksVision(
                cell + Vector3Int.left
            );

        bool rightOpen =
            !mapGrid.BlocksVision(
                cell + Vector3Int.right
            );

        bool bottomOpen =
            !mapGrid.BlocksVision(
                cell + Vector3Int.down
            );

        bool topOpen =
            !mapGrid.BlocksVision(
                cell + Vector3Int.up
            );

        /*
         * 一个角点只要连接的两条边中至少有一条暴露，
         * 就可能形成视线轮廓。
         */
        if (leftOpen || bottomOpen)
        {
            AddCachedCorner(
                cell.x,
                cell.y
            );
        }

        if (leftOpen || topOpen)
        {
            AddCachedCorner(
                cell.x,
                cell.y + 1
            );
        }

        if (rightOpen || topOpen)
        {
            AddCachedCorner(
                cell.x + 1,
                cell.y + 1
            );
        }

        if (rightOpen || bottomOpen)
        {
            AddCachedCorner(
                cell.x + 1,
                cell.y
            );
        }
    }

    /// <summary>
    /// 使用网格边界坐标去重，并转换成世界坐标。
    /// </summary>
    private void AddCachedCorner(
        int gridCornerX,
        int gridCornerY)
    {
        Vector2Int key =
            new Vector2Int(
                gridCornerX,
                gridCornerY
            );

        if (!cachedCornerKeys.Add(key))
            return;

        Vector3 world =
            mapGrid.LayoutGrid.CellToWorld(
                new Vector3Int(
                    gridCornerX,
                    gridCornerY,
                    0
                )
            );

        cachedExposedWallCorners.Add(
            new Vector2(
                world.x,
                world.y
            )
        );
    }

    // ─────────── 视野 Mesh ───────────

    private void RebuildVisionMesh(
        Vector2 origin)
    {
        if (visionMesh == null ||
            mapGrid == null)
        {
            return;
        }

        rayAngles.Clear();
        rayPoints.Clear();

        AddBaseRayAngles();
        AddNearbyWallCornerAngles(origin);
        CastAllVisionRays(origin);

        if (rayPoints.Count < 3)
        {
            visionMesh.Clear();
            return;
        }

        rayPoints.Sort(
            CompareRayPointsByAngle
        );

        BuildVisionMeshData(origin);

        visionMesh.Clear();

        visionMesh.SetVertices(
            visionVertices
        );

        visionMesh.SetTriangles(
            visionTriangles,
            0,
            calculateBounds: false
        );

        visionMesh.RecalculateBounds();

        if (showPerformanceLog)
        {
            Debug.Log(
                $"[CameraVisionMask2D] 重建视野：" +
                $"{rayPoints.Count} 条射线。"
            );
        }
    }

    private static int CompareRayPointsByAngle(
        RayPoint left,
        RayPoint right)
    {
        return left.Angle.CompareTo(
            right.Angle
        );
    }

    private void AddBaseRayAngles()
    {
        float angleStep =
            360f / baseRayCount;

        for (int i = 0;
             i < baseRayCount;
             i++)
        {
            rayAngles.Add(
                i * angleStep
            );
        }
    }

    /// <summary>
    /// 只处理位于当前视距范围内的缓存墙角。
    /// </summary>
    private void AddNearbyWallCornerAngles(
        Vector2 origin)
    {
        float maxDistanceSquared =
            viewDistance * viewDistance;

        for (int i = 0;
             i < cachedExposedWallCorners.Count;
             i++)
        {
            Vector2 corner =
                cachedExposedWallCorners[i];

            Vector2 delta =
                corner - origin;

            float distanceSquared =
                delta.sqrMagnitude;

            if (distanceSquared >
                    maxDistanceSquared ||
                distanceSquared <=
                    Mathf.Epsilon)
            {
                continue;
            }

            float angle =
                Mathf.Atan2(
                    delta.y,
                    delta.x
                ) * Mathf.Rad2Deg;

            AddNormalizedRayAngle(angle);

            AddNormalizedRayAngle(
                angle - cornerAngleOffset
            );

            AddNormalizedRayAngle(
                angle + cornerAngleOffset
            );
        }
    }

    private void AddNormalizedRayAngle(
        float angle)
    {
        rayAngles.Add(
            NormalizeAngle(angle)
        );
    }

    private void CastAllVisionRays(
        Vector2 origin)
    {
        rayAngles.Sort();

        /*
         * 去除非常接近的重复角度，
         * 防止多个墙格共享角点时生成重复射线。
         */
        float previousAngle =
            float.NegativeInfinity;

        for (int i = 0;
             i < rayAngles.Count;
             i++)
        {
            float angle =
                rayAngles[i];

            if (Mathf.Abs(
                    angle - previousAngle
                ) < 0.00001f)
            {
                continue;
            }

            previousAngle = angle;

            float radians =
                angle * Mathf.Deg2Rad;

            Vector2 direction =
                new Vector2(
                    Mathf.Cos(radians),
                    Mathf.Sin(radians)
                );

            Vector2 targetPoint =
                CastGridRayDda(
                    origin,
                    direction
                );

            rayPoints.Add(
                new RayPoint(
                    angle,
                    targetPoint
                )
            );

            if (drawDebugRays)
            {
                Debug.DrawLine(
                    origin,
                    targetPoint,
                    debugRayColor
                );
            }
        }
    }

    /// <summary>
    /// 使用 2D DDA 算法遍历网格。
    ///
    /// 每次直接跳到下一个格子边界，
    /// 每经过一个格子只调用一次 BlocksVision。
    /// </summary>
    private Vector2 CastGridRayDda(
        Vector2 origin,
        Vector2 direction)
    {
        Grid layoutGrid =
            mapGrid.LayoutGrid;

        if (layoutGrid == null)
        {
            return origin +
                   direction * viewDistance;
        }

        Vector3Int currentCell =
            mapGrid.WorldToCell(origin);

        Vector3 currentCellMinWorld =
            layoutGrid.CellToWorld(
                currentCell
            );

        Vector3 nextCellWorld =
            layoutGrid.CellToWorld(
                currentCell +
                Vector3Int.one
            );

        float cellMinX =
            Mathf.Min(
                currentCellMinWorld.x,
                nextCellWorld.x
            );

        float cellMaxX =
            Mathf.Max(
                currentCellMinWorld.x,
                nextCellWorld.x
            );

        float cellMinY =
            Mathf.Min(
                currentCellMinWorld.y,
                nextCellWorld.y
            );

        float cellMaxY =
            Mathf.Max(
                currentCellMinWorld.y,
                nextCellWorld.y
            );

        float cellWidth =
            Mathf.Max(
                Mathf.Abs(
                    cellMaxX - cellMinX
                ),
                DirectionEpsilon
            );

        float cellHeight =
            Mathf.Max(
                Mathf.Abs(
                    cellMaxY - cellMinY
                ),
                DirectionEpsilon
            );

        int stepX;

        if (direction.x > DirectionEpsilon)
            stepX = 1;
        else if (direction.x < -DirectionEpsilon)
            stepX = -1;
        else
            stepX = 0;

        int stepY;

        if (direction.y > DirectionEpsilon)
            stepY = 1;
        else if (direction.y < -DirectionEpsilon)
            stepY = -1;
        else
            stepY = 0;

        float deltaDistanceX =
            stepX == 0
                ? float.PositiveInfinity
                : cellWidth /
                  Mathf.Abs(direction.x);

        float deltaDistanceY =
            stepY == 0
                ? float.PositiveInfinity
                : cellHeight /
                  Mathf.Abs(direction.y);

        float sideDistanceX;

        if (stepX > 0)
        {
            sideDistanceX =
                (cellMaxX - origin.x) /
                direction.x;
        }
        else if (stepX < 0)
        {
            sideDistanceX =
                (origin.x - cellMinX) /
                -direction.x;
        }
        else
        {
            sideDistanceX =
                float.PositiveInfinity;
        }

        float sideDistanceY;

        if (stepY > 0)
        {
            sideDistanceY =
                (cellMaxY - origin.y) /
                direction.y;
        }
        else if (stepY < 0)
        {
            sideDistanceY =
                (origin.y - cellMinY) /
                -direction.y;
        }
        else
        {
            sideDistanceY =
                float.PositiveInfinity;
        }

        float travelledDistance = 0f;

        int maxStepCount =
            Mathf.CeilToInt(
                viewDistance /
                Mathf.Min(
                    cellWidth,
                    cellHeight
                )
            ) * 2 + 8;

        for (int i = 0;
             i < maxStepCount;
             i++)
        {
            /*
             * 当射线恰好经过格子角点时，
             * 同时跨越 X 和 Y，避免只检查其中一侧造成漏光。
             */
            if (Mathf.Abs(
                    sideDistanceX -
                    sideDistanceY
                ) <= DirectionEpsilon)
            {
                travelledDistance =
                    sideDistanceX;

                currentCell.x += stepX;
                currentCell.y += stepY;

                sideDistanceX +=
                    deltaDistanceX;

                sideDistanceY +=
                    deltaDistanceY;
            }
            else if (
                sideDistanceX <
                sideDistanceY)
            {
                travelledDistance =
                    sideDistanceX;

                currentCell.x += stepX;

                sideDistanceX +=
                    deltaDistanceX;
            }
            else
            {
                travelledDistance =
                    sideDistanceY;

                currentCell.y += stepY;

                sideDistanceY +=
                    deltaDistanceY;
            }

            if (travelledDistance >
                viewDistance)
            {
                break;
            }

            if (!mapGrid.BlocksVision(
                    currentCell))
            {
                continue;
            }

            return origin +
                   direction *
                   Mathf.Max(
                       0f,
                       travelledDistance -
                       HitDistanceOffset
                   );
        }

        return origin +
               direction * viewDistance;
    }

    private void BuildVisionMeshData(
        Vector2 origin)
    {
        visionVertices.Clear();
        visionTriangles.Clear();

        visionVertices.Add(
            WorldToVisionMeshLocal(origin)
        );

        int perimeterCount =
            rayPoints.Count;

        for (int i = 0;
             i < perimeterCount;
             i++)
        {
            visionVertices.Add(
                WorldToVisionMeshLocal(
                    rayPoints[i].WorldPoint
                )
            );
        }

        for (int i = 0;
             i < perimeterCount;
             i++)
        {
            int current =
                i + 1;

            int next =
                ((i + 1) %
                 perimeterCount) + 1;

            visionTriangles.Add(0);
            visionTriangles.Add(current);
            visionTriangles.Add(next);
        }
    }

    private Vector3 WorldToVisionMeshLocal(
        Vector2 worldPosition)
    {
        Vector3 world =
            new Vector3(
                worldPosition.x,
                worldPosition.y,
                visionMeshObject.transform.position.z
            );

        Vector3 local =
            visionMeshObject.transform
                .InverseTransformPoint(world);

        local.z = meshLocalZ;

        return local;
    }

    private static float NormalizeAngle(
        float angle)
    {
        angle %= 360f;

        if (angle < 0f)
            angle += 360f;

        return angle;
    }

    // ─────────── 黑暗 Mesh ───────────

    private bool ShouldRebuildDarknessMesh()
    {
        if (forceDarknessRebuild)
            return true;

        if (activeCamera == null)
            return false;

        if (lastDarknessCamera != activeCamera)
            return true;

        Transform cameraTransform =
            activeCamera.transform;

        if (cameraTransform.position !=
            lastCameraPosition)
        {
            return true;
        }

        if (cameraTransform.rotation !=
            lastCameraRotation)
        {
            return true;
        }

        if (activeCamera.orthographic)
        {
            if (!Mathf.Approximately(
                    activeCamera.orthographicSize,
                    lastCameraOrthographicSize
                ))
            {
                return true;
            }
        }
        else
        {
            if (!Mathf.Approximately(
                    activeCamera.fieldOfView,
                    lastCameraFieldOfView
                ))
            {
                return true;
            }
        }

        return Screen.width != lastScreenWidth ||
               Screen.height != lastScreenHeight;
    }

    private void CacheCurrentCameraState()
    {
        if (activeCamera == null)
            return;

        lastDarknessCamera =
            activeCamera;

        lastCameraPosition =
            activeCamera.transform.position;

        lastCameraRotation =
            activeCamera.transform.rotation;

        lastCameraOrthographicSize =
            activeCamera.orthographicSize;

        lastCameraFieldOfView =
            activeCamera.fieldOfView;

        lastScreenWidth =
            Screen.width;

        lastScreenHeight =
            Screen.height;
    }

    private void RebuildDarknessMesh()
    {
        if (darknessMesh == null ||
            darknessMeshObject == null ||
            activeCamera == null)
        {
            return;
        }

        float targetPlaneZ =
            darknessMeshObject.transform.position.z +
            meshLocalZ;

        float planeDistance =
            Mathf.Abs(
                activeCamera.transform.position.z -
                targetPlaneZ
            );

        Vector3 bottomLeftWorld =
            activeCamera.ViewportToWorldPoint(
                new Vector3(
                    0f,
                    0f,
                    planeDistance
                )
            );

        Vector3 bottomRightWorld =
            activeCamera.ViewportToWorldPoint(
                new Vector3(
                    1f,
                    0f,
                    planeDistance
                )
            );

        Vector3 topRightWorld =
            activeCamera.ViewportToWorldPoint(
                new Vector3(
                    1f,
                    1f,
                    planeDistance
                )
            );

        Vector3 topLeftWorld =
            activeCamera.ViewportToWorldPoint(
                new Vector3(
                    0f,
                    1f,
                    planeDistance
                )
            );

        Vector2 center =
            (
                (Vector2)bottomLeftWorld +
                (Vector2)bottomRightWorld +
                (Vector2)topRightWorld +
                (Vector2)topLeftWorld
            ) * 0.25f;

        bottomLeftWorld =
            ExpandCornerFromCenter(
                center,
                bottomLeftWorld,
                darknessPadding
            );

        bottomRightWorld =
            ExpandCornerFromCenter(
                center,
                bottomRightWorld,
                darknessPadding
            );

        topRightWorld =
            ExpandCornerFromCenter(
                center,
                topRightWorld,
                darknessPadding
            );

        topLeftWorld =
            ExpandCornerFromCenter(
                center,
                topLeftWorld,
                darknessPadding
            );

        darknessVertices.Clear();
        darknessTriangles.Clear();

        darknessVertices.Add(
            WorldToDarknessMeshLocal(
                bottomLeftWorld
            )
        );

        darknessVertices.Add(
            WorldToDarknessMeshLocal(
                topLeftWorld
            )
        );

        darknessVertices.Add(
            WorldToDarknessMeshLocal(
                topRightWorld
            )
        );

        darknessVertices.Add(
            WorldToDarknessMeshLocal(
                bottomRightWorld
            )
        );

        darknessTriangles.Add(0);
        darknessTriangles.Add(1);
        darknessTriangles.Add(2);

        darknessTriangles.Add(0);
        darknessTriangles.Add(2);
        darknessTriangles.Add(3);

        darknessMesh.Clear();

        darknessMesh.SetVertices(
            darknessVertices
        );

        darknessMesh.SetTriangles(
            darknessTriangles,
            0,
            calculateBounds: false
        );

        darknessMesh.RecalculateBounds();
    }

    private static Vector3 ExpandCornerFromCenter(
        Vector2 center,
        Vector3 corner,
        float padding)
    {
        Vector2 direction =
            (Vector2)corner - center;

        if (direction.sqrMagnitude <=
            Mathf.Epsilon)
        {
            return corner;
        }

        direction.Normalize();

        corner.x +=
            direction.x * padding;

        corner.y +=
            direction.y * padding;

        return corner;
    }

    private Vector3 WorldToDarknessMeshLocal(
        Vector3 worldPosition)
    {
        Vector3 local =
            darknessMeshObject.transform
                .InverseTransformPoint(
                    worldPosition
                );

        local.z = meshLocalZ;

        return local;
    }

    // ─────────── 清理 ───────────

    private static void DestroyRuntimeObject(
        GameObject targetObject,
        Mesh targetMesh)
    {
        if (targetMesh != null)
        {
            if (Application.isPlaying)
                Destroy(targetMesh);
            else
                DestroyImmediate(targetMesh);
        }

        if (targetObject != null)
        {
            if (Application.isPlaying)
                Destroy(targetObject);
            else
                DestroyImmediate(targetObject);
        }
    }
}