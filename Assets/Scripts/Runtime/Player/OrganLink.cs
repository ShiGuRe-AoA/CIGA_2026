using UnityEngine;

/// <summary>
/// 在两个 PushableObject 之间绘制粘液、筋膜或拉丝形态的连线。
///
/// 特点：
/// 1. 使用 LineRenderer 绘制连线。
/// 2. 连线两端保持固定世界长度的粗线区域。
/// 3. 中间保持细线，不会因连接距离增加而扩大粗线区域。
/// 4. 支持轻微弯曲，使连线看起来不完全笔直。
///
/// 使用方式：
/// 调用 Bind(start, end) 绑定两个端点；
/// 调用 Clear() 清除连线。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class OrganLink : MonoBehaviour
{
    [Header("连线采样")]

    [Tooltip("组成连线的采样点数量。数量越高，弯曲越平滑。")]
    [SerializeField, Min(2)]
    private int pointCount = 16;

    [Header("宽度")]

    [Tooltip("靠近两个器官端点时的最大宽度。")]
    [SerializeField, Min(0f)]
    private float thickWidth = 0.3f;

    [Tooltip("中间细线相对于最大宽度的比例。")]
    [SerializeField, Range(0.01f, 1f)]
    private float thinWidthRatio = 0.1f;

    [Tooltip("每个端点附近保持最大宽度的世界空间长度。")]
    [SerializeField, Min(0.01f)]
    private float thickEndWorldLength = 0.35f;

    [Tooltip("从粗线过渡到细线的世界空间长度。")]
    [SerializeField, Min(0.01f)]
    private float taperWorldLength = 0.2f;

    [Header("弯曲")]

    [Tooltip("连线向侧面弯曲的最大世界空间距离。设置为 0 时为直线。")]
    [SerializeField]
    private float bendAmount = 0f;

    [Tooltip("X 表示连线位置，Y 表示该位置的弯曲强度。")]
    [SerializeField]
    private AnimationCurve bendCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 1f),
        new Keyframe(1f, 0f)
    );

    [Header("LineRenderer")]

    [Tooltip("线段末端使用的圆角顶点数量。数值越大，端点越圆。")]
    [SerializeField, Range(0, 16)]
    private int capVertices = 2;

    [Tooltip("线段弯曲处使用的圆角顶点数量。")]
    [SerializeField, Range(0, 16)]
    private int cornerVertices = 4;

    // ─────────── 运行时引用 ───────────

    private LineRenderer lineRenderer;
    private PushableObject startTarget;
    private PushableObject endTarget;

    // 上一次生成 Width Curve 时的连线长度。
    // 用于避免每帧重复创建 AnimationCurve。
    private float lastCurveLength = -1f;

    private const float MinimumLineLength = 0.001f;
    private const float CurveRebuildThreshold = 0.01f;

    // ─────────── 生命周期 ───────────

    /// <summary>
    /// 缓存 LineRenderer，应用基础设置，并在未绑定端点时关闭连线。
    /// </summary>
    private void Awake()
    {
        CacheComponents();
        ApplyRendererSettings();

        // 如果 Bind() 已在 Awake 之前被调用，则不关闭 LineRenderer
        if (lineRenderer != null && startTarget == null)
            lineRenderer.enabled = false;
    }

    /// <summary>
    /// 在所有物体完成本帧移动后更新连线，
    /// 避免平滑移动过程中连线位置延迟一帧。
    /// </summary>
    private void LateUpdate()
    {
        UpdateLine();
    }

    /// <summary>
    /// Inspector 参数变化时重新应用 LineRenderer 设置。
    /// </summary>
    private void OnValidate()
    {
        pointCount = Mathf.Max(2, pointCount);
        thickWidth = Mathf.Max(0f, thickWidth);
        thinWidthRatio = Mathf.Clamp(thinWidthRatio, 0.01f, 1f);
        thickEndWorldLength = Mathf.Max(0.01f, thickEndWorldLength);
        taperWorldLength = Mathf.Max(0.01f, taperWorldLength);

        CacheComponents();
        ApplyRendererSettings();

        // 强制下一次更新时重新生成宽度曲线。
        lastCurveLength = -1f;
    }

    // ─────────── 外部接口 ───────────

    /// <summary>
    /// 绑定连线的起点和终点。
    /// 两个目标的位置均通过 PushableObject.VisualCenter 获取。
    /// </summary>
    /// <param name="start">连线起点。</param>
    /// <param name="end">连线终点。</param>
    public void Bind(PushableObject start, PushableObject end)
    {
        startTarget = start;
        endTarget = end;

        // 更换端点后强制重新生成宽度曲线。
        lastCurveLength = -1f;

        if (lineRenderer != null)
        {
            lineRenderer.enabled =
                startTarget != null &&
                endTarget != null;
        }
    }

    /// <summary>
    /// 清除当前绑定的两个端点并隐藏连线。
    /// </summary>
    public void Clear()
    {
        startTarget = null;
        endTarget = null;
        lastCurveLength = -1f;

        if (lineRenderer != null)
            lineRenderer.enabled = false;
    }

    // ─────────── LineRenderer 初始化 ───────────

    /// <summary>
    /// 获取同一对象上的 LineRenderer。
    /// </summary>
    private void CacheComponents()
    {
        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();
    }

    /// <summary>
    /// 设置 LineRenderer 的基础绘制参数。
    /// 宽度曲线会在 UpdateWidthCurve 中根据实际距离动态生成。
    /// </summary>
    private void ApplyRendererSettings()
    {
        if (lineRenderer == null)
            return;

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = pointCount;

        // widthCurve 中的数值会乘以该整体宽度。
        lineRenderer.widthMultiplier = thickWidth;

        lineRenderer.numCapVertices = capVertices;
        lineRenderer.numCornerVertices = cornerVertices;
        lineRenderer.textureMode = LineTextureMode.Stretch;
    }

    // ─────────── 连线更新 ───────────

    /// <summary>
    /// 根据两个目标的视觉中心更新连线位置、弯曲和宽度。
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

        Vector3 direction = end - start;
        float lineLength = direction.magnitude;

        // 两个端点几乎完全重合时隐藏连线，
        // 避免 LineRenderer 生成异常宽度或方向。
        if (lineLength <= MinimumLineLength)
        {
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.positionCount = pointCount;

        UpdateWidthCurve(lineLength);

        // 计算二维平面中垂直于连线的方向，
        // 用于让连线向一侧弯曲。
        Vector3 normal = new Vector3(
            -direction.y,
            direction.x,
            0f
        ).normalized;

        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);

            Vector3 position = Vector3.Lerp(start, end, t);

            // bendCurve 决定弯曲发生的位置，
            // bendAmount 决定实际弯曲距离。
            float bendOffset =
                bendCurve.Evaluate(t) * bendAmount;

            position += normal * bendOffset;

            lineRenderer.SetPosition(i, position);
        }
    }

    // ─────────── 动态宽度曲线 ───────────

    /// <summary>
    /// 根据连线实际世界长度动态生成 LineRenderer.widthCurve。
    ///
    /// thickEndWorldLength 和 taperWorldLength 使用世界空间长度，
    /// 因此连接距离变长时，两端粗线区域不会跟随比例扩大，
    /// 增加的长度主要分配给中间细线区域。
    /// </summary>
    /// <param name="lineLength">当前连线的世界空间长度。</param>
    private void UpdateWidthCurve(float lineLength)
    {
        if (lineLength <= MinimumLineLength)
            return;

        // 连线长度变化很小时不重新创建曲线，
        // 减少运行时 GC 和不必要的曲线更新。
        if (Mathf.Abs(lineLength - lastCurveLength)
            < CurveRebuildThreshold)
        {
            return;
        }

        lastCurveLength = lineLength;

        // 将世界长度转换成 LineRenderer.widthCurve 使用的 0～1 比例。
        float thickNormalized =
            thickEndWorldLength / lineLength;

        float taperNormalized =
            taperWorldLength / lineLength;

        // 左右两侧总长度不能超过整条线的一半，
        // 否则两端粗线和过渡区域会互相重叠。
        const float maximumSideNormalized = 0.49f;

        float totalSideNormalized =
            thickNormalized + taperNormalized;

        if (totalSideNormalized > maximumSideNormalized)
        {
            float scale =
                maximumSideNormalized / totalSideNormalized;

            thickNormalized *= scale;
            taperNormalized *= scale;
        }

        // 防止关键帧位置完全重合。
        thickNormalized = Mathf.Max(
            thickNormalized,
            0.001f
        );

        taperNormalized = Mathf.Max(
            taperNormalized,
            0.001f
        );

        float leftThickEnd = thickNormalized;

        float leftThinStart =
            thickNormalized + taperNormalized;

        float rightThinEnd =
            1f - leftThinStart;

        float rightThickStart =
            1f - thickNormalized;

        float thinWidth = thinWidthRatio;

        // 让粗细过渡段保持近似线性。
        float descendingSlope =
            (thinWidth - 1f) / taperNormalized;

        float ascendingSlope =
            (1f - thinWidth) / taperNormalized;

        AnimationCurve generatedCurve =
            new AnimationCurve(
                // 左端保持粗线。
                new Keyframe(
                    0f,
                    1f,
                    0f,
                    0f
                ),

                new Keyframe(
                    leftThickEnd,
                    1f,
                    0f,
                    descendingSlope
                ),

                // 左侧由粗线过渡为细线。
                new Keyframe(
                    leftThinStart,
                    thinWidth,
                    descendingSlope,
                    0f
                ),

                // 中间区域保持细线。
                new Keyframe(
                    rightThinEnd,
                    thinWidth,
                    0f,
                    ascendingSlope
                ),

                // 右侧由细线恢复为粗线。
                new Keyframe(
                    rightThickStart,
                    1f,
                    ascendingSlope,
                    0f
                ),

                // 右端保持粗线。
                new Keyframe(
                    1f,
                    1f,
                    0f,
                    0f
                )
            );

        lineRenderer.widthMultiplier = thickWidth;
        lineRenderer.widthCurve = generatedCurve;
    }
}