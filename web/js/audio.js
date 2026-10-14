// Procedural audio (port of Runtime/Audio): every sound is synthesised at start-up, no audio files.
const RATE = 22050;

let seed = 12345;
const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
const noise = () => rnd() * 2 - 1;
const sine = (p) => Math.sin(p * Math.PI * 2);
const tri = (p) => 1 - 4 * Math.abs(((p + 0.25) % 1 + 1) % 1 - 0.5);
const env = (t, attack, pow = 2) => (t < attack ? t / Math.max(1e-4, attack) : Math.pow(1 - Math.min(1, (t - attack) / (1 - attack)), pow));
const noteHz = (s) => 440 * Math.pow(2, s / 12);
const buf = (sec) => new Float32Array(Math.max(1, Math.floor(sec * RATE)));
function lowPass(d, hz) {
  const rc = 1 / (2 * Math.PI * hz), a = (1 / RATE) / (rc + 1 / RATE);
  let y = 0;
  for (let i = 0; i < d.length; i++) { y += a * (d[i] - y); d[i] = y; }
}
function normalize(d, peak) {
  let m = 0;
  for (let i = 0; i < d.length; i++) m = Math.max(m, Math.abs(d[i]));
  if (m > 1e-4) for (let i = 0; i < d.length; i++) d[i] *= peak / m;
  return d;
}
function makeLoop(d, fade) {
  fade = Math.max(1, Math.min(fade, Math.floor(d.length / 2)));
  const len = d.length - fade, out = d.slice(0, len);
  for (let i = 0; i < fade; i++) { const k = i / fade; out[i] = d[i] * k + d[len + i] * (1 - k); }
  return out;
}

function thump(sec, freq, cutoff, mix) {
  const d = buf(sec), n = buf(sec);
  for (let i = 0; i < n.length; i++) n[i] = noise();
  lowPass(n, cutoff);
  let ph = 0;
  for (let i = 0; i < d.length; i++) { const t = i / d.length; ph += freq * (1 - 0.5 * t) / RATE; d[i] = (sine(ph) * 0.6 + n[i] * mix * 3) * env(t, 0.02, 3); }
  return d;
}
function noiseBurst(sec, cutoff, attack, decay) {
  const d = buf(sec);
  for (let i = 0; i < d.length; i++) d[i] = noise();
  lowPass(d, cutoff);
  for (let i = 0; i < d.length; i++) d[i] *= env(i / d.length, attack, decay);
  return d;
}
function noiseSweep(sec, from, to) {
  const d = buf(sec); let y = 0;
  for (let i = 0; i < d.length; i++) {
    const t = i / d.length, c = from + (to - from) * t, rc = 1 / (2 * Math.PI * c), a = (1 / RATE) / (rc + 1 / RATE);
    y += a * (noise() - y); d[i] = y * Math.sin(t * Math.PI);
  }
  return d;
}
function sweep(sec, from, to, shimmer, sparkle) {
  const d = buf(sec); let ph = 0;
  for (let i = 0; i < d.length; i++) {
    const t = i / d.length; ph += (from + (to - from) * t * t) / RATE;
    let v = tri(ph) * 0.7 + sine(ph * 2) * shimmer * 0.3;
    if (sparkle) v += sine(ph * 3.01) * 0.15 * Math.sin(t * 40);
    d[i] = v * env(t, 0.05, 2);
  }
  return d;
}
function impact(sec, freq, weight) {
  const d = buf(sec); let ph = 0;
  for (let i = 0; i < d.length; i++) {
    const t = i / d.length; ph += freq * (1 - 0.6 * t) / RATE;
    const click = i < RATE * 0.006 ? noise() : 0;
    d[i] = (sine(ph) * weight + noise() * 0.35 * (1 - t) + click) * env(t, 0.01, 3);
  }
  lowPass(d, 3000);
  return d;
}
function chime(sec, semis, gap) {
  const d = buf(sec);
  semis.forEach((s, n) => {
    const hz = noteHz(s + 3), start = Math.floor(n * gap * RATE); let ph = 0;
    for (let i = start; i < d.length; i++) {
      const t = (i - start) / (d.length - start); ph += hz / RATE;
      d[i] += (sine(ph) * 0.6 + sine(ph * 2) * 0.25 + sine(ph * 3) * 0.1) * env(t, 0.01, 2.5);
    }
  });
  return d;
}
function chirp(sec, from, to, syl) {
  const d = buf(sec); let ph = 0;
  for (let i = 0; i < d.length; i++) {
    const t = i / d.length, local = (t * syl) % 1;
    ph += (from + (to - from) * Math.sin(local * Math.PI)) / RATE;
    d[i] = (sine(ph * (1 + 0.03 * Math.sin(t * 60))) * 0.8 + sine(ph * 2) * 0.2) * env(local, 0.1, 1.5);
  }
  return d;
}
function blip(sec, hz) {
  const d = buf(sec); let ph = 0;
  for (let i = 0; i < d.length; i++) { const t = i / d.length; ph += hz / RATE; d[i] = sine(ph) * env(t, 0.05, 2); }
  return d;
}

