
using System;
using System.Collections;
using System.Collections.Generic;
using Config;
using DG.Tweening;
using LanguageSetting;
using MTFrame.MTEvent;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 五关通用结算 UI。场景切换由外部控制器处理。
/// 只使用 TouchUIButton，不依赖原生 Button。
/// </summary>
public class EndPanel : MonoBehaviour
{
    private static EndPanel instance;

    public static EndPanel Instance
    {
        get
        {
            if (instance != null) return instance;
            GameObject prefab = Resources.Load<GameObject>("结算页面");
            if (prefab == null)
            {
                Debug.LogError("Resources 下没有找到【结算页面】预制体。");
                return null;
            }

            GameObject go = Instantiate(prefab);
            go.name = "结算页面";
            if (!go.activeSelf) go.SetActive(true);
            instance = go.GetComponent<EndPanel>();
            if (instance == null) Debug.LogError("结算页面预制体缺少 EndPanel 组件。", go);
            return instance;
        }
    }

    [Header("结算显示容器：只包含结算 UI，不能拖入整个游戏 Canvas")]
    public CanvasGroup Myself;

    [Header("按钮：只使用 TouchUIButton")]
    public TouchUIButton NextBtn;
    public TouchUIButton AgainBtn;

    [Header("自动下一关")]
    public Text CountDownText;
    [SerializeField, Min(1f)] private float autoNextCountDown = 5f;

    [Header("结算声音（0 胜利，1 平局）")]
    public AudioSource _audioSource;
    public AudioClip[] audioClip = new AudioClip[2];

    [Header("胜利装饰（可选：建议拖结算面板内部的 UI 对象）")]
    public GameObject VectorEff;

    [Header("结算背景（0 左胜，1 右胜，2 平局）")]
    public Image EndPanelImage;
    public Sprite[] EndPanelSprite = new Sprite[3];

    [Header("结算文字")]
    public Text ResultText;
    public Text EncouragementText;

    [Header("比分与胜负标记")]
    public GameObject EqualSign;
    public GameObject[] CheckmarkBackground = new GameObject[2];
    public GameObject[] CheckmarkIcon = new GameObject[2];

    [Header("可选：额外需要入场动画的 UI")]
    [Tooltip("例如【本局结算】标题或标题下的短横线的公共父物体")]
    public RectTransform SettlementHeading;
    [Tooltip("右上角 05 倒计时的完整背景框；不填时只动画数字")]
    public RectTransform CountDownRoot;
    [Tooltip("非必填：如果单独显示最后分数，可赋值文本")]
    public Text LeftFinalScoreText;
    public Text RightFinalScoreText;

    [Header("下一关的场景切换入口")]
    [Tooltip("关闭：使用原来的 UIState 消息 Next；开启：使用下方 UnityEvent")]
    public bool UseNextLevelUnityEvent;
    public UnityEvent NextLevelEvent = new UnityEvent();

    [Header("入场与离场速度（使用真实时间，不受暂停影响）")]
    [Min(0.1f)] public float BackgroundDuration = 0.48f;
    [Min(0.1f)] public float ElementDuration = 0.38f;
    [Min(0.05f)] public float ExitDuration = 0.20f;
    [Range(0.5f, 1f)] public float BackgroundStartScale = 0.88f;

    private sealed class UiPart
    {
        public RectTransform Rect;
        public CanvasGroup Group;
        public Vector2 Position;
        public Vector3 Scale;
        public float Alpha;
    }

