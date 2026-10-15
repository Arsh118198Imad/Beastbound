// Pure game rules (direct port of Assets/Beastbound/Scripts/Runtime/Rules). No rendering here.

export const Awareness = { Asleep: 'Asleep', Unaware: 'Unaware', Alert: 'Alert', Hostile: 'Hostile', Exhausted: 'Exhausted' };
export const Element = { Neutral: 'Neutral', Verdant: 'Verdant', Ember: 'Ember', Tide: 'Tide', Gale: 'Gale', Stone: 'Stone' };
export const Personality = { Bold: 'Bold', Curious: 'Curious', Timid: 'Timid', Sleepy: 'Sleepy', Playful: 'Playful', Stubborn: 'Stubborn' };

export const CaptureRules = {
  MinChance: 0.05,
  MaxChance: 1,
  ResonancePerFailure: 0.12,
  awarenessMultiplier(a) {
    switch (a) {
      case Awareness.Asleep: return 2.0;
      case Awareness.Unaware: return 1.5;
      case Awareness.Alert: return 1.0;
      case Awareness.Hostile: return 0.85;
      case Awareness.Exhausted: return 100;
      default: return 1;
    }
  },
  healthMultiplier(cur, max) {
    if (max <= 0) return 1;
    const f = Math.min(1, Math.max(0, cur / max));
    return 1 + 2 * (1 - f);
  },
  /** What you see is what is rolled: this exact number is shown in the HUD. */
  chance(baseRate, cur, max, awareness, failed) {
    if (awareness === Awareness.Exhausted || (max > 0 && cur <= 0)) return 1;
    let c = Math.max(0, baseRate) * this.healthMultiplier(cur, max) * this.awarenessMultiplier(awareness);
    c += Math.max(0, failed) * this.ResonancePerFailure;
    return Math.min(this.MaxChance, Math.max(this.MinChance, c));
  },
  resolve(chance, roll) { return roll < chance; },
};

const beats = {
  Verdant: ['Tide', 'Stone'], Ember: ['Verdant', 'Gale'], Tide: ['Ember', 'Stone'], Gale: ['Verdant'], Stone: ['Ember', 'Gale'],
};

export const DamageRules = {
  statScale(atk, def) { atk = Math.max(1, atk); def = Math.max(1, def); return 0.5 + atk / (atk + def); },
  elementMultiplier(a, d) {
    if (a === Element.Neutral || d === Element.Neutral) return 1;
    if ((beats[a] || []).includes(d)) return 1.25;
    if ((beats[d] || []).includes(a)) return 0.8;
    return 1;
  },
  calculate(power, atk, def, ae, de, roll) {
    if (power <= 0) return 0;
    const spread = 0.92 + Math.min(1, Math.max(0, roll)) * 0.16;
    return Math.max(1, Math.round(power * this.statScale(atk, def) * this.elementMultiplier(ae, de) * spread));
  },
};

const personalityMods = {
  Bold: [1, 1.15, 1, 1], Curious: [1, 1, 1, 1.1], Timid: [1, 0.9, 1, 1.2],
  Sleepy: [1.15, 1, 1, 0.85], Playful: [1, 1, 1, 1], Stubborn: [1.05, 1, 1.2, 0.9],
};

export const StatRules = {
  MinLevel: 1, MaxLevel: 50, MinVigor: 0.9, MaxVigor: 1.1,
  clampVigor(v) { return Number.isFinite(v) ? Math.min(1.1, Math.max(0.9, v)) : 1; },
  vigorStars(v) { v = this.clampVigor(v); return v >= 1.05 ? 3 : v >= 0.97 ? 2 : 1; },
  compute(base, level, personality, vigor) {
    const lv = 1 + 0.08 * (Math.min(50, Math.max(1, level)) - 1);
    const vg = this.clampVigor(vigor);
    const m = personalityMods[personality] || [1, 1, 1, 1];
    return {
      maxHealth: Math.round(base.maxHealth * lv * vg * m[0]),
      attack: Math.round(base.attack * lv * vg * m[1]),
      defense: Math.round(base.defense * lv * vg * m[2]),
      speed: Math.round(base.speed * lv * vg * m[3]),
    };
  },
  describe(p) {
    return {
      Bold: 'Bold — stands its ground and challenges intruders.',
      Curious: 'Curious — wanders closer to see what you are.',
      Timid: 'Timid — bolts at the first sign of danger.',
      Sleepy: 'Sleepy — dozes often and notices you late.',
      Playful: 'Playful — loves to circle and chase.',
      Stubborn: 'Stubborn — shrugs off hits others would not.',
    }[p] || p;
  },
};

