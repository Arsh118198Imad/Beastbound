using System;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.Combat
{
    public enum Faction
    {
        Player,
        Companion,
        Wild
    }

    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Direction;
        public float Knockback;
        public Faction SourceFaction;
        public GameObject Source;
        public Element Element;
        public bool Effective;     // element advantage
        public bool Resisted;      // element disadvantage
    }

    /// <summary>
    /// Hit points + defensive stats for anything that can be hurt. Movement/visual scripts subscribe to
    /// <see cref="Damaged"/> to play knockback and hit reactions, keeping this class purely about numbers.
    /// </summary>
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour
    {
        [SerializeField] private Faction faction = Faction.Wild;
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float defense = 10f;
        [SerializeField] private Element element = Element.Neutral;

        private float _current;
        private float _invulnerableUntil;
        private bool _initialized;

        /// <summary>Raised after damage is applied: (info, damage actually dealt).</summary>
        public event Action<DamageInfo> Damaged;
        public event Action Died;
        public event Action Changed;

        /// <summary>Global hook for UI (floating damage numbers).</summary>
        public static event Action<Health, DamageInfo> AnyDamaged;

        public Faction Faction => faction;
        public float Max => maxHealth;
        public float Current => _initialized ? _current : maxHealth;
        public float Fraction => maxHealth > 0f ? Mathf.Clamp01(Current / maxHealth) : 0f;
        public float Defense => defense;
        public Element Element => element;
        public bool IsDead => _initialized && _current <= 0f;
        public bool IsInvulnerable => Time.time < _invulnerableUntil;

        /// <summary>When false all damage is ignored (e.g. during a bind attempt or menus).</summary>
        public bool CanBeDamaged { get; set; } = true;

        /// <summary>Last attacker, so creatures can retaliate against whoever hit them.</summary>
        public GameObject LastAttacker { get; private set; }

        private void Awake()
        {
            if (!_initialized) Configure(faction, maxHealth, defense, element);
        }

        public void Configure(Faction newFaction, float max, float newDefense, Element newElement)
        {
            faction = newFaction;
            maxHealth = Mathf.Max(1f, max);
            defense = Mathf.Max(1f, newDefense);
            element = newElement;
            _current = maxHealth;
            _initialized = true;
            Changed?.Invoke();
        }

        public static bool AreAllies(Faction a, Faction b)
        {
            if (a == b) return true;
            bool aFriendly = a == Faction.Player || a == Faction.Companion;
            bool bFriendly = b == Faction.Player || b == Faction.Companion;
            return aFriendly && bFriendly;
        }

        /// <summary>
        /// Computes damage with <see cref="DamageRules"/> and applies it. Returns the damage dealt (0 if ignored).
        /// </summary>
        public float TakeHit(float power, float attackerAttack, Element attackElement, Faction sourceFaction,
            GameObject source, Vector3 hitPoint, Vector3 direction, float knockback)
        {
            if (!CanReceiveDamageFrom(sourceFaction)) return 0f;
            float amount = DamageRules.Calculate(power, attackerAttack, defense, attackElement, element,
                UnityEngine.Random.value);
            float mult = DamageRules.ElementMultiplier(attackElement, element);
            var info = new DamageInfo
            {
                Amount = amount,
                Point = hitPoint,
                Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward * -1f,
                Knockback = knockback,
                SourceFaction = sourceFaction,
                Source = source,
                Element = attackElement,
                Effective = mult > 1.01f,
                Resisted = mult < 0.99f
            };
            return ApplyDamage(info);
        }

        public bool CanReceiveDamageFrom(Faction sourceFaction)
        {
            return CanBeDamaged && !IsDead && !IsInvulnerable && !AreAllies(sourceFaction, faction);
        }

        public float ApplyDamage(DamageInfo info)
        {
            if (!CanReceiveDamageFrom(info.SourceFaction) || info.Amount <= 0f) return 0f;
            if (!_initialized) Configure(faction, maxHealth, defense, element);

            float dealt = Mathf.Min(_current, info.Amount);
            _current -= dealt;
            info.Amount = dealt;
            LastAttacker = info.Source;

            Damaged?.Invoke(info);
            AnyDamaged?.Invoke(this, info);
            Changed?.Invoke();
            if (_current <= 0f)
            {
                _current = 0f;
                Died?.Invoke();
            }
            return dealt;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead) return;
            if (!_initialized) Configure(faction, maxHealth, defense, element);
            float before = _current;
            _current = Mathf.Min(maxHealth, _current + amount);
            if (!Mathf.Approximately(before, _current)) Changed?.Invoke();
        }

        /// <summary>Sets health to a fraction of max, reviving if needed.</summary>
        public void SetFraction(float fraction)
        {
            if (!_initialized) Configure(faction, maxHealth, defense, element);
            _current = Mathf.Clamp01(fraction) * maxHealth;
            Changed?.Invoke();
        }

        public void RestoreFull() => SetFraction(1f);

        public void SetInvulnerable(float seconds)
        {
            _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + Mathf.Max(0f, seconds));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AnyDamaged = null;
    }
}
