using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using Lean.Pool;

[System.Serializable]
public class JigsawQuestionData
{
    [Tooltip("中心挖空的题目图片")]
    public Sprite QuestionSprite;

    [Tooltip("四张碎片答案，固定使用下标 0、1、2、3")]
    public Sprite[] Answers = new Sprite[4];

    [Tooltip("正确答案下标，从 0 开始，例如第二张填 1")]
    public int Answer;
}

/// <summary>双方分别选碎片、旋转、安装。题目超时后统一锁定并切题。</summary>
public class PirateTreasureMapGameManager : BaseGameManager
{
    [Header("题目数据")]
    public JigsawQuestionData[] jigsawQuestionDatas;
    public Image QuestionImage;

    [Tooltip("0 左边，1 右边；位置尺寸由场景里的 Image 决定")]
    public Image[] AnswerImage = new Image[2];

    [Header("操作按钮：换件、旋转、安装")]
    public TouchUIButton[] LeftBtn = new TouchUIButton[3];
    public TouchUIButton[] RightBtn = new TouchUIButton[3];

    [Header("缩放回弹动画")]
    [Min(0.01f)] public float QuestionAppearDuration = 0.85f;
    [Min(0.01f)] public float AnswerAppearDuration = 0.7f;
    [Min(0.01f)] public float AnswerSwitchDuration = 0.45f;
    [Min(0.01f)] public float AnswerRotateDuration = 0.22f;
    [Range(0.1f, 1f)] public float AppearStartScale = 0.6f;
    [Min(1f)] public float BackOvershoot = 1.5f;
    [Range(1f, 1.3f)] public float BouncePeakScale = 1.12f;

    [Header("首次出现：答案与按钮由左到右交错弹出")]
    [Min(0.01f)] public float ButtonAppearDuration = 0.55f;
    [Min(0.01f)] public float WaveStaggerInterval = 0.13f;
    [Range(0f, 0.9f)] public float WaveStartRatio = 0.45f;

    [Header("安装答案：飞向题目中心")]
    [Min(0.1f)] public float InstallFlyDuration = 0.65f;
    [Range(1f, 1.5f)] public float InstallFlyPeakScale = 1.15f;

    // 固定 10 分，避免 Unity 场景继续读取旧字段中序列化的 1 分。
    private const int PointsPerCorrectAnswer = 10;

    private readonly int[] answerIndices = new int[2];
    private readonly int[] answerAngles = new int[2];
    private readonly bool[] sideSolved = new bool[2];
    private readonly bool[] sideBusy = new bool[2];
    private readonly Tween[] sideTweens = new Tween[2];
    private readonly Sequence[] installTweens = new Sequence[2];
    private readonly GameObject[] flyingAnswers = new GameObject[2];
    private readonly Vector3[] answerScales = new Vector3[2];
    private readonly Vector3[,] buttonScales = new Vector3[2, 3];
    private readonly List<EntranceItem> entranceItems = new List<EntranceItem>(8);

    private struct EntranceItem
    {
        public Transform Target;
        public Vector3 FinalScale;
        public Image Answer;
    }

    private int questionIndex = -1;
    private bool entranceReady, inputEnabled, buttonsBound;
    private bool scalesCached;
    private bool animateButtonsOnEntrance;
    private Vector3 questionScale = Vector3.one;
    private Sequence entranceSequence;
    private Coroutine entranceStartRoutine;

    protected override void Awake()
    {
        base.Awake();
        CacheImageScales();
        BindButtons();
        DisableAllButtons();
        SetIntroButtonsScaleZero();
    }

    protected override void Start()
    {
        base.Start();
    }

    protected override void PrepareGameForReadyGo()
    {
        inputEnabled = false;
        DisableAllButtons();
        CreatePuzzleQuestion();
    }

    protected override bool IsPreparationVisualReady()
    {
        return entranceReady;
    }

    protected override void InitGame()
    {
        if (questionIndex < 0)
        {
            Debug.LogError("藏宝图：没有可用题目，请检查题目和答案资源。", this);
            GameEnd(DetermineResult());
            return;
        }

        // 首题在语音播放时已经生成，不能在 GO 后重新随机或重复播动画。
        if (entranceReady) OpenQuestionInput();
    }

    protected override void CreateNextQuestion()
    {
        if (!IsGameRunning) return;
        CreatePuzzleQuestion();
        if (questionIndex < 0) GameEnd(DetermineResult());
    }

