using System.Collections.Generic;
using System.Linq;
using SpinTheScrap.Core;
using SpinTheScrap.Enemies;
using SpinTheScrap.Interaction;
using SpinTheScrap.Net;
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
        [Tooltip("協力プレイ用（Create Phase 0 Scene が設定する）")]
        [SerializeField] FishNet.Managing.NetworkManager networkManager;
        [SerializeField] Net.NetSession netSessionPrefab;

        RunSession session;
        FirstPersonController player;
        Interactor interactor;
        PlayerHealth health;
        MachineCabinet currentCabinet;
        Flashlight flashlight;
        BasePanel basePanel;
        Transform scrapRoot;
        BuiltFacility facility;
        Rng spawnRng;
        bool panelWasVisible;
        PlayerCombat combat;
        float floorStartTime;
        float nextCrawlerTime;
        readonly List<Enemy> crawlers = new List<Enemy>();

        void Awake()
        {
            DisableOtherCameras();
            BuildLighting();
            BuildPlayer();

            session = GetComponent<RunSession>();
            if (session == null) session = gameObject.AddComponent<RunSession>();
            session.FloorStarted += OnFloorStarted;
            spawnRng = new Rng((ulong)System.Environment.TickCount);
            health.Init(session.Hazards);
            health.Downed += OnPlayerDowned;
            NoiseEvents.Rules = session.Enemies;
            player.GetComponent<PlayerNoise>().Init(session.Enemies);
            combat.Init(session);
            Enemy.AnyDied += OnEnemyDied;
            Enemy.SummonRequested = SummonMinion;

            var hud = gameObject.AddComponent<Phase0Hud>();
            hud.Init(session, player, interactor, health);
            session.Spun += OnSpun;
            basePanel = gameObject.AddComponent<BasePanel>();
            basePanel.Init(session);

            NetPlayer.LocalTarget = player.transform;
            gameObject.AddComponent<NetLobby>().Init(session.Strings, Begin);
        }

        // ---- ひとり・ホスト・参加 ----

        NetMode mode = NetMode.Solo;
        bool started;
        (ulong seed, int floor, int attempt) builtFor;

        bool IsClientOnly => mode == NetMode.Client;

        /// <summary>始める前の画面で選んだ遊び方で始める。</summary>
        void Begin(NetMode chosen, string address)
        {
            mode = chosen;
            started = true;
            SetCursorLocked(true);
            if (mode != NetMode.Solo && networkManager == null)
            {
                Debug.LogWarning("[Spin the Scrap] NetworkManager がありません。Create Phase 0 Scene でシーンを作り直してください。ひとりで始めます。");
                mode = NetMode.Solo;
            }
            switch (mode)
            {
                case NetMode.Host:
                    networkManager.ServerManager.StartConnection();
                    networkManager.ClientManager.StartConnection();
                    OnFloorStarted(0);
                    break;
                case NetMode.Client:
                    networkManager.ClientManager.StartConnection(address);
                    break;
                default:
                    OnFloorStarted(0);
                    break;
            }
        }

        /// <summary>ホスト：今の階層を全員に配る（NetSession がなければ作る）。</summary>
        void PublishFloor()
        {
            if (mode != NetMode.Host || networkManager == null || !networkManager.IsServerStarted) return;
            if (NetSession.Instance == null && netSessionPrefab != null)
            {
                var nob = Instantiate(netSessionPrefab);
                networkManager.ServerManager.Spawn(nob.gameObject);
            }
            if (NetSession.Instance != null) NetSession.Instance.Publish(session.RunSeed, session.Run.Floor, session.Attempt);
        }

        /// <summary>
        /// クライアント：ホストが配った階層を組み立てる。敵・ガラクタ・スロット台の結果はホストだけが持つ
        /// （同期は次の段階。今は間取りとほかの人の分身だけが見える）。
        /// </summary>
        void UpdateClientFloor()
        {
            var net = NetSession.Instance;
            if (net == null || net.RunSeed == 0) return;
            var key = (net.RunSeed, net.Floor, net.Attempt);
            if (key == builtFor) return;
            builtFor = key;
            if (scrapRoot != null) Destroy(scrapRoot.gameObject);
            if (facility != null) Destroy(facility.Root.gameObject);
            var config = session.Run.Config;
            var layout = config.IsBossFloor(net.Floor)
                ? FacilityLayout.CreateBossFloor()
                : FacilityLayout.Generate(FacilityLayout.SeedFor(net.RunSeed, net.Floor, net.Attempt), config.RoomCountFor(net.Floor));
            facility = FacilityBuilder.Build(layout);
            scrapRoot = new GameObject("Scrap").transform;
            health.ResetHealth();
            player.Controller.enabled = false;
            player.transform.SetPositionAndRotation(playerSpawn + Vector3.right * 1.5f, Quaternion.identity);
            player.Controller.enabled = true;
        }

        void OnDestroy()
        {
            if (session != null)
            {
                session.FloorStarted -= OnFloorStarted;
                session.Spun -= OnSpun;
            }
            if (health != null) health.Downed -= OnPlayerDowned;
            Enemy.AnyDied -= OnEnemyDied;
            Enemy.SummonRequested = null;
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

        /// <summary>
        /// 敵が倒された：攻略条件を進め、倒した人の回復（食べ物の強化）と、這う者はガラクタを落とすことがある。
        /// </summary>
        void OnEnemyDied(Enemy enemy)
        {
            var run = session.Run;
            if (run.ReportKill(EnemyRules.IsBoss(enemy.Kind)))
            {
                session.NotifyObjectiveComplete();
                Audio.Sfx.Play2D(Audio.SfxClips.Extract, 0.7f);
            }
            var stats = run.StatsOf(session.LocalPlayerId);
            if (stats.HealOnKill > 0) health.Heal(stats.HealOnKill);
            crawlers.Remove(enemy);

            if (enemy.Kind == EnemyKind.Crawler && spawnRng.NextDouble() < session.Enemies.CrawlerScrapDropChance)
                DropScrap(enemy.transform.position, 0);
            else if (EnemyRules.IsBoss(enemy.Kind))
                for (int i = 0; i < 6; i++) DropScrap(enemy.transform.position + Random.insideUnitSphere * 1.5f, 3);
        }

        /// <summary>敵が落とすガラクタ。depth が深いほど珍しい物が出やすい。</summary>
        void DropScrap(Vector3 position, int depth)
        {
            var others = session.Content.Symbols.Where(s => s.Size == ScrapSize.Small).ToList();
            if (others.Count == 0) return;
            Place(PickWeighted(others, depth), new Vector3(position.x, Mathf.Max(0.1f, position.y - 0.5f), position.z));
        }

        /// <summary>強化で決まる能力値を、体力・移動・被ダメージに反映する（強化スロットを回すと変わる）。</summary>
        void ApplyStats()
        {
            var stats = session.Run.StatsOf(session.LocalPlayerId);
            if (health.State != null && !health.IsDown && System.Math.Abs(health.State.Max - stats.MaxHealth) > 0.01)
                health.State.SetMax(stats.MaxHealth);
            health.DamageTakenMultiplier = stats.DamageTakenMultiplier;
            player.PerkSpeedMultiplier = (float)stats.MoveSpeedMultiplier;
            Enemy.PlayerStealth = stats.Stealth;
        }

        /// <summary>通常の階では、倒された這う者が湧き直す。階層に長くいるほど早く湧く（長居するほど危ない）。</summary>
        void UpdateCrawlers()
        {
            if (session.Run.IsBossFloor || facility == null || IsClientOnly) return;
            crawlers.RemoveAll(c => c == null);
            int population = session.Enemies.CrawlerPopulationFor(session.Run.Floor);
            if (crawlers.Count >= population || Time.time < nextCrawlerTime) return;
            nextCrawlerTime = Time.time + (float)session.Enemies.CrawlerRespawnSeconds(Time.time - floorStartTime);
            SpawnCrawler(awayFromPlayer: true);
        }

        /// <summary>群れを呼ぶ人形が呼んだ這う者。</summary>
        Enemy SummonMinion(Enemy summoner, Vector3 position)
        {
            if (facility == null) return null;
            return Enemy.Spawn(EnemyKind.Crawler, session.Run.Floor, facility.Root, new Vector3(position.x, 0f, position.z),
                session.Enemies, facility, player, health, flashlight, spawnRng.NextULong());
        }

        void SpawnCrawler(bool awayFromPlayer)
        {
            var playerRoom = facility.RoomAt(player.transform.position);
            var rooms = facility.Rooms.Where(r => r.Plan.Kind != RoomKind.Entrance && (!awayFromPlayer || r.Plan != playerRoom)).ToList();
            if (rooms.Count == 0) return;
            var room = rooms[spawnRng.Next(rooms.Count)];
            var offset = new Vector3((float)(spawnRng.NextDouble() * 12 - 6), 0f, (float)(spawnRng.NextDouble() * 12 - 6));
            var enemy = Enemy.Spawn(EnemyKind.Crawler, session.Run.Floor, facility.Root, FacilityBuilder.RoomCenter(room.Plan) + offset,
                session.Enemies, facility, player, health, flashlight, spawnRng.NextULong());
            crawlers.Add(enemy);
        }

        void Update()
        {
            if (!started) return; // 始める前の画面
            if (mode == NetMode.Host && NetSession.Instance == null) PublishFloor(); // サーバーが立ち上がったら配る
            if (IsClientOnly) UpdateClientFloor();
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
            // ひとりのときだけ時間と世界の音を止める（協力プレイでは止めない）
            bool pause = basePanel.Visible && mode == NetMode.Solo;
            Time.timeScale = pause ? 0f : 1f;
            AudioListener.pause = pause;

            bool playing = !basePanel.Visible && Cursor.lockState == CursorLockMode.Locked;
            player.InputEnabled = playing;
            if (!playing) return;

            UpdateTools();
            ApplyStats();
            UpdateCrawlers();
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
            // ボス階層は決まった間取り、通常の階は自動生成
            var layout = run.IsBossFloor
                ? FacilityLayout.CreateBossFloor()
                : FacilityLayout.Generate(FacilityLayout.SeedFor(session.RunSeed, run.Floor, session.Attempt), run.Config.RoomCountFor(run.Floor));
            facility = FacilityBuilder.Build(layout);
            scrapRoot = new GameObject("Scrap").transform;
            SpawnScrap();
            SpawnHazards();
            SpawnEnemies(enemyBonus);
            crawlers.Clear();
            floorStartTime = Time.time;
            nextCrawlerTime = Time.time + 3f;
            if (!session.Run.IsBossFloor)
                for (int i = 0; i < session.Enemies.CrawlerPopulationFor(session.Run.Floor); i++) SpawnCrawler(awayFromPlayer: true);
            // 台車は道具スロットで当てていれば入口に 1 台
            if (session.Run.HasTool(ToolIds.Cart))
                Cart.Spawn(facility.Root, new Vector3(4f, 0.05f, -5f), Quaternion.identity, session.Tools);
            PlaceMachines();
            health.ResetHealth();

            player.Controller.enabled = false; // CharacterController は無効にしないと瞬間移動できない
            player.transform.SetPositionAndRotation(playerSpawn, Quaternion.identity);
            player.Controller.enabled = true;
            PublishFloor();
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
            var rooms = facility.Rooms.Where(r => r.Plan.Kind != RoomKind.Entrance)
                .OrderByDescending(r => r.Plan.Depth).ThenBy(_ => spawnRng.Next(1000)).ToList();
            if (rooms.Count == 0) return;

            // ボス階層：奥の部屋に目の見えない巨体だけ（ほかの敵は出さない）
            if (run.IsBossFloor)
            {
                var lair = facility.Rooms.First(r => r.Plan.Kind == RoomKind.BossLair);
                Enemy.Spawn(run.Config.BossFor(run.Floor), run.Floor, facility.Root, FacilityBuilder.RoomCenter(lair.Plan),
                    rules, facility, player, health, flashlight, spawnRng.NextULong());
                return;
            }

            int count = rules.CountFor(run.Floor, enemyBonus, facility.Rooms.Count);
            foreach (var room in rooms.Take(count))
            {
                var position = FacilityBuilder.RoomCenter(room.Plan) + new Vector3(3f, 0f, 3f);
                Enemy.Spawn(EnemyKind.Lurker, run.Floor, facility.Root, position, rules, facility, player, health, flashlight, spawnRng.NextULong());
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

        // ---- 照明・プレイヤー ----

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

        // ---- スロット台とエレベーター ----

        /// <summary>
        /// 入口の部屋に武器・強化・道具のスロット台とエレベーター。ボス階層では改造スロットも。
        /// 通常の階では、いちばん奥の部屋にもう 1 台（強化か道具）。
        /// </summary>
        void PlaceMachines()
        {
            var root = facility.Root;
            var kinds = new List<MachineKind> { MachineKind.Weapon, MachineKind.Perk, MachineKind.Tool };
            if (session.Run.IsBossFloor) kinds.Add(MachineKind.Mod);
            for (int i = 0; i < kinds.Count; i++)
                MachineCabinet.Create(root, new Vector3(-4.5f + i * 3f, 0f, -11.3f), Vector3.forward, kinds[i], UseMachine);

            Elevator.Create(root, new Vector3(9.2f, 0f, -9.2f), Vector3.left, () => session.Run.Objective.IsComplete && !session.Run.IsOver,
                strings => session.Run.IsLastFloor ? strings.Get("hud.elevator_exit") : strings.Format("hud.elevator_ready", session.Run.Floor + 1),
                RideElevator);

            if (session.Run.IsBossFloor)
            {
                // ボスの部屋の奥に「最後の台」：ボスに追われながら回しに行くかどうかの賭け
                var lair = facility.Rooms.First(r => r.Plan.Kind == RoomKind.BossLair);
                var back = FacilityBuilder.RoomCenter(lair.Plan) + new Vector3(0f, 0f, 11.3f);
                MachineCabinet.Create(root, back + Vector3.left * 2f, Vector3.back, MachineKind.Mod, UseMachine);
                MachineCabinet.Create(root, back + Vector3.right * 2f, Vector3.back, MachineKind.Perk, UseMachine);
                return;
            }
            var deepest = facility.Rooms.Where(r => r.Plan.Kind != RoomKind.Entrance).OrderByDescending(r => r.Plan.Depth).FirstOrDefault();
            if (deepest == null) return;
            var extra = spawnRng.Next(2) == 0 ? MachineKind.Perk : MachineKind.Tool;
            MachineCabinet.Create(root, FacilityBuilder.RoomCenter(deepest.Plan) + new Vector3(8f, 0f, -10.3f), Vector3.forward, extra, UseMachine);
        }

        /// <summary>スロット台を使う：持っている物と台の前に置いた物をすべて入れて、その台のメニューを開く。</summary>
        void UseMachine(MachineCabinet cabinet, Interactor who)
        {
            var scraps = cabinet.Hopper.ScrapsInside();
            scraps.AddRange(who.Inventory.TakeAll());
            var held = who.TakeHeld();
            if (held != null && !scraps.Contains(held)) scraps.Add(held);
            int fed = session.Feed(scraps, localOwnerId);
            if (fed > 0) Audio.Sfx.Play2D(Audio.SfxClips.Extract, 0.6f);
            currentCabinet = cabinet;
            basePanel.Machine = cabinet.Kind;
            basePanel.Visible = true;
        }

        /// <summary>回すと台から大きな音がして、敵が寄ってくる（大当たりはもっと大きい）。メニューを閉じると敵が動き出す。</summary>
        void OnSpun(MachineOutcome outcome)
        {
            var position = currentCabinet != null ? currentCabinet.transform.position : player.transform.position;
            var rules = session.Run.MachineRules;
            NoiseEvents.Emit(position, (float)(outcome.Reading.Jackpot ? rules.JackpotNoiseRadius : rules.SpinNoiseRadius));
        }

        void RideElevator()
        {
            if (!session.TryAdvanceFloor()) return;
            Audio.Sfx.Play2D(Audio.SfxClips.Extract, 0.7f, 0.8f);
            if (session.Run.IsOver) basePanel.Visible = true;
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
            combat = go.AddComponent<PlayerCombat>();
            go.AddComponent<WeaponViewModel>();
        }
    }
}
