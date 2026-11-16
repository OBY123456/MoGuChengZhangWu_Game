using UnityEngine;
using MTPanel;

/// <summary>
/// 封装类
/// </summary>
public class DataClass
{
    public string className;
    public string classJson;
    public DataClass() { }
    public DataClass(string str0, string str1)
    {
        className = str0;
        classJson = str1;
    }
}

/// <summary>
/// UDP传输的全部的数据类型枚举
/// </summary>
public enum ClassEnum
{
    UdpGameName,
    GameData,
    OrderData,
    HeartbeatData,
}

/// <summary>
/// 场景名字
/// </summary>
public class UdpGameName
{
    public string GameName;

    public UdpGameName() { }

    public UdpGameName(string _Scenename)
    {
        GameName = _Scenename;
    }
}

/// <summary>
/// 游戏选择
/// </summary>
public class GameData
{
    public PanelName panelName;

    public int Times;

    public float Moeny;

    public GameData() { }

    public GameData(PanelName _panelName, int _Times = -1, float _Moeny = 0)
    {
        panelName = _panelName;
        Times = _Times;
        Moeny = _Moeny;
    }
}

public enum Order
{
    下一关,
    返回首页,
}

public class OrderData
{
    public Order order;

    public OrderData() { }

    public OrderData(Order _order)
    {
        order = _order;
    }
}

/// <summary>
/// 自动发现和心跳消息，仅由网络脚本处理，不派发游戏事件。
/// </summary>
public enum HeartbeatType
{
    None,
    Discover,
    Offer,
    Ping,
    Pong,
}

public class HeartbeatData
{
    public HeartbeatType Type;

    public HeartbeatData() { }

    public HeartbeatData(HeartbeatType type)
    {
        Type = type;
    }
}

/// <summary>
/// 统一封装 UDP 数据，classJson 保存具体消息的 JSON。
/// </summary>
public static class ClassToJson
{
    private static readonly Newtonsoft.Json.JsonSerializerSettings Settings = new Newtonsoft.Json.JsonSerializerSettings { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.None, MaxDepth = 32 };

    public static System.Type GetDataType(ClassEnum classEnum)
    {
        switch (classEnum)
        {
            case ClassEnum.UdpGameName: return typeof(UdpGameName);
            case ClassEnum.GameData: return typeof(GameData);
            case ClassEnum.OrderData: return typeof(OrderData);
            case ClassEnum.HeartbeatData: return typeof(HeartbeatData);
            default: return null;
        }
    }

    public static bool TryGetClassEnum(DataClass data, out ClassEnum classEnum)
    {
        classEnum = default(ClassEnum);
        return data != null && System.Enum.TryParse(data.className, false, out classEnum) && GetDataType(classEnum) != null && classEnum.ToString() == data.className;
    }

    public static T Parse<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return null;
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, Settings);
    }

    public static bool TryParseEnvelope(string json, out DataClass data)
    {
        data = null;
        try
        {
            DataClass parsed = Parse<DataClass>(json);
            ClassEnum classEnum;
            if (!TryGetClassEnum(parsed, out classEnum) || string.IsNullOrWhiteSpace(parsed.classJson)) return false;
            data = parsed;
            return true;
        }
        catch (System.Exception) { return false; }
    }

    public static DataClass CreateData(ClassEnum classEnum, object data)
    {
        if (data == null || data.GetType() != GetDataType(classEnum)) return null;
        try
        {
            string classJson = Newtonsoft.Json.JsonConvert.SerializeObject(data, Settings);
            return new DataClass(classEnum.ToString(), classJson);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("CreateData 序列化失败：" + exception.Message);
            return null;
        }
    }

    public static string GetJson(ClassEnum classEnum, object data)
    {
        return GetJson(CreateData(classEnum, data));
    }

    public static string GetJson(DataClass data)
    {
        ClassEnum classEnum;
        if (!TryGetClassEnum(data, out classEnum) || string.IsNullOrWhiteSpace(data.classJson)) return string.Empty;
        return Newtonsoft.Json.JsonConvert.SerializeObject(data, Settings);
    }
}