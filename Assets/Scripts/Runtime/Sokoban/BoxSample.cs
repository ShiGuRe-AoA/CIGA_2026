using UnityEngine;

/// <summary>
/// 可以被推入坑洞并用于填坑的箱子。
/// </summary>
public class BoxSample : ScenePushable, IPitFiller
{
    [Header("填坑")]
    [Tooltip("是否允许当前箱子填坑。")]
    [SerializeField] private bool canFillPit = true;

    [Tooltip("填坑成功时播放的音效。")]
    [SerializeField] private AudioSource fillAudioSource;

    /// <summary>
    /// 判断该箱子能否填平指定坑洞。
    /// </summary>
    public bool CanFillPit(PitMechanism pit)
    {
        return canFillPit &&
               pit != null &&
               !pit.IsFilled;
    }

    /// <summary>
    /// 箱子成功填坑后的回调。
    ///
    /// PushContext 随后会注销并隐藏该箱子，
    /// 因此不要在这里立即销毁坑洞或修改 GridPos。
    /// </summary>
    public void OnFilledPit(PitMechanism pit)
    {
        if (fillAudioSource != null)
            fillAudioSource.Play();

        Debug.Log(
            $"[PitFillerBox] {name} 已填入坑洞 {pit.name}。",
            this
        );
    }
}