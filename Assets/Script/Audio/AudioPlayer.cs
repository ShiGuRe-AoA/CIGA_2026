using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 音频播放器 MonoBehaviour 单例，是整个音频系统的运行核心。
/// 管理 SFX 音效池、BGM 双 Source 淡入淡出、Runtime 生命周期。
/// </summary>
public class AudioPlayer : MonoBehaviour
{
    #region Inspector 配置

    [Header("音频库")]
    [SerializeField] private AudioLibrarySO audioLibrary;

    [Header("SFX 音效池")]
    [SerializeField] private int sfxSourcePoolSize = 16;
    [SerializeField] private AudioSource sfxSourcePrefab;

    [Header("BGM (必须独立于 SFX 池)")]
    [SerializeField] private AudioSource bgmSourceA;
    [SerializeField] private AudioSource bgmSourceB;

    [Header("BGM 设置")]
    [SerializeField] private float defaultBGMFadeDuration = 1f;

    [Header("全局设置")]
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool logWarnings = true;
    [SerializeField] private bool allowStealOldestSource = false;

    #endregion

    #region 单例

    /// <summary>全局单例实例</summary>
    public static AudioPlayer Instance { get; private set; }

    #endregion

    #region 运行时状态

    /// <summary>SFX 音效池</summary>
    private AudioSource[] sfxPool;

    /// <summary>当前活跃的 BGM Source（A 或 B）</summary>
    private AudioSource currentBGMSource;

    /// <summary>当前 BGM 名称</summary>
    private string currentBGMName;

    /// <summary>BGM 淡入淡出 Tween（用于 Kill）</summary>
    private Tweener bgmFadeTweenA;
    private Tweener bgmFadeTweenB;

    /// <summary>运行中音频列表</summary>
    private readonly List<AudioRuntime> runningAudios = new List<AudioRuntime>();

    /// <summary>待移除的 Runtime 缓存（避免 foreach 中修改列表）</summary>
    private readonly List<AudioRuntime> toRemove = new List<AudioRuntime>();

    #endregion

    #region 前缀常量

    private const string LOG_PREFIX = "[AudioPlayer]";
    private const string LOG_LIBRARY_PREFIX = "[AudioLibrarySO]";

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        // 单例处理
        if (Instance != null && Instance != this)
        {
            LogWarning($"已存在另一个 AudioPlayer 实例，销毁当前 GameObject: {gameObject.name}");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }

        // 初始化
        InitializeAudioLibrary();
        InitializeSfxPool();
        ValidateBGMSources();

