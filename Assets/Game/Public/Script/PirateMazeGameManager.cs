using UnityEngine;
using UnityEngine.UI;

/// <summary>第4关：双方各自控制船只穿过迷宫，避开岩石/木箱并到达宝箱。</summary>
public class PirateMazeGameManager : BaseGameManager
{
    private enum Tile { Empty, Rock, Crate, Treasure }

    [System.Serializable]
    public class SideBoard
    {
        public TouchUIButton[] Cells = new TouchUIButton[20];
        public Text[] Labels = new Text[20];
        public TouchUIButton UpButton, DownButton, LeftButton, RightButton;
    }

    [Header("左右迷宫")]
    public SideBoard LeftBoard = new SideBoard();
    public SideBoard RightBoard = new SideBoard();
    [Min(4)] public int Columns = 4;
    [Min(4)] public int Rows = 5;
    public Text HintText;

    private Tile[,] leftMap, rightMap;
    private Vector2Int leftPlayer, rightPlayer, leftTreasure, rightTreasure;
    private bool leftWon, rightWon;

    protected override void PrepareGameForReadyGo()
    {
        GenerateMaps();
        Render(LeftBoard, leftMap, leftPlayer);
        Render(RightBoard, rightMap, rightPlayer);
    }

    protected override void InitGame()
    {
        Bind(LeftBoard, true);
        Bind(RightBoard, false);
        if (HintText != null) HintText.text = "避开障碍，找到宝箱";
        StartQuestionTimer();
    }

    private void GenerateMaps()
    {
        leftMap = MakeMap(out leftPlayer, out leftTreasure);
        rightMap = MakeMap(out rightPlayer, out rightTreasure);
        leftWon = rightWon = false;
    }

    private Tile[,] MakeMap(out Vector2Int start, out Vector2Int treasure)
    {
        Columns = Mathf.Max(4, Columns);
        Rows = Mathf.Max(4, Rows);
        Tile[,] map = new Tile[Columns, Rows];
        start = Vector2Int.zero;
        treasure = new Vector2Int(Columns - 1, Rows - 1);

        // 确保 x=0 竖线与 y=Rows-1 横线畅通，因此始终存在一条到宝箱的路线。
        for (int x = 0; x < Columns; x++)
            for (int y = 0; y < Rows; y++)
                if (x != 0 && y != Rows - 1 && Random.value < .22f)
                    map[x, y] = Random.value < .5f ? Tile.Rock : Tile.Crate;

        map[start.x, start.y] = Tile.Empty;
        map[treasure.x, treasure.y] = Tile.Treasure;
        return map;
    }

    private void Bind(SideBoard b, bool left)
    {
        if (b == null) return;
        AddMoveListener(b.UpButton, left, Vector2Int.up);
        AddMoveListener(b.DownButton, left, Vector2Int.down);
        AddMoveListener(b.LeftButton, left, Vector2Int.left);
        AddMoveListener(b.RightButton, left, Vector2Int.right);
        if (b.Cells != null)
            for (int i = 0; i < b.Cells.Length; i++)
            {
                int n = i;
                if (b.Cells[i] == null) continue;
                b.Cells[i].onClick.RemoveAllListeners();
                b.Cells[i].onClick.AddListener(() => Move(left, CellPosition(n)));
            }
    }

    private void AddMoveListener(TouchUIButton button, bool left, Vector2Int direction)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => Move(left, direction));
    }

    private Vector2Int CellPosition(int index) => new Vector2Int(index % Columns, index / Columns);

    private void Move(bool left, Vector2Int delta)
    {
        if (!IsGameRunning || (left ? leftWon : rightWon)) return;
        Vector2Int current = left ? leftPlayer : rightPlayer;
        Vector2Int target = current + delta;
        Tile[,] map = left ? leftMap : rightMap;
        if (target.x < 0 || target.x >= Columns || target.y < 0 || target.y >= Rows) return;
        if (map[target.x, target.y] == Tile.Rock || map[target.x, target.y] == Tile.Crate) return;

        if (left) leftPlayer = target; else rightPlayer = target;
        Render(left ? LeftBoard : RightBoard, map, target);

        if (target == (left ? leftTreasure : rightTreasure))
        {
            if (left) { leftWon = true; AddLeftScore(1); }
            else { rightWon = true; AddRightScore(1); }
            if (leftWon && rightWon) ResolveRound();
        }
    }

    private void Render(SideBoard b, Tile[,] map, Vector2Int player)
    {
        if (b == null || b.Labels == null) return;
        for (int i = 0; i < b.Labels.Length; i++)
        {
            if (b.Labels[i] == null) continue;
            Vector2Int p = CellPosition(i);
            if (p.x >= Columns || p.y >= Rows) { b.Labels[i].text = ""; continue; }
            string symbol = map[p.x, p.y] == Tile.Rock ? "岩" :
                map[p.x, p.y] == Tile.Crate ? "箱" :
                map[p.x, p.y] == Tile.Treasure ? "宝" : "";
            if (p == player) symbol = "船";
            b.Labels[i].text = symbol;
        }
    }

    private void ResolveRound()
    {
        if (questionResolved) return;
        questionResolved = true;
        StopQuestionTimer();
        ScheduleNextQuestion(() => { GenerateMaps(); Render(LeftBoard, leftMap, leftPlayer); Render(RightBoard, rightMap, rightPlayer); StartQuestionTimer(); });
    }

    protected override void OnQuestionTimeout() { leftWon = rightWon = true; ResolveRound(); }
}
