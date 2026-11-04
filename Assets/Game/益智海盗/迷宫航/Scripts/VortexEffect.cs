using DG.Tweening;
using UnityEngine;

/// <summary>UI漩涡：顺时针匀速旋转，同时轻微呼吸。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class VortexEffect : MonoBehaviour
{
    [Tooltip("动画目标；不填时使用当前RectTransform。父级有入场缩放时，建议指定独立的子级Image。")]
    public RectTransform Target;

    [Tooltip("顺时针旋转一圈所需秒数，越小越快。")]
    [Min(0.1f)] public float RotateDuration = 3f;

    [Tooltip("一次放大或缩小的时长，完整呼吸周期为此值的两倍。")]
    [Min(0.1f)] public float BreathDuration = 0.9f;

    [Tooltip("最大放大幅度，支持运行时修改；0不呼吸，0.08表示最大放大8%。")]
    [Range(0f, 0.3f)] public float BreathAmount = 0.08f;

    [Tooltip("开启后动画不受Time.timeScale影响。")]
    public bool IgnoreTimeScale = true;

    private Vector3 originalScale;
    private Vector3 originalEuler;
    private Tween rotateTween;
    private Tween breathTween;
    private float breathProgress;

    private void Awake()
    {
        if (Target == null) Target = GetComponent<RectTransform>();
        originalScale = Target.localScale;
        originalEuler = Target.localEulerAngles;
    }

    private void OnEnable()
    {
        Play();
    }

    public void Play()
    {
        Stop();
        if (!isActiveAndEnabled || Target == null) return;
        rotateTween = Target.DOLocalRotate(originalEuler + new Vector3(0f, 0f, -360f), Mathf.Max(0.1f, RotateDuration), RotateMode.FastBeyond360).SetEase(Ease.Linear).SetLoops(-1, LoopType.Restart).SetUpdate(IgnoreTimeScale).SetRecyclable(false);
        breathTween = DOTween.To(() => breathProgress, SetBreathProgress, 1f, Mathf.Max(0.1f, BreathDuration)).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(IgnoreTimeScale).SetRecyclable(false);
    }

    private void SetBreathProgress(float value)
    {
        breathProgress = value;
        // 每次动画更新都读取当前幅度，运行时调成0后恢复初始缩放，旋转继续。
        if (Target != null) Target.localScale = originalScale * (1f + Mathf.Clamp(BreathAmount, 0f, 0.3f) * breathProgress);
    }

    public void Stop()
    {
        if (rotateTween != null) rotateTween.Kill(false);
        if (breathTween != null) breathTween.Kill(false);
        rotateTween = null;
        breathTween = null;
        breathProgress = 0f;
        if (Target == null) return;
        Target.localScale = originalScale;
        Target.localEulerAngles = originalEuler;
    }

    private void OnDisable()
    {
        Stop();
    }
}
