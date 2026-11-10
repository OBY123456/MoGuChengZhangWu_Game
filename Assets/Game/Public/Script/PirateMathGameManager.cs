using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>第1关：共享算式，四种运算每轮各出一次，答错可重试，答对得10分；双方答对或超时后切题。</summary>
public class PirateMathGameManager : BaseGameManager
{
    public Text QuestionText;
    public TouchUIButton[] LeftAnswerButtons = new TouchUIButton[3];
    public TouchUIButton[] RightAnswerButtons = new TouchUIButton[3];
    private Text[] LeftAnswerTexts = new Text[3];
    private Text[] RightAnswerTexts = new Text[3];

    [Header("题目打字机与按钮同步出现")]
    [Tooltip("完整算式的打字时长，与第一枚答案按钮的出现动画同时开始")]
    [Min(0.01f)] public float TypewriterDuration = 0.65f;
    [Min(0.01f)] public float ButtonAppearDuration = 0.55f;
    [Min(0f)] public float WaveStaggerInterval = 0.13f;
    [Range(1f, 1.3f)] public float BouncePeakScale = 1.12f;
    [Min(1f)] public float BackOvershoot = 1.5f;

    [Header("选择答案反馈")]
    [Min(0.01f)] public float AnswerFeedbackDuration = 0.25f;
    [Range(1f, 1.3f)] public float AnswerFeedbackScale = 1.08f;

    private enum Operation { Addition, Subtraction, Multiplication, Division }

    private struct MathQuestion
    {
        public string Text;
        public int Answer;
    }

    private struct EntranceItem
    {
        public RectTransform Target;
        public Vector3 Scale;
        public float ScreenX;
        public int Order;
    }

    private const int PointsPerCorrectAnswer = 10;
    private readonly int[] leftAnswers = new int[3];
    private readonly int[] rightAnswers = new int[3];
    private readonly Vector3[,] buttonScales = new Vector3[2, 3];
    private readonly TouchUIButton[,] boundButtons = new TouchUIButton[2, 3];
    private readonly UnityAction[,] buttonListeners = new UnityAction[2, 3];
    private readonly Tween[] feedbackTweens = new Tween[2];
    private readonly bool[] protectedAnswers = new bool[2];
    private readonly List<MathQuestion> questionPool = new List<MathQuestion>(64);
    private readonly List<Operation> operations = new List<Operation>(4);
    private readonly List<EntranceItem> entranceItems = new List<EntranceItem>(6);
    private readonly List<int> distractors = new List<int>(8);
    private readonly List<int> availableAnswers = new List<int>(32);
    private readonly List<int> matchingQuestions = new List<int>(64);
    private readonly Queue<int> recentAnswers = new Queue<int>(3);
    private Sequence entranceSequence;
    private Coroutine answerWaitRoutine;
    private int correctAnswer, revision;
    private bool leftAnswered, rightAnswered;
    private bool buttonsBound, entranceReady, prepared, inputEnabled, changing, ending;
    private string fullQuestion = "";
    private int previousWrongFirst = -1, previousWrongSecond = -1;

    protected override void Awake()
    {
        base.Awake();
        CacheButtonScales();
        BindButtons();
        DisableAllButtons();
        HideAnswerButtons();
        if (QuestionText != null) QuestionText.text = "";
    }

    protected override void PrepareGameForReadyGo()
    {
        ending = false;
        operations.Clear();
        CreateQuestion();
    }

    protected override bool IsPreparationVisualReady()
    {
        return entranceReady;
    }

    protected override void InitGame()
    {
        if (!prepared) { GameEnd(DetermineResult()); return; }
        if (entranceReady) OpenQuestionInput();
    }

    protected override void CreateNextQuestion()
    {
        if (!IsGameRunning || ending) return;
        CreateQuestion();
        if (!prepared) GameEnd(DetermineResult());
    }