    private void CreatePuzzleQuestion()
    {
        KillPuzzleTweens();
        entranceReady = false;
        inputEnabled = false;
        animateButtonsOnEntrance = IsPreparing;
        sideSolved[0] = sideSolved[1] = false;
        sideBusy[0] = sideBusy[1] = false;
        DisableAllButtons();

        questionIndex = PickNextQuestionIndex();
        if (questionIndex < 0)
        {
            entranceReady = true;
            Debug.LogError("藏宝图：题目数组为空，或题目/四个答案未配置完整。", this);
            return;
        }

        JigsawQuestionData question = jigsawQuestionDatas[questionIndex];
        if (QuestionImage != null)
        {
            QuestionImage.sprite = question.QuestionSprite;
            // 准备阶段立即显示缩小的题目，语音期间就能看到动画。
            QuestionImage.enabled = true;
            QuestionImage.rectTransform.localScale = questionScale * AppearStartScale;
        }

        for (int side = 0; side < 2; side++)
        {
            answerIndices[side] = Random.Range(0, 4);
            answerAngles[side] = Random.Range(1, 4) * 90;
            Image answerImage = GetAnswerImage(side);
            if (answerImage == null) continue;
            answerImage.sprite = question.Answers[answerIndices[side]];
            SetAnswerRotation(side, answerAngles[side]);
            Transform answerRoot = GetAnswerRotationRoot(side);
            if (answerRoot != null) answerRoot.localScale = Vector3.zero;
            answerImage.enabled = false;
        }

        if (animateButtonsOnEntrance) SetIntroButtonsScaleZero();
        // 先生成好题目与答案，首题的下一帧便与 NPC 语音同时播放。
        StartEntranceAnimation();
    }

    private void StartEntranceAnimation()
    {
        if (entranceStartRoutine != null || entranceSequence != null) return;
        if (QuestionImage != null) QuestionImage.enabled = true;
        // 此处保持缩小状态可见，等下一帧再开始真正的缩放。
        entranceStartRoutine = StartCoroutine(PlayEntranceNextFrame());
    }

    private IEnumerator PlayEntranceNextFrame()
    {
        yield return null;
        entranceStartRoutine = null;
        if (!isActiveAndEnabled || questionIndex < 0) yield break;

        entranceSequence = DOTween.Sequence().SetUpdate(true);
        entranceSequence.Pause();
        float questionDuration = Mathf.Max(0.85f, QuestionAppearDuration);
        if (QuestionImage != null)
        {
            entranceSequence.Insert(0f, CreateBounceTween(QuestionImage.rectTransform, questionScale, questionDuration));
        }
        else entranceSequence.AppendInterval(questionDuration);

        // 答案和按钮使用实际屏幕 X 坐标排序，避免不同分辨率下顺序错乱。
        entranceItems.Clear();
        for (int side = 0; side < 2; side++)
        {
            Image answerImage = GetAnswerImage(side);
            Transform answerRoot = GetAnswerRotationRoot(side);
            if (answerImage != null && answerRoot != null) entranceItems.Add(new EntranceItem { Target = answerRoot, FinalScale = answerScales[side], Answer = answerImage });
        }
        if (animateButtonsOnEntrance)
        {
            for (int side = 0; side < 2; side++)
            {
                for (int action = 0; action < 3; action++)
                {
                    TouchUIButton button = GetButton(side, action);
                    if (button == null) continue;
                    RectTransform rect = button.transform as RectTransform;
                    if (rect != null) entranceItems.Add(new EntranceItem { Target = rect, FinalScale = buttonScales[side, action] });
                }
            }
        }

        entranceItems.Sort((a, b) => a.Target.position.x.CompareTo(b.Target.position.x));
        float answerDuration = Mathf.Max(0.7f, AnswerAppearDuration);
        float buttonDuration = Mathf.Max(0.45f, ButtonAppearDuration);
        float step = Mathf.Min(Mathf.Max(0.02f, WaveStaggerInterval), Mathf.Min(answerDuration, buttonDuration) * 0.65f);
        float waveStart = questionDuration * Mathf.Clamp01(WaveStartRatio);
        for (int i = 0; i < entranceItems.Count; i++)
        {
            EntranceItem item = entranceItems[i];
            float time = waveStart + i * step;
            float duration = item.Answer != null ? answerDuration : buttonDuration;
            if (item.Answer != null)
            {
                Image answer = item.Answer;
                entranceSequence.InsertCallback(time, () => { item.Target.localScale = item.FinalScale * AppearStartScale; answer.enabled = true; });
            }
            entranceSequence.Insert(time, CreateBounceTween(item.Target, item.FinalScale, duration));
        }
        entranceSequence.OnComplete(FinishEntrance);
        entranceSequence.Play();
    }

