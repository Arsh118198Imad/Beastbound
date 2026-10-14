// The Warden: movement, combat kit, binding and procedural avatar (port of Runtime/Player).
import * as THREE from 'three';
import { clamp, clamp01, lerp, damp, srgb } from './util.js';
import { CaptureRules, Element } from './rules.js';
import { Health, Faction, Targets } from './combat.js';
import { Events } from './events.js';
import { Layout } from './world.js';

const toRad = Math.PI / 180;
const quat = (x, y, z) => new THREE.Quaternion().setFromEuler(new THREE.Euler(x * toRad, y * toRad, z * toRad, 'YXZ'));
const SPHERE = new THREE.SphereGeometry(0.5, 16, 10);
const CAPSULE = new THREE.CapsuleGeometry(0.5, 1, 6, 12);
const CYL = new THREE.CylinderGeometry(0.5, 0.5, 2, 12);
const BOX = new THREE.BoxGeometry(1, 1, 1);

function part(parent, geo, mat, pos, scale, rot = null, shadow = true) {
  const m = new THREE.Mesh(geo, mat); m.position.set(...pos); m.scale.set(...scale);
  if (rot) m.quaternion.copy(rot);
  m.castShadow = shadow; parent.add(m); return m;
}
function pivot(parent, pos) { const g = new THREE.Group(); g.position.set(...pos); parent.add(g); return g; }

class Avatar {
  constructor(group) {
    const M = (r, g, b, rough = 0.7, extra = {}) => new THREE.MeshStandardMaterial({ color: srgb(r, g, b), roughness: rough, ...extra });
    const tunic = M(0.16, 0.5, 0.52), tunicDark = M(0.12, 0.36, 0.4), skin = M(0.93, 0.76, 0.62, 0.6), hair = M(0.32, 0.19, 0.12, 0.6);
    const scarf = M(0.93, 0.45, 0.22, 0.85), leather = M(0.45, 0.3, 0.18, 0.75), pants = M(0.28, 0.24, 0.22, 0.85), boots = M(0.2, 0.14, 0.1, 0.7);
    const leaf = M(0.4, 0.75, 0.3), wood = M(0.52, 0.36, 0.2, 0.7), eye = M(0.1, 0.1, 0.14, 0.2);
    this.glow = M(0.55, 1, 0.9, 0.2, { emissive: srgb(0.4, 1, 0.9), emissiveIntensity: 1.5 });
    this.root = pivot(group, [0, 0, 0]);
    // The rig was authored in Unity's left-handed space; mirroring X keeps the staff in the right hand.
    this.mirror = pivot(this.root, [0, 0, 0]);
    this.mirror.scale.x = -1;
    this.hips = pivot(this.mirror, [0, 0.95, 0]);
    this.torso = pivot(this.hips, [0, 0, 0]);
    part(this.torso, CAPSULE, tunic, [0, 0.32, 0], [0.48, 0.34, 0.34]);
    part(this.torso, new THREE.CylinderGeometry(0.22, 0.3, 0.3, 10).translate(0, 0.15, 0), tunicDark, [0, -0.12, 0], [1, 1, 1]);
    part(this.torso, CYL, leather, [0, 0.08, 0], [0.5, 0.035, 0.37]);
    part(this.torso, SPHERE, scarf, [0, 0.6, 0.02], [0.5, 0.14, 0.42]);
    part(this.torso, BOX, scarf, [0.12, 0.45, -0.2], [0.1, 0.3, 0.03], quat(15, 0, -10));
    part(this.torso, BOX, leather, [0, 0.36, -0.24], [0.34, 0.38, 0.18]);
    part(this.torso, CYL, scarf, [0, 0.6, -0.26], [0.13, 0.2, 0.13], quat(0, 0, 90));
    this.head = pivot(this.torso, [0, 0.8, 0]);
    part(this.head, SPHERE, skin, [0, 0, 0], [0.34, 0.36, 0.33]);
    part(this.head, SPHERE, hair, [0, 0.06, -0.03], [0.37, 0.33, 0.36]);
    part(this.head, SPHERE, hair, [0.05, 0.12, 0.1], [0.28, 0.12, 0.2], quat(0, 0, -12));
    part(this.head, SPHERE, eye, [-0.07, 0, 0.155], [0.045, 0.065, 0.03], null, false);
    part(this.head, SPHERE, eye, [0.07, 0, 0.155], [0.045, 0.065, 0.03], null, false);
    part(this.head, SPHERE, leaf, [-0.1, 0.2, -0.02], [0.06, 0.2, 0.1], quat(0, 0, 30));
    this.armL = pivot(this.torso, [-0.3, 0.55, 0]);
    this.armR = pivot(this.torso, [0.3, 0.55, 0]);
    for (const a of [this.armL, this.armR]) {
      part(a, CAPSULE, tunic, [0, -0.22, 0], [0.14, 0.24, 0.14]);
      part(a, SPHERE, skin, [0, -0.5, 0], [0.12, 0.12, 0.12]);
    }
    const staff = pivot(this.armR, [0, -0.5, 0]);
    staff.quaternion.copy(quat(80, 0, 0));
    part(staff, CYL, wood, [0, 0.25, 0], [0.05, 0.75, 0.05]);
    part(staff, CYL, leaf, [0, 0.85, 0], [0.07, 0.08, 0.07]);
    this.tip = part(staff, new THREE.OctahedronGeometry(0.5, 0).scale(1, 1.6, 1), this.glow, [0, 1.05, 0], [0.12, 0.12, 0.12], null, false);
    this.legL = pivot(this.hips, [-0.12, 0, 0]);
    this.legR = pivot(this.hips, [0.12, 0, 0]);
    for (const l of [this.legL, this.legR]) {
      part(l, CAPSULE, pants, [0, -0.42, 0], [0.17, 0.42, 0.17]);
      part(l, SPHERE, boots, [0, -0.86, 0.05], [0.17, 0.14, 0.27]);
    }
    this.phase = 0; this.crouch = 0; this.lean = 0;
    this.strike = 0; this.strikeIndex = 0; this.cast = 0; this.throw = 0; this.dodge = 0; this.hurt = 0; this.land = 0; this.defeat = 0;
  }
  reset() { this.strike = this.cast = this.throw = this.dodge = this.hurt = this.land = 0; this.defeat = 0; }