const SFX = {
  footstep: () => thump(0.09, 140, 900, 0.7), land: () => thump(0.16, 90, 700, 1), splash: () => noiseBurst(0.22, 2500, 0.02, 1.5),
  jump: () => sweep(0.12, 300, 520, 0.3, false), swing: () => noiseSweep(0.18, 600, 3500), hit: () => impact(0.14, 180, 0.6),
  hitHeavy: () => impact(0.24, 110, 1), spark: () => sweep(0.28, 400, 1400, 0.5, true), sparkImpact: () => noiseBurst(0.25, 4000, 0.005, 2.5),
  dodge: () => noiseSweep(0.16, 2500, 800), bindThrow: () => sweep(0.3, 500, 900, 0.4, true), bindShake: () => chime(0.25, [7], 0.08),
  bindSuccess: () => chime(1.1, [3, 7, 10, 15], 0.09), bindFail: () => chime(0.6, [3, -2, -6], 0.12),
  chirp: () => chirp(0.22, 900, 1400, 2), alert: () => chirp(0.3, 1200, 2000, 1), hurt: () => chirp(0.2, 1100, 600, 1),
  windup: () => sweep(0.4, 200, 420, 0.6, false), pebble: () => noiseSweep(0.15, 1200, 2400), uiClick: () => blip(0.06, 880),
  uiMove: () => blip(0.04, 660), uiOpen: () => chime(0.25, [0, 7], 0.05), uiBack: () => chime(0.2, [7, 0], 0.05),
  victory: () => chime(1.4, [0, 4, 7, 12, 16], 0.11), defeat: () => chime(1.4, [0, -3, -7, -12], 0.2), discovery: () => chime(0.9, [7, 12, 19], 0.1),
  save: () => chime(0.4, [12, 19], 0.06), battleStart: () => chime(0.5, [0, 6, 12], 0.05),
};

