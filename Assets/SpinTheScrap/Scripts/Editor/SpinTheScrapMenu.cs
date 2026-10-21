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
        const string PrefabFolder = "Assets/SpinTheScrap/Prefabs";
        const string NetPlayerPath = PrefabFolder + "/NetPlayer.prefab";
        const string NetSessionPath = PrefabFolder + "/NetSession.prefab";
        const string NetEnemyPath = PrefabFolder + "/NetEnemy.prefab";
        const string NetScrapPath = PrefabFolder + "/NetScrap.prefab";

        [MenuItem("Spin the Scrap/Create Phase 0 Scene", priority = 0)]
        public static void CreatePhase0Scene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Phase 0", ScenePath + " を作り直しますか？", "作り直す", "キャンセル"))
                return;
            RebuildPhase0Scene();
        }

        /// <summary>確認なしでシーンを作り直す（自動化・MCP から呼ぶ用）。</summary>
        public static void RebuildPhase0Scene()
        {

            var prefabs = CreateNetworkPrefabs();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 協力プレイ用の NetworkManager（ローカルネットワークの Tugboat）。プレイヤーの分身は接続時に PlayerSpawner が出す
            var netGo = new GameObject("NetworkManager");
            var manager = netGo.AddComponent<FishNet.Managing.NetworkManager>();
            var objects = AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.PrefabObjects>("Assets/DefaultPrefabObjects.asset");
            if (objects != null) manager.SpawnablePrefabs = objects;
            netGo.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();
            var spawner = netGo.AddComponent<FishNet.Component.Spawning.PlayerSpawner>();
            spawner.SetPlayerPrefab(prefabs.player);

            var root = new GameObject("Phase0");
            var bootstrap = root.AddComponent<Phase0Bootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("networkManager").objectReferenceValue = manager;
            so.FindProperty("netSessionPrefab").objectReferenceValue = prefabs.session;
            so.FindProperty("netEnemyPrefab").objectReferenceValue = prefabs.enemy;
            so.FindProperty("netScrapPrefab").objectReferenceValue = prefabs.scrap;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            Debug.Log("[Spin the Scrap] " + ScenePath + " を作成しました。Play で開始できます。");
        }

        /// <summary>協力プレイのプレハブ（プレイヤーの分身・共有状態）を作る。FishNet の DefaultPrefabObjects に自動で載る。</summary>
        static (FishNet.Object.NetworkObject player, Net.NetSession session, Net.NetEnemy enemy, Net.NetScrap scrap) CreateNetworkPrefabs()
        {
            Directory.CreateDirectory(PrefabFolder);
            AssetDatabase.Refresh();

            var player = LoadOrCreatePrefab(NetPlayerPath, go =>
            {
                go.AddComponent<FishNet.Object.NetworkObject>();
                go.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
                go.AddComponent<Net.NetPlayer>();
            });
            var session = LoadOrCreatePrefab(NetSessionPath, go =>
            {
                go.AddComponent<FishNet.Object.NetworkObject>();
                go.AddComponent<Net.NetSession>();
            });
            // 敵：位置はホストが決める（サーバー権威の NetworkTransform）
            var enemy = LoadOrCreatePrefab(NetEnemyPath, go =>
            {
                go.AddComponent<FishNet.Object.NetworkObject>();
                var transform = go.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
                var so = new SerializedObject(transform);
                so.FindProperty("_clientAuthoritative").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                go.AddComponent<Net.NetEnemy>();
            });
            // ガラクタ：位置は持ち主が決める（拾った人。誰も持っていなければホスト）
            var scrap = LoadOrCreatePrefab(NetScrapPath, go =>
            {
                go.AddComponent<FishNet.Object.NetworkObject>();
                go.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
                go.AddComponent<Net.NetScrap>();
            });
            // 抜けた人が持っていた物は消さずに残す（NetScrap がその場に戻す）
            var scrapNob = new SerializedObject(scrap.GetComponent<FishNet.Object.NetworkObject>());
            scrapNob.FindProperty("_preventDespawnOnDisconnect").boolValue = true;
            scrapNob.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(scrap);

            AssetDatabase.SaveAssets();
            return (player.GetComponent<FishNet.Object.NetworkObject>(), session.GetComponent<Net.NetSession>(), enemy.GetComponent<Net.NetEnemy>(),
                scrap.GetComponent<Net.NetScrap>());
        }

        /// <summary>
        /// プレハブがあればそのまま使い、なければ作る。上書きすると FishNet がプレハブに付けた番号（AssetPathHash）が
        /// 消えてしまい、ネットワークで出せなくなるため。作り直したいときはプレハブを消してからメニューを実行する。
        /// </summary>
        static GameObject LoadOrCreatePrefab(string path, System.Action<GameObject> build)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            build(go);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
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
