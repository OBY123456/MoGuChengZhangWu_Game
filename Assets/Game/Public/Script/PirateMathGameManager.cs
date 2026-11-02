using UnityEngine;
using UnityEngine.UI;

/// <summary>第1关：算式题，左右两边独立选择三个答案，答对得1分。</summary>
public class PirateMathGameManager : BaseGameManager
{
    public Text QuestionText;
    public TouchUIButton[] LeftAnswerButtons = new TouchUIButton[3];
    public TouchUIButton[] RightAnswerButtons = new TouchUIButton[3];
    public Text[] LeftAnswerTexts = new Text[3];
    public Text[] RightAnswerTexts = new Text[3];
    [Min(1)] public int MaxOperand = 9;

    private int correctAnswer;
    private bool leftAnswered, rightAnswered;

    protected override void PrepareGameForReadyGo() { CreateQuestion(); }
    protected override void InitGame() { BindButtons(); StartQuestionTimer(); }

    private void BindButtons()
    {
        for (int i = 0; i < 3; i++)
        {
            int n = i;
            if (LeftAnswerButtons != null && i < LeftAnswerButtons.Length && LeftAnswerButtons[i] != null)
            { LeftAnswerButtons[i].onClick.RemoveAllListeners(); LeftAnswerButtons[i].onClick.AddListener(() => Choose(true, n)); }
            if (RightAnswerButtons != null && i < RightAnswerButtons.Length && RightAnswerButtons[i] != null)
            { RightAnswerButtons[i].onClick.RemoveAllListeners(); RightAnswerButtons[i].onClick.AddListener(() => Choose(false, n)); }
        }
    }

    private void CreateQuestion()
    {
        int a = Random.Range(1, MaxOperand + 1), b = Random.Range(1, MaxOperand + 1);
        bool subtract = Random.value < .25f;
        if (subtract && b > a) { int t = a; a = b; b = t; }
        correctAnswer = subtract ? a - b : a + b;
        if (QuestionText != null) QuestionText.text = subtract ? $"{a} - {b} = ?" : $"{a} + {b} = ?";
        int correctSlot = Random.Range(0, 3);
        int[] values = new int[3];
        values[correctSlot] = correctAnswer;
        for (int i = 0; i < 3; i++)
        {
            if (i == correctSlot) continue;
            int candidate;
            do { candidate = Mathf.Max(0, correctAnswer + Random.Range(-4, 5)); }
            while (candidate == correctAnswer || Contains(values, candidate));
            values[i] = candidate;
        }
        for (int i = 0; i < 3; i++)
        {
            if (LeftAnswerTexts != null && i < LeftAnswerTexts.Length && LeftAnswerTexts[i] != null) LeftAnswerTexts[i].text = values[i].ToString();
            if (RightAnswerTexts != null && i < RightAnswerTexts.Length && RightAnswerTexts[i] != null) RightAnswerTexts[i].text = values[i].ToString();
            if (LeftAnswerButtons != null && i < LeftAnswerButtons.Length && LeftAnswerButtons[i] != null) LeftAnswerButtons[i].interactable = true;
            if (RightAnswerButtons != null && i < RightAnswerButtons.Length && RightAnswerButtons[i] != null) RightAnswerButtons[i].interactable = true;
        }
        leftAnswered = rightAnswered = false;
    }

    private bool Contains(int[] values, int value)
    { foreach (int v in values) if (v == value) return true; return false; }

    private void Choose(bool left, int index)
    {
        if (!IsGameRunning || questionResolved) return;
        if (left ? leftAnswered : rightAnswered) return;
        Text[] labels = left ? LeftAnswerTexts : RightAnswerTexts;
        if (labels == null || index >= labels.Length || labels[index] == null) return;
        bool correct = int.TryParse(labels[index].text, out int answer) && answer == correctAnswer;
        if (left) leftAnswered = true; else rightAnswered = true;
        TouchUIButton[] buttons = left ? LeftAnswerButtons : RightAnswerButtons;
        if (buttons != null && index < buttons.Length && buttons[index] != null) buttons[index].interactable = false;
        if (correct) { if (left) AddLeftScore(1); else AddRightScore(1); }
        if (leftAnswered && rightAnswered) ResolveQuestion();
    }

    private void ResolveQuestion()
    {
        if (questionResolved) return;
        questionResolved = true;
        StopQuestionTimer();
        ScheduleNextQuestion(() => { CreateQuestion(); StartQuestionTimer(); });
    }

    protected override void OnQuestionTimeout() { ResolveQuestion(); }
    protected override void CreateNextQuestion() { CreateQuestion(); StartQuestionTimer(); }
}
