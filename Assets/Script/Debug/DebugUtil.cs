using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Debug 分类枚举
/// </summary>
public enum DebugCategory
{
    Audio,
    UI,
    Input,
    Network,
    Gameplay,
    AI,
    Physics,
    SaveLoad,
    Scene,
    Event,
    Animation,
    General
}

/// <summary>
/// 增强的 Debug 工具类，支持分类着色日志输出。
/// 在 Unity Console 中，不同分类的 [Tag] 会显示为不同颜色，便于快速识别和过滤。
/// </summary>
public static class DebugUtil
{
    #region 配置

    /// <summary>全局开关，设为 false 可禁用所有日志输出</summary>
    public static bool EnableLog = true;

    /// <summary>是否启用 Warning 级别日志</summary>
    public static bool EnableWarning = true;

    /// <summary>是否启用 Error 级别日志</summary>
    public static bool EnableError = true;

    /// <summary>日志时间戳格式，为空则不显示时间</summary>
    public static string TimestampFormat = "HH:mm:ss";

    #endregion

    #region 分类颜色表

    private static readonly Dictionary<DebugCategory, string> CategoryColors = new Dictionary<DebugCategory, string>
    {
        { DebugCategory.Audio,     "#00CED1" }, // 暗青色 - 音频
        { DebugCategory.UI,        "#FFD700" }, // 金色   - UI
        { DebugCategory.Input,     "#FF8C00" }, // 深橙色 - 输入
        { DebugCategory.Network,   "#9370DB" }, // 中紫色 - 网络
        { DebugCategory.Gameplay,  "#32CD32" }, // 石灰绿 - 游戏玩法
        { DebugCategory.AI,        "#FF4500" }, // 橙红色 - AI
        { DebugCategory.Physics,   "#1E90FF" }, // 道奇蓝 - 物理
        { DebugCategory.SaveLoad,  "#A9A9A9" }, // 深灰色 - 存档
        { DebugCategory.Scene,     "#FF69B4" }, // 热粉色 - 场景
        { DebugCategory.Event,     "#FFA500" }, // 橙色   - 事件
        { DebugCategory.Animation, "#EE82EE" }, // 紫罗兰 - 动画
        { DebugCategory.General,   "#FFFFFF" }  // 白色   - 通用
    };

    #endregion

    #region 通用日志方法

    /// <summary>
    /// 输出指定分类的 Log
    /// </summary>
    public static void Log(DebugCategory category, string content, Object context = null)
    {
        if (!EnableLog) return;
        Debug.Log(BuildMessage(category, content), context);
    }

    /// <summary>
    /// 输出指定分类的 Log (带格式化)
    /// </summary>
    public static void LogFormat(DebugCategory category, string format, params object[] args)
    {
        if (!EnableLog) return;
        Debug.Log(BuildMessage(category, string.Format(format, args)));
    }

    /// <summary>
    /// 输出指定分类的 Warning
    /// </summary>
    public static void LogWarning(DebugCategory category, string content, Object context = null)
    {
        if (!EnableLog || !EnableWarning) return;
        Debug.LogWarning(BuildMessage(category, content), context);
    }

    /// <summary>
    /// 输出指定分类的 Warning (带格式化)
    /// </summary>
    public static void LogWarningFormat(DebugCategory category, string format, params object[] args)
    {
        if (!EnableLog || !EnableWarning) return;
        Debug.LogWarning(BuildMessage(category, string.Format(format, args)));
    }

    /// <summary>
    /// 输出指定分类的 Error
    /// </summary>
    public static void LogError(DebugCategory category, string content, Object context = null)
    {
        if (!EnableLog || !EnableError) return;
        Debug.LogError(BuildMessage(category, content), context);
    }

    /// <summary>
    /// 输出指定分类的 Error (带格式化)
    /// </summary>
    public static void LogErrorFormat(DebugCategory category, string format, params object[] args)
    {
        if (!EnableLog || !EnableError) return;
        Debug.LogError(BuildMessage(category, string.Format(format, args)));
    }

    #endregion

    #region 分类快捷方法 — Log

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void AudioLog(string content, Object context = null) => Log(DebugCategory.Audio, content, context);

    public static void UILog(string content, Object context = null) => Log(DebugCategory.UI, content, context);

    public static void InputLog(string content, Object context = null) => Log(DebugCategory.Input, content, context);

    public static void NetworkLog(string content, Object context = null) => Log(DebugCategory.Network, content, context);

    public static void GameplayLog(string content, Object context = null) => Log(DebugCategory.Gameplay, content, context);

    public static void AILog(string content, Object context = null) => Log(DebugCategory.AI, content, context);

    public static void PhysicsLog(string content, Object context = null) => Log(DebugCategory.Physics, content, context);

    public static void SaveLoadLog(string content, Object context = null) => Log(DebugCategory.SaveLoad, content, context);

    public static void SceneLog(string content, Object context = null) => Log(DebugCategory.Scene, content, context);

    public static void EventLog(string content, Object context = null) => Log(DebugCategory.Event, content, context);

    public static void AnimationLog(string content, Object context = null) => Log(DebugCategory.Animation, content, context);

