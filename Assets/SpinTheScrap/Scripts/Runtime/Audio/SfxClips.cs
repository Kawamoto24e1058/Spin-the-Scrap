using System;
using UnityEngine;

namespace SpinTheScrap.Audio
{
    /// <summary>
    /// 効果音をコードで合成する（仮素材。外部素材を使わないのでライセンス管理が要らない）。
    /// 起動時に 1 回だけ作り、Sfx から鳴らす。縦切り版（フェーズ 3）で本番の音に差し替える。
    /// </summary>
    public static class SfxClips
    {
        const int Rate = 44100;

        public static AudioClip[] Steps { get; private set; }
        public static AudioClip[] RunSteps { get; private set; }
        public static AudioClip Land { get; private set; }
        public static AudioClip[] Clinks { get; private set; }
        public static AudioClip Knock { get; private set; }
        public static AudioClip HeavyThud { get; private set; }
        public static AudioClip Shatter { get; private set; }
        public static AudioClip Whoosh { get; private set; }
        public static AudioClip Hurt { get; private set; }
        public static AudioClip BuzzLoop { get; private set; }
        public static AudioClip RattleLoop { get; private set; }
        public static AudioClip DroneLoop { get; private set; }
        public static AudioClip Screech { get; private set; }
        public static AudioClip Groan { get; private set; }
        public static AudioClip Spin { get; private set; }
        public static AudioClip Win { get; private set; }
        public static AudioClip Coin { get; private set; }
        public static AudioClip Extract { get; private set; }
        public static AudioClip Bang { get; private set; }

        // どれかの音に初めて触れたときに全部作る（引数で SfxClips.X を渡す時点で作られている必要がある）
        static SfxClips() => Build();

        /// <summary>音を先に作っておく（最初に鳴らすときの引っかかりを避けたいとき）。</summary>
        public static void EnsureBuilt() { }

        static void Build()
        {
            var rng = new System.Random(20261020);

            Steps = new[] { Footstep(rng, 0.11f, 0.5f), Footstep(rng, 0.11f, 0.45f), Footstep(rng, 0.12f, 0.55f) };
            RunSteps = new[] { Footstep(rng, 0.09f, 0.8f), Footstep(rng, 0.09f, 0.75f) };
            Land = Thud(rng, "Land", 0.3f, 70f, 0.9f);
            Clinks = new[] { Clink(rng, 1.0f), Clink(rng, 1.25f), Clink(rng, 0.85f) };
            Knock = KnockClip(rng);
            HeavyThud = Thud(rng, "Heavy thud", 0.45f, 48f, 0.85f);
            Shatter = ShatterClip(rng);
            Whoosh = WhooshClip(rng);
            Hurt = HurtClip(rng);
            BuzzLoop = Buzz(rng);
            RattleLoop = Rattle(rng);
            DroneLoop = Drone(rng);
            Screech = ScreechClip(rng);
            Groan = GroanClip(rng);
            Spin = SpinClip(rng);
            Win = Arpeggio("Win", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 0.5f);
            Coin = Arpeggio("Coin", new[] { 1318.5f, 1760f }, 0.07f, 0.25f);
            Extract = Arpeggio("Extract", new[] { 440f, 659.25f, 880f }, 0.14f, 0.7f);
            Bang = BangClip(rng);
        }

        /// <summary>爆竹のパパパン。大きな破裂音を 5 回、少しずつ間を空けて鳴らす。</summary>
        static AudioClip BangClip(System.Random rng)
        {
            var d = Buffer(1.1f);
            float when = 0f;
            for (int k = 0; k < 5; k++)
            {
                int start = (int)(when * Rate);
                float low = 0f;
                for (int j = 0; start + j < d.Length && j < 9000; j++)
                {
                    float t = j / (float)Rate;
                    low += (Noise(rng) - low) * 0.3f;
                    d[start + j] += (low * 1.5f + Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.5f) * Mathf.Exp(-t * 35f);
                }
                when += 0.08f + (float)rng.NextDouble() * 0.12f;
            }
            for (int i = 0; i < d.Length; i++) d[i] = (float)Math.Tanh(d[i] * 1.5f) * 0.85f;
            return Make("Bang", d);
        }

        // ---- 部品 ----

        static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2 - 1);

