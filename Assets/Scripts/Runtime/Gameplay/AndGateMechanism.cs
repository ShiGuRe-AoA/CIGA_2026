using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 与门机关。
/// 每次被触发时累加计数，达到指定次数后触发目标 Mechanism 列表。
/// </summary>
public class AndGateMechanism : Mechanism
{
    [Header("计数")]
    [SerializeField, Min(1)] private int requiredCount = 2;

    [Tooltip("达到目标次数后是否只触发一次。")]
    [SerializeField] private bool triggerOnlyOnce = true;

    [Header("目标机关")]
    [SerializeField] private List<Mechanism> targetMechanisms;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;

    private int currentCount;
    private bool hasTriggeredTarget;

    /// <summary>当前累计触发次数。</summary>
    public int CurrentCount => currentCount;

    /// <summary>触发目标所需次数。</summary>
    public int RequiredCount => requiredCount;

    /// <summary>
    /// 被触发时累加计数，达到阈值后触发目标机关。
    /// </summary>
    public override void OnTriggered(Trigger source)
    {
        base.OnTriggered(source);

        if (triggerOnlyOnce && hasTriggeredTarget)
            return;

        currentCount++;
        Log($"计数 {currentCount}/{requiredCount}");

        if (currentCount < requiredCount)
            return;

        hasTriggeredTarget = true;
        TriggerTargets(source);

        Log("已触发目标机关");
    }

    /// <summary>
    /// 关闭时减少与门计数。
    /// </summary>
    public override void OnClosed(Trigger source)
    {
        base.OnClosed(source);
        DecreaseCount();
    }

    /// <summary>手动重置计数器。</summary>
    public void ResetCount()
    {
        currentCount = 0;

        if (!triggerOnlyOnce)
            hasTriggeredTarget = false;
    }

    /// <summary>手动减少计数器。</summary>
    public void DecreaseCount()
    {
        currentCount = Mathf.Max(0, currentCount - 1);

        if (currentCount < requiredCount && !triggerOnlyOnce)
            hasTriggeredTarget = false;

        Log($"计数 {currentCount}/{requiredCount}");
    }

    private void OnValidate()
    {
        requiredCount = Mathf.Max(1, requiredCount);
    }

    private void TriggerTargets(Trigger source)
    {
        if (targetMechanisms == null)
            return;

        foreach (Mechanism mechanism in targetMechanisms)
        {
            if (mechanism != null)
                mechanism.OnTriggered(source);
        }
    }

    private void Log(string message)
    {
        if (!showDebugLog)
            return;

        Debug.Log($"[AndGateMechanism] {name}：{message}", this);
    }
}
