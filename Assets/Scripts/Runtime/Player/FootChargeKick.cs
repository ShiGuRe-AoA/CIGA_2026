using UnityEngine;

/// <summary>
/// 脚蓄力踢组件。挂载在脚的 OrganUnit 所在 GameObject 上。
/// 按住 F 蓄力 → 蓄力条填充 → 松开 F 踢出，推动前方物体多格。
/// </summary>
[RequireComponent(typeof(OrganUnit))]
public class FootChargeKick : MonoBehaviour
{
    [Header("蓄力")]
    [SerializeField] private KeyCode chargeKey = KeyCode.F;
    [SerializeField] private float fullChargeTime = 1.2f;
    [SerializeField] private int minKickDistance = 1;
    [SerializeField] private int maxKickDistance = 6;

    private OrganUnit organUnit;
    private float chargeAmount;
    private bool charging;

    /// <summary>是否正在蓄力（阻止移动）</summary>
    public bool IsCharging => charging;

    private OrganController Controller => GameBootstrap.Instance?.OrganController;

    private void Awake()
    {
        organUnit = GetComponent<OrganUnit>();
    }

    private void Update()
    {
        HandleChargeInput();
    }

    private void HandleChargeInput()
    {
        // 只对 Foot 生效
        if (organUnit.OrganType != OrganType.Foot) return;

        // 按下 F 开始蓄力
        if (Input.GetKeyDown(chargeKey))
        {
            charging = true;
            chargeAmount = 0f;
        }

        // 按住 F 蓄力增长
        if (Input.GetKey(chargeKey) && charging)
        {
            chargeAmount += Time.deltaTime / fullChargeTime;
            chargeAmount = Mathf.Clamp01(chargeAmount);
            organUnit.SetChargeDisplay(chargeAmount);
        }

        // 松开 F 释放踢击
        if (Input.GetKeyUp(chargeKey) && charging)
        {
            charging = false;
            organUnit.SetChargeDisplay(0f);

            if (chargeAmount > 0.01f)
                ExecuteKick();
        }
    }

    /// <summary>
    /// 根据蓄力程度向当前朝向踢出。
    /// </summary>
    private void ExecuteKick()
    {
        if (Controller == null) return;

        Vector3Int kickDir = Direction4ToVector(organUnit.FacingDirection);
        int distance = Mathf.RoundToInt(Mathf.Lerp(minKickDistance, maxKickDistance, chargeAmount));

        Debug.Log($"[FootChargeKick] 踢! 方向:{organUnit.FacingDirection} 蓄力:{chargeAmount:F2} 格数:{distance}");

        Controller.ForceKick(organUnit, kickDir, distance);
    }

    private static Vector3Int Direction4ToVector(Direction4 dir)
    {
        return dir switch
        {
            Direction4.Up    => Vector3Int.up,
            Direction4.Down  => Vector3Int.down,
            Direction4.Left  => Vector3Int.left,
            Direction4.Right => Vector3Int.right,
            _ => Vector3Int.up
        };
    }
}
