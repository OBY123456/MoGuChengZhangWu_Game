using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Lean.Pool;
using DG.Tweening;

/// <summary>第2关：记忆阵。记住脚印的出现顺序，左右双方独立答题。</summary>
public class PirateMemoryGameManager : BaseGameManager
{
    [Tooltip("左右答案按钮，子物体有Text，Text显示数字范围在1~6之间，顺序可以乱，左右数字和数字顺序要相同")]
    public PirateMemoryGameButton[] LeftAnswers = new PirateMemoryGameButton[5];
    public PirateMemoryGameButton[] RightAnswers = new PirateMemoryGameButton[5];

    [Tooltip("问题格子位置，用来给QuestionPrefab创建的位置信息")]
    public RectTransform[] QuestionCells = new RectTransform[6];

    [Tooltip("题目预制体，上面有Image组件，QuestionCells的格子是在布局组件下面，你创建QuestionPrefab的时候要注意UI坐标转换")]
    public GameObject QuestionPrefab;

    [Tooltip("数字图片，数字图片的数字要对应QuestionCells的顺序，比如数字1只能出现在下标为0的格子上面")]
    public Sprite[] sprites = new Sprite[6];

    [Tooltip("创建QuestionPrefab的父物体")]
    public RectTransform QuestionParent;

    [Header("记忆阵动画和分数")]
    [Min(0.01f)] public float ButtonAppearDuration = 0.55f;
    [Min(0.01f)] public float WaveStaggerInterval = 0.13f;
    [Min(0)] public int PointsPerCorrectAnswer = 10;

    private const int CellCount = 6;
    private const int ReplayCount = 2;
    private const float FootprintStep = 0.5f;
    private const float FootprintAppearDuration = 0.38f;
    private const float FootprintHold = 1f;
    private const float ReplayGap = 2f;

    private sealed class AnswerSlot
    {
        public PirateMemoryGameButton Button;
        public RectTransform Rect;
        public Text Label;
        public Vector3 Scale;
        public UnityAction Click;
        public int Number;
    }

    private readonly AnswerSlot[][] answers = { null, null };
    private readonly List<AnswerSlot> entranceOrder = new List<AnswerSlot>(12);
    private readonly int[] digits = new int[CellCount];
    private readonly int[] question = new int[4];
    private readonly int[] progress = new int[2];
    private readonly GameObject[] footprints = new GameObject[4];
    private readonly Tween[] footprintTweens = new Tween[4];
    private Sequence preparationEntrance;
    private Sequence questionSequence;
    private int questionLength;
    private bool inputEnabled;
    private bool preparedQuestion;
    private bool preparationPreviewHidden;

    protected override void Awake()
    {
        base.Awake();
        BindAnswers(0, LeftAnswers);
        BindAnswers(1, RightAnswers);
    }

    protected override void Start()
    {
        SetAnswerInput(false);
        base.Start();
    }

    protected override void PrepareGameForReadyGo()
    {
        CleanupGame();
        if (!ValidateSetup()) return;
        CreateQuestion();
        preparedQuestion = true;
        Sequence entrance = DOTween.Sequence().SetUpdate(true).SetAutoKill(true).Pause();
        preparationEntrance = entrance;
        float buttonEntranceEnd = InsertButtonEntrance(entrance);
        InsertFootprintPreview(entrance, 0f, false);
        float visualEnd = Mathf.Max(buttonEntranceEnd, GetFootprintAppearanceDuration());
        entrance.InsertCallback(visualEnd, () => preparationEntrance = null);
        entrance.Play();
    }

    protected override bool IsPreparationVisualReady()
    {
        return preparationEntrance == null;
    }

    protected override void InitGame()
    {
        if (!preparedQuestion)
        {
            GameEnd(DetermineResult());
            return;
        }

        StartQuestion(false, true);
    }

    protected override void CreateNextQuestion()
    {
        StartQuestion(true);
    }