        static AudioClip Make(string name, float[] data)
        {
            // 鳴らした瞬間にプツッといわないよう、最初と最後を数ミリ秒だけ絞る
            int fade = Mathf.Min(64, data.Length / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] *= k;
                data[data.Length - 1 - i] *= k;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeLoop(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        // ---- 音 ----

        static AudioClip Footstep(System.Random rng, float seconds, float brightness)
        {
            var d = Buffer(seconds);
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 45f);
                low += (Noise(rng) - low) * (0.08f + 0.2f * brightness);
                float thump = Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 60f);
                d[i] = (low * 1.6f + thump * 0.6f) * env * 0.6f;
            }
            return Make("Step", d);
        }

        static AudioClip Thud(System.Random rng, string name, float seconds, float baseHz, float gain)
        {
            var d = Buffer(seconds);
            float low = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float hz = baseHz * (1.6f - 0.6f * Mathf.Clamp01(t / seconds));
                phase += 2f * Mathf.PI * hz / Rate;
                low += (Noise(rng) - low) * 0.05f;
                d[i] = (Mathf.Sin(phase) * 0.9f + low * 2f) * Mathf.Exp(-t * 14f) * gain * 0.8f;
            }
            return Make(name, d);
        }

        static AudioClip Clink(System.Random rng, float pitch)
        {
            var d = Buffer(0.35f);
            float[] partials = { 1150f, 2730f, 4120f, 5600f };
            float[] decays = { 9f, 14f, 22f, 30f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < partials.Length; k++)
                    s += Mathf.Sin(2f * Mathf.PI * partials[k] * pitch * t) * Mathf.Exp(-t * decays[k]) / (k + 1);
                float hit = Noise(rng) * Mathf.Exp(-t * 300f) * 0.4f;
                d[i] = (s * 0.5f + hit) * 0.6f;
            }
            return Make("Clink", d);
        }

        static AudioClip KnockClip(System.Random rng)
        {
            var d = Buffer(0.18f);
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                low += (Noise(rng) - low) * 0.15f;
                d[i] = (Mathf.Sin(2f * Mathf.PI * 210f * t) * 0.7f + low) * Mathf.Exp(-t * 30f) * 0.7f;
            }
            return Make("Knock", d);
        }

        static AudioClip ShatterClip(System.Random rng)
        {
            var d = Buffer(0.7f);
            float prev = 0f;
            // 細かい破片がばらばらに鳴る
            for (int burst = 0; burst < 26; burst++)
            {
                int start = (int)(Math.Pow(rng.NextDouble(), 2) * d.Length * 0.75);
                int length = rng.Next(400, 2200);
                float hz = 2500f + (float)rng.NextDouble() * 4500f;
                float amp = 0.25f + (float)rng.NextDouble() * 0.35f;
                for (int j = 0; j < length && start + j < d.Length; j++)
                {
                    float t = j / (float)Rate;
                    d[start + j] += (Mathf.Sin(2f * Mathf.PI * hz * t) * 0.6f + Noise(rng) * 0.4f) * Mathf.Exp(-t * 60f) * amp;
                }
            }
            for (int i = 0; i < d.Length; i++)
            {
                float hp = d[i] - prev; // 高い音だけ残す
                prev = d[i];
                d[i] = Mathf.Clamp(hp * 1.4f + Noise(rng) * Mathf.Exp(-i / (float)Rate * 25f) * 0.3f, -1f, 1f) * 0.7f;
            }
            return Make("Shatter", d);
        }

        static AudioClip WhooshClip(System.Random rng)
        {
            var d = Buffer(0.28f);
            float band = 0f, low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Sin(Mathf.PI * t / 0.28f);
                low += (Noise(rng) - low) * 0.25f;
                band += (low - band) * 0.08f;
                d[i] = (low - band) * env * 1.4f;
            }
            return Make("Whoosh", d);
        }

        static AudioClip HurtClip(System.Random rng)
        {
            var d = Buffer(0.28f);
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                low += (Noise(rng) - low) * 0.06f;
                float tone = Mathf.Sin(2f * Mathf.PI * (160f - 80f * t / 0.28f) * t);
                d[i] = (tone * 0.6f + low * 1.5f) * Mathf.Exp(-t * 10f) * 0.8f;
            }
            return Make("Hurt", d);
        }

        /// <summary>電気のジーという音。1 秒でちょうど一周するので継ぎ目なく繰り返せる。</summary>
        static AudioClip Buzz(System.Random rng)
        {
            var d = Buffer(1f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float hum = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 100f * t)) * 0.12f + Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.1f;
                d[i] = hum;
            }
            // バチッという火花
            for (int k = 0; k < 9; k++)
            {
                int start = rng.Next(0, d.Length - 900);
                for (int j = 0; j < 900; j++) d[start + j] += Noise(rng) * Mathf.Exp(-j / 120f) * 0.7f;
            }
            return MakeLoop("Buzz", d);
        }

        /// <summary>台車のガタガタ。短いカタカタを不規則に並べる。</summary>
        static AudioClip Rattle(System.Random rng)
        {
            var d = Buffer(0.8f);
            for (int k = 0; k < 22; k++)
            {
                int start = rng.Next(0, d.Length - 1500);
                float hz = 600f + (float)rng.NextDouble() * 900f;
                float amp = 0.2f + (float)rng.NextDouble() * 0.3f;
                for (int j = 0; j < 1500; j++)
                {
                    float t = j / (float)Rate;
                    d[start + j] += (Mathf.Sin(2f * Mathf.PI * hz * t) * 0.5f + Noise(rng) * 0.5f) * Mathf.Exp(-t * 90f) * amp;
                }
            }
            for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp(d[i], -1f, 1f) * 0.8f;
            return MakeLoop("Rattle", d);
        }

        /// <summary>敵の気配。わずかにずれた 2 つの低音がうなる。2 秒でちょうど一周する。</summary>
        static AudioClip Drone(System.Random rng)
        {
            var d = Buffer(2f);
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                low += (Noise(rng) - low) * 0.01f;
                float breath = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
                d[i] = (Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.4f + Mathf.Sin(2f * Mathf.PI * 58f * t) * 0.35f
                        + Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.1f + low * 3f * breath) * 0.6f;
            }
            return MakeLoop("Drone", d);
        }

        static AudioClip ScreechClip(System.Random rng)
        {
            var d = Buffer(0.9f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float hz = 320f + 700f * Mathf.Clamp01(t / 0.35f) + Mathf.Sin(2f * Mathf.PI * 9f * t) * 40f;
                phase += hz / Rate;
                float saw = (phase % 1f) * 2f - 1f;
                float env = Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-Mathf.Max(0f, t - 0.3f) * 5f);
                d[i] = (float)Math.Tanh((saw * 0.7f + Noise(rng) * 0.3f) * 2.5f) * env * 0.55f;
            }
            return Make("Screech", d);
        }

        static AudioClip GroanClip(System.Random rng)
        {
            var d = Buffer(0.8f);
            float phase = 0f, low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float hz = 140f - 70f * t / 0.8f;
                phase += hz / Rate;
                float saw = (phase % 1f) * 2f - 1f;
                low += (saw - low) * 0.1f;
                d[i] = low * Mathf.Sin(Mathf.PI * t / 0.8f) * 0.8f;
            }
            return Make("Groan", d);
        }

        /// <summary>レバーを引くガコンと、リールが回って止まるカチカチ。</summary>
        static AudioClip SpinClip(System.Random rng)
        {
            var d = Buffer(1.2f);
            float low = 0f;
            for (int i = 0; i < 9000; i++)
            {
                float t = i / (float)Rate;
                low += (Noise(rng) - low) * 0.05f;
                d[i] += (Mathf.Sin(2f * Mathf.PI * 80f * t) * 0.6f + low * 1.5f) * Mathf.Exp(-t * 20f) * 0.8f;
            }
            // カチカチがだんだんゆっくりになって止まる
            float when = 0.12f, gap = 0.035f;
            while (when < 1.1f)
            {
                int start = (int)(when * Rate);
                for (int j = 0; j < 500 && start + j < d.Length; j++)
                    d[start + j] += Mathf.Sin(2f * Mathf.PI * 2200f * j / Rate) * Mathf.Exp(-j / 80f) * 0.35f;
                when += gap;
                gap *= 1.12f;
            }
            return Make("Spin", d);
        }

        static AudioClip Arpeggio(string name, float[] notes, float step, float tail)
        {
            var d = Buffer(step * notes.Length + tail);
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * step * Rate);
                for (int j = 0; start + j < d.Length; j++)
                {
                    float t = j / (float)Rate;
                    float env = Mathf.Exp(-t * 6f);
                    d[start + j] += (Mathf.Sin(2f * Mathf.PI * notes[n] * t) + Mathf.Sin(4f * Mathf.PI * notes[n] * t) * 0.25f) * env * 0.22f;
                }
            }
            return Make(name, d);
        }
    }
}
