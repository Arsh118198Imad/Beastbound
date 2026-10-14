// Creatures: Pebbloom model + procedural animation, motor, perception and AI brain.
// Port of Runtime/Creatures and Runtime/CreatureAI (same states, numbers and personalities).
import * as THREE from 'three';
import { clamp, clamp01, lerp, damp, srgb } from './util.js';
import { CaptureRules, Awareness, Personality, StatRules, Species } from './rules.js';
import { Health, Faction, Targets } from './combat.js';
import { Events } from './events.js';

const SPHERE = new THREE.SphereGeometry(0.5, 18, 12);
const CAPSULE = new THREE.CapsuleGeometry(0.5, 1, 6, 12);
const CRYSTAL = new THREE.OctahedronGeometry(0.5, 0).scale(1, 1.6, 1);
const toRad = Math.PI / 180;
const euler = (x, y, z) => new THREE.Euler(x * toRad, y * toRad, z * toRad, 'YXZ');
const quat = (x, y, z) => new THREE.Quaternion().setFromEuler(euler(x, y, z));

const PALETTE = {
  body: [0.55, 0.6, 0.52], bodyDark: [0.38, 0.42, 0.36], belly: [0.93, 0.89, 0.76], moss: [0.36, 0.62, 0.3],
  petal: [1, 0.55, 0.62], petalCenter: [1, 0.85, 0.35], crystal: [1, 0.72, 0.25], eye: [0.08, 0.07, 0.1],
};

function part(parent, geo, mat, pos, scale, rot = null, shadow = true) {
  const m = new THREE.Mesh(geo, mat);
  m.position.set(...pos);
  m.scale.set(...scale);
  if (rot) m.quaternion.copy(rot);
  m.castShadow = shadow;
  parent.add(m);
  return m;
}
function pivot(parent, pos) { const g = new THREE.Group(); g.position.set(...pos); parent.add(g); return g; }

/** Builds Pebbloom from primitives. Same proportions as the Unity version. */
export function buildPebbloom(variant = 0) {
  const tint = ((variant % 8) / 8 - 0.5) * 0.08;
  const shade = (c) => { const col = srgb(...c); const hsl = {}; col.getHSL(hsl); col.setHSL((hsl.h + tint * 0.5 + 1) % 1, hsl.s, clamp01(hsl.l + tint * 0.5)); return col; };
  const M = (c, rough = 0.7, extra = {}) => new THREE.MeshStandardMaterial({ color: Array.isArray(c) ? srgb(...c) : c, roughness: rough, ...extra });
  const mats = {
    body: M(shade(PALETTE.body), 0.75), dark: M(shade(PALETTE.bodyDark), 0.8), belly: M(PALETTE.belly, 0.85), moss: M(PALETTE.moss, 0.95),
    petal: M(PALETTE.petal, 0.6), center: M(PALETTE.petalCenter, 0.6), eye: M(PALETTE.eye, 0.15),
    shine: M([1, 1, 1], 0.1, { emissive: 0xffffff, emissiveIntensity: 0.6 }),
    crystal: M(PALETTE.crystal, 0.2, { emissive: srgb(1, 0.6, 0.15), emissiveIntensity: 1.2 }),
    stem: M([0.3, 0.52, 0.25], 0.9),
  };
  const P = {};
  const root = new THREE.Group();
  P.root = root;
  P.body = part(root, SPHERE, mats.body, [0, 0.5, -0.05], [0.9, 0.72, 1]);
  part(root, SPHERE, mats.dark, [0, 0.73, -0.17], [0.74, 0.36, 0.78], quat(-12, 0, 0));
  part(root, SPHERE, mats.moss, [0, 0.82, -0.13], [0.56, 0.22, 0.6], quat(-8, 0, 0));
  part(root, SPHERE, mats.belly, [0, 0.41, 0.23], [0.65, 0.45, 0.55]);

  const head = pivot(root, [0, 0.78, 0.42]);
  P.head = head;
  part(head, SPHERE, mats.body, [0, 0, 0], [0.62, 0.55, 0.56]);
  part(head, SPHERE, mats.belly, [0, -0.08, 0.24], [0.32, 0.22, 0.22]);
  part(head, SPHERE, mats.eye, [0, -0.02, 0.345], [0.07, 0.05, 0.05], null, false);
  P.eyes = [-1, 1].map((side) => {
    const eye = part(head, SPHERE, mats.eye, [0.15 * side, 0.06, 0.24], [0.12, 0.15, 0.08], quat(0, 18 * side, 0), false);
    part(eye, SPHERE, mats.shine, [-0.15 * side, 0.25, 0.4], [0.35, 0.3, 0.35], null, false);
    return eye;
  });
  P.crystal = part(head, CRYSTAL, mats.crystal, [0, 0.25, 0.17], [0.12, 0.12, 0.12], quat(25, 0, 0), false);
  P.crystalMat = mats.crystal;
  part(head, SPHERE, mats.moss, [0.04, 0.3, -0.02], [0.06, 0.16, 0.1], quat(0, 0, -25));
  part(head, SPHERE, mats.moss, [-0.04, 0.29, -0.04], [0.06, 0.13, 0.09], quat(0, 0, 30));
  const ear = (side) => {
    const p = pivot(head, [0.17 * side, 0.2, -0.04]);
    p.quaternion.copy(quat(-10, 0, -22 * side));
    part(p, SPHERE, mats.petal, [0, 0.24, 0], [0.17, 0.5, 0.08]);
    part(p, SPHERE, mats.center, [0, 0.2, 0.025], [0.08, 0.32, 0.05], null, false);
    return p;
  };
  P.earL = ear(-1); P.earR = ear(1);

  P.legs = [[-0.27, 0.3, 0.25], [0.27, 0.3, 0.25], [-0.27, 0.3, -0.3], [0.27, 0.3, -0.3]].map((h) => {
    const hip = pivot(root, h);
    part(hip, CAPSULE, mats.dark, [0, -0.16, 0], [0.2, 0.16, 0.2]);
    part(hip, SPHERE, mats.belly, [0, -0.28, 0.04], [0.2, 0.09, 0.24], null, false);
    return hip;
  });

  const tail = pivot(root, [0, 0.55, -0.5]);
  P.tail = tail;
  part(tail, CAPSULE, mats.stem, [0, 0.09, -0.156], [0.08, 0.18, 0.08], quat(-60, 0, 0));
  part(tail, CAPSULE, mats.stem, [0, 0.293, -0.375], [0.07, 0.13, 0.07], quat(-30, 0, 0));
  const bloom = pivot(tail, [0, 0.45, -0.46]);
  bloom.quaternion.copy(quat(-30, 0, 0));
  P.bloom = bloom;
  part(bloom, SPHERE, mats.center, [0, 0, 0], [0.13, 0.13, 0.13]);
  for (let i = 0; i < 5; i++) {
    const q = quat(0, i * 72, 0);
    const pos = new THREE.Vector3(0, 0, 0.12).applyQuaternion(q);
    part(bloom, SPHERE, mats.petal, [pos.x, pos.y, pos.z], [0.12, 0.05, 0.2], q.clone().multiply(quat(-15, 0, 0)));
  }
  P.mats = mats;
  return P;
}