    private void StartQuestion(bool animateButtons, bool reusePreparedQuestion = false)
    {
        if (!IsGameRunning) return;
        CleanupGame();
        questionTimer = Mathf.Max(0.01f, QuestionDuration);
        questionResolved = false;
        StopQuestionTimer();
        progress[0] = progress[1] = 0;
        if (!reusePreparedQuestion) CreateQuestion();
        PlayQuestionSequence(animateButtons);
    }

    private void LateUpdate()
    {
        if (!IsPreparing || preparationPreviewHidden || readyGoPanel == null || !readyGoPanel.IsCountingDown) return;
        preparationPreviewHidden = true;
        HideFootprints();
    }

    private void BindAnswers(int side, PirateMemoryGameButton[] buttons)
    {
        if (buttons == null) return;
        answers[side] = new AnswerSlot[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            PirateMemoryGameButton button = buttons[i];
            if (button == null) continue;
            Vector3 scale = button.transform.localScale;
            AnswerSlot slot = new AnswerSlot { Button = button, Rect = (RectTransform)button.transform, Label = button.GetComponentInChildren<Text>(true), Scale = scale == Vector3.zero ? Vector3.one : scale };
            slot.Click = () => Choose(side, slot);
            answers[side][i] = slot;
            entranceOrder.Add(slot);
            button.AddListener(slot.Click);
        }
    }

    private bool ValidateSetup()
    {
        if (answers[0] == null || answers[1] == null || answers[0].Length != answers[1].Length || answers[0].Length < 4 || answers[0].Length > CellCount) return InvalidSetup("左右答案按钮必须等量，每边 4～6 个。");
        HashSet<PirateMemoryGameButton> buttons = new HashSet<PirateMemoryGameButton>();
        foreach (AnswerSlot[] side in answers)
        {
            foreach (AnswerSlot slot in side)
            {
                if (slot == null || slot.Label == null || !buttons.Add(slot.Button)) return InvalidSetup("答案按钮需各自绑定一个带 Text 的独立物体。");
            }
        }

        if (QuestionParent == null || QuestionPrefab == null || QuestionPrefab.GetComponent<Image>() == null) return InvalidSetup("请配置 QuestionParent 和根物体带 Image 的 QuestionPrefab。");
        if (QuestionCells == null || QuestionCells.Length < CellCount || sprites == null || sprites.Length < CellCount) return InvalidSetup("请配置六个问题格子和对应数字图片。");
        HashSet<RectTransform> cells = new HashSet<RectTransform>();
        for (int i = 0; i < CellCount; i++)
        {
            if (QuestionCells[i] == null || sprites[i] == null || !cells.Add(QuestionCells[i])) return InvalidSetup("六个格子必须独立，每个格子需配置对应的数字 Sprite。");
        }

        return true;
    }

    private bool InvalidSetup(string message)
    {
        Debug.LogError("记忆阵：" + message, this);
        return false;
    }

    private void CreateQuestion()
    {
        for (int i = 0; i < CellCount; i++) digits[i] = i + 1;
        Shuffle(digits, CellCount);
        int answerCount = answers[0].Length;
        for (int i = 0; i < answerCount; i++)
        {
            for (int side = 0; side < 2; side++)
            {
                AnswerSlot slot = answers[side][i];
                slot.Number = digits[i];
                slot.Label.text = slot.Number.ToString();
                slot.Button.ResetForQuestion();
            }
        }

        // 题目只抽取本轮按钮上存在的数字，默认五个按钮也能覆盖全部题目。
        Shuffle(digits, answerCount);
        questionLength = Random.Range(0, 10) < 9 ? 3 : 4;
        for (int i = 0; i < questionLength; i++) question[i] = digits[i];
    }

    private static void Shuffle(int[] values, int count)
    {
        for (int i = count - 1; i > 0; i--)
        {
            int index = Random.Range(0, i + 1);
            int saved = values[i];
            values[i] = values[index];
            values[index] = saved;
        }
    }