    private Sequence CreateBounceTween(Transform target, Vector3 finalScale, float duration)
    {
        Sequence bounce = DOTween.Sequence();
        Vector3 peakScale = finalScale * Mathf.Max(1.08f, BouncePeakScale);
        bounce.Append(target.DOScale(peakScale, duration * 0.72f).SetEase(Ease.InOutSine));
        bounce.Append(target.DOScale(finalScale, duration * 0.28f).SetEase(Ease.OutBack, BackOvershoot));
        return bounce;
    }

    private void SetIntroButtonsScaleZero()
    {
        for (int side = 0; side < 2; side++)
        {
            for (int action = 0; action < 3; action++)
            {
                TouchUIButton button = GetButton(side, action);
                if (button != null) button.transform.localScale = Vector3.zero;
            }
        }
    }

    private void RestoreButtonScales()
    {
        for (int side = 0; side < 2; side++)
        {
            for (int action = 0; action < 3; action++)
            {
                TouchUIButton button = GetButton(side, action);
                if (button != null) button.transform.localScale = buttonScales[side, action];
            }
        }
    }

    private void FinishEntrance()
    {
        entranceSequence = null;
        entranceReady = true;
        if (IsGameRunning) OpenQuestionInput();
    }

    private void OpenQuestionInput()
    {
        if (!IsGameRunning || !entranceReady) return;
        inputEnabled = true;
        StartQuestionTimer();
        RefreshAllButtons();
    }

    private int PickNextQuestionIndex()
    {
        if (jigsawQuestionDatas == null) return -1;
        int candidates = 0;
        for (int i = 0; i < jigsawQuestionDatas.Length; i++)
        {
            if (IsValidQuestion(i) && i != questionIndex) candidates++;
        }

        bool allowRepeat = candidates == 0;
        if (allowRepeat)
        {
            for (int i = 0; i < jigsawQuestionDatas.Length; i++)
            {
                if (IsValidQuestion(i)) candidates++;
            }
        }
        if (candidates == 0) return -1;

        int picked = Random.Range(0, candidates);
        for (int i = 0; i < jigsawQuestionDatas.Length; i++)
        {
            if (!IsValidQuestion(i)) continue;
            if (!allowRepeat && i == questionIndex) continue;
            if (picked == 0) return i;
            picked--;
        }
        return -1;
    }

    private bool IsValidQuestion(int index)
    {
        JigsawQuestionData data = jigsawQuestionDatas[index];
        if (data == null || data.QuestionSprite == null) return false;
        if (data.Answers == null || data.Answers.Length < 4) return false;
        if (data.Answer < 0 || data.Answer >= 4) return false;
        for (int i = 0; i < 4; i++)
        {
            if (data.Answers[i] == null) return false;
        }
        return true;
    }

    private static Vector3 GetEuler(int angle)
    {
        return new Vector3(0f, 0f, -angle);
    }

    private static bool IsCorrectRotation(int angle)
    {
        return (angle % 360 + 360) % 360 == 0;
    }

    private Image GetAnswerImage(int side)
    {
        if (AnswerImage == null || side >= AnswerImage.Length) return null;
        return AnswerImage[side];
    }

    private Transform GetAnswerRotationRoot(int side)
    {
        Image answerImage = GetAnswerImage(side);
        if (answerImage == null) return null;
        return answerImage.transform.parent;
    }

    private void SetAnswerRotation(int side, int angle)
    {
        Image answerImage = GetAnswerImage(side);
        if (answerImage == null) return;
        Transform root = GetAnswerRotationRoot(side);
        if (root == null)
        {
            Debug.LogError("藏宝图：答案 Image 缺少独立父物体，无法旋转。", this);
            return;
        }
        answerImage.rectTransform.localEulerAngles = Vector3.zero;
        root.localEulerAngles = GetEuler(angle);
    }

    private TouchUIButton GetButton(int side, int action)
    {
        TouchUIButton[] buttons = side == 0 ? LeftBtn : RightBtn;
        return buttons != null && action < buttons.Length ? buttons[action] : null;
    }