  update(dt, t, s) {
    const move = clamp01(s.speed / 7);
    this.phase += dt * s.speed * (s.sneaking ? 3.4 : 2.6);
    this.crouch += clamp((s.sneaking ? 1 : 0) - this.crouch, -dt * 5, dt * 5);
    this.lean = lerp(this.lean, move * 12 + this.crouch * 18, damp(8, dt));
    this.defeat += clamp((s.dead ? 1 : 0) - this.defeat, -dt * 2.5, dt * 2.5);
    const bob = s.grounded ? Math.abs(Math.sin(this.phase)) * 0.06 * clamp01(s.speed / 2) : 0;
    let squash = 0;
    if (this.land > 0) { this.land -= dt; squash = Math.sin(this.land / 0.15 * Math.PI) * 0.1; }
    this.hips.position.y = 0.95 + bob - this.crouch * 0.22 - squash - (s.wading ? 0.05 : 0);
    let flinch = 0;
    if (this.hurt > 0) { this.hurt -= dt; flinch = Math.sin(this.hurt / 0.25 * Math.PI) * -18; }
    const twist = Math.sin(this.phase) * 6 * move;
    this.torso.quaternion.copy(quat(this.lean + flinch, twist, 0));
    this.head.quaternion.copy(quat(-this.lean * 0.5, -twist * 0.5, 0));
    const amp = lerp(28, 50, move) * clamp01(s.speed / 1.2);
    let lL, lR;
    if (s.grounded) { lL = Math.sin(this.phase) * amp - this.crouch * 25; lR = -Math.sin(this.phase) * amp - this.crouch * 25; }
    else { const k = s.vy > 0 ? 1 : 0.6; lL = -35 * k; lR = 20 * k; }
    this.legL.quaternion.copy(quat(lL, 0, 0));
    this.legR.quaternion.copy(quat(lR, 0, 0));
    const sw = Math.sin(this.phase) * amp * 0.8;
    let aL = quat(-sw + this.crouch * 20, 0, -8 - (s.grounded ? 0 : 35));
    let aR = quat(sw + this.crouch * 20, 0, 8 + (s.grounded ? 0 : 35));
    if (this.strike > 0) {
      this.strike -= dt;
      const k = 1 - clamp01(this.strike / 0.42), w = k < 0.35 ? THREE.MathUtils.smoothstep(k / 0.35, 0, 1) : 1;
      if (this.strikeIndex === 0) aR = quat(lerp(-130, -20, w), lerp(-40, 30, w), 20);
      else if (this.strikeIndex === 1) aR = quat(-80, lerp(60, -60, w), 30);
      else aR = quat(lerp(-190, -10, w), 0, 5);
      aL = quat(-30, 0, -20);
    } else if (this.cast > 0) { this.cast -= dt; aR = quat(-95, 0, 5); aL = quat(-80, 0, -5); }
    else if (this.throw > 0) { this.throw -= dt; aR = quat(lerp(-170, -40, THREE.MathUtils.smoothstep(1 - clamp01(this.throw / 0.4), 0, 1)), 0, 10); }
    this.armL.quaternion.copy(aL);
    this.armR.quaternion.copy(aR);
    let rootQ = new THREE.Quaternion(), ry = 0;
    if (this.dodge > 0) {
      this.dodge -= dt;
      const k = 1 - clamp01(this.dodge / 0.3);
      rootQ = quat(k * 360, 0, 0); ry = Math.sin(k * Math.PI) * 0.45;
      this.hips.position.y -= Math.sin(k * Math.PI) * 0.35;
    }
    if (this.defeat > 0) { rootQ.slerp(quat(-80, 0, 0), this.defeat); ry += 0.25 * this.defeat; }
    this.root.quaternion.copy(rootQ);
    this.root.position.y = ry;
    this.glow.emissiveIntensity = 1.5 + Math.sin(t * 3) * 0.25 + (this.cast > 0 ? 2.5 : 0) + (this.throw > 0 ? 1.5 : 0);
  }
}