    public static void GeneralLog(string content, Object context = null) => Log(DebugCategory.General, content, context);

    #endregion

    #region 分类快捷方法 — Warning

    public static void AudioLogWarning(string content, Object context = null) => LogWarning(DebugCategory.Audio, content, context);
    public static void UILogWarning(string content, Object context = null) => LogWarning(DebugCategory.UI, content, context);
    public static void InputLogWarning(string content, Object context = null) => LogWarning(DebugCategory.Input, content, context);
    public static void NetworkLogWarning(string content, Object context = null) => LogWarning(DebugCategory.Network, content, context);
    public static void GameplayLogWarning(string content, Object context = null) => LogWarning(DebugCategory.Gameplay, content, context);
    public static void AILogWarning(string content, Object context = null) => LogWarning(DebugCategory.AI, content, context);
    public static void PhysicsLogWarning(string content, Object context = null) => LogWarning(DebugCategory.Physics, content, context);
    public static void SaveLoadLogWarning(string content, Object context = null) => LogWarning(DebugCategory.SaveLoad, content, context);
    public static void SceneLogWarning(string content, Object context = null) => LogWarning(DebugCategory.Scene, content, context);
    public static void EventLogWarning(string content, Object context = null) => LogWarning(DebugCategory.Event, content, context);
    public static void AnimationLogWarning(string content, Object context = null) => LogWarning(DebugCategory.Animation, content, context);
    public static void GeneralLogWarning(string content, Object context = null) => LogWarning(DebugCategory.General, content, context);

    #endregion

    #region 分类快捷方法 — Error

    public static void AudioLogError(string content, Object context = null) => LogError(DebugCategory.Audio, content, context);
    public static void UILogError(string content, Object context = null) => LogError(DebugCategory.UI, content, context);
    public static void InputLogError(string content, Object context = null) => LogError(DebugCategory.Input, content, context);
    public static void NetworkLogError(string content, Object context = null) => LogError(DebugCategory.Network, content, context);
    public static void GameplayLogError(string content, Object context = null) => LogError(DebugCategory.Gameplay, content, context);
    public static void AILogError(string content, Object context = null) => LogError(DebugCategory.AI, content, context);
    public static void PhysicsLogError(string content, Object context = null) => LogError(DebugCategory.Physics, content, context);
    public static void SaveLoadLogError(string content, Object context = null) => LogError(DebugCategory.SaveLoad, content, context);
    public static void SceneLogError(string content, Object context = null) => LogError(DebugCategory.Scene, content, context);
    public static void EventLogError(string content, Object context = null) => LogError(DebugCategory.Event, content, context);
    public static void AnimationLogError(string content, Object context = null) => LogError(DebugCategory.Animation, content, context);
    public static void GeneralLogError(string content, Object context = null) => LogError(DebugCategory.General, content, context);

    #endregion

    #region 扩展功能

    /// <summary>
    /// 绘制调试射线（仅 Editor / Development Build 生效）
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void DrawRay(Vector3 origin, Vector3 direction, Color color, float duration = 0f)
    {
        UnityEngine.Debug.DrawRay(origin, direction, color, duration);
    }

    /// <summary>
    /// 绘制调试线段
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration = 0f)
    {
        UnityEngine.Debug.DrawLine(start, end, color, duration);
    }

    /// <summary>
    /// 在场景视图中显示文字标签
    /// </summary>
    public static void DrawLabel(Vector3 position, string text, Color color)
    {
#if UNITY_EDITOR
        GUIStyle style = new GUIStyle();
        style.normal.textColor = color;
        UnityEditor.Handles.Label(position, text, style);
#endif
    }

    /// <summary>
    /// 断言，条件为 false 时输出 Error
    /// </summary>
    public static void Assert(bool condition, DebugCategory category, string message)
    {
        if (!condition)
            LogError(category, $"[ASSERT FAILED] {message}");
    }

    /// <summary>
    /// 输出变量名和值的格式化日志（仅 Editor 环境）
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void PrintVar(DebugCategory category, string varName, object value)
    {
        Log(category, $"{varName} = {value}");
    }

    /// <summary>
    /// 清空 Unity Console 窗口（仅 Editor 环境）
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void ClearConsole()
    {
#if UNITY_EDITOR
        var logEntries = System.Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
        if (logEntries != null)
        {
            var clearMethod = logEntries.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            clearMethod?.Invoke(null, null);
        }
#endif
    }

    #endregion

    #region 内部方法

    /// <summary>
    /// 构建带颜色 Tag 和时间戳的日志消息
    /// </summary>
    private static string BuildMessage(DebugCategory category, string content)
    {
        string color = CategoryColors.TryGetValue(category, out string c) ? c : "#FFFFFF";
        string tag = category.ToString();

        StringBuilder sb = new StringBuilder();

        // 时间戳
        if (!string.IsNullOrEmpty(TimestampFormat))
        {
            sb.Append($"<color=grey>[{System.DateTime.Now.ToString(TimestampFormat)}]</color> ");
        }

        // 分类标签（着色）
        sb.Append($"<color={color}>[{tag}]</color> ");

        // 内容
        sb.Append(content);

        return sb.ToString();
    }

    #endregion
}
