using UnityEngine;
using Cinemachine;
using System.Collections.Generic;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 四方向枚举，用于表示器官和指针的朝向。
/// </summary>
public enum Direction4
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// 器官容器组件。继承 PushableObject，添加器官类型、心的距离约束、
/// 朝向标记、抓取/释放（手）、摄像机（眼/心）等器官特有行为。
/// </summary>
public class OrganUnit : PushableObject
{
    [Header("器官设定")]
    [SerializeField] private OrganType organType;

    [Header("朝向")]
    [SerializeField] private Transform pointerPivot;  // 子物体偏心指针，初始朝上

    private float chargeBarFillAmount
    {
        get
        {
            return chargeBarImage.fillAmount;
        }
        set
        {
            chargeBarImage.fillAmount = value;
            if(value == 0)
            {
                chargeBarTransform.gameObject.SetActive(false);
            }
            else
            {
                chargeBarTransform.gameObject.SetActive(true);
            }
        }
    }
    [Header("蓄力")]
    [SerializeField] private Image chargeBarImage;  // 蓄力条填充 Image，用 fillAmount 控制
    [SerializeField] private Transform chargeBarTransform;  // 蓄力条父物体

    [Header("约束")]
    [SerializeField] private int maxHeartDistance = 8;

    [Tooltip(
        "蓄力踢后的回弹目标距离。" +
        "器官会回缩到距离心脏不大于该值的位置。" +
        "该值应小于等于 Max Heart Distance。"
    )]
    [SerializeField, Min(1)]
    private int kickReturnHeartDistance = 3;

    [Header("视觉")]
    [SerializeField] private OrganSpriteConfig spriteConfig;

    // ─────────── 运行时状态 ───────────
    private List<PushableObject> grabbedTargets = new List<PushableObject>();
    private SpriteRenderer spriteRenderer;
    private OrganLink heartLinkRenderer;
    private OrganUnit heartUnit;

    private CinemachineVirtualCamera vcam;
    private Direction4 facingDirection = Direction4.Up;

    /// <summary>器官类型</summary>
    public OrganType OrganType => organType;

    /// <summary>
    /// 运行时切换器官类型，同步更新精灵、摄像机状态和蓄力条显隐。
    /// OrganController 负责同步其分类列表。
    /// </summary>
    public void SwitchOrganType(OrganType newType)
    {
        if (organType == newType) return;
        organType = newType;

        // 更新精灵（交替使用两张图）
        if (spriteConfig != null && spriteRenderer != null)
            spriteRenderer.sprite = spriteConfig.GetAlternatingSprite(organType);

        // 新的 Hand/Foot/Eye → 默认关闭摄像机（OrganController 管理激活）
        if (vcam != null)
            vcam.gameObject.SetActive(false);

        // 新类型不是 Foot → 隐藏蓄力条
        if (organType != OrganType.Foot && chargeBarImage != null)
        {
            chargeBarImage.fillAmount = 0f;
            if (chargeBarTransform != null)
                chargeBarTransform.gameObject.SetActive(false);
        }
        else
        {
            if (chargeBarTransform != null)
                chargeBarTransform.gameObject.SetActive(true);
        }
    }

    /// <summary>心的引用（由 OrganController 注入）</summary>
    public OrganUnit HeartUnit
    {
        get => heartUnit;

        set
        {
            heartUnit = value;
            RefreshHeartLink();
        }
    }

    /// <summary>当前被抓取的目标列表（仅 Hand）</summary>
    public List<PushableObject> GrabbedTargets => grabbedTargets;

    /// <summary>是否正在抓取任何物体</summary>
    public bool IsGrabbing => grabbedTargets.Count > 0;

    /// <summary>当前朝向（由最后一次移动方向决定）</summary>
    public Direction4 FacingDirection => facingDirection;

    /// <summary>子对象 SpriteRenderer 的世界位置</summary>
    public override Vector3 VisualCenter =>
        spriteRenderer != null ? spriteRenderer.transform.position : transform.position;