    private float InsertButtonEntrance(Sequence timeline)
    {
        Canvas.ForceUpdateCanvases();
        entranceOrder.Sort((a, b) => GetScreenCenter(a.Rect).x.CompareTo(GetScreenCenter(b.Rect).x));
        float duration = Mathf.Max(0.45f, ButtonAppearDuration);
        float stagger = Mathf.Clamp(WaveStaggerInterval, 0.02f, duration * 0.65f);
        for (int i = 0; i < entranceOrder.Count; i++)
        {
            AnswerSlot slot = entranceOrder[i];
            slot.Rect.localScale = Vector3.zero;
            timeline.Insert(i * stagger, CreateBounce(slot.Rect, slot.Scale, duration));
        }

        return (entranceOrder.Count - 1) * stagger + duration;
    }

    private void PlayQuestionSequence(bool animateButtons)
    {
        Canvas.ForceUpdateCanvases();
        Sequence timeline = DOTween.Sequence().SetAutoKill(true).Pause();
        questionSequence = timeline;
        float showAt = animateButtons ? InsertButtonEntrance(timeline) : 0f;
        for (int replay = 0; replay <= ReplayCount; replay++)
        {
            InsertFootprintPreview(timeline, showAt, replay > 0);
            float hideAt = showAt + GetPreviewDuration();
            if (replay == 0) timeline.InsertCallback(hideAt, BeginAnswering);
            else timeline.InsertCallback(hideAt, HideFootprints);
            showAt = hideAt + ReplayGap;
        }

        timeline.OnComplete(() => questionSequence = null);
        timeline.Play();
    }

    private float GetFootprintAppearanceDuration()
    {
        return (questionLength - 1) * FootprintStep + FootprintAppearDuration;
    }

    private float GetPreviewDuration()
    {
        return GetFootprintAppearanceDuration() + FootprintHold;
    }

    private void InsertFootprintPreview(Sequence timeline, float showAt, bool isReplay)
    {
        bool showPreview = !isReplay;
        float duration = GetPreviewDuration();
        for (int i = 0; i < questionLength; i++)
        {
            int index = i;
            timeline.InsertCallback(showAt + i * FootprintStep, () =>
            {
                if (isReplay && index == 0) showPreview = IsGameRunning && questionTimerRunning && !questionResolved && questionTimer >= duration && GetRemainingTime() >= duration;
                if (showPreview) ShowFootprint(index);
            });
        }
    }

    private static Sequence CreateBounce(Transform target, Vector3 scale, float duration)
    {
        Sequence bounce = DOTween.Sequence().SetAutoKill(true);
        bounce.Append(target.DOScale(scale * 1.12f, duration * 0.72f).SetEase(Ease.InOutSine));
        bounce.Append(target.DOScale(scale, duration * 0.28f).SetEase(Ease.OutBack, 1.5f));
        return bounce;
    }

    private void ShowFootprint(int index)
    {
        if ((!IsGameRunning && !IsPreparing) || questionResolved) return;
        int cellIndex = question[index] - 1;
        RectTransform cell = QuestionCells[cellIndex];
        Rect bounds = cell.rect;
        if (!TryConvertCellPoint(cell, bounds.center, out Vector2 center)) return;
        if (!TryConvertCellPoint(cell, bounds.min, out Vector2 min)) return;
        if (!TryConvertCellPoint(cell, new Vector2(bounds.xMax, bounds.yMin), out Vector2 right)) return;
        if (!TryConvertCellPoint(cell, new Vector2(bounds.xMin, bounds.yMax), out Vector2 top)) return;

        GameObject instance = LeanPool.Spawn(QuestionPrefab, Vector3.zero, Quaternion.identity, QuestionParent);
        if (instance == null) return;
        footprints[index] = instance;
        Image image = instance.GetComponent<Image>();
        RectTransform rect = image.rectTransform;
        image.sprite = sprites[cellIndex];
        image.enabled = true;
        image.raycastTarget = false;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localRotation = Quaternion.identity;
        rect.localPosition = new Vector3(center.x, center.y, 0f);
        rect.sizeDelta = new Vector2(Vector2.Distance(min, right), Vector2.Distance(min, top));
        rect.localScale = Vector3.zero;
        rect.SetAsLastSibling();
        footprintTweens[index] = CreateBounce(rect, Vector3.one, FootprintAppearDuration).SetUpdate(IsPreparing).SetRecyclable(false);
    }

