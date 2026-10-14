using Beastbound.Core;
using UnityEngine;

namespace Beastbound.Creatures
{
    /// <summary>References to the animated parts of a procedural creature model.</summary>
    public class CreatureModelParts
    {
        public Transform Root;        // bobbing / squash root
        public Transform Body;
        public Transform Head;
        public Transform EarLeft;
        public Transform EarRight;
        public Transform Tail;
        public Transform TailBloom;
        public Transform[] Legs;      // FL, FR, BL, BR
        public Transform[] Eyes;
        public Transform Crystal;
        public Renderer CrystalRenderer;
        public Material CrystalMaterial;
    }

    /// <summary>
    /// Builds Pebbloom out of simple shapes. The silhouette was designed to read clearly at a distance:
    /// round pebble body, two tall petal-ears, a flower on a curling tail stem and a glowing brow crystal.
    /// Replace with an imported model later by building a prefab with the same part names.
    /// </summary>
    public static class PebbloomModel
    {
        public static CreatureModelParts Build(Transform parent, CreaturePalette palette, float scale, int variantSeed)
        {
            var rng = new System.Random(variantSeed);
            float tint = (float)(rng.NextDouble() * 0.08 - 0.04);
            Color body = Shift(palette.body, tint);
            Color bodyDark = Shift(palette.bodyDark, tint);

            string key = "Pebbloom_" + variantSeed;
            Material matBody = MaterialFactory.Lit(key + "_Body", body, 0.25f);
            Material matDark = MaterialFactory.Lit(key + "_Dark", bodyDark, 0.2f);
            Material matBelly = MaterialFactory.Lit("Pebbloom_Belly", palette.belly, 0.15f);
            Material matMoss = MaterialFactory.Lit("Pebbloom_Moss", palette.moss, 0.05f);
            Material matPetal = MaterialFactory.Lit("Pebbloom_Petal", palette.petal, 0.35f);
            Material matPetalCenter = MaterialFactory.Lit("Pebbloom_PetalCenter", palette.petalCenter, 0.3f);
            Material matEye = MaterialFactory.Lit("Pebbloom_Eye", palette.eye, 0.9f);
            Material matEyeShine = MaterialFactory.Emissive("Pebbloom_EyeShine", Color.white, Color.white * 0.6f, 0.9f);
            // Unique per creature because the glow intensity is animated (mood/binding).
            Material matCrystal = MaterialFactory.Unique("Pebbloom_Crystal", palette.crystal, 0.85f);
            MaterialFactory.SetEmission(matCrystal, palette.crystalGlow * 0.8f);

            Mesh sphere = MeshKit.Primitive(PrimitiveType.Sphere);
            Mesh capsule = MeshKit.Primitive(PrimitiveType.Capsule);

            var parts = new CreatureModelParts();
            var root = new GameObject("Model").transform;
            root.gameObject.layer = parent.gameObject.layer;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;
            parts.Root = root;

            // Body + back plates
            parts.Body = MeshKit.AddPart(root, "Body", sphere, matBody, new Vector3(0f, 0.5f, -0.05f),
                new Vector3(0.9f, 0.72f, 1f), Quaternion.identity).transform;
            MeshKit.AddPart(parts.Body, "BackPlate", sphere, matDark, new Vector3(0f, 0.32f, -0.12f),
                new Vector3(0.82f, 0.5f, 0.78f), Quaternion.Euler(-12f, 0f, 0f));
            MeshKit.AddPart(parts.Body, "Moss", sphere, matMoss, new Vector3(0f, 0.45f, -0.08f),
                new Vector3(0.62f, 0.3f, 0.6f), Quaternion.Euler(-8f, 0f, 0f));
            MeshKit.AddPart(parts.Body, "Belly", sphere, matBelly, new Vector3(0f, -0.12f, 0.28f),
                new Vector3(0.72f, 0.62f, 0.55f), Quaternion.identity);

            // Head
            var head = new GameObject("Head").transform;
            head.gameObject.layer = root.gameObject.layer;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0f, 0.78f, 0.42f);
            parts.Head = head;
            MeshKit.AddPart(head, "Skull", sphere, matBody, Vector3.zero, new Vector3(0.62f, 0.55f, 0.56f), Quaternion.identity);
            MeshKit.AddPart(head, "Snout", sphere, matBelly, new Vector3(0f, -0.08f, 0.24f),
                new Vector3(0.32f, 0.22f, 0.22f), Quaternion.identity);
            MeshKit.AddPart(head, "Nose", sphere, matEye, new Vector3(0f, -0.02f, 0.345f),
                new Vector3(0.07f, 0.05f, 0.05f), Quaternion.identity, false);

            parts.Eyes = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var eye = MeshKit.AddPart(head, i == 0 ? "EyeL" : "EyeR", sphere, matEye,
                    new Vector3(0.15f * side, 0.06f, 0.24f), new Vector3(0.12f, 0.15f, 0.08f),
                    Quaternion.Euler(0f, 18f * side, 0f), false).transform;
                MeshKit.AddPart(eye, "Shine", sphere, matEyeShine, new Vector3(-0.15f * side, 0.25f, 0.4f),
                    new Vector3(0.35f, 0.3f, 0.35f), Quaternion.identity, false);
                parts.Eyes[i] = eye;
            }

