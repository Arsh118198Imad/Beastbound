using System.Collections.Generic;
using UnityEngine;

namespace Beastbound.Creatures
{
    /// <summary>
    /// Poses named bones in model space, so procedural animation can say "pitch this leg 30° forward" without
    /// caring how each bone's local axes were exported. Rotations are applied over the rig's rest pose.
    ///
    /// Angles use the same convention as the web edition (web/js/render/models.js): x pitches (negative swings a
    /// leg forward), y turns, z rolls, with y and z measured in a right-handed frame. Unity is left-handed (the
    /// glTF importer mirrors X), so y and z are negated here and both editions share the same pose numbers.
    /// </summary>
    public class BonePoser
    {
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _rest = new Dictionary<string, Quaternion>();
        private readonly Dictionary<string, Vector3> _restPos = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Quaternion> _parentRot = new Dictionary<string, Quaternion>();
        private readonly Dictionary<string, Quaternion> _parentInv = new Dictionary<string, Quaternion>();

        public BonePoser(Transform root, IDictionary<string, Transform> bones, IEnumerable<string> names)
        {
            Quaternion rootInv = Quaternion.Inverse(root.rotation); // measure relative to the model root
            foreach (var name in names)
            {
                if (!bones.TryGetValue(name, out var b) || b == null || b.parent == null) continue;
                _bones[name] = b;
                _rest[name] = b.localRotation;
                _restPos[name] = b.localPosition;
                Quaternion parent = rootInv * b.parent.rotation;
                _parentRot[name] = parent;
                _parentInv[name] = Quaternion.Inverse(parent);
            }
        }

        public bool Has(string name) => _bones.ContainsKey(name);
        public Transform Get(string name) => _bones.TryGetValue(name, out var b) ? b : null;

        /// <summary>Model-space rotation in degrees over the rest pose (web convention, see class summary).</summary>
        public void Set(string name, float x, float y = 0f, float z = 0f)
        {
            if (!_bones.TryGetValue(name, out var b)) return;
            Quaternion q = Quaternion.Euler(x, -y, -z); // Unity's Euler order (Z, X, then Y) matches three.js 'YXZ'
            b.localRotation = _parentInv[name] * q * _parentRot[name] * _rest[name];
        }

        /// <summary>Model-space offset from the rest position in metres (web convention: x is mirrored).</summary>
        public void Offset(string name, float x, float y, float z)
        {
            if (!_bones.TryGetValue(name, out var b)) return;
            b.localPosition = _restPos[name] + _parentInv[name] * new Vector3(-x, y, z);
        }

        /// <summary>Uniform scale of a bone (eyes closing, the tail-flower folding).</summary>
        public void Scale(string name, float s)
        {
            if (_bones.TryGetValue(name, out var b)) b.localScale = Vector3.one * s;
        }

        /// <summary>Web-convention rotation as a Unity quaternion (for whole-model roots).</summary>
        public static Quaternion Euler(float x, float y, float z) => Quaternion.Euler(x, -y, -z);
    }
}