    /// <summary>该器官距离心的最大曼哈顿距离限制（优先读取 SpriteConfig 的类型配置）</summary>
    public int MaxHeartDistance =>
        spriteConfig != null
            ? spriteConfig.GetHeartDistance(organType)
            : maxHeartDistance;

    /// <summary>
    /// 蓄力踢后器官回缩到的目标心脏距离。
    /// 最终值不会超过最大允许距离。
    /// </summary>
    public int KickReturnHeartDistance =>
        Mathf.Clamp(
            kickReturnHeartDistance,
            1,
            Mathf.Max(1, MaxHeartDistance)
        );

    // ─────────── 摄像机 ───────────

    /// <summary>
    /// 开关该器官绑定的 CinemachineVirtualCamera。
    /// 仅当子对象中存在 vcam 且此器官为 Eye 或 Heart 时有效。
    /// </summary>
    public void SetCameraActive(bool active)
    {
        if (vcam == null) return;
        if (organType != OrganType.Eye && organType != OrganType.Heart) return;
        vcam.gameObject.SetActive(active);
    }

    /// <summary>该器官是否配有可用摄像机</summary>
    public bool HasCamera => vcam != null;

    // ─────────── 蓄力条 ───────────

    /// <summary>
    /// 设置蓄力条填充量，同时控制其父对象显隐。
    /// 仅 Foot 类型生效，其他类型自动隐藏。
    /// </summary>
    public void SetChargeDisplay(float t)
    {
        if (chargeBarImage == null) return;

        // 只有脚需要蓄力条
        if (organType != OrganType.Foot)
        {
            if (chargeBarImage.transform.parent != null)
                chargeBarImage.transform.parent.gameObject.SetActive(false);
            return;
        }

        chargeBarFillAmount = Mathf.Clamp01(t);
        bool show = t > 0.001f;

        if (chargeBarImage.transform.parent != null)
            chargeBarImage.transform.parent.gameObject.SetActive(show);
    }

    // ─────────── 生命周期 ───────────

    private bool spriteInitialized;

    protected override void Awake()
    {
        base.Awake();

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        //if (lineRenderer == null)
        //    lineRenderer = GetComponentInChildren<LineRenderer>();
        if(heartLinkRenderer == null)
            heartLinkRenderer = GetComponentInChildren<OrganLink>(true);    // 代替 linkRenderer
        if (vcam == null)
            vcam = GetComponentInChildren<CinemachineVirtualCamera>();
        if (pointerPivot == null)
            pointerPivot = transform.Find("Pointer");
        if (chargeBarImage == null)
            chargeBarImage = GetComponentInChildren<UnityEngine.UI.Image>();

        // 交替分配初始 Sprite（仅首次）
        if (!spriteInitialized && spriteConfig != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = spriteConfig.GetAlternatingSprite(organType);
            spriteInitialized = true;
        }

        // 蓄力条初始隐藏
        if (chargeBarImage != null)
            chargeBarFillAmount = 0f;

        if (vcam != null)
            vcam.gameObject.SetActive(false);
    }

    protected override void Start()
    {
        base.Start();
        UpdatePointerRotation();  // 初始状态朝上
    }

    /// <summary>
    /// 覆写基类 MoveTo，额外记录移动方向作为朝向并更新指针旋转。
    /// </summary>
    public override void MoveTo(Vector3Int newPos, Ease ease = Ease.InOutQuad, float? duration = null)
    {
        Vector3Int delta = newPos - gridPos;
        base.MoveTo(newPos, ease, duration);

        if (delta != Vector3Int.zero)
        {
            facingDirection = DeltaToDirection(delta);
            UpdatePointerRotation();
        }
    }

