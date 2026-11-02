// 触摸屏安卓板：游戏指令直接以 UTF-8 明文发送，无包头、无 ACK、无离线队列。
// 仅用四个保留明文控制词完成自动发现、心跳检测和断线重连。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class UDPSend : MonoBehaviour
{
    [Serializable] public sealed class ConnectionEvent : UnityEvent<bool> { }

    private struct Datagram
    {
        public string Text;
        public IPEndPoint Remote;

        public Datagram(string text, IPEndPoint remote)
        {
            Text = text;
            Remote = remote;
        }
    }

    private const string DiscoverText = "__UDP_DISCOVER__";
    private const string OfferText = "__UDP_OFFER__";
    private const string PingText = "__UDP_PING__";
    private const string PongText = "__UDP_PONG__";
    private const int MaxDatagramBytes = 1200;
    private const int ReceiveBufferSize = 2048;
    private const int MaxQueuedDatagrams = 256;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static UDPSend Instance { get; private set; }

    [Header("投影端连接")]
    [SerializeField] private int serverPort = 8060;
    [SerializeField] private bool enableBroadcastDiscovery = true;
    [SerializeField] private string broadcastAddress = "255.255.255.255";
    [Tooltip("可填投影板局域网 IP；填写后只接受该 IP 的回应。")]
    [SerializeField] private string manualServerIp = "";
    [SerializeField] private bool persistAcrossScenes = true;

    [Header("心跳与自动重连")]
    [SerializeField, Min(0.2f)] private float heartbeatIntervalSeconds = 1f;
    [SerializeField, Min(1f)] private float connectionTimeoutSeconds = 5f;
    [SerializeField, Min(0.5f)] private float discoveryIntervalSeconds = 1.5f;
    [SerializeField, Min(0.5f)] private float socketRestartIntervalSeconds = 2f;
    [SerializeField, Range(1, 256)] private int maxProcessPerFrame = 64;

    [Header("状态事件")]
    [SerializeField] private bool logNetworkState = true;
    [SerializeField] private ConnectionEvent onConnectionChanged = new ConnectionEvent();

    public bool IsConnected { get; private set; }
    public string ConnectedServerIp { get; private set; }
    public int ServerPort { get { return serverPort; } }
    public int PendingCount { get { return 0; } }
    public event Action<bool> ConnectionChanged;

    private readonly object queueLock = new object();
    private readonly object sendLock = new object();
    private readonly Queue<Datagram> incoming = new Queue<Datagram>();

    private Socket socket;
    private Thread receiveThread;
    private volatile bool receiving;
    private volatile bool socketFaulted;
    private volatile string backgroundError;
    private bool started;
    private bool paused;
    private float nextSocketOpenTime;
    private float nextDiscoverTime;
    private float nextHeartbeatTime;
    private float lastServerReplyTime;
    private IPEndPoint serverEndpoint;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[UDPSend] 已存在控制端实例，移除重复组件。", this);
            Destroy(gameObject);
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
            newSocket.ReceiveTimeout = 1000;
            newSocket.SendTimeout = 200;
            newSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            socket = newSocket;
            receiving = true;
            socketFaulted = false;
            backgroundError = null;
            receiveThread = new Thread(() => ReceiveLoop(newSocket));
            receiveThread.IsBackground = true;
            receiveThread.Name = "UDPSend-Control";
            receiveThread.Start();
            nextDiscoverTime = 0f;
            nextHeartbeatTime = 0f;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UDPSend] Socket 初始化失败，稍后重试：" + ex.Message, this);
            if (newSocket != null) newSocket.Close();
            StopSocket(true);
            nextSocketOpenTime = Time.realtimeSinceStartup + socketRestartIntervalSeconds;
        }
    }

    private void ReceiveLoop(Socket activeSocket)
    {
        byte[] buffer = new byte[ReceiveBufferSize];

        while (receiving)
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int count = activeSocket.ReceiveFrom(buffer, ref remote);
                if (count <= 0 || count > MaxDatagramBytes) continue;
                IPEndPoint sender = remote as IPEndPoint;
                if (sender == null) continue;

                string text;
                try { text = Utf8.GetString(buffer, 0, count); }
                catch (DecoderFallbackException) { continue; }

                Datagram datagram = new Datagram(text, new IPEndPoint(sender.Address, sender.Port));
                lock (queueLock)
                {
                    if (incoming.Count >= MaxQueuedDatagrams) incoming.Dequeue();
                    incoming.Enqueue(datagram);
                }
            }
            catch (SocketException ex)
            {
                if (!receiving) break;
                if (ex.SocketErrorCode == SocketError.TimedOut || ex.SocketErrorCode == SocketError.WouldBlock) continue;
                backgroundError = ex.Message;
                socketFaulted = true;
                break;
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (!receiving) break;
                backgroundError = ex.Message;
                socketFaulted = true;
                break;
            }
        }
    }

    // 与旧按钮调用兼容。断线时直接忽略，没有任何待发送队列。
    public void Send(string value)
    {
        TrySend(value);
    }

    // 旧接口保留；现在和 TrySend 完全相同，不再保存“最新消息”。
    public bool SendLatest(string value)
    {
        return TrySend(value);
    }

    // true 只表示本地 UDP 发送成功，不能保证对方已执行。
    public bool TrySend(string value)
    {
        if (!IsConnected || serverEndpoint == null || socket == null || paused) return false;
        if (Time.realtimeSinceStartup - lastServerReplyTime > connectionTimeoutSeconds)
        {
            ChangeConnection(false);
            return false;
        }

        if (string.IsNullOrEmpty(value) || IsControlText(value)) return false;

        byte[] bytes;
        try { bytes = Utf8.GetBytes(value); }
        catch (EncoderFallbackException) { return false; }
        if (bytes.Length == 0 || bytes.Length > MaxDatagramBytes) return false;

        bool sent = SendBytes(bytes, serverEndpoint);
        if (!sent) ChangeConnection(false);
        return sent;
    }

    private void Update()
    {
        if (Instance != this) return;

        int processed = 0;
        while (processed < maxProcessPerFrame)
        {
            Datagram datagram;
            lock (queueLock)
            {
                if (incoming.Count == 0) break;
                datagram = incoming.Dequeue();
            }

            ProcessIncoming(datagram);
            processed++;
        }

        float now = Time.realtimeSinceStartup;
        if (socketFaulted)
        {
            socketFaulted = false;
            if (logNetworkState) Debug.LogWarning("[UDPSend] 接收异常，重建 Socket：" + backgroundError, this);
            StopSocket(true);
            nextSocketOpenTime = now + socketRestartIntervalSeconds;
        }

        if (!paused && socket == null && now >= nextSocketOpenTime) Init();
        if (paused || socket == null) return;

        if (IsConnected && now - lastServerReplyTime > connectionTimeoutSeconds) ChangeConnection(false);

        if (!IsConnected && now >= nextDiscoverTime)
        {
            SendDiscovery();
            nextDiscoverTime = now + discoveryIntervalSeconds;
        }
        else if (IsConnected && now >= nextHeartbeatTime)
        {
            if (!SendText(PingText, serverEndpoint)) ChangeConnection(false);
            nextHeartbeatTime = now + heartbeatIntervalSeconds;
        }
    }

    private void ProcessIncoming(Datagram datagram)
    {
        if (datagram.Text == OfferText)
        {
            IPAddress selectedIp;
            if (!string.IsNullOrWhiteSpace(manualServerIp))
            {
                if (!IPAddress.TryParse(manualServerIp.Trim(), out selectedIp)) return;
                if (!selectedIp.Equals(datagram.Remote.Address)) return;
            }

            if (IsConnected && !SameEndpoint(datagram.Remote, serverEndpoint)) return;
            serverEndpoint = datagram.Remote;
            lastServerReplyTime = Time.realtimeSinceStartup;
            nextHeartbeatTime = Time.realtimeSinceStartup + heartbeatIntervalSeconds;
            ChangeConnection(true);
            SendText(PingText, serverEndpoint);
            return;
        }

        if (datagram.Text == PongText && IsConnected && SameEndpoint(datagram.Remote, serverEndpoint))
        {
            lastServerReplyTime = Time.realtimeSinceStartup;
        }
    }

    private void SendDiscovery()
    {
        if (enableBroadcastDiscovery)
        {
            IPAddress broadcastIp;
            if (IPAddress.TryParse(broadcastAddress, out broadcastIp) && broadcastIp.AddressFamily == AddressFamily.InterNetwork)
            {
                SendText(DiscoverText, new IPEndPoint(broadcastIp, serverPort));
            }
        }

        IPAddress fixedIp;
        if (!string.IsNullOrWhiteSpace(manualServerIp) && IPAddress.TryParse(manualServerIp.Trim(), out fixedIp) && fixedIp.AddressFamily == AddressFamily.InterNetwork)
        {
            SendText(DiscoverText, new IPEndPoint(fixedIp, serverPort));
        }
    }

    private bool SendText(string text, IPEndPoint remote)
    {
        return SendBytes(Utf8.GetBytes(text), remote);
    }

    private bool SendBytes(byte[] bytes, IPEndPoint remote)
    {
        Socket activeSocket = socket;
        if (activeSocket == null || remote == null || bytes.Length == 0 || bytes.Length > MaxDatagramBytes) return false;

        try
        {
            lock (sendLock) return activeSocket.SendTo(bytes, remote) == bytes.Length;
        }
        catch (SocketException ex)
        {
            if (logNetworkState) Debug.LogWarning("[UDPSend] UDP 发送失败：" + ex.Message, this);
            return false;
        }
        catch (ObjectDisposedException) { return false; }
        catch (Exception ex)
        {
            Debug.LogWarning("[UDPSend] UDP 发送异常：" + ex.Message, this);
            return false;
        }
    }

    private void ChangeConnection(bool online)
    {
        if (!online)
        {
            serverEndpoint = null;
            nextDiscoverTime = 0f;
        }

        ConnectedServerIp = online && serverEndpoint != null ? serverEndpoint.Address.ToString() : string.Empty;
        if (IsConnected == online) return;
        IsConnected = online;

        if (logNetworkState) Debug.Log(online ? "[UDPSend] 已连接投影端：" + ConnectedServerIp : "[UDPSend] 连接中断，自动重新发现。", this);
        if (ConnectionChanged != null) ConnectionChanged(online);
        if (onConnectionChanged != null) onConnectionChanged.Invoke(online);
    }

    private static bool SameEndpoint(IPEndPoint a, IPEndPoint b)
    {
        return a != null && b != null && a.Port == b.Port && a.Address.Equals(b.Address);
    }

    private static bool IsControlText(string text)
    {
        return text == DiscoverText || text == OfferText || text == PingText || text == PongText;
    }

    public void SetManualServerIp(string address)
    {
        manualServerIp = address == null ? string.Empty : address.Trim();
        ForceRediscover();
    }

    public void ForceRediscover()
    {
        ChangeConnection(false);
        nextDiscoverTime = 0f;
    }

    public IEnumerator ResetSocket()
    {
        StopSocket(true);
        nextSocketOpenTime = 0f;
        if (!paused) Init();
        yield return null;
    }

    private void StopSocket(bool reportDisconnected)
    {
        receiving = false;
        Socket oldSocket = socket;
        socket = null;

        if (oldSocket != null)
        {
            try { oldSocket.Close(); }
            catch (Exception) { }
        }

        Thread oldThread = receiveThread;
        receiveThread = null;
        if (oldThread != null && oldThread.IsAlive && Thread.CurrentThread != oldThread) oldThread.Join(250);
        lock (queueLock) incoming.Clear();
        if (reportDisconnected) ChangeConnection(false);
    }

    private void OnApplicationPause(bool pause)
    {
        paused = pause;
        if (pause) StopSocket(true);
        else if (started && Instance == this) Init();
    }

    private void OnDisable() { StopSocket(true); }
    private void OnApplicationQuit() { StopSocket(true); }

    private void OnDestroy()
    {
        StopSocket(true);
        if (Instance == this) Instance = null;
    }

    private void OnValidate()
    {
        serverPort = Mathf.Clamp(serverPort, 1, 65535);
        heartbeatIntervalSeconds = Mathf.Max(0.2f, heartbeatIntervalSeconds);
        connectionTimeoutSeconds = Mathf.Max(heartbeatIntervalSeconds * 2f, connectionTimeoutSeconds);
        discoveryIntervalSeconds = Mathf.Max(0.5f, discoveryIntervalSeconds);
        socketRestartIntervalSeconds = Mathf.Max(0.5f, socketRestartIntervalSeconds);
        maxProcessPerFrame = Mathf.Clamp(maxProcessPerFrame, 1, 256);
    }
}