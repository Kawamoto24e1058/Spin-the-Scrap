using System.Collections.Generic;
using SpinTheScrap.Core;
using SpinTheScrap.Interaction;
using UnityEngine;

namespace SpinTheScrap.World
{
    /// <summary>
    /// ガラクタの生成。フェーズ 0〜2 は仮素材（プリミティブ）で、形と色だけで見分けられるようにする。
    /// モデルが用意できたら SymbolDefinitionAsset.model に差し替える。
    /// </summary>
    public static class ScrapFactory
    {
        struct Look
        {
            public PrimitiveType Shape;
            public Vector3 Scale;
            public Color Color;

            public Look(PrimitiveType shape, Vector3 scale, Color color)
            {
                Shape = shape;
                Scale = scale;
                Color = color;
            }
        }

        static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            { "empty_can", new Look(PrimitiveType.Cylinder, new Vector3(0.14f, 0.1f, 0.14f), new Color(0.75f, 0.75f, 0.8f)) },
            { "rusty_screw", new Look(PrimitiveType.Cylinder, new Vector3(0.1f, 0.16f, 0.1f), new Color(0.55f, 0.3f, 0.15f)) },
            { "broken_radio", new Look(PrimitiveType.Cube, new Vector3(0.4f, 0.25f, 0.15f), new Color(0.35f, 0.25f, 0.2f)) },
            { "chipped_lucky_cat", new Look(PrimitiveType.Capsule, new Vector3(0.22f, 0.15f, 0.22f), new Color(0.95f, 0.95f, 0.9f)) },
            { "lottery_bundle", new Look(PrimitiveType.Cube, new Vector3(0.3f, 0.08f, 0.2f), new Color(0.95f, 0.85f, 0.2f)) },
            { "old_calculator", new Look(PrimitiveType.Cube, new Vector3(0.2f, 0.06f, 0.3f), new Color(0.3f, 0.3f, 0.32f)) },
            { "plush_arm", new Look(PrimitiveType.Capsule, new Vector3(0.14f, 0.18f, 0.14f), new Color(0.95f, 0.55f, 0.7f)) },
            { "mannequin_head", new Look(PrimitiveType.Sphere, new Vector3(0.45f, 0.5f, 0.45f), new Color(0.9f, 0.8f, 0.7f)) },
            { "cracked_mirror", new Look(PrimitiveType.Cube, new Vector3(0.6f, 0.9f, 0.08f), new Color(0.6f, 0.85f, 0.95f)) },
            { "safe_door", new Look(PrimitiveType.Cube, new Vector3(1.6f, 0.25f, 1.4f), new Color(0.25f, 0.27f, 0.3f)) },
            { "bent_spoon", new Look(PrimitiveType.Cube, new Vector3(0.04f, 0.02f, 0.2f), new Color(0.8f, 0.8f, 0.82f)) },
            { "lunch_box", new Look(PrimitiveType.Cube, new Vector3(0.45f, 0.18f, 0.3f), new Color(0.3f, 0.5f, 0.75f)) },
            { "tin_robot", new Look(PrimitiveType.Cube, new Vector3(0.12f, 0.22f, 0.1f), new Color(0.7f, 0.65f, 0.4f)) },
            { "jack_in_the_box", new Look(PrimitiveType.Cube, new Vector3(0.4f, 0.4f, 0.4f), new Color(0.85f, 0.25f, 0.2f)) },
            { "old_newspaper", new Look(PrimitiveType.Cube, new Vector3(0.3f, 0.06f, 0.4f), new Color(0.8f, 0.78f, 0.68f)) },
            { "losing_ticket", new Look(PrimitiveType.Cube, new Vector3(0.18f, 0.01f, 0.08f), new Color(0.6f, 0.85f, 0.6f)) },
            { "pair_of_dice", new Look(PrimitiveType.Cube, new Vector3(0.07f, 0.07f, 0.07f), new Color(0.95f, 0.95f, 0.95f)) },
            { "rusty_horseshoe", new Look(PrimitiveType.Cylinder, new Vector3(0.16f, 0.015f, 0.16f), new Color(0.5f, 0.32f, 0.2f)) },
            { "creepy_doll", new Look(PrimitiveType.Capsule, new Vector3(0.3f, 0.4f, 0.3f), new Color(0.92f, 0.88f, 0.86f)) },
            { "broken_vending_machine", new Look(PrimitiveType.Cube, new Vector3(1.0f, 1.8f, 0.8f), new Color(0.6f, 0.15f, 0.15f)) },
        };

        /// <summary>見た目（形・大きさ・色）。スロット台のリールに小さな模型を並べるのにも使う。</summary>
        public static void LookOf(SymbolDefinition def, out PrimitiveType shape, out Vector3 scale, out Color color)
        {
            var look = Looks.TryGetValue(def.Id, out var l) ? l : DefaultLook(def);
            shape = look.Shape;
            scale = look.Scale;
            color = look.Color;
        }

        /// <summary>
        /// 協力プレイ用：渡した物（NetScrap のプレハブ）を本体にして、見た目は子に作る。
        /// 本体の大きさを 1 のままにしておくため（NetworkTransform が位置と向きだけを配る）。
        /// </summary>
        public static Scrap CreateOn(GameObject root, SymbolDefinition def, Vector3 position, Quaternion rotation)
        {
            var look = Looks.TryGetValue(def.Id, out var l) ? l : DefaultLook(def);
            root.transform.SetPositionAndRotation(position, rotation);
            var visual = GameObject.CreatePrimitive(look.Shape);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = look.Scale;
            visual.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(look.Color);
            root.name = def.Id;
            var scrap = root.GetComponent<Scrap>();
            if (scrap == null) scrap = root.AddComponent<Scrap>();
            scrap.Configure(def.Id, def.Size, def.Fragile);
            return scrap;
        }

        public static Scrap Create(SymbolDefinition def, Vector3 position, Quaternion rotation, Transform parent, GameObject modelPrefab = null)
        {
            GameObject go;
            if (modelPrefab != null)
            {
                go = Object.Instantiate(modelPrefab, position, rotation, parent);
            }
            else
            {
                var look = Looks.TryGetValue(def.Id, out var l) ? l : DefaultLook(def);
                go = GameObject.CreatePrimitive(look.Shape);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(position, rotation);
                go.transform.localScale = look.Scale;
                go.GetComponent<Renderer>().sharedMaterial = MaterialUtil.Create(look.Color);
            }
            go.name = def.Id;

            var scrap = go.GetComponent<Scrap>();
            if (scrap == null) scrap = go.AddComponent<Scrap>();
            scrap.Configure(def.Id, def.Size, def.Fragile);
            return scrap;
        }

        static Look DefaultLook(SymbolDefinition def)
        {
            switch (def.Size)
            {
                case ScrapSize.Huge: return new Look(PrimitiveType.Cube, new Vector3(1.6f, 0.6f, 0.8f), Color.gray);
                case ScrapSize.Large: return new Look(PrimitiveType.Cube, new Vector3(0.6f, 0.5f, 0.5f), Color.gray);
                default: return new Look(PrimitiveType.Cube, Vector3.one * 0.25f, Color.gray);
            }
        }
    }
}