// ------------------------------------------------------------------ Content

export const Attacks = {
  pebbleToss: { id: 'pebble_toss', name: 'Pebble Toss', kind: 'projectile', element: Element.Stone, power: 9, range: 13, cooldown: 2.6, windup: 0.55, speed: 15, knockback: 2.5, radius: 0.55 },
  brambleRush: { id: 'bramble_rush', name: 'Bramble Rush', kind: 'charge', element: Element.Verdant, power: 14, range: 7, cooldown: 4.2, windup: 0.7, speed: 11, knockback: 6, radius: 1.1 },
  petalSwipe: { id: 'petal_swipe', name: 'Petal Swipe', kind: 'melee', element: Element.Verdant, power: 8, range: 2.2, cooldown: 1.6, windup: 0.35, speed: 0, knockback: 3, radius: 1.3 },
  thornfangLunge: { id: 'thornfang_lunge', name: 'Thornfang Lunge', kind: 'charge', element: Element.Verdant, power: 17, range: 9, cooldown: 4.6, windup: 0.85, speed: 12.5, knockback: 7, radius: 1.4 },
  veinlightBolt: { id: 'veinlight_bolt', name: 'Veinlight Bolt', kind: 'projectile', element: Element.Verdant, power: 11, range: 15, cooldown: 2.9, windup: 0.6, speed: 17, knockback: 3, radius: 0.6, color: [0.35, 0.78, 1], emissive: [0.3, 0.75, 1], scale: 0.3 },
  guardianSwipe: { id: 'guardian_swipe', name: 'Guardian Swipe', kind: 'melee', element: Element.Verdant, power: 12, range: 2.9, cooldown: 1.9, windup: 0.45, speed: 0, knockback: 4.5, radius: 1.7 },
};

export const Species = {
  pebbloom: {
    id: 'pebbloom',
    name: 'Pebbloom',
    element: Element.Verdant,
    description: 'A round, pebble-backed creature that carries a living flower on its tail. Moss grows on the stone plates of its back, and an amber crystal on its brow glows brighter when it is happy.',
    habitat: 'Meadows, lakeshores and old ruins of Sunmeadow Vale.',
    lore: "Travellers say a Pebbloom's tail-flower closes at dusk and opens again only for someone it trusts.",
    base: { maxHealth: 58, attack: 11, defense: 12, speed: 10 },
    captureRate: 0.32,
    attacks: [Attacks.pebbleToss, Attacks.brambleRush, Attacks.petalSwipe],
    walkSpeed: 1.9, runSpeed: 5.6,
    sightRadius: 16, fov: 150, hearingRadius: 8, closeRadius: 2.4,
  },
  thalorix: {
    id: 'thalorix',
    name: 'Thalorix',
    element: Element.Verdant,
    description: 'A maned forest guardian, part wolf and part lion. Blue light runs through the veins of its legs and face, and its swept-back horns end in living crystal that brightens when it is about to strike.',
    habitat: 'Deep, quiet woodland at the western edge of Sunmeadow Vale.',
    lore: 'Old wardens say a Thalorix chooses whom it follows. It will not be rushed — only patience, or a fair fight, earns its trust.',
    base: { maxHealth: 92, attack: 15, defense: 15, speed: 12 },
    captureRate: 0.16,
    attacks: [Attacks.thornfangLunge, Attacks.veinlightBolt, Attacks.guardianSwipe],
    walkSpeed: 2.3, runSpeed: 7.2,
    sightRadius: 20, fov: 160, hearingRadius: 10, closeRadius: 3.2,
    radius: 0.85, centerHeight: 1.15, markerHeight: 2.85,
  },
};

let uidCounter = 0;
export function newUid() {
  uidCounter++;
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 8) + uidCounter;
}

export function createRecord(speciesId, level, personality, vigor, nestId) {
  return {
    uid: newUid(), speciesId, nickname: '',
    level: Math.min(50, Math.max(1, level)), personality,
    vigor: StatRules.clampVigor(vigor), originNestId: nestId, boundAt: '',
  };
}
