using UnityEngine;

/// <summary>
/// 器官 Sprite & 参数集中配置资产。
/// </summary>
[CreateAssetMenu(menuName = "Anchor/Organ Sprite Config", fileName = "OrganSpriteConfig")]
public class OrganSpriteConfig : ScriptableObject
{
    [Header("心")]
    [SerializeField] private Sprite heartSprite;

    [Header("脚 — 交替使用")]
    [SerializeField] private Sprite footSpriteA;
    [SerializeField] private Sprite footSpriteB;

    [Header("手 — 交替使用")]
    [SerializeField] private Sprite handSpriteA;
    [SerializeField] private Sprite handSpriteB;

    [Header("眼 — 交替使用")]
    [SerializeField] private Sprite eyeSpriteA;
    [SerializeField] private Sprite eyeSpriteB;

    [Header("距心最大距离（按类型）")]
    [Tooltip("脚的最大距离，默认 4。")]
    [SerializeField, Min(1)] private int footMaxHeartDistance = 4;
    [Tooltip("手的最大距离，默认 4。")]
    [SerializeField, Min(1)] private int handMaxHeartDistance = 4;
    [Tooltip("眼的最大距离，默认 8。")]
    [SerializeField, Min(1)] private int eyeMaxHeartDistance = 8;

    // 每个类型独立的计数器，交替分配 variant A / B
    private int footVariant;
    private int handVariant;
    private int eyeVariant;

    /// <summary>
    /// 静态确定性获取 Sprite（始终返回 variant A）。
    /// </summary>
    public Sprite GetSprite(OrganType type)
    {
        return type switch
        {
            OrganType.Heart => heartSprite,
            OrganType.Foot  => footSpriteA,
            OrganType.Hand  => handSpriteA,
            OrganType.Eye   => eyeSpriteA,
            _               => null
        };
    }

    /// <summary>
    /// 交替获取 Sprite。
    /// Hand / Foot / Eye 每次调用在 A / B 之间交替；
    /// Heart 始终返回同一张。
    /// </summary>
    public Sprite GetAlternatingSprite(OrganType type)
    {
        return type switch
        {
            OrganType.Heart => heartSprite,
            OrganType.Foot  => (footVariant++ % 2 == 0) ? footSpriteA : footSpriteB,
            OrganType.Hand  => (handVariant++ % 2 == 0) ? handSpriteA : handSpriteB,
            OrganType.Eye   => (eyeVariant++  % 2 == 0) ? eyeSpriteA   : eyeSpriteB,
            _               => null
        };
    }

    /// <summary>
    /// 获取该类型的最大心距离。Heart 无限制（返回 int.MaxValue）。
    /// </summary>
    public int GetHeartDistance(OrganType type)
    {
        return type switch
        {
            OrganType.Heart => int.MaxValue,
            OrganType.Foot  => footMaxHeartDistance,
            OrganType.Hand  => handMaxHeartDistance,
            OrganType.Eye   => eyeMaxHeartDistance,
            _               => 4
        };
    }
}