    private void CreateQuestion()
    {
        revision++;
        KillMathTweens();
        DisableAllButtons();
        inputEnabled = false;
        entranceReady = false;
        prepared = false;
        changing = false;
        leftAnswered = rightAnswered = false;
        protectedAnswers[0] = protectedAnswers[1] = false;
        ResetQuestionDisplay();
        if (!ValidateSetup() || !BuildQuestionPool())
        {
            entranceReady = true;
            RestoreButtonScales();
            if (QuestionText != null) QuestionText.text = "";
            return;
        }

        MathQuestion question = SelectQuestion();
        fullQuestion = question.Text;
        correctAnswer = question.Answer;
        recentAnswers.Enqueue(correctAnswer);
        if (recentAnswers.Count > 3) recentAnswers.Dequeue();
        CreateAnswerOptions();
        prepared = true;
        QuestionText.text = "";
        HideAnswerButtons();
        PlayEntranceAnimation();
    }

    private void ResetQuestionDisplay()
    {
        questionTimerRunning = false;
        questionResolved = false;
        questionTimer = Mathf.Max(0.01f, QuestionDuration);
        RefreshTimerUI();
        if (QuestionTimerImage != null) QuestionTimerImage.fillAmount = 1f;
    }

    private bool BuildQuestionPool()
    {
        questionPool.Clear();
        Operation operation = PickNextOperation();
        int hidden = Random.Range(0, 3);
        for (int first = 1; first < 9; first++)
        {
            for (int second = 1; second < 9; second++)
            {
                if (!TryCalculate(operation, first, second, out int result)) continue;
                // 结果显示在题目里时同样小于9；结果隐藏时，答案允许大于9。
                if (hidden != 2 && result >= 9) continue;
                string left = hidden == 0 ? "?" : FormatNumber(first);
                string right = hidden == 1 ? "?" : FormatNumber(second);
                string answer = hidden == 2 ? "?" : FormatNumber(result);
                int value = hidden == 0 ? first : hidden == 1 ? second : result;
                questionPool.Add(new MathQuestion { Text = string.Join(" ", left, GetSymbol(operation), right, "=", answer), Answer = value });
            }
        }
        return questionPool.Count > 0 || Invalid("当前运算规则没有可用题目。");
    }

    private Operation PickNextOperation()
    {
        // 每轮仅从尚未使用的运算中随机抽取，四种全部用完才开始新一轮。
        if (operations.Count == 0)
        {
            operations.Add(Operation.Addition);
            operations.Add(Operation.Subtraction);
            operations.Add(Operation.Multiplication);
            operations.Add(Operation.Division);
        }
        int index = Random.Range(0, operations.Count);
        Operation operation = operations[index];
        operations.RemoveAt(index);
        return operation;
    }

    private MathQuestion SelectQuestion()
    {
        // 所有运算共用：先等概率抽答案，再抽算式，避免小答案因算式多而反复出现。
        availableAnswers.Clear();
        for (int i = 0; i < questionPool.Count; i++)
        {
            int value = questionPool[i].Answer;
            if (recentAnswers.Contains(value) || availableAnswers.Contains(value)) continue;
            availableAnswers.Add(value);
        }
        // 当前四种运算的每类题库都有足够答案；此处兼容以后缩小题库的情况。
        if (availableAnswers.Count == 0)
        {
            for (int i = 0; i < questionPool.Count; i++)
            {
                int value = questionPool[i].Answer;
                if (value == correctAnswer || availableAnswers.Contains(value)) continue;
                availableAnswers.Add(value);
            }
        }
        if (availableAnswers.Count == 0) return questionPool[Random.Range(0, questionPool.Count)];
        int selectedAnswer = availableAnswers[Random.Range(0, availableAnswers.Count)];
        matchingQuestions.Clear();
        for (int i = 0; i < questionPool.Count; i++)
        {
            if (questionPool[i].Answer == selectedAnswer) matchingQuestions.Add(i);
        }
        return questionPool[matchingQuestions[Random.Range(0, matchingQuestions.Count)]];
    }

    private static bool TryCalculate(Operation operation, int first, int second, out int result)
    {
        result = 0;
        switch (operation)
        {
            case Operation.Addition: result = first + second; return true;
            case Operation.Subtraction: result = first - second; return result >= 0;
            case Operation.Multiplication: result = first * second; return true;
            case Operation.Division:
                if (second == 0 || first % second != 0) return false;
                result = first / second;
                return true;
            default: return false;
        }
    }

