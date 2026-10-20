using UnityEngine;

namespace SpinTheScrap
{
    /// <summary>
    /// URP（_BaseColor）と Built-in（_Color）の両方で色を扱うための補助。
    /// フェーズ 0〜2 は仮素材（箱・カプセル）で進めるため、マテリアルはコードで作る。
    /// </summary>
    public static class MaterialUtil
    {
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int ColorId = Shader.PropertyToID("_Color");

        static Material template;

        /// <summary>現在のレンダーパイプラインの既定マテリアルを複製して色を付ける。</summary>
        public static Material Create(Color color)
        {
            if (template == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                template = probe.GetComponent<Renderer>().sharedMaterial;
                Object.Destroy(probe);
            }
            var mat = new Material(template);
            SetColor(mat, color);
            return mat;
        }

        public static Color GetColor(Material mat)
        {
            if (mat == null) return Color.white;
            if (mat.HasProperty(BaseColorId)) return mat.GetColor(BaseColorId);
            if (mat.HasProperty(ColorId)) return mat.GetColor(ColorId);
            return Color.white;
        }

        public static void SetColor(Material mat, Color color)
        {
            if (mat == null) return;
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
        }
    }
}
