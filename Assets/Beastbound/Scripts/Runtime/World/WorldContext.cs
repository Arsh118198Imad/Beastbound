using System.Collections.Generic;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.World
{
    /// <summary>A place where a wild creature lives. The id is stable so captures persist across saves.</summary>
    public class NestPoint
    {
        public string Id;
        public string SpeciesId;
        public Vector3 Position;
        public Personality Personality;
        public int Level;
        public float Vigor;
        public float HomeRadius = 11f;
    }

    /// <summary>
    /// Runtime facts about the generated world that gameplay needs: ground height, water, walkability,
    /// spawn points and landmarks.
    /// </summary>
    public class WorldContext : MonoBehaviour
    {
        public TerrainField Terrain { get; private set; }
        public float WaterLevel => ValeLayout.WaterLevel;
        public float PlayableRadius => ValeLayout.PlayableRadius;
        public Vector3 PlayerSpawn { get; private set; }
        public Quaternion PlayerSpawnRotation { get; private set; }
        public Vector3 WaystonePosition { get; private set; }
        public List<NestPoint> Nests { get; } = new List<NestPoint>();
        public DayNightCycle DayNight { get; set; }
        public VegetationCuller Vegetation { get; set; }

        public void Initialize(TerrainField terrain)
        {
            Terrain = terrain;
            WaystonePosition = GroundPoint(new Vector3(ValeLayout.Waystone.x, 0f, ValeLayout.Waystone.y));
            PlayerSpawn = GroundPoint(new Vector3(ValeLayout.PlayerSpawn.x, 0f, ValeLayout.PlayerSpawn.y)) + Vector3.up * 0.1f;
            // Face the open meadow and the lake.
            Vector3 look = new Vector3(ValeLayout.LakeCenter.x, 0f, ValeLayout.LakeCenter.y) - PlayerSpawn;
            look.y = 0f;
            PlayerSpawnRotation = Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward);
        }

        public float GroundHeight(float x, float z) => Terrain != null ? Terrain.HeightAt(x, z) : 0f;

        public Vector3 GroundPoint(Vector3 p)
        {
            p.y = GroundHeight(p.x, p.z);
            return p;
        }

        public bool IsUnderwater(Vector3 p) => GroundHeight(p.x, p.z) < WaterLevel - 0.05f;

        /// <summary>Depth of water above the ground at this XZ (0 when dry).</summary>
        public float WaterDepth(Vector3 p) => Mathf.Max(0f, WaterLevel - GroundHeight(p.x, p.z));

        public bool IsInsidePlayArea(Vector3 p, float margin = 0f)
        {
            return new Vector2(p.x, p.z).magnitude <= PlayableRadius - margin;
        }

        /// <summary>Dry, not too steep and inside the play area. Used by creature AI when picking destinations.</summary>
        public bool IsWalkable(Vector3 p, float maxSlope = 32f)
        {
            if (Terrain == null) return true;
            if (!IsInsidePlayArea(p, 3f)) return false;
            if (GroundHeight(p.x, p.z) < WaterLevel + 0.15f) return false;
            return Terrain.SlopeAt(p.x, p.z) <= maxSlope;
        }

        /// <summary>Finds a walkable point near <paramref name="desired"/>; returns false if none found.</summary>
        public bool TryFindWalkableNear(Vector3 desired, float searchRadius, out Vector3 result, int attempts = 12)
        {
            if (IsWalkable(desired))
            {
                result = GroundPoint(desired);
                return true;
            }
            for (int i = 0; i < attempts; i++)
            {
                Vector2 r = Random.insideUnitCircle * searchRadius;
                var candidate = desired + new Vector3(r.x, 0f, r.y);
                if (IsWalkable(candidate))
                {
                    result = GroundPoint(candidate);
                    return true;
                }
            }
            result = GroundPoint(desired);
            return false;
        }

        /// <summary>Pulls a point back inside the play area (soft world boundary).</summary>
        public Vector3 ClampToPlayArea(Vector3 p)
        {
            var flat = new Vector2(p.x, p.z);
            if (flat.magnitude <= PlayableRadius) return p;
            flat = flat.normalized * PlayableRadius;
            return new Vector3(flat.x, p.y, flat.y);
        }
    }
}
