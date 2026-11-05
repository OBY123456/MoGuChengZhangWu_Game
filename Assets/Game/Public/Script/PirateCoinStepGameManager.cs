using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Lean.Pool;
using DG.Tweening;

/// <summary>双方踩道具计分；双方全部踩完或本题超时后，同步切题。</summary>
public class PirateCoinStepGameManager : BaseGameManager
{
    [Tooltip("左右格子，按各侧弹出顺序填写；格子不可重复或留空")]
    public RectTransform[] LeftCells = new RectTransform[20];
    public RectTransform[] RightCells = new RectTransform[20];

    [Tooltip("炸弹道具，按其 PirateCoinStepItem.Score 的绝对值扣分；Score 设为10即扣10分")]
    public GameObject Boom;

    [Tooltip("得分道具，每个道具按其 PirateCoinStepItem.Score 配置计分，不按拾取次数递增")]
    public GameObject[] ScoreItem;

    [Tooltip("道具生成父物体；自动换算格子坐标，不修改预制体宽高")]
    public RectTransform ItemParent;

    [Tooltip("每侧炸弹数量；最多为本侧格子数减1，至少保留一个得分道具")]
    [Min(0)] public int BombCount = 3;

    [Tooltip("相邻道具弹出的时间间隔，左右相同序号同时弹出")]
    [Min(0.01f)] public float PopInterval = 0.08f;

    [Tooltip("单个道具的弹出动画时长")]
    [Min(0.01f)] public float PopDuration = 0.3f;

    [Tooltip("炸弹踩中后的缩小退场时长；得分道具使用下方的飞行动画")]
    [Min(0.01f)] public float StepDuration = 0.2f;

    [Header("得分道具飞入分数")]
    [Tooltip("道具飞到本方分数文本中心的时长，到达回收后才计分")]
    [Min(0.1f)] public float ScoreFlyDuration = 0.8f;

    [Tooltip("弧线相对于起终点连线的向上抬升量，单位为屏幕像素")]
    [Min(0f)] public float ScoreArcHeight = 160f;

    [Tooltip("飞行过程中顺时针旋转的圈数，后半程逐渐缩小到零")]
    [Min(0f)] public float ScoreSpinTurns = 1f;

    private sealed class PendingStep
    {
        public PirateCoinStepItem Item;
        public int Side, Points, Revision;
        public bool Awarded, Arrived;
        public Sequence Flight;
    }

    private readonly List<PirateCoinStepItem> items = new List<PirateCoinStepItem>();
    private readonly List<PendingStep> pending = new List<PendingStep>();
    private readonly HashSet<PirateCoinStepItem> exitingItems = new HashSet<PirateCoinStepItem>();
    private readonly int[] remaining = new int[2];
    private Sequence entrance;
    private Coroutine scoreWaitRoutine;
    private int revision, scoreOffset;
    private bool prepared, revealing, changing, ending;

    protected override void PrepareGameForReadyGo()
    {
        ending = false;
        prepared = BuildQuestion();
    }

    protected override bool IsPreparationVisualReady()
    {
        return !revealing;
    }

    protected override void InitGame()
    {
        ending = false;
        if (!prepared) prepared = BuildQuestion();
        if (!prepared) { GameEnd(DetermineResult()); return; }
        if (!revealing) StartQuestionTimer();
    }

    protected override void CreateNextQuestion()
    {
        if (!IsGameRunning || ending) return;
        prepared = BuildQuestion();
        if (!prepared) GameEnd(DetermineResult());
    }

    private bool BuildQuestion()
    {
        ClearBoard();
        ResetQuestionDisplay();
        if (!ValidateSetup()) return false;
        Canvas.ForceUpdateCanvases();
        scoreOffset = Random.Range(0, ScoreItem.Length);
        revealing = true;
        int id = revision;
        entrance = DOTween.Sequence().SetAutoKill(true).SetRecyclable(false).SetUpdate(IsPreparing).Pause();
        if (!SpawnSide(LeftCells, 0) || !SpawnSide(RightCells, 1))
        {
            ClearBoard();
            return false;
        }
        entrance.OnComplete(() => FinishEntrance(id));
        entrance.Play();
        return true;
    }

