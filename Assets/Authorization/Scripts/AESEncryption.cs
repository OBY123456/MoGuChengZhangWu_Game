using System;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using UnityEngine;

//namespace Encryption
//{
/// <summary>
/// AES加密
/// </summary>
public static class AESEncryption
{
    public static readonly string AndroidUrl = @"/storage/emulated/0";

    public static readonly string FolderName = "Cache";

    //默认密钥向量
    private static byte[] _key1 = { 0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD, 0xEF };

    /// <summary>
            /// AES加密算法
            /// </summary>
            /// <param name="plainText">明文字符串</param>
            /// <param name="strKey">密钥</param>
            /// <returns>返回加密后的密文字节数组</returns>
    public static byte[] AESEncrypt(string plainText, string strKey)
    {
        //分组加密算法
        SymmetricAlgorithm des = Rijndael.Create();
        byte[] inputByteArray = Encoding.UTF8.GetBytes(plainText);//得到需要加密的字节数组

        var key = Encoding.UTF8.GetBytes(strKey);
        //设置密钥及密钥向量
        byte[] Key = new byte[32];
        Array.Copy(key, 0, Key, 0, key.Length > 32 ? 32 : key.Length);
        des.Mode = CipherMode.ECB;
        des.KeySize = 256;
        des.BlockSize = 128;
        des.Padding = PaddingMode.PKCS7;
        des.Key = Key;
        des.IV = _key1;
        ICryptoTransform encryptor = des.CreateEncryptor(des.Key, des.IV);
        MemoryStream ms = new MemoryStream();
        CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        cs.Write(inputByteArray, 0, inputByteArray.Length);
        cs.FlushFinalBlock();
        byte[] cipherBytes = ms.ToArray();//得到加密后的字节数组
        cs.Close();
        ms.Close();
        return cipherBytes;
    }

