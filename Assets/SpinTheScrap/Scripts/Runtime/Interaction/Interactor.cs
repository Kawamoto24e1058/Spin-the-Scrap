using SpinTheScrap.Core;
using SpinTheScrap.Player;
using UnityEngine;

namespace SpinTheScrap.Interaction
{
    /// <summary>
    /// 拾う・運ぶ・投げる・落とす（要件書 4.1、フェーズ 0）。
    /// 手に持った物は「つかんだ点を目標位置へ引っ張るバネ」で動かす。力に上限があるため、
    /// 特大は 1 人だと片端を持ち上げて引きずるのが限界になり、2 人分の力が加わると持ち上がる。
    /// この方式は物理をホストだけで計算する同期方針（要件書 8.2）とも相性がよい。
    /// </summary>
    [RequireComponent(typeof(FirstPersonController), typeof(Inventory))]
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField] float reach = 2.6f;
        [SerializeField] float holdDistance = 1.3f;
        [Tooltip("つかんだ点が目標からこれ以上離れたら手を離す (m)")]
        [SerializeField] float breakDistance = 2.4f;

        [Header("Carry spring")]
        [SerializeField] float stiffness = 900f;
        [SerializeField] float damping = 120f;
        [Tooltip("大を抱えるときの最大の力 (N)。8kg の重さ 78N を十分に上回る")]
        [SerializeField] float largeMaxForce = 600f;
        [Tooltip("特大を 1 人で持ち上げる力の上限 (N)。40kg の重さ 392N を下回るので 1 人では持ち上がらず、2 人なら持ち上がる")]
        [SerializeField] float hugeMaxForce = 260f;
        [Tooltip("特大を水平に引っ張る力の上限 (N)。床との摩擦（約 235N）を少し上回り、1 人でもゆっくり引きずれる")]
        [SerializeField] float hugeDragForce = 300f;
        [Tooltip("特大を持っている間、つかんだ点からこれ以上は歩いて離れられない (m)。breakDistance より短くして手が離れないようにする")]
        [SerializeField] float hugeTetherLength = 1.9f;
        [Tooltip("特大は、つかんだ点と目の距離がこれを超えたら手を離す (m)。振り向いただけでは離れない")]
        [SerializeField] float hugeBreakDistance = 3.2f;
        [Tooltip("特大を 1 人で引きずっている間の移動速度の倍率")]
        [SerializeField, Range(0, 1)] float hugeDragSpeedFactor = 0.35f;
        [SerializeField] float heldAngularDamping = 6f;

        [Header("Throw")]
        [Tooltip("右クリックを軽く押しただけのときの、1kg の物の初速 (m/s)")]
        [SerializeField] float minThrowSpeed = 5f;
        [Tooltip("最後までためたときの、1kg の物の初速 (m/s)")]
        [SerializeField] float maxThrowSpeed = 18f;
        [Tooltip("重さで初速が落ちる度合い。初速 ÷ 質量^この値。0.5 なら √質量（8kg で約 1/2.8）、0.35 なら 8kg で約 1/2")]
        [SerializeField, Range(0, 1)] float throwMassExponent = 0.35f;
        [Tooltip("押し始めてから最大までたまる時間 (s)")]
        [SerializeField] float throwChargeTime = 0.8f;
        [Tooltip("1 人で抱えたときの重さとして数える、特大の質量の割合")]
        [SerializeField, Range(0, 1)] float hugeLoadShare = 0.5f;

        FirstPersonController controller;
        Inventory inventory;
        Scrap held;
        Vector3 heldLocalAnchor;
        float heldOriginalAngularDamping;
        Collider[] heldColliders;
        Vector3 hoverPoint;
        float chargeStartTime = -1f;

        public Scrap Hovered { get; private set; }
        public Scrap Held => held;
        public Inventory Inventory => inventory;
        public bool InventoryFullFeedback { get; private set; }
        /// <summary>右クリックを押したまま投げる力をためている。</summary>
        public bool IsChargingThrow => chargeStartTime >= 0f;
        /// <summary>ためた力（0〜1）。</summary>
        public float ThrowCharge => IsChargingThrow ? Mathf.Clamp01((Time.time - chargeStartTime) / throwChargeTime) : 0f;

        Camera Cam => controller.ViewCamera;

        void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            inventory = GetComponent<Inventory>();
        }

        void Update()
        {
            // 持っている物が破棄・回収されたら手を空ける（Unity の == null は破棄済みも null 扱い）
            if (!ReferenceEquals(held, null) && (held == null || !held.gameObject.activeInHierarchy)) Release();

            if (controller.InputEnabled)
            {
                UpdateHover();
                HandleInput();
            }
            else
            {
                Hovered = null;
                chargeStartTime = -1f; // メニューを開いたらためを取り消す
            }

            controller.LoadKg = CurrentLoadKg();
            controller.RunBlocked = held != null && held.Size != ScrapSize.Small;
            bool draggingHuge = held != null && held.Size == ScrapSize.Huge;
            if (draggingHuge) controller.SetTether(held.transform.TransformPoint(heldLocalAnchor), hugeTetherLength);
            else controller.ClearTether();
            controller.SpeedMultiplier = draggingHuge ? hugeDragSpeedFactor : 1f;
        }

        void FixedUpdate()
        {
            if (held == null) return;
            DriveHeld();
        }

        void UpdateHover()
        {
            Hovered = null;
            if (Cam == null) return;
            var ray = new Ray(Cam.transform.position, Cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, reach, ~0, QueryTriggerInteraction.Ignore)) return;
            if (hit.rigidbody == null) return;
            var scrap = hit.rigidbody.GetComponent<Scrap>();
            if (scrap == null || scrap == held) return;
            Hovered = scrap;
            hoverPoint = hit.point;
        }

        void HandleInput()
        {
            int slot = GameInput.SlotSelectPressed();
            if (slot >= 0) inventory.Select(slot);

            if (GameInput.InteractPressed)
            {
                InventoryFullFeedback = false;
                if (held != null) Release();
                else if (Hovered != null) PickUp(Hovered, hoverPoint);
            }

            HandleThrowInput();

            if (GameInput.DropPressed)
            {
                if (held != null) Release();
                else if (inventory.Selected != null) PlaceInFront(inventory.TakeSelected());
            }
        }

        /// <summary>右クリックを押すとため始め、離すと投げる。特大は投げられないので押した時点で手を離す。</summary>
        void HandleThrowInput()
        {
            if (GameInput.ThrowPressed)
            {
                if (held != null && held.Size == ScrapSize.Huge) Release();
                else if (held != null || inventory.Selected != null) chargeStartTime = Time.time;
            }

            if (!IsChargingThrow) return;
            if (held == null && inventory.Selected == null)
            {
                chargeStartTime = -1f; // ためている間に手から離れた
                return;
            }
            if (GameInput.ThrowHeld) return;

            float charge = ThrowCharge;
            chargeStartTime = -1f;
            if (held != null) Throw(charge);
            else ThrowFromInventory(charge);
        }

        void PickUp(Scrap scrap, Vector3 point)
        {
            if (scrap.Size == ScrapSize.Small)
            {
                if (inventory.TryAdd(scrap)) return;
                InventoryFullFeedback = true; // 枠が埋まっていても手では持てる
            }
            Grab(scrap, point);
        }

        void Grab(Scrap scrap, Vector3 point)
        {
            held = scrap;
            held.IsHeld = true;
            var body = held.Body;
            // 特大はつかんだ点（端）を、それ以外は重心を持つ
            heldLocalAnchor = held.Size == ScrapSize.Huge
                ? held.transform.InverseTransformPoint(point)
                : held.transform.InverseTransformPoint(body.worldCenterOfMass);
            heldOriginalAngularDamping = body.angularDamping;
            if (held.Size != ScrapSize.Huge) body.angularDamping = heldAngularDamping;
            heldColliders = held.GetComponentsInChildren<Collider>();
            SetIgnorePlayerCollision(true);
        }

        /// <summary>手に持っている物をその場で離す（倒れたときなど）。</summary>
        public void ReleaseHeld()
        {
            chargeStartTime = -1f;
            Release();
        }

        void Release()
        {
            if (held != null)
            {
                held.IsHeld = false;
                held.Body.angularDamping = heldOriginalAngularDamping;
                SetIgnorePlayerCollision(false);
            }
            held = null;
            heldColliders = null;
        }

        void Throw(float charge)
        {
            var scrap = held;
            Release();
            Launch(scrap.Body, charge);
        }

        void ThrowFromInventory(float charge)
        {
            var scrap = inventory.TakeSelected();
            PlaceInFront(scrap);
            Launch(scrap.Body, charge);
        }

        void Launch(Rigidbody body, float charge)
        {
            float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, charge) / Mathf.Pow(Mathf.Max(1f, body.mass), throwMassExponent);
            var dir = (Cam.transform.forward + Vector3.up * 0.1f).normalized;
            body.linearVelocity = dir * speed;
        }

        /// <summary>インベントリから出した物を目の前に置く。壁にめり込まないよう手前に縮める。</summary>
        void PlaceInFront(Scrap scrap)
        {
            var origin = Cam.transform.position;
            var forward = Cam.transform.forward;
            float distance = 0.7f;
            if (Physics.Raycast(origin, forward, out var hit, distance + 0.2f, ~0, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0.2f, hit.distance - 0.25f);

            scrap.transform.position = origin + forward * distance;
            scrap.gameObject.SetActive(true);
            scrap.Body.linearVelocity = Vector3.zero;
            scrap.Body.angularVelocity = Vector3.zero;
        }

        void DriveHeld()
        {
            var body = held.Body;
            var anchor = held.transform.TransformPoint(heldLocalAnchor);
            var target = Cam.transform.position + Cam.transform.forward * holdDistance;
            var offset = target - anchor;
            bool tooFar = held.Size == ScrapSize.Huge
                ? Vector3.Distance(Cam.transform.position, anchor) > hugeBreakDistance
                : offset.magnitude > breakDistance;
            if (tooFar)
            {
                Release();
                return;
            }

            // 小を手で持つ場合も大と同じ扱い（軽いのでほぼ追従する）
            var force = offset * (stiffness * body.mass / 8f) - body.GetPointVelocity(anchor) * (damping * body.mass / 8f);
            if (held.Size == ScrapSize.Huge)
            {
                // 持ち上げる力と引っ張る力の上限を分ける。1 人では持ち上がらないが、引きずることはできる
                var horizontal = Vector3.ClampMagnitude(new Vector3(force.x, 0f, force.z), hugeDragForce);
                force = horizontal + Vector3.up * Mathf.Clamp(force.y, -hugeMaxForce, hugeMaxForce);
            }
            else
            {
                force = Vector3.ClampMagnitude(force, largeMaxForce);
            }
            body.AddForceAtPosition(force, anchor, ForceMode.Force);
        }

        float CurrentLoadKg()
        {
            float load = inventory.TotalMass;
            if (held != null) load += held.Size == ScrapSize.Huge ? held.Body.mass * hugeLoadShare : held.Body.mass;
            return load;
        }

        void SetIgnorePlayerCollision(bool ignore)
        {
            if (heldColliders == null) return;
            foreach (var c in heldColliders)
                if (c != null) Physics.IgnoreCollision(c, controller.Controller, ignore);
        }

        void OnDisable() => Release();
    }
}
