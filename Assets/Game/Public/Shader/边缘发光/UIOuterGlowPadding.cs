using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("UI/Effects/Outer Glow Padding")]
[RequireComponent(typeof(Image))]
public sealed class UIOuterGlowPadding : BaseMeshEffect
{
    [Tooltip("Extra drawing area on each side, measured in source texture pixels.")]
    [Min(0f)] public float PaddingPixels = 16f;

    public override void ModifyMesh(VertexHelper vertices)
    {
        if (!IsActive() || vertices.currentVertCount != 4 || PaddingPixels <= 0f) return;
        Image image = graphic as Image;
        if (image == null || image.type != Image.Type.Simple || image.useSpriteMesh) return;
        Texture texture = image.mainTexture;
        if (texture == null) return;

        UIVertex vertex = default(UIVertex);
        vertices.PopulateUIVertex(ref vertex, 0);
        Vector2 uvMin = vertex.uv0;
        Vector2 uvMax = uvMin;
        Vector2 positionMin = vertex.position;
        Vector2 positionMax = positionMin;

        for (int i = 1; i < 4; i++)
        {
            vertices.PopulateUIVertex(ref vertex, i);
            uvMin = Vector2.Min(uvMin, vertex.uv0);
            uvMax = Vector2.Max(uvMax, vertex.uv0);
            positionMin = Vector2.Min(positionMin, vertex.position);
            positionMax = Vector2.Max(positionMax, vertex.position);
        }

        Vector2 uvSize = uvMax - uvMin;
        if (uvSize.x <= 0f || uvSize.y <= 0f) return;
        Vector2 uvPadding = new Vector2(PaddingPixels / texture.width, PaddingPixels / texture.height);
        Vector2 growth = new Vector2(1f + 2f * uvPadding.x / uvSize.x, 1f + 2f * uvPadding.y / uvSize.y);
        Vector2 uvCenter = (uvMin + uvMax) * 0.5f;
        Vector2 positionCenter = (positionMin + positionMax) * 0.5f;

        for (int i = 0; i < 4; i++)
        {
            vertices.PopulateUIVertex(ref vertex, i);
            Vector2 uv = uvCenter + Vector2.Scale((Vector2)vertex.uv0 - uvCenter, growth);
            Vector2 position = positionCenter + Vector2.Scale((Vector2)vertex.position - positionCenter, growth);
            vertex.uv0 = uv;
            vertex.position = new Vector3(position.x, position.y, vertex.position.z);
            vertices.SetUIVertex(vertex, i);
        }
    }
}