/** Procedural animation: hop, breathe, ears, tail, blink, sleep, hurt squash, lunge, binding shake. */
export class CreatureAnimator {
  constructor(parts, maxSpeed) {
    this.p = parts; this.maxSpeed = maxSpeed;
    this.speed = 0; this.smoothed = 0; this.phase = Math.random() * 10; this.pose = 'Normal';
    this.hurt = 0; this.lunge = 0; this.happy = 0; this.blend = 0; this.glow = 0;
    this.base = {
      head: parts.head.quaternion.clone(), earL: parts.earL.quaternion.clone(), earR: parts.earR.quaternion.clone(),
      tail: parts.tail.quaternion.clone(), eyes: parts.eyes.map((e) => e.scale.clone()),
    };
  }
  playHurt() { this.hurt = 0.25; }
  playLunge() { this.lunge = 0.3; }
  playHappy() { this.happy = 0.8; }
  addGlow(a) { this.glow = Math.max(this.glow, a); }

  update(dt, t) {
    const p = this.p, b = this.base;
    this.smoothed = lerp(this.smoothed, this.speed, damp(10, dt));
    const move = clamp01(this.smoothed / Math.max(0.1, this.maxSpeed));
    if (this.smoothed > 0.05) this.phase += dt * lerp(7, 15, move);
    const sleeping = this.pose === 'Sleeping', exhausted = this.pose === 'Exhausted', low = sleeping || exhausted;
    this.blend += clamp((low ? 1 : 0) - this.blend, -dt * 2.5, dt * 2.5);
    const breathe = Math.sin(t * (sleeping ? 1.4 : 2.6)) * (sleeping ? 0.04 : 0.025);
    const hop = Math.abs(Math.sin(this.phase)) * 0.09 * move;
    let happyHop = 0;
    if (this.happy > 0) { this.happy -= dt; happyHop = Math.abs(Math.sin(this.happy * 12)) * 0.18; }
    let y = hop + happyHop - 0.14 * this.blend, z = 0;
    if (this.pose === 'Windup') { y -= 0.06; z -= 0.08; }
    if (this.lunge > 0) { this.lunge -= dt; z += Math.sin(this.lunge / 0.3 * Math.PI) * 0.35; }
    p.root.position.set(0, y, z);
    let sx = 1 - breathe * 0.5, sy = 1 + breathe, sz = sx;
    if (this.hurt > 0) { this.hurt -= dt; const k = this.hurt / 0.25; sx *= 1 + 0.18 * k; sy *= 1 - 0.22 * k; sz *= 1 + 0.18 * k; }
    if (this.pose === 'Windup') { sx *= 1.08; sy *= 0.9; sz *= 1.08; }
    p.root.scale.set(sx, sy, sz);
    if (this.pose === 'Binding') p.root.quaternion.copy(quat(0, Math.sin(t * 38) * 12, Math.sin(t * 31) * 5));
    else if (exhausted) p.root.quaternion.copy(quat(0, 0, 18 * this.blend));
    else p.root.quaternion.identity();

    const nod = Math.sin(this.phase * 2) * 4 * move;
    const look = Math.sin(t * 0.7) * 10 * (1 - move) * (1 - this.blend);
    p.head.quaternion.copy(b.head).multiply(quat(nod + 20 * this.blend, look, 0));
    const perk = this.pose === 'Alert' || this.pose === 'Windup' ? -14 : 0;
    let droop = 55 * this.blend;
    const flap = Math.sin(this.phase * 2 + 0.5) * 10 * move + Math.sin(t * 1.7) * 3;
    if (this.pose === 'Windup') droop += 40;
    p.earL.quaternion.copy(b.earL).multiply(quat(droop * 0.6, 0, -(flap + perk) + droop * 0.5));
    p.earR.quaternion.copy(b.earR).multiply(quat(droop * 0.6, 0, flap + perk - droop * 0.5));
    const wagSpeed = this.happy > 0 ? 18 : lerp(3, 12, move);
    p.tail.quaternion.copy(b.tail).multiply(quat(0, Math.sin(t * wagSpeed) * lerp(12, 22, move) * (1 - this.blend * 0.8), 0));
    const open = lerp(1, 0.55, this.blend);
    p.bloom.scale.set(open, 1, open);
    p.legs.forEach((leg, i) => {
      const off = i === 0 || i === 3 ? 0 : Math.PI;
      leg.quaternion.copy(quat(Math.sin(this.phase + off) * 35 * move + 70 * this.blend * (i < 2 ? -1 : 1), 0, 0));
    });
    const blink = ((t + this.phase * 0.01) % 3.7) < 0.12 ? 0.15 : 1;
    const openness = sleeping ? 0.12 : exhausted ? 0.45 : blink;
    p.eyes.forEach((e, i) => e.scale.set(b.eyes[i].x, b.eyes[i].y * openness, b.eyes[i].z));
    this.glow = Math.max(0, this.glow - dt * 1.5);
    let pulse = 0.9 + Math.sin(t * 2) * 0.2 + this.glow * 2.5;
    if (sleeping) pulse *= 0.4;
    p.crystalMat.emissiveIntensity = pulse;
  }
}

