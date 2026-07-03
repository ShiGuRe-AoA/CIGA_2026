using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在两个 PushableObject 之间绘制粘液、筋膜或拉丝形态的连线。
///
/// 连线由三部分组成：
/// 1. 靠近两个目标的固定粗线区域。
/// 2. 从粗线过渡到细线的渐变区域。
/// 3. 中间保持稳定宽度的细线区域。
///
/// 粗线长度、过渡长度和中间细线长度均以世界空间单位配置。
/// Taper Profile 用于控制过渡区域内部从粗到细的变化曲线。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class OrganLink : MonoBehaviour
{
    [Header("连线采样")]

    [Tooltip(
        "组成连线的采样点数量。" +
        "虽然连线是直线，但需要足够的采样点表现宽度曲线。"
    )]
    [SerializeField, Min(2)]
    private int pointCount = 32;

    [Header("宽度")]

    [Tooltip("靠近两个器官端点时的最大宽度。")]
    [SerializeField, Min(0f)]
    private float thickWidth = 0.15f;

    [Tooltip("中间细线相对于最大宽度的比例。")]
    [SerializeField, Range(0.01f, 1f)]
    private float thinWidthRatio = 0.15f;

    [Tooltip("每个端点附近保持最大宽度的世界空间长度。")]
    [SerializeField, Min(0f)]
    private float thickEndWorldLength = 0.2f;

    [Tooltip(
        "从粗线过渡到细线的期望世界空间长度。" +
        "当连线太短时，该长度会自动缩短。"
    )]
    [SerializeField, Min(0.01f)]
    private float taperWorldLength = 2f;

    [Tooltip(
        "尽量为连线中间保留的纯细线世界空间长度。" +
        "当连线较短时，会优先压缩过渡区域。"
    )]
    [SerializeField, Min(0f)]
    private float minimumThinWorldLength = 0.05f;

    [Header("过渡曲线")]

    [Tooltip(
        "控制一侧过渡区域中从粗到细的变化过程。\n" +
        "X=0 表示靠近器官，X=1 表示靠近中间细线。\n" +
        "Y=0 表示保持粗线，Y=1 表示已经变为细线。"
    )]
    [SerializeField]
    private AnimationCurve taperProfile = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.45f, 0.05f),
        new Keyframe(0.75f, 0.4f),
        new Keyframe(1f, 1f)
    );

    [Tooltip(
        "将 Taper Profile 转换为 LineRenderer Width Curve 时的采样数。" +
        "数值越高，复杂曲线越准确。"
    )]
    [SerializeField, Range(2, 32)]
    private int taperSampleCount = 12;

    [Header("Line Renderer")]

    [Tooltip("线段末端的圆角顶点数。")]
    [SerializeField, Range(0, 16)]
    private int capVertices = 2;

    [Tooltip("线段拐角处的圆角顶点数。")]
    [SerializeField, Range(0, 16)]
    private int cornerVertices = 2;

    // ─────────── 运行时引用 ───────────

    private LineRenderer lineRenderer;
    private PushableObject startTarget;
    private PushableObject endTarget;

    // 记录上一次生成宽度曲线时的连线长度，
    // 防止静止状态下每帧创建新的 AnimationCurve。
    private float lastCurveLength = -1f;

    private const float MinimumLineLength = 0.001f;
    private const float CurveRebuildThreshold = 0.01f;

    // ─────────── 生命周期 ───────────

    /// <summary>
    /// 缓存组件并初始化 LineRenderer。
    /// </summary>
    private void Awake()
    {
        CacheComponents();
        ApplyRendererSettings();

        if (lineRenderer != null)
            lineRenderer.enabled = false;
    }

    /// <summary>
    /// 在目标完成本帧移动后更新连线位置。
    /// </summary>
    private void LateUpdate()
    {
        UpdateLine();
    }

    /// <summary>
    /// Inspector 参数变化时重新应用配置。
    /// </summary>
    private void OnValidate()
    {
        pointCount = Mathf.Max(2, pointCount);
        thickWidth = Mathf.Max(0f, thickWidth);
        thinWidthRatio = Mathf.Clamp(thinWidthRatio, 0.01f, 1f);
        thickEndWorldLength = Mathf.Max(0f, thickEndWorldLength);
        taperWorldLength = Mathf.Max(0.01f, taperWorldLength);
        minimumThinWorldLength = Mathf.Max(0f, minimumThinWorldLength);
        taperSampleCount = Mathf.Clamp(taperSampleCount, 2, 32);

        CacheComponents();
        ApplyRendererSettings();

        // 强制下一次更新重新生成宽度曲线。
        lastCurveLength = -1f;
    }

    // ─────────── 外部接口 ───────────

    /// <summary>
    /// 绑定连线的起点和终点。
    /// 连线位置使用 PushableObject.VisualCenter。
    /// </summary>
    /// <param name="start">连线起始目标。</param>
    /// <param name="end">连线结束目标。</param>
    public void Bind(PushableObject start, PushableObject end)
    {
        startTarget = start;
        endTarget = end;
        lastCurveLength = -1f;

        if (lineRenderer != null)
        {
            lineRenderer.enabled =
                startTarget != null &&
                endTarget != null;
        }
    }

    /// <summary>
    /// 清除当前绑定并隐藏连线。
    /// </summary>
    public void Clear()
    {
        startTarget = null;
        endTarget = null;
        lastCurveLength = -1f;

        if (lineRenderer != null)
            lineRenderer.enabled = false;
    }

    // ─────────── 初始化 ───────────

    /// <summary>
    /// 获取当前对象上的 LineRenderer。
    /// </summary>
    private void CacheComponents()
    {
        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();
    }

    /// <summary>
    /// 应用 LineRenderer 的基础配置。
    /// </summary>
    private void ApplyRendererSettings()
    {
        if (lineRenderer == null)
            return;

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = pointCount;
        lineRenderer.widthMultiplier = thickWidth;

        lineRenderer.numCapVertices = capVertices;
        lineRenderer.numCornerVertices = cornerVertices;
        lineRenderer.textureMode = LineTextureMode.Stretch;
    }

    // ─────────── 连线更新 ───────────

    /// <summary>
    /// 更新直线采样点以及动态宽度曲线。
    /// </summary>
    private void UpdateLine()
    {
        if (lineRenderer == null)
            return;

        if (startTarget == null || endTarget == null)
        {
            lineRenderer.enabled = false;
            return;
        }

        Vector3 start = startTarget.VisualCenter;
        Vector3 end = endTarget.VisualCenter;

        float lineLength = Vector3.Distance(start, end);

        if (lineLength <= MinimumLineLength)
        {
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.positionCount = pointCount;

        UpdateWidthCurve(lineLength);

        // 连线本身保持完全笔直。
        // 多个采样点用于让 Width Curve 获得足够的几何精度。
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            Vector3 position = Vector3.Lerp(start, end, t);

            lineRenderer.SetPosition(i, position);
        }
    }

    // ─────────── 宽度曲线 ───────────

    /// <summary>
    /// 根据当前连线长度生成 LineRenderer.widthCurve。
    ///
    /// 当 taperWorldLength 大于当前连线能够容纳的长度时，
    /// 优先保留两端粗线和中间细线，自动缩短过渡区域。
    /// </summary>
    /// <param name="lineLength">当前连线世界空间长度。</param>
    private void UpdateWidthCurve(float lineLength)
    {
        if (lineLength <= MinimumLineLength)
            return;

        if (Mathf.Abs(lineLength - lastCurveLength)
            < CurveRebuildThreshold)
        {
            return;
        }

        lastCurveLength = lineLength;

        // 中间细线不会超过当前连线长度的 80%，
        // 为两端粗线和渐变区域保留最低空间。
        float effectiveMinimumThinLength = Mathf.Min(
            minimumThinWorldLength,
            lineLength * 0.8f
        );

        // 每一侧能够使用的世界空间长度。
        float sideBudget =
            (lineLength - effectiveMinimumThinLength) * 0.5f;

        sideBudget = Mathf.Max(sideBudget, 0.001f);

        // 优先保留器官附近的固定粗线。
        float effectiveThickLength = Mathf.Min(
            thickEndWorldLength,
            sideBudget
        );

        // 剩余空间才分配给渐变区域。
        float availableTaperLength = Mathf.Max(
            sideBudget - effectiveThickLength,
            0.001f
        );

        float effectiveTaperLength = Mathf.Min(
            taperWorldLength,
            availableTaperLength
        );

        float thickNormalized =
            effectiveThickLength / lineLength;

        float taperNormalized =
            effectiveTaperLength / lineLength;

        float leftThickEnd = thickNormalized;

        float leftThinStart =
            thickNormalized + taperNormalized;

        float rightThinEnd =
            1f - leftThinStart;

        float rightThickStart =
            1f - thickNormalized;

        List<Keyframe> keys = new List<Keyframe>();

        // 左侧固定粗线区域。
        keys.Add(new Keyframe(0f, 1f));
        keys.Add(new Keyframe(leftThickEnd, 1f));

        // 左侧：从器官粗线过渡到中间细线。
        for (int i = 1; i <= taperSampleCount; i++)
        {
            float u = i / (float)taperSampleCount;

            float time =
                leftThickEnd + taperNormalized * u;

            float profileValue = EvaluateTaperProfile(u);

            float width = Mathf.Lerp(
                1f,
                thinWidthRatio,
                profileValue
            );

            keys.Add(new Keyframe(time, width));
        }

        // 中间纯细线区域。
        keys.Add(new Keyframe(rightThinEnd, thinWidthRatio));

        // 右侧是左侧过渡的镜像。
        for (int i = 1; i <= taperSampleCount; i++)
        {
            float u = i / (float)taperSampleCount;

            float time =
                rightThinEnd + taperNormalized * u;

            // 从中间向器官移动时，需要反向读取曲线。
            float profileValue =
                EvaluateTaperProfile(1f - u);

            float width = Mathf.Lerp(
                1f,
                thinWidthRatio,
                profileValue
            );

            keys.Add(new Keyframe(time, width));
        }

        // 右侧固定粗线区域。
        keys.Add(new Keyframe(1f, 1f));

        SetLinearTangents(keys);

        lineRenderer.widthMultiplier = thickWidth;
        lineRenderer.widthCurve =
            new AnimationCurve(keys.ToArray());
    }

    /// <summary>
    /// 读取过渡曲线，并将曲线首尾值重新映射到 0～1。
    ///
    /// 这样即使 Inspector 中曲线首尾没有精确设置成 0 和 1，
    /// 最终宽度仍会准确从粗线过渡到细线。
    /// </summary>
    /// <param name="time">过渡区域内的归一化位置。</param>
    /// <returns>归一化后的粗细过渡进度。</returns>
    private float EvaluateTaperProfile(float time)
    {
        time = Mathf.Clamp01(time);

        if (taperProfile == null || taperProfile.length == 0)
            return time;

        float startValue = taperProfile.Evaluate(0f);
        float endValue = taperProfile.Evaluate(1f);

        if (Mathf.Abs(endValue - startValue) <= 0.0001f)
            return time;

        float value = taperProfile.Evaluate(time);

        return Mathf.Clamp01(
            Mathf.InverseLerp(startValue, endValue, value)
        );
    }

    /// <summary>
    /// 根据相邻关键帧设置线性切线。
    ///
    /// Width Curve 由 Taper Profile 多点采样产生，
    /// 使用线性切线可以防止 Unity 自动切线造成宽度过冲、
    /// 局部鼓包或变为负值。
    /// </summary>
    /// <param name="keys">按时间升序排列的关键帧集合。</param>
    private static void SetLinearTangents(List<Keyframe> keys)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            Keyframe key = keys[i];

            if (i > 0)
            {
                Keyframe previous = keys[i - 1];

                float deltaTime =
                    key.time - previous.time;

                if (deltaTime > 0.0001f)
                {
                    key.inTangent =
                        (key.value - previous.value) /
                        deltaTime;
                }
            }
            else
            {
                key.inTangent = 0f;
            }

            if (i < keys.Count - 1)
            {
                Keyframe next = keys[i + 1];

                float deltaTime =
                    next.time - key.time;

                if (deltaTime > 0.0001f)
                {
                    key.outTangent =
                        (next.value - key.value) /
                        deltaTime;
                }
            }
            else
            {
                key.outTangent = 0f;
            }

            keys[i] = key;
        }
    }
}