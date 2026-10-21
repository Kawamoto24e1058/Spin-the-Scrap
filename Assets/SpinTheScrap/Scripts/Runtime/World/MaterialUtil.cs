using UnityEngine;

namespace SpinTheScrap
{
    /// <summary>
    /// URP（_BaseColor）と Built-in（_Color）の両方で色を扱うための補助。
    /// フェーズ 0〜2 は仮素材（箱・カプセル）で進めるため、マテリアルはコードで作る。
    /// 元にするのは Resources の PrototypeLit（メニュー「Set Up Project」で生成）。どのアセットからも参照されない
    /// シェーダーはビルドから外されてピンクになるため、プリミティブの既定マテリアルは予備としてだけ使う。
    /// </summary>
    public static class MaterialUtil
    {
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int ColorId = Shader.PropertyToID("_Color");

        public const string TemplateResourcePath = "Materials/PrototypeLit";
        /// <summary>光る物（電飾・画面）の元。発光を有効にしたマテリアルがないと、ビルドで発光の版が外される。</summary>
        public const string EmissiveResourcePath = "Materials/PrototypeEmissive";
        /// <summary>3D の文字（TextMesh）のシェーダーをビルドに含めるためのマテリアル。</summary>
        public const string TextResourcePath = "Materials/WorldText";

        public static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        static Material template;
        static Material emissiveTemplate;

        /// <summary>光るマテリアル。intensity が 1 を超えるとブルームでにじむ。</summary>
        public static Material CreateEmissive(Color color, float intensity)
        {
            if (emissiveTemplate == null)
            {
                var asset = Resources.Load<Material>(EmissiveResourcePath);
                emissiveTemplate = asset != null && asset.shader.isSupported ? asset : Create(Color.white);
            }
            var mat = new Material(emissiveTemplate);
            SetColor(mat, color);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            SetEmission(mat, color, intensity);
            return mat;
        }

        public static void SetEmission(Material mat, Color color, float intensity)
        {
            if (mat != null && mat.HasProperty(EmissionColorId)) mat.SetColor(EmissionColorId, color * intensity);
        }

        /// <summary>現在のレンダーパイプラインの既定マテリアルを複製して色を付ける。</summary>
        public static Material Create(Color color)
        {
            if (template == null)
            {
                var asset = Resources.Load<Material>(TemplateResourcePath);
                if (asset != null && asset.shader.isSupported) template = asset;
            }
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
