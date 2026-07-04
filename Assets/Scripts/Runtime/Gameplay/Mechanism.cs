using UnityEngine;

/// <summary>
/// 机关基类。继承此类实现具体机关行为，由 Trigger 在满足条件时调用。
/// </summary>
public abstract class Mechanism : MonoBehaviour
{
    [Header("运行时状态")]
    [SerializeField] protected bool isActive;

    /// <summary>机关当前是否处于激活状态。</summary>
    public bool IsActive => isActive;

    /// <summary>
     /// 当关联的 Trigger 被触发时调用。
     /// </summary>
     /// <param name="source">触发此机关的 Trigger 实例</param>
    public virtual void OnTriggered(Trigger source)
    {
        isActive = true;
    }

    /// <summary>
    /// 当关联的 Trigger 条件关闭或压力板释放时调用。
    /// </summary>
    /// <param name="source">关闭此机关的 Trigger 实例</param>
    public virtual void OnClosed(Trigger source)
    {
        isActive = false;
    }
}
