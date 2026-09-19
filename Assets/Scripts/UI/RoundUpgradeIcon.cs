using UnityEngine;
using UnityEngine.UI;

/// <summary>用 UI 顶点绘制清晰的升级箭头；无需位图导入或每次升级生成纹理。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RoundUpgradeIcon : MaskableGraphic
{
    /// <summary>随 RectTransform 尺寸绘制箭头轮廓，保持小尺寸与换行布局下的可读性。</summary>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2[] points = { new Vector2(.12f,.55f), new Vector2(.5f,.95f), new Vector2(.88f,.55f),
            new Vector2(.63f,.55f), new Vector2(.63f,.08f), new Vector2(.37f,.08f), new Vector2(.37f,.55f) };
        foreach (Vector2 point in points)
            vh.AddVert(new Vector3(rect.x + point.x * rect.width, rect.y + point.y * rect.height), color, Vector2.zero);
        vh.AddTriangle(0,1,2); vh.AddTriangle(3,4,5); vh.AddTriangle(3,5,6);
    }
}
