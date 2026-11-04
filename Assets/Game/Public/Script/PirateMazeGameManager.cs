using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Lean.Pool;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>第4关：双方独立出题、移动、计分与切题；无人操作时各自轮换地图展示。</summary>
public class PirateMazeGameManager : BaseGameManager
{
    [System.Serializable]
    public class SideBoard
    {
        [Tooltip("填写全部格子；长度必须等于Columns×Rows，运行时按布局实际位置校正为从左上逐行排列。")]
        public RectTransform[] Cells = new RectTransform[20];
        public TouchUIButton UpButton, DownButton, LeftButton, RightButton;
    }

    [Header("左右迷宫")]
    public SideBoard LeftBoard = new SideBoard();
    public SideBoard RightBoard = new SideBoard();
    [Min(4)] public int Columns = 4;
    [Min(4)] public int Rows = 5;

    [Tooltip("玩家预制体，0 左边，1 右边；Image 默认朝右，零旋转。")]
    public GameObject[] Player = new GameObject[2];
    [Tooltip("得分道具预制体；保留预制体Width和Height，随机选取，常驻时呼吸，拾取后飞入本方分数再加10分。可留空。")]
    public GameObject[] ScoreItem;
    [Tooltip("障碍物预制体；禁止通过，每张地图至少生成每种障碍物一次。")]
    public GameObject[] ObstacleItem;
    [Tooltip("终点预制体；弧线飞向本方分数，旋转并在后半程缩小；到达后加100分。")]
    public GameObject Goal;
    [Tooltip("起点和终点背景预制体；位于玩家、宝箱和其他道具下方。")]
    public GameObject BG;
    [Tooltip("全部生成物体的公共UI父物体；不能带布局组件，也不能放在会裁切宝箱飞行的Mask内。")]
    public RectTransform Parent;

    [Header("地图生成")]
    [Tooltip("每侧障碍数量；实际值至少等于障碍种类数，且不会占用保证可通行的路径。")]
    [Min(0)] public int ObstaclesPerBoard = 6;
    [Tooltip("每侧得分道具数量；只放在玩家能够到达的空格上。")]
    [Min(0)] public int ScoreItemsPerBoard = 3;
    [Tooltip("期望的最短通路步数；优先生成需要绕行的障碍屏障，空间不足时自动降低。")]
    [Min(2)] public int MinimumPathSteps = 5;

    [Header("生成物体相对格子的尺寸")]
    [Range(0.1f, 1f)] public float PlayerCellRatio = 0.84f;
    [Range(0.1f, 1f)] public float GoalCellRatio = 0.85f;
    [Range(0.1f, 1f)] public float ObstacleCellRatio = 0.86f;

    [Header("移动：先转向，再前进一步")]
    [Min(0.01f)] public float TurnDuration = 0.16f;
    [Min(0.01f)] public float MoveDuration = 0.22f;
    [Min(0.01f)] public float BlockedFeedbackDuration = 0.16f;

    [Header("入场：参考藏宝图关卡的由左到右交错回弹")]
    [Min(0.01f)] public float BoardAppearDuration = 0.85f;
    [Min(0.01f)] public float ButtonAppearDuration = 0.55f;
    [Min(0.01f)] public float WaveStaggerInterval = 0.13f;
    [Range(0f, 0.9f)] public float WaveStartRatio = 0.45f;
    [Range(0.1f, 1f)] public float AppearStartScale = 0.6f;
    [Range(1f, 1.3f)] public float BouncePeakScale = 1.12f;
    [Min(1f)] public float BackOvershoot = 1.5f;

    [Header("得分道具呼吸")]
    [Min(0.1f)] public float ScoreBreathDuration = 0.9f;
    [Range(0f, 0.2f)] public float ScoreBreathAmplitude = 0.08f;

    [Header("普通得分道具与宝箱飞入分数")]
    [Min(0.1f)] public float GoalFlyDuration = 0.8f;
    [Tooltip("弧线相对于画面起终点连线的向上抬升量，以屏幕像素计。")]
    [Min(0f)] public float GoalArcHeight = 160f;
    [Min(0f)] public float GoalSpinTurns = 1f;

    private const int ItemPoints = 10;
    private const int GoalPoints = 100;

    private sealed class MapData
    {
        public int Start;
        public int Finish;
        public int[] Obstacles;
        public bool[] Scores;
        public int SolutionSteps;
        public bool HasDetour;
    }

    private sealed class PooledView
    {
        public GameObject Object;
        public RectTransform Rect;
        public int Side;
        public int Cell;
        public float SizeRatio;
        public bool PreservePrefabSize;
        public Vector2 PrefabSize;
        public bool FollowCell = true;
        public Tween Breathing;
    }

    private sealed class ScoreFlight
    {
        public PooledView View;
        public int Side;
        public int Version;
        public bool Protected;
        public bool Awarded;
        public Sequence Tween;
    }

    private struct DetourPlan
    {
        public bool Horizontal;
        public bool Reverse;
        public int Gap;
        public int From;
        public int To;
    }

    private sealed class SideState
    {
        public MapData Map;
        public PooledView Ship;
        public PooledView Treasure;
        public PooledView[] Coins;
        public int Cell;
        public bool Busy;
        public bool MoveProtected;
        public Sequence Motion;
        public bool Ready;
        public bool InputEnabled;
        public bool Resolved;
        public bool GoalProtected;
        public int Version;
        public float IdleElapsed;
        public Coroutine EntranceRoutine;
        public Coroutine NextQuestionRoutine;
        public Sequence Entrance;
        public Sequence GoalFlight;
        public Vector3 FacingScale = Vector3.one;
        public float FacingAngle;
        public readonly List<ScoreFlight> ItemFlights = new List<ScoreFlight>();
    }

    private struct EntranceItem
    {
        public RectTransform Rect;
        public int Side;
        public int Action;
    }

    private readonly SideState[] sides = { new SideState(), new SideState() };
    private readonly List<PooledView> views = new List<PooledView>(64);
    private readonly int[,] buttonWaveIndices = new int[2, 4];
    private readonly Vector3[,] buttonScales = new Vector3[2, 4];
    private readonly Vector3[] worldCorners = new Vector3[4];
    private readonly Camera[] cellCameras = new Camera[2];
    private Camera parentCamera;
    private bool scalesCached, buttonsBound, configurationValid, configurationFailed, ending;
    private float inactivityDuration;
    private int screenWidth, screenHeight;
    private Matrix4x4 parentMatrix;
    private Vector2 parentSize;
    private Vector3 leftFirstPosition, leftLastPosition, rightFirstPosition, rightLastPosition;
    private Vector2 leftFirstSize, leftLastSize, rightFirstSize, rightLastSize;

    protected override void Awake()
    {
        base.Awake();
        CacheButtonScales();
        BindButtons();
        DisableAllButtons();
        HideIntroButtons();
    }

    protected override void PrepareGameForReadyGo()
    {
        ending = false;
        // 无操作换题使用独立的QuestionDuration，不读取或重置全局GameDuration计时。
        inactivityDuration = Mathf.Max(0.1f, QuestionDuration);
        questionTimerRunning = false;
        GenerateMaps();
    }

