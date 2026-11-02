using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>第2关：记忆阵。记忆目标格子的顺序，随后左右两边依序点击。</summary>
public class PirateMemoryGameManager : BaseGameManager
{
    public TouchUIButton[] LeftCells = new TouchUIButton[6];
    public TouchUIButton[] RightCells = new TouchUIButton[6];
    public Text[] LeftLabels = new Text[6];
    public Text[] RightLabels = new Text[6];
    public Text HintText;
    [Range(2, 6)] public int SequenceLength = 3;
    [Min(.1f)] public float PreviewSeconds = 2f;

    private int[] sequence;
    private int leftProgress, rightProgress;
    private bool leftDone, rightDone;
    private Coroutine preview;

    protected override void PrepareGameForReadyGo() { MakeSequence(); RenderPreview(); }
    protected override void InitGame() { BindCells(); preview = StartCoroutine(HidePreview()); }

    private void MakeSequence()
    {
        sequence = new int[Mathf.Clamp(SequenceLength, 2, 6)];
        for (int i = 0; i < sequence.Length; i++)
        {
            int v; do { v = Random.Range(0, 6); } while (ContainsEarlier(i, v));
            sequence[i] = v;
        }
        leftProgress = rightProgress = 0; leftDone = rightDone = false;
    }

    private bool ContainsEarlier(int count, int value)
    { for (int i = 0; i < count; i++) if (sequence[i] == value) return true; return false; }

    private void RenderPreview()
    {
        if (HintText != null) HintText.text = "记住亮起的顺序";
        for (int i = 0; i < 6; i++)
        {
            bool target = ContainsEarlier(sequence.Length, i);
            int order = -1;
            for (int j = 0; j < sequence.Length; j++) if (sequence[j] == i) order = j + 1;
            SetText(LeftLabels, i, target ? order.ToString() : "★");
            SetText(RightLabels, i, target ? order.ToString() : "★");
            SetInteractable(LeftCells, i, false); SetInteractable(RightCells, i, false);
        }
    }

    private IEnumerator HidePreview()
    {
        yield return new WaitForSeconds(PreviewSeconds);
        for (int i = 0; i < 6; i++)
        { SetText(LeftLabels, i, "★"); SetText(RightLabels, i, "★"); SetInteractable(LeftCells, i, true); SetInteractable(RightCells, i, true); }
        if (HintText != null) HintText.text = "按顺序点击";
        StartQuestionTimer();
    }

    private void BindCells()
    {
        for (int i = 0; i < 6; i++)
        {
            int n = i;
            if (LeftCells != null && i < LeftCells.Length && LeftCells[i] != null)
            { LeftCells[i].onClick.RemoveAllListeners(); LeftCells[i].onClick.AddListener(() => Pick(true, n)); }
            if (RightCells != null && i < RightCells.Length && RightCells[i] != null)
            { RightCells[i].onClick.RemoveAllListeners(); RightCells[i].onClick.AddListener(() => Pick(false, n)); }
        }
    }

    private void Pick(bool left, int cell)
    {
        if (!IsGameRunning || questionResolved || (left ? leftDone : rightDone)) return;
        int progress = left ? leftProgress : rightProgress;
        bool correct = progress < sequence.Length && sequence[progress] == cell;
        if (left) leftProgress++; else rightProgress++;
        SetInteractable(left ? LeftCells : RightCells, cell, false);
        SetText(left ? LeftLabels : RightLabels, cell, correct ? "✓" : "×");
        if (!correct) { if (left) leftDone = true; else rightDone = true; }
        else if (progress + 1 >= sequence.Length)
        {
            if (left) { leftDone = true; AddLeftScore(1); }
            else { rightDone = true; AddRightScore(1); }
        }
        if (leftDone && rightDone) ResolveRound();
    }

    private void ResolveRound()
    {
        if (questionResolved) return;
        questionResolved = true; StopQuestionTimer();
        ScheduleNextQuestion(() => { MakeSequence(); RenderPreview(); preview = StartCoroutine(HidePreview()); });
    }

    protected override void OnQuestionTimeout() { leftDone = rightDone = true; ResolveRound(); }
    private void SetText(Text[] a, int i, string s) { if (a != null && i < a.Length && a[i] != null) a[i].text = s; }
    private void SetInteractable(TouchUIButton[] a, int i, bool b) { if (a != null && i < a.Length && a[i] != null) a[i].interactable = b; }
}