function dayAmbience() {
  const sec = 14, d = buf(sec + 1), w = buf(sec + 1);
  for (let i = 0; i < w.length; i++) w[i] = noise();
  lowPass(w, 400); lowPass(w, 400);
  for (let i = 0; i < d.length; i++) { const t = i / RATE; d[i] = w[i] * 4 * (0.55 + 0.45 * Math.sin(t * 0.45) * Math.sin(t * 0.17 + 1)); }
  for (let b = 0; b < 9; b++) {
    const start = Math.floor(rnd() * (d.length - RATE)), base = 2200 + rnd() * 1800, notes = 2 + Math.floor(rnd() * 4);
    for (let n = 0; n < notes; n++) {
      const s0 = start + Math.floor(n * 0.11 * RATE), len = Math.floor(0.08 * RATE); let ph = 0;
      for (let i = 0; i < len && s0 + i < d.length; i++) { const t = i / len; ph += base * (1 + 0.25 * Math.sin(t * Math.PI)) / RATE; d[s0 + i] += sine(ph) * 0.12 * Math.sin(t * Math.PI); }
    }
  }
  return normalize(makeLoop(d, RATE), 0.6);
}
function nightAmbience() {
  const d = buf(11);
  for (let i = 0; i < d.length; i++) d[i] = noise();
  lowPass(d, 250);
  let ph = 0;
  for (let i = 0; i < d.length; i++) {
    const t = i / RATE; ph += 4400 / RATE;
    d[i] = d[i] * 3 + sine(ph) * 0.05 * ((t * 14) % 1 < 0.45 ? 1 : 0) * ((t * 0.7) % 1 < 0.5 ? 1 : 0.15);
  }
  return normalize(makeLoop(d, RATE), 0.5);
}
function exploreMusic() {
  const chords = [[-9, -5, -2, 3], [-12, -9, -5, 0], [-16, -12, -9, -4], [-14, -10, -7, -2]];
  const cl = 4, d = buf(cl * chords.length + 1);
  chords.forEach((ch, c) => {
    const start = Math.floor(c * cl * RATE), len = Math.floor((cl + 1) * RATE);
    for (const note of ch) {
      const hz = noteHz(note); let ph = 0;
      for (let i = 0; i < len && start + i < d.length; i++) { ph += hz / RATE; d[start + i] += (tri(ph) * 0.5 + sine(ph * 2) * 0.15) * Math.sin((i / len) * Math.PI) * 0.12; }
    }
    for (let k = 0; k < 8; k++) {
      const hz = noteHz(ch[k % 4] + 12), s0 = start + Math.floor(k * cl / 8 * RATE), pl = Math.floor(0.6 * RATE); let ph = 0;
      for (let i = 0; i < pl && s0 + i < d.length; i++) { ph += hz / RATE; d[s0 + i] += sine(ph) * Math.exp(-(i / pl) * 6) * 0.08; }
    }
  });
  lowPass(d, 3500);
  return normalize(makeLoop(d, RATE), 0.55);
}
function battleMusic() {
  const beat = 60 / 132, beats = 16, tail = 0.05, d = buf(beat * beats + tail);
  const bass = [-21, -21, -21, -21, -26, -26, -24, -24, -21, -21, -21, -21, -28, -28, -26, -24];
  for (let b = 0; b < beats; b++) {
    const s0 = Math.floor(b * beat * RATE), kl = Math.floor(0.18 * RATE); let ph = 0;
    for (let i = 0; i < kl && s0 + i < d.length; i++) { const t = i / kl; ph += (120 + (45 - 120) * t) / RATE; d[s0 + i] += sine(ph) * (1 - t) * (1 - t) * 0.5; }
    const h0 = s0 + Math.floor(beat * 0.5 * RATE), hl = Math.floor(0.05 * RATE);
    for (let i = 0; i < hl && h0 + i < d.length; i++) d[h0 + i] += noise() * (1 - i / hl) * 0.12;
    const hz = noteHz(bass[b]);
    for (let e = 0; e < 2; e++) {
      const e0 = s0 + Math.floor(e * beat * 0.5 * RATE), el = Math.floor(beat * 0.45 * RATE); let p2 = 0;
      for (let i = 0; i < el && e0 + i < d.length; i++) { p2 += hz / RATE; d[e0 + i] += tri(p2) * (1 - i / el) * 0.22; }
    }
  }
  [0, 3, 7, 10, 7, 3, 5, 2].forEach((n, r) => {
    const s0 = Math.floor(r * beat * 2 * RATE), len = Math.floor(beat * 1.6 * RATE), hz = noteHz(n - 9); let ph = 0;
    for (let i = 0; i < len && s0 + i < d.length; i++) { ph += hz / RATE; d[s0 + i] += (tri(ph) * 0.6 + sine(ph * 2) * 0.2) * env(i / len, 0.03, 1.5) * 0.1; }
  });
  lowPass(d, 5000);
  return normalize(makeLoop(d, Math.floor(tail * RATE)), 0.6);
}

