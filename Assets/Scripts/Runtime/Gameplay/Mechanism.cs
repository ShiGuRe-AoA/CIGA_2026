using UnityEngine;

/// <summary>
/// 机关基类。继承此类实现具体机关行为，由 Trigger 在满足条件时调用。
/// </summary>
public abstract class Mechanism : MonoBehaviour
{
    /// <summary>
    /// 当关联的 Trigger 被触发时调用。
    /// </summary>
    /// <param name="source">触发此机关的 Trigger 实例</param>
    public abstract void OnTriggered(Trigger source);
}
