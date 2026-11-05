using System.Collections.Generic;
using Lean.Pool;
using UnityEngine;

public abstract class BaseTouch : MonoBehaviour
{
    [Header("点击特效")]
    public GameObject clickEffectPrefab;

    [Tooltip("生成特效的统一缩放值，X、Y、Z 均使用此值")]
    public float effectScale = 1f;

    [Tooltip("世界空间点击特效使用的摄像机；不赋值时使用 Camera.main")]
    public Camera clickEffectCamera;

    [Tooltip("特效存活时间，秒。小于或等于 0 时不会自动回收到 LeanPool。")]
    public float effectLifeTime = 1f;

    private sealed class EffectSorting
    {
        public GameObject Root;
        public SpriteRenderer[] Sprites;
    }

    // 所有 BaseTouch 子类共享，避免不同道具各自从相同层级开始分配。
    private static readonly List<EffectSorting> activeEffects = new List<EffectSorting>();
    private static int nextEffectSortingOrder = 1;

    protected virtual void Awake()
    {
        gameObject.layer = LayerMask.NameToLayer("Touch");
    }

    public virtual void OnTouch(Vector3 point)
    {
        if (!isActiveAndEnabled || clickEffectPrefab == null) return;
        if (!TryGetEffectPosition(out Vector3 position)) return;
        GameObject effect = LeanPool.Spawn(clickEffectPrefab, position, clickEffectPrefab.transform.rotation, null);
        if (effect == null) return;
        effect.transform.localScale = Vector3.one * effectScale;
        PlaySprite player = effect.GetComponentInChildren<PlaySprite>(true);
        if (player != null) { player.Reset(); player.Play(); }
        AssignEffectSortingOrders(effect);
        if (effectLifeTime > 0f) LeanPool.Despawn(effect, effectLifeTime);
    }

    private static void AssignEffectSortingOrders(GameObject effect)
    {
        SpriteRenderer[] sprites = effect.GetComponentsInChildren<SpriteRenderer>(true);
        if (sprites.Length == 0) return;

        // 对象池复用时移除旧记录；子节点暂时隐藏时仍保留其层级。
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            EffectSorting item = activeEffects[i];
            if (item.Root == null || !item.Root.activeInHierarchy || item.Root == effect) activeEffects.RemoveAt(i);
        }
        if (activeEffects.Count == 0) nextEffectSortingOrder = 1;

        // 稳定排序：保留原来的前后关系，相同 Order 按节点遍历顺序分配。
        for (int i = 1; i < sprites.Length; i++)
        {
            SpriteRenderer sprite = sprites[i];
            int j = i - 1;
            while (j >= 0 && sprites[j].sortingOrder > sprite.sortingOrder)
            {
                sprites[j + 1] = sprites[j];
                j--;
            }
            sprites[j + 1] = sprite;
        }

        // 接近 Unity 的 Order 范围上限时压缩编号，保持现存特效的前后顺序。
        if (nextEffectSortingOrder > short.MaxValue - sprites.Length + 1)
        {
            int count = sprites.Length;
            foreach (EffectSorting item in activeEffects)
            {
                foreach (SpriteRenderer sprite in item.Sprites) if (sprite != null) count++;
            }
            if (count > ushort.MaxValue + 1)
            {
                Debug.LogWarning("点击特效的 SpriteRenderer 数量超过 sortingOrder 可分配范围。", effect);
                return;
            }
            nextEffectSortingOrder = Mathf.Min(1, short.MaxValue - count + 1);
            foreach (EffectSorting item in activeEffects)
            {
                foreach (SpriteRenderer sprite in item.Sprites) if (sprite != null) sprite.sortingOrder = nextEffectSortingOrder++;
            }
        }

        foreach (SpriteRenderer sprite in sprites) sprite.sortingOrder = nextEffectSortingOrder++;
        activeEffects.Add(new EffectSorting { Root = effect, Sprites = sprites });
    }

    private bool TryGetEffectPosition(out Vector3 position)
    {
        position = transform.position;
        RectTransform rect = transform as RectTransform;
        if (rect == null)
        {
            Renderer renderer = GetComponentInChildren<Renderer>();
            if (renderer != null) position = renderer.bounds.center;
            return true;
        }
        Vector3 center = rect.TransformPoint(rect.rect.center);
        Camera camera = clickEffectCamera != null ? clickEffectCamera : Camera.main;
        if (camera == null) return false;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(GetCanvasCamera(rect), center);
        Ray ray = camera.ScreenPointToRay(screen);
        Plane plane = new Plane(Vector3.forward, Vector3.zero);
        if (!plane.Raycast(ray, out float distance)) return false;
        position = ray.GetPoint(distance);
        return true;
    }

    private static Camera GetCanvasCamera(Transform target)
    {
        Canvas canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        Canvas root = canvas.rootCanvas;
        if (root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return root.worldCamera != null ? root.worldCamera : Camera.main;
    }
}
