using System;
using SpinTheScrap.Audio;
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
        float lastHurtSoundTime = -10f;

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
            // 水たまりのように毎フレーム受けるダメージで鳴りっぱなしにならないよう、間を空ける
            if (Time.time - lastHurtSoundTime > 0.5f)
            {
                lastHurtSoundTime = Time.time;
                Sfx.Play2D(SfxClips.Hurt, Mathf.Clamp01((float)amount / 30f) * 0.5f + 0.35f, UnityEngine.Random.Range(0.9f, 1.05f));
            }
            lastDamageTime = Time.time;
            if (State.Damage(amount)) Downed?.Invoke();
        }

        /// <summary>回復する。実際に回復したら true。</summary>
        public bool Heal(double amount)
        {
            if (State == null) return false;
            return State.Heal(amount) > 0;
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
