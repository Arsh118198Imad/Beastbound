// Beastbound (web edition) — game bootstrap, state flow, systems hub and main loop.
import * as THREE from 'three';
import { World, occlusionUniforms } from './world.js';
import { Models } from './render/models.js';
import { applyArtStyle, ArtStyle } from './render/style.js';
import { Player } from './player.js';
import { OrbitCamera } from './camera.js';
import { Creature } from './creatures.js';
import { Battle, Projectiles, Targets } from './combat.js';
import { AudioSystem } from './audio.js';
import { Input } from './input.js';
import { UI } from './ui.js';
import { Events } from './events.js';
import { Saves, FrameCaps, QualityNames } from './save.js';
import { createRecord, Species, Awareness } from './rules.js';
import { PostFX } from './render/postfx.js';

const SEED = 20261014;

class Collection {
  constructor() { this.clear(); }
  clear() { this.records = []; this.partnerUid = null; this.nests = new Set(); this.seen = new Set(); }
  get partner() { return this.records.find((r) => r.uid === this.partnerUid) || null; }
  find(uid) { return this.records.find((r) => r.uid === uid) || null; }
  load(d) {
    this.clear();
    for (const r of d.collection) { r.level = Math.min(50, Math.max(1, r.level | 0 || 1)); this.records.push(r); this.seen.add(r.speciesId); }
    for (const n of d.capturedNests) this.nests.add(n);
    for (const s of d.seenSpecies) this.seen.add(s);
    this.partnerUid = this.find(d.partnerUid) ? d.partnerUid : (this.records[0] ? this.records[0].uid : null);
  }
}

class Objectives {
  constructor(game) {
    this.game = game; this.step = 0; this.bestiaryOpened = false;
    Events.on('spotted', () => { if (this.step === 0) this.advance(1); });
    Events.on('bestiaryOpened', () => { this.bestiaryOpened = true; this.refresh(); });
  }
  reset() { this.step = 0; this.bestiaryOpened = false; this.metGuardian = false; }
  remaining() { return this.game.world.nests.filter((n) => !this.game.collection.nests.has(n.id)).length; }
  advance(s) { if (s > this.step) { this.step = s; this.refresh(); } }
  update() {
    if (this.game.state !== 'playing') return;
    const p = this.game.player.position;
    if (!this.metGuardian) {
      const g = this.game.creatures.find((c) => c.isWild && c.species.id === 'thalorix' && c.position.distanceToSquared(p) < 24 * 24);
      if (g) { this.metGuardian = true; this.game.ui.banner('Thalorix', 'Forest Guardian of the western woods'); this.game.sfx('discovery', null, 0.6); }
    }
    if (this.step !== 0) return;
    const near = this.game.creatures.find((c) => c.isWild && c.position.distanceToSquared(p) < 14 * 14);
    if (near) {
      this.advance(1);
      Events.notify(`A wild ${near.species.name}! Sneak closer, or weaken it, then bind it.`, 'discovery');
      this.game.sfx('discovery', null, 0.6);
    }
  }
  refresh() {
    const bound = this.game.collection.records.length;
    let s = this.step;
    if (bound > 0 && s < 2) s = 2;
    if (s === 2 && this.bestiaryOpened) s = 3;
    if (s === 3 && this.remaining() === 0) s = 4;
    if (s !== this.step) {
      this.step = s;
      if (s === 4) Events.notify('Every creature in Sunmeadow Vale is bound. Prototype complete — thank you for playing!', 'discovery');
    }
  }
  get title() { return ['Explore Sunmeadow Vale', 'Bind a Pebbloom', 'Meet your partner', 'Befriend the vale', 'Sunmeadow Vale complete'][this.step]; }
  get description() {
    const P = (a) => `<kbd>${this.game.input.prompt(a)}</kbd>`;
    switch (this.step) {
      case 0: return 'Wild Pebblooms live around the meadow, the lake and the old ruins. Find one.';
      case 1: return `Sneak ${P('Sneak')} to stay unnoticed, or weaken it with ${P('Attack')} / ${P('Spark')}. Then press ${P('Bind')} to throw a Bind Orb — the chance is shown on screen.`;
      case 2: return `Open your Bestiary ${P('Bestiary')} to see your new partner's stats.`;
      case 3: { const t = this.game.world.nests.length; return `Bind every creature in the vale (${t - this.remaining()}/${t}) — the Thalorix guards the western woods.`; }
      default: return 'Keep exploring! More regions and creatures arrive in future milestones.';
    }
  }
}

