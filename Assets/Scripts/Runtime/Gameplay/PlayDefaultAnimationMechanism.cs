using System.Collections;
using UnityEngine;

/// <summary>
/// Mechanism that keeps an Animator stopped on startup, then plays its default animation once when triggered.
/// </summary>
public class PlayDefaultAnimationMechanism : Mechanism
{
    [Header("Animation")]
    [SerializeField] private Animator targetAnimator;

    [Tooltip("Disable the Animator after the default clip duration so looping controller states do not keep playing.")]
    [SerializeField] private bool disableAfterPlayback = true;

    [Tooltip("Used when the Animator Controller has no clips to measure.")]
    [SerializeField, Min(0f)] private float fallbackDuration = 1f;

    private Coroutine playRoutine;

    private void Awake()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        StopAnimatorAtDefaultPose();
    }

    public override void OnTriggered(Trigger source)
    {
        base.OnTriggered(source);

        AudioPlayer.PlayBGM("EndBGM");
        AudioPlayer.PlayOneShot("SFX_Win");

        if (targetAnimator == null)
            return;

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        playRoutine = StartCoroutine(PlayOnce());
    }

    private IEnumerator PlayOnce()
    {
        targetAnimator.enabled = true;
        targetAnimator.speed = 1f;
        targetAnimator.Rebind();
        targetAnimator.Update(0f);

        if (disableAfterPlayback)
        {
            yield return new WaitForSeconds(GetDefaultClipDuration());
            StopAnimatorAtCurrentPose();
        }

        playRoutine = null;
    }

    private void StopAnimatorAtDefaultPose()
    {
        if (targetAnimator == null)
            return;

        targetAnimator.enabled = true;
        targetAnimator.speed = 0f;
        targetAnimator.Rebind();
        targetAnimator.Update(0f);
        targetAnimator.enabled = false;
    }

    private void StopAnimatorAtCurrentPose()
    {
        if (targetAnimator == null)
            return;

        targetAnimator.speed = 0f;
        targetAnimator.enabled = false;
    }

    private float GetDefaultClipDuration()
    {
        RuntimeAnimatorController controller = targetAnimator.runtimeAnimatorController;

        if (controller == null ||
            controller.animationClips == null ||
            controller.animationClips.Length == 0)
        {
            return fallbackDuration;
        }

        return Mathf.Max(0f, controller.animationClips[0].length);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        fallbackDuration = Mathf.Max(0f, fallbackDuration);

        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>();
    }
#endif
}
