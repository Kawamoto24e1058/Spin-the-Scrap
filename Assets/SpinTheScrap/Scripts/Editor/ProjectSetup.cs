using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpinTheScrap.EditorTools
{
    /// <summary>
    /// プロジェクト設定の初期化。URP Asset の作成と Graphics への割り当て、入力方式を Input System にする。
    /// 設定ファイルを手書きせず、ここからエディタに書き出させる。
    /// </summary>
    public static class ProjectSetup
    {
        const string SettingsFolder = "Assets/SpinTheScrap/Settings";
        const string PipelinePath = SettingsFolder + "/URP.asset";
        const string RendererPath = SettingsFolder + "/URP_Renderer.asset";
        const string TemplateMaterialPath = "Assets/SpinTheScrap/Resources/" + MaterialUtil.TemplateResourcePath + ".mat";
        const string DefaultPostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        /// <summary>PlayerSettings.activeInputHandler の値。0 = Input Manager（旧）、1 = Input System、2 = 両方。</summary>
        const int InputSystemOnly = 1;

        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        [MenuItem("Spin the Scrap/Set Up Project (URP + Input System)", priority = 40)]
        public static void SetUpProject()
        {
            bool inputChanged = SetUpInputSystem();
            SetUpUrp();
            AssetDatabase.SaveAssets();
            Debug.Log("[Spin the Scrap] URP と Input System を設定しました。" +
                      (inputChanged ? "入力方式の変更はエディタの再起動後に反映されます。" : ""));
        }

        static void SetUpUrp()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                Directory.CreateDirectory(SettingsFolder);
                AssetDatabase.Refresh();

                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(DefaultPostProcessDataPath);
                AssetDatabase.CreateAsset(renderer, RendererPath);

                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            // 品質レベルごとの上書きがあると既定が使われないので、すべて同じ Asset にそろえる
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            CreateTemplateMaterial(pipeline);
        }

        /// <summary>MaterialUtil が複製する元のマテリアル。Resources に置くとシェーダーがビルドに含まれる。</summary>
        static void CreateTemplateMaterial(UniversalRenderPipelineAsset pipeline)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterialPath);
            if (mat == null)
            {
                var shader = pipeline.defaultShader;
                if (shader == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(TemplateMaterialPath));
                AssetDatabase.Refresh();
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, TemplateMaterialPath);
            }
            // 既定の 0.5 だと懐中電灯の照り返しで近くの物が白く飛ぶ
            mat.SetFloat(SmoothnessId, 0.15f);
            EditorUtility.SetDirty(mat);
        }

        static bool SetUpInputSystem()
        {
            var playerSettings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var so = new SerializedObject(playerSettings);
            var handler = so.FindProperty("activeInputHandler");
            if (handler == null || handler.intValue == InputSystemOnly) return false;
            handler.intValue = InputSystemOnly;
            so.ApplyModifiedProperties();
            return true;
        }
    }
}
