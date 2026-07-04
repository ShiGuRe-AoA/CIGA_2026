using UnityEngine;

/// <summary>
/// 坑洞机关。
///
/// 该组件同时承担两种职责：
///
/// 1. 作为 Mechanism：
///    可以被按钮、拉杆等其他 Trigger 填平。
///
/// 2. 作为事件源：
///    坑首次变为已填平状态时，主动调用 filledTrigger.Fire()，
///    从而触发门、墙、平台等后续机关。
///
/// 箱子是否能够进入坑由 MapGrid 和 PushContext 判断；
/// 坑本身只负责状态变化与后续机关触发。
/// </summary>
public class PitMechanism : Mechanism
{
    [Header("视觉")]
    [Tooltip("坑尚未填平时显示的对象。")]
    [SerializeField] private GameObject emptyVisual;

    [Tooltip("坑已经填平时显示的对象。")]
    [SerializeField] private GameObject filledVisual;

    [Header("填平事件")]
    [Tooltip(
        "坑从未填平变为已填平时，" +
        "调用该 Trigger.Fire() 激活后续机关。" +
        "建议将该 Trigger 设置为 Interact 模式。"
    )]
    [SerializeField] private Trigger filledTrigger;

    [Header("初始状态")]
    [Tooltip("场景开始时坑是否已经填平。")]
    [SerializeField] private bool initiallyFilled;

    [Header("外部触发")]
    [Tooltip(
        "开启后，其他 Trigger 将本组件作为 Mechanism 触发时，" +
        "会直接填平该坑。"
    )]
    [SerializeField] private bool fillWhenTriggered = true;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    // ─────────── 运行时状态 ───────────

    private Vector3Int gridPos;
    private bool isFilled;
    private bool registered;

    /// <summary>坑洞所在格子。</summary>
    public Vector3Int GridPos => gridPos;

    /// <summary>坑洞当前是否已经填平。</summary>
    public bool IsFilled => isFilled;

    private MapGrid Grid =>
        GameBootstrap.Instance?.MapGrid;

    // ─────────── 生命周期 ───────────

    private void Start()
    {
        InitializePit();
    }

    private void OnDestroy()
    {
        UnregisterFromGrid();
    }

    /// <summary>
    /// 初始化坑洞格子、初始状态、视觉和地图注册。
    /// </summary>
    private void InitializePit()
    {
        if (Grid == null)
        {
            Debug.LogError(
                $"[PitMechanism] {name} 未找到 MapGrid。",
                this
            );
            return;
        }

        gridPos =
            Grid.WorldToCell(transform.position);

        transform.position =
            Grid.CellToWorld(gridPos);

        isFilled = initiallyFilled;

        Grid.RegisterPit(this);
        registered = true;

        RefreshVisual();
    }

    private void UnregisterFromGrid()
    {
        if (!registered)
            return;

        if (Grid != null)
            Grid.UnregisterPit(this);

        registered = false;
    }

    // ─────────── Mechanism 入口 ───────────

    /// <summary>
    /// 被按钮、拉杆等其他 Trigger 激活时调用。
    /// </summary>
    public override void OnTriggered(Trigger source)
    {
        if (!fillWhenTriggered)
            return;

        Fill();
    }

    // ─────────── 填坑判定 ───────────

    /// <summary>
    /// 判断指定可推动物能否填平当前坑洞。
    /// </summary>
    public bool CanBeFilledBy(PushableObject pushable)
    {
        if (isFilled || pushable == null)
            return false;

        if (pushable is not IPitFiller filler)
            return false;

        return filler.CanFillPit(this);
    }

    /// <summary>
    /// 使用指定可推动物填平坑洞。
    ///
    /// 返回 true 表示填坑成功，调用方可将该物体注销并隐藏。
    /// </summary>
    public bool FillWith(PushableObject pushable)
    {
        if (!CanBeFilledBy(pushable))
            return false;

        IPitFiller filler =
            (IPitFiller)pushable;

        // 先通知填坑物体，让其播放音效或记录状态。
        filler.OnFilledPit(this);

        SetFilled(true);

        Log(
            $"{pushable.name} 填平了坑洞 {name}。"
        );

        return true;
    }

    // ─────────── 状态控制 ───────────

    /// <summary>
    /// 直接将坑洞填平。
    /// 可由其他机关或脚本调用。
    /// </summary>
    public void Fill()
    {
        SetFilled(true);
    }

    /// <summary>
    /// 将坑洞重新恢复为未填平状态。
    ///
    /// 只恢复坑状态和视觉，
    /// 不会自动恢复此前被消耗的箱子。
    /// </summary>
    public void Reopen()
    {
        SetFilled(false);
    }

    /// <summary>
    /// 设置坑洞状态。
    ///
    /// 只有从未填平变为已填平时，
    /// 才会主动触发 filledTrigger。
    /// </summary>
    public void SetFilled(bool filled)
    {
        if (isFilled == filled)
            return;

        bool wasFilled = isFilled;

        isFilled = filled;
        RefreshVisual();

        if (!wasFilled && isFilled)
        {
            // 使用当前 Trigger 已有的公共手动触发入口。
            filledTrigger?.Fire();
        }

        Log(
            $"状态切换为：{(isFilled ? "已填平" : "未填平")}。"
        );
    }

    /// <summary>
    /// 根据坑状态切换视觉。
    /// </summary>
    private void RefreshVisual()
    {
        if (emptyVisual != null)
            emptyVisual.SetActive(!isFilled);

        if (filledVisual != null)
            filledVisual.SetActive(isFilled);
    }

    // ─────────── Gizmos ───────────

    private void OnDrawGizmos()
    {
        Color color =
            isFilled
                ? new Color(0.2f, 0.8f, 0.3f, 0.35f)
                : new Color(0.3f, 0.1f, 0.05f, 0.5f);

        Gizmos.color = color;

        Gizmos.DrawCube(
            transform.position,
            Vector3.one * 0.85f
        );

        Gizmos.DrawWireCube(
            transform.position,
            Vector3.one * 0.9f
        );
    }

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log(
            $"[PitMechanism] {name}：{message}",
            this
        );
    }
}