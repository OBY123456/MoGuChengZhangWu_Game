using UnityEngine;

public class PirateMemoryGameButton : TouchUIButton
{
    public bool IsCorrectLocked { get; private set; }

    private bool applyingPressedVisual;
    private bool refreshAfterTouch;

    public override void OnTouch(Vector3 point)
    {
        if (IsCorrectLocked) return;
        base.OnTouch(point);
        if (!refreshAfterTouch) return;
        refreshAfterTouch = false;
        RefreshVisual();
    }

    public void LockCorrect()
    {
        IsCorrectLocked = true;
        interactable = false;
    }

    public void ResetForQuestion()
    {
        IsCorrectLocked = false;
        refreshAfterTouch = false;
        interactable = false;
        RefreshVisual();
    }

    protected override void PlayPressedVisual()
    {
        applyingPressedVisual = true;
        base.PlayPressedVisual();
        applyingPressedVisual = false;
    }

    protected override void RefreshVisual()
    {
        // 按下时长为 0 时，也要等本次点击的判定执行完再恢复图片。
        if (applyingPressedVisual)
        {
            refreshAfterTouch = true;
            return;
        }

        if (!IsCorrectLocked) base.RefreshVisual();
    }
}