export class AudioSystem {
  constructor() {
    this.ctx = null; this.buffers = {}; this.volumes = { master: 0.8, sfx: 0.9, music: 0.6 };
    this.loops = {}; this.battle = 0; this.night = 0; this.ready = false;
  }

  /** Must be called from a user gesture (browser autoplay rules). */
  unlock() {
    if (this.ctx) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    this.ctx = new AC();
    this.master = this.ctx.createGain(); this.master.connect(this.ctx.destination);
    this.sfxGain = this.ctx.createGain(); this.sfxGain.connect(this.master);
    this.musicGain = this.ctx.createGain(); this.musicGain.connect(this.master);
    const toBuffer = (data) => { const b = this.ctx.createBuffer(1, data.length, RATE); b.copyToChannel(data, 0); return b; };
    for (const [k, f] of Object.entries(SFX)) this.buffers[k] = toBuffer(normalize(f(), 0.9));
    const loop = (name, data, bus) => {
      const src = this.ctx.createBufferSource(); src.buffer = toBuffer(data); src.loop = true;
      const g = this.ctx.createGain(); g.gain.value = 0; src.connect(g); g.connect(bus); src.start();
      this.loops[name] = g;
    };
    loop('day', dayAmbience(), this.sfxGain);
    loop('night', nightAmbience(), this.sfxGain);
    loop('explore', exploreMusic(), this.musicGain);
    loop('battle', battleMusic(), this.musicGain);
    this.ready = true;
    this.setVolumes(this.volumes);
  }

  setVolumes(v) {
    this.volumes = { ...this.volumes, ...v };
    if (!this.ctx) return;
    this.master.gain.value = this.volumes.master;
    this.sfxGain.gain.value = this.volumes.sfx;
    this.musicGain.gain.value = this.volumes.music;
  }

  /** listener = {position, yaw} for simple distance attenuation + stereo panning. */
  play(name, opts = {}) {
    if (!this.ready || !this.buffers[name]) return;
    const { position = null, listener = null, volume = 1, pitch = 1 } = opts;
    let gainValue = volume, pan = 0;
    if (position && listener) {
      const dx = position.x - listener.position.x, dz = position.z - listener.position.z;
      const d = Math.hypot(dx, dz);
      gainValue *= Math.max(0, 1 - Math.max(0, d - 2) / 38);
      if (gainValue <= 0.005) return;
      const rightX = Math.cos(listener.yaw), rightZ = -Math.sin(listener.yaw);
      pan = d > 0.5 ? Math.max(-1, Math.min(1, (dx * rightX + dz * rightZ) / d)) * 0.7 : 0;
    }
    const src = this.ctx.createBufferSource();
    src.buffer = this.buffers[name];
    src.playbackRate.value = pitch;
    const g = this.ctx.createGain(); g.gain.value = gainValue;
    let node = g;
    if (this.ctx.createStereoPanner) { const p = this.ctx.createStereoPanner(); p.pan.value = pan; g.connect(p); node = p; }
    src.connect(g); node.connect(this.sfxGain);
    src.start();
  }

  update(dt, battle, night, duck) {
    if (!this.ready) return;
    this.battle += Math.sign((battle ? 1 : 0) - this.battle) * Math.min(Math.abs((battle ? 1 : 0) - this.battle), dt * 0.8);
    this.night += Math.sign((night ? 1 : 0) - this.night) * Math.min(Math.abs((night ? 1 : 0) - this.night), dt * 0.2);
    const k = duck ? 0.5 : 1;
    this.loops.explore.gain.value = 0.55 * (1 - this.battle) * k;
    this.loops.battle.gain.value = 0.6 * this.battle * k;
    this.loops.day.gain.value = 0.5 * (1 - this.night);
    this.loops.night.gain.value = 0.5 * this.night;
  }
}
