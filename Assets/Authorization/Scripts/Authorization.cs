using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;
using ZXing;
using ZXing.QrCode;
using System.IO;
using BestHTTP;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace Encryption
{
    /// <summary>
    /// 计算硬件相关唯一标识符
    /// </summary>
    /// <summary>
    /// 计算硬件相关唯一标识符
    /// </summary>
    public class HardwareCodeUtil
    {
        const string cpu_command = "wmic CPU get ProcessorID "; //不可改变
        const string cpu_Tag = "ProcessorId";

        const string Disk_command = "wmic diskdrive where index=0 get SerialNumber"; //不可改变
        const string Disk_Tag = "SerialNumber";

        const string uuid_command = "wmic csproduct get UUID";
        const string uuid_Tag = "UUID";
        public static string GetWin_ID(string command, string Tag)
        {
            string cpuID = string.Empty;

            Process process = new Process();
            process.StartInfo.UseShellExecute = false;   //是否使用操作系统shell启动 
            process.StartInfo.CreateNoWindow = true;   //是否在新窗口中启动该进程的值 (不显示程序窗口)
            process.StartInfo.RedirectStandardInput = true;  // 接受来自调用程序的输入信息 
            process.StartInfo.RedirectStandardOutput = true;  // 由调用程序获取输出信息
            process.StartInfo.RedirectStandardError = true;  //重定向标准错误输出
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c" + command;
            process.StartInfo.WorkingDirectory = @"C:\Windows\System32";
            process.Start();                         // 启动程序
            //Thread.Sleep(10);
            //process.StandardInput.WriteLine(command); //向cmd窗口发送输入信息
            process.StandardInput.AutoFlush = true;
            Thread.Sleep(10);
            // 前面一个命令不管是否执行成功都执行后面(exit)命令，如果不执行exit命令，后面调用ReadToEnd()方法会假死
            process.StandardInput.WriteLine("exit");

            //循环等待读取缓冲区
            List<string> ReadLines = new List<string>();
            StreamReader reader = process.StandardOutput;//获取exe处理之后的输出信息
            string curLine = reader.ReadLine(); //获取错误信息到error
            while (!reader.EndOfStream)
            {
                Thread.Sleep(1);
                if (!string.IsNullOrEmpty(curLine))
                {
                    ReadLines.Add(curLine);
                }
                curLine = reader.ReadLine();
            }
            reader.Close(); //close进程
            process.WaitForExit();  //等待程序执行完退出进程
            process.Close();


            bool ReadCpuID = false;
            for (int i = 0; i < ReadLines.Count; i++)
            {
                if (ReadCpuID)
                {
                    cpuID += ReadLines[i];
                }
                if (ReadLines[i].StartsWith(Tag))
                {
                    ReadCpuID = true;
                }
            }

            return cpuID;
        }

        public static string GetMac()
        {
            StringBuilder builder = new StringBuilder();
#if UNITY_STANDALONE_WIN
            string id = string.Format("{0}&{1}&{2}", GetWin_ID(cpu_command, cpu_Tag), GetWin_ID(Disk_command, Disk_Tag), GetWin_ID(uuid_command, uuid_Tag));
            builder.Append(id);
#else
            builder.Append(SystemInfo.deviceUniqueIdentifier);
#endif
            builder.Append(Application.productName);
            string cl = builder.ToString().Replace(" ", "").Replace("\n", "").Replace("\r", "");
            //Debug.Log(cl);
            MD5 md5 = MD5.Create(); //实例化一个md5对像
                                    // 加密后是一个字节类型的数组，这里要注意编码UTF8/Unicode等的选择　
            byte[] s = md5.ComputeHash(Encoding.UTF8.GetBytes(cl));
            // 通过使用循环，将字节类型的数组转换为字符串，此字符串是常规字符格式化所得
            string pwd = string.Empty;
            for (int i = 0; i < s.Length; i++)
            {
                // 将得到的字符串使用十六进制类型格式。格式后的字符是小写的字母，如果使用大写（X）则格式后的字符是大写字符 
                pwd = pwd + s[i].ToString("X");
            }

            return pwd;
        }
    }

    /// <summary>
    /// 授权与绑定
    /// </summary>
    public class Authorization : MonoBehaviour
    {
        //private const string HttpSeverUri = "https://api.itaocow.com.cn/factory";
        private const string HttpSeverUri = @"https://www.junziboy.com/factory";
        /// <summary>
        /// 设备唯一标识码key值
        /// </summary>
        public static readonly string MachineKey = "machineCode";

        /// <summary>
        /// 到期日期key值
        /// </summary>
        public static readonly string DateKey = "DateCode";

        /// <summary>
        /// 设备时间key值
        /// </summary>
        public static readonly string TimeKey = "TimeCode";

        private bool Is显示绑定二维码 = false;
        private bool Is显示授权二维码 = false;

        [Header("二维码")]
        public RawImage image;

        [Header("提示信息")]
        public Text text;

        [Header("提示信息2")]
        public Text text2;

        [Header("版本号")]
        public Text text3;

        /// <summary>
        /// 设备唯一码
        /// </summary>
        private string HardwareCode = string.Empty;

        [Header("跳转场景")]
        public string SceneName;

        private string machineCode = "未授权";

        //允许误差2小时
        private int ErrorRange = 60 * 2;

        private static readonly string ExpirationTime = "2380-12-31 00:00:00";

        /// <summary>
        /// 检查状态-流程
        /// </summary>
        public enum CheckState
        {
            检查授权,
            检查绑定用户,
            检查是否设备合法性,
        }

        private void Awake()
        {
            //Debuger.Instance.Init();
            IsNullHardwareCode();
            text3.text = "Version:" + Application.version;
        }

        // Start is called before the first frame update
        private void Start()
        {
            Check(CheckState.检查授权, false);
            DebugManager.Instance.AddData("设备编码", machineCode);
        }

        private bool Check设备合法授权()
        {
            string V1 = string.Empty;
            string V2 = string.Empty;

            if (!TryPlayerGet(DateKey, HardwareCode, ref V1, ref V2))
            {
                SetText2("本地授权数据异常，请重新授权。");
                return false;
            }

            Debug.Log("HardwareCode==" + HardwareCode);
            Debug.Log("DateKey==" + V1);
            Debug.Log("V2==" + V2);

            DebugManager.Instance.AddData("设备编码", machineCode);

            if (V1.Equals(ExpirationTime))
            {
                return true;
            }

            if (!V1.Equals(V2))
            {
                SetText2("请联网更新系统时间...");
                return false;
            }

            DateTime AuthTime;

            if (!DateTime.TryParse(V1, out AuthTime))
            {
                ClearLocalAuthCache();
                SetText2("授权时间格式异常，请重新授权。");
                return false;
            }

            if (DateTime.Compare(AuthTime, GetBeiJingTime()) < 0)
            {
                SetText2("请联网更新授权时间");
                return false;
            }

            V1 = string.Empty;
            V2 = string.Empty;

            if (!TryPlayerGet(MachineKey, HardwareCode, ref V1, ref V2))
            {
                SetText2("设备授权数据异常，请重新授权。");
                return false;
            }

            if (!V1.Equals(V2))
            {
                return false;
            }

            V1 = string.Empty;
            V2 = string.Empty;

            if (!TryPlayerGet(TimeKey, HardwareCode, ref V1, ref V2))
            {
                SetText2("系统时间数据异常，请重新联网授权。");
                return false;
            }

            if (!V1.Equals(V2))
            {
                SetText2("请联网更新系统时间!");
                return false;
            }

            DateTime LastTime;

            if (!DateTime.TryParse(V1, out LastTime))
            {
                ClearLocalAuthCache();
                SetText2("本地时间数据异常，请重新授权。");
                return false;
            }

            TimeSpan temp = GetBeiJingTime() - LastTime;

            if (temp.TotalSeconds <= -ErrorRange)
            {
                SetText2("请联网更新系统时间。");
                return false;
            }

            AESEncryption.PlayerSet(TimeKey, GetBeiJingTime().ToString(), HardwareCode);
            Debug.Log(GetBeiJingTime().ToString());

            return true;
        }

        private void IsNullHardwareCode()
        {
            if (string.IsNullOrEmpty(HardwareCode))
            {
                HardwareCode = HardwareCodeUtil.GetMac();
            }
        }

        private void SetText1(string msg)
        {
            if (!text.gameObject.activeInHierarchy)
            {
                image.gameObject.SetActive(true);
                Text2Hide();
            }

            text.text = LanguageManger.Instance.GetValue(msg);
        }

        private void SetText2(string msg)
        {
            if (!text2.gameObject.activeInHierarchy)
            {
                Text1Hide();
                text2.gameObject.SetActive(true);
            }

            text2.text = LanguageManger.Instance.GetValue(msg);
        }

        private void Text2Hide()
        {
            if (text2.gameObject.activeInHierarchy)
                text2.gameObject.SetActive(false);
        }

        private void Text1Hide()
        {
            if (image.gameObject.activeInHierarchy)
                image.gameObject.SetActive(false);
        }

        private void Check(CheckState checkState, bool IsDelayed = true)
        {
            switch (checkState)
            {
                case CheckState.检查授权:
                    InvokeUtil.Instance.Run(() => { Check检查设备授权(On检查设备授权); }, 1.0f);
                    break;
                case CheckState.检查绑定用户:
                    InvokeUtil.Instance.Run(() => { Check检查绑定用户(machineCode, On检查绑定用户); }, 1.0f);
                    break;
                case CheckState.检查是否设备合法性:
                    if (!Check设备合法授权())
                    {
                        Check(CheckState.检查授权, IsDelayed);
                    }
                    else
                    {
                        UnityEngine.SceneManagement.SceneManager.LoadScene(SceneName);
                    }
                    break;
                default:
                    break;
            }
        }

        private void On检查设备授权(string js, bool suc)
        {
            if (suc)
            {
                try
                {
                    JObject pairs = JObject.Parse(js);
                    if (pairs.ContainsKey("Tag"))
                    {
                        int Tag = (int)pairs.GetValue("Tag");
                        var data = pairs.GetValue("Data");

                        if (Tag == 0)
                        {
                            DebugManager.Log("不合法设备，需要生成二维码授权机器！");
                            if (!Is显示授权二维码)
                            {
                                Is显示授权二维码 = true;
                                DebugManager.Log("生成授权二维码并显示");
                                CreatCode((string)data);
                                if(PlayerPrefs.HasKey(DateKey))
                                    PlayerPrefs.DeleteKey(DateKey);
                                if (PlayerPrefs.HasKey(TimeKey))
                                    PlayerPrefs.DeleteKey(TimeKey);
                                if (machineCode.Equals("未授权"))
                                {
                                    string V1 = string.Empty;
                                    string V2 = string.Empty;

                                    if (TryPlayerGet(MachineKey, HardwareCode, ref V1, ref V2))
                                    {
                                        if (V1.Equals(V2))
                                        {
                                            DebugManager.Instance.AddData("设备编码", V1);
                                        }
                                    }
                                }
                            }
                            Check(CheckState.检查授权);
                        }
                        else if (Tag == 1)
                        {
                            machineCode = (string)data["MachineCode"];
                            DebugManager.Log("合法设备，进行授权");
                            DebugManager.Instance.AddData("设备编码", machineCode);
                            string ExpiresDate = (string)data["ExpiresDate"];
                            AESEncryption.PlayerSet(DateKey, ExpiresDate, HardwareCode);

                            string Times = (string)data["NowDate"];
                            AESEncryption.PlayerSet(TimeKey, Times, HardwareCode);

                            AESEncryption.PlayerSet(MachineKey, machineCode, HardwareCode);
                            Check(CheckState.检查是否设备合法性, false);
                        }
                    }
                }
                catch (Exception e)
                {
                    Is显示授权二维码 = false;
                    DebugManager.LogError("产生错误:" + e.Message);
                    Check(CheckState.检查授权);
                }
            }
            else
            {
                IsNullHardwareCode();
                Is显示授权二维码 = false;
                Check(CheckState.检查授权);
            }
        }

        /// <summary>
        /// 检查设备是否授权(app使用)  
        /// </summary>
        /// <param name="hardwareCode">硬件mac地址</param>
        /// <param name="cb">回调函数 js，err </param>
        /// 返回结果对象的Tag==1，合法授权Data字段则为备授权期唯一id（MachineCode） 返回结果对象的Tag==0，非合法授权Data字段则是含硬件码的授权地址，可用于生成二维码
        /// <returns></returns>
        public void Check检查设备授权(Action<string, bool> cb)
        {
            //Debug.Log("HardwareCode==" + HardwareCode);
            string url = HttpSeverUri + "/Factory/CheckMachineAuth" + "?hardwareCode=" + HardwareCode;
            HTTPRequest request = new HTTPRequest(new Uri(url), methodType: HTTPMethods.Get, (req, resp) =>
            {
                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        DebugManager.Log("Check检查设备授权");
                        if (!string.IsNullOrEmpty(resp.DataAsText))
                        {
                            cb?.Invoke(resp.DataAsText, true);
                        }
                        else
                        {
                            SetText2("请检查网络是否连接");
                            Is显示授权二维码 = false;
                            cb?.Invoke(resp.DataAsText, false);
                        }
                        break;
                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.Aborted:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                    default:
                        SetText2("请检查网络是否连接!");
                        Is显示授权二维码 = false;
                        Check(CheckState.检查是否设备合法性);
                        break;
                }
            });
            request.SetHeader("accept", "*/*");
            request.Send();
        }

        /// <summary>
        /// 检查设备是否绑定用户(app使用)  
        /// </summary>
        /// <param name="machineCode">用户绑定获取的唯一编号</param>
        /// <param name="cb">回调函数 js，err </param>
        /// 返回结果对象的Tag==1，已经绑定 返回结果对象的Tag==0，未绑定，Data返回设备二维码图片连接
        /// <returns></returns>
        public void Check检查绑定用户(string machineCode, Action<string, bool> cb)
        {
            string url = HttpSeverUri + "/Factory/CheckIsBind" + "?machineCode=" + machineCode;
            HTTPRequest request = new HTTPRequest(new Uri(url), methodType: HTTPMethods.Get, (req, resp) =>
            {
                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        DebugManager.Log("Check检查绑定用户");
                        if (!string.IsNullOrEmpty(resp.DataAsText))
                        {
                            cb?.Invoke(resp.DataAsText, true);
                        }
                        else
                        {
                            SetText2("请检查网络是否连接");
                            Is显示授权二维码 = false;
                            cb?.Invoke(resp.DataAsText, false);
                        }
                        break;
                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.Aborted:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                    default:
                        SetText2("请检查网络是否连接!");
                        Is显示授权二维码 = false;
                        Check(CheckState.检查绑定用户);
                        break;
                }
            });
            request.SetHeader("accept", "*/*");
            request.Send();
        }

        private void On检查绑定用户(string js, bool suc)
        {
            if (suc)
            {
                try
                {
                    JObject pairs = JObject.Parse(js);
                    if (pairs.ContainsKey("Tag"))
                    {
                        int Tag = (int)pairs.GetValue("Tag");
                        if (Tag == 0)
                        {
                            string temp = pairs.GetValue("Data").ToString();
                            if (!string.IsNullOrWhiteSpace(temp))
                            {
                                string url = pairs.GetValue("Data").ToString();
                                SetText1("绑定用户");
                                if (!Is显示绑定二维码)
                                {
                                    DownPicTexture(url, OnDownPicTexture);
                                }
                            }
                            Check(CheckState.检查绑定用户);
                        }
                        else if (Tag == 1)
                        {
                            DebugManager.Log("已经绑定设备，进入游戏");
                            Check(CheckState.检查是否设备合法性, false);
                        }
                    }
                }
                catch (Exception e)
                {
                    DebugManager.Log("解析错误:" + e.Message);
                    Is显示绑定二维码 = false;
                    Check(CheckState.检查绑定用户);
                }
            }
            else
            {
                IsNullHardwareCode();
                Is显示绑定二维码 = false;
                Check(CheckState.检查绑定用户);
            }
        }

        private void DownPicTexture(string url, Action<Texture2D, bool> action)
        {
            HTTPRequest request = new HTTPRequest(new Uri(url), methodType: HTTPMethods.Get, (req, resp) =>
            {
                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        action?.Invoke(resp.DataAsTexture2D, true);
                        SetText1("绑定用户");
                        break;
                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.Aborted:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                    default:
                        SetText2("请检查网络是否连接!");
                        Is显示绑定二维码 = false;
                        DownPicTexture(url, action);
                        break;
                }
            });
            request.SetHeader("accept", "*/*");
            request.Send();
        }

        private void OnDownPicTexture(Texture2D texture2D, bool suc)
        {
            Is显示绑定二维码 = suc;
            if (suc)
            {
                image.texture = texture2D;
                SetText1("用户绑定设备");
            }
        }

        private void CreatCode(string url)
        {
            var rect = image.rectTransform.rect;
            QrCodeEncodingOptions options = new QrCodeEncodingOptions();
            options.CharacterSet = "UTF-8";
            options.DisableECI = true;
            options.ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.H;
            options.Width = (int)rect.width;
            options.Height = (int)rect.height;
            options.Margin = 1;
            BarcodeWriter writer = new BarcodeWriter();
            writer.Format = BarcodeFormat.QR_CODE;
            writer.Options = options;

            var Lastresult = url;
            //Debug.Log(Lastresult);
            var colors = writer.Write(Lastresult);

            var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false, false);
            texture.SetPixels32(colors);
            texture.Apply();
            image.texture = texture;

            SetText1("设备授权");
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Application.Quit();
            }
        }

        public DateTime GetBeiJingTime()
        {
            return DateTime.UtcNow + new TimeSpan(8, 0, 0);
        }

        private void ClearLocalAuthCache()
        {
            if (PlayerPrefs.HasKey(DateKey))
                PlayerPrefs.DeleteKey(DateKey);

            if (PlayerPrefs.HasKey(TimeKey))
                PlayerPrefs.DeleteKey(TimeKey);

            if (PlayerPrefs.HasKey(MachineKey))
                PlayerPrefs.DeleteKey(MachineKey);

            PlayerPrefs.Save();

            machineCode = "未授权";

            DebugManager.Log("本地授权缓存已清理。");
        }

        private bool TryPlayerGet(string key, string hardwareCode, ref string v1, ref string v2)
        {
            try
            {
                AESEncryption.PlayerGet(key, hardwareCode, ref v1, ref v2);
                return true;
            }
            catch (Exception e)
            {
                DebugManager.LogError("读取本地授权数据失败，准备清理缓存：" + e.Message);
                ClearLocalAuthCache();
                return false;
            }
        }
    }
}