    private void ResetQuestionDisplay()
    {
        questionTimerRunning = false;
        questionResolved = false;
        questionTimer = Mathf.Max(0.01f, QuestionDuration);
        RefreshTimerUI();
        if (QuestionTimerImage != null) QuestionTimerImage.fillAmount = 1f;
    }

    private bool SpawnSide(RectTransform[] cells, int side)
    {
        GameObject[] plan = BuildPlan(cells.Length);
        for (int index = 0; index < cells.Length; index++)
        {
            GameObject clone = LeanPool.Spawn(plan[index], ItemParent, false);
            if (clone == null) return false;
            PirateCoinStepItem item = clone.GetComponent<PirateCoinStepItem>();
            items.Add(item);
            bool bomb = plan[index] == Boom;
            item.Initialize(this, cells[index], plan[index].GetComponent<RectTransform>(), side, revision, bomb);
            if (!bomb) remaining[side]++;
            item.InsertEntrance(entrance, index * Mathf.Max(0.01f, PopInterval), Mathf.Max(0.01f, PopDuration));
        }
        return true;
    }

    private GameObject[] BuildPlan(int count)
    {
        GameObject[] plan = new GameObject[count];
        int bombs = Mathf.Clamp(BombCount, 0, count - 1);
        for (int i = 0; i < count; i++) plan[i] = i < bombs ? Boom : ScoreItem[(i - bombs + scoreOffset) % ScoreItem.Length];
        for (int i = count - 1; i > 0; i--)
        {
            int other = Random.Range(0, i + 1);
            GameObject saved = plan[i];
            plan[i] = plan[other];
            plan[other] = saved;
        }
        return plan;
    }

    private void FinishEntrance(int id)
    {
        if (revision != id || ending) return;
        entrance = null;
        revealing = false;
        foreach (PirateCoinStepItem item in items) if (item != null) item.SetTouchable(true);
        if (IsGameRunning) StartQuestionTimer();
    }

    public void TryStep(PirateCoinStepItem item, Vector3 point)
    {
        if (!IsGameRunning || ending || revealing || changing || questionResolved || !questionTimerRunning || GetRemainingTime() <= 0f) return;
        if (item == null || item.Owner != this || item.Revision != revision || !item.TryClaim()) return;
        int side = item.Side;
        bool bomb = item.Type == PirateCoinStepItem.ItemType.炸弹;
        int points = bomb && item.Score > 0 ? -item.Score : item.Score;
        if (!bomb) remaining[side]--;
        BeginProtectedAction();
        PendingStep step = new PendingStep { Item = item, Side = side, Points = points, Revision = revision };
        pending.Add(step);
        exitingItems.Add(item);
        if (bomb)
        {
            AwardStep(step);
            item.PlayStep(Mathf.Max(0.01f, StepDuration), 0f, () => FinishItemAnimation(step));
        }
        else StartScoreFlight(step);
        if (remaining[0] == 0 && remaining[1] == 0) ResolveQuestion();
    }

    private void StartScoreFlight(PendingStep step)
    {
        RectTransform rect = step.Item.transform as RectTransform;
        Vector2 startScreen = GetScreenCenter(rect);
        Vector3 rotation = rect.localEulerAngles;
        rotation.z -= 360f * Mathf.Max(0f, ScoreSpinTurns);
        rect.SetAsLastSibling();
        float duration = Mathf.Max(0.1f, ScoreFlyDuration);
        float progress = 0f;
        Sequence flight = DOTween.Sequence().SetAutoKill(true).SetRecyclable(false).Pause();
        flight.Append(DOTween.To(() => progress, value => { progress = value; ApplyScoreFlight(step, startScreen, value); }, 1f, duration).SetEase(Ease.InOutSine));
        flight.Join(rect.DOLocalRotate(rotation, duration, RotateMode.FastBeyond360).SetEase(Ease.Linear));
        flight.Insert(duration * 0.5f, rect.DOScale(Vector3.zero, duration * 0.5f).SetEase(Ease.InCubic));
        flight.OnComplete(() => ArriveScoreItem(step));
        step.Flight = flight;
        flight.Play();
    }

