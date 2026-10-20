using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpinTheScrap.Core;
using SpinTheScrap.Data;
using SpinTheScrap.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpinTheScrap.EditorTools
{
    /// <summary>メニュー「Spin the Scrap」。シーンやアセットをテキストで手書きせず、エディタに生成させる。</summary>
    public static class SpinTheScrapMenu
    {
        const string ScenePath = "Assets/SpinTheScrap/Scenes/Phase0.unity";
        const string DataFolder = "Assets/SpinTheScrap/Data";

        [MenuItem("Spin the Scrap/Create Phase 0 Scene", priority = 0)]
        public static void CreatePhase0Scene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Phase 0", ScenePath + " を作り直しますか？", "作り直す", "キャンセル"))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Phase0");
            root.AddComponent<Phase0Bootstrap>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            Debug.Log("[Spin the Scrap] " + ScenePath + " を作成しました。Play で開始できます。");
        }

        [MenuItem("Spin the Scrap/Generate MVP Content Assets", priority = 20)]
        public static void GenerateMvpContentAssets()
        {
            Directory.CreateDirectory(DataFolder + "/Symbols");
            Directory.CreateDirectory(DataFolder + "/Relics");
            AssetDatabase.Refresh();

            var database = LoadOrCreate<ContentDatabaseAsset>(DataFolder + "/ContentDatabase.asset");
            database.symbols = MvpContent.CreateSymbols()
                .Select(def => Upsert<SymbolDefinitionAsset>(DataFolder + "/Symbols/" + def.Id + ".asset", a => a.CopyFrom(def)))
                .ToList();
            database.relics = MvpContent.CreateRelics()
                .Select(def => Upsert<RelicDefinitionAsset>(DataFolder + "/Relics/" + def.Id + ".asset", a => a.CopyFrom(def)))
                .ToList();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();

            Selection.activeObject = database;
            Debug.Log("[Spin the Scrap] " + DataFolder + " に MVP のシンボル・レリックを書き出しました。" +
                      "RunSession の Content Asset に ContentDatabase を割り当てると、こちらの値が使われます。");
        }

        static T Upsert<T>(string path, System.Action<T> fill) where T : ScriptableObject
        {
            var asset = LoadOrCreate<T>(path);
            fill(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void AddSceneToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
