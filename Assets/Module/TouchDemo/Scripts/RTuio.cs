using System;
using TUIOsharp;
using TUIOsharp.DataProcessors;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OBYTouch
{
    [Flags]
    public enum InputType
    {
        /// <summary>
        /// Pointer.
        /// </summary>
        Cursors = 1 << 0,

        /// <summary>
        /// Shape.
        /// </summary>
        Blobs = 1 << 1,

        /// <summary>
        /// Tagged object.
        /// </summary>
        Objects = 1 << 2
    }

    public static class ClickEvent
    {
        public static event Action<Vector3> clickevent;

        public static void OnClickEvent(Vector3 hitPoint)
        {
            clickevent?.Invoke(hitPoint);
        }
    }

    /// <summary>
    /// 将tuio数据转化为屏幕坐标
    /// </summary>
    public class RTuio : MonoBehaviour
    {
        public static RTuio Instance;

        [SerializeField]
        private int tuioPort = 3333;
        public TuioServer server;
        [SerializeField]
        private InputType supportedInputs = InputType.Cursors | InputType.Blobs | InputType.Objects;
        private CursorProcessor cursorProcessor;

        private int screenWidth;
        private int screenHeight;

        private bool IsAutoInput;

        public int TuioPort
        {
            get { return tuioPort; }
            set
            {
                if (tuioPort == value) return;
                tuioPort = value;
                connect();
            }
        }

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(this);
        }

        private void Start()
        {
            Open();
        }

        public void Open()
        {
            screenWidth = Screen.width;
            screenHeight = Screen.height;

            cursorProcessor = new CursorProcessor();
            cursorProcessor.CursorAdded += OnCursorAdded;
            connect();
        }

        public void Hide()
        {
            cursorProcessor.CursorAdded -= OnCursorAdded;
            disconnect();
        }

        private void disconnect()
        {
            if (server != null)
            {
                server.RemoveAllDataProcessors();
                server.Disconnect();
                server = null;
            }
        }

        private void connect()
        {
            if (!Application.isPlaying) return;
            if (server != null) disconnect();
            server = new TuioServer(TuioPort);
            server.Connect();
            updateInputs();
        }

        private void updateInputs()
        {
            if (server == null) return;

            if ((supportedInputs & InputType.Cursors) != 0)
            {
                server.AddDataProcessor(cursorProcessor);
                //UpdateInput = true;
            }
            else
            {
                server.RemoveDataProcessor(cursorProcessor);
            }
        }

        private void OnCursorAdded(object sender, TuioCursorEventArgs e)
        {
            var entity = e.Cursor;
            lock (this)
            {
                var x = entity.X * screenWidth;
                var y = (1 - entity.Y) * screenHeight;
                ClickEvent.OnClickEvent(new Vector2(x, y));
            }
        }

        public void RandPointSet()
        {
            if(IsAutoInput)
            {
                IsAutoInput = false;
            }
            else
            {
                IsAutoInput = true;
            }
        }

        public void RandPointHide()
        {
            IsAutoInput = false;
        }
    }
}