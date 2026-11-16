using System.Collections;
using System.Collections.Generic;
using Coffee.UIEffects;
using DG.Tweening;
using LanguageSetting;
using MTFrame.MTEvent;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[System.Serializable]
public class SceneData
{
    [Tooltip("游戏大标题")]
    public string GameName;

    [Tooltip("游戏关卡名字，也是实际场景名字，加载场景用这个，数组顺序就是关卡顺序")]
    public string[] SceneName = new string[5];

    [Tooltip("加载页背景图")]
    public Sprite BG;

    [Tooltip("加载页logo，0是中文，1是英文")]
    public Sprite[] Logo = new Sprite[2];

    [Tooltip("加载提示语")]
    public string loadingPrompt;
}

public class SceneControl : MonoBehaviour
{
    private static SceneControl instance;

    public static SceneControl Instance
    {
        get
        {
            if (instance != null) return instance;
            SceneControl existing = FindObjectOfType<SceneControl>();
            if (existing != null) return existing;

            GameObject prefab = Resources.Load<GameObject>("加载页面");
            if (prefab == null || prefab.GetComponent<SceneControl>() == null)
            {
                Debug.LogError("Resources 下的【加载页面】预制体不存在或缺少 SceneControl。");
                return null;
            }

            GameObject go = Instantiate(prefab);
            go.name = "加载页面";
            if (!go.activeSelf) go.SetActive(true);
            return go.GetComponent<SceneControl>();
        }
    }

    [Header("首页场景名称")]
    public string HomeSceneName = "选择页";

    public List<SceneData> SceneDatas = new List<SceneData>();

    [Tooltip("加载进度条，固定用2秒真实时间播放分段进度动画")]
    public Image LoadingTimerImage;

    [Tooltip("加载进度，格式为75%，整数")]
    public Text LoadingNumber;

    public Text LoadingPromptText;

    public Image LogoImage;

    [Tooltip("加载页背景图的 Image")]
    public Image BackgroundImage;

    [Tooltip("加载页的 CanvasGroup，不赋值时自动查找自身或子物体")]
    public CanvasGroup Myself;

    [Tooltip("加载页渐入的时长，使用真实时间")]
    [Min(0f)] public float FadeDuration = 0.25f;

    private const float LoadingProgressDuration = 2f;
    private const float HideObjectsDuration = 0.2f;
    private readonly HashSet<BaseGameManager> startupManagers = new HashSet<BaseGameManager>();
    private readonly Dictionary<CanvasGroup, float> hideObjectAlphas = new Dictionary<CanvasGroup, float>();
    private bool isLoadingScene;
    private bool returningHome;
    private bool homeLoadCompleted;
    private int transitionVersion;
    private string targetSceneName;
    private AsyncOperation sceneOperation;
    private Tween fadeTween;
    private Sequence loadingProgressTween;

    public bool IsLoading => isLoadingScene;
    public static bool IsReturningHome => instance != null && instance.returningHome;
    public static bool IsTransitionInProgress => instance != null && instance.isLoadingScene;
    public SceneData CurrentGameData { get; private set; }
    public int CurrentLevelIndex { get; private set; } = -1;

    [Tooltip("这是调用BGHideEffct的Hide方法前需要隐藏的物体，用0.2秒的时间渐变消失这些物体，但是显示加载页面的时候你要记得恢复他们")]
    public CanvasGroup[] HideObj;

    [Tooltip("使用这个脚本的Hide来隐藏页面，不再使用Myself的渐变消失了")]
    public UITransitionEffect BGHideEffct;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        if (transform.parent != null) transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
        if (Myself == null) Myself = GetComponentInChildren<CanvasGroup>(true);
        CacheHideObjectAlphas();
        SetLoadingPageVisible(false);
        if (LoadingTimerImage != null)
        {
            LoadingTimerImage.type = Image.Type.Filled;
            LoadingTimerImage.fillMethod = Image.FillMethod.Horizontal;
            LoadingTimerImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        }
        SetLoadingProgress(0f);

