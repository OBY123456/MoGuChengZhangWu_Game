using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

/// <summary>只发送数据和单向心跳，不接收回包，不等待 ACK。</summary>
[DisallowMultipleComponent]
public class UDPSend : MonoBehaviour
{
    [Serializable] public sealed class ConnectionEvent : UnityEvent<bool> { }

    private const int MaxDatagramBytes = 1200;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static UDPSend Instance { get; private set; }

    [Tooltip("控制端 UDPReceive 的监听端口，与本机接收端口独立")]
    [SerializeField] private int serverPort = 8060;
    [Tooltip("尚未知道控制端 IP 时，广播发送单向发现消息，不等待回应")]
    [SerializeField] private bool enableBroadcastDiscovery = true;
    [SerializeField] private string broadcastAddress = "255.255.255.255";
    [Tooltip("控制端接收脚本的 IP；留空时使用本机 UDPReceive 收到的控制端 IP")]
    [SerializeField] private string manualServerIp = "";
    [SerializeField] private bool persistAcrossScenes = true;

    [Header("单向心跳与 Socket 重建")]
    [SerializeField, Min(0.2f)] private float heartbeatIntervalSeconds = 1f;
    [SerializeField, Min(0.5f)] private float discoveryIntervalSeconds = 1.5f;
    [SerializeField, Min(0.5f)] private float socketRestartIntervalSeconds = 2f;

    [Header("状态事件")]
    [SerializeField] private bool logNetworkState = true;
    [SerializeField] private ConnectionEvent onConnectionChanged = new ConnectionEvent();

    // 在线依据来自接收脚本；Socket 和发送目标端口仍由本脚本独立管理。
    public bool IsConnected => IsReady && UDPReceive.Instance != null && UDPReceive.Instance.HasRecentClient && TryGetServerEndpoint(out _);
    public bool IsReady => socket != null && !paused && isActiveAndEnabled;
    public string ConnectedServerIp => IsConnected && TryGetServerEndpoint(out IPEndPoint endpoint) ? endpoint.Address.ToString() : string.Empty;
    public int ServerPort => serverPort;
    public int PendingCount => 0;
    public event Action<bool> ConnectionChanged;