    protected override bool IsPreparationVisualReady() => configurationFailed || (sides[0].Ready && sides[1].Ready);

    protected override void InitGame()
    {
        questionTimerRunning = false;
        if (!configurationValid || sides[0].Map == null || sides[1].Map == null)
        {
            Debug.LogError("迷宫：配置不完整，无法开始，请修正前面的配置错误。", this);
            GameEnd(DetermineResult());
            return;
        }
        OpenSideQuestionInput(0);
        OpenSideQuestionInput(1);
    }

    protected override void Update()
    {
        if (!IsGameRunning) return;
        // 全局计时始终由基类推进，到点无条件进入结算；无操作换题不显示倒计时UI。
        questionTimerRunning = false;
        base.Update();
        // 基类先处理整局结束；结算或等待已提交动作时，禁止再生成任何新题。
        if (!IsGameRunning || ending) return;
        UpdateSideInactivity(Time.deltaTime);
    }

    protected override void RefreshTimerUI()
    {
        if (GameTimerText == null) return;
        int seconds = Mathf.CeilToInt(Mathf.Max(0f, GetRemainingTime()));
        GameTimerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void UpdateSideInactivity(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        for (int side = 0; side < 2; side++)
        {
            SideState state = sides[side];
            if (!state.Ready || !state.InputEnabled || state.Resolved || state.Map == null) continue;
            state.IdleElapsed = Mathf.Min(inactivityDuration, state.IdleElapsed + deltaTime);
            if (state.IdleElapsed < inactivityDuration || state.Busy || state.MoveProtected || state.GoalProtected || state.ItemFlights.Count > 0) continue;
            GenerateSideQuestion(side, false);
        }
    }

    private void RecordMovement(int side)
    {
        sides[side].IdleElapsed = 0f;
    }

    private void GenerateMaps()
    {
        ClearAllQuestions();
        CacheButtonScales();
        BindButtons();
        configurationFailed = !ValidateConfiguration();
        if (configurationFailed)
        {
            RestoreButtonScales();
            return;
        }
        configurationValid = true;
        RebuildBoardLayouts();
        NormalizeCellOrder(0);
        NormalizeCellOrder(1);
        RefreshCameras();
        CacheButtonWaveOrder();
        GenerateSideQuestion(0, true);
        GenerateSideQuestion(1, true);
        if (configurationValid) CacheLayoutState();
    }

    private void GenerateSideQuestion(int side, bool animateButtons)
    {
        if (!configurationValid || ending || (!IsGameRunning && !IsPreparing)) return;
        SideState state = sides[side];
        int previousStart = state.Map != null ? state.Map.Start : -1;
        MapData otherMap = sides[1 - side].Map;
        int excludedStart = otherMap != null ? otherMap.Start : -1;
        ClearSideQuestion(side);
        try
        {
            state.Map = BuildMap(excludedStart, previousStart);
            RebuildSideLayout(side);
            RefreshCameras();
            SpawnBoard(side);
            if (animateButtons) HideSideButtons(side);
            else RestoreSideButtonScales(side);
            state.EntranceRoutine = StartCoroutine(PlayEntranceNextFrame(side, state.Version, animateButtons));
            CacheLayoutState();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
            ClearSideQuestion(side);
            configurationValid = false;
            configurationFailed = true;
            RestoreButtonScales();
            if (IsGameRunning) GameEnd(DetermineResult());
        }
    }

    private bool ValidateConfiguration()
    {
        if (Columns < 4 || Rows < 4) return ConfigurationError("Columns和Rows都必须至少为4。");
        long count = (long)Columns * Rows;
        if (count > int.MaxValue) return ConfigurationError("棋盘尺寸过大。");
        if (Parent == null || Parent.GetComponentInParent<Canvas>() == null) return ConfigurationError("Parent必须是Canvas下的RectTransform。");
        if (!HasUiCamera(Parent)) return ConfigurationError("Parent所属Canvas需要指定渲染相机，或使用Overlay模式。");
        if (Parent.GetComponent<LayoutGroup>() != null) return ConfigurationError("Parent不能挂LayoutGroup，请使用独立的UI覆盖层。");
        if (leftScoreText == null || rightScoreText == null) return ConfigurationError("请绑定左右分数文本，作为宝箱飞行的目标。");
        if (!HasUiCamera(leftScoreText.rectTransform) || !HasUiCamera(rightScoreText.rectTransform)) return ConfigurationError("分数文本所属Canvas缺少渲染相机。");
        if (Player == null || Player.Length < 2 || !IsUiPrefab(Player[0]) || !IsUiPrefab(Player[1])) return ConfigurationError("Player需要两个带RectTransform和Image的预制体。");
        if (!IsUiPrefab(Goal) || !IsUiPrefab(BG)) return ConfigurationError("Goal和BG都需要带RectTransform和Image的预制体。");
        HashSet<RectTransform> cells = new HashSet<RectTransform>();
        HashSet<TouchUIButton> buttons = new HashSet<TouchUIButton>();
        for (int side = 0; side < 2; side++)
        {
            SideBoard board = GetBoard(side);
            if (board == null || board.Cells == null || board.Cells.Length != (int)count) return ConfigurationError("每侧Cells长度必须等于Columns×Rows。");
            for (int i = 0; i < board.Cells.Length; i++)
            {
                RectTransform cell = board.Cells[i];
                if (cell == null || cell.GetComponentInParent<Canvas>() == null || !cells.Add(cell)) return ConfigurationError("Cells不能有空格子、重复格子或没有Canvas的格子。");
                if (!HasUiCamera(cell)) return ConfigurationError("棋盘Canvas需要指定渲染相机，或使用Overlay模式。");
                if (cell == Parent || Parent.IsChildOf(cell)) return ConfigurationError("Parent不能是棋盘格子本身，也不能放在单个格子内部。");
            }
            for (int action = 0; action < 4; action++)
            {
                TouchUIButton button = GetButton(side, action);
                if (button == null || !(button.transform is RectTransform) || !buttons.Add(button)) return ConfigurationError("双方8个方向按钮必须完整、互不重复且是UI物体。");
            }
        }
        int kinds = ObstacleItem != null ? ObstacleItem.Length : 0;
        if (kinds > (int)count - 3) return ConfigurationError("障碍种类过多，无法同时保留起点、终点和通路。");
        for (int i = 0; i < kinds; i++)
        {
            if (!IsUiPrefab(ObstacleItem[i])) return ConfigurationError("ObstacleItem不能有空元素，且必须有RectTransform和Image。");
        }
        if (ScoreItem != null)
        {
            for (int i = 0; i < ScoreItem.Length; i++)
            {
                if (!IsUiPrefab(ScoreItem[i])) return ConfigurationError("ScoreItem不能有空元素，且必须有RectTransform和Image。");
            }
        }
        return true;
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("迷宫配置：" + message, this);
        return false;
    }

    private static bool IsUiPrefab(GameObject prefab) => prefab != null && prefab.GetComponent<RectTransform>() != null && prefab.GetComponent<Image>() != null;
    private SideBoard GetBoard(int side) => side == 0 ? LeftBoard : RightBoard;
    private Vector2Int CellPosition(int index) => new Vector2Int(index % Columns, index / Columns);
    private bool IsEdge(int index) => index % Columns == 0 || index % Columns == Columns - 1 || index / Columns == 0 || index / Columns == Rows - 1;
    private int Manhattan(int first, int second) => Mathf.Abs(first % Columns - second % Columns) + Mathf.Abs(first / Columns - second / Columns);

    private MapData BuildMap(int excludedStart, int previousStart)
    {
        int total = Columns * Rows;
        int kinds = ObstacleItem != null ? ObstacleItem.Length : 0;
        MapData map = new MapData { Obstacles = new int[total], Scores = new bool[total] };
        for (int i = 0; i < total; i++) map.Obstacles[i] = -1;
        bool[] reserved = new bool[total];
        List<int> forced = new List<int>();
        bool detour = kinds > 0 && (TryBuildBarrierDetour(map, excludedStart, previousStart, reserved, forced) || TryBuildSingleBlockDetour(map, excludedStart, previousStart, reserved, forced));
        if (!detour)
        {
            // 障碍种类极多、只剩很少空位时，优先满足每种障碍一次及终点可达。
            PickEndpoints(map, excludedStart, previousStart, kinds);
            ReserveLine(map.Start, map.Finish, reserved);
        }
        List<int> available = new List<int>(total);
        for (int i = 0; i < total; i++)
        {
            if (!reserved[i] && !forced.Contains(i)) available.Add(i);
        }
        Shuffle(forced);
        Shuffle(available);
        List<int> placements = new List<int>(forced);
        placements.AddRange(available);
        int obstacleCount = kinds > 0 ? Mathf.Min(placements.Count, Mathf.Max(kinds, Mathf.Max(ObstaclesPerBoard, forced.Count))) : 0;
        for (int i = 0; i < obstacleCount; i++) map.Obstacles[placements[i]] = i < kinds ? i : Random.Range(0, kinds);
        map.SolutionSteps = GetShortestDistance(map);
        map.HasDetour = map.SolutionSteps > Manhattan(map.Start, map.Finish);
        if (map.SolutionSteps < 0 || (detour && !map.HasDetour)) throw new System.InvalidOperationException("迷宫生成失败：终点不可达或障碍没有形成绕行。");
        List<int> reachable = FindReachableCells(map);
        reachable.Remove(map.Start);
        reachable.Remove(map.Finish);
        Shuffle(reachable);
        int scoreCount = ScoreItem != null && ScoreItem.Length > 0 ? Mathf.Min(Mathf.Max(0, ScoreItemsPerBoard), reachable.Count) : 0;
        for (int i = 0; i < scoreCount; i++) map.Scores[reachable[i]] = true;
        return map;
    }

    private int OrientedIndex(bool horizontal, int x, int y) => horizontal ? y * Columns + x : x * Columns + y;

    private bool TryBuildBarrierDetour(MapData map, int excludedStart, int previousStart, bool[] reserved, List<int> forced)
    {
        int kinds = ObstacleItem.Length;
        int requested = Mathf.Max(kinds, ObstaclesPerBoard);
        int candidates = 0;
        DetourPlan plan = new DetourPlan();
        for (int pass = 0; pass < 2 && candidates == 0; pass++)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                bool horizontal = axis == 0;
                int across = horizontal ? Columns : Rows;
                int along = horizontal ? Rows : Columns;
                int required = Mathf.Max(kinds, across - 1);
                if (requested < across - 1) continue;
                for (int gap = 0; gap < across; gap++)
                {
                    for (int from = 0; from < across; from++)
                    {
                        for (int to = 0; to < across; to++)
                        {
                            if (!((from < gap && to < gap) || (from > gap && to > gap))) continue;
                            int steps = along - 1 + Mathf.Abs(from - gap) + Mathf.Abs(to - gap);
                            if (steps + 1 + required > Columns * Rows || (pass == 0 && steps < MinimumPathSteps)) continue;
                            for (int reverse = 0; reverse < 2; reverse++)
                            {
                                int start = reverse == 0 ? OrientedIndex(horizontal, from, 0) : OrientedIndex(horizontal, to, along - 1);
                                if (start == excludedStart || start == previousStart) continue;
                                candidates++;
                                if (Random.Range(0, candidates) != 0) continue;
                                plan = new DetourPlan { Horizontal = horizontal, Reverse = reverse != 0, Gap = gap, From = from, To = to };
                            }
                        }
                    }
                }
            }
        }
        if (candidates == 0) return false;
        int width = plan.Horizontal ? Columns : Rows;
        int height = plan.Horizontal ? Rows : Columns;
        int first = OrientedIndex(plan.Horizontal, plan.From, 0);
        int last = OrientedIndex(plan.Horizontal, plan.To, height - 1);
        map.Start = plan.Reverse ? last : first;
        map.Finish = plan.Reverse ? first : last;
        int openingStart = OrientedIndex(plan.Horizontal, plan.Gap, 0);
        int openingEnd = OrientedIndex(plan.Horizontal, plan.Gap, height - 1);
        ReserveLine(first, openingStart, reserved);
        ReserveLine(openingStart, openingEnd, reserved);
        ReserveLine(openingEnd, last, reserved);
        int wall = Random.Range(1, height - 1);
        for (int x = 0; x < width; x++)
        {
            if (x != plan.Gap) forced.Add(OrientedIndex(plan.Horizontal, x, wall));
        }
        return true;
    }

    private bool TryBuildSingleBlockDetour(MapData map, int excludedStart, int previousStart, bool[] reserved, List<int> forced)
    {
        int candidates = 0;
        DetourPlan plan = new DetourPlan();
        for (int pass = 0; pass < 2 && candidates == 0; pass++)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                bool horizontal = axis == 0;
                int width = horizontal ? Columns : Rows;
                int height = horizontal ? Rows : Columns;
                int steps = width + 1;
                if (steps + 1 + ObstacleItem.Length > Columns * Rows || (pass == 0 && steps < MinimumPathSteps)) continue;
                for (int line = 0; line < height; line++)
                {
                    for (int reverse = 0; reverse < 2; reverse++)
                    {
                        int start = OrientedIndex(horizontal, reverse == 0 ? 0 : width - 1, line);
                        if (start == excludedStart || start == previousStart) continue;
                        candidates++;
                        if (Random.Range(0, candidates) != 0) continue;
                        plan = new DetourPlan { Horizontal = horizontal, Reverse = reverse != 0, Gap = line };
                    }
                }
            }
        }
        if (candidates == 0) return false;
        int across = plan.Horizontal ? Columns : Rows;
        int along = plan.Horizontal ? Rows : Columns;
        int bypass = plan.Gap == 0 ? 1 : plan.Gap == along - 1 ? along - 2 : plan.Gap + (Random.Range(0, 2) == 0 ? -1 : 1);
        int first = OrientedIndex(plan.Horizontal, 0, plan.Gap);
        int last = OrientedIndex(plan.Horizontal, across - 1, plan.Gap);
        map.Start = plan.Reverse ? last : first;
        map.Finish = plan.Reverse ? first : last;
        int middleStart = OrientedIndex(plan.Horizontal, 0, bypass);
        int middleEnd = OrientedIndex(plan.Horizontal, across - 1, bypass);
        ReserveLine(first, middleStart, reserved);
        ReserveLine(middleStart, middleEnd, reserved);
        ReserveLine(middleEnd, last, reserved);
        forced.Add(OrientedIndex(plan.Horizontal, Random.Range(1, across - 1), plan.Gap));
        return true;
    }

    private void ReserveLine(int first, int last, bool[] reserved)
    {
        int current = first;
        reserved[current] = true;
        while (current != last)
        {
            Vector2Int from = CellPosition(current);
            Vector2Int to = CellPosition(last);
            if (from.x != to.x) current += from.x < to.x ? 1 : -1;
            else current += from.y < to.y ? Columns : -Columns;
            reserved[current] = true;
        }
    }

    private int GetShortestDistance(MapData map)
    {
        int[] distance = new int[map.Obstacles.Length];
        for (int i = 0; i < distance.Length; i++) distance[i] = -1;
        Queue<int> queue = new Queue<int>();
        distance[map.Start] = 0;
        queue.Enqueue(map.Start);
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            if (cell == map.Finish) return distance[cell];
            Vector2Int position = CellPosition(cell);
            for (int action = 0; action < 4; action++)
            {
                int x = position.x + (action == 0 ? 1 : action == 1 ? -1 : 0);
                int y = position.y + (action == 2 ? 1 : action == 3 ? -1 : 0);
                if (x < 0 || x >= Columns || y < 0 || y >= Rows) continue;
                int next = y * Columns + x;
                if (distance[next] >= 0 || map.Obstacles[next] >= 0) continue;
                distance[next] = distance[cell] + 1;
                queue.Enqueue(next);
            }
        }
        return -1;
    }

    private void PickEndpoints(MapData map, int excludedStart, int previousStart, int kinds)
    {
        List<int> edges = new List<int>();
        for (int i = 0; i < Columns * Rows; i++)
        {
            if (IsEdge(i)) edges.Add(i);
        }
        int maxDistance = Mathf.Min(Columns + Rows - 2, Columns * Rows - kinds - 1);
        int minDistance = Mathf.Clamp(MinimumPathSteps, 2, maxDistance);
        int candidates = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i] == excludedStart || edges[i] == previousStart) continue;
            for (int j = 0; j < edges.Count; j++)
            {
                int distance = Manhattan(edges[i], edges[j]);
                if (distance < minDistance || distance > maxDistance) continue;
                candidates++;
                if (Random.Range(0, candidates) != 0) continue;
                map.Start = edges[i];
                map.Finish = edges[j];
            }
        }
        if (candidates == 0) throw new System.InvalidOperationException("迷宫无法选择合法的边缘起终点。");
    }

    private List<int> FindReachableCells(MapData map)
    {
        List<int> reachable = new List<int>();
        Queue<int> queue = new Queue<int>();
        bool[] visited = new bool[map.Obstacles.Length];
        visited[map.Start] = true;
        queue.Enqueue(map.Start);
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            reachable.Add(cell);
            EnqueueOpenNeighbor(map, cell, Vector2Int.up, visited, queue);
            EnqueueOpenNeighbor(map, cell, Vector2Int.down, visited, queue);
            EnqueueOpenNeighbor(map, cell, Vector2Int.left, visited, queue);
            EnqueueOpenNeighbor(map, cell, Vector2Int.right, visited, queue);
        }
        return reachable;
    }

    private void EnqueueOpenNeighbor(MapData map, int cell, Vector2Int delta, bool[] visited, Queue<int> queue)
    {
        if (!TryNeighbor(cell, delta, out int next) || visited[next] || map.Obstacles[next] >= 0) return;
        visited[next] = true;
        queue.Enqueue(next);
    }

    private bool TryNeighbor(int cell, Vector2Int delta, out int next)
    {
        Vector2Int position = CellPosition(cell);
        int x = position.x + delta.x;
        int y = position.y - delta.y;
        next = -1;
        if (x < 0 || x >= Columns || y < 0 || y >= Rows) return false;
        next = y * Columns + x;
        return true;
    }

    private static void Shuffle(List<int> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int other = Random.Range(0, i + 1);
            int value = values[i];
            values[i] = values[other];
            values[other] = value;
        }
    }

    private void SpawnBoard(int side)
    {
        SideState state = sides[side];
        MapData map = state.Map;
        state.Cell = map.Start;
        state.FacingScale = Vector3.one;
        state.FacingAngle = 0f;
        state.Coins = new PooledView[Columns * Rows];
        SpawnView(BG, side, map.Start, 1f, false);
        SpawnView(BG, side, map.Finish, 1f, false);
        for (int i = 0; i < map.Obstacles.Length; i++)
        {
            if (map.Obstacles[i] >= 0) SpawnView(ObstacleItem[map.Obstacles[i]], side, i, ObstacleCellRatio, true);
            if (map.Scores[i]) state.Coins[i] = SpawnView(ScoreItem[Random.Range(0, ScoreItem.Length)], side, i, 1f, true, preservePrefabSize: true);
        }
        state.Treasure = SpawnView(Goal, side, map.Finish, GoalCellRatio, true);
        state.Ship = SpawnView(Player[side], side, map.Start, PlayerCellRatio, true);
        state.Ship.Rect.SetAsLastSibling();
    }

    private PooledView SpawnView(GameObject prefab, int side, int cell, float sizeRatio, bool preserveAspect, bool preservePrefabSize = false)
    {
        GameObject clone = LeanPool.Spawn(prefab, Vector3.zero, Quaternion.identity, Parent);
        if (clone == null) throw new System.InvalidOperationException("LeanPool无法生成：" + prefab.name + "。请检查池的容量和限制。");
        RectTransform rect = clone.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition3D = Vector3.zero;
        Image image = clone.GetComponent<Image>();
        Image original = prefab.GetComponent<Image>();
        image.sprite = original.sprite;
        image.color = original.color;
        image.enabled = true;
        image.preserveAspect = preserveAspect;
        Graphic[] graphics = clone.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++) graphics[i].raycastTarget = false;
        CanvasGroup[] groups = clone.GetComponentsInChildren<CanvasGroup>(true);
        for (int i = 0; i < groups.Length; i++)
        {
            groups[i].alpha = 1f;
            groups[i].interactable = false;
            groups[i].blocksRaycasts = false;
        }
        LayoutElement element = clone.GetComponent<LayoutElement>();
        if (element != null) element.ignoreLayout = true;
        PooledView view = new PooledView { Object = clone, Rect = rect, Side = side, Cell = cell, SizeRatio = Mathf.Clamp(sizeRatio, 0.1f, 1f), PreservePrefabSize = preservePrefabSize, PrefabSize = prefab.GetComponent<RectTransform>().rect.size };
        views.Add(view);
        AlignView(view);
        return view;
    }

    private void RebuildBoardLayouts()
    {
        RebuildSideLayout(0);
        RebuildSideLayout(1);
    }

    private void NormalizeCellOrder(int side)
    {
        SideBoard board = GetBoard(side);
        List<RectTransform> ordered = new List<RectTransform>(board.Cells);
        ordered.Sort((first, second) => GetScreenCenter(second).y.CompareTo(GetScreenCenter(first).y));
        for (int row = 0; row < Rows; row++)
        {
            List<RectTransform> line = ordered.GetRange(row * Columns, Columns);
            line.Sort((first, second) => GetScreenCenter(first).x.CompareTo(GetScreenCenter(second).x));
            for (int column = 0; column < Columns; column++) board.Cells[row * Columns + column] = line[column];
        }
    }

    private void RebuildSideLayout(int side)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform layout = GetBoard(side).Cells[0].parent as RectTransform;
        if (layout != null) LayoutRebuilder.ForceRebuildLayoutImmediate(layout);
        Canvas.ForceUpdateCanvases();
    }

    private static Camera GetUiCamera(RectTransform rect)
    {
        Canvas canvas = rect != null ? rect.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return null;
        canvas = canvas.rootCanvas;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    private static bool HasUiCamera(RectTransform rect)
    {
        Canvas canvas = rect != null ? rect.GetComponentInParent<Canvas>() : null;
        return canvas != null && (canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay || GetUiCamera(rect) != null);
    }

    private static Vector2 GetScreenCenter(RectTransform rect)
    {
        return RectTransformUtility.WorldToScreenPoint(GetUiCamera(rect), rect.TransformPoint(rect.rect.center));
    }

    private void RefreshCameras()
    {
        parentCamera = GetUiCamera(Parent);
        for (int side = 0; side < 2; side++) cellCameras[side] = GetUiCamera(GetBoard(side).Cells[0]);
    }

    private Vector2 ScreenToParent(Vector2 screen)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Parent, screen, parentCamera, out Vector2 local)) return Vector2.zero;
        // 固定中心锚点的anchoredPosition要减去父Rect的中心，兼容非中心pivot。
        return local - Parent.rect.center;
    }

    private Vector2 GetCellAnchor(int side, int cell)
    {
        RectTransform rect = GetBoard(side).Cells[cell];
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cellCameras[side], rect.TransformPoint(rect.rect.center));
        return ScreenToParent(screen);
    }

    private Vector2 GetCellSize(int side, int cell)
    {
        RectTransform rect = GetBoard(side).Cells[cell];
        rect.GetWorldCorners(worldCorners);
        Vector2 first = ScreenToParent(RectTransformUtility.WorldToScreenPoint(cellCameras[side], worldCorners[0]));
        Vector2 upper = ScreenToParent(RectTransformUtility.WorldToScreenPoint(cellCameras[side], worldCorners[1]));
        Vector2 right = ScreenToParent(RectTransformUtility.WorldToScreenPoint(cellCameras[side], worldCorners[3]));
        return new Vector2(Vector2.Distance(first, right), Vector2.Distance(first, upper));
    }

    private void AlignView(PooledView view)
    {
        if (view == null || view.Object == null || view.Rect == null || !view.FollowCell) return;
        view.Rect.anchoredPosition = GetCellAnchor(view.Side, view.Cell);
        // 得分道具每次都从预制体恢复尺寸，避免对象池复用和布局刷新覆盖原始宽高。
        view.Rect.sizeDelta = view.PreservePrefabSize ? view.PrefabSize : GetCellSize(view.Side, view.Cell) * view.SizeRatio;
    }

    private void LateUpdate()
    {
        if (!configurationValid || Parent == null || !LayoutChanged()) return;
        RefreshCameras();
        for (int i = 0; i < views.Count; i++) AlignView(views[i]);
        CacheLayoutState();
    }

    private bool LayoutChanged()
    {
        if (screenWidth != Screen.width || screenHeight != Screen.height || parentMatrix != Parent.localToWorldMatrix || parentSize != Parent.rect.size) return true;
        RectTransform[] left = LeftBoard.Cells;
        RectTransform[] right = RightBoard.Cells;
        if (leftFirstSize != left[0].rect.size || leftLastSize != left[left.Length - 1].rect.size || rightFirstSize != right[0].rect.size || rightLastSize != right[right.Length - 1].rect.size) return true;
        return leftFirstPosition != left[0].position || leftLastPosition != left[left.Length - 1].position || rightFirstPosition != right[0].position || rightLastPosition != right[right.Length - 1].position;
    }

    private void CacheLayoutState()
    {
        screenWidth = Screen.width;
        screenHeight = Screen.height;
        parentMatrix = Parent.localToWorldMatrix;
        parentSize = Parent.rect.size;
        leftFirstPosition = LeftBoard.Cells[0].position;
        leftLastPosition = LeftBoard.Cells[LeftBoard.Cells.Length - 1].position;
        rightFirstPosition = RightBoard.Cells[0].position;
        rightLastPosition = RightBoard.Cells[RightBoard.Cells.Length - 1].position;
        leftFirstSize = LeftBoard.Cells[0].rect.size;
        leftLastSize = LeftBoard.Cells[LeftBoard.Cells.Length - 1].rect.size;
        rightFirstSize = RightBoard.Cells[0].rect.size;
        rightLastSize = RightBoard.Cells[RightBoard.Cells.Length - 1].rect.size;
    }

    private IEnumerator PlayEntranceNextFrame(int side, int version, bool animateButtons)
    {
        SideState state = sides[side];
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Side == side) views[i].Rect.localScale = Vector3.one * AppearStartScale;
        }
        yield return null;
        if (version != state.Version) yield break;
        state.EntranceRoutine = null;
        if (!isActiveAndEnabled || !configurationValid || state.Map == null) yield break;
        RebuildSideLayout(side);
        RefreshCameras();
        Sequence entrance = DOTween.Sequence().SetUpdate(true).SetAutoKill(true);
        entrance.Pause();
        float boardDuration = Mathf.Max(0.85f, BoardAppearDuration);
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Side != side) continue;
            AlignView(views[i]);
            entrance.Insert(0f, CreateBounceTween(views[i].Rect, Vector3.one, boardDuration));
        }
        if (animateButtons)
        {
            float buttonDuration = Mathf.Max(0.45f, ButtonAppearDuration);
            float step = Mathf.Clamp(WaveStaggerInterval, 0.02f, buttonDuration * 0.65f);
            float waveStart = boardDuration * Mathf.Clamp01(WaveStartRatio);
            for (int action = 0; action < 4; action++)
            {
                RectTransform rect = GetButton(side, action).transform as RectTransform;
                entrance.Insert(waveStart + buttonWaveIndices[side, action] * step, CreateBounceTween(rect, buttonScales[side, action], buttonDuration));
            }
        }
        entrance.OnComplete(() => FinishEntrance(side, version));
        state.Entrance = entrance;
        entrance.Play();
        CacheLayoutState();
    }

    private void CacheButtonWaveOrder()
    {
        List<EntranceItem> items = new List<EntranceItem>(8);
        for (int side = 0; side < 2; side++)
        {
            for (int action = 0; action < 4; action++) items.Add(new EntranceItem { Rect = GetButton(side, action).transform as RectTransform, Side = side, Action = action });
        }
        items.Sort((first, second) => GetScreenCenter(first.Rect).x.CompareTo(GetScreenCenter(second.Rect).x));
        for (int i = 0; i < items.Count; i++) buttonWaveIndices[items[i].Side, items[i].Action] = i;
    }

    private Sequence CreateBounceTween(RectTransform target, Vector3 scale, float duration)
    {
        Sequence bounce = DOTween.Sequence().SetAutoKill(true);
        bounce.Append(target.DOScale(scale * Mathf.Max(1.08f, BouncePeakScale), duration * 0.72f).SetEase(Ease.InOutSine));
        bounce.Append(target.DOScale(scale, duration * 0.28f).SetEase(Ease.OutBack, BackOvershoot));
        return bounce;
    }

    private void FinishEntrance(int side, int version)
    {
        SideState state = sides[side];
        if (version != state.Version) return;
        state.Entrance = null;
        state.Ready = true;
        state.IdleElapsed = 0f;
        StartScoreBreathing(side);
        if (IsGameRunning && !ending) OpenSideQuestionInput(side);
    }

    private void OpenSideQuestionInput(int side)
    {
        SideState state = sides[side];
        if (!IsGameRunning || ending || !configurationValid || !state.Ready || state.Resolved || state.Map == null) return;
        state.InputEnabled = true;
        RefreshSideButtons(side);
    }

    private bool CanOperate(int side) => side >= 0 && side < 2 && IsGameRunning && !ending && configurationValid && sides[side].Map != null && sides[side].Ready && sides[side].InputEnabled && !sides[side].Resolved && !sides[side].Busy;

    private void Move(bool left, Vector2Int delta)
    {
        int side = left ? 0 : 1;
        if (!CanOperate(side)) return;
        SideState state = sides[side];
        bool canMove = TryNeighbor(state.Cell, delta, out int target) && state.Map.Obstacles[target] < 0;
        int version = state.Version;
        state.Busy = true;
        state.MoveProtected = true;
        state.Ship.FollowCell = false;
        BeginProtectedAction();
        RefreshSideButtons(side);
        Sequence motion = DOTween.Sequence().SetAutoKill(true);
        motion.Pause();
        SetShipFacing(side, delta);
        motion.Append(state.Ship.Rect.DOLocalRotate(new Vector3(0f, 0f, state.FacingAngle), Mathf.Max(0.01f, TurnDuration), RotateMode.Fast).SetEase(Ease.OutCubic));
        if (canMove)
        {
            float progress = 0f;
            int source = state.Cell;
            motion.Append(DOTween.To(() => progress, value => { progress = value; ApplyMovePosition(side, source, target, value); }, 1f, Mathf.Max(0.01f, MoveDuration)).SetEase(Ease.InOutSine));
        }
        else
        {
            motion.Append(state.Ship.Rect.DOScale(state.FacingScale * 0.94f, Mathf.Max(0.01f, BlockedFeedbackDuration) * 0.5f).SetEase(Ease.OutSine));
            motion.Append(state.Ship.Rect.DOScale(state.FacingScale, Mathf.Max(0.01f, BlockedFeedbackDuration) * 0.5f).SetEase(Ease.OutSine));
        }
        motion.OnComplete(() => FinishMove(side, target, canMove, version));
        state.Motion = motion;
        motion.Play();
    }

    private float GetFacingAngle(int side, Vector2Int delta)
    {
        int cell = sides[side].Cell;
        Vector2 direction;
        if (TryNeighbor(cell, delta, out int next)) direction = GetCellAnchor(side, next) - GetCellAnchor(side, cell);
        else if (TryNeighbor(cell, -delta, out int opposite)) direction = GetCellAnchor(side, cell) - GetCellAnchor(side, opposite);
        else direction = new Vector2(delta.x, delta.y);
        return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
    }

    private void SetShipFacing(int side, Vector2Int delta)
    {
        SideState state = sides[side];
        float angle = GetFacingAngle(side, delta);
        bool mirror = angle > 90f || angle < -90f;
        state.FacingScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
        state.FacingAngle = mirror ? angle + (angle > 0f ? -180f : 180f) : angle;
        // 只水平镜像船身；绝不使用Z轴180度把船体上下颠倒。
        state.Ship.Rect.localScale = state.FacingScale;
    }

    private void ApplyMovePosition(int side, int source, int target, float progress)
    {
        SideState state = sides[side];
        if (state.Ship == null || state.Ship.Rect == null) return;
        state.Ship.Rect.anchoredPosition = Vector2.LerpUnclamped(GetCellAnchor(side, source), GetCellAnchor(side, target), progress);
    }

    private void FinishMove(int side, int target, bool moved, int version)
    {
        SideState state = sides[side];
        state.Motion = null;
        state.Busy = false;
        if (version == state.Version && IsGameRunning && !state.Resolved)
        {
            if (moved) state.Cell = target;
            state.Ship.Cell = state.Cell;
            state.Ship.FollowCell = true;
            state.Ship.Rect.localScale = state.FacingScale;
            AlignView(state.Ship);
            if (moved)
            {
                RecordMovement(side);
                CollectScoreItem(side);
                if (state.Cell == state.Map.Finish) ResolveRound(side);
            }
        }
        RefreshSideButtons(side);
        ReleaseMoveProtection(state);
    }

    private void CollectScoreItem(int side)
    {
        SideState state = sides[side];
        PooledView coin = state.Coins[state.Cell];
        if (coin == null || coin.Object == null) return;
        state.Coins[state.Cell] = null;
        state.Map.Scores[state.Cell] = false;
        ScoreFlight flight = new ScoreFlight { View = coin, Side = side, Version = state.Version, Protected = true };
        state.ItemFlights.Add(flight);
        BeginProtectedAction();
        flight.Tween = CreateRewardFlight(coin, side, () => AwardScoreItem(flight), () => FinishScoreItemFlight(flight));
        flight.Tween.Play();
    }

    private void AwardScoreItem(ScoreFlight flight)
    {
        SideState state = sides[flight.Side];
        if (flight.Awarded || flight.Version != state.Version || !IsGameRunning || flight.View == null || flight.View.Object == null) return;
        flight.Awarded = true;
        DespawnView(flight.View);
        AddSideScore(flight.Side, ItemPoints);
    }

    private void FinishScoreItemFlight(ScoreFlight flight)
    {
        SideState state = sides[flight.Side];
        if (!state.ItemFlights.Remove(flight)) return;
        flight.Tween = null;
        ReleaseScoreItemProtection(flight);
        if (flight.Version == state.Version && state.Resolved && state.GoalProtected && state.GoalFlight == null) FinishGoal(flight.Side, flight.Version);
    }

    private void ReleaseScoreItemProtection(ScoreFlight flight)
    {
        if (!flight.Protected) return;
        flight.Protected = false;
        EndProtectedAction();
    }

    private void CancelScoreItemFlights(SideState state)
    {
        for (int i = state.ItemFlights.Count - 1; i >= 0; i--)
        {
            ScoreFlight flight = state.ItemFlights[i];
            if (flight.Tween != null) flight.Tween.Kill(false);
            flight.Tween = null;
            state.ItemFlights.RemoveAt(i);
            DespawnView(flight.View);
            ReleaseScoreItemProtection(flight);
        }
    }

    private void StartScoreBreathing(int side)
    {
        PooledView[] coins = sides[side].Coins;
        if (coins == null) return;
        for (int i = 0; i < coins.Length; i++)
        {
            PooledView coin = coins[i];
            if (coin == null || coin.Rect == null) continue;
            StopBreathing(coin);
            coin.Rect.localScale = Vector3.one;
            coin.Breathing = coin.Rect.DOScale(Vector3.one * (1f + Mathf.Clamp(ScoreBreathAmplitude, 0f, 0.2f)), Mathf.Max(0.1f, ScoreBreathDuration)).SetEase(Ease.InOutSine).SetDelay((i % 4) * 0.12f).SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
        }
    }

    private void StopBreathing(PooledView view)
    {
        if (view == null || view.Breathing == null) return;
        view.Breathing.Kill(false);
        view.Breathing = null;
    }

    private void AddSideScore(int side, int points)
    {
        if (side == 0) AddLeftScore(points);
        else AddRightScore(points);
    }

    private void ResolveRound(int side)
    {
        SideState state = sides[side];
        if (state.Resolved || !IsGameRunning) return;
        state.Resolved = true;
        state.InputEnabled = false;
        state.GoalProtected = true;
        BeginProtectedAction();
        RefreshSideButtons(side);
        // 与藏宝图关卡相同：传入UI屏幕坐标，基类生成AnswerEff[0]并负责LeanPool回收。
        Vector2 effectScreen = GetScreenCenter(GetBoard(side).Cells[state.Map.Finish]);
        PlayAnswerEffect(true, effectScreen);
        PlayGoalFlight(side, state.Version);
    }

    private void PlayGoalFlight(int side, int version)
    {
        SideState state = sides[side];
        state.GoalFlight = CreateRewardFlight(state.Treasure, side, () => AwardGoal(side, version), () => FinishGoal(side, version));
        state.GoalFlight.Play();
    }

    private Sequence CreateRewardFlight(PooledView view, int side, TweenCallback onArrived, TweenCallback onComplete)
    {
        StopBreathing(view);
        view.FollowCell = false;
        view.Rect.localScale = Vector3.one;
        view.Rect.SetAsLastSibling();
        Vector2 startScreen = GetScreenCenter(view.Rect);
        float duration = Mathf.Max(0.1f, GoalFlyDuration);
        float progress = 0f;
        Sequence flight = DOTween.Sequence().SetAutoKill(true);
        flight.Pause();
        flight.Append(DOTween.To(() => progress, value => { progress = value; ApplyRewardFlight(view, side, startScreen, value); }, 1f, duration).SetEase(Ease.InOutSine));
        flight.Join(view.Rect.DOLocalRotate(new Vector3(0f, 0f, -360f * Mathf.Max(0f, GoalSpinTurns)), duration, RotateMode.FastBeyond360).SetEase(Ease.Linear));
        flight.Insert(duration * 0.5f, view.Rect.DOScale(Vector3.zero, duration * 0.5f).SetEase(Ease.InCubic));
        flight.AppendCallback(onArrived);
        flight.AppendInterval(Mathf.Max(0f, ScoreRollDuration));
        flight.OnComplete(onComplete);
        return flight;
    }

    private void ApplyRewardFlight(PooledView view, int side, Vector2 startScreen, float progress)
    {
        if (view == null || view.Rect == null) return;
        RectTransform score = side == 0 ? leftScoreText.rectTransform : rightScoreText.rectTransform;
        Vector2 endScreen = GetScreenCenter(score);
        Vector2 control = (startScreen + endScreen) * 0.5f + Vector2.up * (2f * Mathf.Max(0f, GoalArcHeight));
        float inverse = 1f - progress;
        Vector2 screen = inverse * inverse * startScreen + 2f * inverse * progress * control + progress * progress * endScreen;
        view.Rect.anchoredPosition = ScreenToParent(screen);
    }

    private void AwardGoal(int side, int version)
    {
        SideState state = sides[side];
        if (version != state.Version || !IsGameRunning || !state.Resolved) return;
        PooledView treasure = state.Treasure;
        if (treasure == null || treasure.Object == null) return;
        state.Treasure = null;
        DespawnView(treasure);
        AddSideScore(side, GoalPoints);
    }

    private void FinishGoal(int side, int version)
    {
        SideState state = sides[side];
        if (version != state.Version) return;
        state.GoalFlight = null;
        // 只等待本方已拾取道具的飞行与分数滚动，另一方仍然可以独立操作和切题。
        if (state.ItemFlights.Count > 0) return;
        ReleaseGoalProtection(state);
        if (version == state.Version && IsGameRunning && !ending) ScheduleSideNextQuestion(side, version);
    }

    private void ScheduleSideNextQuestion(int side, int version)
    {
        SideState state = sides[side];
        if (!IsGameRunning || ending || version != state.Version) return;
        if (state.NextQuestionRoutine != null) StopCoroutine(state.NextQuestionRoutine);
        state.NextQuestionRoutine = StartCoroutine(SideNextQuestionFlow(side, version));
    }

    private IEnumerator SideNextQuestionFlow(int side, int version)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, NextQuestionInterval));
        SideState state = sides[side];
        if (version != state.Version) yield break;
        state.NextQuestionRoutine = null;
        if (IsGameRunning && !ending && !state.MoveProtected && !state.GoalProtected && state.ItemFlights.Count == 0) GenerateSideQuestion(side, false);
    }

    // 本关不启用StartQuestionTimer，也不使用基类共享的ScheduleNextQuestion。

    protected override void OnGameEndWaitingForActions()
    {
        ending = true;
        for (int side = 0; side < 2; side++) sides[side].InputEnabled = false;
        DisableAllButtons();
    }

    private void CacheButtonScales()
    {
        if (scalesCached) return;
        for (int side = 0; side < 2; side++)
        {
            for (int action = 0; action < 4; action++)
            {
                TouchUIButton button = GetButton(side, action);
                Vector3 scale = button != null ? button.transform.localScale : Vector3.one;
                buttonScales[side, action] = scale == Vector3.zero ? Vector3.one : scale;
            }
        }
        scalesCached = true;
    }

    private void HideIntroButtons()
    {
        HideSideButtons(0);
        HideSideButtons(1);
    }

    private void HideSideButtons(int side)
    {
        for (int action = 0; action < 4; action++)
        {
            TouchUIButton button = GetButton(side, action);
            if (button != null) button.transform.localScale = Vector3.zero;
        }
    }

    private void RestoreButtonScales()
    {
        RestoreSideButtonScales(0);
        RestoreSideButtonScales(1);
    }

    private void RestoreSideButtonScales(int side)
    {
        if (!scalesCached) return;
        for (int action = 0; action < 4; action++)
        {
            TouchUIButton button = GetButton(side, action);
            if (button != null) button.transform.localScale = buttonScales[side, action];
        }
    }

    private TouchUIButton GetButton(int side, int action)
    {
        SideBoard board = GetBoard(side);
        if (board == null) return null;
        switch (action)
        {
            case 0: return board.UpButton;
            case 1: return board.DownButton;
            case 2: return board.LeftButton;
            case 3: return board.RightButton;
            default: return null;
        }
    }

    private void RefreshSideButtons(int side)
    {
        bool enabled = CanOperate(side);
        for (int action = 0; action < 4; action++)
        {
            TouchUIButton button = GetButton(side, action);
            if (button != null) button.interactable = enabled;
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
            for (int action = 0; action < 4; action++)
            {
                TouchUIButton button = GetButton(side, action);
                if (button != null) button.interactable = false;
            }
        }
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        if (GetButton(0, 0) != null) GetButton(0, 0).AddListener(OnLeftUp);
        if (GetButton(0, 1) != null) GetButton(0, 1).AddListener(OnLeftDown);
        if (GetButton(0, 2) != null) GetButton(0, 2).AddListener(OnLeftLeft);
        if (GetButton(0, 3) != null) GetButton(0, 3).AddListener(OnLeftRight);
        if (GetButton(1, 0) != null) GetButton(1, 0).AddListener(OnRightUp);
        if (GetButton(1, 1) != null) GetButton(1, 1).AddListener(OnRightDown);
        if (GetButton(1, 2) != null) GetButton(1, 2).AddListener(OnRightLeft);
        if (GetButton(1, 3) != null) GetButton(1, 3).AddListener(OnRightRight);
        buttonsBound = true;
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        if (GetButton(0, 0) != null) GetButton(0, 0).RemoveListener(OnLeftUp);
        if (GetButton(0, 1) != null) GetButton(0, 1).RemoveListener(OnLeftDown);
        if (GetButton(0, 2) != null) GetButton(0, 2).RemoveListener(OnLeftLeft);
        if (GetButton(0, 3) != null) GetButton(0, 3).RemoveListener(OnLeftRight);
        if (GetButton(1, 0) != null) GetButton(1, 0).RemoveListener(OnRightUp);
        if (GetButton(1, 1) != null) GetButton(1, 1).RemoveListener(OnRightDown);
        if (GetButton(1, 2) != null) GetButton(1, 2).RemoveListener(OnRightLeft);
        if (GetButton(1, 3) != null) GetButton(1, 3).RemoveListener(OnRightRight);
        buttonsBound = false;
    }

    private void OnLeftUp() { Move(true, Vector2Int.up); }
    private void OnLeftDown() { Move(true, Vector2Int.down); }
    private void OnLeftLeft() { Move(true, Vector2Int.left); }
    private void OnLeftRight() { Move(true, Vector2Int.right); }
    private void OnRightUp() { Move(false, Vector2Int.up); }
    private void OnRightDown() { Move(false, Vector2Int.down); }
    private void OnRightLeft() { Move(false, Vector2Int.left); }
    private void OnRightRight() { Move(false, Vector2Int.right); }

    private void CancelSideMotion(int side, bool restorePosition)
    {
        SideState state = sides[side];
        if (state.Motion != null) state.Motion.Kill(false);
        state.Motion = null;
        state.Busy = false;
        if (state.Ship != null && state.Ship.Rect != null)
        {
            state.Ship.FollowCell = true;
            state.Ship.Rect.localScale = state.FacingScale;
            if (restorePosition) AlignView(state.Ship);
        }
        ReleaseMoveProtection(state);
    }

    private void ReleaseMoveProtection(SideState state)
    {
        if (!state.MoveProtected) return;
        state.MoveProtected = false;
        EndProtectedAction();
    }

    private void ReleaseGoalProtection(SideState state)
    {
        if (!state.GoalProtected) return;
        state.GoalProtected = false;
        EndProtectedAction();
    }

    private void DespawnView(PooledView view)
    {
        if (view == null || view.Object == null) return;
        StopBreathing(view);
        GameObject instance = view.Object;
        view.Object = null;
        view.Rect = null;
        views.Remove(view);
        LeanPool.Despawn(instance);
    }

    private void ClearAllQuestions()
    {
        configurationValid = false;
        ClearSideQuestion(0);
        ClearSideQuestion(1);
    }

    private void ClearSideQuestion(int side)
    {
        SideState state = sides[side];
        state.Version++;
        state.Ready = false;
        state.InputEnabled = false;
        state.Resolved = false;
        state.IdleElapsed = 0f;
        RefreshSideButtons(side);
        if (state.EntranceRoutine != null) StopCoroutine(state.EntranceRoutine);
        state.EntranceRoutine = null;
        if (state.NextQuestionRoutine != null) StopCoroutine(state.NextQuestionRoutine);
        state.NextQuestionRoutine = null;
        if (state.Entrance != null) state.Entrance.Kill(false);
        state.Entrance = null;
        if (state.GoalFlight != null) state.GoalFlight.Kill(false);
        state.GoalFlight = null;
        CancelSideMotion(side, false);
        CancelScoreItemFlights(state);
        ReleaseGoalProtection(state);
        // 切题、重玩和退场必须回收旧物体；整局结算由CleanupGame保留画面，不走这里。
        for (int i = views.Count - 1; i >= 0; i--)
        {
            if (views[i].Side == side) DespawnView(views[i]);
        }
        state.Map = null;
        state.Ship = null;
        state.Treasure = null;
        state.Coins = null;
        RestoreSideButtonScales(side);
    }

    protected override void CleanupGame()
    {
        ending = true;
        // 正式结算只停止逻辑和动画，保留场上物体及当前位置。
        for (int side = 0; side < 2; side++) StopSideForSettlement(side);
        for (int i = 0; i < views.Count; i++) StopBreathing(views[i]);
        DisableAllButtons();
        RestoreButtonScales();
    }

    private void StopSideForSettlement(int side)
    {
        SideState state = sides[side];
        state.Version++;
        state.InputEnabled = false;
        state.Ready = false;
        state.Busy = false;
        if (state.EntranceRoutine != null) StopCoroutine(state.EntranceRoutine);
        state.EntranceRoutine = null;
        if (state.NextQuestionRoutine != null) StopCoroutine(state.NextQuestionRoutine);
        state.NextQuestionRoutine = null;
        if (state.Entrance != null) state.Entrance.Kill(false);
        state.Entrance = null;
        if (state.Motion != null) state.Motion.Kill(false);
        state.Motion = null;
        if (state.GoalFlight != null) state.GoalFlight.Kill(false);
        state.GoalFlight = null;
        for (int i = 0; i < state.ItemFlights.Count; i++)
        {
            ScoreFlight flight = state.ItemFlights[i];
            if (flight.Tween != null) flight.Tween.Kill(false);
            flight.Tween = null;
            flight.Protected = false;
        }
        state.ItemFlights.Clear();
        // 基类进入正式结算时保护计数已为0，场景退出时也会先重置该计数。
        state.MoveProtected = false;
        state.GoalProtected = false;
    }

    public override void CancelForSceneExit()
    {
        // 由外部场景控制或base.OnDestroy调用；真正离开场景时才统一回收。
        base.CancelForSceneExit();
        ClearAllQuestions();
    }

    protected override void OnDestroy()
    {
        UnbindButtons();
        base.OnDestroy();
    }
}
