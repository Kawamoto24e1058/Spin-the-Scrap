using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>
    /// 一人称移動。運搬中の重さ（LoadKg）で移動速度・ジャンプ・段差の上り下りが落ちる（要件書 4.1）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;

        [Header("Move")]
        [SerializeField] float walkSpeed = 4f;
        [SerializeField] float runSpeed = 6.5f;
        [SerializeField] float jumpHeight = 1.1f;
        [SerializeField] float gravity = -20f;
        [SerializeField] float baseStepOffset = 0.35f;

        [Header("Load")]
        [Tooltip("この重さで速度・ジャンプが最低値になる")]
        [SerializeField] float maxLoadKg = 45f;
        [SerializeField, Range(0, 1)] float minSpeedFactor = 0.3f;
        [SerializeField, Range(0, 1)] float minJumpFactor = 0.2f;
        [SerializeField] float loadedStepOffset = 0.12f;

        [Header("Look")]
        [SerializeField] float lookSensitivity = 2f;
        [SerializeField] float maxPitch = 85f;

        CharacterController controller;
        float pitch;
        bool tethered;
        Vector3 tetherPoint;
        float tetherLength;
        float verticalVelocity;

        public Camera ViewCamera => viewCamera;
        public CharacterController Controller => controller;
        /// <summary>移動・視点の入力を受け付けるか（拠点メニュー表示中などは false）。</summary>
        public bool InputEnabled { get; set; } = true;
        /// <summary>現在の運搬重量（Interactor が毎フレーム設定する）。</summary>
        public float LoadKg { get; set; }
        /// <summary>大・特大を抱えているときは走れない。</summary>
        public bool RunBlocked { get; set; }
        /// <summary>重さとは別に掛ける移動速度の倍率（特大を引きずるときなど）。</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        public float LoadFactor => Mathf.Clamp01(LoadKg / maxLoadKg);

        public void SetCamera(Camera cam) => viewCamera = cam;

        /// <summary>水平方向でこの点から length より遠くへは歩けなくする（特大を引きずるとき）。</summary>
        public void SetTether(Vector3 point, float length)
        {
            tethered = true;
            tetherPoint = point;
            tetherLength = length;
        }

        public void ClearTether() => tethered = false;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
        }

        void Update()
        {
            if (InputEnabled) Look();
            Move();
        }

        void Look()
        {
            var look = GameInput.Look * lookSensitivity;
            transform.Rotate(0f, look.x, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - look.y, -maxPitch, maxPitch);
            if (viewCamera != null) viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move()
        {
            float load = LoadFactor;
            controller.stepOffset = Mathf.Lerp(baseStepOffset, loadedStepOffset, load);

            var input = InputEnabled ? GameInput.Move : Vector2.zero;
            bool running = InputEnabled && GameInput.RunHeld && !RunBlocked && input.y > 0f;
            float speed = (running ? runSpeed : walkSpeed) * Mathf.Lerp(1f, minSpeedFactor, load) * SpeedMultiplier;
            var horizontal = (transform.right * input.x + transform.forward * input.y) * speed;
            if (tethered) horizontal = ApplyTether(horizontal);

            if (controller.isGrounded)
            {
                verticalVelocity = -2f; // 接地を安定させる
                if (InputEnabled && GameInput.JumpPressed)
                {
                    float height = jumpHeight * Mathf.Lerp(1f, minJumpFactor, load);
                    verticalVelocity = Mathf.Sqrt(2f * height * -gravity);
                }
            }
            // 前後の速度の平均で動かすと、フレームレートが変わってもジャンプの高さが変わらない
            float previousVertical = verticalVelocity;
            verticalVelocity += gravity * Time.deltaTime;
            float vertical = (previousVertical + verticalVelocity) * 0.5f;

            controller.Move((horizontal + Vector3.up * vertical) * Time.deltaTime);
        }

        /// <summary>引き綱の長さを超えて離れる方向の速度を取り除く。近づく・回り込む動きはそのまま。</summary>
        Vector3 ApplyTether(Vector3 velocity)
        {
            var away = transform.position - tetherPoint;
            away.y = 0f;
            float distance = away.magnitude;
            if (distance < tetherLength || distance < 0.001f) return velocity;
            var outward = away / distance;
            float outwardSpeed = Vector3.Dot(velocity, outward);
            return outwardSpeed > 0f ? velocity - outward * outwardSpeed : velocity;
        }

        /// <summary>歩いてぶつかった小物を軽く押す。重い物はほとんど動かない。</summary>
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var body = hit.rigidbody;
            if (body == null || body.isKinematic || hit.moveDirection.y < -0.3f) return;
            var push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            body.AddForceAtPosition(push * (2f / Mathf.Max(1f, body.mass)), hit.point, ForceMode.VelocityChange);
        }
    }
}
