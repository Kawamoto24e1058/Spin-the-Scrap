using System;
using SpinTheScrap.Audio;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using SpinTheScrap.Player;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>
    /// 電気が流れる水たまり（要件書 4.2 ハザード）。中に立つと体力が減り続ける。
    /// 金属のガラクタを投げ込むとショートして、しばらく止まる（入れた物はそのまま拾い直せる）。
    /// 判定は箱の範囲で行う。プレイヤーは足元の位置、ガラクタはトリガーへの進入で見る。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ElectricPuddle : MonoBehaviour
    {
        static readonly Color LiveColor = new Color(0.25f, 0.55f, 1f);
        static readonly Color ShortedColor = new Color(0.12f, 0.16f, 0.2f);

        HazardRules rules;
        Func<string, SymbolTag> tagsOf;
        PlayerHealth player;
        BoxCollider box;
        Renderer surface;
        Light glow;
        float shortedUntil = -1f;
        bool? shownLive;
        AudioSource buzz;

        public bool IsLive => Time.time >= shortedUntil;

        public void Init(HazardRules hazardRules, Func<string, SymbolTag> symbolTags, PlayerHealth playerHealth, Renderer surfaceRenderer, Light glowLight)
        {
            rules = hazardRules;
            tagsOf = symbolTags;
            player = playerHealth;
            surface = surfaceRenderer;
            glow = glowLight;
            buzz = Sfx.CreateLoop(gameObject, SfxClips.BuzzLoop, 0.5f, 12f);
            UpdateLook();
        }

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void Update()
        {
            if (rules == null) return;
            UpdateLook();
            if (!IsLive || player == null || player.IsDown) return;
            var feet = player.transform.position + Vector3.down * 0.9f;
            if (Contains(feet)) player.Damage(rules.PuddleDamagePerSecond * Time.deltaTime);
        }

        void OnTriggerEnter(Collider other)
        {
            if (rules == null || !IsLive) return;
            var body = other.attachedRigidbody;
            if (body == null) return;
            var scrap = body.GetComponent<Scrap>();
            if (scrap == null || !rules.CanShortCircuit(tagsOf(scrap.SymbolId))) return;
            shortedUntil = Time.time + (float)rules.PuddleShortCircuitSeconds;
        }

        bool Contains(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition) - box.center;
            var half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        void UpdateLook()
        {
            bool live = IsLive;
            if (surface != null && live != shownLive)
            {
                MaterialUtil.SetColor(surface.material, live ? LiveColor : ShortedColor);
                // ショートした瞬間と、戻った瞬間にバチッと鳴らす
                if (shownLive.HasValue) Sfx.PlayAt(SfxClips.Shatter, transform.position, 0.35f, live ? 1.6f : 0.6f);
                shownLive = live;
            }
            if (buzz != null) buzz.volume = live ? 0.5f : 0f;
            if (glow != null)
            {
                glow.enabled = live;
                // 不規則にちらつかせる
                glow.intensity = 2f + Mathf.PerlinNoise(Time.time * 8f, transform.position.x) * 3f;
            }
        }
    }
}
