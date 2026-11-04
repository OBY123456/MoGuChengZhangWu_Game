using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using MTFrame.MTEvent;
using TMPro;
using DG.Tweening;
using Lean.Pool;

public enum ResultType { 左边胜利 = 0, 右边胜利 = 1, 平局 = 2 }

/// <summary>五关共用：准备语音、ReadyGo、游戏/答题倒计时、双方计分、切题间隔和结算。</summary>
public abstract class BaseGameManager : MonoBehaviour
{
    [Header("音频")]
    public AudioSource BGM;
    public AudioSource PreparationVoiceSource;
    public AudioClip PreparationVoiceClip;
    public bool WaitForExternalPreparationVoice;

    [Header("时间")]
    [Min(1)] private float GameDuration = 60f;
    [Min(1)] public float QuestionDuration = 10f;
    [Min(0)] public float NextQuestionInterval = 1f;

    public Text GameTimerText;
    public TextMeshProUGUI leftScoreText;
    public TextMeshProUGUI rightScoreText;

    [Header("分数滚动动画")]
    [Min(0)] public float ScoreRollDuration = 0.45f;
    public Ease ScoreRollEase = Ease.OutCubic;

    [Tooltip("答题倒计时：开始为满格，时间结束为0")]
    public Image QuestionTimerImage;

    public bool AutoReadyGoOnDirectSceneStart = true;

    public bool IsGameRunning { get; private set; }
    public bool IsPreparing { get; private set; }
    protected int leftScore, rightScore;
    protected float gameTimer, questionTimer;
    protected bool questionTimerRunning, questionResolved;

    private bool startupOwnedBySceneControl, readyGoStarted;
    private Coroutine preparationRoutine, nextQuestionRoutine;
    private Action externalVoiceFinished;
    private int displayedLeftScore, displayedRightScore;
    private int leftScoreTweenTarget, rightScoreTweenTarget;
    private Tween leftScoreTween, rightScoreTween;
    private int pendingGameplayActions;
    private bool gameEndRequested;
    private static int nextAnswerEffectOrder;

    public ReadyGoPanel readyGoPanel;

    [Header("0为答对特效，1为答错特效")]
    public GameObject[] AnswerEff = new GameObject[2];

    [Header("世界空间答题特效")]
    public Camera AnswerEffectCamera;
    public int AnswerEffectSortingOrder = 1000;
    [Min(0.01f)] public float AnswerEffectLifetime = 1f;

    protected virtual void Awake()
    {
        StopBGM();
        EndPanel.Preload();
        questionTimer = QuestionDuration;
        ConfigureQuestionTimerImage();
        SetDisplayedLeftScore(leftScore);
        SetDisplayedRightScore(rightScore);
        EventManager.AddListener(GenericEventEnumType.Generic, "Again", AgainCallback);
        startupOwnedBySceneControl = SceneControl.TryRegisterGameManagerForStartup(this);
    }

    protected virtual void Start()
    {
        RefreshTimerUI();
        RefreshLocalizedScoreTexts();
        if (readyGoStarted) return;
        if (!startupOwnedBySceneControl && AutoReadyGoOnDirectSceneStart) StartPreparation();
    }

    private void AgainCallback(EventParamete data)
    {
        if (!IsGameRunning && !IsPreparing) StartPreparation();
    }

    public void BeginReadyGoFromSceneControl()
    {
        startupOwnedBySceneControl = false;
        StartPreparation();
    }

    /// <summary>先生成题目/棋盘，再播准备语音；准备阶段不计时、不接收游戏输入。</summary>
    public void StartPreparation()
    {
        if (IsGameRunning || IsPreparing || readyGoStarted) return;
        IsPreparing = true;
        StopBGM();
        if (nextQuestionRoutine != null) StopCoroutine(nextQuestionRoutine);
        nextQuestionRoutine = null;
        if (readyGoPanel != null) readyGoPanel.CancelCountdown();
        ResetRoundState();
        // 准备阶段先创建题目并启动动画，随后同步播放 NPC 语音。
        PrepareGameForReadyGo();
        if (preparationRoutine != null) StopCoroutine(preparationRoutine);
        preparationRoutine = StartCoroutine(PreparationFlow());
    }

