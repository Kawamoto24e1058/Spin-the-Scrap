using System.Collections.Generic;
using UnityEngine;

namespace SpinTheScrap.Audio
{
    /// <summary>
    /// 効果音を鳴らす窓口。1 回鳴らす音は使い回しの AudioSource で、繰り返す音（気配・ジー・ガタガタ）は呼び出し側に AudioSource を作る。
    /// 空間の音は距離で小さくなり、左右も聞き分けられる。
    /// </summary>
    public static class Sfx
    {
        const int PoolSize = 24;
        const float MaxDistance = 28f;

        static readonly List<AudioSource> pool = new List<AudioSource>();
        static Transform root;
        static int next;

        /// <summary>空間のある位置で 1 回鳴らす。</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            var source = Take();
            if (source == null || clip == null) return;
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.ignoreListenerPause = false;
            Play(source, clip, volume, pitch);
        }

        /// <summary>画面の音として 1 回鳴らす（自分の足音・UI など）。拠点メニューで世界の音を止めている間も鳴る。</summary>
        public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            var source = Take();
            if (source == null || clip == null) return;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            Play(source, clip, volume, pitch);
        }

        /// <summary>繰り返し鳴らす音源を owner に付ける。音量は呼び出し側が毎フレーム変える。</summary>
        public static AudioSource CreateLoop(GameObject owner, AudioClip clip, float volume, float maxDistance = MaxDistance)
        {
            SfxClips.EnsureBuilt();
            var source = owner.AddComponent<AudioSource>();
            Configure3D(source, maxDistance);
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            // 同じ音が重なってもうならないよう、再生位置をずらす
            source.timeSamples = Random.Range(0, clip.samples);
            source.Play();
            return source;
        }

        public static AudioClip Pick(AudioClip[] clips) => clips[Random.Range(0, clips.Length)];

        static void Play(AudioSource source, AudioClip clip, float volume, float pitch)
        {
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.Play();
        }

        static AudioSource Take()
        {
            SfxClips.EnsureBuilt();
            if (root == null)
            {
                pool.Clear();
                var go = new GameObject("Sfx");
                Object.DontDestroyOnLoad(go);
                root = go.transform;
                for (int i = 0; i < PoolSize; i++)
                {
                    var child = new GameObject("Source " + i);
                    child.transform.SetParent(root, false);
                    var source = child.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    Configure3D(source, MaxDistance);
                    pool.Add(source);
                }
            }
            // いちばん古い音から使い回す（鳴り終わっていない物があれば次を探す）
            for (int i = 0; i < pool.Count; i++)
            {
                var source = pool[(next + i) % pool.Count];
                if (source.isPlaying) continue;
                next = (next + i + 1) % pool.Count;
                return source;
            }
            var oldest = pool[next];
            next = (next + 1) % pool.Count;
            return oldest;
        }

        static void Configure3D(AudioSource source, float maxDistance)
        {
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            root = null;
            pool.Clear();
            next = 0;
        }
    }
}
