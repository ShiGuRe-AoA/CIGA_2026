using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 触发器工作模式。
/// </summary>
public enum TriggerMode
{
    /// <summary>符合筛选条件的物体进入本格时触发一次</summary>
    Press,

    /// <summary>物体尝试进入本格但被阻挡时触发（本格视为障碍物）</summary>
    Impact,

    /// <summary>仅由外部代码主动调用 Trigger()，不做自动检测</summary>
    Interact
}

/// <summary>
/// 触发器。挂载在关卡格子上的 GameObject，检测符合条件的物体并激活关联的机关。
///
/// Press 模式：每帧检测是否有符合筛选的物体占住本格，从无到有时触发一次。
/// Impact 模式：当物体尝试进入本格但被阻挡时触发一次。
/// Interact 模式：不做自动检测，由外部（如手交互）主动调用 Trigger()。
/// </summary>
public class Trigger : MonoBehaviour
{
    // ─────────── Inspector ───────────

    [Header("模式")]
    [SerializeField] private TriggerMode mode = TriggerMode.Press;

    [Header("压力板")]
    [Tooltip("开启后，Press 条件从满足变为不满足时，会调用关联 Mechanism 的 OnClosed。")]
    [SerializeField] private bool pressurePlate;

    [Header("阻挡")]
    [Tooltip("开启后，本触发器所在格会注册为动态墙体阻挡。常用于 Impact 模式。")]
    [SerializeField] private bool blockMovement;

    [Header("筛选 — 器官")]
    [SerializeField] private bool detectHeart;
    [SerializeField] private bool detectFoot = true;
    [SerializeField] private bool detectHand;
    [SerializeField] private bool detectEye;

    [Header("筛选 — 其他")]
    [SerializeField] private bool detectSceneObject;   // ScenePushable 等
    [SerializeField] private bool detectAnyPushable;   // 任意可推动物体（覆盖以上全部）

    [Header("机关列表")]
    [SerializeField] private List<Mechanism> mechanisms;

    // ─────────── 运行时状态 ───────────

    private Vector3Int gridPos;
    private bool wasPressed;
    private bool blockerRegistered;

    /// <summary>快捷访问 MapGrid 单例</summary>
    private MapGrid Grid => GameBootstrap.Instance?.MapGrid;

    /// <summary>快捷访问 OrganController 单例</summary>
    private OrganController Controller => GameBootstrap.Instance?.OrganController;

    // ─────────── 生命周期 ───────────

    private void Start()
    {
        SnapPosition();

        // 向 GameBootstrap 注册
        if (GameBootstrap.Instance != null)
            GameBootstrap.Instance.AllTriggers.Add(this);

        if (blockMovement)
            RegisterBlocker();

        if (mode == TriggerMode.Impact && Grid != null)
            Grid.OnCellBlocked += HandleCellBlocked;
    }

    private void Update()
    {
        if (mode == TriggerMode.Press)
            DetectPress();
    }

    private void OnDestroy()
    {
        if (GameBootstrap.Instance != null)
            GameBootstrap.Instance.AllTriggers.Remove(this);

        if (Grid != null)
        {
            UnregisterBlocker();
            Grid.OnCellBlocked -= HandleCellBlocked;
        }
    }

    private void OnDrawGizmos()
    {
        Color c = mode switch
        {
            TriggerMode.Press   => new Color(0, 1, 0, 0.3f),
            TriggerMode.Impact  => new Color(1, 0.5f, 0, 0.3f),
            TriggerMode.Interact => new Color(0, 0.5f, 1, 0.3f),
            _ => Color.gray
        };
        Gizmos.color = c;
        Gizmos.DrawCube(transform.position, Vector3.one * 0.9f);

        Gizmos.color = c;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.95f);
    }

    // ─────────── 位置 ───────────

    private void SnapPosition()
    {
        if (Grid == null) return;
        gridPos = Grid.WorldToCell(transform.position);
        transform.position = Grid.CellToWorld(gridPos);
    }

    /// <summary>本触发器所在的格子坐标。</summary>
    public Vector3Int GridPos => gridPos;

    /// <summary>当前是否将本格作为动态墙体阻挡。</summary>
    public bool BlocksMovement => blockMovement;

    /// <summary>
    /// 运行时切换本触发器是否阻挡移动。
    /// </summary>
    public void SetBlocksMovement(bool blocks)
    {
        if (blockMovement == blocks)
            return;

        blockMovement = blocks;

        if (blockMovement)
            RegisterBlocker();
        else
            UnregisterBlocker();
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

    // ─────────── 筛选 ───────────

    /// <summary>
    /// 判断指定 PushableObject 是否符合本触发器的筛选条件。
    /// </summary>
    public bool MatchesFilter(PushableObject obj)
    {
        if (obj == null) return false;

        if (detectAnyPushable) return true;

        if (obj is OrganUnit organ)
        {
            return organ.OrganType switch
            {
                OrganType.Heart => detectHeart,
                OrganType.Foot  => detectFoot,
                OrganType.Hand  => detectHand,
                OrganType.Eye   => detectEye,
                _ => false
            };
        }

        // 非器官 → 场景物体
        return detectSceneObject;
    }

    // ─────────── Press 模式 ───────────

    private void DetectPress()
    {
        if (Controller == null) return;

        PushableObject obj = Controller.GetPushableAt(gridPos);
        bool isPressed = obj != null && MatchesFilter(obj);

        if (isPressed && !wasPressed)
            FireAll();
        else if (!isPressed && wasPressed && pressurePlate)
            CloseAll();

        wasPressed = isPressed;
    }

    // ─────────── Impact 模式 ───────────

    private void HandleCellBlocked(Vector3Int cell)
    {
        if (cell != gridPos) return;
        if (Controller == null) return;

        Vector3Int[] dirs = {
            Vector3Int.up, Vector3Int.down,
            Vector3Int.left, Vector3Int.right
        };

        foreach (var d in dirs)
        {
            PushableObject neighbor = Controller.GetPushableAt(gridPos - d);
            if (neighbor != null && MatchesFilter(neighbor))
            {
                FireAll();
                return;
            }
        }
    }

    // ─────────── Interact / 外部调用 ───────────

    /// <summary>
    /// 手动触发此触发器（Interact 模式或外部强制触发）。
    /// </summary>
    public void Fire()
    {
        FireAll();
    }

    // ─────────── 内部 ───────────

    private void FireAll()
    {
        if (mechanisms == null) return;

        foreach (var mech in mechanisms)
        {
            if (mech != null)
                mech.OnTriggered(this);
        }
    }

    private void CloseAll()
    {
        if (mechanisms == null) return;

        foreach (var mech in mechanisms)
        {
            if (mech != null)
                mech.OnClosed(this);
        }
    }
}
