using MqttData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

public class MQTT 
{
    MqttClient mqttClient;

    public static readonly string Host = "www.junziboy.com";

    public static readonly int Port = 7020;

    private const string Zsproducts = "/zsproducts/";
    private const string User = "/user/";
    private const string Get = "get";
    private const string Update = "update";

    protected Queue<MQTTMsg> MsgQueue = new Queue<MQTTMsg>();

    /// <summary>
    /// 是否已建立连接
    /// </summary>
    public bool IsConnected
    {
        get
        {
            if (mqttClient == null)
            {
                return false;
            }
            return mqttClient.IsConnected;
        }
    }

    /// <summary>
    /// 创建mqtt
    /// </summary>
    public  MQTT()
    {
        mqttClient = new MqttClient(Host, Port, false, null);
        mqttClient.MqttMsgPublishReceived += MqttClient_MqttMsgPublishReceived;
    }

    /// <summary>
    /// 建立连接，当该方法建立连接会在调用后立即改变IsConnected状态值 因为该方法是会等待连接成功后返回
    /// </summary>
    /// <param name="machineCode">clientid 这里使用授权唯一码</param>
    /// <param name="usr">用户名</param>
    /// <param name="pwd">密码</param>
    /// <returns> MqttMsgConnack 状态值</returns>
    public bool ConnectMqtt(string machineCode, string usr,string pwd)
    {
        String ClientId = string.Format("{0}|securemode=3,timestamp={1},signmethod=hmacsha1", machineCode, 2524608000000);
        return MqttMsgConnack.CONN_ACCEPTED == mqttClient.Connect(ClientId, usr, pwd, true, 30);
    }

    /// <summary>
    /// 发送指定主题的消息
    /// </summary>
    /// <param name="machineCode">主题</param>
    /// <param name="msg">消息</param>
    public void Publish(string machineCode,string msg)
    {
        if (mqttClient != null && !string.IsNullOrEmpty(machineCode) && !string.IsNullOrEmpty(msg))
        {
            string topic = SpliceTopic(machineCode, Update);
            //Debug.Log("发送主题==" + topic);
            byte[] vs = Encoding.UTF8.GetBytes(msg);
            string temp = BitConverter.ToString(vs, 0).Replace("-", string.Empty).ToUpper();
            //Debug.Log("16进制==" + temp);
            mqttClient.Publish(topic, Encoding.UTF8.GetBytes(temp));
        }
    }


    /// <summary>
    /// 订阅指定主题的消息
    /// </summary>
    /// <param name="topic">主题</param>
    /// <param name="qosLevel">消息qos级别</param>
    public void Subscribe(string machineCode, byte qosLevel = MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE)
    {
        
        if (mqttClient != null && !string.IsNullOrEmpty(machineCode))
        {
            string topic = SpliceTopic(machineCode, Get);
            //Debug.Log("接收主题==" + topic);
            mqttClient.Subscribe(new string[] { topic }, new byte[] { qosLevel });
        }
    }

    /// <summary>
    /// 拼接主题
    /// </summary>
    /// <param name="machineCode">唯一码</param>
    /// <param name="Topic">主题</param>
    /// <returns></returns>
    private string SpliceTopic(string machineCode,string Topic)
    {
        StringBuilder str = new StringBuilder();
        str.Append(Zsproducts);
        str.Append(machineCode);
        str.Append(User);
        str.Append(Topic);
        return str.ToString();
    }

    public void UnSubscribe(string topic)
    {
        if (mqttClient != null && !string.IsNullOrEmpty(topic))
        {
            mqttClient.Unsubscribe(new string[] { topic});
        }
    }


    public void DisConnect()
    {
        if (mqttClient != null)
        {
            try
            {
                mqttClient.Disconnect();
            }
            catch
            {
                Debug.LogError("远程主机强迫关闭了一个现有的连接");
            }
        }
    }


    /// <summary>
    /// 当收到消息时触发
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e">消息内容</param>
    private void MqttClient_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
    {
        try
        {
            string temp = BitConverter.ToString(e.Message, 0).Replace("-", string.Empty).ToUpper();
            string msg = Encoding.UTF8.GetString(e.Message);
            List<byte> bs = new List<byte>();
            for (int i = 0; i < msg.Length; i += 2)
            {
                bs.Add(Convert.ToByte(msg.Substring(i, 2), 16));
            }
            msg = Encoding.UTF8.GetString(bs.ToArray());
            if(!string.IsNullOrEmpty(msg) && !string.IsNullOrWhiteSpace(msg))
            {
                //Debug.Log("Mqtt:" + msg);
                MqttEvent.MqttData((Newtonsoft.Json.JsonConvert.DeserializeObject<MQTTMsg>(msg)));
            }
        }
        catch (Exception ex)
        {
            Debug.Log(ex.Message);
        }
    }
}
