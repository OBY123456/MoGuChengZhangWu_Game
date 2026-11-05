using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

[RequireComponent(typeof(RectTransform))]
public class PirateCoinStepItem : BaseTouch
{
    [Tooltip("该道具的分数；得分道具直接按此值计分，炸弹按此值的绝对值扣分")]
    public int Score = 10;

    [Tooltip("炸弹左右摇摆的最大角度；设为0关闭摇摆，运行时修改立即生效")]
    [Range(0f, 15f)] public float BombSwayAngle = 6f;

    [Tooltip("炸弹完整左右摇摆一轮的时长；修改后下次生成时生效")]
    [Min(0.1f)] public float BombSwayDuration = 1.2f;

    [Tooltip("得分道具呼吸放大幅度；0.06为最大放大6%，设为0关闭，运行时修改立即生效")]
    [Range(0f, 0.2f)] public float ScoreBreathAmount = 0.06f;

    [Tooltip("得分道具单程呼吸时长；放大、缩小各用此时长，修改后下次生成时生效")]
    [Min(0.1f)] public float ScoreBreathDuration = 0.9f;

    public enum ItemType
    {
        炸弹,
        得分道具,
    }

    public PirateCoinStepGameManager Owner { get; private set; }
    public ItemType Type { get; private set; }
    public int Side { get; private set; }
    public int Revision { get; private set; }

    private RectTransform rect, cell;
    private Graphic[] graphics;
    private bool[] originalRaycasts;
    private Vector3 normalScale;
    private Tween motion;
    private Tween bombSway;
    private Tween scoreBreathing;
    private Quaternion normalRotation = Quaternion.identity;
    private Action finished;
    private bool touchable, consumed;

    protected override void Awake()
    {
        base.Awake();
        rect = GetComponent<RectTransform>();
        graphics = GetComponentsInChildren<Graphic>(true);
        originalRaycasts = new bool[graphics.Length];
        for (int i = 0; i < graphics.Length; i++) originalRaycasts[i] = graphics[i].raycastTarget;
        LayoutElement layout = GetComponent<LayoutElement>();
        if (layout == null) layout = gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
    }

    public void Initialize(PirateCoinStepGameManager owner, RectTransform targetCell, RectTransform prefab, int side, int revision, bool bomb)
    {
        Release();
        Owner = owner;
        cell = targetCell;
        Side = side;
        Revision = revision;
        Type = bomb ? ItemType.炸弹 : ItemType.得分道具;
        PirateCoinStepItem source = prefab.GetComponent<PirateCoinStepItem>();
        Score = source.Score;
        BombSwayAngle = source.BombSwayAngle;
        BombSwayDuration = source.BombSwayDuration;
        ScoreBreathAmount = source.ScoreBreathAmount;
        ScoreBreathDuration = source.ScoreBreathDuration;
        consumed = false;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = prefab.rect.size;
        normalRotation = prefab.localRotation;
        rect.localRotation = normalRotation;
        normalScale = prefab.localScale;
        rect.localScale = Vector3.zero;
        AlignToCell(owner.ItemParent);
        SetTouchable(false);
    }

