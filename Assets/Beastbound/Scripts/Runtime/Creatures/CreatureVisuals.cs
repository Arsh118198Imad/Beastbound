using Beastbound.Core;
using UnityEngine;

namespace Beastbound.Creatures
{
    /// <summary>
    /// Builds the visible model for a creature and configures its <see cref="CreatureAnimator"/>: the skinned
    /// Blender model when it has been imported, otherwise the procedural primitive Pebbloom.
    /// </summary>
    public static class CreatureVisuals
    {
        public static CreatureAnimator Attach(GameObject owner, CreatureSpecies species, string uid)
        {
            int variant = string.IsNullOrEmpty(uid) ? 0 : uid.GetHashCode() & 0x7;
            var anim = owner.AddComponent<CreatureAnimator>();
            anim.MaxSpeed = species.runSpeed;

            var cfg = QuadrupedConfig.For(species.model);
            var holder = new GameObject("Model").transform;
            holder.gameObject.layer = owner.layer;
            holder.SetParent(owner.transform, false);
            var model = ModelLibrary.Instantiate(cfg.ModelName, holder, owner.layer);
            if (model != null)
            {
                model.Root.localScale = Vector3.one * cfg.Scale;
                // Individual colour variation, like the primitive model's per-uid tint.
                ModelLibrary.Tint(model, "Fur", ((variant % 8) / 8f - 0.5f) * 0.08f);
                anim.InitializeSkinned(new QuadrupedRig(holder, model, cfg));
                return anim;
            }

            Object.Destroy(holder.gameObject);
            float scale = species.model == CreatureModelKind.Thalorix ? 1.6f : species.modelScale;
            var parts = PebbloomModel.Build(owner.transform, species.palette, scale, variant);
            anim.BaseScale = scale;
            anim.Initialize(parts, species.palette.crystalGlow);
            return anim;
        }
    }
}
