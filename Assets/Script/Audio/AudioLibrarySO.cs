using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音频库 ScriptableObject，保存所有 AudioClipSO 列表，
/// 并在运行时按 AudioName 构建字典，提供高效查询。
/// </summary>
[CreateAssetMenu(fileName = "AudioLibrary_", menuName = "Audio/AudioLibrarySO", order = 2)]
public class AudioLibrarySO : ScriptableObject
{
    [Header("音频列表")]
    [SerializeField] private List<AudioClipSO> audioClips;

    /// <summary>运行时字典：AudioName → AudioClipSO</summary>
    private Dictionary<string, AudioClipSO> audioDict;

    /// <summary>是否已完成初始化</summary>
    public bool IsInitialized => audioDict != null;

    #region 初始化

    /// <summary>
    /// 初始化字典。可重复调用，每次调用会重建字典。
    /// </summary>
    public void Init()
    {
        audioDict = new Dictionary<string, AudioClipSO>();

        if (audioClips == null || audioClips.Count == 0)
        {
            Debug.LogWarning("[AudioLibrarySO] 音频列表为空，字典初始化完成但无数据。");
            return;
        }

        for (int i = 0; i < audioClips.Count; i++)
        {
            AudioClipSO audio = audioClips[i];

            // 跳过空引用
            if (audio == null)
            {
                Debug.LogWarning($"[AudioLibrarySO] 列表中第 {i} 项为空引用，已跳过。");
                continue;
            }

            // 跳过空名称
            if (string.IsNullOrEmpty(audio.AudioName))
            {
                Debug.LogWarning($"[AudioLibrarySO] 列表中第 {i} 项的 AudioName 为空，已跳过。");
                continue;
            }

            // 处理重复名称
            if (audioDict.ContainsKey(audio.AudioName))
            {
                Debug.LogWarning($"[AudioLibrarySO] 音频名 \"{audio.AudioName}\" 重复，保留第一个，跳过后续项。");
                continue;
            }

            audioDict.Add(audio.AudioName, audio);
        }

        Debug.Log($"[AudioLibrarySO] 字典初始化完成，共加载 {audioDict.Count} 个音频配置。");
    }

    #endregion

    #region 查询方法

    /// <summary>
    /// 尝试通过名称获取音频配置。
    /// </summary>
    /// <returns>成功获取返回 true，否则 false</returns>
    public bool TryGetAudio(string audioName, out AudioClipSO audio)
    {
        audio = null;

        if (!IsInitialized)
        {
            Debug.LogError("[AudioLibrarySO] 音频库尚未初始化，请先调用 Init()。");
            return false;
        }

        if (string.IsNullOrEmpty(audioName))
        {
            Debug.LogWarning("[AudioLibrarySO] 查询的音频名为空。");
            return false;
        }

        return audioDict.TryGetValue(audioName, out audio);
    }

    /// <summary>
    /// 通过名称获取音频配置，找不到返回 null。
    /// </summary>
    public AudioClipSO GetAudio(string audioName)
    {
        TryGetAudio(audioName, out AudioClipSO audio);
        return audio;
    }

    /// <summary>
    /// 查询音频名是否存在。
    /// </summary>
    public bool ContainsAudio(string audioName)
    {
        if (!IsInitialized || string.IsNullOrEmpty(audioName))
            return false;
        return audioDict.ContainsKey(audioName);
    }

    /// <summary>
    /// 获取当前库中音频数量。
    /// </summary>
    public int AudioCount => audioDict?.Count ?? 0;

    #endregion
}