    private void ChangeAnswer(int side)
    {
        if (!CanOperate(side)) return;
        answerIndices[side] = (answerIndices[side] + 1) % 4;
        answerAngles[side] = Random.Range(1, 4) * 90;
        Image answerImage = GetAnswerImage(side);
        if (answerImage == null) return;

        Transform answerRoot = GetAnswerRotationRoot(side);
        if (answerRoot == null) return;
        if (sideTweens[side] != null) sideTweens[side].Kill();
        answerImage.sprite = jigsawQuestionDatas[questionIndex].Answers[answerIndices[side]];
        SetAnswerRotation(side, answerAngles[side]);
        answerRoot.localScale = answerScales[side] * AppearStartScale;
        sideBusy[side] = true;
        RefreshSideButtons(side);
        sideTweens[side] = CreateBounceTween(answerRoot, answerScales[side], Mathf.Max(0.45f, AnswerSwitchDuration)).SetUpdate(true).OnComplete(() => FinishSideTween(side));
        sideTweens[side].Play();
    }

    private void RotateAnswer(int side)
    {
        if (!CanOperate(side)) return;
        Transform rotationRoot = GetAnswerRotationRoot(side);
        if (rotationRoot == null) return;

        int previous = answerAngles[side];
        answerAngles[side] = (previous + 90) % 360;
        sideBusy[side] = true;
        RefreshSideButtons(side);
        if (sideTweens[side] != null) sideTweens[side].Kill();
        // 只旋转答案的父物体，Image 自身保持零角度。
        // FastBeyond360 保证始终只沿相同方向旋转 90°。
        sideTweens[side] = rotationRoot.DOLocalRotate(GetEuler(previous + 90), AnswerRotateDuration, RotateMode.FastBeyond360).SetEase(Ease.OutCubic).OnComplete(() => FinishSideTween(side));
        sideTweens[side].Play();
    }

    private void FinishSideTween(int side)
    {
        sideTweens[side] = null;
        sideBusy[side] = false;
        SetAnswerRotation(side, answerAngles[side]);
        RefreshSideButtons(side);
    }

    private void InstallAnswer(int side)
    {
        if (!CanOperate(side)) return;
        int submittedIndex = answerIndices[side];
        int submittedAngle = answerAngles[side];
        int submittedQuestion = questionIndex;
        sideBusy[side] = true;
        RefreshSideButtons(side);
        BeginProtectedAction();

        float duration = Mathf.Max(0.1f, InstallFlyDuration);
        Sequence flight = DOTween.Sequence().SetUpdate(true);
        flight.Pause();
        Vector2 effectScreen = GetUiScreenPosition(QuestionImage);
        if (TryCreateFlyingAnswer(side, out RectTransform flyRect, out Vector2 destination, out effectScreen))
        {
            Vector3 startingScale = flyRect.localScale;
            float enlargeTime = duration * 0.32f;
            flight.Insert(0f, flyRect.DOAnchorPos(destination, duration).SetEase(Ease.InOutSine));
            flight.Insert(0f, flyRect.DOScale(startingScale * Mathf.Max(1.02f, InstallFlyPeakScale), enlargeTime).SetEase(Ease.OutCubic));
            flight.Insert(enlargeTime, flyRect.DOScale(Vector3.zero, duration - enlargeTime).SetEase(Ease.InBack));
        }
        else
        {
            Debug.LogWarning("藏宝图：创建飞行答案失败，请检查脚本父物体是否为 RectTransform。", this);
            flight.AppendInterval(duration);
        }
        Vector2 finalScreen = effectScreen;
        flight.OnComplete(() => FinishInstallFlight(side, submittedIndex, submittedAngle, submittedQuestion, finalScreen));
        installTweens[side] = flight;
        flight.Play();
    }

    private bool TryCreateFlyingAnswer(int side, out RectTransform flyRect, out Vector2 destination, out Vector2 effectScreen)
    {
        flyRect = null;
        destination = Vector2.zero;
        effectScreen = GetUiScreenPosition(QuestionImage);
        Image source = GetAnswerImage(side);
        if (source == null || QuestionImage == null) return false;

        Transform parent = transform.parent != null ? transform.parent : transform;
        RectTransform parentRect = parent as RectTransform;
        if (parentRect == null) return false;

        Vector2 sourceScreen = GetUiScreenPosition(source);
        Image clone = LeanPool.Spawn(source, Vector3.zero, Quaternion.identity, parent);
        if (clone == null) return false;

        RectTransform rect = clone.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = source.rectTransform.rect.size;
        clone.sprite = source.sprite;
        clone.color = source.color;
        clone.raycastTarget = false;
        clone.enabled = true;
        Graphic[] graphics = clone.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++) graphics[i].raycastTarget = false;