    /// <summary>将移动向量转换为四方向枚举。</summary>
    private static Direction4 DeltaToDirection(Vector3Int delta)
    {
        if (delta.y > 0) return Direction4.Up;
        if (delta.y < 0) return Direction4.Down;
        if (delta.x < 0) return Direction4.Left;
        if (delta.x > 0) return Direction4.Right;
        return Direction4.Up; // 兜底
    }

    /// <summary>将 pointerPivot 子物体的旋转对齐到当前 facingDirection。</summary>
    private void UpdatePointerRotation()
    {
        if (pointerPivot == null) return;

        float angle = facingDirection switch
        {
            Direction4.Up    => 0f,
            Direction4.Down  => 180f,
            Direction4.Left  => 90f,
            Direction4.Right => -90f,
            _ => 0f
        };

        pointerPivot.localRotation = Quaternion.Euler(0, 0, angle);
    }

    private void OnValidate()
    {
        if (spriteConfig == null) return;

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.sprite = spriteConfig.GetSprite(organType);
    }

    // ─────────── 能力判断 ───────────

    /// <summary>
    /// 判断能否向指定目标格移动。检查走步 + 心距离限制。
    /// 心本身不可独立移动。
    /// </summary>
    public bool CanMoveTo(Vector3Int targetPos)
    {
        var grid = SafeGrid;
        if (grid == null) return false;

        if (organType == OrganType.Heart)
            return false;

        if (!grid.IsWalkable(targetPos))
            return false;

        if (!IsWithinHeartRange(targetPos))
            return false;

        return true;
    }

    /// <summary>
    /// 器官被推动时，除了基类的墙壁检查，还需要检查心距离约束。
    /// 在推进链中由 OrganController 调用 IsWithinHeartRange 额外校验。
    /// </summary>
    public override bool CanBePushed(Vector3Int pushDir)
    {
        return base.CanBePushed(pushDir);
    }

    // ─────────── 移动 ───────────

    /// <summary>
    /// 尝试向指定方向移动一格。包含网格走步 + 心距离等所有合法性检查。
    /// OrganController 或其他调用方均可直接调用。
    /// </summary>
    /// <returns>是否成功移动</returns>
    public bool TryMoveSelf(Vector3Int dir)
    {
        var ctrl = GameBootstrap.Instance?.OrganController;
        if (ctrl != null)
            return ctrl.InvokeTryMoveWithPush(this, dir, canPush: true, 0f);

        Vector3Int target = gridPos + dir;
        if (!CanMoveTo(target)) return false;
        MoveTo(target);
        return true;
    }

    /// <summary>
    /// 检查指定单元格是否有其他可推动物体占据（排除自身）。
    /// </summary>
    public bool IsCellOccupied(Vector3Int cellPos)
    {
        var ctrl = GameBootstrap.Instance?.OrganController;
        return ctrl != null && ctrl.GetPushableAt(cellPos, exclude: this) != null;
    }

    /// <summary>
    /// 检测当前位置偏移 offset 后是否仍在心的约束范围内。
    /// </summary>
    /// <param name="offset">当前格子坐标的偏移量</param>
    /// <returns>在范围内返回 true</returns>
    public bool CheckInHeartRange(Vector3Int offset)
    {
        return IsWithinHeartRange(gridPos + offset);
    }

    // ─────────── 心距离相关 ───────────

    /// <summary>
    /// 当前是否超出心的距离范围。
    /// </summary>
    public bool IsOutOfHeartRange()
    {
        if (organType == OrganType.Heart) return false;

        int maxDist = MaxHeartDistance;
        if (HeartUnit == null || maxDist <= 0) return false;

        int dist = Mathf.Abs(gridPos.x - HeartUnit.gridPos.x)
                 + Mathf.Abs(gridPos.y - HeartUnit.gridPos.y);
        return dist > maxDist;
    }

