/// <summary>
/// 器官类型枚举。决定器官容器的基础行为和能力。
/// </summary>
public enum OrganType
{
    /// <summary>心 —— 玩家本体、锚点，被其他器官推动，抵达目标即胜利</summary>
    Heart,

    /// <summary>脚 —— 主要行动器官，可移动、可推动其他器官</summary>
    Foot,

    /// <summary>手 —— 交互器官，可抓取相邻器官/物体</summary>
    Hand,

    /// <summary>眼 —— 视野器官，可放置以扩展视野范围</summary>
    Eye
}