class Game {
  constructor() {
    this.canvas = document.getElementById('game');
    this.renderer = new THREE.WebGLRenderer({ canvas: this.canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 0.55; // raised for the stylized art style in applySettings
    this.scene = new THREE.Scene();
    this.camera3d = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 1500);
    this.input = new Input(this.canvas);
    this.audio = new AudioSystem();
    this.settings = Saves.loadSettings();
    applyArtStyle(this.settings.artStyle); // must run before any shader compiles
    this.bootArtStyle = this.settings.artStyle;
    this.state = 'boot';
    this.time = 0; this.dt = 0; this.frame = 0; this.playTime = 0; this.playStart = 0;
    this.hitStopUntil = 0; this.lastRender = 0; this.nextAutosave = 0;
    this.creatures = [];
    this.battle = new Battle();
    this.projectiles = new Projectiles(this);
    this.collection = new Collection();
    this.wasLocked = false;
    window.addEventListener('resize', () => this.resize());
    this.resize();
  }

  boot() {
    this.world = new World(this.scene, SEED, this.renderer);
    this.world.setHour(17.4); // golden hour for the title vista
    this.postfx = new PostFX(this.renderer, this.scene, this.camera3d);
    this.player = new Player(this);
    this.camera = new OrbitCamera(this.camera3d);
    this.camera.snap(this.player.position, this.player.yaw);
    this.objectives = new Objectives(this);
    this.ui = new UI(this);
    this.spawnWild();
    this.applySettings();
    Events.on('battleEnded', (outcome) => { if (outcome === 'Victory') { this.ui.banner('Victory!', 'Bind the exhausted creature before it recovers.'); this.sfx('victory'); } });
    Events.on('battleStarted', () => this.sfx('battleStart'));
    Events.on('spotted', (id) => this.collection.seen.add(id));
    this.canvas.addEventListener('click', () => {
      this.audio.unlock();
      if (this.state === 'playing' && !this.input.locked) this.input.requestLock();
    });
    document.addEventListener('pointerlockchange', () => {
      const locked = document.pointerLockElement === this.canvas;
      if (this.wasLocked && !locked && this.state === 'playing') this.pause();
      this.wasLocked = locked;
    });
    console.info(`[Beastbound] World generated in ${Math.round(this.world.genMs)} ms (seed ${SEED}).`);
    this.setState('title');
    document.getElementById('loading').classList.add('hidden');
    this.last = performance.now();
    requestAnimationFrame((t) => this.loop(t));
  }

  resize() {
    const w = window.innerWidth, h = window.innerHeight;
    this.renderer.setSize(w, h, false);
    if (this.postfx) this.postfx.setSize(w, h, this.renderer.getPixelRatio());
    this.camera3d.aspect = w / Math.max(1, h);
    this.camera3d.updateProjectionMatrix();
  }

  // ------------------------------------------------------------------ helpers
  sfx(name, position = null, volume = 1, pitch = 1) {
    this.audio.play(name, { position, listener: position ? { position: this.camera3d.position, yaw: this.camera.yaw } : null, volume, pitch });
  }
  hitStop(seconds) { if (this.state === 'playing') this.hitStopUntil = performance.now() / 1000 + seconds; }

  setState(s) {
    this.state = s;
    this.camera.mode = s === 'title' ? 'title' : 'game';
    if (s !== 'playing') this.input.releaseLock();
    this.ui.onState(s);
  }

  // ------------------------------------------------------------------ creatures
  spawnWild() {
    for (const c of this.creatures.filter((x) => x.isWild)) this.removeCreature(c);
    for (const n of this.world.nests) {
      if (this.collection.nests.has(n.id)) continue;
      const rec = createRecord(n.speciesId, n.level, n.personality, n.vigor, n.id);
      this.creatures.push(new Creature(this, rec, 'Wild', n, n.position, Math.random() * Math.PI * 2));
    }
  }
  spawnCompanion(record, pos, yaw) {
    this.despawnCompanion();
    const p = this.world.findWalkableNear(pos.x, pos.z, 3, 10) || pos.clone();
    const c = new Creature(this, record, 'Companion', null, p, yaw);
    this.creatures.push(c);
    return c;
  }
  despawnCompanion() { for (const c of this.creatures.filter((x) => !x.isWild)) this.removeCreature(c); }
  removeCreature(c) {
    const i = this.creatures.indexOf(c);
    if (i >= 0) this.creatures.splice(i, 1);
    c.dispose();
  }
  get companion() { return this.creatures.find((c) => !c.isWild) || null; }

  bindCreature(wild, awareness) {
    const rec = { ...wild.record, boundAt: new Date().toISOString() };
    this.collection.records.push(rec);
    this.collection.seen.add(rec.speciesId);
    if (wild.nest) this.collection.nests.add(wild.nest.id);
    this.battle.markBound(wild);
    const pos = wild.position.clone(), yaw = wild.yaw;
    this.removeCreature(wild);
    const becomesPartner = !this.collection.partner;
    if (becomesPartner) { this.collection.partnerUid = rec.uid; this.spawnCompanion(rec, pos, yaw); }
    const how = awareness === Awareness.Asleep ? ' while it slept' : awareness === Awareness.Unaware ? ' without being noticed' : '';
    Events.notify(`${wild.name} bound${how}! It joined your Bestiary.${becomesPartner ? ' It will follow you as your partner.' : ''}`, 'discovery');
    this.objectives.refresh();
    this.saveGame(true);
  }

  setPartner(uid) {
    const r = this.collection.find(uid);
    if (!r || uid === this.collection.partnerUid) return;
    this.collection.partnerUid = uid;
    this.spawnCompanion(r, this.player.position.clone().addScaledVector(this.player.forward, -2), this.player.yaw);
    Events.notify(`${r.nickname || Species[r.speciesId].name} is now your partner.`, 'success');
  }

  // ------------------------------------------------------------------ flow
  startNewGame() {
    this.audio.unlock();
    this.collection.clear();
    this.objectives.reset();
    this.battle.reset();
    this.projectiles.clear();
    this.despawnCompanion();
    this.spawnWild();
    this.player.respawn(this.world.playerSpawn, this.world.playerSpawnYaw);
    this.world.setHour(16.2); // warm late afternoon, like the reference art
    this.playTime = 0;
    this.sessionActive = true;
    this.camera.snap(this.player.position, this.player.yaw);
    this.nextAutosave = this.time + 180;
    this.enterPlay();
    Events.notify('Welcome to Sunmeadow Vale, Warden.', 'discovery');
    this.saveGame(true);
  }

  continueGame() {
    this.audio.unlock();
    const { data, error } = Saves.load();
    if (!data) { Events.notify(`Could not load your journey: ${error}`, 'warning'); return; }
    this.battle.reset();
    this.projectiles.clear();
    this.despawnCompanion();
    this.collection.load(data);
    this.spawnWild();
    let pos = new THREE.Vector3(data.player?.x || 0, data.player?.y || 0, data.player?.z || 0);
    let yaw = data.player?.yaw || 0;
    if (!this.world.insidePlayArea(pos.x, pos.z, 1) || this.world.waterDepth(pos.x, pos.z) > 0.8 || pos.lengthSq() < 0.01) { pos = this.world.playerSpawn.clone(); yaw = this.world.playerSpawnYaw; }
    pos.y = this.world.heightAt(pos.x, pos.z);
    this.player.respawn(pos, yaw);
    this.player.health.setFraction(Math.min(1, Math.max(0.25, data.player?.health ?? 1)));
    const partner = this.collection.partner;
    if (partner) this.spawnCompanion(partner, pos.clone().addScaledVector(this.player.forward, -2), yaw);
    this.objectives.step = Math.min(4, Math.max(0, data.objectiveStep | 0));
    this.objectives.bestiaryOpened = !!data.bestiaryOpened;
    this.objectives.refresh();
    this.world.setHour(Number.isFinite(data.hour) ? data.hour : 9.5);
    this.playTime = data.playTime || 0;
    this.sessionActive = true;
    this.camera.snap(this.player.position, yaw);
    this.nextAutosave = this.time + 180;
    this.enterPlay();
    Events.notify('Journey restored.', 'success');
  }

  enterPlay() {
    this.setState('playing');
    this.playStart = this.time;
    this.input.requestLock();
  }

  saveGame(silent) {
    if (!this.sessionActive) return false;
    const p = this.player;
    const err = Saves.save({
      playTime: this.playTime, hour: this.world.hour,
      player: { x: p.position.x, y: p.position.y, z: p.position.z, yaw: p.yaw, health: p.alive ? p.health.fraction : 1 },
      collection: this.collection.records, partnerUid: this.collection.partnerUid || '',
      capturedNests: [...this.collection.nests], seenSpecies: [...this.collection.seen],
      objectiveStep: this.objectives.step, bestiaryOpened: this.objectives.bestiaryOpened,
    });
    if (err) Events.notify(`Saving failed: ${err}`, 'warning');
    else if (!silent) { Events.notify('Journey saved.', 'success'); this.sfx('save'); }
    return !err;
  }

  pause() { if (this.state !== 'playing') return; this.setState('paused'); this.ui.openPause(); }
  openBestiary() { if (this.state !== 'playing') return; this.setState('paused'); this.ui.openBestiary(); }
  resume() { if (this.state !== 'paused') return; this.setState('playing'); this.input.requestLock(); }

  returnToTitle() {
    if (this.state === 'defeat') this.recoverFromDefeat();
    if (this.sessionActive) this.saveGame(true);
    this.player.cancelBinding();
    this.battle.reset();
    for (const c of this.creatures) c.brain.resetToCalm();
    this.sessionActive = false;
    this.setState('title');
  }

  retryAfterDefeat() {
    this.recoverFromDefeat();
    this.enterPlay();
    Events.notify('The Waystone restored your strength.');
  }

  recoverFromDefeat() {
    this.battle.reset();
    this.projectiles.clear();
    for (const c of this.creatures) c.brain.resetToCalm();
    this.player.respawn(this.world.playerSpawn, this.world.playerSpawnYaw);
    const comp = this.companion;
    if (comp) comp.teleport(this.world.playerSpawn.clone().add(new THREE.Vector3(1.5, 0, -1.5)));
    this.camera.snap(this.player.position, this.player.yaw);
  }

  onPlayerDefeated() {
    this.player.cancelBinding();
    this.battle.reset();
    this.sfx('defeat');
    this.setState('defeat');
  }

  applySettings() {
    const s = this.settings;
    this.camera.sensitivity = s.mouseSensitivity; this.camera.invertY = s.invertY; this.camera.baseFov = s.fov;
    this.audio.setVolumes({ master: s.master, sfx: s.sfx, music: s.music });
    this.world.dayLengthMinutes = s.dayLength;
    const q = s.quality, dpr = window.devicePixelRatio || 1;
    this.renderer.setPixelRatio([Math.min(dpr, 1) * 0.75, Math.min(dpr, 1), Math.min(dpr, 1.5), Math.min(dpr, 2)][q]);
    this.renderer.shadowMap.enabled = q > 0;
    const size = [512, 1024, 2048, 4096][q];
    if (this.world.sun.shadow.mapSize.x !== size) {
      this.world.sun.shadow.mapSize.set(size, size);
      if (this.world.sun.shadow.map) { this.world.sun.shadow.map.dispose(); this.world.sun.shadow.map = null; }
    }
    this.grassLayers = [1, 2, 3, 3][q];
    this.grassDistance = [40, 50, 70, 100][q];
    this.scene.fog.density = ArtStyle.stylized ? 0.0027 : 0.0036;
    this.renderer.toneMappingExposure = ArtStyle.stylized ? 0.6 : 0.55;
    this.postfx.setQuality(q);
    this.resize();
  }
  saveSettings() {
    Saves.saveSettings(this.settings);
    if (this.settings.artStyle !== this.bootArtStyle) {
      // Shaders are built for one art style, so switching restarts the page (progress is saved first).
      if (this.state === 'playing' || this.state === 'paused') this.saveGame(true);
      window.location.reload();
    }
  }

  // ------------------------------------------------------------------ loop
  loop(now) {
    requestAnimationFrame((t) => this.loop(t));
    const cap = FrameCaps[this.settings.frameCap];
    if (cap > 0 && now - this.lastRender < 1000 / cap - 1) return;
    this.lastRender = now;
    this.watchPerformance(now);
    let realDt = Math.min(0.05, Math.max(0, (now - this.last) / 1000));
    this.last = now;
    this.frame++;
    const input = this.input;
    input.poll();

    // Global keys
    if (this.state === 'playing' && this.ui.changedAt !== this.frame) {
      if (input.pausePressed) this.pause();
      else if (input.bestiaryPressed) this.openBestiary();
      else if (input.helpPressed) this.ui.hintExpanded = !this.ui.hintExpanded;
    }
    this.ui.handleMenuInput();

    let scale = 1;
    if (this.state === 'paused') scale = 0;
    else if (this.state === 'defeat') scale = 0.3;
    else if (performance.now() / 1000 < this.hitStopUntil) scale = 0.05;
    const dt = realDt * scale;
    this.dt = dt;
    this.time += dt;

    if (dt > 0) {
      if (this.state === 'playing') this.playTime += dt;
      this.player.update(dt);
      for (const c of [...this.creatures]) c.update(dt);
      this.separate();
      this.projectiles.update(dt);
      this.objectives.update();
      if (this.state === 'playing' && !this.player.alive) this.onPlayerDefeated();
      if (this.state === 'playing' && this.time > this.nextAutosave) { this.nextAutosave = this.time + 180; this.saveGame(true); }
    }
    this.camera.update(realDt, this);
    const focus = this.state === 'title' ? this.world.playerSpawn : this.player.position;
    this.updateGrassPushers();
    this.world.update(dt > 0 ? dt : (this.state === 'title' ? realDt : 0), this.camera3d.position, focus, this.grassLayers, this.grassDistance);
    this.audio.update(realDt, this.battle.inBattle && this.state === 'playing', this.world.daylight < 0.3, this.state === 'paused' || this.state === 'defeat');
    // Let foliage between the camera and the Warden dissolve instead of hiding them.
    this.camera3d.updateMatrixWorld();
    if (this.state === 'title') occlusionUniforms.uFocusView.value.set(0, 0, 0);
    else occlusionUniforms.uFocusView.value.copy(this.player.position).setY(this.player.position.y + 1.2).applyMatrix4(this.camera3d.matrixWorldInverse);
    if (this.postfx && this.postfx.enabled) {
      this.postfx.update(this.world.realSunDir, this.world.sunColor, this.world.daylight);
      this.postfx.render();
    } else this.renderer.render(this.scene, this.camera3d);
    this.ui.updateHud(realDt);
    this.ui.renderPortrait(realDt);
    input.endFrame();
  }

  /** If play stays well below the target frame rate for 5 s, step graphics quality down once (can be disabled). */
  watchPerformance(now) {
    const p = this.perf || (this.perf = { start: now, frames: 0, slow: 0 });
    if (this.state !== 'playing' || document.hidden) { p.start = now; p.frames = 0; return; }
    p.frames++;
    if (now - p.start < 1000) return;
    const fps = p.frames * 1000 / (now - p.start);
    const cap = FrameCaps[this.settings.frameCap];
    const target = Math.min(cap > 0 ? cap : 60, 60) * 0.7;
    p.slow = fps < target ? p.slow + 1 : Math.min(0, p.slow + 1);
    p.start = now; p.frames = 0;
    if (p.slow >= 5 && this.settings.autoQuality && this.settings.quality > 0) {
      this.settings.quality--;
      this.applySettings();
      Saves.saveSettings(this.settings);
      Events.notify(`Graphics lowered to ${QualityNames[this.settings.quality]} to keep things smooth (Settings → Graphics quality).`);
      p.slow = -10; // give the new setting time to settle before judging again
    }
  }

  /** Grass parts around the Warden (and stays bent along a short trail) and around nearby creatures. */
  updateGrassPushers() {
    const list = [];
    if (this.state !== 'title') {
      const p = this.player.position;
      list.push({ x: p.x, y: p.y, z: p.z, r: 0.85 });
      const trail = this.grassTrail || (this.grassTrail = []);
      if (!trail.length || Math.hypot(trail[0].x - p.x, trail[0].z - p.z) > 0.45) trail.unshift({ x: p.x, y: p.y, z: p.z, t: this.time });
      while (trail.length > 6 || (trail.length && this.time - trail[trail.length - 1].t > 2.2)) trail.pop();
      for (const q of trail) list.push({ x: q.x, y: q.y, z: q.z, r: 0.8 * (1 - (this.time - q.t) / 2.2) });
    }
    const cam = this.camera3d.position;
    const near = this.creatures.filter((c) => c.position.distanceToSquared(cam) < 35 * 35)
      .sort((a, b) => a.position.distanceToSquared(cam) - b.position.distanceToSquared(cam));
    for (const c of near) {
      if (list.length >= 16) break;
      list.push({ x: c.position.x, y: c.position.y, z: c.position.z, r: (c.species.radius || 0.42) * 1.9 });
    }
    this.world.setGrassPushers(list);
  }

  /** Keeps the player and creatures from overlapping (simple circle separation). */
  separate() {
    const ents = [this.player, ...this.creatures];
    for (let i = 0; i < ents.length; i++) {
      for (let j = i + 1; j < ents.length; j++) {
        const a = ents[i], b = ents[j];
        const dx = b.position.x - a.position.x, dz = b.position.z - a.position.z;
        const d = Math.hypot(dx, dz), min = a.radius + b.radius;
        if (d < min && d > 1e-4) {
          const push = (min - d) / 2, nx = dx / d, nz = dz / d;
          a.position.x -= nx * push; a.position.z -= nz * push;
          b.position.x += nx * push; b.position.z += nz * push;
        }
      }
    }
  }
}

// ---------------------------------------------------------------------- start
function fail(message) {
  const t = document.getElementById('loading-text');
  if (t) t.innerHTML = message;
  document.querySelector('#loading .bar')?.classList.add('hidden');
}

window.addEventListener('error', (e) => console.error('[Beastbound]', e.message));
try {
  const probe = document.createElement('canvas');
  if (!(probe.getContext('webgl2') || probe.getContext('webgl'))) throw new Error('WebGL is not available');
  const game = new Game();
  window.beastbound = game; // handy for debugging in the browser console
  const t = document.getElementById('loading-text');
  if (t) t.textContent = 'Waking the creatures…';
  Models.preload().finally(() => setTimeout(() => {
    if (t) t.textContent = 'Growing Sunmeadow Vale…';
    try { game.boot(); } catch (e) { console.error(e); fail(`Beastbound could not start: ${e.message}`); }
  }, 30));
} catch (e) {
  console.error(e);
  fail('Beastbound needs WebGL. Please use a recent desktop browser (Chrome, Edge, Firefox or Safari) with hardware acceleration enabled.');
}