        Log($"[AudioPlayer] 初始化完成（SFX 池大小: {sfxSourcePoolSize}）。");
    }
    private void Start()
    {
        //PlayOneShot("Fix");
    }
    private void OnDestroy()
    {
        // 销毁所有 Tween
        KillBGMTweens();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        UpdateRunningAudios();
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化音频库字典。
    /// </summary>
    private void InitializeAudioLibrary()
    {
        if (audioLibrary == null)
        {
            LogError("audioLibrary 未配置！请在 Inspector 中拖入 AudioLibrarySO 资产。");
            return;
        }

        audioLibrary.Init();
    }

    /// <summary>
    /// 初始化 SFX Source 池。优先使用 Prefab，无 Prefab 则自动创建。
    /// </summary>
    private void InitializeSfxPool()
    {
        sfxPool = new AudioSource[sfxSourcePoolSize];

        for (int i = 0; i < sfxSourcePoolSize; i++)
        {
            AudioSource source;

            if (sfxSourcePrefab != null)
            {
                source = Instantiate(sfxSourcePrefab, transform);
            }
            else
            {
                GameObject go = new GameObject($"SFX_Source_{i}");
                go.transform.SetParent(transform);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }

            source.name = $"SFX_Source_{i}";
            sfxPool[i] = source;
        }
    }

    /// <summary>
    /// 验证 BGM Source 是否配置。
    /// </summary>
    private void ValidateBGMSources()
    {
        if (bgmSourceA == null)
            LogError("bgmSourceA 未配置！BGM 功能将不可用。");

        if (bgmSourceB == null)
            LogError("bgmSourceB 未配置！BGM 功能将不可用。");
    }

    #endregion

    #region 运行时更新

    /// <summary>
    /// 每帧检查运行中音频的状态：
    /// 1. 非 Loop 播放完毕 → 回收
    /// 2. Loop 停止条件触发 → 停止
    /// 3. Stoppping 中的 BGM 淡出完成 → 回收
    /// 4. 已释放 → 从列表移除
    /// </summary>
    private void UpdateRunningAudios()
    {
        toRemove.Clear();

        for (int i = 0; i < runningAudios.Count; i++)
        {
            AudioRuntime runtime = runningAudios[i];

            // 已释放的直接移除
            if (runtime.IsReleased)
            {
                toRemove.Add(runtime);
                continue;
            }

            // Source 已失效
            if (runtime.Source == null)
            {
                runtime.MarkReleased();
                toRemove.Add(runtime);
                continue;
            }

            // BGM 特殊处理（淡出中的 BGM 在 BGM 逻辑中回收）
            if (runtime.IsBGM)
            {
                continue;
            }

            // 检查停止条件（Loop 音频）
            if (runtime.IsLoop && !runtime.IsStopping && runtime.ShouldStopByCondition())
            {
                ReleaseSfxSource(runtime.Source);
                runtime.MarkReleased();
                toRemove.Add(runtime);
                continue;
            }

            // 非 Loop 播放完毕
            if (runtime.ShouldRecycle())
            {
                ReleaseSfxSource(runtime.Source);
                runtime.MarkReleased();
                toRemove.Add(runtime);
            }
        }

        // 清理已释放的 Runtime
        for (int i = 0; i < toRemove.Count; i++)
        {
            runningAudios.Remove(toRemove[i]);
        }
    }

    #endregion

    #region Source 池管理

    /// <summary>
    /// 从 SFX 池获取空闲 AudioSource。
    /// </summary>
    /// <returns>空闲 Source，池满时返回 null</returns>
    private AudioSource GetFreeSfxSource()
    {
        // 优先找未播放的
        for (int i = 0; i < sfxPool.Length; i++)
        {
            if (sfxPool[i] != null && !sfxPool[i].isPlaying)
                return sfxPool[i];
        }

        // 池满
        if (allowStealOldestSource)
        {
            return StealOldestSource();
        }

        LogWarning("SFX Source 池已耗尽，忽略本次播放请求。考虑增加 sfxSourcePoolSize。");
        return null;
    }

    /// <summary>
    /// 抢占最早开始播放的非 Loop Source。
    /// </summary>
    private AudioSource StealOldestSource()
    {
        AudioRuntime oldest = null;
        float oldestStart = float.MaxValue;

        for (int i = 0; i < runningAudios.Count; i++)
        {
            AudioRuntime rt = runningAudios[i];
            if (rt.IsBGM || rt.IsLoop || rt.IsReleased) continue;
            if (rt.StartTime < oldestStart)
            {
                oldestStart = rt.StartTime;
                oldest = rt;
            }
        }

        if (oldest != null && oldest.Source != null)
        {
            LogWarning($"抢占最早音效 \"{oldest.AudioName}\" (ID:{oldest.RuntimeId}) 的 Source。");
            oldest.MarkReleased();
            toRemove.Add(oldest);
            ReleaseSfxSource(oldest.Source);
            return oldest.Source;
        }

        return null;
    }

    /// <summary>
    /// 释放 SFX Source，重置所有状态。
    /// </summary>
    private void ReleaseSfxSource(AudioSource source)
    {
        if (source == null) return;

        source.Stop();
        source.clip = null;
        source.loop = false;
        source.pitch = 1f;
        source.volume = 1f;
        source.spatialBlend = 0f;
        source.priority = 128;
        DOTween.Kill(source); // 安全 Kill 可能残留的 Tween
    }

    #endregion

    #region 配置 Source

    /// <summary>
    /// 根据 AudioClipSO 配置 AudioSource。
    /// </summary>
    private void ConfigureSource(AudioSource source, AudioClipSO audioData, bool isLoop, float volumeScale)
    {
        if (source == null || audioData == null) return;

        source.clip = audioData.Clip;
        source.volume = Mathf.Clamp01(audioData.Volume * volumeScale);
        source.pitch = audioData.GetRandomPitch();
        source.loop = isLoop;
        source.spatialBlend = audioData.SpatialBlend;
        source.priority = audioData.Priority;
    }

    #endregion

    #region 静态播放接口

    #region PlayOneShot - 单次播放

    /// <summary>
    /// 播放一次非 Loop 音效。
    /// </summary>
    /// <param name="audioName">音频名称（AudioClipSO.AudioName）</param>
    /// <param name="volumeScale">额外音量倍率（0~1）</param>
    /// <returns>AudioRuntime 实例，播放失败返回 null</returns>
    public static AudioRuntime PlayOneShot(string audioName, float volumeScale = 1f)
    {
        if (!ValidateInstanceAndLibrary()) return null;

        if (!Instance.audioLibrary.TryGetAudio(audioName, out AudioClipSO audioData))
        {
            Instance.LogWarning($"找不到音频 \"{audioName}\"，单次播放失败。");
            return null;
        }

        if (!audioData.IsValid())
        {
            return null;
        }

        AudioSource source = Instance.GetFreeSfxSource();
        if (source == null) return null;

        Instance.ConfigureSource(source, audioData, false, volumeScale);
        source.Play();

        AudioRuntime runtime = new AudioRuntime(
            audioName, audioData, source,
            isLoop: false, isBGM: false,
            stopCondition: null, volumeScale
        );
        runtime.MarkStarted();
        Instance.runningAudios.Add(runtime);

        return runtime;
    }

    #endregion

    #region PlayLoopUntil - 条件循环播放

    /// <summary>
    /// 循环播放音频，直到 stopCondition 返回 true 时自动停止。
    /// </summary>
    /// <param name="audioName">音频名称</param>
    /// <param name="stopCondition">停止条件函数，返回 true 时停止</param>
    /// <param name="volumeScale">额外音量倍率</param>
    /// <returns>AudioRuntime 实例，播放失败返回 null</returns>
    public static AudioRuntime PlayLoopUntil(
        string audioName,
        Func<bool> stopCondition,
        float volumeScale = 1f)
    {
        if (!ValidateInstanceAndLibrary()) return null;

        if (!Instance.audioLibrary.TryGetAudio(audioName, out AudioClipSO audioData))
        {
            Instance.LogWarning($"找不到音频 \"{audioName}\"，条件播放失败。");
            return null;
        }

        if (!audioData.IsValid())
        {
            return null;
        }

        AudioSource source = Instance.GetFreeSfxSource();
        if (source == null) return null;

        Instance.ConfigureSource(source, audioData, true, volumeScale);
        source.Play();

        AudioRuntime runtime = new AudioRuntime(
            audioName, audioData, source,
            isLoop: true, isBGM: false,
            stopCondition, volumeScale
        );
        runtime.MarkStarted();
        Instance.runningAudios.Add(runtime);

        return runtime;
    }

    #endregion

    #region PlayBGM - BGM 播放/切换

    /// <summary>
    /// 播放或切换 BGM，使用双 Source 淡入淡出。
    /// </summary>
    /// <param name="audioName">音频名称</param>
    /// <param name="fadeDuration">淡入淡出时间，&lt;0 使用 defaultBGMFadeDuration</param>
    public static void PlayBGM(string audioName, float fadeDuration = -1f)
    {
        if (!ValidateInstanceAndLibrary()) return;

        if (Instance.bgmSourceA == null || Instance.bgmSourceB == null)
        {
            Instance.LogError("BGM Source 未配置，无法播放 BGM。");
            return;
        }

        if (!Instance.audioLibrary.TryGetAudio(audioName, out AudioClipSO audioData))
        {
            Instance.LogWarning($"找不到音频 \"{audioName}\"，BGM 播放失败。");
            return;
        }

        if (!audioData.IsValid())
        {
            return;
        }

        // 相同 BGM 不重复播放
        if (Instance.currentBGMName == audioName && Instance.currentBGMSource != null && Instance.currentBGMSource.isPlaying)
        {
            Instance.Log($"BGM \"{audioName}\" 已在播放，跳过。");
            return;
        }

        float duration = fadeDuration >= 0f ? fadeDuration : Instance.defaultBGMFadeDuration;
        duration = Mathf.Max(duration, 0f);

        // 确定新 Source 和旧 Source
        AudioSource newSource = Instance.GetInactiveBGMSource();
        AudioSource oldSource = Instance.currentBGMSource;

        // 配置新 Source
        Instance.ConfigureSource(newSource, audioData, true, 1f);

        // Kill 旧 Tween
        DOTween.Kill(newSource);

        if (oldSource != null && oldSource.isPlaying)
        {
            if (duration > 0f)
            {
                // 交错淡入淡出
                newSource.volume = 0f;
                newSource.Play();

                // 新 Source 淡入
                newSource.DOFade(audioData.Volume, duration).SetEase(Ease.Linear);

                // 旧 Source 淡出
                KillBGMTweenForSource(oldSource);
                oldSource.DOFade(0f, duration)
                    .SetEase(Ease.Linear)
                    .OnComplete(() =>
                    {
                        // 淡出完成后停止旧 Source
                        Instance.ReleaseBGMSource(oldSource);
                    });
            }
            else
            {
                // 无淡入淡出，直接切换
                Instance.ReleaseBGMSource(oldSource);
                newSource.Play();
            }
        }
        else
        {
            // 首次播放或旧 Source 已停
            if (duration > 0f)
            {
                newSource.volume = 0f;
                newSource.Play();
                newSource.DOFade(audioData.Volume, duration).SetEase(Ease.Linear);
            }
            else
            {
                newSource.Play();
            }
        }

        Instance.currentBGMSource = newSource;
        Instance.currentBGMName = audioName;

        // 移除旧的 BGM Runtime（如果有），添加新的
        Instance.runningAudios.RemoveAll(r => r.IsBGM && r.IsReleased);
        AudioRuntime bgmRuntime = new AudioRuntime(
            audioName, audioData, newSource,
            isLoop: true, isBGM: true,
            stopCondition: null, 1f, duration
        );
        bgmRuntime.MarkStarted();
        Instance.runningAudios.Add(bgmRuntime);
    }

    /// <summary>
    /// 停止 BGM 播放。
    /// </summary>
    public static void StopBGM(float fadeDuration = -1f)
    {
        if (Instance == null) return;

        float duration = fadeDuration >= 0f ? fadeDuration : Instance.defaultBGMFadeDuration;
        duration = Mathf.Max(duration, 0f);

        if (Instance.currentBGMSource != null && Instance.currentBGMSource.isPlaying)
        {
            if (duration > 0f)
            {
                KillBGMTweenForSource(Instance.currentBGMSource);
                Instance.currentBGMSource.DOFade(0f, duration)
                    .SetEase(Ease.Linear)
                    .OnComplete(() =>
                    {
                        Instance.ReleaseBGMSource(Instance.currentBGMSource);
                        Instance.currentBGMSource = null;
                    });
            }
            else
            {
                Instance.ReleaseBGMSource(Instance.currentBGMSource);
                Instance.currentBGMSource = null;
            }
        }

        Instance.currentBGMName = null;
    }

    #endregion

    #endregion // 静态播放接口

    #region Stop 静态接口

    /// <summary>
    /// 停止指定 Runtime。
    /// </summary>
    public static void Stop(AudioRuntime runtime, float fadeDuration = 0f)
    {
        if (Instance == null || runtime == null || runtime.IsReleased) return;

        if (runtime.IsBGM)
        {
            StopBGM(fadeDuration);
            return;
        }

        if (fadeDuration > 0f && runtime.Source != null)
        {
            runtime.MarkStopping();
            runtime.Source.DOFade(0f, fadeDuration)
                .SetEase(Ease.Linear)
                .OnComplete(() =>
                {
                    Instance.ReleaseSfxSource(runtime.Source);
                    runtime.MarkReleased();
                });
        }
        else
        {
            Instance.ReleaseSfxSource(runtime.Source);
            runtime.MarkReleased();
        }
    }

    /// <summary>
    /// 按名称停止所有匹配的音频（非 BGM）。
    /// </summary>
    public static void StopByName(string audioName, float fadeDuration = 0f)
    {
        if (Instance == null || string.IsNullOrEmpty(audioName)) return;

        for (int i = Instance.runningAudios.Count - 1; i >= 0; i--)
        {
            AudioRuntime rt = Instance.runningAudios[i];
            if (!rt.IsBGM && rt.AudioName == audioName && !rt.IsReleased)
            {
                Stop(rt, fadeDuration);
            }
        }
    }

    /// <summary>
    /// 停止所有非 BGM 音效。
    /// </summary>
    public static void StopAllSFX(float fadeDuration = 0f)
    {
        if (Instance == null) return;

        for (int i = Instance.runningAudios.Count - 1; i >= 0; i--)
        {
            AudioRuntime rt = Instance.runningAudios[i];
            if (!rt.IsBGM && !rt.IsReleased)
            {
                Stop(rt, fadeDuration);
            }
        }
    }

    #endregion

    #region BGM Source 辅助

    /// <summary>
    /// 获取当前未使用的 BGM Source。
    /// </summary>
    private AudioSource GetInactiveBGMSource()
    {
        // B 空闲时优先返回 B（A 作为主 Source），否则返回 A
        if (bgmSourceB == null) return bgmSourceA;
        if (bgmSourceA == null) return bgmSourceB;

        if (currentBGMSource == bgmSourceA)
            return bgmSourceB;
        else
            return bgmSourceA;
    }

    /// <summary>
    /// 释放 BGM Source（停止并清空）。
    /// </summary>
    private void ReleaseBGMSource(AudioSource source)
    {
        if (source == null) return;
        source.Stop();
        source.clip = null;
        DOTween.Kill(source);
    }

    /// <summary>
    /// Kill 指定 BGM Source 上的 Tween。
    /// </summary>
    private static void KillBGMTweenForSource(AudioSource source)
    {
        if (source == null) return;
        DOTween.Kill(source);
    }

    /// <summary>
    /// Kill 所有 BGM Tween。
    /// </summary>
    private void KillBGMTweens()
    {
        if (bgmSourceA != null) DOTween.Kill(bgmSourceA);
        if (bgmSourceB != null) DOTween.Kill(bgmSourceB);
    }

    #endregion

    #region 验证

    /// <summary>
    /// 验证 Instance 和 AudioLibrary 是否就绪。
    /// </summary>
    private static bool ValidateInstanceAndLibrary()
    {
        if (Instance == null)
        {
            Debug.LogError("[AudioPlayer] Instance 不存在！请确保场景中存在挂载了 AudioPlayer 的 GameObject。");
            return false;
        }

        if (Instance.audioLibrary == null)
        {
            Debug.LogError("[AudioPlayer] audioLibrary 未配置！");
            return false;
        }

        if (!Instance.audioLibrary.IsInitialized)
        {
            Debug.LogError("[AudioPlayer] 音频库尚未初始化！");
            return false;
        }

        return true;
    }

    #endregion

    #region 日志辅助

    private void Log(string message)
    {
        Debug.Log($"{LOG_PREFIX} {message}");
    }

    private void LogWarning(string message)
    {
        if (logWarnings)
            Debug.LogWarning($"{LOG_PREFIX} {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"{LOG_PREFIX} {message}");
    }

    #endregion
}