// ====================================================================== Player

export class Player {
  constructor(game) {
    this.game = game;
    this.position = game.world.playerSpawn.clone();
    this.yaw = game.world.playerSpawnYaw;
    this.radius = 0.34;
    this.velocity = new THREE.Vector3(); this.vy = 0; this.knockback = new THREE.Vector3();
    this.grounded = true; this.lastGrounded = 0; this.jumpPressedAt = -10; this.forcedUntil = 0; this.forced = new THREE.Vector3();
    this.sneaking = false; this.running = false; this.wading = false; this.noise = 0.2; this.speedPlanar = 0;
    this.moveSpeedMultiplier = 1; this.facingTarget = null; this.moveWorld = new THREE.Vector3();
    this.footDist = 0;
    this.health = new Health(this, Faction.Player, 100, 10, Element.Neutral);
    this.health.onDamaged.push((info) => this.onDamaged(info));
    this.group = new THREE.Group();
    game.scene.add(this.group);
    this.avatar = new Avatar(this.group);
    Targets.list.add(this);
    // combat
    this.attackStat = 12; this.comboPower = [10, 10, 16];
    this.attackStart = -10; this.hitApplied = true; this.queued = false; this.lastAttackEnd = -10; this.combo = 0;
    this.sparkReadyAt = 0; this.dodgeReadyAt = 0; this.lastDamaged = -10; this.noiseUntil = 0;
    this.sparkCooldown = 4.5; this.dodgeCooldown = 0.7;
    // binding
    this.bindTarget = null; this.binding = null; this.bindReadyAt = 0; this.nextScan = 0;
    this.orbMat = new THREE.MeshStandardMaterial({ color: srgb(0.55, 1, 0.9), emissive: srgb(0.5, 1, 0.9), emissiveIntensity: 2.4 });
  }

  get alive() { return !this.health.isDead; }
  get center() { return this.position.clone().setY(this.position.y + 0.9); }
  get forward() { return new THREE.Vector3(Math.sin(this.yaw), 0, Math.cos(this.yaw)); }
  get sparkCd01() { return clamp01(1 - (this.sparkReadyAt - this.game.time) / this.sparkCooldown); }
  get dodgeCd01() { return clamp01(1 - (this.dodgeReadyAt - this.game.time) / this.dodgeCooldown); }
  get attacking() { return this.game.time - this.attackStart < 0.42; }

