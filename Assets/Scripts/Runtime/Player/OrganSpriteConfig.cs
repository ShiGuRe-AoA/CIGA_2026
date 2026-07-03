using UnityEngine;

/// <summary>
/// 器官 Sprite 集中配置资产。
/// 在 Project 中创建一个实例，将四种器官的 Sprite 拖入对应字段，
/// 然后所有 OrganUnit 引用此资产即可自动根据 OrganType 切换显示。
/// </summary>
[CreateAssetMenu(menuName = "Anchor/Organ Sprite Config", fileName = "OrganSpriteConfig")]
public class OrganSpriteConfig : ScriptableObject
{
    [Header("器官 Sprite")]
    [SerializeField] private Sprite heartSprite;
    [SerializeField] private Sprite footSprite;
    [SerializeField] private Sprite handSprite;
    [SerializeField] private Sprite eyeSprite;

    /// <summary>
    /// 根据器官类型返回对应的 Sprite。
    /// </summary>
    public Sprite GetSprite(OrganType type)
    {
        return type switch
        {
            OrganType.Heart => heartSprite,
            OrganType.Foot  => footSprite,
            OrganType.Hand  => handSprite,
            OrganType.Eye   => eyeSprite,
            _               => null
        };
    }
}
