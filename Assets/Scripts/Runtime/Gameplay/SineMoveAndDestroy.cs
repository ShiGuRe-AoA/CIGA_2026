using UnityEngine;

/// <summary>
/// 让物体以随机相位和随机速度沿正弦曲线移动，并在指定时间后销毁自身。
/// </summary>
public class SineMoveAndDestroy : MonoBehaviour
{
    [Header("正弦移动")]
    [SerializeField] private Vector2 moveDirection = Vector2.right;
    [SerializeField] private Vector2 sineDirection = Vector2.up;

    [SerializeField, Min(0f)] private float moveSpeed = 0.5f;
    [SerializeField, Min(0f)] private float amplitude = 0.25f;

    [Header("随机速度")]
    [SerializeField, Min(0f)] private float minFrequency = 1f;
    [SerializeField, Min(0f)] private float maxFrequency = 2f;

    [Header("生命周期")]
    [SerializeField, Min(0f)] private float lifeTime = 2f;

    private Vector3 startPosition;
    private Vector2 normalizedMoveDirection;
    private Vector2 normalizedSineDirection;
    private float phase;
    private float frequency;
    private float age;

    private void Start()
    {
        startPosition = transform.position;

        normalizedMoveDirection =
            moveDirection.sqrMagnitude > 0.0001f
                ? moveDirection.normalized
                : Vector2.right;

        normalizedSineDirection =
            sineDirection.sqrMagnitude > 0.0001f
                ? sineDirection.normalized
                : Vector2.up;

        phase = Random.Range(0f, Mathf.PI * 2f);
        frequency = Random.Range(
            Mathf.Min(minFrequency, maxFrequency),
            Mathf.Max(minFrequency, maxFrequency)
        );

        if (lifeTime <= 0f)
            Destroy(gameObject);
    }

    private void Update()
    {
        age += Time.deltaTime;

        if (age >= lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 forwardOffset =
            (Vector3)(normalizedMoveDirection * moveSpeed * age);

        float sine =
            Mathf.Sin(age * frequency * Mathf.PI * 2f + phase);

        Vector3 sineOffset =
            (Vector3)(normalizedSineDirection * amplitude * sine);

        transform.position =
            startPosition + forwardOffset + sineOffset;
    }
}