            // Brow crystal
            var crystal = MeshKit.AddPart(head, "Crystal", MeshKit.Crystal(6, 0.5f, 1.1f, 0.5f), matCrystal,
                new Vector3(0f, 0.25f, 0.17f), Vector3.one * 0.12f, Quaternion.Euler(25f, 0f, 0f), false);
            parts.Crystal = crystal.transform;
            parts.CrystalRenderer = crystal.GetComponent<Renderer>();
            parts.CrystalMaterial = matCrystal;

            // Leaf sprout between the ears
            MeshKit.AddPart(head, "SproutA", sphere, matMoss, new Vector3(0.04f, 0.3f, -0.02f),
                new Vector3(0.06f, 0.16f, 0.1f), Quaternion.Euler(0f, 0f, -25f));
            MeshKit.AddPart(head, "SproutB", sphere, matMoss, new Vector3(-0.04f, 0.29f, -0.04f),
                new Vector3(0.06f, 0.13f, 0.09f), Quaternion.Euler(0f, 0f, 30f));

            // Petal ears (pivot at the base so they can droop/perk)
            parts.EarLeft = BuildEar(head, "EarL", -1f, matPetal, matPetalCenter, sphere);
            parts.EarRight = BuildEar(head, "EarR", 1f, matPetal, matPetalCenter, sphere);

            // Legs (pivot at hip)
            parts.Legs = new Transform[4];
            Vector3[] hips =
            {
                new Vector3(-0.27f, 0.3f, 0.25f), new Vector3(0.27f, 0.3f, 0.25f),
                new Vector3(-0.27f, 0.3f, -0.3f), new Vector3(0.27f, 0.3f, -0.3f)
            };
            for (int i = 0; i < 4; i++)
            {
                var hip = new GameObject("Leg" + i).transform;
                hip.gameObject.layer = root.gameObject.layer;
                hip.SetParent(root, false);
                hip.localPosition = hips[i];
                MeshKit.AddPart(hip, "Limb", capsule, matDark, new Vector3(0f, -0.16f, 0f),
                    new Vector3(0.2f, 0.16f, 0.2f), Quaternion.identity);
                MeshKit.AddPart(hip, "Paw", sphere, matBelly, new Vector3(0f, -0.28f, 0.04f),
                    new Vector3(0.2f, 0.09f, 0.24f), Quaternion.identity, false);
                parts.Legs[i] = hip;
            }

            // Tail: curling stem with a flower at the end
            var tail = new GameObject("Tail").transform;
            tail.gameObject.layer = root.gameObject.layer;
            tail.SetParent(root, false);
            tail.localPosition = new Vector3(0f, 0.55f, -0.5f);
            parts.Tail = tail;
            Material matStem = MaterialFactory.Lit("Pebbloom_Stem", palette.moss * 0.85f, 0.1f);
            // Capsule axis is local Y; Euler(-60) points it back-and-up, Euler(-30) curls it upward.
            MeshKit.AddPart(tail, "Stem1", capsule, matStem, new Vector3(0f, 0.09f, -0.156f),
                new Vector3(0.08f, 0.18f, 0.08f), Quaternion.Euler(-60f, 0f, 0f));
            MeshKit.AddPart(tail, "Stem2", capsule, matStem, new Vector3(0f, 0.293f, -0.375f),
                new Vector3(0.07f, 0.13f, 0.07f), Quaternion.Euler(-30f, 0f, 0f));

            var bloom = new GameObject("Bloom").transform;
            bloom.gameObject.layer = root.gameObject.layer;
            bloom.SetParent(tail, false);
            bloom.localPosition = new Vector3(0f, 0.45f, -0.46f);
            bloom.localRotation = Quaternion.Euler(-30f, 0f, 0f);
            parts.TailBloom = bloom;
            MeshKit.AddPart(bloom, "Center", sphere, matPetalCenter, Vector3.zero, Vector3.one * 0.13f, Quaternion.identity);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                var rot = Quaternion.Euler(0f, a, 0f);
                MeshKit.AddPart(bloom, "Petal" + i, sphere, matPetal, rot * new Vector3(0f, 0f, 0.12f),
                    new Vector3(0.12f, 0.05f, 0.2f), rot * Quaternion.Euler(-15f, 0f, 0f));
            }

            return parts;
        }

        private static Transform BuildEar(Transform head, string name, float side, Material petal, Material inner, Mesh sphere)
        {
            var pivot = new GameObject(name).transform;
            pivot.gameObject.layer = head.gameObject.layer;
            pivot.SetParent(head, false);
            pivot.localPosition = new Vector3(0.17f * side, 0.2f, -0.04f);
            pivot.localRotation = Quaternion.Euler(-10f, 0f, -22f * side);
            MeshKit.AddPart(pivot, "Petal", sphere, petal, new Vector3(0f, 0.24f, 0f),
                new Vector3(0.17f, 0.5f, 0.08f), Quaternion.identity);
            MeshKit.AddPart(pivot, "Inner", sphere, inner, new Vector3(0f, 0.2f, 0.025f),
                new Vector3(0.08f, 0.32f, 0.05f), Quaternion.identity, false);
            return pivot;
        }

        private static Color Shift(Color c, float amount)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + amount * 0.5f, 1f);
            v = Mathf.Clamp01(v + amount);
            var result = Color.HSVToRGB(h, s, v);
            result.a = c.a;
            return result;
        }
    }
}
