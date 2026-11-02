using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneControl : MonoBehaviour
{
    private static SceneControl instance;

    public static SceneControl Instance
    {
        get
        {
            if (instance != null) return instance;
            GameObject obj = new GameObject("SceneControl");
            instance = obj.AddComponent<SceneControl>();
            DontDestroyOnLoad(obj);
            return instance;
        }
    }

    [Header("首页场景名称")]
    public string HomeSceneName = "选择页";

    private bool isLoadingScene = false;

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
        if (!Input.GetKeyDown(KeyCode.F5)) return;
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