    /// <summary>
            /// AES解密
            /// </summary>
            /// <param name="cipherText">密文字节数组</param>
            /// <param name="strKey">密钥</param>
            /// <returns>返回解密后的字符串</returns>
    public static string AESDecrypt(byte[] cipherText, string strKey)
    {
        SymmetricAlgorithm des = Rijndael.Create();

        var key = Encoding.UTF8.GetBytes(strKey);
        //设置密钥及密钥向量
        byte[] Key = new byte[32];
        Array.Copy(key, 0, Key, 0, key.Length > 32 ? 32 : key.Length);
        Array.Resize(ref Key, 32);
        des.Mode = CipherMode.ECB;
        des.KeySize = 256;
        des.BlockSize = 128;
        des.Padding = PaddingMode.PKCS7;
        des.Key = Key;
        des.IV = _key1;
        ICryptoTransform encryptor = des.CreateDecryptor(des.Key, des.IV);
        using (MemoryStream msDecrypt = new MemoryStream(cipherText))
        {
            using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, encryptor, CryptoStreamMode.Read))
            {
                using (StreamReader srDecrypt = new StreamReader(csDecrypt, Encoding.UTF8))
                {
                    // Read the decrypted bytes from the decrypting stream
                    // and place them in a string.
                    return srDecrypt.ReadToEnd();
                }
            }
        }
    }

    public static string Encrypt(string plainText, string strKey)
    {
        return Convert.ToBase64String(AESEncrypt(plainText, strKey));
    }

    public static string Decrypt(string plainText, string strKey)
    {
        return AESDecrypt(Convert.FromBase64String(plainText), strKey);
    }

    /// <summary>
    /// 加密
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <param name="AesKey">秘钥</param>
    public static void PlayerSet(string key, string value, string AesKey)
    {
        PlayerPrefs.SetString(key, value + "|" + Encrypt(value, AesKey));
    }

    /// <summary>
    /// 解密
    /// </summary>
    /// <param name="key"></param>
    /// <param name="AesKey"></param>
    /// <returns></returns>
    public static void PlayerGet(string key, string AesKey, ref string V1, ref string V2)
    {
        if (PlayerPrefs.HasKey(key))
        {
            string _content = PlayerPrefs.GetString(key);
            string[] vs = _content.Split('|');
            if (vs.Length >= 2)
            {
                V1 = vs[0];
                V2 = Decrypt(vs[1], AesKey);
                return;
            }
        }
        V1 = string.Empty;
        V2 = " ";
    }

    /// <summary>
    /// 写入- 解决异常关闭程序时数据不是实时保存的问题
    /// </summary>
    /// <param name="FileName">文件名</param>
    /// <param name="Content">内容</param>
    /// <param name="FolderName">文件夹名字,如果不需要则空着</param>
    public static void WriteAllText(string FileName, string Content, string FolderName = "")
    {
        string FolderPath;
        if (Application.platform == RuntimePlatform.Android && Application.platform != RuntimePlatform.WindowsEditor)
        {
            FolderPath = AndroidUrl + "/" + FolderName;
        }
        else
        {
            FolderPath = Application.persistentDataPath + "/" + FolderName;
        }

        Loom.Initialize();
        Loom.RunAsync(() =>
        {
            if (!string.IsNullOrEmpty(FolderName) && !string.IsNullOrWhiteSpace(FolderName))
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                string FilePath = FolderPath + "/" + FileName;
                if (File.Exists(FilePath))
                {
                    string Temp = File.ReadAllText(FilePath);
                    if (!string.IsNullOrEmpty(Temp))
                    {
                        string NawPath = FolderPath + "/" + FileName + ".Temp";
                        File.Copy(FilePath, NawPath, true);
                    }
                }
                Debug.Log("写入文件路径：" + FilePath);
                FileHandle.WriteAllText(FilePath.Trim(),Content);

            }
            else
            {
                string FilePath = FolderPath + FileName;
                if (File.Exists(FilePath))
                {
                    string Temp = File.ReadAllText(FilePath);
                    if (!string.IsNullOrEmpty(Temp))
                    {
                        string NawPath = FolderPath + FileName + ".Temp";
                        File.Copy(FilePath, NawPath, true);
                    }
                }

                Debug.Log("写入文件路径：" + FilePath);
                FileHandle.WriteAllText(FilePath.Trim(),Content);
            }
        });

    }

    /// <summary>
    /// 读取
    /// </summary>
    /// <param name="FileName">文件名</param>
    /// <param name="FolderName">文件夹名称，与写入保持一致</param>
    /// <returns></returns>
    public static string ReadAllText(string FileName, string FolderName = "")
    {
        string FolderPath;
        if (Application.platform == RuntimePlatform.Android && Application.platform != RuntimePlatform.WindowsEditor)
        {
            FolderPath = AndroidUrl + "/" + FolderName;
        }
        else
        {
            FolderPath = Application.persistentDataPath + "/" + FolderName;
        }

        string FilePath = string.Empty;
        string TempPath = string.Empty;
        if (!string.IsNullOrEmpty(FolderName) && !string.IsNullOrWhiteSpace(FolderName))
        {
            FilePath = FolderPath + "/" + FileName;
            TempPath = FolderPath + "/" + FileName + ".Temp";
        }
        else
        {
            FilePath = FolderPath + FileName;
            TempPath = FolderPath + FileName + ".Temp";
        }

        if (!File.Exists(FilePath))
        {
            if (File.Exists(TempPath))
            {
                Debug.Log("读取文件路径：" + FilePath);
                return FileHandle.ReadAllText(TempPath);
            }
            return string.Empty;
        }
        else
        {
            Debug.Log("读取文件路径：" + FilePath);
            return FileHandle.ReadAllText(FilePath);
        }
    }

    /// <summary>
    /// 是否存在文件
    /// </summary>
    /// <param name="FileName"></param>
    /// <param name="FolderName"></param>
    /// <returns></returns>
    public static bool Exists(string FileName, string FolderName = "")
    {
        string FolderPath;
        if (Application.platform == RuntimePlatform.Android && Application.platform != RuntimePlatform.WindowsEditor)
        {
            FolderPath = AndroidUrl + "/" + FolderName;
        }
        else
        {
            FolderPath = Application.persistentDataPath + "/" + FolderName;
        }

        string FilePath = string.Empty;
        string TempPath = string.Empty;
        if (!string.IsNullOrEmpty(FolderName) && !string.IsNullOrWhiteSpace(FolderName))
        {
            FilePath = FolderPath + "/" + FileName;
            TempPath = FolderPath + "/" + FileName + ".Temp";
        }
        else
        {
            FilePath = FolderPath + FileName;
            TempPath = FolderPath + FileName + ".Temp";
        }

        if (File.Exists(FilePath))
        {
            return true;
        }

        if (File.Exists(TempPath))
        {
            return true;
        }

        return false;
    }

    public static void Delete(string FileName, string FolderName = "")
    {
        string FolderPath;
        if (Application.platform == RuntimePlatform.Android && Application.platform != RuntimePlatform.WindowsEditor)
        {
            FolderPath = AndroidUrl + "/" + FolderName;
        }
        else
        {
            FolderPath = Application.persistentDataPath + "/" + FolderName;
        }

        string FilePath = string.Empty;
        string TempPath = string.Empty;
        if (!string.IsNullOrEmpty(FolderName) && !string.IsNullOrWhiteSpace(FolderName))
        {
            FilePath = FolderPath + "/" + FileName;
            TempPath = FolderPath + "/" + FileName + ".Temp";
        }
        else
        {
            FilePath = FolderPath + FileName;
            TempPath = FolderPath + FileName + ".Temp";
        }

        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }

        if (File.Exists(TempPath))
        {
            File.Delete(TempPath);
        }
    }

    public static DateTime ToBeiJingTime(this DateTime time)
    {
        return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(time, "China Standard Time");
    }
}

//}
