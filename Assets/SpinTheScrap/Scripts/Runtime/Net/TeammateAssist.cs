using SpinTheScrap.Core;
using SpinTheScrap.Player;
using UnityEngine;

namespace SpinTheScrap.Net
{
    /// <summary>
    /// 協力プレイ：倒れた仲間のそばで [R] を押し続けると起こし、[F] で引きずって運ぶ（安全な所まで連れて行ってから起こす）。
    /// 起こす・引きずるのは倒れた本人のマシンで行う（NetPlayer がホスト経由で伝える）。
    /// </summary>
    public sealed class TeammateAssist : MonoBehaviour
    {
        const float DragSendInterval = 0.08f;
        const float DragBreakDistance = 4f;
        const float DragBehind = 1.1f;

        FirstPersonController player;
        PlayerHealth health;
        HazardRules rules;
        StringTable strings;
        float reviveProgress;
        float nextDragSendTime;

        public static TeammateAssist Instance { get; private set; }

        /// <summary>そばにいる倒れた仲間（いなければ null）。</summary>
        public NetPlayer Nearby { get; private set; }
        /// <summary>引きずっている仲間。</summary>
        public NetPlayer Dragging { get; private set; }
        /// <summary>起こすのに押し続けた割合（0〜1）。</summary>
        public float ReviveProgress => rules == null ? 0f : Mathf.Clamp01(reviveProgress / (float)rules.ReviveSeconds);

        public void Init(FirstPersonController controller, PlayerHealth playerHealth, HazardRules hazardRules, StringTable table)
        {
            player = controller;
            health = playerHealth;
            rules = hazardRules;
            strings = table;
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>HUD に出す案内（なければ null）。</summary>
        public string Prompt
        {
            get
            {
                if (strings == null) return null;
                if (Dragging != null) return strings.Get("hud.dragging");
                if (Nearby == null) return null;
                return reviveProgress > 0
                    ? strings.Format("hud.reviving", Mathf.RoundToInt(ReviveProgress * 100))
                    : strings.Get("hud.revive");
            }
        }

        void Update()
        {
            Nearby = null;
            var me = NetPlayer.Local;
            if (me == null || player == null || health == null || health.IsDown || !player.InputEnabled)
            {
                reviveProgress = 0f;
                Dragging = null;
                return;
            }

            Nearby = FindDownedTeammate(me);
            UpdateDrag(me);

            if (Nearby != null && GameInput.ReviveHeld)
            {
                reviveProgress += Time.deltaTime;
                if (reviveProgress >= rules.ReviveSeconds)
                {
                    me.Revive(Nearby);
                    reviveProgress = 0f;
                    if (Dragging == Nearby) Dragging = null;
                    Audio.Sfx.Play2D(Audio.SfxClips.Extract, 0.5f, 1.2f);
                }
            }
            else
            {
                reviveProgress = 0f;
            }
        }

        NetPlayer FindDownedTeammate(NetPlayer me)
        {
            NetPlayer best = null;
            float bestDistance = (float)rules.ReviveReach;
            foreach (var other in FindObjectsByType<NetPlayer>())
            {
                if (other == me || !other.IsDown) continue;
                float distance = Vector3.Distance(other.transform.position, player.transform.position);
                if (other == Dragging) distance = 0f; // 引きずっている相手を優先
                if (distance <= bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }
            return best;
        }

        void UpdateDrag(NetPlayer me)
        {
            if (GameInput.DragPressed)
            {
                if (Dragging != null) Dragging = null;
                else if (Nearby != null) Dragging = Nearby;
            }
            if (Dragging == null) return;
            if (!Dragging.IsDown || Vector3.Distance(Dragging.transform.position, player.transform.position) > DragBreakDistance)
            {
                Dragging = null;
                return;
            }
            if (Time.unscaledTime < nextDragSendTime) return;
            nextDragSendTime = Time.unscaledTime + DragSendInterval;
            // 自分の後ろに引きずる
            var forward = player.transform.forward;
            forward.y = 0f;
            me.Drag(Dragging, player.transform.position - forward.normalized * DragBehind);
        }
    }
}