    private bool TryConvertCellPoint(RectTransform cell, Vector2 point, out Vector2 local)
    {
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(GetCanvasCamera(cell), cell.TransformPoint(point));
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(QuestionParent, screen, GetCanvasCamera(QuestionParent), out local);
    }

    private static Vector2 GetScreenCenter(RectTransform rect)
    {
        return RectTransformUtility.WorldToScreenPoint(GetCanvasCamera(rect), rect.TransformPoint(rect.rect.center));
    }

    private static Camera GetCanvasCamera(RectTransform rect)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Canvas root = canvas != null ? canvas.rootCanvas : null;
        if (root == null || root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return root.worldCamera != null ? root.worldCamera : Camera.main;
    }

    private void BeginAnswering()
    {
        HideFootprints();
        if (!IsGameRunning || questionResolved) return;
        StartQuestionTimer();
        SetAnswerInput(true);
    }

    private void Choose(int side, AnswerSlot slot)
    {
        if (!IsGameRunning || !inputEnabled || questionResolved || !questionTimerRunning || slot.Button.IsCorrectLocked || progress[side] >= questionLength) return;
        bool correct = slot.Number == question[progress[side]];
        if (correct)
        {
            slot.Button.LockCorrect();
            progress[side]++;
            if (side == 0) AddLeftScore(PointsPerCorrectAnswer);
            else AddRightScore(PointsPerCorrectAnswer);
        }

        PlayAnswerEffect(correct, GetScreenCenter(slot.Rect));
        if (progress[side] == questionLength)
        {
            foreach (AnswerSlot answer in answers[side]) answer.Button.interactable = false;
        }

        if (progress[0] == questionLength && progress[1] == questionLength) FinishQuestion();
    }

    protected override void OnQuestionTimeout()
    {
        FinishQuestion();
    }

    private void FinishQuestion()
    {
        questionResolved = true;
        SetAnswerInput(false);
        StopQuestionSequence();
        questionTimer = Mathf.Max(0.01f, QuestionDuration);
        ScheduleNextQuestion(CreateNextQuestion);
    }

    private void SetAnswerInput(bool enabled)
    {
        inputEnabled = enabled;
        foreach (AnswerSlot slot in entranceOrder) if (slot.Button != null) slot.Button.interactable = enabled && !slot.Button.IsCorrectLocked;
    }

    private void StopQuestionSequence()
    {
        Sequence timeline = questionSequence;
        questionSequence = null;
        if (timeline != null) timeline.Kill(false);
        HideFootprints();
    }

    private void HideFootprints()
    {
        for (int i = 0; i < footprints.Length; i++)
        {
            Tween tween = footprintTweens[i];
            footprintTweens[i] = null;
            if (tween != null) tween.Kill(false);
            GameObject instance = footprints[i];
            footprints[i] = null;
            if (instance != null) LeanPool.Despawn(instance);
        }
    }

    protected override void CleanupGame()
    {
        SetAnswerInput(false);
        preparedQuestion = false;
        preparationPreviewHidden = false;
        Sequence entrance = preparationEntrance;
        preparationEntrance = null;
        if (entrance != null) entrance.Kill(false);
        StopQuestionSequence();
        foreach (AnswerSlot slot in entranceOrder) if (slot.Rect != null) slot.Rect.localScale = slot.Scale;
    }

    private void OnDisable()
    {
        CancelForSceneExit();
    }

    protected override void OnDestroy()
    {
        foreach (AnswerSlot slot in entranceOrder) if (slot.Button != null) slot.Button.RemoveListener(slot.Click);
        base.OnDestroy();
    }
}
