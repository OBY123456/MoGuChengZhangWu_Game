using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 播放序列帧，可挂在 Image 或 SpriteRenderer 上
/// </summary>
public class PlaySprite : MonoBehaviour
{
    private Image ImageSource;
    private SpriteRenderer SpriteSource;
    private int mCurFrame = 0;
    private float mDelta = 0;

    [Header("帧率")]
    public float FPS = 25;

    [Header("序列帧图片")]
    public Sprite[] SpriteFrames;

    [Header("是否播放（true和Loop == 0时自动播放）")]
    public bool IsPlaying = false;

    [Header("循环帧次数")]
    public int LoopTimes = 1;

    [Header("循环帧")]
    public int LoopFrame = 0;

    private int _Looptimes = 0;

    public CanvasGroup canvasGroup;

    public Action action;

    public int FrameCount
    {
        get
        {
            if(SpriteFrames != null)
            {
                return SpriteFrames.Length;
            }
            else
            {
                return 0;
            }
        }
    }
    void Awake()
    {
        ImageSource = GetComponent<Image>();
        SpriteSource = GetComponent<SpriteRenderer>();
        if (SpriteSource == null) SpriteSource = GetComponentInChildren<SpriteRenderer>(true);
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public void SetSpriteFrames(Sprite[] sprites)
    {
        SpriteFrames = sprites;
        SetSprite(0);
    }

    private void SetSprite(int idx)
    {
        if(SpriteFrames != null && FrameCount > 0)
        {
            int frame = Mathf.Clamp(idx, 0, FrameCount - 1);
            if (ImageSource != null) ImageSource.sprite = SpriteFrames[frame];
            if (SpriteSource != null) SpriteSource.sprite = SpriteFrames[frame];
        }
    }

    private void OnEnable()
    {
        if(IsPlaying && LoopTimes == 0)
        {
            Play();
        }
    }

    private void OnDisable()
    {
        if(IsPlaying && LoopTimes == 0)
        {
            Stop();
        }
    }

    void Update()
    {
        if (!IsPlaying || 0 == FrameCount)
        {
            return;
        }
        else
        {
            mDelta += Time.deltaTime;
            if (mDelta > 1 / FPS)
            {
                mDelta = 0;
                mCurFrame++;
                if (mCurFrame >= FrameCount)
                {
                    //0就是一直循环
                    if (LoopTimes != 0)
                    {
                       _Looptimes++;
                        if (_Looptimes == LoopTimes)
                        {
                            action?.Invoke();
                            Stop();
                            return;
                        }
                    }
                    mCurFrame = LoopFrame;
                }
                SetSprite(mCurFrame);
            }
        }
    }

    public void LoopPlay()
    {
        if (!IsPlaying)
        {
            if (SpriteSource != null) SpriteSource.enabled = true;
            LoopTimes = 0;
            IsPlaying = true;
            SetSprite(0);
            mDelta = 0;
            mCurFrame = 0;
            if(canvasGroup != null)
            canvasGroup.alpha = 1;
        }
    }

    public void Play()
    {
        if(!IsPlaying)
        {
            if (SpriteSource != null) SpriteSource.enabled = true;
            SetSprite(0);
            mDelta = 0;
            mCurFrame = 0;
            IsPlaying = true;
            _Looptimes = 0;
            if(canvasGroup != null)
            canvasGroup.alpha = 1;
        }
    }

    public void Stop()
    {
        if(IsPlaying)
        {
            IsPlaying = false;
            if (SpriteSource != null) SpriteSource.enabled = false;
            if(canvasGroup != null)
            canvasGroup.alpha = 0;
            SetSprite(0);
            mDelta = 0;
            mCurFrame = 0;
            _Looptimes = 0;
        }
    }

    public void Reset()
    {
        IsPlaying = false;
        if (SpriteSource != null) SpriteSource.enabled = false;
        if(canvasGroup != null)
        canvasGroup.alpha = 0;
        SetSprite(0);
        mDelta = 0;
        mCurFrame = 0;
        _Looptimes = 0;
    }
}