// ====================================================================== Creature entity

export class Creature {
  constructor(game, record, role, nest, position, yaw) {
    this.game = game;
    this.species = Species[record.speciesId] || Species.pebbloom;
    this.record = record;
    this.role = role;              // 'Wild' | 'Companion'
    this.nest = nest;
    this.stats = StatRules.compute(this.species.base, record.level, record.personality, record.vigor);
    this.health = new Health(this, role === 'Wild' ? Faction.Wild : Faction.Companion, this.stats.maxHealth, this.stats.defense, this.species.element);
    this.failedBinds = 0;
    this.radius = 0.42;
    this.position = position.clone();
    this.yaw = yaw;
    this.group = new THREE.Group();
    this.parts = buildPebbloom(hashCode(record.uid) & 7);
    this.group.add(this.parts.root);
    this.group.position.copy(position);
    game.scene.add(this.group);
    this.anim = new CreatureAnimator(this.parts, this.species.runSpeed);
    // Motor
    this.velocity = new THREE.Vector3();
    this.desired = new THREE.Vector3();
    this.knockback = new THREE.Vector3();
    this.faceTarget = null;
    this.frozen = false;
    this.stuck = false; this.stuckTimer = 0; this.speed = 0;
    // Senses
    this.suspicion = 0; this.lastDetected = -100; this.nextTick = 0; this.lastTick = -1; this.playerVisible = false;
    this.brain = new Brain(this);
    Targets.list.add(this);
    this.health.onDamaged.push((info) => this.brain.onDamaged(info));
    this.health.onDied.push(() => this.brain.onExhausted());
  }

  get isWild() { return this.role === 'Wild'; }
  get name() { return this.record.nickname || this.species.name; }
  get center() { return this.position.clone().setY(this.position.y + 0.55); }
  get markerPoint() { return this.position.clone().setY(this.position.y + 1.75); }
  get home() { return this.nest ? this.nest.position : this.position; }
  get homeRadius() { return this.nest ? this.nest.homeRadius : 10; }
  get forward() { return new THREE.Vector3(Math.sin(this.yaw), 0, Math.cos(this.yaw)); }

  get awareness() {
    switch (this.brain.state) {
      case 'Exhausted': return Awareness.Exhausted;
      case 'Sleep': return Awareness.Asleep;
      case 'Combat': return Awareness.Hostile;
      case 'Curious': case 'Flee': return Awareness.Alert;
      default: return this.suspicion >= 0.5 ? Awareness.Alert : Awareness.Unaware;
    }
  }
  get captureChance() {
    return CaptureRules.chance(this.species.captureRate, this.health.current, this.health.max, this.awareness, this.failedBinds);
  }
  get canBeBound() { return this.isWild && this.brain.state !== 'Binding' && !this.removed; }