    private IEnumerator PreparationFlow()
    {
        if (WaitForExternalPreparationVoice)
        {
            bool done = false;
            externalVoiceFinished = () => done = true;
            yield return new WaitUntil(() => done || !IsPreparing);
            externalVoiceFinished = null;
            if (!IsPreparing) yield break;
        }
        else if (PreparationVoiceSource != null && PreparationVoiceClip != null)
        {
            PreparationVoiceSource.Stop();
            PreparationVoiceSource.loop = false;
            PreparationVoiceSource.clip = PreparationVoiceClip;
            PreparationVoiceSource.Play();
            // 音频暂停时 isPlaying 也会是 false，至少等待语音本身的时长。
            yield return new WaitForSecondsRealtime(PreparationVoiceClip.length);
            yield return new WaitWhile(() => PreparationVoiceSource != null && PreparationVoiceSource.isPlaying && IsPreparing);
            if (!IsPreparing) yield break;
        }

        // 语音结束时通知子类；本关的首题动画已在语音期间启动。
        if (!IsPreparing) yield break;
        OnPreparationVoiceEnded();

        // 语音和首题动画都结束后，再留 1 秒才开始倒计时。
        yield return new WaitUntil(() => !IsPreparing || IsPreparationVisualReady());
        if (!IsPreparing) yield break;
        yield return new WaitForSecondsRealtime(1f);
        if (!IsPreparing) yield break;
        readyGoStarted = true;
        if (readyGoPanel != null && readyGoPanel.isActiveAndEnabled)
        {
            readyGoPanel.PlayCountdown(BeginActualGame);
            yield break;
        }

        BeginActualGame();
    }

    public void NotifyPreparationVoiceFinished() { externalVoiceFinished?.Invoke(); }

    protected virtual void PrepareGameForReadyGo() { }
    protected virtual void OnPreparationVoiceEnded() { }
    protected virtual bool IsPreparationVisualReady() { return true; }
    protected abstract void InitGame();
    protected virtual void CleanupGame() { }
    protected virtual void CreateNextQuestion() { }

    private void BeginActualGame()
    {
        if (IsGameRunning || !readyGoStarted) return;
        IsPreparing = false;
        readyGoStarted = false;
        preparationRoutine = null;
        ResetRoundState();
        IsGameRunning = true;
        InitGame();
        RefreshTimerUI();
        if (IsGameRunning && BGM != null) { BGM.Stop(); BGM.Play(); }
    }

    private void ResetRoundState()
    {
        KillScoreTweens();
        pendingGameplayActions = 0;
        gameEndRequested = false;
        gameTimer = 0;
        questionTimer = QuestionDuration;
        questionTimerRunning = false;
        questionResolved = false;
        leftScore = rightScore = 0;
        SetDisplayedLeftScore(leftScore);
        SetDisplayedRightScore(rightScore);
        RefreshTimerUI();
        OnBattleScoreChanged(leftScore, rightScore);
    }

    protected virtual void Update()
    {
        if (!IsGameRunning) return;
        if (gameEndRequested) { RefreshTimerUI(); return; }
        gameTimer = Mathf.Min(GameDuration, gameTimer + Time.deltaTime);
        if (gameTimer >= GameDuration)
        {
            GameEnd(DetermineResult());
            RefreshTimerUI();
            return;
        }
        if (questionTimerRunning && !questionResolved)
        {
            questionTimer -= Time.deltaTime;
            if (questionTimer <= 0)
            {
                questionTimer = 0;
                questionTimerRunning = false;
                questionResolved = true;
                OnQuestionTimeout();
            }
        }
        RefreshTimerUI();
    }

    protected void StartQuestionTimer()
    {
        questionTimer = QuestionDuration;
        questionTimerRunning = true;
        questionResolved = false;
        RefreshTimerUI();
    }

    protected void StopQuestionTimer() { questionTimerRunning = false; RefreshTimerUI(); }

    /// <summary>开始一个必须完成的操作，例如答案飞行及其判定。</summary>
    protected void BeginProtectedAction() { pendingGameplayActions++; }

    /// <summary>所有已开始的操作结束后，才允许真正结算整局。</summary>
    protected void EndProtectedAction()
    {
        pendingGameplayActions = Mathf.Max(0, pendingGameplayActions - 1);
        if (pendingGameplayActions == 0 && gameEndRequested && IsGameRunning)
        {
            gameEndRequested = false;
            GameEnd(DetermineResult());
        }
    }

    protected virtual void OnGameEndWaitingForActions() { }

