using UnityEngine;

/// <summary>
/// 在自身周围指定圆形范围内，以随机时间间隔实例化指定预制体。
/// </summary>
public class RandomCircleSpawner : MonoBehaviour
{
    [Header("生成")]
    [SerializeField] private GameObject prefab;
    [SerializeField, Min(0f)] private float radius = 1f;

    [Header("间隔")]
    [SerializeField, Min(0f)] private float minInterval = 0.5f;
    [SerializeField, Min(0f)] private float maxInterval = 1.5f;
    [SerializeField] private bool spawnOnStart;

    [Header("父物体")]
    [Tooltip("为空时生成在场景根节点。")]
    [SerializeField] private Transform spawnParent;

    private float timer;

    private void Start()
    {
        if (spawnOnStart)
            Spawn();

        ResetTimer();
    }

    private void Update()
    {
        if (prefab == null)
            return;

        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        Spawn();
        ResetTimer();
    }

    private void Spawn()
    {
        if (prefab == null)
            return;

        Vector2 offset =
            Random.insideUnitCircle * radius;

        Vector3 position =
            transform.position +
            new Vector3(offset.x, offset.y, 0f);

        Instantiate(
            prefab,
            position,
            Quaternion.identity,
            spawnParent
        );
    }

    private void ResetTimer()
    {
        float min = Mathf.Min(minInterval, maxInterval);
        float max = Mathf.Max(minInterval, maxInterval);
        timer = Random.Range(min, max);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color =
            new Color(0.2f, 0.8f, 1f, 0.35f);

        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
