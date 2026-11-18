using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class WaitControl : MonoBehaviour
{
    public RectTransform[] rects = new RectTransform[4];

    [Header("呼吸效果")]
    [Tooltip("最大缩放倍率，相对于物体原始大小。例如 1.05 表示放大到原来的 105%。")]
    [Min(1f)]
    public float breatheScale = 1.05f;

    [Tooltip("一次完整呼吸的时间（秒），包含放大和缩回。")]
    [Min(0.1f)]
    public float breatheDuration = 2f;

    [Tooltip("勾选后，即使 Time.timeScale 为 0，呼吸动画仍会播放。")]
    public bool ignoreTimeScale = true;

    private readonly List<Tween> breatheTweens = new List<Tween>();
    private readonly Dictionary<RectTransform, Vector3> originalScales =
        new Dictionary<RectTransform, Vector3>();

    private void OnEnable()
    {
        PlayBreathing();
    }

    private void OnDisable()
    {
        StopBreathing();
    }

    // 运行时修改参数后，也可以调用此方法重新应用。
    [ContextMenu("重新播放呼吸动画")]
    public void PlayBreathing()
    {
        StopBreathing();

        if (!isActiveAndEnabled || rects == null)
            return;

        float targetScale = Mathf.Max(1f, breatheScale);
        float halfDuration = Mathf.Max(0.1f, breatheDuration) * 0.5f;

        foreach (RectTransform rect in rects)
        {
            // 跳过未赋值或重复添加的对象。
            if (rect == null || originalScales.ContainsKey(rect))
                continue;

            Vector3 initialScale = rect.localScale;
            originalScales.Add(rect, initialScale);

            Tween tween = rect
                .DOScale(initialScale * targetScale, halfDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(ignoreTimeScale);

            breatheTweens.Add(tween);
        }
    }

    public void StopBreathing()
    {
        // 只停止本脚本创建的动画。
        foreach (Tween tween in breatheTweens)
            tween.Kill();

        breatheTweens.Clear();

        foreach (KeyValuePair<RectTransform, Vector3> item in originalScales)
        {
            if (item.Key != null)
                item.Key.localScale = item.Value;
        }

        originalScales.Clear();
    }
}