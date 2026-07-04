using UnityEngine.Rendering;

/// <summary>
/// 标记该可推动物能够填平坑洞。
/// </summary>
public interface IPitFiller
{
    bool CanFillPit(Pit pit);
    void OnFilledPit(Pit pit);
}