        EventManager.AddListener(GenericEventEnumType.Message, nameof(UdpGameName), OnSceneNameReceived);
        EventManager.AddListener(GenericEventEnumType.Message, nameof(OrderData), OnOrderReceived);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneNameReceived(EventParamete parameters)
    {
        if (returningHome || parameters == null) return;
        var messages = parameters.GetParameter<UdpGameName>();
        if (messages == null) return;
        foreach (UdpGameName message in messages)
        {
            if (message == null) continue;
            // 协议字段沿用 Scenename，实际内容是游戏名称。
            TryLoadFirstGameScene(message.GameName);
            return;
        }
    }

    public bool TryLoadFirstGameScene(string gameName)
    {
        if (isLoadingScene || returningHome || string.IsNullOrWhiteSpace(gameName)) return false;
        string requestedName = gameName.Trim();
        SceneData matchedGame = null;
        if (SceneDatas != null)
        {
            foreach (SceneData data in SceneDatas)
            {
                if (data == null || !string.Equals(data.GameName?.Trim(), requestedName, System.StringComparison.Ordinal)) continue;
                if (matchedGame != null)
                {
                    Debug.LogError($"游戏名称重复，无法确定第一关：{requestedName}", this);
                    return false;
                }
                matchedGame = data;
            }
        }

        if (matchedGame == null)
        {
            Debug.LogError($"没有找到游戏名称对应的配置：{requestedName}", this);
            return false;
        }
        if (matchedGame.SceneName == null || matchedGame.SceneName.Length == 0 || string.IsNullOrWhiteSpace(matchedGame.SceneName[0]))
        {
            Debug.LogError($"游戏未配置第一关场景：{requestedName}", this);
            return false;
        }

        return TryLoadScene(matchedGame.SceneName[0]);
    }

    private void OnOrderReceived(EventParamete parameters)
    {
        if (parameters == null) return;
        var messages = parameters.GetParameter<OrderData>();
        if (messages == null) return;
        foreach (OrderData message in messages)
        {
            if (message != null && message.order == Order.返回首页) TryReturnHome();
        }
    }

    public static bool TryRegisterGameManagerForStartup(BaseGameManager gameManager)
    {
        if (gameManager == null) return false;
        SceneControl controller = Instance;
        return controller != null && controller.RegisterGameManager(gameManager);
    }

    public static void UnregisterGameManagerForStartup(BaseGameManager gameManager)
    {
        if (instance != null) instance.startupManagers.Remove(gameManager);
    }

    private bool RegisterGameManager(BaseGameManager gameManager)
    {
        if (returningHome) return true;
        string sceneName = gameManager.gameObject.scene.name;
        if (isLoadingScene)
        {
            if (sceneName != targetSceneName) return false;
            startupManagers.Add(gameManager);
            return true;
        }
        if (!TryFindGameScene(sceneName, out SceneData data, out int levelIndex)) return false;

        RecordGame(data, levelIndex);
        if (!gameManager.AutoReadyGoOnDirectSceneStart) return false;
        startupManagers.Add(gameManager);
        BeginTransition(sceneName, data, levelIndex, true);
        return true;
    }

    public void LoadScene(string sceneName)
    {
        TryLoadScene(sceneName);
    }

    public void LoadGameScene(string sceneName)
    {
        TryLoadScene(sceneName);
    }

    public void Back()
    {
        TryReturnHome();
    }

    public bool TryLoadNextGameScene()
    {
        if (isLoadingScene || returningHome) return true;
        string activeSceneName = SceneManager.GetActiveScene().name;
        string[] sceneNames = CurrentGameData?.SceneName;
        if (sceneNames == null || CurrentLevelIndex < 0 || CurrentLevelIndex >= sceneNames.Length || sceneNames[CurrentLevelIndex] != activeSceneName)
        {
            if (!TryFindGameScene(activeSceneName, out SceneData data, out int levelIndex)) return false;
            RecordGame(data, levelIndex);
            sceneNames = data.SceneName;
        }

        int nextLevelIndex = CurrentLevelIndex + 1;
        while (nextLevelIndex < sceneNames.Length && string.IsNullOrWhiteSpace(sceneNames[nextLevelIndex])) nextLevelIndex++;
        string nextSceneName = nextLevelIndex < sceneNames.Length ? sceneNames[nextLevelIndex] : HomeSceneName;
        if (!CanLoadScene(nextSceneName)) return false;

        // 只通知一次，不等待控制端回复；离线或发送失败均不影响本地切关。
        NotifyControllerNextLevel();
        return TryLoadScene(nextSceneName);
    }

