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
    /// フェーズ 0（手触り検証）用の施設を、空のシーンからコードだけで組み立てる。
    /// 間取りは潜行ごとに FacilityLayout（Core）で作り直す。入口の部屋に脱出地点がある。
    /// 一人称移動・拾う・運ぶ・投げる・落とす・脱出 →（拠点メニューで）売る/リール/スロット までを 1 人で試せる。
    /// アートは仮素材（箱・カプセル）。シーンに置くのはこのコンポーネント 1 つだけでよい。
    /// </summary>
    public sealed class Phase0Bootstrap : MonoBehaviour
    {
        [SerializeField] Vector3 playerSpawn = new Vector3(0f, 1.1f, -8f);
        [Tooltip("フェーズ 1 で 2 人目が入るまでは 0 固定")]
        [SerializeField] int localOwnerId;

        RunSession session;
        FirstPersonController player;
        Interactor interactor;
        PlayerHealth health;
        ExtractionZone zone;
        Flashlight flashlight;
        BasePanel basePanel;
        Transform scrapRoot;
        BuiltFacility facility;
        Rng spawnRng;
        bool panelWasVisible;

        void Awake()
        {
            DisableOtherCameras();
            BuildLighting();
            BuildExtractionZone();
            BuildPlayer();

            session = GetComponent<RunSession>();
            if (session == null) session = gameObject.AddComponent<RunSession>();
            session.FloorStarted += OnFloorStarted;
            spawnRng = new Rng((ulong)System.Environment.TickCount);
            health.Init(session.Hazards);
            health.Downed += OnPlayerDowned;
            NoiseEvents.Rules = session.Enemies;
            player.GetComponent<PlayerNoise>().Init(session.Enemies);

            var hud = gameObject.AddComponent<Phase0Hud>();
            hud.Init(session, player, interactor, zone, health);
            basePanel = gameObject.AddComponent<BasePanel>();
            basePanel.Init(session);

            OnFloorStarted(0);
            SetCursorLocked(true);
        }

        void OnDestroy()
        {
            if (session != null) session.FloorStarted -= OnFloorStarted;
            if (health != null) health.Downed -= OnPlayerDowned;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        /// <summary>倒れたら、持っている物をすべて失って拠点に戻る。運び出し済みの物は残る。</summary>
        void OnPlayerDowned()
        {
            interactor.ReleaseHeld();
            var lost = interactor.Inventory.TakeAll();
            foreach (var scrap in lost) Destroy(scrap.gameObject);
            session.ReportDowned(lost.Count);
            basePanel.OnDownedScreen = true;
            basePanel.Visible = true;
        }

        void Update()
        {
            // 倒れている間は拠点メニューを閉じられない（「次の潜行へ」で起き上がる）
            if (health.IsDown) basePanel.Visible = true;
            else if (GameInput.BaseMenuPressed) basePanel.Visible = !basePanel.Visible;
            if (GameInput.EscapePressed)
            {
                if (basePanel.Visible && !health.IsDown) basePanel.Visible = false;
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
            // ソロでは拠点メニューを開いている間は時間を止める（敵も止まる）。フェーズ 1 の協力プレイでは止めない
            Time.timeScale = basePanel.Visible ? 0f : 1f;
            AudioListener.pause = basePanel.Visible; // 世界の音（敵の気配・ジー・ガタガタ）も止める。UI の音は鳴る

            bool playing = !basePanel.Visible && Cursor.lockState == CursorLockMode.Locked;
            player.InputEnabled = playing;
            if (!playing) return;

            UpdateTools();
            if (GameInput.ExtractPressed)
            {
                bool playerInZone = zone.Contains(player.transform.position);
                int count = session.Extract(zone, interactor.Inventory, playerInZone, localOwnerId);
                if (count > 0)
                {
                    basePanel.Visible = true;
                    Audio.Sfx.Play2D(Audio.SfxClips.Extract, 0.6f);
                }
            }
        }

        /// <summary>懐中電灯の電池、包帯（H）、爆竹（Q）。数と使えるかどうかは RunState（Core）が決める。</summary>
        void UpdateTools()
        {
            var run = session.Run;
            if (GameInput.FlashlightPressed)
            {
                // 電池が切れていて予備もなければ点かない
                if (flashlight.IsOn) flashlight.SetOn(false);
                else if (run.FlashlightCharge > 0 || run.HasTool(ToolIds.Battery)) flashlight.SetOn(true);
            }
            if (flashlight.IsOn && !run.DrainFlashlight(Time.deltaTime)) flashlight.SetOn(false);

            if (GameInput.BandagePressed && health.State.Current < health.State.Max && run.TryUseTool(ToolIds.Bandage))
            {
                health.Heal(session.Hazards.BandageHeal);
                Audio.Sfx.Play2D(Audio.SfxClips.Knock, 0.4f, 0.7f);
            }

            if (GameInput.FirecrackerPressed && run.TryUseTool(ToolIds.Firecracker))
            {
                var cam = player.ViewCamera.transform;
                Firecracker.Throw(facility.Root, cam.position + cam.forward * 0.5f, cam.forward, session.Enemies);
            }
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ---- 階層ごとのリセット ----

        void OnFloorStarted(int enemyBonus)
        {
            basePanel.OnDownedScreen = false;
            if (scrapRoot != null) Destroy(scrapRoot.gameObject);
            if (facility != null) Destroy(facility.Root.gameObject);

            var run = session.Run;
            var layout = FacilityLayout.Generate(spawnRng.NextULong(), run.Config.RoomCountFor(run.Floor));
            facility = FacilityBuilder.Build(layout);
            scrapRoot = new GameObject("Scrap").transform;
            SpawnScrap();
            SpawnHazards();
            SpawnEnemies(enemyBonus);
            // 台車は道具屋で買っていれば入口に 1 台
            if (session.Run.HasTool(ToolIds.Cart))
                Cart.Spawn(facility.Root, new Vector3(6f, 0.05f, -9f), Quaternion.identity, session.Tools);
            health.ResetHealth();

            player.Controller.enabled = false; // CharacterController は無効にしないと瞬間移動できない
            player.transform.SetPositionAndRotation(playerSpawn, Quaternion.identity);
            player.Controller.enabled = true;
        }

        /// <summary>部屋ごとに、深さに応じた数と珍しさでガラクタを置く。特大は入口以外のどこか 1 部屋に必ず 1 個。</summary>
        void SpawnScrap()
        {
            var content = session.Content;
            var huge = content.Symbols.Where(s => s.Size == ScrapSize.Huge).ToList();
            var others = content.Symbols.Where(s => s.Size != ScrapSize.Huge).ToList();

            var hugeRooms = facility.Rooms.Where(r => r.HugeSpot.HasValue).ToList();
            if (huge.Count > 0 && hugeRooms.Count > 0)
            {
                var room = hugeRooms[spawnRng.Next(hugeRooms.Count)];
                Place(huge[spawnRng.Next(huge.Count)], room.HugeSpot.Value);
            }

            if (others.Count == 0) return;
            foreach (var room in facility.Rooms)
            {
                var spots = new List<ScrapSpot>(room.Spots);
                spawnRng.Shuffle(spots);
                int count = Mathf.Min(LootTable.ScrapCountFor(room.Plan.Kind, room.Plan.Depth), spots.Count);
                for (int i = 0; i < count; i++) Place(PickWeighted(others, spots[i].Depth), spots[i].Position);
            }
        }

        /// <summary>入口以外の部屋に、深さに応じた確率で電気が流れる水たまりを置く。</summary>
        void SpawnHazards()
        {
            var rules = session.Hazards;
            var content = session.Content;
            foreach (var room in facility.Rooms)
            {
                if (!room.PuddleSpot.HasValue) continue;
                if (spawnRng.NextDouble() >= rules.PuddleChance(room.Plan.Kind, room.Plan.Depth)) continue;
                var puddle = FacilityBuilder.CreatePuddle(room.Root, room.PuddleSpot.Value, out var surface, out var glow);
                puddle.Init(rules, id => content.GetSymbol(id).Tags, () => session.Run.HasTool(ToolIds.InsulatedBoots), health, surface, glow);
            }
        }

        /// <summary>
        /// 敵を奥の部屋から順に 1 部屋 1 体ずつ置く。入口には置かない。
        /// 数はサイクルと、マネキンの首などで増えた分（enemyBonus）で決まる。
        /// </summary>
        void SpawnEnemies(int enemyBonus)
        {
            var rules = session.Enemies;
            var run = session.Run;
            int count = rules.CountFor(run.Floor, enemyBonus, facility.Rooms.Count);
            var rooms = facility.Rooms.Where(r => r.Plan.Kind != RoomKind.Entrance)
                .OrderByDescending(r => r.Plan.Depth).ThenBy(_ => spawnRng.Next(1000)).Take(count);
            foreach (var room in rooms)
            {
                var position = FacilityBuilder.RoomCenter(room.Plan) + new Vector3(3f, 0f, 3f);
                Enemies.Lurker.Spawn(facility.Root, position, rules, facility, player, health, flashlight, spawnRng.NextULong());
            }
        }

        void Place(SymbolDefinition def, Vector3 spot)
        {
            var rot = Quaternion.Euler(0f, spawnRng.Next(360), 0f);
            ScrapFactory.Create(def, spot + Vector3.up * 0.4f, rot, scrapRoot);
        }

        SymbolDefinition PickWeighted(List<SymbolDefinition> candidates, int depth)
        {
            double total = candidates.Sum(c => LootTable.Weight(c.Rarity, depth));
            double roll = spawnRng.NextDouble() * total;
            foreach (var c in candidates)
            {
                roll -= LootTable.Weight(c.Rarity, depth);
                if (roll < 0) return c;
            }
            return candidates[candidates.Count - 1];
        }

        // ---- 照明・脱出地点・プレイヤー ----

        static void DisableOtherCameras()
        {
            foreach (var cam in FindObjectsByType<Camera>()) cam.gameObject.SetActive(false);
        }

        static void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.36f, 0.34f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.04f, 0.05f, 0.04f);
            RenderSettings.fogStartDistance = 8f;
            RenderSettings.fogEndDistance = 32f;

            foreach (var light in FindObjectsByType<Light>()) light.gameObject.SetActive(false);
        }

        void BuildExtractionZone()
        {
            var pad = FacilityBuilder.Block(null, "Extraction pad", new Vector3(0f, 0.01f, -10f), new Vector3(6f, 0.02f, 3f), new Color(0.2f, 0.6f, 0.3f));
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
            health = go.AddComponent<PlayerHealth>();
            go.AddComponent<PlayerNoise>();
        }
    }
}