    /// <summary>
    /// 获取向心拉动的方向（优先差距大的轴）。
    /// </summary>
    public Vector3Int GetPullDirectionTowardHeart()
    {
        if (HeartUnit == null) return Vector3Int.zero;

        int dx = HeartUnit.gridPos.x - gridPos.x;
        int dy = HeartUnit.gridPos.y - gridPos.y;

        if (Mathf.Abs(dx) >= Mathf.Abs(dy))
            return new Vector3Int(dx > 0 ? 1 : (dx < 0 ? -1 : 0), 0, 0);
        else
            return new Vector3Int(0, dy > 0 ? 1 : (dy < 0 ? -1 : 0), 0);
    }

    /// <summary>
    /// 检查指定位置是否在心的约束范围内（曼哈顿距离）。
    /// </summary>
    public bool IsWithinHeartRange(Vector3Int checkPos)
    {
        if (organType == OrganType.Heart) return true;

        int maxDist = MaxHeartDistance;
        if (HeartUnit == null || maxDist <= 0) return true;

        int dist = Mathf.Abs(checkPos.x - HeartUnit.gridPos.x)
                 + Mathf.Abs(checkPos.y - HeartUnit.gridPos.y);
        return dist <= maxDist;
    }

    // ─────────── 手抓取 ───────────

    public void DoSpecial()
    {
        switch (organType)
        {
            case OrganType.Eye:
                Debug.Log("[OrganUnit] Eye 特殊动作（暂未实现）。");
                break;
        }
    }

    /// <summary>批量添加抓取目标。</summary>
    public void AddGrabbed(PushableObject target)
    {
        if (organType != OrganType.Hand) return;
        if (!grabbedTargets.Contains(target))
            grabbedTargets.Add(target);
    }

    /// <summary>释放所有抓取目标。</summary>
    public void ReleaseAllGrabbed()
    {
        if (grabbedTargets.Count == 0) return;
        Debug.Log($"[OrganUnit] Hand 释放了 {grabbedTargets.Count} 个物体");
        grabbedTargets.Clear();
    }

    // ─────────── 连线 ───────────

    //private void UpdateHeartLine()
    //{
    //    if (lineRenderer == null || organType == OrganType.Heart) return;

    //    if (HeartUnit != null)
    //    {
    //        lineRenderer.enabled = true;
    //        lineRenderer.SetPosition(0, VisualCenter);
    //        lineRenderer.SetPosition(1, HeartUnit.VisualCenter);
    //    }
    //    else
    //    {
    //        lineRenderer.enabled = false;
    //    }
    //}

    private void RefreshHeartLink()
    {
        if (heartLinkRenderer == null)
            return;

        // 心脏自身不连接自己。
        if (organType == OrganType.Heart || HeartUnit == null)
        {
            heartLinkRenderer.Clear();
            return;
        }

        heartLinkRenderer.Bind(this, HeartUnit);
    }

    // ─────────── Gizmos ───────────

    protected override void OnDrawGizmos()
    {
        Color typeColor = organType switch
        {
            OrganType.Heart => Color.red,
            OrganType.Foot  => Color.blue,
            OrganType.Hand  => Color.green,
            OrganType.Eye   => Color.yellow,
            _               => Color.gray
        };
        Gizmos.color = typeColor;
        Gizmos.DrawWireSphere(transform.position, 0.35f);

        // 手抓取连线
        if (organType == OrganType.Hand)
        {
            Gizmos.color = Color.magenta;
            foreach (var g in grabbedTargets)
            {
                if (g != null)
                    Gizmos.DrawLine(transform.position, g.transform.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (SafeGrid == null || HeartUnit == null) return;

        Gizmos.color = organType == OrganType.Heart
            ? Color.red
            : new Color(1f, 0.5f, 0.2f, 0.5f);

        Gizmos.DrawLine(transform.position, HeartUnit.transform.position);

        if (organType != OrganType.Heart && MaxHeartDistance > 0)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            float r = MaxHeartDistance;
            Vector3 size = new Vector3(r * 2f + 1f, r * 2f + 1f, 0.01f);
            Gizmos.DrawWireCube(HeartUnit.transform.position, size);
        }
    }
}
