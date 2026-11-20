using MqttData;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MqttData
{
    public class MQTTMsg
    {
        public MsgType CommandType;

        public NetType Client = NetType.客户端;

        public string Data = string.Empty;

        public MQTTMsg(){ }

        public MQTTMsg(MsgType _CommandType)
        {
            CommandType = _CommandType;
        }

        public MQTTMsg(MsgType _CommandType,string _Data)
        {
            CommandType = _CommandType;
            Data = _Data;
        }
    }

    /// <summary>
    /// 消息类型
    /// </summary>
    public enum MsgType
    {
        心跳包 = 0x01,
        安装应用 = 0x02,
        播放视频_启动游戏 = 0x03,
        获取当前设备已安装产品 = 0x04,
        下载应用_视频成功通知 = 0x05,
        获取服务器视频_游戏列表 = 0x06,
        设置参数 = 0x07,
        获取设备硬件信息 = 0x08,
        支付成功通知 = 0x09,
        照片墙图片推送 = 0x0A,
        游戏结束请求抽奖 = 0x0B
    }

    public enum NetType
    {
        服务器 = 0,
        客户端 = 1,
    }

    /// <summary>
    /// sc - server to client
    /// 用户点击安装时候，服务器先向客户端发送安装指令，客服端启动下载程序成功后，需回复结果，注意当下载成功后需发送5 下载成功通知
    /// </summary>
    public class SC_安装应用
    {
        /// <summary>
        /// 产品编号
        /// </summary>
        public string ProductCode;

        /// <summary>
        /// 产品名称
        /// </summary>
        public string ProductName;

        /// <summary>
        /// 文件唯一ID
        /// </summary>
        public long FileId;

        /// <summary>
        /// 下载应用/视频 Url地址数组
        /// </summary>
        public string[] Urls;
    }

    /// <summary>
    /// cs - client to server
    /// 用户点击安装时候，服务器先向客户端发送安装指令，客服端启动下载程序成功后，需回复结果，注意当下载成功后需发送5 下载成功通知
    /// </summary>
    public class CS_安装应用
    {
        /// <summary>
        /// 产品编号
        /// </summary>
        public string ProductCode;

        /// <summary>
        /// 产品名称
        /// </summary>
        public string ProductName;

        /// <summary>
        /// 文件唯一ID
        /// </summary>
        public long FileId;

        /// <summary>
        /// 1成功 0失败
        /// </summary>
        public int IsSuccess;

        public CS_安装应用(){ }

        /// <summary>
        /// 用户点击安装时候，服务器先向客户端发送安装指令，客服端启动下载程序成功后，需回复结果，注意当下载成功后需发送5 下载成功通知
        /// </summary>
        /// <param name="_ProductCode">产品编号</param>
        /// <param name="_ProductName">产品名称</param>
        /// <param name="_FileId">文件唯一ID</param>
        /// <param name="_IsSuccess">1成功 0失败</param>
        public CS_安装应用(string _ProductCode,string __ProductName, long _FileId,int _IsSuccess)
        {
            ProductCode = _ProductCode;
            ProductName = __ProductName;
            FileId = _FileId;
            IsSuccess = _IsSuccess;
        }
    }

    /// <summary>
    /// 下载应用/视频成功通知
    /// 当安装/下载，应用/视频成功后，需通知服务器结果，服务器正常处理成功后会回复处理结果
    /// </summary>
    public class CS_下载资源
    {
        /// <summary>
        /// 产品编号
        /// </summary>
        public string ProductCode;

        /// <summary>
        /// 文件唯一ID
        /// </summary>
        public long FileId;

        /// <summary>
        /// 1成功 0失败
        /// </summary>
        public int IsSuccess;

        public CS_下载资源(){ }

        /// <summary>
        /// 当安装/下载，应用/视频成功后，需通知服务器结果，服务器正常处理成功后会回复处理结果
        /// </summary>
        /// <param name="_ProductCode">产品编号</param>
        /// <param name="_FileId">文件唯一ID</param>
        /// <param name="_IsSuccess">1成功 0失败</param>
        public CS_下载资源(string _ProductCode,long _FileId,int _IsSuccess)
        {
            ProductCode = _ProductCode;
            FileId = _FileId;
            IsSuccess = _IsSuccess;
        }
    }

    public class 视频_游戏列表
    {
        public List<ProductItem> Data = new List<ProductItem>();
    }

    /// <summary>
    /// 产品
    /// </summary>
    public class ProductItem
    {
        /// <summary>
        /// 产品编号
        /// </summary>
        public string ProductCode;

        /// <summary>
        /// 文件唯一ID
        /// </summary>
        public long FileId;

        /// <summary>
        /// 下载应用/视频 Url地址数组
        /// </summary>
        public string[] Urls;

        /// <summary>
        /// 1已安装 0未安装
        /// </summary>
        public int IsInstall;
    }

    public class CS_抽奖请求
    {
        /// <summary>
        /// 本局游戏的唯一ID必填，如果收到重复会返回历史抽奖结果
        /// </summary>
        public string GameSessionId;

        /// <summary>
        /// 预留的，暂时传空值，后续如果需要扩展玩家信息再用
        /// </summary>
        public string OpenId;

        public CS_抽奖请求() { }

        public CS_抽奖请求(string _GameSessionId, string _OpenId)
        {
            GameSessionId = _GameSessionId;
            OpenId = _OpenId;
        }
    }

    public class SC_抽奖请求
    {
        /// <summary>
        /// 与请求一致/一致
        /// </summary>
        public string GameSessionId;

        /// <summary>
        /// 0=未中奖不打印券；1=打印
        /// </summary>
        public int HasCoupon;

        /// <summary>
        /// 券码
        /// </summary>
        public string IssueNo;

        /// <summary>
        /// 店铺名
        /// </summary>
        public string ShopName;

        /// <summary>
        /// 优惠标题
        /// </summary>
        public string Title;

        /// <summary>
        /// 有效日期  yyyy-MM-dd
        /// </summary>
        public string ValidDate;

        /// <summary>
        /// 限用星期，如「星期一至星期五」
        /// </summary>
        public string WeekDayText;

        /// <summary>
        /// 店铺地址
        /// </summary>
        public string ShopAddress;

        /// <summary>
        /// 二维码内容（必用）
        /// </summary>
        public string QrCodeContent;

        /// <summary>
        /// 与 QrCodeContent 相同，兼容字段
        /// </summary>
        public string VerifyQrCode;
    }
}

public static class MqttEvent
{
    public static event Action<MQTTMsg> Mqttevent;

    public static void MqttData(MQTTMsg mqttmsg)
    {
        Mqttevent?.Invoke(mqttmsg);
    }
}
