using System;
using SpinTheScrap.Core;
using UnityEngine;

namespace SpinTheScrap.Player
{
    /// <summary>
    /// プレイヤーの体力（要件書 4.2）。数値と計算は HazardRules / HealthState（Core）にある。
    /// 着地の速さから落下ダメージを受ける。フェーズ 1 ではホストだけがダメージを計算する。
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class PlayerHealth : MonoBehaviour
    {
        const float FlashSeconds = 0.4f;

        FirstPersonController controller;
        HazardRules rules;
        float lastDamageTime = -10f;

        public HealthState State { get; private set; }
        public bool IsDown => State != null && State.IsDown;
        /// <summary>直前にダメージを受けてからの、画面を赤くする強さ（0〜1）。</summary>
        public float DamageFlash => Mathf.Clamp01(1f - (Time.time - lastDamageTime) / FlashSeconds);

        /// <summary>体力が 0 になった。</summary>
        public event Action Downed;

        public void Init(HazardRules hazardRules)
        {
            rules = hazardRules;
            State = new HealthState(rules.MaxHealth);
        }

        void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            controller.Landed += OnLanded;
        }

        void OnDestroy()
        {
            if (controller != null) controller.Landed -= OnLanded;
        }

        public void Damage(double amount)
        {
            if (State == null || amount <= 0 || State.IsDown) return;
            lastDamageTime = Time.time;
            if (State.Damage(amount)) Downed?.Invoke();
        }

        public void ResetHealth()
        {
            State?.Reset();
            lastDamageTime = -10f;
        }

        void OnLanded(float speed)
        {
            if (rules != null) Damage(rules.FallDamage(speed));
        }
    }
}
