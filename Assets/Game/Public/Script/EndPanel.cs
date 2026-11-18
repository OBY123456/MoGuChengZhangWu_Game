using System;
using System.Collections;
using System.Collections.Generic;
using Config;
using DG.Tweening;
using LanguageSetting;
using MTFrame.MTEvent;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

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

    [Tooltip("当不是最后一关的时候显示：进入下一关，当已经是最后一关的时候显示：返回首页")]
    public Text NextLevelText;

    [Header("入场与离场速度（使用真实时间，不受暂停影响）")]
    [Min(0.1f)] public float BackgroundDuration = 0.48f;
    [Min(0.1f)] public float ElementDuration = 0.38f;
    [Min(0.05f)] public float ExitDuration = 0.20f;
    [Range(0.5f, 1f)] public float BackgroundStartScale = 0.88f;

    [Header("对应SceneControl里面的SceneDatas的顺序，第五关结算的时候，先播放胜利/失败音效，再播放这个语音，这个语音播放完成才开始倒计时返回首页")]
    public AudioClip[] audioClips_cn = new AudioClip[4];
    public AudioClip[] audioClips_en = new AudioClip[4];

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
    private bool isLastLevel;
    private bool finalVoiceFinished;
    private bool buttonsBound;
    private int sessionId;
    private Sequence entranceSequence;
    private Tween exitTween;
    private Coroutine countdownRoutine;
    private Coroutine resultAudioRoutine;
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
        if (SceneControl.IsTransitionInProgress) return;
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

        isLastLevel = IsCurrentGameLastLevel();
        finalVoiceFinished = !isLastLevel;
        ApplyContent(result);
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

    private void ApplyContent(ResultType result)
    {
        int index = Mathf.Clamp((int)result, 0, 2);
        Sprite sprite = PickSprite(EndPanelSprite, index);
        if (EndPanelImage != null && sprite != null) EndPanelImage.sprite = sprite;

        if (ResultText != null) ResultText.text = Localize(result.ToString());
        string description = result == ResultType.平局 ? "旗鼓相当，再来一局！" : "漂亮的一局，继续保持！";
        if (EncouragementText != null) EncouragementText.text = Localize(description);
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
        string nextLabel = isLastLevel ? "返回首页" : "进入下一关";
        if (NextLevelText != null) NextLevelText.text = LanguageManger.Instance != null ? LanguageManger.Instance.GetValue(nextLabel) : nextLabel;
        if (VectorEff != null) VectorEff.SetActive(false);
        if (NextBtn != null) NextBtn.gameObject.SetActive(!isLastLevel);
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
        if (!isLastLevel) EnterSlide(sequence, GetPart(NextBtn), baseTime + 1.30f, duration, new Vector2(0f, -24f));
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
        while (isLastLevel && !finalVoiceFinished && isShowing && !transitionRequested && sessionId == id) yield return null;
        if (sessionId != id || !isShowing || transitionRequested)
        {
            countdownRoutine = null;
            yield break;
        }
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
        if (_audioSource == null)
        {
            finalVoiceFinished = true;
            return;
        }
        _audioSource.Stop();
        _audioSource.loop = false;
        int index = result == ResultType.平局 ? 1 : 0;
        AudioClip clip = PickClip(audioClip, index);
        if (clip != null) _audioSource.PlayOneShot(clip);
        if (isLastLevel) resultAudioRoutine = StartCoroutine(PlayFinalStoryVoice(sessionId, clip, GetFinalStoryClip()));
    }

    private IEnumerator PlayFinalStoryVoice(int id, AudioClip resultClip, AudioClip storyClip)
    {
        if (resultClip != null) yield return new WaitForSecondsRealtime(resultClip.length);
        yield return new WaitWhile(() => IsAudioSessionActive(id) && _audioSource != null && _audioSource.isPlaying);
        if (!IsAudioSessionActive(id)) yield break;
        if (_audioSource != null && storyClip != null)
        {
            _audioSource.clip = storyClip;
            _audioSource.loop = false;
            _audioSource.Play();
            yield return new WaitForSecondsRealtime(storyClip.length);
            yield return new WaitWhile(() => IsAudioSessionActive(id) && _audioSource != null && _audioSource.isPlaying);
        }
        if (!IsAudioSessionActive(id)) yield break;
        finalVoiceFinished = true;
        resultAudioRoutine = null;
    }

    private bool IsAudioSessionActive(int id)
    {
        return sessionId == id && isShowing && !transitionRequested && !SceneControl.IsTransitionInProgress;
    }

    private AudioClip GetFinalStoryClip()
    {
        SceneControl controller = SceneControl.Instance;
        if (controller == null || controller.SceneDatas == null) return null;
        string activeScene = SceneManager.GetActiveScene().name;
        SceneData currentData = controller.CurrentGameData;
        int gameIndex = -1;
        if (currentData != null && currentData.SceneName != null && Array.IndexOf(currentData.SceneName, activeScene) >= 0) gameIndex = controller.SceneDatas.IndexOf(currentData);
        if (gameIndex < 0)
        {
            for (int i = 0; i < controller.SceneDatas.Count; i++)
            {
                SceneData data = controller.SceneDatas[i];
                if (data == null || data.SceneName == null || Array.IndexOf(data.SceneName, activeScene) < 0) continue;
                gameIndex = i;
                break;
            }
        }
        bool english = LanguageManger.Instance != null && LanguageManger.Instance.GetLanguageType() == LanguageType.英文;
        return PickClip(english ? audioClips_en : audioClips_cn, gameIndex);
    }

    private void StopResultAudio()
    {
        if (resultAudioRoutine != null) StopCoroutine(resultAudioRoutine);
        resultAudioRoutine = null;
        if (_audioSource != null) _audioSource.Stop();
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

    private void SetButtonsEnabled(bool enabled)
    {
        if (NextBtn != null) NextBtn.interactable = enabled && !isLastLevel;
        if (AgainBtn != null) AgainBtn.interactable = enabled;
    }

    private static bool IsCurrentGameLastLevel()
    {
        SceneControl controller = SceneControl.Instance;
        if (controller == null) return false;
        string activeScene = SceneManager.GetActiveScene().name;
        SceneData data = controller.CurrentGameData;
        int index = controller.CurrentLevelIndex;
        if (data == null || data.SceneName == null || index < 0 || index >= data.SceneName.Length || data.SceneName[index] != activeScene)
        {
            data = null;
            if (controller.SceneDatas != null)
            {
                foreach (SceneData entry in controller.SceneDatas)
                {
                    if (entry == null || entry.SceneName == null) continue;
                    int found = Array.IndexOf(entry.SceneName, activeScene);
                    if (found < 0) continue;
                    data = entry;
                    index = found;
                    break;
                }
            }
        }
        if (data == null || data.SceneName == null) return false;
        for (int next = index + 1; next < data.SceneName.Length; next++)
        {
            if (!string.IsNullOrWhiteSpace(data.SceneName[next])) return false;
        }
        return true;
    }

    private static void NotifyControllerReturnHome()
    {
        try
        {
            UDPSend sender = UDPSend.Instance;
            if (sender != null) sender.TrySend(ClassEnum.OrderData, new OrderData(Order.返回首页));
        }
        catch (Exception exception) { Debug.LogWarning("通知控制端返回首页失败，继续本地返回：" + exception.Message); }
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
        if (!isShowing || transitionRequested || SceneControl.IsTransitionInProgress) return;
        transitionRequested = true;
        SetButtonsEnabled(false);
        StopAnimations();
        int id = ++sessionId;
        exitTween = Myself.DOFade(0f, Mathf.Max(0.05f, ExitDuration)).SetEase(Ease.InQuad).SetUpdate(true).OnComplete(() => FinishExit(id, again));
    }

    private void FinishExit(int id, bool again)
    {
        if (id != sessionId || SceneControl.IsTransitionInProgress) return;
        exitTween = null;
        HideNow();
        if (again)
        {
            UIState.SendEventMsg("Again", "Again");
        }
        else
        {
            SceneControl controller = SceneControl.Instance;
            if (controller != null && isLastLevel)
            {
                NotifyControllerReturnHome();
                controller.Back();
                return;
            }
            if (controller != null && controller.TryLoadNextGameScene()) return;
            if (SceneControl.IsTransitionInProgress) return;
            Debug.LogError("下一关切换失败，请检查 SceneControl 的场景配置。", this);
            isShowing = true;
            transitionRequested = false;
            if (Myself != null)
            {
                Myself.alpha = 1f;
                Myself.interactable = true;
                Myself.blocksRaycasts = true;
            }
            SetButtonsEnabled(true);
        }
    }

    private void StopAnimations()
    {
        StopResultAudio();
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