    private static void NotifyControllerNextLevel()
    {
        try
        {
            UDPSend sender = UDPSend.Instance;
            if (sender != null && sender.IsConnected) sender.TrySend(ClassEnum.OrderData, new OrderData(Order.下一关));
        }
        catch (System.Exception exception) { Debug.LogWarning("通知控制端下一关失败，继续本地切关：" + exception.Message); }
    }

    private bool CanLoadScene(string sceneName)
    {
        if (!string.IsNullOrWhiteSpace(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName)) return true;
        Debug.LogError($"场景未加入可加载场景列表：{sceneName}", this);
        return false;
    }

    public bool TryReturnHome()
    {
        if (returningHome) return true;
        if (SceneManager.GetActiveScene().name == HomeSceneName && !isLoadingScene && sceneOperation == null)
        {
            SetLoadingPageVisible(false);
            RecordGame(null, -1);
            return true;
        }
        if (!CanLoadScene(HomeSceneName)) return false;

        // 必须先设置抢占标记，旧场景被强制激活时也不能启动准备或游戏。
        returningHome = true;
        homeLoadCompleted = false;
        isLoadingScene = true;
        transitionVersion++;
        targetSceneName = HomeSceneName;
        StopAllCoroutines();
        KillLoadingAnimations();
        SetLoadingPageVisible(false);
        startupManagers.Clear();
        StopCurrentGameForSceneExit();
        EndPanel.CancelForSceneExit();
        RecordGame(null, -1);

        AsyncOperation pendingOperation = sceneOperation;
        sceneOperation = null;
        if (pendingOperation != null && !pendingOperation.isDone) pendingOperation.allowSceneActivation = true;
        try
        {
            // LoadScene 强制完成旧异步任务并切回首页，避免任务队列被旧加载阻塞。
            SceneManager.LoadScene(HomeSceneName, LoadSceneMode.Single);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
            returningHome = false;
            isLoadingScene = false;
            targetSceneName = null;
            return false;
        }

        StartCoroutine(FinishReturnHome());
        return true;
    }

    private IEnumerator FinishReturnHome()
    {
        // 不能只检查活动场景名称：从首页进入加载时，活动场景可能仍然是首页。
        while (!homeLoadCompleted) yield return null;
        yield return null;
        SetLoadingPageVisible(false);
        startupManagers.Clear();
        targetSceneName = null;
        returningHome = false;
        isLoadingScene = false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!returningHome) return;
        if (scene.name == HomeSceneName)
        {
            homeLoadCompleted = true;
            return;
        }

