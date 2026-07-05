using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// 简单器官存档/复原场景单例。
/// T 键保存所有 OrganUnit 的位置和朝向，R 键复原到上次保存状态。
/// </summary>
public class OrganSaveLoadController : MonoBehaviour
{
    private struct OrganSaveData
    {
        public Vector3Int GridPos;
        public Direction4 FacingDirection;
    }

    public static OrganSaveLoadController Instance { get; private set; }

    [Header("输入")]
    [SerializeField] private KeyCode saveKey = KeyCode.T;
    [SerializeField] private KeyCode loadKey = KeyCode.R;

    [Header("读取")]
    [Tooltip("读取时是否瞬移。开启后动画时长为 0。")]
    [SerializeField] private bool instantLoad = true;

    [Tooltip("非瞬移读取时使用的移动动画时长。")]
    [SerializeField, Min(0f)] private float loadMoveDuration = 0.08f;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    private readonly Dictionary<OrganUnit, OrganSaveData> savedOrgans =
        new Dictionary<OrganUnit, OrganSaveData>();

    private bool hasSave;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(
                "[OrganSaveLoadController] 场景中存在多个实例，后创建的实例将被禁用。",
                this
            );
            enabled = false;
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (Input.GetKeyDown(saveKey))
            Save();

        if (Input.GetKeyDown(loadKey))
            Load();
    }

    /// <summary>
    /// 保存当前场景所有 OrganUnit 的网格位置和朝向。
    /// </summary>
    public void Save()
    {
        savedOrgans.Clear();

        OrganUnit[] organs = FindObjectsOfType<OrganUnit>();

        foreach (OrganUnit organ in organs)
        {
            if (organ == null)
                continue;

            savedOrgans[organ] = new OrganSaveData
            {
                GridPos = organ.GridPos,
                FacingDirection = organ.FacingDirection
            };
        }

        hasSave = savedOrgans.Count > 0;

        Log($"已保存 {savedOrgans.Count} 个器官。");
    }

    /// <summary>
    /// 读取上次保存的器官网格位置和朝向。
    /// </summary>
    public void Load()
    {
        if (!hasSave)
        {
            Log("没有可读取的存档。");
            return;
        }

        AudioPlayer.PlayOneShot("SFX_Load");

        float? duration = instantLoad ? 0f : loadMoveDuration;
        var invalidOrgans = new List<OrganUnit>();

        foreach (var pair in savedOrgans)
        {
            OrganUnit organ = pair.Key;
            if (organ == null)
            {
                invalidOrgans.Add(organ);
                continue;
            }

            OrganSaveData data = pair.Value;

            organ.transform.DOKill(complete: false);
            organ.MoveTo(data.GridPos, Ease.Linear, duration);
            organ.SetFacingDirection(data.FacingDirection);
        }

        foreach (OrganUnit invalidOrgan in invalidOrgans)
            savedOrgans.Remove(invalidOrgan);

        Log($"已读取 {savedOrgans.Count} 个器官。");
    }

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log($"[OrganSaveLoadController] {message}", this);
    }
}