    public void AlignToCell(RectTransform parent)
    {
        if (rect == null || cell == null || parent == null) return;
        Vector3 center = cell.TransformPoint(cell.rect.center);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(GetUICamera(cell), center);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, GetUICamera(parent), out Vector2 local)) rect.localPosition = new Vector3(local.x, local.y, 0f);
    }

    public static Camera GetUICamera(Transform target)
    {
        Canvas canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        Canvas root = canvas.rootCanvas;
        if (root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return root.worldCamera != null ? root.worldCamera : Camera.main;
    }

    public void InsertEntrance(Sequence sequence, float start, float duration)
    {
        sequence.Insert(start, rect.DOScale(normalScale, duration).SetEase(Ease.OutBack, 1.35f).OnComplete(StartIdleEffect));
    }

    private void StartIdleEffect()
    {
        if (Type == ItemType.炸弹) StartBombSway();
        else StartScoreBreathing();
    }

    private void StartScoreBreathing()
    {
        StopScoreBreathing(true);
        if (Type != ItemType.得分道具 || consumed || Owner == null || !isActiveAndEnabled) return;
        scoreBreathing = DOVirtual.Float(0f, Mathf.PI * 2f, Mathf.Max(0.1f, ScoreBreathDuration) * 2f, ApplyScoreBreathing).SetEase(Ease.Linear).SetLoops(-1, LoopType.Restart).SetAutoKill(true).SetRecyclable(false);
        scoreBreathing.Play();
    }

    private void ApplyScoreBreathing(float phase)
    {
        if (rect == null) return;
        float weight = (1f - Mathf.Cos(phase)) * 0.5f;
        float scale = 1f + Mathf.Clamp(ScoreBreathAmount, 0f, 0.2f) * weight;
        rect.localScale = normalScale * scale;
    }

    private void StopScoreBreathing(bool restoreScale)
    {
        if (scoreBreathing != null) scoreBreathing.Kill(false);
        scoreBreathing = null;
        if (restoreScale && rect != null) rect.localScale = normalScale;
    }

    private void StartBombSway()
    {
        StopBombSway(true);
        if (Type != ItemType.炸弹 || consumed || Owner == null || !isActiveAndEnabled) return;
        bombSway = DOVirtual.Float(0f, Mathf.PI * 2f, Mathf.Max(0.1f, BombSwayDuration), ApplyBombSway).SetEase(Ease.Linear).SetLoops(-1, LoopType.Restart).SetAutoKill(true).SetRecyclable(false);
        bombSway.Play();
    }

    private void ApplyBombSway(float phase)
    {
        if (rect == null) return;
        float angle = Mathf.Sin(phase) * Mathf.Clamp(BombSwayAngle, 0f, 15f);
        rect.localRotation = normalRotation * Quaternion.Euler(0f, 0f, angle);
    }

    private void StopBombSway(bool restoreRotation)
    {
        if (bombSway != null) bombSway.Kill(false);
        bombSway = null;
        if (restoreRotation && rect != null) rect.localRotation = normalRotation;
    }

    public void SetTouchable(bool value)
    {
        touchable = value && !consumed;
        if (graphics == null) return;
        for (int i = 0; i < graphics.Length; i++) if (graphics[i] != null) graphics[i].raycastTarget = touchable && originalRaycasts[i];
    }

    public bool TryClaim()
    {
        if (!touchable || consumed || !isActiveAndEnabled) return false;
        consumed = true;
        StopBombSway(true);
        StopScoreBreathing(true);
        SetTouchable(false);
        return true;
    }

    public override void OnTouch(Vector3 point)
    {
        if (Owner == null || consumed || !touchable || !isActiveAndEnabled) return;
        PirateCoinStepGameManager owner = Owner;
        int id = Revision;
        owner.TryStep(this, point);
        if (consumed && Owner == owner && Revision == id) base.OnTouch(point);
    }

    public void PlayStep(float duration, float scoreDuration, Action onFinished)
    {
        StopScoreBreathing(true);
        finished = onFinished;
        Sequence sequence = DOTween.Sequence().SetAutoKill(true).SetRecyclable(false).Pause();
        sequence.Append(rect.DOScale(normalScale * 1.12f, duration * 0.3f).SetEase(Ease.OutQuad));
        sequence.Append(rect.DOScale(Vector3.zero, duration * 0.7f).SetEase(Ease.InBack));
        sequence.AppendInterval(Mathf.Max(0f, scoreDuration - duration));
        sequence.OnComplete(CompleteStep);
        motion = sequence;
        sequence.Play();
    }

    private void CompleteStep()
    {
        motion = null;
        Action callback = finished;
        finished = null;
        callback?.Invoke();
    }

    public void Freeze()
    {
        SetTouchable(false);
        StopBombSway(false);
        StopScoreBreathing(false);
        if (motion != null) motion.Kill(false);
        motion = null;
        if (!consumed && rect != null) rect.localScale = normalScale;
    }

    public void Release()
    {
        finished = null;
        StopBombSway(true);
        StopScoreBreathing(true);
        Freeze();
        Owner = null;
        cell = null;
    }

    private void OnDisable()
    {
        StopBombSway(true);
        StopScoreBreathing(true);
        if (motion != null) motion.Kill(false);
        motion = null;
        SetTouchable(false);
        CompleteStep();
    }

    private void OnDestroy()
    {
        Release();
    }
}