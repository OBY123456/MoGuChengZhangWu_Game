using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;

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

    [Tooltip("游戏开始前的故事语音")]
    public AudioClip[] audioClips = new AudioClip[5]; 

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
            GameObject prefab = Resources.Load<GameObject>("加载页面");
            if (prefab == null)
            {
                Debug.LogError("Resources 下没有找到【加载页面】预制体。");
                return null;
            }

            GameObject go = Instantiate(prefab);
            go.name = "加载页面";
            if (!go.activeSelf) go.SetActive(true);
            instance = go.GetComponent<SceneControl>();
            if (instance == null) Debug.LogError("加载页面预制体缺少SceneControl组件。", go);
            return instance;
        }
    }

    [Header("首页场景名称")]
    public string HomeSceneName = "选择页";

    private bool isLoadingScene = false;

    public List<SceneData> SceneDatas = new List<SceneData>();

    [Tooltip("加载进度条，使用FillAount从0到1,根据当前游戏的语音长度去控制进度条进度，语音结束，进度条就刚刚好满，进度条满")]
    public Image LoadingTimerImage;

    [Tooltip("加载进度，格式为75%,整数")]
    public Text LoadingNumber;

    public Text LoadingPromptText;

    public Image LogoImage;

    [Tooltip("播放游戏开始前语音")]
    public AudioSource audioSource;

    public bool IsLoading
    {
        get { return isLoadingScene; }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (isLoadingScene || IsHomeScene()) return;
        Back();
    }

    public static bool TryRegisterGameManagerForStartup(BaseGameManager gameManager)
    {
        return false;
    }

    public static void UnregisterGameManagerForStartup(BaseGameManager gameManager)
    {
    }

    public void LoadScene(string sceneName)
    {
        LoadSceneSync(sceneName);
    }

    public void LoadGameScene(string sceneName)
    {
        LoadSceneSync(sceneName);
    }

    public void Back()
    {
        if (isLoadingScene) return;
        StopCurrentGameForSceneExit();
        EndPanel.CancelForSceneExit();
        LoadSceneSync(HomeSceneName);
    }

    private void LoadSceneSync(string sceneName)
    {
        if (isLoadingScene) return;
        if (string.IsNullOrWhiteSpace(sceneName)) return;
        isLoadingScene = true;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        isLoadingScene = false;
    }

    private void StopCurrentGameForSceneExit()
    {
        BaseGameManager[] managers = FindObjectsOfType<BaseGameManager>();

        for (int i = 0; i < managers.Length; i++)
        {
            BaseGameManager manager = managers[i];
            if (manager != null) manager.CancelForSceneExit();
        }
    }

    private bool IsHomeScene()
    {
        return SceneManager.GetActiveScene().name == HomeSceneName;
    }
}
