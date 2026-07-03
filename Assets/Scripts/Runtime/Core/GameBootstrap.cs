using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 项目终端单例。持有 MapGrid、OrganController 等核心引用，
/// 其他脚本通过 GameBootstrap.Instance 访问，避免到处挂 SerializeField 引用。
/// 挂载到场景根对象上，Awake 时自动查找核心组件。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    public static GameBootstrap Instance { get; private set; }

    /// <summary>地图网格</summary>
    public MapGrid MapGrid { get; private set; }

    /// <summary>器官控制器</summary>
    public OrganController OrganController { get; private set; }

    /// <summary>所有 Trigger 的注册表（由 Trigger 自主注册）</summary>
    public readonly List<Trigger> AllTriggers = new List<Trigger>();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        MapGrid = FindObjectOfType<MapGrid>();
        OrganController = FindObjectOfType<OrganController>();

        if (MapGrid == null)
            Debug.LogError("[GameBootstrap] 场景中未找到 MapGrid。");
        if (OrganController == null)
            Debug.LogError("[GameBootstrap] 场景中未找到 OrganController。");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
