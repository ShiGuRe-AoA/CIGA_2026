using System;
using UnityEngine;

/// <summary>
/// 运行中音频状态类，表示一次音频播放实例的运行状态。
/// 不继承 MonoBehaviour，由 AudioPlayer 统一管理生命周期。
/// </summary>
public class AudioRuntime
{
    #region 属性

    /// <summary>全局自增 ID，用于唯一标识</summary>
    private static int nextRuntimeId = 1;

    /// <summary>本次播放的唯一 ID</summary>
    public int RuntimeId { get; }

    /// <summary>音频名称（查找 key）</summary>
    public string AudioName { get; }

    /// <summary>对应的音频配置</summary>
    public AudioClipSO AudioData { get; }

    /// <summary>使用的 AudioSource</summary>
    public AudioSource Source { get; }

    /// <summary>是否为循环播放</summary>
    public bool IsLoop { get; }

    /// <summary>是否为 BGM</summary>
    public bool IsBGM { get; }

    /// <summary>是否已释放</summary>
    public bool IsReleased { get; private set; }

    /// <summary>是否正在停止中（淡出中）</summary>
    public bool IsStopping { get; private set; }

    /// <summary>播放开始时间（Time.time）</summary>
    public float StartTime { get; }

    /// <summary>停止条件（仅 Loop 音频有效），返回 true 时自动停止</summary>
    public Func<bool> StopCondition { get; }

    /// <summary>额外音量倍率（调用侧传入）</summary>
    public float VolumeScale { get; }

    /// <summary>淡入淡出时间</summary>
    public float FadeDuration { get; }

    /// <summary>是否已开始播放</summary>
    public bool HasStarted { get; private set; }

    #endregion

    #region 构造

    public AudioRuntime(
        string audioName,
        AudioClipSO audioData,
        AudioSource source,
        bool isLoop,
        bool isBGM,
        Func<bool> stopCondition = null,
        float volumeScale = 1f,
        float fadeDuration = 0f)
    {
        RuntimeId = nextRuntimeId++;
        AudioName = audioName;
        AudioData = audioData;
        Source = source;
        IsLoop = isLoop;
        IsBGM = isBGM;
        StopCondition = stopCondition;
        VolumeScale = volumeScale;
        FadeDuration = fadeDuration;
        StartTime = Time.time;
    }

    #endregion

    #region 状态控制

    /// <summary>
    /// 标记已开始播放。
    /// </summary>
    public void MarkStarted()
    {
        HasStarted = true;
    }

    /// <summary>
    /// 标记正在停止。
    /// </summary>
    public void MarkStopping()
    {
        if (IsStopping || IsReleased) return;
        IsStopping = true;
    }

    /// <summary>
    /// 停止播放并标记释放。
    /// </summary>
    /// <param name="fadeDuration">淡出时间，≤0 则立即停止</param>
    public void Stop(float fadeDuration = 0f)
    {
        if (IsReleased || IsStopping) return;

        if (Source == null)
        {
            MarkReleased();
            return;
        }

        if (fadeDuration > 0f && IsBGM)
        {
            // BGM: 使用 DOFade 淡出，在 AudioPlayer 的 BGM 逻辑中处理
            MarkStopping();
        }
        else
        {
            // 立即停止
            Source.Stop();
            Source.clip = null;
            MarkReleased();
        }
    }

    /// <summary>
    /// 标记为已释放（供 AudioPlayer 回收 Source 后调用）。
    /// </summary>
    public void MarkReleased()
    {
        if (IsReleased) return;
        IsReleased = true;
        IsStopping = false;
    }

    #endregion

    #region 检查方法

    /// <summary>
    /// 检查是否需要回收（非 Loop 且播放完毕，或 Source 无效）。
    /// </summary>
    public bool ShouldRecycle()
    {
        if (IsReleased) return true;
        if (Source == null) return true;

        // 非 Loop 且不在播放中
        if (!IsLoop && !Source.isPlaying && HasStarted)
            return true;

        return false;
    }

    /// <summary>
    /// 检查停止条件是否触发。
    /// </summary>
    public bool ShouldStopByCondition()
    {
        if (IsReleased || IsStopping) return false;
        if (StopCondition == null) return false;

        try
        {
            return StopCondition.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[AudioRuntime] 音频 \"{AudioName}\" (ID:{RuntimeId}) 的停止条件执行异常：" +
                           $"{e.Message}\n{e.StackTrace}");
            return true; // 异常时强制停止，防止刷屏
        }
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 获取本次播放的目标音量（配置音量 × 额外倍率）。
    /// </summary>
    public float GetTargetVolume()
    {
        if (AudioData == null) return VolumeScale;
        return Mathf.Clamp01(AudioData.Volume * VolumeScale);
    }

    public override string ToString()
    {
        string type = IsBGM ? "BGM" : (IsLoop ? "Loop" : "OneShot");
        return $"[AudioRuntime] #{RuntimeId} [{type}] \"{AudioName}\"";
    }

    #endregion
}
