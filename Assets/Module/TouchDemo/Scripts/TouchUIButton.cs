using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using LanguageSetting;

/// <summary>
/// 通过 ClickManager -> BaseTouch.OnTouch 触发的 UGUI 按钮。
/// 每个按钮独立配置中英文的普通/按下/禁用图片，初始化时选择一次语言。
/// 支持点击间隔、按下持续时间以及 UnityEvent 注册。
/// 兼容 Unity 6 LTS 及旧版 UGUI。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class TouchUIButton : BaseTouch
{
    [Serializable]
    public class TouchButtonClickedEvent : UnityEvent
    {
    }

    [Serializable]
    public class TouchButtonPointEvent : UnityEvent<Vector3>
    {
    }

    [Header("按钮状态")]
    [SerializeField] private bool m_Interactable = true;

    [Tooltip("用于切换 Sprite 的 Image。不设置时自动查找当前物体上的 Image。")]
    [SerializeField] private Image targetImage;

    // 保留这三个旧字段名，Unity 已绑定的中文图片不会丢失。
    [Header("中文按钮图片（原字段）")]
    [Tooltip("中文普通图；不填时使用 Image 初始图片。")]
    [SerializeField] private Sprite normalSprite;

    [Tooltip("中文按下图；不填时使用当前语言的普通图。")]
    [SerializeField] private Sprite pressedSprite;

    [Tooltip("中文禁用图；不填时使用当前语言的普通图。")]
    [SerializeField] private Sprite disabledSprite;

    [Header("英文按钮图片")]
    [Tooltip("英文普通图；不填时回退中文普通图。")]
    [SerializeField] private Sprite englishNormalSprite;

    [Tooltip("英文按下图；不填时回退中文按下图，中文按下图也未填时使用当前普通图。")]
    [SerializeField] private Sprite englishPressedSprite;

    [Tooltip("英文禁用图；不填时回退中文禁用图，中文禁用图也未填时使用当前普通图。")]
    [SerializeField] private Sprite englishDisabledSprite;

    [Header("点击设置")]
    [Min(0f)]
    [Tooltip("两次有效点击之间的最短间隔，使用不受 Time.timeScale 影响的时间。")]
    [SerializeField] private float clickInterval = 1f;

    [Min(0f)]
    [Tooltip("按下 Sprite 保持的时间，之后自动恢复普通状态。")]
    [SerializeField] private float pressedDuration = 0.25f;

    [Tooltip("禁用或隐藏时是否恢复普通/禁用状态。")]
    [SerializeField] private bool resetVisualOnDisable = true;

    [Header("按钮事件")]
    [SerializeField] private TouchButtonClickedEvent m_OnClick = new TouchButtonClickedEvent();

    [Tooltip("需要获取本次 UGUI 世界触点时使用。")]
    [SerializeField] private TouchButtonPointEvent m_OnClickWithPoint = new TouchButtonPointEvent();

    private float nextAllowedClickTime;
    private Coroutine pressedCoroutine;
    private Sprite currentNormalSprite;
    private Sprite currentPressedSprite;
    private Sprite currentDisabledSprite;

    // 按下视觉是否正在保持中。
    // 保持期间即使 interactable 状态发生变化，也不立即覆盖 Pressed Sprite。
    private bool isPressedVisualPlaying;

    public bool interactable
    {
        get
        {
            return m_Interactable;
        }
        set
        {
            if (m_Interactable == value)
            {
                return;
            }

            m_Interactable = value;

            // Pressed Sprite 正在显示时，不立即刷新成 Normal / Disabled。
            // 等按下持续时间结束后，再根据最新 interactable 状态统一刷新。
            if (!isPressedVisualPlaying)
            {
                RefreshVisual();
            }
        }
    }

    public TouchButtonClickedEvent onClick
    {
        get
        {
            return m_OnClick;
        }
        set
        {
            m_OnClick = value ?? new TouchButtonClickedEvent();
        }
    }

    public TouchButtonPointEvent onClickWithPoint
    {
        get
        {
            return m_OnClickWithPoint;
        }
        set
        {
            m_OnClickWithPoint = value ?? new TouchButtonPointEvent();
        }
    }

    public Vector3 LastTouchWorldPoint { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (targetImage == null) targetImage = GetComponent<Image>();
        if (normalSprite == null && targetImage != null) normalSprite = targetImage.sprite;
        RefreshLanguage();
    }

    private void OnEnable()
    {
        nextAllowedClickTime = 0f;
        RefreshVisual();
    }

    private void OnDisable()
    {
        StopPressedCoroutine();
        isPressedVisualPlaying = false;
        if (resetVisualOnDisable) RefreshVisual();
    }

    /// <summary>Awake 时调用一次；如确需运行时切换，可主动调用。</summary>
    public void RefreshLanguage()
    {
        if (targetImage == null) targetImage = GetComponent<Image>();
        bool english = IsEnglishLanguage();
        currentNormalSprite = english && englishNormalSprite != null ? englishNormalSprite : normalSprite;
        if (currentNormalSprite == null && targetImage != null) currentNormalSprite = targetImage.sprite;
        currentPressedSprite = english && englishPressedSprite != null ? englishPressedSprite : pressedSprite;
        currentDisabledSprite = english && englishDisabledSprite != null ? englishDisabledSprite : disabledSprite;
        if (currentPressedSprite == null) currentPressedSprite = currentNormalSprite;
        if (currentDisabledSprite == null) currentDisabledSprite = currentNormalSprite;
        if (!isPressedVisualPlaying) RefreshVisual();
    }

    private static bool IsEnglishLanguage()
    {
        LanguageManger manager = LanguageManger.Instance;
        return manager != null && manager.GetLanguageType() == LanguageType.英文;
    }

    public override void OnTouch(Vector3 point)
    {
        if (!IsActiveAndInteractable())
        {
            return;
        }

        float now = Time.unscaledTime;

        if (now < nextAllowedClickTime)
        {
            return;
        }

        nextAllowedClickTime = now + Mathf.Max(0f, clickInterval);
        LastTouchWorldPoint = point;
        PlayPressedVisual();

        if (m_OnClick != null)
        {
            m_OnClick.Invoke();
        }

        if (m_OnClickWithPoint != null)
        {
            m_OnClickWithPoint.Invoke(point);
        }

        AudioManager.PlayAudio("按钮音效", null, MTFrame.MTAudio.AudioEnunType.Effset);
    }

    public bool IsActiveAndInteractable()
    {
        return m_Interactable && isActiveAndEnabled && gameObject.activeInHierarchy;
    }

    public void SetInteractable(bool value)
    {
        interactable = value;
    }

    public void AddListener(UnityAction callback)
    {
        if (callback == null)
        {
            return;
        }

        if (m_OnClick == null)
        {
            m_OnClick = new TouchButtonClickedEvent();
        }

        m_OnClick.AddListener(callback);
    }

    public void RemoveListener(UnityAction callback)
    {
        if (callback != null && m_OnClick != null)
        {
            m_OnClick.RemoveListener(callback);
        }
    }

    public void RemoveAllListeners()
    {
        if (m_OnClick != null)
        {
            m_OnClick.RemoveAllListeners();
        }

        if (m_OnClickWithPoint != null)
        {
            m_OnClickWithPoint.RemoveAllListeners();
        }
    }

    public void InvokeClick()
    {
        OnTouch(transform.position);
    }

    protected virtual void PlayPressedVisual()
    {
        StopPressedCoroutine();

        isPressedVisualPlaying = true;

        if (targetImage != null) targetImage.sprite = currentPressedSprite;

        if (pressedDuration <= 0f)
        {
            isPressedVisualPlaying = false;
            RefreshVisual();
            return;
        }

        pressedCoroutine = StartCoroutine(RestoreVisualRoutine());
    }

    protected virtual void RefreshVisual()
    {
        if (targetImage == null) return;
        if (!m_Interactable)
        {
            targetImage.sprite = currentDisabledSprite;
            return;
        }
        targetImage.sprite = currentNormalSprite;
    }

    private IEnumerator RestoreVisualRoutine()
    {
        // Pressed Sprite 固定保持 pressedDuration。
        // 在这段时间里，按钮功能即使把 interactable 改成 false，
        // 也不会提前覆盖 Pressed Sprite。
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, pressedDuration));

        pressedCoroutine = null;
        isPressedVisualPlaying = false;

        // 按下动画结束后，再读取“此刻”的 interactable 状态：
        // true  -> Normal Sprite
        // false -> Disabled Sprite
        RefreshVisual();
    }

    private void StopPressedCoroutine()
    {
        if (pressedCoroutine == null)
        {
            return;
        }

        StopCoroutine(pressedCoroutine);
        pressedCoroutine = null;
    }

    private void OnValidate()
    {
        clickInterval = Mathf.Max(0f, clickInterval);
        pressedDuration = Mathf.Max(0f, pressedDuration);
    }
}