        // 强制结束旧异步加载时可能触发旧场景的 Awake；在首帧渲染和 Start 前停用它。
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (BaseGameManager manager in root.GetComponentsInChildren<BaseGameManager>(true)) manager.CancelForSceneExit();
            root.SetActive(false);
        }
    }

    private void KillLoadingAnimations()
    {
        if (fadeTween != null) fadeTween.Kill();
        if (loadingProgressTween != null) loadingProgressTween.Kill();
        fadeTween = null;
        loadingProgressTween = null;
        if (BGHideEffct != null) BGHideEffct.ResetEffectFactor();
    }

    private bool TryLoadScene(string sceneName)
    {
        if (sceneName == HomeSceneName) return TryReturnHome();
        if (isLoadingScene || returningHome || string.IsNullOrWhiteSpace(sceneName)) return false;
        if (!CanLoadScene(sceneName)) return false;
        if (SceneManager.GetActiveScene().name == sceneName) return true;

        TryFindGameScene(sceneName, out SceneData data, out int levelIndex);
        BeginTransition(sceneName, data, levelIndex, false);
        return true;
    }

    private void BeginTransition(string sceneName, SceneData data, int levelIndex, bool alreadyLoaded)
    {
        isLoadingScene = true;
        targetSceneName = sceneName;
        int version = ++transitionVersion;
        StartCoroutine(Transition(sceneName, data, levelIndex, alreadyLoaded, version));
    }

    private IEnumerator Transition(string sceneName, SceneData data, int levelIndex, bool alreadyLoaded, int version)
    {
        // 单独运行场景时，等场景内各组件完成 Awake 和 Start。
        yield return null;
        if (!IsCurrentTransition(version)) yield break;

        if (!alreadyLoaded)
        {
            sceneOperation = StartSceneLoad(sceneName);
            if (sceneOperation == null)
            {
                FinishTransition(version);
                yield break;
            }

            sceneOperation.allowSceneActivation = false;
            StopCurrentGameForSceneExit();
            EndPanel.CancelForSceneExit();
        }

        if (data != null)
        {
            ApplyLoadingContent(data);
            yield return ShowLoadingPage();
            if (!IsCurrentTransition(version)) yield break;
            yield return RunLoadingProgress();
            if (!IsCurrentTransition(version)) yield break;
        }

        if (sceneOperation != null)
        {
            sceneOperation.allowSceneActivation = true;
            yield return sceneOperation;
            if (!IsCurrentTransition(version)) yield break;
            yield return null;
        }

        if (!IsCurrentTransition(version)) yield break;
        RecordGame(data, levelIndex);
        if (data != null) yield return DissolveLoadingPage();
        FinishTransition(version);
    }

    private AsyncOperation StartSceneLoad(string sceneName)
    {
        try
        {
            return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
            return null;
        }
    }

    private IEnumerator RunLoadingProgress()
    {
        loadingProgressTween = DOTween.Sequence().SetUpdate(true).SetEase(Ease.Linear).SetAutoKill(false).SetRecyclable(false).Pause();
        loadingProgressTween.Append(DOVirtual.Float(0f, 0.28f, LoadingProgressDuration * 0.23f, SetLoadingProgress).SetEase(Ease.OutQuad));
        loadingProgressTween.AppendInterval(LoadingProgressDuration * 0.13f);
        loadingProgressTween.Append(DOVirtual.Float(0.28f, 0.84f, LoadingProgressDuration * 0.29f, SetLoadingProgress).SetEase(Ease.InQuad));
        loadingProgressTween.Append(DOVirtual.Float(0.84f, 0.999f, LoadingProgressDuration * 0.35f, SetLoadingProgress).SetEase(Ease.OutCubic));
        loadingProgressTween.Play();
        yield return loadingProgressTween.WaitForCompletion();
        loadingProgressTween.Kill();
        loadingProgressTween = null;
        SetLoadingProgress(1f);
    }

    private void CacheHideObjectAlphas()
    {
        if (HideObj == null) return;
        foreach (CanvasGroup group in HideObj)
        {
            if (group == null || group == Myself || hideObjectAlphas.ContainsKey(group)) continue;
            hideObjectAlphas.Add(group, group.alpha);
        }
    }

    private IEnumerator ShowLoadingPage()
    {
        SetLoadingPageVisible(true);
        foreach (KeyValuePair<CanvasGroup, float> entry in hideObjectAlphas)
        {
            if (entry.Key != null) entry.Key.alpha = entry.Value;
        }

        if (BGHideEffct != null) BGHideEffct.ResetEffectFactor();
        if (Myself == null) yield break;
        if (FadeDuration <= 0f)
        {
            Myself.alpha = 1f;
            yield break;
        }

        fadeTween = Myself.DOFade(1f, FadeDuration).SetEase(Ease.InOutSine).SetUpdate(true);
        yield return fadeTween.WaitForCompletion();
        fadeTween = null;
    }

    private IEnumerator DissolveLoadingPage()
    {
        Sequence hideObjects = null;
        foreach (CanvasGroup group in hideObjectAlphas.Keys)
        {
            if (group == null || group.alpha <= 0f) continue;
            if (hideObjects == null) hideObjects = DOTween.Sequence().SetUpdate(true);
            hideObjects.Join(group.DOFade(0f, HideObjectsDuration).SetEase(Ease.InOutSine));
        }

        if (hideObjects != null)
        {
            fadeTween = hideObjects;
            yield return hideObjects.WaitForCompletion();
            fadeTween = null;
        }

        UITransitionEffect effect = BGHideEffct;
        if (effect == null || !effect.isActiveAndEnabled) yield break;

        bool dissolveFinished = false;
        effect.Hide(() => dissolveFinished = true);
        while (!dissolveFinished && effect != null && effect.isActiveAndEnabled) yield return null;
    }

    private void ApplyLoadingContent(SceneData data)
    {
        if (BackgroundImage != null) SetSprite(BackgroundImage, data.BG);
        if (LogoImage != null)
        {
            bool english = LanguageManger.Instance != null && LanguageManger.Instance.GetLanguageType() == LanguageType.英文;
            int logoIndex = english ? 1 : 0;
            Sprite[] logos = data.Logo;
            Sprite logo = logos != null && logoIndex < logos.Length ? logos[logoIndex] : null;
            if (logo == null && logos != null && logos.Length > 0) logo = logos[0];
            SetSprite(LogoImage, logo);
            if (logo != null) LogoImage.SetNativeSize();
        }

        if (LoadingPromptText != null)
        {
            string prompt = data.loadingPrompt ?? string.Empty;
            LoadingPromptText.text = LanguageManger.Instance != null && !string.IsNullOrEmpty(prompt) ? LanguageManger.Instance.GetValue(prompt) : prompt;
        }

        SetLoadingProgress(0f);
    }

    private static void SetSprite(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    private void SetLoadingProgress(float progress)
    {
        float value = Mathf.Clamp01(progress);
        if (LoadingTimerImage != null) LoadingTimerImage.fillAmount = value;
        if (LoadingNumber != null) LoadingNumber.text = Mathf.FloorToInt(value * 100f) + "%";
    }

    private void SetLoadingPageVisible(bool visible)
    {
        if (Myself == null) return;
        if (visible && !Myself.gameObject.activeSelf) Myself.gameObject.SetActive(true);
        Myself.alpha = 0f;
        Myself.interactable = false;
        Myself.blocksRaycasts = visible;
    }

    private bool IsCurrentTransition(int version)
    {
        return version == transitionVersion && !returningHome;
    }

    private void FinishTransition(int version)
    {
        if (!IsCurrentTransition(version)) return;
        SetLoadingPageVisible(false);
        sceneOperation = null;
        targetSceneName = null;
        isLoadingScene = false;
        List<BaseGameManager> managers = new List<BaseGameManager>(startupManagers);
        startupManagers.Clear();
        foreach (BaseGameManager manager in managers)
        {
            if (manager != null && manager.gameObject.scene == SceneManager.GetActiveScene()) manager.BeginReadyGoFromSceneControl();
        }
    }

    private bool TryFindGameScene(string sceneName, out SceneData data, out int levelIndex)
    {
        data = null;
        levelIndex = -1;
        if (string.IsNullOrWhiteSpace(sceneName) || sceneName == HomeSceneName || SceneDatas == null) return false;
        foreach (SceneData entry in SceneDatas)
        {
            if (entry == null || entry.SceneName == null) continue;
            int index = System.Array.IndexOf(entry.SceneName, sceneName);
            if (index < 0) continue;
            data = entry;
            levelIndex = index;
            return true;
        }
        return false;
    }

    private void RecordGame(SceneData data, int levelIndex)
    {
        CurrentGameData = data;
        CurrentLevelIndex = levelIndex;
    }

    private static void StopCurrentGameForSceneExit()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        foreach (BaseGameManager manager in FindObjectsOfType<BaseGameManager>())
        {
            if (manager.gameObject.scene == activeScene) manager.CancelForSceneExit();
        }
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        KillLoadingAnimations();
        if (sceneOperation != null && !sceneOperation.isDone) sceneOperation.allowSceneActivation = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        EventManager.RemoveListener(GenericEventEnumType.Message, nameof(UdpGameName), OnSceneNameReceived);
        EventManager.RemoveListener(GenericEventEnumType.Message, nameof(OrderData), OnOrderReceived);
        instance = null;
    }
}