    private static string GetSymbol(Operation operation)
    {
        switch (operation)
        {
            case Operation.Addition: return "+";
            case Operation.Subtraction: return "-";
            case Operation.Multiplication: return "×";
            case Operation.Division: return "÷";
            default: return "+";
        }
    }

    private static string FormatNumber(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private void CreateAnswerOptions()
    {
        distractors.Clear();
        for (int value = Mathf.Max(0, correctAnswer - 5); value <= correctAnswer + 5; value++)
        {
            if (value == correctAnswer || value == previousWrongFirst || value == previousWrongSecond) continue;
            distractors.Add(value);
        }
        int firstWrong = Random.Range(0, distractors.Count);
        leftAnswers[0] = correctAnswer;
        leftAnswers[1] = distractors[firstWrong];
        distractors.RemoveAt(firstWrong);
        leftAnswers[2] = distractors[Random.Range(0, distractors.Count)];
        previousWrongFirst = leftAnswers[1];
        previousWrongSecond = leftAnswers[2];
        for (int i = 0; i < 3; i++) rightAnswers[i] = leftAnswers[i];
        ShuffleAnswers(leftAnswers);
        ShuffleAnswers(rightAnswers);
        if (leftAnswers[0] == rightAnswers[0] && leftAnswers[1] == rightAnswers[1])
        {
            int saved = rightAnswers[0];
            rightAnswers[0] = rightAnswers[1];
            rightAnswers[1] = saved;
        }
        for (int i = 0; i < 3; i++)
        {
            LeftAnswerTexts[i].text = FormatNumber(leftAnswers[i]);
            RightAnswerTexts[i].text = FormatNumber(rightAnswers[i]);
        }
    }

    private static void ShuffleAnswers(int[] answers)
    {
        for (int i = answers.Length - 1; i > 0; i--)
        {
            int index = Random.Range(0, i + 1);
            int saved = answers[i];
            answers[i] = answers[index];
            answers[index] = saved;
        }
    }

    private void PlayEntranceAnimation()
    {
        RestoreButtonScales();
        Canvas.ForceUpdateCanvases();
        entranceItems.Clear();
        for (int side = 0; side < 2; side++)
        {
            for (int i = 0; i < 3; i++)
            {
                RectTransform rect = GetButton(side, i).transform as RectTransform;
                entranceItems.Add(new EntranceItem { Target = rect, Scale = buttonScales[side, i], ScreenX = GetScreenCenter(rect).x, Order = side * 3 + i });
            }
        }
        entranceItems.Sort((a, b) => a.ScreenX != b.ScreenX ? a.ScreenX.CompareTo(b.ScreenX) : a.Order.CompareTo(b.Order));
        HideAnswerButtons();
        int id = revision;
        entranceSequence = DOTween.Sequence().SetAutoKill(true).SetRecyclable(false).SetUpdate(true).Pause();
        entranceSequence.Insert(0f, QuestionText.DOText(fullQuestion, Mathf.Max(0.01f, TypewriterDuration), false).SetEase(Ease.Linear));
        float duration = Mathf.Max(0.01f, ButtonAppearDuration);
        float interval = Mathf.Clamp(WaveStaggerInterval, 0f, duration * 0.65f);
        for (int i = 0; i < entranceItems.Count; i++) entranceSequence.Insert(i * interval, CreateBounceTween(entranceItems[i].Target, entranceItems[i].Scale, duration));
        entranceSequence.OnComplete(() => FinishEntrance(id));
        entranceSequence.Play();
    }

    private Sequence CreateBounceTween(RectTransform target, Vector3 finalScale, float duration)
    {
        Sequence bounce = DOTween.Sequence().SetAutoKill(true).SetRecyclable(false);
        bounce.Append(target.DOScale(finalScale * Mathf.Max(1f, BouncePeakScale), duration * 0.72f).SetEase(Ease.InOutSine));
        bounce.Append(target.DOScale(finalScale, duration * 0.28f).SetEase(Ease.OutBack, BackOvershoot));
        return bounce;
    }

    private void FinishEntrance(int id)
    {
        if (id != revision || ending) return;
        entranceSequence = null;
        entranceReady = true;
        if (IsGameRunning) OpenQuestionInput();
    }

    private void OpenQuestionInput()
    {
        if (!IsGameRunning || ending || !prepared || !entranceReady || changing) return;
        inputEnabled = true;
        StartQuestionTimer();
        RefreshAllButtons();
    }

    private void Choose(bool left, int index)
    {
        if (!IsGameRunning || ending || !inputEnabled || !entranceReady || questionResolved || !questionTimerRunning || GetRemainingTime() <= 0f) return;
        if (index < 0 || index >= 3 || (left ? leftAnswered : rightAnswered)) return;
        int side = left ? 0 : 1;
        if (protectedAnswers[side]) return;
        bool correct = (left ? leftAnswers[index] : rightAnswers[index]) == correctAnswer;
        BeginProtectedAction();
        protectedAnswers[side] = true;
        if (correct)
        {
            if (left) leftAnswered = true;
            else rightAnswered = true;
            if (left) AddLeftScore(PointsPerCorrectAnswer);
            else AddRightScore(PointsPerCorrectAnswer);
        }
        TouchUIButton button = GetButton(side, index);
        PlayAnswerEffect(correct, GetScreenCenter(button.transform as RectTransform));
        PlayAnswerFeedback(side, index);
        RefreshAllButtons();
        if (answerWaitRoutine == null) answerWaitRoutine = StartCoroutine(WaitForAnswers());
        if (leftAnswered && rightAnswered) ResolveQuestion();
    }

    private void PlayAnswerFeedback(int side, int index)
    {
        Transform target = GetButton(side, index).transform;
        float duration = Mathf.Max(0.01f, AnswerFeedbackDuration);
        int id = revision;
        Sequence feedback = DOTween.Sequence().SetAutoKill(true).SetRecyclable(false).Pause();
        feedback.Append(target.DOScale(buttonScales[side, index] * Mathf.Max(1f, AnswerFeedbackScale), duration * 0.4f).SetEase(Ease.OutSine));
        feedback.Append(target.DOScale(buttonScales[side, index], duration * 0.6f).SetEase(Ease.OutBack));
        feedback.OnComplete(() => { if (id == revision) feedbackTweens[side] = null; });
        feedbackTweens[side] = feedback;
        feedback.Play();
    }

    private IEnumerator WaitForAnswers()
    {
        while (IsGameRunning && (protectedAnswers[0] || protectedAnswers[1]))
        {
            yield return null;
            if (IsScoreRolling) continue;
            for (int side = 0; side < 2; side++)
            {
                if (!protectedAnswers[side] || (feedbackTweens[side] != null && feedbackTweens[side].IsActive())) continue;
                protectedAnswers[side] = false;
                EndProtectedAction();
                if (!IsGameRunning) break;
            }
            RefreshAllButtons();
        }
        answerWaitRoutine = null;
    }

    private void ResolveQuestion()
    {
        if (!IsGameRunning || ending || changing) return;
        changing = true;
        questionResolved = true;
        inputEnabled = false;
        StopQuestionTimer();
        DisableAllButtons();
        ScheduleNextQuestion(CreateNextQuestion);
    }

    protected override void OnQuestionTimeout()
    {
        ResolveQuestion();
    }

    protected override void OnGameEndWaitingForActions()
    {
        ending = true;
        inputEnabled = false;
        DisableAllButtons();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        for (int side = 0; side < 2; side++)
        {
            for (int index = 0; index < 3; index++)
            {
                int capturedSide = side;
                int capturedIndex = index;
                TouchUIButton button = GetButton(side, index);
                if (button == null) continue;
                UnityAction callback = () => Choose(capturedSide == 0, capturedIndex);
                buttonListeners[side, index] = callback;
                boundButtons[side, index] = button;
                button.AddListener(callback);
            }
        }
        buttonsBound = true;
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        for (int side = 0; side < 2; side++)
        {
            for (int index = 0; index < 3; index++)
            {
                if (boundButtons[side, index] != null && buttonListeners[side, index] != null) boundButtons[side, index].RemoveListener(buttonListeners[side, index]);
                boundButtons[side, index] = null;
                buttonListeners[side, index] = null;
            }
        }
        buttonsBound = false;
    }

    private TouchUIButton GetButton(int side, int index)
    {
        TouchUIButton[] buttons = side == 0 ? LeftAnswerButtons : RightAnswerButtons;
        return buttons != null && index < buttons.Length ? buttons[index] : null;
    }

    private void CacheButtonScales()
    {
        for (int side = 0; side < 2; side++)
        {
            for (int index = 0; index < 3; index++)
            {
                TouchUIButton button = GetButton(side, index);
                Vector3 scale = button != null ? button.transform.localScale : Vector3.one;
                buttonScales[side, index] = scale == Vector3.zero ? Vector3.one : scale;
            }
        }
    }

    private void HideAnswerButtons()
    {
        for (int side = 0; side < 2; side++)
        {
            for (int index = 0; index < 3; index++)
            {
                TouchUIButton button = GetButton(side, index);
                if (button != null) button.transform.localScale = Vector3.zero;
            }
        }
    }

    private void RestoreButtonScales()
    {
        for (int side = 0; side < 2; side++)
        {
            for (int index = 0; index < 3; index++)
            {
                TouchUIButton button = GetButton(side, index);
                if (button != null) button.transform.localScale = buttonScales[side, index];
            }
        }
    }

    private void DisableAllButtons()
    {
        for (int side = 0; side < 2; side++)
        {
            for (int index = 0; index < 3; index++)
            {
                TouchUIButton button = GetButton(side, index);
                if (button != null) button.interactable = false;
            }
        }
    }

    private void RefreshAllButtons()
    {
        for (int side = 0; side < 2; side++)
        {
            bool enabled = IsGameRunning && !ending && inputEnabled && !questionResolved && !protectedAnswers[side] && !(side == 0 ? leftAnswered : rightAnswered);
            for (int index = 0; index < 3; index++)
            {
                TouchUIButton button = GetButton(side, index);
                if (button != null) button.interactable = enabled;
            }
        }
    }

    private static Camera GetUICamera(RectTransform rect)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        Canvas root = canvas.rootCanvas;
        if (root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return root.worldCamera != null ? root.worldCamera : Camera.main;
    }

    private static Vector2 GetScreenCenter(RectTransform rect)
    {
        return RectTransformUtility.WorldToScreenPoint(GetUICamera(rect), rect.TransformPoint(rect.rect.center));
    }

    private bool ValidateSetup()
    {
        if (QuestionText == null) return Invalid("请绑定 QuestionText。");
        if (LeftAnswerButtons == null || RightAnswerButtons == null || LeftAnswerButtons.Length != 3 || RightAnswerButtons.Length != 3) return Invalid("左右答案按钮都必须恰好配置3个。");
        HashSet<TouchUIButton> unique = new HashSet<TouchUIButton>();
        for (int side = 0; side < 2; side++)
        {
            for (int index = 0; index < 3; index++)
            {
                TouchUIButton button = GetButton(side, index);
                if (button == null || !unique.Add(button) || !(button.transform is RectTransform)) return Invalid("答案按钮不能留空或重复，且必须使用 RectTransform。");
                Text text = button.GetComponentInChildren<Text>(true);
                if (text == null) return Invalid("每个 TouchUIButton 下都必须包含 Text 文本。");
                if (side == 0) LeftAnswerTexts[index] = text;
                else RightAnswerTexts[index] = text;
            }
        }
        return true;
    }

    private bool Invalid(string message)
    {
        Debug.LogError("数字题：" + message, this);
        return false;
    }

    private void KillMathTweens()
    {
        if (entranceSequence != null) entranceSequence.Kill(false);
        entranceSequence = null;
        for (int side = 0; side < 2; side++)
        {
            if (feedbackTweens[side] != null) feedbackTweens[side].Kill(false);
            feedbackTweens[side] = null;
        }
        if (answerWaitRoutine != null) StopCoroutine(answerWaitRoutine);
        answerWaitRoutine = null;
    }

    protected override void CleanupGame()
    {
        ending = true;
        inputEnabled = false;
        revision++;
        KillMathTweens();
        DisableAllButtons();
        RestoreButtonScales();
        if (QuestionText != null) QuestionText.text = fullQuestion;
    }

    public override void CancelForSceneExit()
    {
        base.CancelForSceneExit();
        protectedAnswers[0] = protectedAnswers[1] = false;
    }

    protected override void OnDestroy()
    {
        UnbindButtons();
        base.OnDestroy();
    }
}
