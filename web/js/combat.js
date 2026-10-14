// Health, hit detection, projectiles and battle tracking (port of Runtime/Combat).
import * as THREE from 'three';
import { DamageRules, Element } from './rules.js';
import { Events } from './events.js';

export const Faction = { Player: 'Player', Companion: 'Companion', Wild: 'Wild' };
export const allies = (a, b) => a === b || ((a === 'Player' || a === 'Companion') && (b === 'Player' || b === 'Companion'));

export class Health {
  constructor(owner, faction, max, defense, element = Element.Neutral) {
    this.owner = owner; // entity with .position, .radius
    this.faction = faction;
    this.max = Math.max(1, max);
    this.current = this.max;
    this.defense = Math.max(1, defense);
    this.element = element;
    this.invulnerableUntil = 0;
    this.canBeDamaged = true;
    this.lastAttacker = null;
    this.onDamaged = [];
    this.onDied = [];
  }
  get fraction() { return this.max > 0 ? Math.min(1, Math.max(0, this.current / this.max)) : 0; }
  get isDead() { return this.current <= 0; }
  canReceiveFrom(faction, now) {
    return this.canBeDamaged && !this.isDead && now >= this.invulnerableUntil && !allies(faction, this.faction);
  }
  takeHit(power, attack, element, faction, source, point, dir, knockback, now) {
    if (!this.canReceiveFrom(faction, now)) return 0;
    const amount = DamageRules.calculate(power, attack, this.defense, element, this.element, Math.random());
    const mult = DamageRules.elementMultiplier(element, this.element);
    const dealt = Math.min(this.current, amount);
    this.current -= dealt;
    this.lastAttacker = source;
    const d = dir.clone(); d.y = 0;
    if (d.lengthSq() < 1e-4) d.set(0, 0, 1);
    d.normalize();
    const info = { amount: dealt, point: point.clone(), direction: d, knockback, faction, source, element, effective: mult > 1.01, resisted: mult < 0.99 };
    for (const f of this.onDamaged) f(info);
    Events.emit('damaged', this, info);
    if (this.current <= 0) { this.current = 0; for (const f of this.onDied) f(); }
    return dealt;
  }
  heal(amount) { if (amount > 0 && !this.isDead) this.current = Math.min(this.max, this.current + amount); }
  setFraction(f) { this.current = Math.min(1, Math.max(0, f)) * this.max; }
  setInvulnerable(seconds, now) { this.invulnerableUntil = Math.max(this.invulnerableUntil, now + seconds); }
}

/** Every damageable entity registers here so attacks can find targets without a physics engine. */
export const Targets = {
  list: new Set(),
  findHostiles(center, radius, faction, self, forward = null, maxAngleDeg = 180) {
    const out = [];
    const f = forward ? new THREE.Vector3(forward.x, 0, forward.z).normalize() : null;
    for (const e of this.list) {
      const h = e.health;
      if (!h || h.isDead || e === self || allies(faction, h.faction)) continue;
      const c = e.center;
      const dx = c.x - center.x, dy = c.y - center.y, dz = c.z - center.z;
      const d = Math.hypot(dx, dy, dz);
      if (d > radius + (e.radius || 0.4)) continue;
      if (f && maxAngleDeg < 180 && Math.hypot(dx, dz) > 0.3) {
        const ang = Math.acos(Math.max(-1, Math.min(1, (dx * f.x + dz * f.z) / Math.hypot(dx, dz)))) * 180 / Math.PI;
        if (ang > maxAngleDeg) continue;
      }
      out.push(e);
    }
    return out;
  },
};

const projGeo = new THREE.SphereGeometry(0.5, 12, 8);

export class Projectiles {
  constructor(game) { this.game = game; this.items = []; }

  spawn({ position, velocity, faction, owner, power, attack, element, radius, knockback, color, emissive, scale, gravity = 0, life = 3, homing = null, homingStrength = 0, onImpact = null }) {
    const mat = new THREE.MeshStandardMaterial({ color, emissive: emissive || 0x000000, emissiveIntensity: emissive ? 2 : 0, roughness: 0.4 });
    const mesh = new THREE.Mesh(projGeo, mat);
    mesh.scale.setScalar(scale);
    mesh.position.copy(position);
    mesh.castShadow = true;
    this.game.scene.add(mesh);
    // No per-projectile lights: changing the light count forces shader recompiles (hitches).
    this.items.push({ mesh, velocity: velocity.clone(), faction, owner, power, attack, element, radius, knockback, gravity, dieAt: this.game.time + life, homing, homingStrength, onImpact });
  }

  update(dt) {
    const world = this.game.world, now = this.game.time;
    for (let i = this.items.length - 1; i >= 0; i--) {
      const p = this.items[i];
      let done = now > p.dieAt;
      if (!done) {
        if (p.homing && p.homingStrength > 0 && !p.homing.health.isDead) {
          const to = p.homing.center.clone().sub(p.mesh.position).normalize();
          const speed = p.velocity.length();
          const cur = p.velocity.clone().normalize();
          cur.lerp(to, Math.min(1, p.homingStrength * dt)).normalize();
          p.velocity.copy(cur.multiplyScalar(speed));
        }
        p.velocity.y += p.gravity * dt;
        p.mesh.position.addScaledVector(p.velocity, dt);
        p.mesh.rotation.x += dt * 9;
        const pos = p.mesh.position;
        if (pos.y < world.heightAt(pos.x, pos.z) || world.segmentBlocked(pos, pos.clone().addScaledVector(p.velocity, 0.02))) done = true;
        const hits = Targets.findHostiles(pos, p.radius, p.faction, p.owner);
        if (hits.length) {
          for (const e of hits) e.health.takeHit(p.power, p.attack, p.element, p.faction, p.owner, pos, p.velocity, p.knockback, now);
          done = true;
        }
      }
      if (done) {
        if (p.onImpact) p.onImpact(p.mesh.position.clone());
        this.game.scene.remove(p.mesh);
        p.mesh.material.dispose();
        this.items.splice(i, 1);
      }
    }
  }

  clear() { for (const p of this.items) { this.game.scene.remove(p.mesh); p.mesh.material.dispose(); } this.items.length = 0; }
}

/** Tracks which wild creatures are fighting the player. Battles are seamless (no separate screen). */
export class Battle {
  constructor() { this.engaged = []; this.bound = new Set(); this.primary = null; }
  get inBattle() { return this.engaged.length > 0; }
  engage(c) {
    if (this.engaged.includes(c)) return;
    const first = this.engaged.length === 0;
    this.engaged.push(c);
    this.primary = c;
    if (first) Events.emit('battleStarted', c);
  }
  markBound(c) { if (this.engaged.includes(c)) this.bound.add(c); }
  disengage(c) {
    const i = this.engaged.indexOf(c);
    if (i < 0) return;
    let outcome;
    if (this.bound.delete(c)) outcome = 'Bound';
    else if (c.brain.state === 'Binding') return;
    else if (c.brain.state === 'Exhausted') outcome = 'Victory';
    else outcome = 'Escaped';
    this.engaged.splice(i, 1);
    this.primary = this.engaged.length ? this.engaged[this.engaged.length - 1] : null;
    Events.emit('battleEnded', outcome, c);
  }
  reset() { this.engaged.length = 0; this.bound.clear(); this.primary = null; }
}
