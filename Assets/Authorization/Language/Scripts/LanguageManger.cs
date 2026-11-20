using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum LanguageType
{
    中文,
    英文,
}

public class LanguageManger : MonoSingle<LanguageManger>
{
    private LanguageConfig languageConfig;

    private LanguageType languageType;

    protected override void Awake()
    {
        base.Awake();
        languageType = LanguageType.英文;
    }

    /// <summary>
    /// 静态文本通过key值获取
    /// </summary>
    /// <param name="Key"></param>
    /// <returns></returns>
    public string GetValue(string Key)
    {
        if (languageConfig == null)
            languageConfig = Resources.Load<LanguageConfig>("Language/LanguageConfig");

        if (GetLanguageType().Equals(LanguageType.英文))
            return languageConfig.GetValue(Key);

        return Key;
    }

    public string GetENValue(string Key)
    {
        if (languageConfig == null)
            languageConfig = Resources.Load<LanguageConfig>("Language/LanguageConfig");

        return languageConfig.GetValue(Key);
    }

    /// <summary>
    /// 动态文本通过获取语言类型自己赋值
    /// </summary>
    /// <returns></returns>
    public LanguageType GetLanguageType()
    {
        if (languageConfig == null)
            languageConfig = Resources.Load<LanguageConfig>("Language/LanguageConfig");

        return languageType;
    }
}