    /// <summary>传入 UI 屏幕坐标，生成 Z=0 的世界空间 SpriteRenderer 特效。</summary>
    protected GameObject PlayAnswerEffect(bool correct, Vector2 screenPosition)
    {
        int index = correct ? 0 : 1;
        if (AnswerEff == null || index >= AnswerEff.Length || AnswerEff[index] == null) return null;
        Camera camera = AnswerEffectCamera != null ? AnswerEffectCamera : Camera.main;
        if (camera == null) { Debug.LogWarning("答题特效：没有找到带 MainCamera 标签的相机。", this); return null; }

        Ray ray = camera.ScreenPointToRay(screenPosition);
        Plane plane = new Plane(Vector3.forward, Vector3.zero);
        if (!plane.Raycast(ray, out float distance)) return null;
        Vector3 worldPosition = ray.GetPoint(distance);
        worldPosition.z = 0f;

        GameObject effect = LeanPool.Spawn(AnswerEff[index], worldPosition, Quaternion.identity, null);
        if (effect == null) return null;
        effect.transform.SetParent(null, true);
        effect.transform.position = worldPosition;

        int order = Mathf.Clamp(AnswerEffectSortingOrder, -20000, 10000) + (nextAnswerEffectOrder++ % 1000) * 16;
        SpriteRenderer[] renderers = effect.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++) renderers[i].sortingOrder = Mathf.Clamp(order + i, -32768, 32767);
        if (renderers.Length == 0) Debug.LogWarning("答题特效预制体没有 SpriteRenderer。", effect);