  onDamaged(info) {
    this.knockback.addScaledVector(info.direction, info.knockback);
    this.avatar.hurt = 0.25;
    this.lastDamaged = this.game.time;
    this.game.camera.shake(0.25, 0.18);
    this.game.sfx('hitHeavy', this.center, 0.9, 0.85);
  }

  respawn(pos, yaw) {
    this.position.copy(pos); this.yaw = yaw;
    this.velocity.set(0, 0, 0); this.vy = 0; this.knockback.set(0, 0, 0); this.forcedUntil = 0;
    this.health.canBeDamaged = true; this.health.setFraction(1); this.health.invulnerableUntil = 0;
    this.game.input.sneakToggled = false;
    this.cancelBinding();
    this.attackStart = -10; this.hitApplied = true; this.queued = false; this.moveSpeedMultiplier = 1; this.facingTarget = null;
    this.avatar.reset();
  }

  forceMove(v, dur) { this.forced.set(v.x, 0, v.z); this.forcedUntil = this.game.time + dur; }

  update(dt) {
    const g = this.game, input = g.input, world = g.world, now = g.time;
    const control = this.alive && input.gameplayEnabled;
    const mv = control ? input.move : { x: 0, y: 0 };
    const camYaw = g.camera.yaw;
    const fwd = new THREE.Vector3(Math.sin(camYaw), 0, Math.cos(camYaw));
    const right = new THREE.Vector3(-fwd.z, 0, fwd.x);
    // three.js is right-handed: for a camera looking along fwd, screen-right is (-fwd.z, 0, fwd.x).
    const moveDir = fwd.clone().multiplyScalar(mv.y).addScaledVector(right, mv.x);
    if (moveDir.lengthSq() > 1) moveDir.normalize();
    this.moveWorld.copy(moveDir);

    this.wading = world.waterDepth(this.position.x, this.position.z) > 0.35;
    this.sneaking = control && input.sneak && !this.wading;
    this.running = control && input.run && !this.sneaking && !this.wading && moveDir.lengthSq() > 0.01;
    let speed = this.sneaking ? 2.1 : this.running ? 7 : 3.9;
    if (this.wading) speed *= 0.55;
    speed *= this.moveSpeedMultiplier;

    const groundY = world.heightAt(this.position.x, this.position.z);
    const wasGrounded = this.grounded;
    this.grounded = this.position.y <= groundY + 0.02 && this.vy <= 0;
    if (this.grounded) {
      this.lastGrounded = now; this.position.y = groundY; this.vy = 0;
      if (!wasGrounded) { this.avatar.land = 0.15; g.sfx('land', this.position, 0.5); }
    }
    if (control && input.jumpPressed) this.jumpPressedAt = now;
    if (now - this.lastGrounded <= 0.12 && !this.wading && this.moveSpeedMultiplier > 0.5 && now - this.jumpPressedAt <= 0.15) {
      this.vy = Math.sqrt(2 * 1.25 * 24); this.jumpPressedAt = -10; this.lastGrounded = -10; this.grounded = false;
      g.sfx('jump', this.position, 0.5);
    }
    let grav = -24;
    if (this.vy > 0 && !(control && input.jumpHeld)) grav *= 1.8;
    if (!this.grounded) this.vy = Math.max(-40, this.vy + grav * dt);

    const target = moveDir.clone().multiplyScalar(speed);
    const ctrl = this.grounded ? 1 : 0.45;
    const rate = (target.lengthSq() > this.velocity.lengthSq() ? 30 : 26) * ctrl * dt;
    const diff = target.clone().sub(this.velocity);
    if (diff.length() <= rate) this.velocity.copy(target); else this.velocity.addScaledVector(diff.normalize(), rate);
    const horiz = now < this.forcedUntil ? this.forced : this.velocity;
    const kl = this.knockback.length();
    if (kl > 0) this.knockback.multiplyScalar(Math.max(0, kl - 20 * dt) / kl);
    const before = this.position.clone();
    this.position.x += (horiz.x + this.knockback.x) * dt;
    this.position.z += (horiz.z + this.knockback.z) * dt;
    this.position.y += this.vy * dt;
    // Steep slopes act as walls (like CharacterController slope limit)
    const newGround = world.heightAt(this.position.x, this.position.z);
    if (this.grounded && newGround - before.y > 0.45 && world.terrain.slopeAt(this.position.x, this.position.z) > 48) {
      this.position.x = before.x; this.position.z = before.z;
    }
    world.resolve(this.position, this.radius);
    if (this.position.y < world.heightAt(this.position.x, this.position.z)) this.position.y = world.heightAt(this.position.x, this.position.z);
    if (!world.insidePlayArea(this.position.x, this.position.z)) {
      const d = Math.hypot(this.position.x, this.position.z), k = Layout.playableRadius / d;
      this.position.x *= k; this.position.z *= k;
      if (!this._edgeNote || now - this._edgeNote > 4) { this._edgeNote = now; Events.notify('The mountains are too steep to cross — for now.'); }
    }

    let face = null;
    if (this.facingTarget) face = this.facingTarget.clone().sub(this.position);
    else if (now < this.forcedUntil) face = this.forced.clone();
    else if (moveDir.lengthSq() > 0.01) face = moveDir.clone();
    if (face && face.x * face.x + face.z * face.z > 1e-4) {
      const t = Math.atan2(face.x, face.z);
      const d = ((t - this.yaw + Math.PI * 3) % (Math.PI * 2)) - Math.PI, max = 720 * toRad * dt;
      this.yaw += clamp(d, -max, max);
    }
    this.speedPlanar = Math.hypot(this.position.x - before.x, this.position.z - before.z) / dt;
    let noise = this.speedPlanar < 0.2 ? 0.15 : this.sneaking ? 0.35 : this.running ? 1.15 : 0.7;
    if (!this.grounded) noise = Math.max(noise, 0.8);
    if (now < this.noiseUntil) noise = Math.max(noise, 1.3);
    this.noise = noise;
    if (this.grounded && this.speedPlanar > 0.5) {
      this.footDist += this.speedPlanar * dt;
      if (this.footDist > (this.running ? 2.1 : 1.5)) {
        this.footDist = 0;
        g.sfx(this.wading ? 'splash' : 'footstep', this.position, this.sneaking ? 0.12 : this.running ? 0.45 : 0.3, 0.9 + Math.random() * 0.2);
      }
    }

    if (this.alive) {
      if (!g.battle.inBattle && now - this.lastDamaged > 4) this.health.heal(12 * dt);
      if (input.gameplayEnabled) this.updateCombat();
      this.updateBinder();
    }
    this.avatar.update(dt, now, { speed: this.speedPlanar, grounded: this.grounded, sneaking: this.sneaking, wading: this.wading, vy: this.vy, dead: !this.alive });
    this.group.position.copy(this.position);
    this.group.rotation.y = this.yaw;
  }

