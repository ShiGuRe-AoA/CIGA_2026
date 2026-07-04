using UnityEngine;

/// <summary>
/// 单个音频封装 ScriptableObject，保存音频资源的静态配置。
/// 在 Inspector 中创建和配置，不参与运行时播放逻辑。
/// </summary>
[CreateAssetMenu(fileName = "AudioClip_", menuName = "Audio/AudioClipSO", order = 1)]
public class AudioClipSO : ScriptableObject
{
    [Header("基础配置")]
    [SerializeField] private string audioName;
    [SerializeField] private AudioClip clip;

    [Header("音量与音调")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField] private Vector2 randomPitchRange = new Vector2(1f, 1f);

    [Header("音频属性")]
    [SerializeField] private bool defaultLoop = false;
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 0f;
    [SerializeField, Range(0, 255)] private int priority = 128;
    [SerializeField] private float defaultFadeDuration = 0.5f;

    #region 只读属性

    public string AudioName => audioName;
    public AudioClip Clip => clip;
    public float Volume => volume;
    public Vector2 RandomPitchRange => randomPitchRange;
    public bool DefaultLoop => defaultLoop;
    public float SpatialBlend => spatialBlend;
    public int Priority => priority;
    public float DefaultFadeDuration => defaultFadeDuration;

    #endregion

    #region 验证与名称同步

#if UNITY_EDITOR
    /// <summary>
    /// 用于追踪上一次的 Clip，判断是否需要自动同步 AudioName。
    /// </summary>
    [SerializeField, HideInInspector] private AudioClip lastSyncedClip;

    /// <summary>
    /// 标记：OnValidate 中自动同步了 AudioName，需要 Editor 延迟同步文件名。
    /// </summary>
    [System.NonSerialized] public bool PendingFileRename;

    /// <summary>
    /// 在 Inspector 中修改值时自动调用的验证。
    /// </summary>
    private void OnValidate()
    {
        // 修正非法 pitch 范围
        if (randomPitchRange.x > randomPitchRange.y)
        {
            Debug.LogWarning($"[AudioClipSO] \"{audioName}\" 的 randomPitchRange 无效 (x > y)，已自动修正为 (1, 1)");
            randomPitchRange = new Vector2(1f, 1f);
        }

        // 自动同步 AudioName：Clip 已变更 且 AudioName 为空或仍为旧 Clip 的名称
        if (clip != null && clip != lastSyncedClip)
        {
            string clipName = clip.name;

            if (string.IsNullOrWhiteSpace(audioName) || audioName == (lastSyncedClip != null ? lastSyncedClip.name : null))
            {
                audioName = clipName;
                PendingFileRename = true; // 标记需要同步文件名
            }

            lastSyncedClip = clip;
        }
    }
#endif

    #endregion

    #region 工具方法

    /// <summary>
    /// 手动将 AudioName 同步为当前 Clip 的名称。
    /// </summary>
    public void SyncNameFromClip()
    {
        if (clip != null)
        {
            audioName = clip.name;
#if UNITY_EDITOR
            lastSyncedClip = clip;
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        else
        {
            Debug.LogWarning("[AudioClipSO] 无法同步：Clip 为空。");
        }
    }

    /// <summary>
    /// 获取本次播放应使用的随机 Pitch，基于 randomPitchRange。
    /// </summary>
    public float GetRandomPitch()
    {
        if (randomPitchRange.x >= randomPitchRange.y)
            return 1f;
        return Random.Range(randomPitchRange.x, randomPitchRange.y);
    }

    /// <summary>
    /// 检查此资产是否有效用于播放（clip 不为空）。
    /// </summary>
    public bool IsValid()
    {
        if (clip == null)
        {
            Debug.LogWarning($"[AudioClipSO] 音频 \"{audioName}\" 的 Clip 为空，无法播放。");
            return false;
        }
        return true;
    }

    #endregion
}