    private readonly Dictionary<RectTransform, UiPart> parts = new Dictionary<RectTransform, UiPart>();
    private bool hasInitialized;
    private bool isShowing;
    private bool transitionRequested;
    private bool buttonsBound;
    private int sessionId;
    private Sequence entranceSequence;
    private Tween exitTween;
    private Coroutine countdownRoutine;
    private Vector3 backgroundScale = Vector3.one;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeOnce();
    }

    private void InitializeOnce()
    {
        if (hasInitialized) return;
        hasInitialized = true;
        if (Myself == null) Myself = GetComponent<CanvasGroup>();
        if (Myself == null) Myself = gameObject.AddComponent<CanvasGroup>();
        if (EndPanelImage != null) backgroundScale = EndPanelImage.rectTransform.localScale;
        if (backgroundScale == Vector3.zero) backgroundScale = Vector3.one;
        BindButtons();
        HideNow();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        if (NextBtn != null) NextBtn.AddListener(NextEvent);
        if (AgainBtn != null) AgainBtn.AddListener(AgainEvent);
        buttonsBound = true;
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        if (NextBtn != null) NextBtn.RemoveListener(NextEvent);
        if (AgainBtn != null) AgainBtn.RemoveListener(AgainEvent);
        buttonsBound = false;
    }

    public static void Preload()
    {
        EndPanel panel = Instance;
        if (panel != null) panel.InitializeOnce();
    }

    /// <summary>场景即将销毁时只隐藏现有结算页，不创建新预制体。</summary>
    public static void CancelForSceneExit()
    {
        if (instance != null) instance.HideNow();
    }

    public void ShowResult(ResultType result, int leftScore, int rightScore)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        InitializeOnce();
        StopAnimations();
        RestoreParts();
        if ((int)result < 0 || (int)result > 2)
        {
            result = leftScore > rightScore ? ResultType.左边胜利 : rightScore > leftScore ? ResultType.右边胜利 : ResultType.平局;
        }
        sessionId++;
        int id = sessionId;
        isShowing = true;
        transitionRequested = false;

        ApplyContent(result, leftScore, rightScore);
        SetButtonsEnabled(false);
        Myself.alpha = 0f;
        Myself.interactable = false;
        Myself.blocksRaycasts = true;

        if (EndPanelImage != null)
        {
            EndPanelImage.enabled = true;
            EndPanelImage.rectTransform.localScale = backgroundScale * BackgroundStartScale;
        }

        entranceSequence = DOTween.Sequence().SetUpdate(true);
        entranceSequence.Pause();
        PlayBackgroundEntrance(entranceSequence);
        if (result == ResultType.平局) BuildDrawEntrance(entranceSequence);
        else BuildVictoryEntrance(entranceSequence, result);
        BuildCommonEnding(entranceSequence);
        entranceSequence.OnComplete(() => FinishEntrance(id));
        entranceSequence.Play();

        PlayResultAudio(result);
    }

    private void ApplyContent(ResultType result, int left, int right)
    {
        int index = Mathf.Clamp((int)result, 0, 2);
        Sprite sprite = PickSprite(EndPanelSprite, index);
        if (EndPanelImage != null && sprite != null) EndPanelImage.sprite = sprite;

        if (ResultText != null) ResultText.text = Localize(result.ToString());
        string description = result == ResultType.平局 ? "旗鼓相当，再来一局！" : "漂亮的一局，继续保持！";
        if (EncouragementText != null) EncouragementText.text = Localize(description);
        if (LeftFinalScoreText != null) LeftFinalScoreText.text = FormatScore(left);
        if (RightFinalScoreText != null) RightFinalScoreText.text = FormatScore(right);
        if (CountDownText != null) CountDownText.text = Mathf.CeilToInt(autoNextCountDown).ToString("00");

        if (EqualSign != null) EqualSign.SetActive(result == ResultType.平局);
        for (int side = 0; side < 2; side++)
        {
            GameObject background = GetObject(CheckmarkBackground, side);
            GameObject icon = GetObject(CheckmarkIcon, side);
            if (background != null) background.SetActive(true);
            bool winner = result != ResultType.平局 && (int)result == side;
            if (icon != null) icon.SetActive(winner);
        }
        if (VectorEff != null) VectorEff.SetActive(false);
        if (NextBtn != null) NextBtn.gameObject.SetActive(true);
        if (AgainBtn != null) AgainBtn.gameObject.SetActive(true);
    }

    private void PlayBackgroundEntrance(Sequence sequence)
    {
        float duration = Mathf.Max(0.1f, BackgroundDuration);
        sequence.Insert(0f, Myself.DOFade(1f, duration * 0.8f).SetEase(Ease.OutSine));
        if (EndPanelImage != null)
        {
            sequence.Insert(0f, EndPanelImage.rectTransform.DOScale(backgroundScale, duration).SetEase(Ease.OutBack, 1.18f));
        }
        EnterFade(sequence, GetPart(SettlementHeading), duration * 0.82f, 0.22f);
    }

    private void BuildVictoryEntrance(Sequence sequence, ResultType result)
    {
        float baseTime = Mathf.Max(0.1f, BackgroundDuration);
        float duration = Mathf.Max(0.1f, ElementDuration);
        EnterPop(sequence, GetPart(ResultText), baseTime + 0.08f, duration + 0.12f, 0.58f, 1.8f);
        EnterSlide(sequence, GetPart(EncouragementText), baseTime + 0.35f, duration, new Vector2(0f, -18f));

        EnterSlide(sequence, GetPart(GetObject(CheckmarkBackground, 0)), baseTime + 0.46f, duration, new Vector2(-26f, 0f));
        EnterSlide(sequence, GetPart(GetObject(CheckmarkBackground, 1)), baseTime + 0.56f, duration, new Vector2(26f, 0f));
        int winner = result == ResultType.左边胜利 ? 0 : 1;
        EnterPop(sequence, GetPart(GetObject(CheckmarkIcon, winner)), baseTime + 0.82f, 0.45f, 0.22f, 2.2f);

        if (VectorEff != null)
        {
            UiPart effect = GetPart(VectorEff);
            if (effect != null) EnterPop(sequence, effect, baseTime + 0.85f, 0.45f, 0.7f, 1.6f);
            sequence.InsertCallback(baseTime + 0.84f, () => { if (VectorEff != null) VectorEff.SetActive(true); });
        }
    }

    private void BuildDrawEntrance(Sequence sequence)
    {
        float baseTime = Mathf.Max(0.1f, BackgroundDuration);
        float duration = Mathf.Max(0.1f, ElementDuration);
        EnterSlide(sequence, GetPart(ResultText), baseTime + 0.12f, duration + 0.10f, new Vector2(0f, 24f));
        EnterFade(sequence, GetPart(EncouragementText), baseTime + 0.43f, duration);
        EnterSlide(sequence, GetPart(GetObject(CheckmarkBackground, 0)), baseTime + 0.45f, duration, new Vector2(-22f, 0f));
        EnterSlide(sequence, GetPart(GetObject(CheckmarkBackground, 1)), baseTime + 0.45f, duration, new Vector2(22f, 0f));
        EnterPop(sequence, GetPart(EqualSign), baseTime + 0.84f, 0.32f, 0.82f, 1.13f);
    }

    private void BuildCommonEnding(Sequence sequence)
    {
        float baseTime = Mathf.Max(0.1f, BackgroundDuration);
        float duration = Mathf.Max(0.1f, ElementDuration);
        EnterSlide(sequence, GetPart(AgainBtn), baseTime + 1.18f, duration, new Vector2(0f, -24f));
        EnterSlide(sequence, GetPart(NextBtn), baseTime + 1.30f, duration, new Vector2(0f, -24f));
        RectTransform countdown = CountDownRoot != null ? CountDownRoot : CountDownText != null ? CountDownText.rectTransform : null;
        EnterFade(sequence, GetPart(countdown), baseTime + 1.48f, 0.26f);
    }

    private void FinishEntrance(int id)
    {
        if (sessionId != id || !isShowing || transitionRequested) return;
        entranceSequence = null;
        Myself.interactable = true;
        SetButtonsEnabled(true);
        countdownRoutine = StartCoroutine(AutoNextCountdown(id));
    }

    private IEnumerator AutoNextCountdown(int id)
    {
        float endTime = Time.realtimeSinceStartup + Mathf.Max(1f, autoNextCountDown);
        int lastSecond = -1;
        while (isShowing && !transitionRequested && sessionId == id)
        {
            float remaining = Mathf.Max(0f, endTime - Time.realtimeSinceStartup);
            int second = Mathf.CeilToInt(remaining);
            if (second != lastSecond && CountDownText != null) CountDownText.text = second.ToString("00");
            lastSecond = second;
            if (remaining <= 0f) break;
            yield return null;
        }
        countdownRoutine = null;
        if (sessionId == id && isShowing && !transitionRequested) NextEvent();
    }

    private UiPart GetPart(Component component)
    {
        return component != null ? GetPart(component.gameObject) : null;
    }

    private UiPart GetPart(GameObject go)
    {
        if (go == null) return null;
        RectTransform rect = go.transform as RectTransform;
        if (rect == null || rect == Myself.transform) return null;
        if (parts.TryGetValue(rect, out UiPart stored)) return stored;
        CanvasGroup group = go.GetComponent<CanvasGroup>();
        if (group == null) group = go.AddComponent<CanvasGroup>();
        UiPart part = new UiPart();
        part.Rect = rect;
        part.Group = group;
        part.Position = rect.anchoredPosition;
        part.Scale = rect.localScale == Vector3.zero ? Vector3.one : rect.localScale;
        part.Alpha = group.alpha <= 0.01f ? 1f : group.alpha;
        parts.Add(rect, part);
        return part;
    }

    private void EnterFade(Sequence sequence, UiPart part, float start, float duration)
    {
        if (part == null) return;
        part.Group.alpha = 0f;
        part.Rect.anchoredPosition = part.Position;
        part.Rect.localScale = part.Scale;
        sequence.Insert(start, part.Group.DOFade(part.Alpha, duration).SetEase(Ease.OutSine));
    }

    private void EnterSlide(Sequence sequence, UiPart part, float start, float duration, Vector2 offset)
    {
        if (part == null) return;
        part.Group.alpha = 0f;
        part.Rect.localScale = part.Scale;
        part.Rect.anchoredPosition = part.Position + offset;
        sequence.Insert(start, part.Group.DOFade(part.Alpha, duration * 0.72f).SetEase(Ease.OutSine));
        sequence.Insert(start, part.Rect.DOAnchorPos(part.Position, duration).SetEase(Ease.OutCubic));
    }

    private void EnterPop(Sequence sequence, UiPart part, float start, float duration, float from, float overshoot)
    {
        if (part == null) return;
        part.Group.alpha = 0f;
        part.Rect.anchoredPosition = part.Position;
        part.Rect.localScale = part.Scale * from;
        sequence.Insert(start, part.Group.DOFade(part.Alpha, duration * 0.55f).SetEase(Ease.OutSine));
        sequence.Insert(start, part.Rect.DOScale(part.Scale, duration).SetEase(Ease.OutBack, overshoot));
    }

    private void PlayResultAudio(ResultType result)
    {
        if (_audioSource == null) return;
        _audioSource.Stop();
        int index = result == ResultType.平局 ? 1 : 0;
        AudioClip clip = PickClip(audioClip, index);
        if (clip != null) _audioSource.PlayOneShot(clip);
    }

    private static Sprite PickSprite(Sprite[] sprites, int index)
    {
        return sprites != null && index >= 0 && index < sprites.Length ? sprites[index] : null;
    }

    private static AudioClip PickClip(AudioClip[] clips, int index)
    {
        return clips != null && index >= 0 && index < clips.Length ? clips[index] : null;
    }

    private static GameObject GetObject(GameObject[] items, int index)
    {
        return items != null && index >= 0 && index < items.Length ? items[index] : null;
    }

    private static string Localize(string text)
    {
        LanguageManger manager = LanguageManger.Instance;
        return manager != null ? manager.GetValue(text) : text;
    }

    private static string FormatScore(int score)
    {
        return score + " " + Localize("分");
    }

    private void SetButtonsEnabled(bool enabled)
    {
        if (NextBtn != null) NextBtn.interactable = enabled;
        if (AgainBtn != null) AgainBtn.interactable = enabled;
    }

    private void AgainEvent()
    {
        BeginExit(true);
    }

    private void NextEvent()
    {
        BeginExit(false);
    }

    private void BeginExit(bool again)
    {
        if (!isShowing || transitionRequested) return;
        transitionRequested = true;
        SetButtonsEnabled(false);
        StopAnimations();
        int id = ++sessionId;
        exitTween = Myself.DOFade(0f, Mathf.Max(0.05f, ExitDuration)).SetEase(Ease.InQuad).SetUpdate(true).OnComplete(() => FinishExit(id, again));
    }

    private void FinishExit(int id, bool again)
    {
        if (id != sessionId) return;
        exitTween = null;
        HideNow();
        if (again)
        {
            UIState.SendEventMsg("Again", "Again");
        }
        else if (UseNextLevelUnityEvent)
        {
            NextLevelEvent?.Invoke();
        }
        else
        {
            UIState.SendEventMsg("Next", "Next");
        }
    }

    private void StopAnimations()
    {
        if (countdownRoutine != null) StopCoroutine(countdownRoutine);
        if (entranceSequence != null) entranceSequence.Kill();
        if (exitTween != null) exitTween.Kill();
        countdownRoutine = null;
        entranceSequence = null;
        exitTween = null;
    }

    private void RestoreParts()
    {
        foreach (UiPart part in parts.Values)
        {
            if (part == null || part.Rect == null || part.Group == null) continue;
            part.Rect.anchoredPosition = part.Position;
            part.Rect.localScale = part.Scale;
            part.Group.alpha = part.Alpha;
        }
        if (EndPanelImage != null) EndPanelImage.rectTransform.localScale = backgroundScale;
    }

    private void HideNow()
    {
        sessionId++;
        StopAnimations();
        isShowing = false;
        transitionRequested = false;
        SetButtonsEnabled(false);
        RestoreParts();
        if (Myself != null)
        {
            Myself.alpha = 0f;
            Myself.interactable = false;
            Myself.blocksRaycasts = false;
        }
        if (VectorEff != null) VectorEff.SetActive(false);
        if (_audioSource != null) _audioSource.Stop();
    }

    private void OnDisable()
    {
        if (hasInitialized) HideNow();
    }

    private void OnDestroy()
    {
        StopAnimations();
        UnbindButtons();
        if (instance == this) instance = null;
    }

    private void OnValidate()
    {
        autoNextCountDown = Mathf.Max(1f, autoNextCountDown);
        BackgroundDuration = Mathf.Max(0.1f, BackgroundDuration);
        ElementDuration = Mathf.Max(0.1f, ElementDuration);
        ExitDuration = Mathf.Max(0.05f, ExitDuration);
    }
}