  // ------------------------------------------------------------------ motor
  moveTowards(target, speed, stop = 0.4) {
    const dx = target.x - this.position.x, dz = target.z - this.position.z, d = Math.hypot(dx, dz);
    if (d <= stop) { this.desired.set(0, 0, 0); return true; }
    const arrive = clamp01((d - stop) / 1.5);
    this.desired.set(dx / d, 0, dz / d).multiplyScalar(speed * Math.max(0.35, arrive));
    return false;
  }
  moveDir(dir, speed) {
    const l = Math.hypot(dir.x, dir.z);
    this.desired.set(l > 1e-4 ? dir.x / l * speed : 0, 0, l > 1e-4 ? dir.z / l * speed : 0);
  }
  stop() { this.desired.set(0, 0, 0); }
  face(p) { this.faceTarget = p.clone(); }
  teleport(p) { this.position.copy(p); this.velocity.set(0, 0, 0); this.knockback.set(0, 0, 0); }

  updateMotor(dt) {
    const world = this.game.world;
    const desired = this.frozen ? new THREE.Vector3() : this.desired.clone();
    if (desired.lengthSq() > 0.01) {
      const ahead = this.position.clone().addScaledVector(desired.clone().normalize(), 1.2);
      const wet = world.waterDepth(this.position.x, this.position.z) > 0.15;
      if ((!wet && world.waterDepth(ahead.x, ahead.z) > 0.15) || !world.insidePlayArea(ahead.x, ahead.z, 2)) { desired.set(0, 0, 0); this.stuck = true; }
    }
    const step = 16 * dt;
    const diff = desired.clone().sub(this.velocity);
    if (diff.length() <= step) this.velocity.copy(desired); else this.velocity.addScaledVector(diff.normalize(), step);
    const kl = this.knockback.length();
    if (kl > 0) this.knockback.multiplyScalar(Math.max(0, kl - 18 * dt) / kl);
    const before = this.position.clone();
    this.position.addScaledVector(this.velocity, dt).addScaledVector(this.knockback, dt);
    world.resolve(this.position, this.radius);
    this.position.y = world.heightAt(this.position.x, this.position.z);
    // Facing
    let fx = 0, fz = 0;
    if (this.faceTarget) { fx = this.faceTarget.x - this.position.x; fz = this.faceTarget.z - this.position.z; }
    else if (this.velocity.lengthSq() > 0.05) { fx = this.velocity.x; fz = this.velocity.z; }
    if (!this.frozen && fx * fx + fz * fz > 1e-4) {
      const target = Math.atan2(fx, fz);
      let d = ((target - this.yaw + Math.PI * 3) % (Math.PI * 2)) - Math.PI;
      const max = 540 * toRad * dt;
      this.yaw += clamp(d, -max, max);
    }
    this.faceTarget = null;
    const moved = Math.hypot(this.position.x - before.x, this.position.z - before.z);
    this.speed = moved / dt;
    if (desired.lengthSq() > 0.25 && this.speed < desired.length() * 0.2) { this.stuckTimer += dt; if (this.stuckTimer > 1.2) this.stuck = true; }
    else { this.stuckTimer = 0; if (desired.lengthSq() > 0.25) this.stuck = false; }
    this.anim.speed = this.frozen ? 0 : this.speed;
  }
  clearStuck() { this.stuck = false; this.stuckTimer = 0; }

  // ------------------------------------------------------------------ perception (suspicion builds over time)
  updatePerception() {
    const now = this.game.time;
    if (now < this.nextTick) return;
    const dt = this.lastTick < 0 ? 0.1 : Math.min(0.5, now - this.lastTick);
    this.lastTick = now; this.nextTick = now + 0.1;
    if (!this.isWild) return;
    const g = this.game, player = g.player;
    if (!player || !player.alive || g.state === 'title') { this.playerVisible = false; this.suspicion = Math.max(0, this.suspicion - 0.3 * dt); return; }
    const sp = this.species;
    const eye = this.position.clone().setY(this.position.y + 0.8);
    const target = player.position.clone().setY(player.position.y + 1.2);
    const dist = eye.distanceTo(target);
    this.distanceToPlayer = dist;
    const sleeping = this.brain.state === 'Sleep';
    const night = g.world.isNight;
    const sleepy = this.record.personality === Personality.Sleepy;
    let rate = 0;
    this.playerVisible = false;
    const sight = sp.sightRadius * (night ? 0.65 : 1) * (player.sneaking ? 0.6 : 1);
    if (!sleeping && dist < sight) {
      const fx = target.x - eye.x, fz = target.z - eye.z;
      const ang = Math.acos(clamp((fx * Math.sin(this.yaw) + fz * Math.cos(this.yaw)) / Math.max(1e-4, Math.hypot(fx, fz)), -1, 1)) / toRad;
      if (ang <= sp.fov / 2 && !g.world.segmentBlocked(eye, target)) { this.playerVisible = true; rate += lerp(1.8, 0.35, dist / sight); }
    }
    const hearing = sp.hearingRadius * player.noise * (sleeping ? 0.55 : 1) * (sleepy ? 0.7 : 1);
    if (dist < hearing) rate += lerp(1.1, 0.25, dist / hearing) * (sleeping ? 0.6 : 1);
    if (dist < sp.closeRadius && !(sleeping && player.sneaking)) rate += 3;
    if (rate > 0) { this.suspicion = clamp01(this.suspicion + rate * dt); if (this.suspicion >= 0.999) this.lastDetected = now; }
    else if (this.suspicion < 0.999 || now - this.lastDetected > 5) this.suspicion = Math.max(0, this.suspicion - 0.22 * dt);
  }
  get aware() { return this.suspicion >= 0.999; }
  forceAware() { this.suspicion = 1; this.lastDetected = this.game.time; }
  calm(to = 0) { this.suspicion = clamp01(to); }

