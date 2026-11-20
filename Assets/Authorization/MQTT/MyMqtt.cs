using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using Encryption;
using System.Net;
using Authorization = Encryption.Authorization;
using MqttData;

public class MyMqtt : MonoSingle<MyMqtt>
{
    private MQTT mqtt;

    //测试使用的授权唯一码
    string machineCode = string.Empty;

    public bool IsNetwork = false;

    protected override void Awake()
    {
        base.Awake();
        Loom.Initialize();
        string V1 = string.Empty;
        string V2 = string.Empty;
        AESEncryption.PlayerGet(Authorization.MachineKey, HardwareCodeUtil.GetMac(), ref V1, ref V2);
        if (V1.Equals(V2))
        {
            machineCode = V1;
            Debug.Log("machineCode==" + machineCode);
            DebugManager.Instance.AddData("设备编码", machineCode);
            if (!string.IsNullOrEmpty(machineCode))
            {
                IsGetHostAsync(MQTT.Host, OnGetHostCallBack);
            }
            else
            {
                DebugManager.LogError("设备不合法");
            }
        }
    }

    void OnGetHostCallBack(bool b)
    {
        IsNetwork = b;
        if (IsNetwork)
        {
            if (mqtt == null)
            {
                Connet();
            }
        }
        else
        {
            if (mqtt != null)
            {
                mqtt.DisConnect();
                mqtt = null;
            }
        }
    }

    private void Start()
    {
        
    }

    private void IsGetHostAsync(string host, Action<bool> action)
    {
        try
        {
            Dns.BeginGetHostAddresses(host, (ia) =>
            {
                try
                {
                    IPAddress[] iPAddresses = Dns.EndGetHostAddresses(ia);
                    Loom.QueueOnMainThread(() =>
                    {
                        action.Invoke(iPAddresses.Length > 0);
                        InvokeUtil.Instance.Run(() =>
                        {
                            IsGetHostAsync(host, action);
                        }, 1f);
                    });
                }
                catch
                {
                    Loom.QueueOnMainThread(() =>
                    {
                        action.Invoke(false);
                        InvokeUtil.Instance.Run(() =>
                        {
                            IsGetHostAsync(host, action);
                        }, 1f);
                    });
                }
            }, null);
        }
        catch
        {
            Loom.QueueOnMainThread(() =>
            {
                action.Invoke(false);
                InvokeUtil.Instance.Run(() =>
                {
                    IsGetHostAsync(host, action);
                }, 1f);
            });
        }
    }

    private void Connet()
    {
        mqtt = new MQTT();
        string usr, pwd;
        GenUserInfo(machineCode, out usr, out pwd);
        bool isconnet = mqtt.ConnectMqtt(machineCode, usr, pwd);
        if (!isconnet)
        {
            mqtt.DisConnect();
            mqtt = null;
        }
        else
        {
            mqtt.Subscribe(machineCode);
            CancelInvoke(nameof(Heartbeat));
            InvokeRepeating(nameof(Heartbeat),0,6);
        }
    }

    private void Heartbeat()
    {
        Send(MqttData.MsgType.心跳包,string.Empty);
    }

    public void Send(MqttData.MsgType msgType,string data)
    {
        MQTTMsg msg = new MQTTMsg(msgType,data);
        if(mqtt != null && mqtt.IsConnected && !string.IsNullOrEmpty(machineCode) && msg != null)
        {
            string temp = JsonConvert.SerializeObject(msg);
            Debug.Log("发送==" + temp);
            mqtt.Publish(machineCode, temp);
        }
    }

    private void OnDestroy()
    {
        if (mqtt != null)
        {
            mqtt.DisConnect();
        }
    }

    public static DateTime GetBeiJingTime()
    {
        return DateTime.UtcNow + new TimeSpan(8, 0, 0);
    }

    /// <summary>
    /// 使用设备唯一码计算mqtt连接账户密码
    /// </summary>
    /// <param name="machineCode">设备授权唯一码</param>
    /// <param name="user">用户名</param>
    /// <param name="pwd">密码</param>
    public static void GenUserInfo(string machineCode, out string user, out string pwd)
    {
        const string productKey = "&zsproducts";
        user = machineCode + productKey;
        TimeSpan ts = GetBeiJingTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        var utcTime = Convert.ToInt64(ts.TotalMilliseconds);
        utcTime = 2524608000000;
        string Pwd = string.Format("clientId{0}deviceName{1}productKey{2}timestamp{3}", machineCode, machineCode, "zsproducts", utcTime);
        string key0 = user;

        Encoding encoding = Encoding.ASCII;

        HMACSHA1 myHMACSHA0 = new HMACSHA1(encoding.GetBytes(key0));
        byte[] byteKey = myHMACSHA0.ComputeHash(encoding.GetBytes(machineCode));
        var enText = new StringBuilder();
        foreach (byte iByte in byteKey)
        {
            enText.AppendFormat("{0:X2}", iByte);
        }
        var key1 = enText.ToString();

        encoding = Encoding.ASCII;
        HMACSHA1 myHMACSHA1 = new HMACSHA1(encoding.GetBytes(key1));
        byte[] byteText = myHMACSHA1.ComputeHash(encoding.GetBytes(Pwd));

        enText = new StringBuilder();
        foreach (byte iByte in byteText)
        {
            enText.AppendFormat("{0:X2}", iByte);
        }
        pwd = enText.ToString();
    }

    //private int msgIndex = 0;
    //private void Update()
    //{
    //    //if(Input.GetKeyDown(KeyCode.Space))
    //    //{
    //    //    msgIndex++;
    //    //    MsgType msgType = (MsgType)msgIndex;
    //    //    switch (msgType)
    //    //    {
    //    //        case MsgType.心跳包:
    //    //            Send(msgType,string.Empty);
    //    //            break;
    //    //        case MsgType.安装应用:
    //    //            Send(msgType,JsonConvert.SerializeObject(new CS_安装应用("001",576758124745068544,1)));
    //    //            break;
    //    //        case MsgType.播放视频_启动游戏:
    //    //            Send(msgType,"1");
    //    //            break;
    //    //        case MsgType.获取当前设备已安装产品:
    //    //            break;
    //    //        case MsgType.下载应用_视频成功通知:
    //    //            Send(msgType,JsonConvert.SerializeObject(new CS_下载资源("001",576758124745068544,1)));
    //    //            break;
    //    //        case MsgType.获取服务器视频_游戏列表:
    //    //            Send(msgType,string.Empty);
    //    //            break;
    //    //        case MsgType.设置参数:
    //    //            break;
    //    //        case MsgType.获取设备硬件信息:
    //    //            break;
    //    //        default:
    //    //            break;
    //    //    }
    //    //}

    //    //if(Input.GetKeyDown(KeyCode.P))
    //    //{
    //    //    Send(MsgType.获取服务器视频_游戏列表,string.Empty);
    //    //}
    //}
}