    private void ApplyScoreFlight(PendingStep step, Vector2 startScreen, float progress)
    {
        if (step.Revision != revision || step.Item == null) return;
        RectTransform rect = step.Item.transform as RectTransform;
        RectTransform target = step.Side == 0 ? leftScoreText.rectTransform : rightScoreText.rectTransform;
        RectTransform parent = rect.parent as RectTransform;
        if (parent == null) return;
        Vector2 endScreen = GetScreenCenter(target);
        Vector2 control = (startScreen + endScreen) * 0.5f + Vector2.up * (2f * Mathf.Max(0f, ScoreArcHeight));
        float inverse = 1f - progress;
        Vector2 screen = inverse * inverse * startScreen + 2f * inverse * progress * control + progress * progress * endScreen;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, PirateCoinStepItem.GetUICamera(parent), out Vector2 local)) rect.localPosition = new Vector3(local.x, local.y, 0f);
    }

    private static Vector2 GetScreenCenter(RectTransform rect)
    {
        return RectTransformUtility.WorldToScreenPoint(PirateCoinStepItem.GetUICamera(rect), rect.TransformPoint(rect.rect.center));
    }

    private void ArriveScoreItem(PendingStep step)
    {
        if (step.Revision != revision || !pending.Contains(step)) return;
        step.Flight = null;
        FinishItemAnimation(step);
        AwardStep(step);
    }

    private void FinishItemAnimation(PendingStep step)
    {
        PirateCoinStepItem item = step.Item;
        if (step.Revision != revision || !exitingItems.Remove(item)) return;
        step.Arrived = true;
        items.Remove(item);
        if (item != null) LeanPool.Despawn(item.gameObject);
    }

    private void AwardStep(PendingStep step)
    {
        if (step.Awarded || step.Revision != revision || !pending.Contains(step) || !IsGameRunning) return;
        step.Awarded = true;
        if (step.Side == 0) AddLeftScore(step.Points);
        else AddRightScore(step.Points);
        if (scoreWaitRoutine == null) scoreWaitRoutine = StartCoroutine(WaitForScoredSteps());
    }

    private IEnumerator WaitForScoredSteps()
    {
        while (IsGameRunning && pending.Count > 0)
        {
            yield return null;
            if (IsScoreRolling) continue;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingStep step = pending[i];
                if (!step.Awarded || !step.Arrived) continue;
                pending.RemoveAt(i);
                EndProtectedAction();
                if (!IsGameRunning) break;
            }
            bool hasAwardedStep = false;
            foreach (PendingStep step in pending) if (step.Awarded) hasAwardedStep = true;
            if (!hasAwardedStep) break;
        }
        scoreWaitRoutine = null;
    }

    protected override void OnQuestionTimeout()
    {
        ResolveQuestion();
    }

    private void ResolveQuestion()
    {
        if (changing || ending || !IsGameRunning) return;
        changing = true;
        questionResolved = true;
        StopQuestionTimer();
        foreach (PirateCoinStepItem item in items) if (item != null) item.SetTouchable(false);
        ScheduleNextQuestion(CreateNextQuestion);
    }

    private void LateUpdate()
    {
        if (!IsPreparing && !IsGameRunning) return;
        foreach (PirateCoinStepItem item in items) if (item != null && !exitingItems.Contains(item)) item.AlignToCell(ItemParent);
    }

    public override void GameEnd(ResultType result)
    {
        if (!IsGameRunning) return;
        ending = true;
        changing = true;
        foreach (PirateCoinStepItem item in items) if (item != null) item.SetTouchable(false);
        // 与迷宫一致：停新操作，保留已开始的飞行，等待到达计分和总分滚动。
        base.GameEnd(DetermineResult());
    }

    protected override void CleanupGame()
    {
        ending = true;
        changing = true;
        if (entrance != null) entrance.Kill(false);
        entrance = null;
        revealing = false;
        foreach (PirateCoinStepItem item in items) if (item != null) item.Freeze();
    }

    public override void CancelForSceneExit()
    {
        ending = true;
        revision++;
        base.CancelForSceneExit();
        ClearBoard();
    }

    private void ClearBoard()
    {
        revision++;
        prepared = false;
        revealing = false;
        changing = false;
        if (entrance != null) entrance.Kill(false);
        entrance = null;
        if (scoreWaitRoutine != null) StopCoroutine(scoreWaitRoutine);
        scoreWaitRoutine = null;
        foreach (PendingStep step in pending) if (step.Flight != null) step.Flight.Kill(false);
        pending.Clear();
        exitingItems.Clear();
        foreach (PirateCoinStepItem item in items)
        {
            if (item == null) continue;
            item.Release();
            if (Application.isPlaying) LeanPool.Despawn(item.gameObject);
        }
        items.Clear();
        remaining[0] = remaining[1] = 0;
    }

    private bool ValidateSetup()
    {
        if (ItemParent == null || ItemParent.GetComponentInParent<Canvas>() == null) return Invalid("ItemParent 必须位于 Canvas 下。");
        if (leftScoreText == null || rightScoreText == null) return Invalid("请绑定左右分数文本，作为得分道具飞行的目标。");
        if (!HasUICamera(ItemParent) || !HasUICamera(leftScoreText.rectTransform) || !HasUICamera(rightScoreText.rectTransform)) return Invalid("道具和分数文本必须位于 Canvas 下；非 Overlay Canvas 需要配置渲染相机或 MainCamera。");
        if (LayerMask.NameToLayer("Touch") < 0) return Invalid("请先创建名为 Touch 的 Layer。");
        if (LeftCells == null || RightCells == null || LeftCells.Length == 0 || RightCells.Length == 0) return Invalid("双方至少需要一个格子。");
        HashSet<RectTransform> cells = new HashSet<RectTransform>();
        foreach (RectTransform cell in LeftCells) if (cell == null || !cells.Add(cell)) return Invalid("LeftCells 存在空格子或重复格子。");
        foreach (RectTransform cell in RightCells) if (cell == null || !cells.Add(cell)) return Invalid("RightCells 存在空格子或与其他格子重复。");
        if (ScoreItem == null || ScoreItem.Length == 0) return Invalid("至少绑定一种得分道具。");
        foreach (GameObject prefab in ScoreItem) if (!ValidPrefab(prefab) || prefab == Boom) return Invalid("得分道具不能留空或与炸弹相同；根节点需要 RectTransform 和 PirateCoinStepItem，并包含 UI Graphic。");
        if (BombCount > 0 && !ValidPrefab(Boom)) return Invalid("请绑定炸弹预制体，根节点需要 RectTransform 和 PirateCoinStepItem，并包含 UI Graphic。");
        return true;
    }

    private static bool ValidPrefab(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<RectTransform>() != null && prefab.GetComponent<PirateCoinStepItem>() != null && prefab.GetComponentInChildren<Graphic>(true) != null;
    }

    private static bool HasUICamera(Transform target)
    {
        Canvas canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null) return false;
        Canvas root = canvas.rootCanvas;
        return root.renderMode == RenderMode.ScreenSpaceOverlay || root.worldCamera != null || Camera.main != null;
    }

    private bool Invalid(string message)
    {
        Debug.LogError("PirateCoinStep：" + message, this);
        return false;
    }
}