  update(dt) {
    this.updatePerception();
    this.brain.update(dt);
    this.updateMotor(dt);
    this.anim.update(dt, this.game.time);
    this.group.position.copy(this.position);
    this.group.rotation.y = this.yaw;
  }

  dispose() {
    this.removed = true;
    Targets.list.delete(this);
    this.brain.notifyBattle(false);
    this.game.scene.remove(this.group);
    this.group.traverse((o) => { if (o.material && !o.material._shared) o.material.dispose?.(); });
  }
}

function hashCode(s) { let h = 0; for (let i = 0; i < (s || '').length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return Math.abs(h); }

// ====================================================================== Brain

const EXHAUSTED_WILD = 25, EXHAUSTED_COMPANION = 8, LEASH_TARGET = 26, LEASH_HOME = 45;

export class Brain {
  constructor(c) {
    this.c = c; this.state = null; this.stateTime = 0; this.timer = 0; this.target = new THREE.Vector3();
    this.disengagedAt = -100; this.nextChirp = c.game.time + 4 + Math.random() * 8;
    this.combatTarget = null; this.pending = null; this.windupEnd = 0; this.recoverUntil = 0;
    this.charging = false; this.chargeEnd = 0; this.chargeDir = new THREE.Vector3(); this.chargeHits = new Set(); this.currentCharge = null;
    this.cooldowns = new Map(); this.strafeSign = Math.random() < 0.5 ? -1 : 1; this.nextFlip = 0; this.before = null;
    if (c.role === 'Companion') this.set('Follow');
    else if (c.record.personality === Personality.Sleepy) this.enterSleep(30 + Math.random() * 30);
    else this.enterIdle();
  }
  get g() { return this.c.game; }
  get now() { return this.c.game.time; }
  sfx(name, vol = 1, pitch = 1) { this.g.sfx(name, this.c.center, vol, pitch); }

  set(next) {
    if (this.state === next && this.stateTime > 0) return;
    const prev = this.state;
    this.state = next; this.stateTime = 0; this.pending = null; this.charging = false;
    this.c.frozen = next === 'Binding' || next === 'Exhausted';
    this.c.stop(); this.c.clearStuck();
    if (prev === 'Combat' && next !== 'Combat') this.notifyBattle(false);
    if (next === 'Combat' && this.c.isWild) this.notifyBattle(true);
  }
  notifyBattle(engaged) {
    if (!this.c.isWild || !this.g.battle) return;
    if (engaged) this.g.battle.engage(this.c); else this.g.battle.disengage(this.c);
  }

  get indicator() {
    if (!this.c.isWild) return 'friendly';
    switch (this.state) {
      case 'Sleep': return 'sleep'; case 'Exhausted': return 'exhausted'; case 'Combat': return 'hostile';
      case 'Curious': case 'Flee': return 'noticed'; case 'Binding': return null;
      default: return this.c.suspicion > 0.15 ? 'suspicious' : null;
    }
  }

  update(dt) {
    this.stateTime += dt;
    switch (this.state) {
      case 'Idle': this.tickIdle(); break;
      case 'Wander': this.tickWander(); break;
      case 'Sleep': this.tickSleep(); break;
      case 'Curious': this.tickCurious(); break;
      case 'Flee': this.tickFlee(); break;
      case 'Combat': case 'Assist': this.tickCombat(); break;
      case 'Return': this.tickReturn(dt); break;
      case 'Exhausted': this.tickExhausted(); break;
      case 'Follow': this.tickFollow(); break;
      default: break;
    }
    const poses = { Sleep: 'Sleeping', Exhausted: 'Exhausted', Binding: 'Binding', Curious: 'Alert', Flee: 'Alert' };
    let pose = poses[this.state] || 'Normal';
    if (this.state === 'Combat' || this.state === 'Assist') pose = this.pending ? 'Windup' : 'Alert';
    this.c.anim.pose = pose;
    if (this.now > this.nextChirp) {
      this.nextChirp = this.now + 6 + Math.random() * 10;
      if (!['Sleep', 'Exhausted', 'Binding'].includes(this.state)) this.sfx('chirp', 0.6, 0.9 + Math.random() * 0.25);
    }
  }

  // ---------------------------------------------------------------- wild
  shouldReact() { return this.c.isWild && this.now - this.disengagedAt >= 5 && this.c.aware; }
  react() {
    const player = this.g.player;
    if (!player) return;
    this.sfx('alert', 0.8);
    this.c.anim.playHappy();
    Events.emit('spotted', this.c.species.id);
    const p = this.c.record.personality;
    if (p === Personality.Bold || p === Personality.Stubborn) this.enterCombat(player);
    else if (p === Personality.Timid) this.set('Flee');
    else this.set('Curious');
  }
  enterIdle() { this.set('Idle'); this.timer = 2 + Math.random() * 3; }
  enterSleep(d) { this.set('Sleep'); this.timer = d; this.c.calm(); }

  tickIdle() {
    this.c.stop();
    if (this.shouldReact()) return this.react();
    if (this.stateTime < this.timer) return;
    const night = this.g.world.isNight;
    let sleepChance = this.c.record.personality === Personality.Sleepy ? 0.55 : 0.08;
    if (night) sleepChance = 0.9;
    if (Math.random() < sleepChance) {
      if (night && this.c.position.distanceToSquared(this.c.home) > 9) return this.set('Return');
      return this.enterSleep(night ? 45 : 15 + Math.random() * 20);
    }
    const a = Math.random() * Math.PI * 2, r = Math.sqrt(Math.random()) * this.c.homeRadius;
    const p = this.g.world.findWalkableNear(this.c.home.x + Math.cos(a) * r, this.c.home.z + Math.sin(a) * r, 4, 6);
    if (!p) return this.enterIdle();
    this.target.copy(p);
    this.set('Wander');
  }
  tickWander() {
    if (this.shouldReact()) return this.react();
    if (this.c.moveTowards(this.target, this.c.species.walkSpeed, 0.6) || this.c.stuck || this.stateTime > 20) this.enterIdle();
  }
  tickSleep() {
    this.c.stop();
    if (this.c.aware && this.now - this.disengagedAt > 5) return this.react();
    if (this.stateTime > this.timer && !this.g.world.isNight) this.enterIdle();
  }
  tickCurious() {
    const player = this.g.player;
    if (!player || !player.alive) return this.enterIdle();
    const to = player.position.clone().sub(this.c.position); to.y = 0;
    const d = to.length();
    if (d > 24 || this.stateTime > 22) return this.loseInterest();
    if (this.c.record.personality === Personality.Playful && d < 7) {
      const n = to.clone().normalize();
      const tangent = new THREE.Vector3(n.z, 0, -n.x).multiplyScalar(this.strafeSign * 3);
      this.c.moveDir(tangent.addScaledVector(n, d - 4), this.c.species.walkSpeed * 1.8);
      if (this.now > this.nextFlip) { this.nextFlip = this.now + 3 + Math.random() * 3; this.strafeSign *= -1; this.c.anim.playHappy(); }
    } else if (d > 3.6) this.c.moveTowards(player.position, this.c.species.walkSpeed * 1.3, 3.4);
    else { this.c.stop(); this.c.face(player.position); }
  }
  tickFlee() {
    const player = this.g.player;
    if (!player) return this.enterIdle();
    const away = this.c.position.clone().sub(player.position); away.y = 0;
    if (away.length() > 24 || this.stateTime > 14) return this.loseInterest();
    if (this.stateTime < 0.05 || this.c.stuck || this.c.position.distanceToSquared(this.target) < 2) {
      this.c.clearStuck();
      const dir = away.lengthSq() > 0.01 ? away.normalize() : this.c.forward.negate();
      let found = null;
      for (const a of [0, 35, -35, 70, -70, 110, -110]) {
        const r = (a + (Math.random() * 20 - 10)) * toRad;
        const d2 = new THREE.Vector3(dir.x * Math.cos(r) + dir.z * Math.sin(r), 0, -dir.x * Math.sin(r) + dir.z * Math.cos(r));
        const p = this.c.position.clone().addScaledVector(d2, 9);
        if (this.g.world.isWalkable(p.x, p.z)) { found = p; break; }
      }
      this.target.copy(found || this.c.position.clone().addScaledVector(dir, -6));
    }
    this.c.moveTowards(this.target, this.c.species.runSpeed, 0.5);
  }
  loseInterest() { this.disengagedAt = this.now; this.c.calm(0.3); this.set('Return'); }
  tickReturn(dt) {
    this.c.health.heal(this.c.health.max * 0.08 * dt);
    if (this.now - this.disengagedAt > 5 && this.shouldReact()) return this.react();
    const arrived = this.c.moveTowards(this.c.home, this.c.species.walkSpeed * 1.4, 1.2);
    if (arrived || this.stateTime > 30) {
      if (this.stateTime > 30) this.c.teleport(this.c.home);
      this.enterIdle();
    } else if (this.c.stuck) {
      this.c.clearStuck();
      const f = this.c.forward;
      this.c.knockback.add(new THREE.Vector3(f.z, 0, -f.x).multiplyScalar(3));
    }
  }

  // ---------------------------------------------------------------- combat
  enterCombat(targetEntity) {
    if (!targetEntity || targetEntity.health.isDead || this.state === 'Exhausted' || this.state === 'Binding') return;
    this.combatTarget = targetEntity;
    if (!this.c.isWild) { if (this.state !== 'Assist') this.set('Assist'); return; }
    this.c.forceAware();
    if (this.state !== 'Combat') { this.recoverUntil = this.now + 0.8; this.set('Combat'); }
  }

  tickCombat() {
    const g = this.g, c = this.c;
    if (!c.isWild) {
      const enemy = g.battle.primary;
      this.combatTarget = enemy && !enemy.health.isDead && !enemy.removed ? enemy : null;
      if (!this.combatTarget || !g.player || g.player.position.distanceToSquared(c.position) > 22 * 22) return this.set('Follow');
    } else {
      const t = this.combatTarget;
      if (!t || t.health.isDead || t.removed) return this.loseInterest();
      if (c.position.distanceTo(t.position) > LEASH_TARGET || c.position.distanceTo(c.home) > LEASH_HOME) {
        Events.notify(`The ${c.name} lost interest.`);
        return this.loseInterest();
      }
      if (c.record.personality === Personality.Timid && c.health.fraction < 0.4 && !this.charging && !this.pending) return this.set('Flee');
    }
    const tp = this.combatTarget.position;
    const to = tp.clone().sub(c.position); to.y = 0;
    const d = to.length();
    if (this.charging) return this.tickCharge();
    if (this.pending) {
      c.stop(); c.face(tp);
      if (this.now >= this.windupEnd) this.execute(this.pending, tp);
      return;
    }
    if (this.now < this.recoverUntil) { c.face(tp); this.strafe(to, d, 0.6); return; }
    const attack = this.chooseAttack(d);
    if (attack) {
      this.pending = attack; this.windupEnd = this.now + attack.windup;
      c.anim.addGlow(0.6); this.sfx('windup', 0.8);
      return;
    }
    if (d > 8) c.moveTowards(tp, c.species.runSpeed * 0.8, 6);
    else if (d < 2.2) c.moveDir(to.clone().negate(), c.species.walkSpeed * 1.4);
    else this.strafe(to, d, 1);
    c.face(tp);
  }
  strafe(to, d, scale) {
    if (this.now > this.nextFlip) { this.nextFlip = this.now + 1.5 + Math.random() * 2; this.strafeSign *= -1; }
    if (d < 0.01) return;
    const n = to.clone().divideScalar(d);
    const tangent = new THREE.Vector3(n.z, 0, -n.x).multiplyScalar(this.strafeSign);
    this.c.moveDir(tangent.addScaledVector(n, (d - 5) * 0.3), this.c.species.walkSpeed * 1.2 * scale);
  }
  chooseAttack(d) {
    let best = null;
    for (const a of this.c.species.attacks) {
      if ((this.cooldowns.get(a) || 0) > this.now) continue;
      const inRange = a.kind === 'melee' ? d <= a.range : a.kind === 'charge' ? d >= 3 && d <= a.range : d >= 3.5 && d <= a.range;
      if (inRange && (!best || a.power > best.power)) best = a;
    }
    return best;
  }
  execute(a, tp) {
    const c = this.c, g = this.g;
    this.pending = null;
    this.cooldowns.set(a, this.now + a.cooldown * (0.9 + Math.random() * 0.3));
    this.recoverUntil = this.now + 0.6;
    const faction = c.health.faction;
    if (a.kind === 'projectile') {
      const origin = c.center.addScaledVector(c.forward, 0.5); origin.y += 0.3;
      const aim = tp.clone(); aim.y += 0.9;
      const speed = Math.max(5, a.speed), flight = origin.distanceTo(aim) / speed, grav = -9;
      const vel = aim.clone().sub(origin).divideScalar(flight); vel.y -= 0.5 * grav * flight;
      g.projectiles.spawn({ position: origin, velocity: vel, faction, owner: c, power: a.power, attack: c.stats.attack, element: a.element, radius: a.radius, knockback: a.knockback, color: srgb(0.6, 0.62, 0.5), scale: 0.28, gravity: grav, life: 4 });
      c.anim.playLunge(); this.sfx('pebble', 0.8);
    } else if (a.kind === 'charge') {
      const dir = tp.clone().sub(c.position); dir.y = 0;
      this.chargeDir = dir.lengthSq() > 0.01 ? dir.clone().normalize() : c.forward;
      this.chargeEnd = this.now + Math.min(a.range + 2, dir.length() + 2.5) / a.speed;
      this.charging = true; this.chargeHits.clear(); this.currentCharge = a;
      this.sfx('dodge', 0.8, 0.7);
    } else {
      c.anim.playLunge();
      const center = c.center.addScaledVector(c.forward, 0.9);
      for (const e of Targets.findHostiles(center, a.radius, faction, c, c.forward, 70)) {
        e.health.takeHit(a.power, c.stats.attack, a.element, faction, c, center, e.position.clone().sub(c.position), a.knockback, this.now);
      }
      this.sfx('swing', 0.7, 1.3);
    }
  }
  tickCharge() {
    const a = this.currentCharge, c = this.c;
    if (!a || this.now >= this.chargeEnd || c.stuck) { this.charging = false; c.clearStuck(); this.recoverUntil = this.now + 0.9; return; }
    c.moveDir(this.chargeDir, a.speed);
    c.face(c.position.clone().add(this.chargeDir));
    for (const e of Targets.findHostiles(c.center.addScaledVector(this.chargeDir, 0.4), a.radius, c.health.faction, c)) {
      if (this.chargeHits.has(e)) continue;
      this.chargeHits.add(e);
      e.health.takeHit(a.power, c.stats.attack, a.element, c.health.faction, c, e.position, this.chargeDir, a.knockback, this.now);
    }
  }
  onDamaged(info) {
    const c = this.c;
    c.anim.playHurt();
    c.knockback.addScaledVector(info.direction, info.knockback);
    this.sfx('hurt', 0.8, 0.95 + Math.random() * 0.15);
    if (!c.isWild || c.health.isDead) return;
    const attacker = info.source && info.source.health ? info.source : null;
    if (this.state === 'Combat') { if (attacker && attacker !== this.combatTarget && Math.random() < 0.35) this.combatTarget = attacker; return; }
    if (this.state === 'Binding' || this.state === 'Exhausted') return;
    if (c.record.personality === Personality.Timid && c.health.fraction < 0.4) return this.set('Flee');
    this.enterCombat(attacker || this.g.player);
  }
  onExhausted() {
    if (this.state === 'Binding') return;
    this.set('Exhausted');
    this.timer = this.c.isWild ? EXHAUSTED_WILD : EXHAUSTED_COMPANION;
    this.sfx('hurt', 1, 0.7);
    if (this.c.isWild) Events.notify(`The ${this.c.name} is exhausted! Bind it now — 100% chance.`, 'success');
  }
  tickExhausted() {
    if (this.stateTime < this.timer) return;
    this.c.health.setFraction(this.c.isWild ? 0.35 : 0.5);
    if (this.c.isWild) { Events.notify(`The ${this.c.name} recovered and ran off.`); this.loseInterest(); }
    else this.set('Follow');
  }

  // ---------------------------------------------------------------- binding
  beginBinding() {
    if (this.state === 'Binding') return;
    this.before = this.state;
    this.c.health.canBeDamaged = false;
    this.set('Binding');
    this.c.anim.addGlow(1);
  }
  breakFree() {
    if (this.state !== 'Binding') return;
    this.c.health.canBeDamaged = true;
    this.c.anim.playHappy();
    if (this.before === 'Exhausted') { this.set('Exhausted'); this.timer = 5; return; }
    if (this.c.record.personality === Personality.Timid) this.set('Flee');
    else if (this.g.player) this.enterCombat(this.g.player);
    else this.enterIdle();
  }

  // ---------------------------------------------------------------- companion
  tickFollow() {
    const g = this.g, p = g.player;
    if (!p) return;
    const enemy = g.battle.primary;
    if (enemy && !enemy.health.isDead && !enemy.removed && enemy.position.distanceToSquared(p.position) < 18 * 18) return this.enterCombat(enemy);
    const fwd = p.forward, right = new THREE.Vector3(-fwd.z, 0, fwd.x);
    const slot = p.position.clone().addScaledVector(fwd, -0.8).addScaledVector(right, 2.0); // beside, not between player and camera
    const d = this.c.position.distanceTo(slot);
    if (d > 25) { const w = g.world.findWalkableNear(slot.x, slot.z, 3, 8) || slot; this.c.teleport(w); return; }
    if (d > 1.2) {
      const sp = d > 6 ? this.c.species.runSpeed : lerp(this.c.species.walkSpeed, this.c.species.runSpeed, (d - 1.2) / 5);
      this.c.moveTowards(slot, sp, 0.8);
    } else {
      this.c.stop();
      if (this.stateTime > 4 && Math.random() < 0.004) this.c.anim.playHappy();
    }
  }

  resetToCalm() {
    this.combatTarget = null;
    this.c.health.canBeDamaged = true;
    this.c.health.setFraction(1);
    this.c.calm();
    this.disengagedAt = this.now;
    if (!this.c.isWild) this.set('Follow');
    else { this.c.teleport(this.c.home); this.enterIdle(); }
  }
}
