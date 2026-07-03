using UnityEngine;

/// <summary>
/// 可被推动的场景物体（箱子、机关等），不具备器官特有的心距离约束。
/// 挂载到场景中可推动物体的 GameObject 上，视觉 SpriteRenderer 放在子对象。
/// </summary>
public class ScenePushable : PushableObject
{
    [Header("场景物体")]
    [SerializeField] private SpriteRenderer visualRenderer;

    public override Vector3 VisualCenter =>
        visualRenderer != null ? visualRenderer.transform.position : transform.position;

    protected override void Awake()
    {
        base.Awake();
        if (visualRenderer == null)
            visualRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    protected override void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.7f, 0.5f, 0.3f, 0.8f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
    }
}
