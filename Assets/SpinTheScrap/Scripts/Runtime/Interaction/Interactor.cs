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
        [Tooltip("特大を 1 人でつかむときの最大の力 (N)。40kg の重さ 392N を下回るので 1 人では持ち上がらない")]
        [SerializeField] float hugeMaxForce = 260f;
        [SerializeField] float heldAngularDamping = 6f;

        [Header("Throw")]
        [Tooltip("1kg の物を投げたときの初速 (m/s)。重いほど √質量 に反比例して遅くなる")]
        [SerializeField] float throwSpeed = 9f;
        [Tooltip("1 人で抱えたときの重さとして数える、特大の質量の割合")]
        [SerializeField, Range(0, 1)] float hugeLoadShare = 0.5f;

        FirstPersonController controller;
        Inventory inventory;
        Scrap held;
        Vector3 heldLocalAnchor;
        float heldOriginalAngularDamping;
        Collider[] heldColliders;
        Vector3 hoverPoint;

        public Scrap Hovered { get; private set; }
        public Scrap Held => held;
        public Inventory Inventory => inventory;
        public bool InventoryFullFeedback { get; private set; }

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
            }

            controller.LoadKg = CurrentLoadKg();
            controller.RunBlocked = held != null && held.Size != ScrapSize.Small;
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

            if (GameInput.ThrowPressed)
            {
                if (held != null) Throw();
                else if (inventory.Selected != null) ThrowFromInventory();
            }

            if (GameInput.DropPressed)
            {
                if (held != null) Release();
                else if (inventory.Selected != null) PlaceInFront(inventory.TakeSelected());
            }
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

        void Throw()
        {
            var scrap = held;
            Release();
            if (scrap.Size == ScrapSize.Huge) return; // 特大は投げられない。手を離すだけ
            Launch(scrap.Body);
        }

        void ThrowFromInventory()
        {
            var scrap = inventory.TakeSelected();
            PlaceInFront(scrap);
            Launch(scrap.Body);
        }

        void Launch(Rigidbody body)
        {
            float speed = throwSpeed / Mathf.Sqrt(Mathf.Max(1f, body.mass));
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
            if (offset.magnitude > breakDistance)
            {
                Release();
                return;
            }

            float maxForce = held.Size == ScrapSize.Huge ? hugeMaxForce : largeMaxForce;
            // 小を手で持つ場合も大と同じ扱い（軽いのでほぼ追従する）
            var force = offset * (stiffness * body.mass / 8f) - body.GetPointVelocity(anchor) * (damping * body.mass / 8f);
            force = Vector3.ClampMagnitude(force, maxForce);
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
