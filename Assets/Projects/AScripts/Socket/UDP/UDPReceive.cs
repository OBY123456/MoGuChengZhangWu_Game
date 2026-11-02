// 投影安卓板：直接接收 UTF-8 明文游戏指令，并转发到原有 MTFrame 事件。
// 自动发现和心跳使用明文保留词；没有自定义包头、Base64、ACK 或消息去重。

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MTFrame.MTEvent;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class UDPReceive : MonoBehaviour
{
    [Serializable] public sealed class MessageEvent : UnityEvent<string> { }
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

    public static UDPReceive Instance { get; private set; }

    [Header("投影端 UDP 监听")]
    [SerializeField] private int listenPort = 8060;
    [SerializeField] private bool persistAcrossScenes = true;
    [SerializeField, Min(1f)] private float clientTimeoutSeconds = 5f;
    [SerializeField, Min(0.5f)] private float reopenIntervalSeconds = 2f;
    [SerializeField, Range(1, 256)] private int maxProcessPerFrame = 64;
    [SerializeField] private bool logNetworkState = true;

    [Header("游戏指令事件")]
    [SerializeField] private bool forwardToOriginalPanelEvent = true;
    [SerializeField] private MessageEvent onCommandReceived = new MessageEvent();
    [SerializeField] private ConnectionEvent onClientConnectionChanged = new ConnectionEvent();

    public bool IsClientConnected { get; private set; }
    public string ConnectedClientIp { get; private set; }
    public string LastReceivedMessage { get; private set; }
    public int ListenPort { get { return listenPort; } }
    public event Action<string> CommandReceived;
    public event Action<bool> ClientConnectionChanged;

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
    private float nextOpenTime;
    private float lastClientMessageTime;
    private IPEndPoint clientEndpoint;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[UDPReceive] 已存在投影端实例，移除重复组件。", this);
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
            newSocket.ReceiveTimeout = 1000;
            newSocket.SendTimeout = 200;
            newSocket.Bind(new IPEndPoint(IPAddress.Any, listenPort));

            socket = newSocket;
            receiving = true;
            socketFaulted = false;
            backgroundError = null;
            receiveThread = new Thread(() => ReceiveLoop(newSocket));
            receiveThread.IsBackground = true;
            receiveThread.Name = "UDPReceive-Projection";
            receiveThread.Start();

            if (logNetworkState) Debug.Log("[UDPReceive] 已监听 UDP 端口：" + listenPort, this);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UDPReceive] 监听失败，稍后重试：" + ex.Message, this);
            if (newSocket != null) newSocket.Close();
            StopSocket(true);
            nextOpenTime = Time.realtimeSinceStartup + reopenIntervalSeconds;
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
        if (IsClientConnected && now - lastClientMessageTime > clientTimeoutSeconds) DisconnectClient();

        if (socketFaulted)
        {
            socketFaulted = false;
            if (logNetworkState) Debug.LogWarning("[UDPReceive] 接收异常，重建 Socket：" + backgroundError, this);
            StopSocket(true);
            nextOpenTime = now + reopenIntervalSeconds;
        }

        if (!paused && socket == null && now >= nextOpenTime) Init();
    }

    private void ProcessIncoming(Datagram datagram)
    {
        string text = datagram.Text;

        if (text == DiscoverText)
        {
            if (IsClientConnected && clientEndpoint != null && !SameEndpoint(datagram.Remote, clientEndpoint))
            {
                bool expired = Time.realtimeSinceStartup - lastClientMessageTime > clientTimeoutSeconds;
                bool sameIp = datagram.Remote.Address.Equals(clientEndpoint.Address);
                if (!expired && !sameIp) return;
            }

            clientEndpoint = datagram.Remote;
            lastClientMessageTime = Time.realtimeSinceStartup;
            ChangeClientConnection(true);
            SendText(OfferText, clientEndpoint);
            return;
        }

        if (!IsClientConnected || !SameEndpoint(datagram.Remote, clientEndpoint)) return;
        lastClientMessageTime = Time.realtimeSinceStartup;

        if (text == PingText)
        {
            SendText(PongText, clientEndpoint);
            return;
        }

        if (text == OfferText || text == PongText) return;
        DeliverToGame(text);
    }

    private void DeliverToGame(string message)
    {
        LastReceivedMessage = message;

        try
        {
            if (CommandReceived != null) CommandReceived(message);
        }
        catch (Exception ex) { Debug.LogError("[UDPReceive] C# 指令事件异常：" + ex, this); }

        try
        {
            if (onCommandReceived != null) onCommandReceived.Invoke(message);
        }
        catch (Exception ex) { Debug.LogError("[UDPReceive] Inspector 指令事件异常：" + ex, this); }

        if (!forwardToOriginalPanelEvent) return;

        try
        {
            EventParamete eventParamete = new EventParamete();
            eventParamete.AddParameter(message);
            EventManager.TriggerEvent(GenericEventEnumType.Message, MTPanel.EventType.DataToPanel.ToString(), eventParamete);
        }
        catch (Exception ex) { Debug.LogError("[UDPReceive] MTFrame 游戏事件异常：" + ex, this); }
    }

    private bool SendText(string text, IPEndPoint remote)
    {
        Socket activeSocket = socket;
        if (activeSocket == null || remote == null) return false;
        byte[] bytes = Utf8.GetBytes(text);

        try
        {
            lock (sendLock) return activeSocket.SendTo(bytes, remote) == bytes.Length;
        }
        catch (SocketException ex)
        {
            if (logNetworkState) Debug.LogWarning("[UDPReceive] 发送回应失败：" + ex.Message, this);
            return false;
        }
        catch (ObjectDisposedException) { return false; }
    }

    private void ChangeClientConnection(bool online)
    {
        ConnectedClientIp = online && clientEndpoint != null ? clientEndpoint.Address.ToString() : string.Empty;
        if (IsClientConnected == online) return;
        IsClientConnected = online;

        if (logNetworkState) Debug.Log(online ? "[UDPReceive] 已连接触摸端：" + ConnectedClientIp : "[UDPReceive] 触摸端心跳超时，等待重连。", this);
        if (ClientConnectionChanged != null) ClientConnectionChanged(online);
        if (onClientConnectionChanged != null) onClientConnectionChanged.Invoke(online);
    }

    private void DisconnectClient()
    {
        ChangeClientConnection(false);
        clientEndpoint = null;
    }

    private static bool SameEndpoint(IPEndPoint a, IPEndPoint b)
    {
        return a != null && b != null && a.Port == b.Port && a.Address.Equals(b.Address);
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
        if (reportDisconnected) DisconnectClient();
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
        listenPort = Mathf.Clamp(listenPort, 1, 65535);
        clientTimeoutSeconds = Mathf.Max(1f, clientTimeoutSeconds);
        reopenIntervalSeconds = Mathf.Max(0.5f, reopenIntervalSeconds);
        maxProcessPerFrame = Mathf.Clamp(maxProcessPerFrame, 1, 256);
    }
}