        PlaySprite player = effect.GetComponentInChildren<PlaySprite>(true);
        if (player != null) { player.Reset(); player.Play(); }
        LeanPool.Despawn(effect, Mathf.Max(0.01f, AnswerEffectLifetime));
        return effect;
    }

    protected void ScheduleNextQuestion(Action action)
    {
        if (gameEndRequested) return;
        if (nextQuestionRoutine != null) StopCoroutine(nextQuestionRoutine);
        nextQuestionRoutine = StartCoroutine(NextQuestionFlow(action));
    }

    private IEnumerator NextQuestionFlow(Action action)
    {
        StopQuestionTimer();
        yield return new WaitForSeconds(NextQuestionInterval);
        yield return new WaitUntil(() => pendingGameplayActions == 0 || !IsGameRunning);
        nextQuestionRoutine = null;
        if (IsGameRunning && !gameEndRequested) action?.Invoke();
    }

    protected virtual void OnQuestionTimeout() { ScheduleNextQuestion(CreateNextQuestion); }

    protected virtual void RefreshTimerUI()
    {
        if (QuestionTimerImage != null)
            QuestionTimerImage.fillAmount = Mathf.Clamp01(questionTimer / Mathf.Max(0.01f, QuestionDuration));

        if (GameTimerText != null)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0, GameDuration - gameTimer));
            GameTimerText.text = $"{s / 60:00}:{s % 60:00}";
        }
    }

    public virtual void AddLeftScore(int points)
    {
        if (!IsGameRunning) return;
        leftScore = (int)Math.Min(int.MaxValue, Math.Max(0L, (long)leftScore + points));
        RefreshLeftScoreDisplay(true);
        OnBattleScoreChanged(leftScore, rightScore);
    }

    public virtual void AddRightScore(int points)
    {
        if (!IsGameRunning) return;
        rightScore = (int)Math.Min(int.MaxValue, Math.Max(0L, (long)rightScore + points));
        RefreshRightScoreDisplay(true);
        OnBattleScoreChanged(leftScore, rightScore);
    }

    public void SetBattleScores(int left, int right)
    {
        leftScore = Mathf.Max(0, left);
        rightScore = Mathf.Max(0, right);
        RefreshLeftScoreDisplay(IsGameRunning);
        RefreshRightScoreDisplay(IsGameRunning);
        OnBattleScoreChanged(leftScore, rightScore);
    }

    private void ConfigureQuestionTimerImage()
    {
        if (QuestionTimerImage == null) return;
        QuestionTimerImage.type = Image.Type.Filled;
        QuestionTimerImage.fillMethod = Image.FillMethod.Horizontal;
        QuestionTimerImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        QuestionTimerImage.fillAmount = 1f;
    }

    private void SetDisplayedLeftScore(int value)
    {
        displayedLeftScore = value;
        if (leftScoreText != null) leftScoreText.text = FormatScore(value);
    }

    private void SetDisplayedRightScore(int value)
    {
        displayedRightScore = value;
        if (rightScoreText != null) rightScoreText.text = FormatScore(value);
    }

    private static string FormatScore(int value)
    {
        string unit = LanguageManger.Instance != null ? LanguageManger.Instance.GetValue("分") : "分";
        return value + " " + (string.IsNullOrWhiteSpace(unit) ? "分" : unit.Trim());
    }

    public void RefreshLocalizedScoreTexts()
    {
        SetDisplayedLeftScore(displayedLeftScore);
        SetDisplayedRightScore(displayedRightScore);
    }

    private void RefreshLeftScoreDisplay(bool animate)
    {
        if (animate && leftScoreTween != null && leftScoreTweenTarget == leftScore) return;
        if (leftScoreTween != null) leftScoreTween.Kill();
        leftScoreTween = null;
        leftScoreTweenTarget = leftScore;
        if (!animate || leftScoreText == null || ScoreRollDuration <= 0f || displayedLeftScore == leftScore)
        {
            SetDisplayedLeftScore(leftScore);
            return;
        }

        leftScoreTween = DOTween.To(() => displayedLeftScore, SetDisplayedLeftScore, leftScore, ScoreRollDuration).SetEase(ScoreRollEase).OnComplete(() => { SetDisplayedLeftScore(leftScore); leftScoreTween = null; });
        leftScoreTween.Play();
    }

    private void RefreshRightScoreDisplay(bool animate)
    {
        if (animate && rightScoreTween != null && rightScoreTweenTarget == rightScore) return;
        if (rightScoreTween != null) rightScoreTween.Kill();
        rightScoreTween = null;
        rightScoreTweenTarget = rightScore;
        if (!animate || rightScoreText == null || ScoreRollDuration <= 0f || displayedRightScore == rightScore)
        {
            SetDisplayedRightScore(rightScore);
            return;
        }

        rightScoreTween = DOTween.To(() => displayedRightScore, SetDisplayedRightScore, rightScore, ScoreRollDuration).SetEase(ScoreRollEase).OnComplete(() => { SetDisplayedRightScore(rightScore); rightScoreTween = null; });
        rightScoreTween.Play();
    }

    private void KillScoreTweens()
    {
        if (leftScoreTween != null) leftScoreTween.Kill();
        if (rightScoreTween != null) rightScoreTween.Kill();
        leftScoreTween = null;
        rightScoreTween = null;
    }

    protected virtual void OnBattleScoreChanged(int left, int right) { }
    public int GetLeftScore() => leftScore;
    public int GetRightScore() => rightScore;
    public float GetRemainingTime() => Mathf.Max(0, GameDuration - gameTimer);

    protected virtual ResultType DetermineResult()
    {
        if (leftScore > rightScore) return ResultType.左边胜利;
        if (rightScore > leftScore) return ResultType.右边胜利;
        return ResultType.平局;
    }

    public virtual void GameEnd(ResultType result)
    {
        if (!IsGameRunning) return;
        if (pendingGameplayActions > 0)
        {
            if (!gameEndRequested) OnGameEndWaitingForActions();
            gameEndRequested = true;
            StopQuestionTimer();
            return;
        }
        gameEndRequested = false;
        IsGameRunning = false;
        StopQuestionTimer();
        if (nextQuestionRoutine != null) StopCoroutine(nextQuestionRoutine);
        nextQuestionRoutine = null;
        CleanupGame();
        if (BGM != null) BGM.Stop();
        if (EndPanel.Instance != null) EndPanel.Instance.ShowResult(result, leftScore, rightScore);
    }

    public void GameEndBattle(int left, int right) { SetBattleScores(left, right); GameEnd(DetermineResult()); }
    public void ForceEndGame() { if (IsGameRunning) GameEnd(DetermineResult()); }

    /// <summary>安全停止背景音乐，供 Awake 和准备流程调用。</summary>
    private void StopBGM()
    {
        if (BGM != null)
            BGM.Stop();
    }

    public virtual void CancelForSceneExit()
    {
        IsGameRunning = IsPreparing = false;
        readyGoStarted = false;
        pendingGameplayActions = 0;
        gameEndRequested = false;
        KillScoreTweens();
        if (preparationRoutine != null) StopCoroutine(preparationRoutine);
        if (nextQuestionRoutine != null) StopCoroutine(nextQuestionRoutine);
        preparationRoutine = null;
        nextQuestionRoutine = null;
        externalVoiceFinished = null;
        if (readyGoPanel != null) readyGoPanel.CancelCountdown();
        CleanupGame();
        if (BGM != null) BGM.Stop();
        if (PreparationVoiceSource != null) PreparationVoiceSource.Stop();
    }

    protected virtual void OnDestroy()
    {
        CancelForSceneExit();
        SceneControl.UnregisterGameManagerForStartup(this);
        EventManager.RemoveListener(GenericEventEnumType.Generic, "Again", AgainCallback);
    }
}