        Camera camera = GetUiCamera(clone);
        bool foundStart = RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, sourceScreen, camera, out Vector2 startLocal);
        bool foundTarget = RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, effectScreen, camera, out Vector2 targetLocal);
        if (!foundStart || !foundTarget)
        {
            LeanPool.Despawn(clone.gameObject);
            return false;
        }

        Vector3 sourceScale = source.rectTransform.lossyScale;
        Vector3 parentScale = parentRect.lossyScale;
        rect.localScale = new Vector3(SafeScaleRatio(sourceScale.x, parentScale.x), SafeScaleRatio(sourceScale.y, parentScale.y), 1f);
        // 保留源答案包含父物体旋转后的世界朝向。
        rect.rotation = source.rectTransform.rotation;
        rect.anchoredPosition = startLocal - parentRect.rect.center;
        rect.SetAsLastSibling();
        destination = targetLocal - parentRect.rect.center;
        flyingAnswers[side] = clone.gameObject;
        flyRect = rect;
        return true;
    }

    private static float SafeScaleRatio(float value, float divisor)
    {
        return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
    }

    private static Camera GetUiCamera(Image image)
    {
        Canvas canvas = image != null ? image.canvas : null;
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    private static Vector2 GetUiScreenPosition(Image image)
    {
        if (image == null) return Vector2.zero;
        RectTransform rect = image.rectTransform;
        return RectTransformUtility.WorldToScreenPoint(GetUiCamera(image), rect.TransformPoint(rect.rect.center));
    }

    private void FinishInstallFlight(int side, int submittedIndex, int submittedAngle, int submittedQuestion, Vector2 effectScreen)
    {
        installTweens[side] = null;
        GameObject flying = flyingAnswers[side];
        flyingAnswers[side] = null;
        if (flying != null)
        {
            Image cloneImage = flying.GetComponent<Image>();
            if (cloneImage != null) effectScreen = GetUiScreenPosition(cloneImage);
            LeanPool.Despawn(flying);
        }

        // 飞行动画完成、UI 回收后才判断正误，过期的题目绝不重复计分。
        bool validQuestion = IsGameRunning && submittedQuestion == questionIndex && submittedQuestion >= 0 && jigsawQuestionDatas != null && submittedQuestion < jigsawQuestionDatas.Length && IsValidQuestion(submittedQuestion);
        bool correct = validQuestion && submittedIndex == jigsawQuestionDatas[submittedQuestion].Answer && IsCorrectRotation(submittedAngle);
        if (validQuestion) PlayAnswerEffect(correct, effectScreen);
        if (correct)
        {
            sideSolved[side] = true;
            if (side == 0) AddLeftScore(PointsPerCorrectAnswer);
            else AddRightScore(PointsPerCorrectAnswer);
        }

        sideBusy[side] = false;
        RefreshSideButtons(side);
        if (sideSolved[0] && sideSolved[1] && !questionResolved)
        {
            questionResolved = true;
            inputEnabled = false;
            DisableAllButtons();
            SchedulePuzzleQuestion();
        }
        EndProtectedAction();
    }

    private bool CanOperate(int side)
    {
        if (!IsGameRunning || !inputEnabled) return false;
        if (questionResolved || !questionTimerRunning) return false;
        if (side < 0 || side > 1) return false;
        return !sideSolved[side] && !sideBusy[side];
    }

    protected override void OnQuestionTimeout()
    {
        // 先锁定答题，但已经飞出去的答案必须飞完才能判定及切题。
        inputEnabled = false;
        for (int side = 0; side < 2; side++)
        {
            if (sideTweens[side] != null) sideTweens[side].Kill();
            sideTweens[side] = null;
            if (installTweens[side] == null) sideBusy[side] = false;
        }
        RestoreAnswerScales();
        DisableAllButtons();
        SchedulePuzzleQuestion();
    }

    private void SchedulePuzzleQuestion()
    {
        // 切题协程会先停止计时并刷新 UI，再等待切题间隔。
        questionTimer = Mathf.Max(0.01f, QuestionDuration);
        ScheduleNextQuestion(CreateNextQuestion);
    }

    protected override void OnGameEndWaitingForActions()
    {
        inputEnabled = false;
        DisableAllButtons();
    }

    private void RefreshSideButtons(int side)
    {
        bool enable = CanOperate(side);
        for (int action = 0; action < 3; action++)
        {
            TouchUIButton button = GetButton(side, action);
            if (button != null) button.interactable = enable;
        }
    }

    private void RefreshAllButtons()
    {
        RefreshSideButtons(0);
        RefreshSideButtons(1);
    }

    private void DisableAllButtons()
    {
        for (int side = 0; side < 2; side++)
        {
            for (int action = 0; action < 3; action++)
            {
                TouchUIButton button = GetButton(side, action);
                if (button != null) button.interactable = false;
            }
        }
    }

    private void CacheImageScales()
    {
        if (scalesCached) return;
        if (QuestionImage != null) questionScale = QuestionImage.rectTransform.localScale;
        for (int side = 0; side < 2; side++)
        {
            Transform answerRoot = GetAnswerRotationRoot(side);
            answerScales[side] = answerRoot != null ? answerRoot.localScale : Vector3.one;
            for (int action = 0; action < 3; action++)
            {
                TouchUIButton button = GetButton(side, action);
                Vector3 original = button != null ? button.transform.localScale : Vector3.one;
                buttonScales[side, action] = original == Vector3.zero ? Vector3.one : original;
            }
        }
        scalesCached = true;
    }

    private void RestoreAnswerScales()
    {
        for (int side = 0; side < 2; side++)
        {
            Transform answerRoot = GetAnswerRotationRoot(side);
            if (answerRoot != null) answerRoot.localScale = answerScales[side];
        }
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        AddButtonListener(0, 0, OnLeftChange);
        AddButtonListener(0, 1, OnLeftRotate);
        AddButtonListener(0, 2, OnLeftInstall);
        AddButtonListener(1, 0, OnRightChange);
        AddButtonListener(1, 1, OnRightRotate);
        AddButtonListener(1, 2, OnRightInstall);
        buttonsBound = true;
    }

    private void AddButtonListener(int side, int action, UnityEngine.Events.UnityAction callback)
    {
        TouchUIButton button = GetButton(side, action);
        if (button != null) button.AddListener(callback);
    }

    private void RemoveButtonListener(int side, int action, UnityEngine.Events.UnityAction callback)
    {
        TouchUIButton button = GetButton(side, action);
        if (button != null) button.RemoveListener(callback);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        RemoveButtonListener(0, 0, OnLeftChange);
        RemoveButtonListener(0, 1, OnLeftRotate);
        RemoveButtonListener(0, 2, OnLeftInstall);
        RemoveButtonListener(1, 0, OnRightChange);
        RemoveButtonListener(1, 1, OnRightRotate);
        RemoveButtonListener(1, 2, OnRightInstall);
        buttonsBound = false;
    }

    private void OnLeftChange() { ChangeAnswer(0); }
    private void OnLeftRotate() { RotateAnswer(0); }
    private void OnLeftInstall() { InstallAnswer(0); }
    private void OnRightChange() { ChangeAnswer(1); }
    private void OnRightRotate() { RotateAnswer(1); }
    private void OnRightInstall() { InstallAnswer(1); }

    private void KillPuzzleTweens()
    {
        if (entranceStartRoutine != null) StopCoroutine(entranceStartRoutine);
        entranceStartRoutine = null;
        if (entranceSequence != null) entranceSequence.Kill();
        entranceSequence = null;
        for (int side = 0; side < 2; side++)
        {
            if (sideTweens[side] != null) sideTweens[side].Kill();
            sideTweens[side] = null;
        }
        RestoreAnswerScales();
    }

    protected override void CleanupGame()
    {
        inputEnabled = false;
        KillPuzzleTweens();
        CancelInstallFlights();
        RestoreButtonScales();
        DisableAllButtons();
    }

    private void CancelInstallFlights()
    {
        for (int side = 0; side < 2; side++)
        {
            if (installTweens[side] != null) installTweens[side].Kill();
            installTweens[side] = null;
            if (flyingAnswers[side] != null) LeanPool.Despawn(flyingAnswers[side]);
            flyingAnswers[side] = null;
            sideBusy[side] = false;
        }
    }

    protected override void OnDestroy()
    {
        UnbindButtons();
        base.OnDestroy();
    }
}
