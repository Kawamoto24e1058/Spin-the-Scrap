using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using SpinTheScrap.Player;
using SpinTheScrap.Session;
using SpinTheScrap.UI;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>
    /// フェーズ 0（手触り検証）用の 1 部屋を、空のシーンからコードだけで組み立てる。
    /// 一人称移動・拾う・運ぶ・投げる・落とす・脱出 →（拠点メニューで）売る/リール/スロット までを 1 人で試せる。
    /// アートは仮素材（箱・カプセル）。シーンに置くのはこのコンポーネント 1 つだけでよい。
    /// </summary>
    public sealed class Phase0Bootstrap : MonoBehaviour
    {
        [SerializeField] Vector3 playerSpawn = new Vector3(0f, 1.1f, -8f);
        [SerializeField] int scrapPerDive = 16;
        [Tooltip("フェーズ 1 で 2 人目が入るまでは 0 固定")]
        [SerializeField] int localOwnerId;

        static readonly Color WallColor = new Color(0.22f, 0.25f, 0.21f);
        static readonly Color FloorColor = new Color(0.18f, 0.17f, 0.15f);
        static readonly Color PropColor = new Color(0.3f, 0.26f, 0.22f);

        RunSession session;
        FirstPersonController player;
        Interactor interactor;
        ExtractionZone zone;
        Flashlight flashlight;
        BasePanel basePanel;
        Transform scrapRoot;
        Rng spawnRng;
        bool panelWasVisible;

        readonly List<Vector3> floorSpots = new List<Vector3>();
        readonly List<Vector3> raisedSpots = new List<Vector3>();

        void Awake()
        {
            DisableOtherCameras();
            BuildLighting();
            BuildRoom();
            BuildExtractionZone();
            BuildPlayer();

            session = GetComponent<RunSession>();
            if (session == null) session = gameObject.AddComponent<RunSession>();
            session.DiveStarted += OnDiveStarted;
            spawnRng = new Rng((ulong)System.Environment.TickCount);

            var hud = gameObject.AddComponent<Phase0Hud>();
            hud.Init(session, player, interactor, zone);
            basePanel = gameObject.AddComponent<BasePanel>();
            basePanel.Init(session);

            OnDiveStarted(0);
            SetCursorLocked(true);
        }

        void OnDestroy()
        {
            if (session != null) session.DiveStarted -= OnDiveStarted;
        }

        void Update()
        {
            if (GameInput.BaseMenuPressed) basePanel.Visible = !basePanel.Visible;
            if (GameInput.EscapePressed)
            {
                if (basePanel.Visible) basePanel.Visible = false;
                else SetCursorLocked(false);
            }
            else if (!basePanel.Visible && Cursor.lockState != CursorLockMode.Locked && GameInput.AnyMouseButtonPressed)
            {
                SetCursorLocked(true);
                return; // ロックし直したクリックで物を拾わない
            }

            // メニューを開いたらカーソルを出し、閉じたら（Tab・Esc・「次の潜行へ」のどれでも）戻す
            if (basePanel.Visible != panelWasVisible) SetCursorLocked(!basePanel.Visible);
            panelWasVisible = basePanel.Visible;

            bool playing = !basePanel.Visible && Cursor.lockState == CursorLockMode.Locked;
            player.InputEnabled = playing;
            if (!playing) return;

            if (GameInput.FlashlightPressed) flashlight.Toggle();
            if (GameInput.ExtractPressed)
            {
                bool playerInZone = zone.Contains(player.transform.position);
                int count = session.Extract(zone, interactor.Inventory, playerInZone, localOwnerId);
                if (count > 0) basePanel.Visible = true;
            }
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ---- 潜行ごとのリセット ----

        void OnDiveStarted(int enemyBonus)
        {
            if (scrapRoot != null) Destroy(scrapRoot.gameObject);
            scrapRoot = new GameObject("Scrap").transform;
            SpawnScrap();

            player.Controller.enabled = false; // CharacterController は無効にしないと瞬間移動できない
            player.transform.SetPositionAndRotation(playerSpawn, Quaternion.identity);
            player.Controller.enabled = true;
            // 敵はフェーズ 0 の対象外。enemyBonus は HUD の表示のみ（BasePanel）
        }

        void SpawnScrap()
        {
            var content = session.Content;
            var bySize = content.Symbols.ToLookup(s => s.Size == ScrapSize.Huge);
            var floor = new List<Vector3>(floorSpots);
            var raised = new List<Vector3>(raisedSpots);
            spawnRng.Shuffle(floor);
            spawnRng.Shuffle(raised);

            // 特大は必ず 1 個（協力運搬の検証用）。置けるのは床だけ
            var huge = bySize[true].ToList();
            if (huge.Count > 0 && floor.Count > 0)
            {
                Place(huge[spawnRng.Next(huge.Count)], floor[0]);
                floor.RemoveAt(0);
            }

            var others = bySize[false].ToList();
            if (others.Count == 0) return;
            var spots = raised.Concat(floor).ToList();
            int count = Mathf.Min(scrapPerDive - 1, spots.Count);
            for (int i = 0; i < count; i++) Place(PickWeighted(others), spots[i]);
        }

        void Place(SymbolDefinition def, Vector3 spot)
        {
            var rot = Quaternion.Euler(0f, spawnRng.Next(360), 0f);
            ScrapFactory.Create(def, spot + Vector3.up * 0.4f, rot, scrapRoot);
        }

        SymbolDefinition PickWeighted(List<SymbolDefinition> candidates)
        {
            double total = candidates.Sum(Weight);
            double roll = spawnRng.NextDouble() * total;
            foreach (var c in candidates)
            {
                roll -= Weight(c);
                if (roll < 0) return c;
            }
            return candidates[candidates.Count - 1];
        }

        static double Weight(SymbolDefinition def)
        {
            switch (def.Rarity)
            {
                case Rarity.Common: return 6;
                case Rarity.Uncommon: return 3;
                default: return 1;
            }
        }

        // ---- 部屋の組み立て ----

        static void DisableOtherCameras()
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) cam.gameObject.SetActive(false);
        }

        static void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.24f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.04f, 0.05f, 0.04f);
            RenderSettings.fogStartDistance = 6f;
            RenderSettings.fogEndDistance = 26f;

            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None)) light.gameObject.SetActive(false);
        }

        void BuildRoom()
        {
            var root = new GameObject("Room").transform;

            Block(root, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(24f, 0.2f, 24f), FloorColor);
            Block(root, "Ceiling", new Vector3(0f, 4.1f, 0f), new Vector3(24f, 0.2f, 24f), WallColor);
            Block(root, "Wall N", new Vector3(0f, 2f, 12f), new Vector3(24f, 4f, 0.3f), WallColor);
            Block(root, "Wall S", new Vector3(0f, 2f, -12f), new Vector3(24f, 4f, 0.3f), WallColor);
            Block(root, "Wall E", new Vector3(12f, 2f, 0f), new Vector3(0.3f, 4f, 24f), WallColor);
            Block(root, "Wall W", new Vector3(-12f, 2f, 0f), new Vector3(0.3f, 4f, 24f), WallColor);

            // 中 2 階（x 3〜11, z 2〜10, 高さ 2m）。階段と坂で上がれる。重い物を持って階段を上る検証用
            Block(root, "Mezzanine", new Vector3(7f, 1f, 6f), new Vector3(8f, 2f, 8f), PropColor);
            for (int k = 0; k < 8; k++)
            {
                float h = (k + 1) * 0.25f;
                Block(root, "Step " + k, new Vector3(5f, h * 0.5f, -1.75f + k * 0.5f), new Vector3(2f, h, 0.5f), PropColor);
            }
            float rampAngle = Mathf.Atan2(2f, 4f) * Mathf.Rad2Deg;
            var ramp = Block(root, "Ramp", new Vector3(9.5f, 0.95f, 0f), new Vector3(2.5f, 0.2f, Mathf.Sqrt(20f)), PropColor);
            ramp.rotation = Quaternion.Euler(-rampAngle, 0f, 0f);

            // 投げて越える低い壁と、物を置くカウンター
            Block(root, "Low wall", new Vector3(-1f, 0.6f, 1f), new Vector3(6f, 1.2f, 0.3f), WallColor);
            Block(root, "Counter A", new Vector3(-7f, 0.5f, 5f), new Vector3(4f, 1f, 1f), PropColor);
            Block(root, "Counter B", new Vector3(-9f, 0.5f, -2f), new Vector3(1f, 1f, 4f), PropColor);
            Block(root, "Pillar 1", new Vector3(-3f, 2f, 7f), new Vector3(0.6f, 4f, 0.6f), WallColor);
            Block(root, "Pillar 2", new Vector3(2f, 2f, -4f), new Vector3(0.6f, 4f, 0.6f), WallColor);

            // 非常灯（緑）と、拠点側の明るい灯り
            PointLight(root, new Vector3(-6f, 3.6f, 6f), new Color(0.5f, 1f, 0.6f), 9f, 1.2f);
            PointLight(root, new Vector3(7f, 3.8f, 6f), new Color(0.5f, 1f, 0.6f), 8f, 1f);
            PointLight(root, new Vector3(0f, 3.6f, -9.5f), new Color(1f, 0.9f, 0.7f), 8f, 1.6f);

            floorSpots.AddRange(new[]
            {
                new Vector3(-4f, 0f, 4f), new Vector3(-1f, 0f, 6f), new Vector3(2f, 0f, 9f), new Vector3(-9f, 0f, 9f),
                new Vector3(-6f, 0f, -6f), new Vector3(-3f, 0f, -2f), new Vector3(1f, 0f, 3.5f), new Vector3(9f, 0f, -6f),
                new Vector3(5f, 0f, -7f), new Vector3(-9f, 0f, 2f), new Vector3(10f, 0f, -9f), new Vector3(-5f, 0f, 10f),
            });
            raisedSpots.AddRange(new[]
            {
                new Vector3(-8f, 1f, 5f), new Vector3(-6.5f, 1f, 5f), new Vector3(-9f, 1f, -1f), new Vector3(-9f, 1f, -3f),
                new Vector3(5f, 2f, 5f), new Vector3(7f, 2f, 8f), new Vector3(9.5f, 2f, 4f), new Vector3(8f, 2f, 9f),
            });
        }

        void BuildExtractionZone()
        {
            var pad = Block(null, "Extraction pad", new Vector3(0f, 0.01f, -10f), new Vector3(6f, 0.02f, 3f), new Color(0.2f, 0.6f, 0.3f));
            Destroy(pad.GetComponent<Collider>());

            var go = new GameObject("Extraction zone");
            go.transform.position = new Vector3(0f, 1.5f, -10f);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(6f, 3f, 3f);
            zone = go.AddComponent<ExtractionZone>();
        }

        void BuildPlayer()
        {
            var go = new GameObject("Player");
            go.transform.position = playerSpawn;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0f, 0f);

            var camGo = new GameObject("Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 75f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            camGo.AddComponent<AudioListener>();
            flashlight = camGo.AddComponent<Flashlight>();

            player = go.AddComponent<FirstPersonController>();
            player.SetCamera(cam);
            go.AddComponent<Inventory>();
            interactor = go.AddComponent<Interactor>();
        }

        static Transform Block(Transform parent, string name, Vector3 position, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(color);
            return go.transform;
        }

        static void PointLight(Transform parent, Vector3 position, Color color, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
        }
    }
}
