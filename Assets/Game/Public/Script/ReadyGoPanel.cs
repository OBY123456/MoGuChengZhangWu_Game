using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>准备语音结束后的 3、2、1、GO。显示结束才回调开局。</summary>
public class ReadyGoPanel : MonoBehaviour
{
    public CanvasGroup Myself;
    public AudioSource audioSource;

    [Tooltip("顺序：3、2、1、GO")]
    public AudioClip[] audioClips = new AudioClip[4];

    [Tooltip("显示 3、2、1、GO 的 Image")]
    public Image image;

    [Tooltip("顺序：3、2、1、GO")]
    public Sprite[] sprites = new Sprite[4];

    [Header("倒计时动画")]
    [Min(0.01f)] public float NumberHoldDuration = 0.85f;
    [Min(0.01f)] public float GoHoldDuration = 0.7f;
    [Min(0.01f)] public float ScaleDuration = 0.55f;
    [Min(1f)] public float StartScale = 1.65f;
    [Min(1f)] public float BackOvershoot = 1.45f;

    public bool IsCountingDown { get; private set; }

    private Coroutine countdownRoutine;
    private Tween scaleTween;
    private Action onCountdownFinished;
    private Vector3 normalScale = Vector3.one;

    private void Awake()
    {
        if (Myself == null) Myself = GetComponent<CanvasGroup>();
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.Stop();
        }
        if (image != null)
        {
            normalScale = image.rectTransform.localScale;
            image.raycastTarget = false;
        }
        Hide();
    }

    /// <summary>不负责创建题目或播放 NPC 语音，只负责倒计时。</summary>
    public void PlayCountdown(Action onComplete)
    {
        CancelCountdown();
        onCountdownFinished = onComplete;
        IsCountingDown = true;
        if (Myself != null)
        {
            Myself.alpha = 1f;
            Myself.blocksRaycasts = false;
            Myself.interactable = false;
        }
        if (image != null) image.gameObject.SetActive(true);
        countdownRoutine = StartCoroutine(CountdownFlow());
    }

    private IEnumerator CountdownFlow()
    {
        for (int index = 0; index < 4; index++)
        {
            if (scaleTween != null) scaleTween.Kill();
            scaleTween = null;

            Sprite stepSprite = GetSprite(index);
            if (image != null)
            {
                image.sprite = stepSprite;
                image.enabled = stepSprite != null;
                image.rectTransform.localScale = normalScale * StartScale;
                if (stepSprite != null)
                {
                    scaleTween = image.rectTransform.DOScale(normalScale, ScaleDuration).SetEase(Ease.OutBack, BackOvershoot).SetUpdate(true);
                    scaleTween.Play();
                }
            }

            AudioClip clip = GetAudioClip(index);
            if (audioSource != null)
            {
                audioSource.Stop();
                if (clip != null)
                {
                    audioSource.loop = false;
                    audioSource.clip = clip;
                    audioSource.Play();
                }
            }

            float hold = index == 3 ? GoHoldDuration : NumberHoldDuration;
            hold = Mathf.Max(hold, ScaleDuration);
            if (clip != null && audioSource != null) hold = Mathf.Max(hold, clip.length);
            yield return new WaitForSecondsRealtime(hold);
            if (clip != null && audioSource != null)
            {
                yield return new WaitWhile(() => audioSource != null && audioSource.isPlaying);
            }
        }

        Action callback = onCountdownFinished;
        onCountdownFinished = null;
        countdownRoutine = null;
        IsCountingDown = false;
        if (audioSource != null) audioSource.Stop();
        if (scaleTween != null) scaleTween.Kill();
        scaleTween = null;
        Hide();
        callback?.Invoke();
    }

    private Sprite GetSprite(int index)
    {
        return sprites != null && index < sprites.Length ? sprites[index] : null;
    }

    private AudioClip GetAudioClip(int index)
    {
        return audioClips != null && index < audioClips.Length ? audioClips[index] : null;
    }

    public void CancelCountdown()
    {
        if (countdownRoutine != null) StopCoroutine(countdownRoutine);
        if (scaleTween != null) scaleTween.Kill();
        if (audioSource != null) audioSource.Stop();
        countdownRoutine = null;
        scaleTween = null;
        onCountdownFinished = null;
        IsCountingDown = false;
        Hide();
    }

    private void Hide()
    {
        if (Myself != null)
        {
            Myself.alpha = 0f;
            Myself.blocksRaycasts = false;
            Myself.interactable = false;
        }
        if (image != null)
        {
            image.rectTransform.localScale = normalScale;
            image.enabled = false;
        }
    }

    private void OnDisable()
    {
        CancelCountdown();
    }
}