    private readonly object sendLock = new object();
    private Socket socket;
    private bool started;
    private bool paused;
    private bool lastConnectionState;
    private float nextSocketOpenTime;
    private float nextHeartbeatTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (Instance != this) return;
        started = true;
        Init();
    }

    private void OnEnable()
    {
        if (started && !paused && Instance == this) Init();
    }

    public void Init()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || paused || socket != null) return;
        Socket newSocket = null;
        try
        {
            newSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            newSocket.EnableBroadcast = true;
            newSocket.SendTimeout = 200;
            newSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
            socket = newSocket;
            nextHeartbeatTime = 0f;
        }
        catch (Exception ex)
        {
            if (newSocket != null) newSocket.Close();
            if (logNetworkState) Debug.LogWarning("[UDPSend] 初始化失败：" + ex.Message, this);
            StopSocket();
            nextSocketOpenTime = Time.realtimeSinceStartup + socketRestartIntervalSeconds;
        }
    }

    public void Send(string value)
    {
        TrySend(value);
    }

    public void Send(DataClass data)
    {
        TrySend(data);
    }

    public void Send(ClassEnum classEnum, object data)
    {
        TrySend(classEnum, data);
    }

    public bool SendLatest(string value)
    {
        return TrySend(value);
    }

    public bool TrySend(string value)
    {
        DataClass data;
        return ClassToJson.TryParseEnvelope(value, out data) && TrySend(data);
    }

    public bool TrySend(ClassEnum classEnum, object data)
    {
        return TrySend(ClassToJson.CreateData(classEnum, data));
    }

    /// <summary>离线直接跳过；成功仅表示本地发送完成，不代表对方已经执行。</summary>
    public bool TrySend(DataClass data)
    {
        if (!IsConnected || !TryGetServerEndpoint(out IPEndPoint endpoint)) return false;
        ClassEnum classEnum;
        if (!ClassToJson.TryGetClassEnum(data, out classEnum) || classEnum == ClassEnum.HeartbeatData) return false;
        return SendData(data, endpoint);
    }

    private void Update()
    {
        if (Instance != this) return;
        float now = Time.realtimeSinceStartup;
        if (!paused && socket == null && now >= nextSocketOpenTime) Init();
        PublishConnectionState();
        if (!IsReady || now < nextHeartbeatTime || serverPort < 1 || serverPort > 65535) return;

        if (TryGetServerEndpoint(out IPEndPoint endpoint))
        {
            SendHeartbeat(HeartbeatType.Ping, endpoint);
            nextHeartbeatTime = now + heartbeatIntervalSeconds;
        }
        else
        {
            if (enableBroadcastDiscovery && IPAddress.TryParse(broadcastAddress, out IPAddress address) && address.AddressFamily == AddressFamily.InterNetwork) SendHeartbeat(HeartbeatType.Discover, new IPEndPoint(address, serverPort));
            nextHeartbeatTime = now + discoveryIntervalSeconds;
        }
    }

    private bool TryGetServerEndpoint(out IPEndPoint endpoint)
    {
        endpoint = null;
        if (serverPort < 1 || serverPort > 65535) return false;
        string address = manualServerIp;
        if (string.IsNullOrWhiteSpace(address))
        {
            UDPReceive receiver = UDPReceive.Instance;
            if (receiver == null || !receiver.HasRecentClient) return false;
            address = receiver.ConnectedClientIp;
        }
        if (string.IsNullOrWhiteSpace(address) || !IPAddress.TryParse(address.Trim(), out IPAddress ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        endpoint = new IPEndPoint(ip, serverPort);
        return true;
    }

    private bool SendHeartbeat(HeartbeatType type, IPEndPoint endpoint)
    {
        return SendData(ClassToJson.CreateData(ClassEnum.HeartbeatData, new HeartbeatData(type)), endpoint);
    }

    private bool SendData(DataClass data, IPEndPoint endpoint)
    {
        if (!IsReady || endpoint == null) return false;
        try
        {
            string json = ClassToJson.GetJson(data);
            if (string.IsNullOrEmpty(json)) return false;
            byte[] bytes = Utf8.GetBytes(json);
            if (bytes.Length == 0 || bytes.Length > MaxDatagramBytes) return false;
            lock (sendLock) return socket.SendTo(bytes, endpoint) == bytes.Length;
        }
        catch (EncoderFallbackException) { return false; }
        catch (Exception ex)
        {
            if (logNetworkState) Debug.LogWarning("[UDPSend] 发送失败：" + ex.Message, this);
            StopSocket();
            nextSocketOpenTime = Time.realtimeSinceStartup + socketRestartIntervalSeconds;
            return false;
        }
    }

    private void PublishConnectionState()
    {
        bool connected = IsConnected;
        if (connected == lastConnectionState) return;
        lastConnectionState = connected;
        if (logNetworkState) Debug.Log(connected ? "[UDPSend] 控制端在线，可以发送业务消息。" : "[UDPSend] 控制端离线或发送 Socket 不可用。", this);
        ConnectionChanged?.Invoke(connected);
        onConnectionChanged?.Invoke(connected);
    }

    public void SetManualServerIp(string address)
    {
        manualServerIp = address == null ? string.Empty : address.Trim();
        ForceRediscover();
    }

    public void ForceRediscover()
    {
        // 保留原调用入口，只让下次 Update 立即发出单向发现或心跳。
        nextHeartbeatTime = 0f;
        PublishConnectionState();
    }

    public IEnumerator ResetSocket()
    {
        StopSocket();
        nextSocketOpenTime = 0f;
        if (!paused) Init();
        yield return null;
    }

    private void StopSocket()
    {
        Socket oldSocket = socket;
        socket = null;
        if (oldSocket != null)
        {
            try { oldSocket.Close(); }
            catch (Exception) { }
        }
        PublishConnectionState();
    }

    private void OnApplicationPause(bool pause)
    {
        paused = pause;
        if (pause) StopSocket();
        else if (started && Instance == this) Init();
    }

    private void OnDisable() { StopSocket(); }
    private void OnApplicationQuit() { StopSocket(); }

    private void OnDestroy()
    {
        StopSocket();
        if (Instance == this) Instance = null;
    }

    private void OnValidate()
    {
        serverPort = Mathf.Clamp(serverPort, 1, 65535);
        heartbeatIntervalSeconds = Mathf.Max(0.2f, heartbeatIntervalSeconds);
        discoveryIntervalSeconds = Mathf.Max(0.5f, discoveryIntervalSeconds);
        socketRestartIntervalSeconds = Mathf.Max(0.5f, socketRestartIntervalSeconds);
    }
}