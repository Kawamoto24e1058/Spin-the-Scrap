using SpinTheScrap.Core;
using SpinTheScrap.World;
using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>
    /// プレイヤーの足音と着地の音を敵に知らせる。走ると遠くまで聞こえ、歩けばほとんど聞こえない。
    /// 音の大きさは EnemyRules（Core）。
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class PlayerNoise : MonoBehaviour
    {
        [Tooltip("足音を出す間隔 (s)")]
        [SerializeField] float stepInterval = 0.45f;
        [Tooltip("この速さ (m/s) を超えたら走りの足音にする（歩きは 4m/s）")]
        [SerializeField] float runSpeedThreshold = 4.6f;
        [Tooltip("この速さ (m/s) 未満なら足音を出さない")]
        [SerializeField] float silentSpeed = 0.5f;

        FirstPersonController controller;
        EnemyRules rules;
        float nextStepTime;

        public void Init(EnemyRules enemyRules) => rules = enemyRules;

        void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            controller.Landed += OnLanded;
        }

        void OnDestroy()
        {
            if (controller != null) controller.Landed -= OnLanded;
        }

        void Update()
        {
            if (rules == null || !controller.Controller.isGrounded || Time.time < nextStepTime) return;
            var velocity = controller.Controller.velocity;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (speed < silentSpeed) return;
            nextStepTime = Time.time + stepInterval;
            float radius = (float)(speed > runSpeedThreshold ? rules.RunNoiseRadius : rules.WalkNoiseRadius);
            NoiseEvents.Emit(transform.position, radius);
        }

        void OnLanded(float speed)
        {
            // 段差を下りただけの着地は数えない
            if (rules != null && speed > 4f) NoiseEvents.Emit(transform.position, (float)rules.LandNoiseRadius);
        }
    }
}
