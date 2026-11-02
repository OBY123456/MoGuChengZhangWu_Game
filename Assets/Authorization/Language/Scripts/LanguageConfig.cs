using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;


[Serializable]
public struct LanguageKV
{
    public string Key, Value;
}

[CreateAssetMenu(fileName = "LanguageConfig", menuName = "Create AssetFile/Create LanguageConfig", order = 1)]
[Serializable]
public class LanguageConfig : ScriptableObject
{
    [Header("英文配置")]
    public List<LanguageKV> LanguageEN;

    public LanguageConfig CopyConfig;

    /// <summary>
    /// 中文不需要，默认就是中文
    /// </summary>
    /// <param name="Key"></param>
    /// <returns></returns>
    public string GetValue(string Key)
    {
        string temp = LanguageEN.Find((LanguageKV) => LanguageKV.Key == Key).Value;
        if (!string.IsNullOrEmpty(temp))
            return temp;
        else
            return Key;
    }

    [Button("复制CopyConfig里面的键值对到LanguageEN")]
    public void Copy()
    {
        if (CopyConfig == null || CopyConfig.LanguageEN == null)
            return;

        if (LanguageEN == null)
            LanguageEN = new List<LanguageKV>();

        HashSet<string> existingKeys = new HashSet<string>();
        foreach (LanguageKV item in LanguageEN)
        {
            existingKeys.Add(item.Key);
        }

        foreach (LanguageKV item in CopyConfig.LanguageEN)
        {
            // LanguageEN 中已有相同 Key，则保留原值，不覆盖
            if (existingKeys.Contains(item.Key))
                continue;

            LanguageEN.Add(item);
            existingKeys.Add(item.Key);
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}

