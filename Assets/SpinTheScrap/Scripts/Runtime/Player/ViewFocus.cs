using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>
    /// スロット台のメニューを開いている間、カメラを台の正面へ寄せて、画面の左側に台が映るようにする
    /// （右側にメニューが出る）。閉じると一人称の視点へなめらかに戻る。実時間で動く（ソロではメニュー中に時間が止まる）。
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class ViewFocus : MonoBehaviour
    {
        [SerializeField] float blendSpeed = 3.5f;

        FirstPersonController controller;
        Vector3 restLocalPosition;
        Transform target;
        Vector3 localEye;
        Vector3 localLook;
        float weight;
        bool restored = true;

        public bool IsFocused => target != null;

        void Awake() => controller = GetComponent<FirstPersonController>();

        void Start()
        {
            if (controller.ViewCamera != null) restLocalPosition = controller.ViewCamera.transform.localPosition;
        }

        /// <summary>target から見た位置 eye に立ち、look を見る（どちらも target のローカル座標）。</summary>
        public void Focus(Transform focusTarget, Vector3 eye, Vector3 look)
        {
            target = focusTarget;
            localEye = eye;
            localLook = look;
        }

        public void Release() => target = null;

        void LateUpdate()
        {
            var cam = controller.ViewCamera;
            if (cam == null) return;
            if (target == null && ReferenceEquals(target, null) == false) target = null; // 階層が変わって台が消えた
            weight = Mathf.MoveTowards(weight, target != null ? 1f : 0f, Time.unscaledDeltaTime * blendSpeed);
            var t = cam.transform;
            if (weight <= 0f)
            {
                if (!restored)
                {
                    t.localPosition = restLocalPosition;
                    t.localRotation = controller.RestCameraRotation;
                    restored = true;
                }
                return;
            }
            restored = false;
            var parent = t.parent;
            var basePosition = parent.TransformPoint(restLocalPosition);
            var baseRotation = parent.rotation * controller.RestCameraRotation;
            if (target != null)
            {
                lastEye = target.TransformPoint(localEye);
                lastRotation = Quaternion.LookRotation(target.TransformPoint(localLook) - lastEye, Vector3.up);
            }
            float s = Mathf.SmoothStep(0f, 1f, weight);
            t.position = Vector3.Lerp(basePosition, lastEye, s);
            t.rotation = Quaternion.Slerp(baseRotation, lastRotation, s);
        }

        Vector3 lastEye;
        Quaternion lastRotation = Quaternion.identity;
    }
}
