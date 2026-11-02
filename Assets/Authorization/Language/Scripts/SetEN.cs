using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LanguageSetting;
using TMPro;

public class SetEN : MonoBehaviour
{
    private Text text;
    private TextMeshProUGUI textMesh;

    private void Awake()
    {
        text = GetComponent<Text>();
        textMesh = GetComponent<TextMeshProUGUI>();
    }

    // Start is called before the first frame update
    void Start()
    {
        if(text != null)
        {
            text.text = LanguageManger.Instance.GetValue(text.text);
        }

        if(textMesh != null)
        {
            textMesh.text = LanguageManger.Instance.GetValue(textMesh.text);
        }
    }
}
