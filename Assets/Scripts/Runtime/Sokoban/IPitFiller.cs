/// <summary>
/// 表示该可推动物能够填入坑洞。
/// </summary>
public interface IPitFiller
{
    /// <summary>
    /// 判断当前物体能否填平指定坑洞。
    /// </summary>
    bool CanFillPit(PitMechanism pit);

    /// <summary>
    /// 当前物体成功填平坑洞后调用。
    ///
    /// 适合处理音效、动画、统计或存档信息；
    /// 物体的注销和隐藏仍由 PushContext 负责。
    /// </summary>
    void OnFilledPit(PitMechanism pit);
}