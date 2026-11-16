// 投影端：统一接收 DataClass，在主线程解析消息并派发具体类型事件。
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MTFrame.MTEvent;
using UnityEngine;

[DisallowMultipleComponent]
public class UDPReceive : MonoBehaviour
{
    private const int MaxDatagramBytes = 1200;
    private const int ReceiveBufferSize = 2048;
    private const int MaxQueuedMessages = 256;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static UDPReceive Instance { get; private set; }

    [SerializeField] private int listenPort = 8060;
    [SerializeField] private bool persistAcrossScenes = true;
    [SerializeField, Min(1f)] private float clientTimeoutSeconds = 5f;
    [SerializeField, Min(0.5f)] private float reopenIntervalSeconds = 2f;
    [SerializeField, Range(1, 256)] private int maxProcessPerFrame = 64;
    [SerializeField] private bool logNetworkState = true;

    public bool IsClientConnected { get; private set; }
    public string ConnectedClientIp { get; private set; }
    public string LastReceivedMessage { get; private set; }
    public int ListenPort { get { return listenPort; } }
    public bool HasRecentClient => IsClientConnected && socket != null && !paused && isActiveAndEnabled && Time.realtimeSinceStartup - lastClientMessageTime <= clientTimeoutSeconds;

    private readonly object queueLock = new object();
    private readonly Queue<KeyValuePair<IPEndPoint, DataClass>> incoming = new Queue<KeyValuePair<IPEndPoint, DataClass>>();

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

        while (receiving && ReferenceEquals(socket, activeSocket))
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

                DataClass data;
                if (!ClassToJson.TryParseEnvelope(text, out data)) continue;
                KeyValuePair<IPEndPoint, DataClass> message = new KeyValuePair<IPEndPoint, DataClass>(new IPEndPoint(sender.Address, sender.Port), data);
                lock (queueLock)
                {
                    if (!receiving || !ReferenceEquals(socket, activeSocket)) break;
                    if (incoming.Count >= MaxQueuedMessages) incoming.Dequeue();
                    incoming.Enqueue(message);
                }
            }
            catch (SocketException ex)
            {
                if (!receiving || !ReferenceEquals(socket, activeSocket)) break;
                if (ex.SocketErrorCode == SocketError.TimedOut || ex.SocketErrorCode == SocketError.WouldBlock) continue;
                backgroundError = ex.Message;
                socketFaulted = true;
                break;
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (!receiving || !ReferenceEquals(socket, activeSocket)) break;
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
            KeyValuePair<IPEndPoint, DataClass> message;
            lock (queueLock)
            {
                if (incoming.Count == 0) break;
                message = incoming.Dequeue();
            }

            DeliverToGame(message.Value, message.Key);
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

    private void DeliverToGame(DataClass data, IPEndPoint remote)
    {
        try
        {
            ClassEnum classEnum;
            if (!ClassToJson.TryGetClassEnum(data, out classEnum)) return;
            if (classEnum == ClassEnum.HeartbeatData)
            {
                HeartbeatData heartbeat = ClassToJson.Parse<HeartbeatData>(data.classJson);
                if (heartbeat != null) HandleHeartbeat(heartbeat, remote);
                return;
            }

            // 业务消息无需先完成发现握手，解析成功后即可识别控制端并派发。
            switch (classEnum)
            {
                case ClassEnum.UdpGameName: DispatchToGame(ClassToJson.Parse<UdpGameName>(data.classJson), data, remote); break;
                case ClassEnum.OrderData: DispatchToGame(ClassToJson.Parse<OrderData>(data.classJson), data, remote); break;
            }
        }
        catch (Exception ex) { Debug.LogError("[UDPReceive] 数据解析或游戏事件异常：" + ex, this); }
    }

    private void DispatchToGame<T>(T value, DataClass data, IPEndPoint remote) where T : class
    {
        if (value == null || !AcceptClient(remote)) return;
        LastReceivedMessage = ClassToJson.GetJson(data);
        SceneControl controller = SceneControl.Instance;
        if (controller == null) return;
        EventParamete eventParamete = new EventParamete();
        eventParamete.AddParameter(value);
        EventManager.TriggerEvent(GenericEventEnumType.Message, typeof(T).Name, eventParamete);
    }

    private void HandleHeartbeat(HeartbeatData heartbeat, IPEndPoint remote)
    {
        // 单向保活，不发送 Offer、Pong 或其他回应，也不派发游戏事件。
        if (heartbeat.Type == HeartbeatType.Discover || heartbeat.Type == HeartbeatType.Ping) AcceptClient(remote);
    }

    private bool AcceptClient(IPEndPoint remote)
    {
        if (remote == null) return false;
        if (HasRecentClient && clientEndpoint != null && !remote.Address.Equals(clientEndpoint.Address)) return false;
        clientEndpoint = remote;
        lastClientMessageTime = Time.realtimeSinceStartup;
        ChangeClientConnection(true);
        return true;
    }

    private void ChangeClientConnection(bool online)
    {
        ConnectedClientIp = online && clientEndpoint != null ? clientEndpoint.Address.ToString() : string.Empty;
        if (IsClientConnected == online) return;
        IsClientConnected = online;

        if (logNetworkState) Debug.Log(online ? "[UDPReceive] 已连接触摸端：" + ConnectedClientIp : "[UDPReceive] 触摸端心跳超时，等待重连。", this);
    }

    private void DisconnectClient()
    {
        ChangeClientConnection(false);
        clientEndpoint = null;
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