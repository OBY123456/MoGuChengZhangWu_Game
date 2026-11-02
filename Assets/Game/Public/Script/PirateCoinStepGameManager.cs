using UnityEngine;
using UnityEngine.UI;

/// <summary>第3关特殊玩法：踩中央道具计分，不按题目轮次强制切题。</summary>
public class PirateCoinStepGameManager : BaseGameManager
{
    public enum ItemType { Empty, Coin, Bomb }

    [Header("中央道具格")]
    public TouchUIButton[] ItemButtons = new TouchUIButton[24];
    public Text[] ItemLabels = new Text[24];
    [Range(0f, 1f)] public float BombChance = .2f;
    public Text HintText;

    private ItemType[] items;
    private bool[] leftClaimed, rightClaimed;

    protected override void PrepareGameForReadyGo() { CreateBoard(); RenderBoard(); }
    protected override void InitGame() { BindButtons(); }

    private void CreateBoard()
    {
        int count = ItemButtons != null && ItemButtons.Length > 0 ? ItemButtons.Length : 24;
        items = new ItemType[count];
        leftClaimed = new bool[count];
        rightClaimed = new bool[count];
        for (int i = 0; i < count; i++)
        {
            float r = Random.value;
            items[i] = r < BombChance ? ItemType.Bomb : r < BombChance + .55f ? ItemType.Coin : ItemType.Empty;
        }
    }

    private void RenderBoard()
    {
        if (ItemLabels != null)
            for (int i = 0; i < ItemLabels.Length && i < items.Length; i++)
                if (ItemLabels[i] != null)
                    ItemLabels[i].text = items[i] == ItemType.Coin ? "金币" : items[i] == ItemType.Bomb ? "炸弹" : "";
        if (HintText != null) HintText.text = "踩金币 +1，踩炸弹 -1";
    }

    private void BindButtons()
    {
        if (ItemButtons == null) return;
        for (int i = 0; i < ItemButtons.Length; i++)
        {
            int index = i;
            if (ItemButtons[i] == null) continue;
            ItemButtons[i].onClick.RemoveAllListeners();
            // 仅作为鼠标/触屏测试入口；真实双人输入请分别调用 OnLeftStep / OnRightStep。
            ItemButtons[i].onClick.AddListener(() => OnLeftStep(index));
        }
    }

    public void OnLeftStep(int cellIndex) { ApplyStep(cellIndex, true); }
    public void OnRightStep(int cellIndex) { ApplyStep(cellIndex, false); }

    private void ApplyStep(int index, bool left)
    {
        if (!IsGameRunning || items == null || index < 0 || index >= items.Length) return;
        bool[] claimed = left ? leftClaimed : rightClaimed;
        if (claimed[index]) return;
        claimed[index] = true;

        if (items[index] == ItemType.Coin) { if (left) AddLeftScore(1); else AddRightScore(1); }
        else if (items[index] == ItemType.Bomb) { if (left) AddLeftScore(-1); else AddRightScore(-1); }

        bool exhausted = true;
        for (int i = 0; i < items.Length; i++)
            if (items[i] != ItemType.Empty && (!leftClaimed[i] || !rightClaimed[i])) { exhausted = false; break; }

        if (exhausted) { CreateBoard(); RenderBoard(); }
    }
}
