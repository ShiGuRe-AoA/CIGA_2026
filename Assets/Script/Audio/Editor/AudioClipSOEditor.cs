using UnityEditor;
using UnityEngine;

/// <summary>
/// AudioClipSO 的自定义 Inspector，添加「Sync Name」按钮及文件名同步。
/// </summary>
[CustomEditor(typeof(AudioClipSO))]
public class AudioClipSOEditor : Editor
{
    private const string ASSET_PREFIX = "AudioClip_";

    public override void OnInspectorGUI()
    {
        // 处理 OnValidate 中标记的延迟文件重命名
        AudioClipSO audioClipSO = (AudioClipSO)target;
        if (audioClipSO.PendingFileRename)
        {
            audioClipSO.PendingFileRename = false;
            RenameAssetFile(audioClipSO);
        }

        // 绘制默认 Inspector
        DrawDefaultInspector();

        EditorGUILayout.Space(8);

        DrawSyncSection(audioClipSO);
    }

    /// <summary>
    /// 绘制同步区域：文件名与 AudioName 对比 + Sync 按钮。
    /// </summary>
    private void DrawSyncSection(AudioClipSO audioClipSO)
    {
        if (audioClipSO.Clip == null)
        {
            EditorGUILayout.HelpBox(
                "请先拖入 AudioClip，之后可通过下方按钮同步 AudioName 和文件名。",
                MessageType.Warning);
            return;
        }

        string clipName = audioClipSO.Clip.name;
        string assetPath = AssetDatabase.GetAssetPath(audioClipSO);
        string assetFileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        string targetFileName = $"{ASSET_PREFIX}{clipName}";

        bool nameMismatch = audioClipSO.AudioName != clipName;
        bool fileMismatch = assetFileName != targetFileName;

        if (nameMismatch || fileMismatch)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("名称不一致", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Clip 名称:", clipName);
            EditorGUILayout.LabelField($"AudioName:", audioClipSO.AudioName,
                nameMismatch ? new GUIStyle(EditorStyles.label) { normal = { textColor = Color.yellow } } : EditorStyles.label);
            EditorGUILayout.LabelField($"文件名:", assetFileName,
                fileMismatch ? new GUIStyle(EditorStyles.label) { normal = { textColor = Color.yellow } } : EditorStyles.label);
            EditorGUILayout.LabelField($"目标文件名:", targetFileName);

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            {
                if (nameMismatch && GUILayout.Button("Sync AudioName", GUILayout.Height(26)))
                {
                    Undo.RecordObject(audioClipSO, "Sync AudioClipSO Name");
                    audioClipSO.SyncNameFromClip();
                }

                if (fileMismatch && GUILayout.Button("Sync File Name", GUILayout.Height(26)))
                {
                    RenameAssetFile(audioClipSO);
                }

                if (GUILayout.Button("Sync All", GUILayout.Height(26)))
                {
                    Undo.RecordObject(audioClipSO, "Sync AudioClipSO All");
                    audioClipSO.SyncNameFromClip();
                    RenameAssetFile(audioClipSO);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"AudioName 与文件名均已同步: \"{clipName}\"",
                MessageType.None);
        }
    }

    /// <summary>
    /// 将资产文件重命名为 AudioName 对应的文件名。
    /// </summary>
    private static void RenameAssetFile(AudioClipSO audioClipSO)
    {
        if (audioClipSO.Clip == null) return;

        string assetPath = AssetDatabase.GetAssetPath(audioClipSO);
        if (string.IsNullOrEmpty(assetPath)) return;

        string directory = System.IO.Path.GetDirectoryName(assetPath);
        string newName = $"{ASSET_PREFIX}{audioClipSO.Clip.name}";
        string newPath = $"{directory}/{newName}.asset";

        if (assetPath == newPath) return;

        // 避免与已有文件冲突
        if (AssetDatabase.LoadAssetAtPath<Object>(newPath) != null)
        {
            Debug.LogWarning($"[AudioClipSOEditor] 目标路径已存在文件: {newPath}");
            return;
        }

        string result = AssetDatabase.RenameAsset(assetPath, newName);
        if (!string.IsNullOrEmpty(result))
        {
            Debug.LogError($"[AudioClipSOEditor] 重命名失败: {result}");
        }
        else
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}