  // ------------------------------------------------------------------ combat
  updateCombat() {
    const input = this.game.input, now = this.game.time;
    const busy = !!this.binding;
    if (input.dodgePressed && now >= this.dodgeReadyAt && !busy) this.dodge();
    if (input.attackPressed && !busy) { if (this.attacking) this.queued = true; else this.startStrike(); }
    if (input.sparkPressed && now >= this.sparkReadyAt && !busy) this.castSpark();
    const t = now - this.attackStart;
    if (!this.hitApplied && t >= 0.16) { this.hitApplied = true; this.applyStrike(); }
    if (this.attackStart > 0 && t >= 0.42 && this.moveSpeedMultiplier < 1) {
      this.lastAttackEnd = now; this.moveSpeedMultiplier = 1; this.facingTarget = null;
      this.combo = (this.combo + 1) % 3;
      if (this.queued) this.startStrike();
    }
  }
  findAimTarget(radius) {
    const pref = this.moveWorld.lengthSq() > 0.01 ? this.moveWorld : new THREE.Vector3(Math.sin(this.game.camera.yaw), 0, Math.cos(this.game.camera.yaw));
    let best = null, bestScore = Infinity;
    for (const e of Targets.findHostiles(this.position, radius, Faction.Player, this)) {
      const to = e.position.clone().sub(this.position); to.y = 0;
      const ang = to.angleTo(pref) / toRad;
      if (ang > 100) continue;
      const score = to.length() + ang * 0.05;
      if (score < bestScore) { bestScore = score; best = e; }
    }
    return best;
  }
  startStrike() {
    const now = this.game.time;
    if (now - this.lastAttackEnd > 0.75) this.combo = 0;
    this.attackStart = now; this.hitApplied = false; this.queued = false; this.noiseUntil = now + 0.6;
    const target = this.findAimTarget(6);
    this.facingTarget = target ? target.position.clone() : null;
    if (!target && this.moveWorld.lengthSq() > 0.01) this.yaw = Math.atan2(this.moveWorld.x, this.moveWorld.z);
    const lunge = target ? target.position.clone().sub(this.position).setY(0) : this.forward;
    const dist = target ? clamp(lunge.length() - 1.6, 0, 2) : 0.6;
    if (lunge.lengthSq() > 0.01) this.forceMove(lunge.normalize().multiplyScalar(dist / 0.12), 0.12);
    this.moveSpeedMultiplier = 0.3;
    this.avatar.strike = 0.42; this.avatar.strikeIndex = this.combo;
    this.game.sfx('swing', this.center, 0.7, 0.9 + this.combo * 0.12);
  }
  applyStrike() {
    const center = this.center.addScaledVector(this.forward, 1.1);
    const power = this.comboPower[this.combo], finisher = this.combo === 2;
    let hit = false;
    for (const e of Targets.findHostiles(center, 1.5, Faction.Player, this, this.forward, 80)) {
      if (e.health.takeHit(power, this.attackStat, Element.Neutral, Faction.Player, this, e.center, e.position.clone().sub(this.position), finisher ? 6 : 2.5, this.game.time) > 0) hit = true;
    }
    if (hit) {
      this.game.sfx(finisher ? 'hitHeavy' : 'hit', center, 0.9);
      this.game.hitStop(finisher ? 0.08 : 0.045);
      this.game.camera.shake(finisher ? 0.2 : 0.1, 0.12);
    }
  }
  castSpark() {
    const g = this.game, now = g.time;
    this.sparkReadyAt = now + this.sparkCooldown; this.noiseUntil = now + 0.8;
    const target = this.findAimTarget(18);
    const origin = this.position.clone().setY(this.position.y + 1.3).addScaledVector(this.forward, 0.6);
    let dir;
    if (target) { dir = target.center.sub(origin).normalize(); this.yaw = Math.atan2(dir.x, dir.z); }
    else { dir = new THREE.Vector3(Math.sin(g.camera.yaw), 0, Math.cos(g.camera.yaw)); this.yaw = g.camera.yaw; }
    g.projectiles.spawn({
      position: origin, velocity: dir.multiplyScalar(22), faction: Faction.Player, owner: this, power: 20, attack: this.attackStat,
      element: Element.Ember, radius: 0.6, knockback: 4, color: srgb(1, 0.6, 0.25), emissive: srgb(1, 0.45, 0.1), scale: 0.35, life: 1.6,
      homing: target, homingStrength: 3, onImpact: (p) => g.sfx('sparkImpact', p, 0.8),
    });
    this.avatar.cast = 0.35;
    g.sfx('spark', origin, 0.8);
  }
  dodge() {
    const now = this.game.time;
    this.dodgeReadyAt = now + this.dodgeCooldown;
    const dir = this.moveWorld.lengthSq() > 0.01 ? this.moveWorld.clone().normalize() : this.forward.negate();
    this.forceMove(dir.multiplyScalar(12.5), 0.3);
    this.health.setInvulnerable(0.35, now);
    this.avatar.dodge = 0.3;
    this.attackStart = -10; this.hitApplied = true; this.queued = false; this.moveSpeedMultiplier = 1; this.facingTarget = null;
    this.game.sfx('dodge', this.position, 0.6);
  }

  // ------------------------------------------------------------------ binding (transparent odds, free orbs, Resonance)
  updateBinder() {
    const g = this.game, now = g.time;
    if (now >= this.nextScan) { this.nextScan = now + 0.1; this.bindTarget = this.binding ? null : this.findBindTarget(); }
    if (this.bindTarget && !this.bindTarget.canBeBound) this.bindTarget = null;
    if (this.binding) this.updateBinding();
    else if (g.input.bindPressed && now >= this.bindReadyAt) {
      if (this.bindTarget) this.startBinding(this.bindTarget);
      else Events.notify('No creature in reach. Get closer and face it.');
    }
  }
  findBindTarget() {
    const camF = new THREE.Vector3(Math.sin(this.game.camera.yaw), 0, Math.cos(this.game.camera.yaw));
    let best = null, bestScore = Infinity;
    for (const c of this.game.creatures) {
      if (!c.canBeBound) continue;
      const to = c.position.clone().sub(this.position); to.y = 0;
      const d = to.length();
      if (d > 7.5) continue;
      const ang = Math.min(to.angleTo(camF), to.angleTo(this.forward)) / toRad;
      if (ang > 75 && d > 2) continue;
      if (this.game.world.segmentBlocked(this.position.clone().setY(this.position.y + 1.4), c.center)) continue;
      const score = d + ang * 0.04;
      if (score < bestScore) { bestScore = score; best = c; }
    }
    return best;
  }
  startBinding(target) {
    const g = this.game, now = g.time;
    this.bindReadyAt = now + 1.2;
    const chance = target.captureChance, awareness = target.awareness; // locked at throw time
    const to = target.position.clone().sub(this.position);
    this.yaw = Math.atan2(to.x, to.z);
    this.avatar.throw = 0.4;
    g.sfx('bindThrow', this.center, 0.8);
    const orb = new THREE.Mesh(new THREE.SphereGeometry(0.15, 12, 8), this.orbMat);
    g.scene.add(orb);
    this.binding = { target, chance, awareness, phase: 'flight', t: 0, orb, ring: null, start: this.center.addScaledVector(this.forward, 0.4), pulses: 0 };
  }
  updateBinding() {
    const g = this.game, b = this.binding, dt = g.dt, t = b.target;
    b.t += dt;
    if (!t || t.removed) return this.cancelBinding();
    if (b.phase === 'flight') {
      const k = clamp01(b.t / 0.4);
      b.orb.position.lerpVectors(b.start, t.center, k).y += Math.sin(k * Math.PI) * 1.2;
      if (k >= 1) {
        if (!t.canBeBound) return this.cancelBinding();
        g.scene.remove(b.orb); b.orb.geometry.dispose(); b.orb = null;
        t.brain.beginBinding();
        b.ring = new THREE.Mesh(new THREE.TorusGeometry(0.75, 0.05, 8, 40), this.orbMat);
        b.ring.rotation.x = Math.PI / 2;
        g.scene.add(b.ring);
        b.phase = 'shake'; b.t = 0;
      }
    } else if (b.phase === 'shake') {
      b.ring.position.copy(t.position).y += 0.5;
      b.ring.scale.setScalar(1 + Math.sin(g.time * 12) * 0.08);
      b.ring.rotation.z += dt * 6;
      if (b.t >= b.pulses * 0.5) { g.sfx('bindShake', t.center, 0.8, 1 + b.pulses * 0.12); b.pulses++; }
      if (b.t >= 1.5) {
        g.scene.remove(b.ring); b.ring.geometry.dispose(); b.ring = null;
        if (CaptureRules.resolve(b.chance, Math.random())) {
          g.sfx('bindSuccess', t.center, 1);
          g.bindCreature(t, b.awareness);
        } else {
          t.failedBinds++;
          t.brain.breakFree();
          g.sfx('bindFail', t.center, 0.9);
          Events.notify(`${t.name} broke free! Resonance builds — next attempt: ${Math.round(t.captureChance * 100)}%.`, 'warning');
        }
        this.binding = null;
      }
    }
  }
  cancelBinding() {
    const b = this.binding;
    if (!b) return;
    if (b.orb) { this.game.scene.remove(b.orb); b.orb.geometry.dispose(); }
    if (b.ring) { this.game.scene.remove(b.ring); b.ring.geometry.dispose(); }
    if (b.target && !b.target.removed && b.target.brain.state === 'Binding') b.target.brain.resetToCalm();
    this.binding = null;
  }
}
