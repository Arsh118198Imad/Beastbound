// DOM-based UI: HUD, menu stack, settings, Bestiary with live 3D portrait, keyboard/gamepad navigation.
import * as THREE from 'three';
import { Events } from './events.js';
import { StatRules, Species, CaptureRules } from './rules.js';
import { buildCreatureParts, createAnimator } from './creatures.js';
import { QualityNames, FrameCaps, defaultSettings, Saves } from './save.js';
import { Navigation } from './hud/navigation.js';
import { ArtStyles } from './render/style.js';

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

export class UI {
  constructor(game) {
    this.game = game;
    this.stack = [];
    this.changedAt = -1;
    this.hintExpanded = false;
    this.markers = [];
    this.nav = new Navigation(game, $('#compass'), $('#minimap'));
    this.damage = [];
    this.fps = { frames: 0, time: 0 };
    this.el = {
      hud: $('#hud'), title: $('#title'), pause: $('#pause'), settings: $('#settings'), controls: $('#controls'),
      bestiary: $('#bestiary'), confirm: $('#confirm'), defeat: $('#defeat'),
    };
    this.wireButtons();
    Events.on('notify', (m, k) => this.toast(m, k));
    Events.on('damaged', (h, info) => this.damageNumber(h, info));
  }

  // ------------------------------------------------------------------ wiring
  wireButtons() {
    const g = this.game;
    const click = (el, fn) => el.addEventListener('click', () => { g.audio.unlock(); g.sfx('uiClick'); fn(); });
    click($('#btn-continue'), () => g.continueGame());
    click($('#btn-new'), () => {
      if (Saves.hasSave()) this.confirm('Start a new journey?', 'Your existing journey will be replaced the next time the game saves.', 'Start New Journey', () => g.startNewGame());
      else g.startNewGame();
    });
    click($('#btn-settings'), () => this.push('settings'));
    click($('#btn-controls'), () => this.push('controls'));
    for (const b of document.querySelectorAll('#ui [data-act]')) {
      click(b, () => {
        const a = b.dataset.act;
        if (a === 'resume') this.closeAllAndResume();
        else if (a === 'bestiary') this.openBestiary();
        else if (a === 'settings') this.push('settings');
        else if (a === 'controls') this.push('controls');
        else if (a === 'save') g.saveGame(false);
        else if (a === 'restart') this.confirm('Restart from the beginning?', 'All bound creatures and progress in this journey will be reset.', 'Restart', () => g.startNewGame());
        else if (a === 'title') g.returnToTitle();
        else if (a === 'back') this.pop();
        else if (a === 'yes') { const f = this.onYes; this.pop(); if (f) f(); }
        else if (a === 'retry') g.retryAfterDefeat();
        else if (a === 'partner') this.setPartner();
        else if (a === 'reset-settings') { Object.assign(g.settings, defaultSettings()); g.applySettings(); this.buildSettings(); }
      });
    }
    for (const b of document.querySelectorAll('#ui button')) {
      b.addEventListener('mouseenter', () => { b.focus({ preventScroll: true }); });
      b.addEventListener('focus', () => g.sfx('uiMove', null, 0.35));
    }
  }

  // ------------------------------------------------------------------ menu stack
  show(name, on) { this.el[name].classList.toggle('hidden', !on); }

  push(name) {
    if (this.stack.length) this.show(this.stack[this.stack.length - 1], false);
    this.stack = this.stack.filter((s) => s !== name);
    this.stack.push(name);
    if (name === 'settings') this.buildSettings();
    if (name === 'bestiary') this.buildBestiary();
    this.show(name, true);
    this.changedAt = this.game.frame;
    this.game.sfx('uiOpen', null, 0.5);
    this.focusFirst();
  }

  pop() {
    if (!this.stack.length) return;
    const top = this.stack.pop();
    this.show(top, false);
    if (top === 'settings') this.game.saveSettings();
    this.changedAt = this.game.frame;
    this.game.sfx('uiBack', null, 0.5);
    if (this.stack.length) { this.show(this.stack[this.stack.length - 1], true); this.focusFirst(); }
    else if (this.game.state === 'paused') this.game.resume();
    else if (this.game.state === 'title') this.focusFirst();
  }

  closeAll() { for (const s of this.stack) this.show(s, false); this.stack = []; this.changedAt = this.game.frame; }
  closeAllAndResume() { this.closeAll(); this.game.resume(); }
  get menuOpen() { return this.stack.length > 0; }

  topElement() {
    if (this.stack.length) return this.el[this.stack[this.stack.length - 1]];
    if (this.game.state === 'title') return this.el.title;
    if (this.game.state === 'defeat') return this.el.defeat;
    return null;
  }
  focusables() {
    const top = this.topElement();
    if (!top) return [];
    return Array.from(top.querySelectorAll('button, input')).filter((e) => !e.disabled && e.offsetParent !== null);
  }
  focusFirst() {
    const f = this.focusables();
    if (f.length) f[0].focus({ preventScroll: true });
  }

  confirm(title, message, yes, onYes) {
    $('#confirm .ct').textContent = title;
    $('#confirm .cm').textContent = message;
    $('#confirm [data-act=yes]').textContent = yes;
    this.onYes = onYes;
    this.push('confirm');
  }

  onState(state) {
    const g = this.game;
    this.closeAll();
    this.show('hud', state === 'playing' || state === 'paused');
    this.el.hud.style.visibility = state === 'playing' ? 'visible' : 'hidden';
    this.show('title', state === 'title');
    this.show('defeat', state === 'defeat');
    if (state === 'title') {
      $('#btn-continue').classList.toggle('hidden', !Saves.hasSave());
      $('#title-status').textContent = Saves.available ? '' : 'Your browser is blocking storage, so progress cannot be saved.';
      this.focusFirst();
    }
    if (state === 'defeat') setTimeout(() => this.focusFirst(), 50);
    if (state === 'paused') this.el.hud.style.visibility = 'hidden';
    g.input.gameplayEnabled = state === 'playing';
  }

  openPause() { this.push('pause'); }
  openBestiary() { this.push('bestiary'); Events.emit('bestiaryOpened'); }

  // ------------------------------------------------------------------ per-frame input for menus
  handleMenuInput() {
    const g = this.game, input = g.input;
    if (this.stack.length && this.changedAt !== g.frame) {
      const top = this.stack[this.stack.length - 1];
      if ((top === 'bestiary' && input.bestiaryPressed) || (input.padPressed(9) && g.state === 'paused')) { this.closeAllAndResume(); return; }
      if (input.cancelPressed) { this.pop(); return; }
    }
    // Keyboard arrows / D-pad / stick move focus; A activates.
    const list = this.focusables();
    if (!list.length) return;
    let dir = 0;
    if (input.keyPressed('ArrowDown') || input.padPressed(13)) dir = 1;
    if (input.keyPressed('ArrowUp') || input.padPressed(12)) dir = -1;
    const ay = input.gamepadAxes[1] || 0;
    if (Math.abs(ay) > 0.6 && (!this.stickHeld || performance.now() - this.stickHeld > 280)) { dir = ay > 0 ? 1 : -1; this.stickHeld = performance.now(); }
    if (Math.abs(ay) < 0.3) this.stickHeld = 0;
    const active = document.activeElement;
    let idx = list.indexOf(active);
    if (dir) {
      idx = idx < 0 ? 0 : (idx + dir + list.length) % list.length;
      list[idx].focus({ preventScroll: false });
    }
    if (input.padPressed(0) && active && list.includes(active)) {
      if (active.type === 'checkbox') { active.checked = !active.checked; active.dispatchEvent(new Event('change')); }
      else active.click();
    }
    if (active && active.type === 'range' && (input.padPressed(14) || input.padPressed(15))) {
      const step = (parseFloat(active.max) - parseFloat(active.min)) / 20;
      active.value = parseFloat(active.value) + (input.padPressed(15) ? step : -step);
      active.dispatchEvent(new Event('input'));
    }
    if (active && active.classList.contains('cycle') && (input.padPressed(14) || input.padPressed(15) || input.keyPressed('ArrowLeft') || input.keyPressed('ArrowRight'))) {
      active.dispatchEvent(new CustomEvent('cycle', { detail: input.padPressed(14) || input.keyPressed('ArrowLeft') ? -1 : 1 }));
    }
  }

  // ------------------------------------------------------------------ settings
  buildSettings() {
    const g = this.game, s = g.settings, rows = $('#settings-rows');
    rows.innerHTML = '';
    const apply = () => g.applySettings();
    const slider = (label, key, min, max, step, fmt) => {
      const row = document.createElement('div'); row.className = 'row';
      row.innerHTML = `<label>${label}</label><input type="range" min="${min}" max="${max}" step="${step}" value="${s[key]}"><span class="val">${fmt(s[key])}</span>`;
      const input = row.querySelector('input'), val = row.querySelector('.val');
      input.addEventListener('input', () => { s[key] = parseFloat(input.value); val.textContent = fmt(s[key]); apply(); });
      rows.appendChild(row);
    };
    const toggle = (label, key) => {
      const row = document.createElement('div'); row.className = 'row';
      row.innerHTML = `<label>${label}</label><input type="checkbox" ${s[key] ? 'checked' : ''}><span></span>`;
      const input = row.querySelector('input');
      input.addEventListener('change', () => { s[key] = input.checked; apply(); g.sfx('uiClick'); });
      rows.appendChild(row);
    };
    const cycle = (label, key, names) => {
      const row = document.createElement('div'); row.className = 'row';
      row.innerHTML = `<label>${label}</label><button class="btn cycle"></button><span></span>`;
      const b = row.querySelector('button');
      const render = () => { b.textContent = `‹  ${names[s[key]]}  ›`; };
      const step = (d) => { s[key] = (s[key] + d + names.length) % names.length; render(); apply(); g.sfx('uiClick'); };
      b.addEventListener('click', () => step(1));
      b.addEventListener('cycle', (e) => step(e.detail));
      b.addEventListener('mouseenter', () => b.focus({ preventScroll: true }));
      render();
      rows.appendChild(row);
    };
    const pct = (v) => `${Math.round(v * 100)}%`;
    slider('Mouse sensitivity', 'mouseSensitivity', 0.2, 8, 0.1, (v) => v.toFixed(1));
    toggle('Invert camera Y', 'invertY');
    slider('Field of view', 'fov', 50, 100, 1, (v) => `${Math.round(v)}°`);
    slider('Master volume', 'master', 0, 1, 0.01, pct);
    slider('Effects & ambience', 'sfx', 0, 1, 0.01, pct);
    slider('Music', 'music', 0, 1, 0.01, pct);
    cycle('Art style (reloads)', 'artStyle', ArtStyles);
    cycle('Graphics quality', 'quality', QualityNames);
    cycle('Frame cap', 'frameCap', FrameCaps.map((c) => (c ? `${c} FPS` : 'Unlimited (display rate)')));
    slider('Day length (minutes)', 'dayLength', 2, 120, 1, (v) => `${Math.round(v)}`);
    toggle('Show FPS counter', 'showFps');
    toggle('Show controls hint', 'showHint');
  }

  // ------------------------------------------------------------------ bestiary
  buildBestiary() {
    const g = this.game, root = this.el.bestiary;
    const list = $('.list', root), empty = $('.empty', root), layout = $('.layout', root);
    const records = g.collection.records;
    $('.count', root).textContent = `Bound: ${records.length}/${g.world.nests.length} in this vale  ·  Species discovered: ${g.collection.seen.size}/${Object.keys(Species).length}`;
    list.innerHTML = '';
    const partnerBtn = $('[data-act=partner]', root);
    if (!records.length) {
      layout.classList.add('hidden'); partnerBtn.classList.add('hidden'); empty.classList.remove('hidden');
      empty.innerHTML = `No creatures bound yet.<br><br>Find a wild Pebbloom, sneak up or weaken it,<br>then press <kbd>${g.input.prompt('Bind')}</kbd> to throw a Bind Orb.`;
      return;
    }
    layout.classList.remove('hidden'); partnerBtn.classList.remove('hidden'); empty.classList.add('hidden');
    for (const r of records) {
      const b = document.createElement('button');
      b.className = 'btn';
      b.innerHTML = `${esc(r.nickname || Species[r.speciesId].name)} · Lv ${r.level}${r.uid === g.collection.partnerUid ? ' <span style="color:var(--nature)">(Partner)</span>' : ''}`;
      b.addEventListener('click', () => this.selectRecord(r.uid));
      b.addEventListener('focus', () => this.selectRecord(r.uid));
      b.addEventListener('mouseenter', () => b.focus({ preventScroll: true }));
      list.appendChild(b);
    }
    if (!this.selectedUid || !records.find((r) => r.uid === this.selectedUid)) this.selectedUid = g.collection.partnerUid || records[0].uid;
    this.selectRecord(this.selectedUid);
  }

  selectRecord(uid) {
    const g = this.game, root = this.el.bestiary;
    const r = g.collection.records.find((x) => x.uid === uid);
    if (!r) return;
    this.selectedUid = uid;
    const sp = Species[r.speciesId];
    const st = StatRules.compute(sp.base, r.level, r.personality, r.vigor);
    const stars = StatRules.vigorStars(r.vigor);
    $('.name', root).textContent = r.nickname || sp.name;
    $('.sub', root).textContent = `${sp.name} · ${sp.element} · Level ${r.level} · Vigor ${'★'.repeat(stars)}${'☆'.repeat(3 - stars)}`;
    const bar = (k, v, max, hp) => `<div class="stat"><span>${k}</span><div class="track"><div class="fill ${hp ? 'hp' : ''}" style="width:${Math.min(100, v / max * 100)}%"></div></div><span>${v}</span></div>`;
    $('.stats', root).innerHTML = bar('HP', st.maxHealth, 150, true) + bar('ATK', st.attack, 40) + bar('DEF', st.defense, 40) + bar('SPD', st.speed, 40);
    $('.personality', root).textContent = StatRules.describe(r.personality);
    const kinds = { melee: 'Close', charge: 'Charge', projectile: 'Ranged' };
    $('.moves', root).innerHTML = sp.attacks.map((a) => `<div class="move"><span class="el ${a.element.toLowerCase()}">${esc(a.element)}</span><b>${esc(a.name)}</b><small>${kinds[a.kind] || a.kind} · Power ${a.power}</small></div>`).join('');
    $('.desc', root).innerHTML = `${esc(sp.description)}<br><br><i>Habitat.</i> ${esc(sp.habitat)}<br><i>Lore.</i> ${esc(sp.lore)}`;
    const isPartner = g.collection.partnerUid === uid;
    const pb = $('[data-act=partner]', root);
    pb.disabled = isPartner;
    pb.textContent = isPartner ? 'Current Partner' : 'Set as Partner';
    this.showPortrait(r);
  }

  setPartner() {
    if (!this.selectedUid) return;
    this.game.setPartner(this.selectedUid);
    this.buildBestiary();
  }

  showPortrait(record) {
    const canvas = $('#bestiary .portrait');
    if (!this.portrait) {
      try {
        // Dramatic studio lighting over a transparent canvas (the dark/crimson backdrop is CSS).
        const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
        renderer.setPixelRatio(1);
        renderer.setSize(640, 640, false);
        renderer.setClearColor(0x000000, 0);
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 1.1;
        const scene = new THREE.Scene();
        scene.add(new THREE.HemisphereLight(0xc9d6ff, 0x2a1a14, 0.7));
        const key = new THREE.DirectionalLight(0xffe2b8, 3.2); key.position.set(2.5, 3.2, 2.6); scene.add(key);
        const rim = new THREE.DirectionalLight(0x7fc8ff, 3.6); rim.position.set(-2.8, 2.2, -2.6); scene.add(rim);
        const rim2 = new THREE.DirectionalLight(0xff8a6a, 1.4); rim2.position.set(3, 0.8, -2.5); scene.add(rim2);
        const cam = new THREE.PerspectiveCamera(32, 1, 0.1, 20);
        cam.position.set(0, 1.0, 3.2); cam.lookAt(0, 0.62, 0);
        const turntable = new THREE.Group(); scene.add(turntable);
        this.portrait = { renderer, scene, cam, turntable, model: null, anim: null, uid: null };
      } catch (e) { this.portrait = { failed: true }; }
    }
    const p = this.portrait;
    if (p.failed || p.uid === record.uid) return;
    if (p.model) p.turntable.remove(p.model.root);
    p.model = buildCreatureParts(record.speciesId, hash(record.uid) & 7);
    const big = record.speciesId === 'thalorix';
    p.turntable.scale.setScalar(big ? 0.55 : 1);
    p.turntable.add(p.model.root);
    p.anim = createAnimator(p.model, 5.6);
    p.uid = record.uid;
  }

  renderPortrait(dt) {
    const p = this.portrait;
    if (!p || p.failed || this.el.bestiary.classList.contains('hidden') || !p.model) return;
    p.turntable.rotation.y += dt * 0.6;
    p.anim.update(dt, performance.now() / 1000);
    p.renderer.render(p.scene, p.cam);
  }

  // ------------------------------------------------------------------ HUD
  toast(message, kind = 'info') {
    const box = $('#toasts');
    const t = document.createElement('div');
    t.className = `toast ${kind}`;
    t.textContent = message;
    box.prepend(t);
    while (box.children.length > 4) box.lastChild.remove();
    setTimeout(() => { t.style.opacity = '0'; setTimeout(() => t.remove(), 650); }, 4600);
    if (this.game.state === 'title' && kind === 'warning') $('#title-status').textContent = message;
  }

  banner(title, sub) {
    const b = $('#banner');
    $('.t', b).textContent = title; $('.s', b).textContent = sub;
    b.style.opacity = '1';
    clearTimeout(this.bannerTimer);
    this.bannerTimer = setTimeout(() => { b.style.opacity = '0'; }, 2800);
  }

  damageNumber(health, info) {
    if (this.game.state !== 'playing' && this.game.state !== 'defeat') return;
    const el = document.createElement('div');
    el.className = 'dmg';
    const playerHurt = health.faction === 'Player';
    el.innerHTML = `${Math.round(info.amount)}${info.effective ? ' <small>Effective!</small>' : info.resisted ? ' <small>Resisted</small>' : ''}`;
    el.style.color = playerHurt ? '#f2735e' : info.effective ? '#fac75a' : '#ffffff';
    $('#damage').appendChild(el);
    const world = health.owner.position.clone(); world.y += 1.4;
    world.x += (Math.random() - 0.5) * 0.5; world.z += (Math.random() - 0.5) * 0.5;
    this.damage.push({ el, world, born: performance.now() / 1000 });
  }

  project(v) {
    const cam = this.game.camera3d;
    const p = v.clone().project(cam);
    if (p.z > 1 || p.z < -1) return null;
    return { x: (p.x * 0.5 + 0.5) * window.innerWidth, y: (-p.y * 0.5 + 0.5) * window.innerHeight };
  }

  updateHud(dt) {
    const g = this.game, p = g.player, input = g.input;
    if (g.state !== 'playing') return;
    // Player
    const hpBar = $('#player-panel .hp');
    $('i', hpBar).style.width = `${p.health.fraction * 100}%`;
    $('i', hpBar).style.background = p.health.fraction < 0.35 ? '#f2735e' : '';
    $('b', hpBar).textContent = `${Math.ceil(p.health.current)} / ${p.health.max}`;
    $('#player-panel .spark i').style.width = `${p.sparkCd01 * 100}%`;
    $('#player-panel .dodge i').style.width = `${p.dodgeCd01 * 100}%`;
    $('#player-panel .status').textContent = p.sneaking ? 'Sneaking' : p.wading ? 'Wading' : p.running ? 'Running' : '';
    const partner = g.collection.partner;
    $('#player-panel .partner').textContent = partner ? `Partner: ${partner.nickname || Species[partner.speciesId].name} · Lv ${partner.level} (${partner.personality})` : 'No partner yet';
    const v = p.health.fraction < 0.35 ? (0.35 - p.health.fraction) / 0.35 * (0.75 + 0.25 * Math.sin(g.time * 6)) : 0;
    $('#vignette').style.opacity = v * 0.55;
    this.nav.update();
    // Objective
    $('#objective .t').textContent = g.objectives.title;
    $('#objective .d').innerHTML = g.objectives.description;
    // Prompt
    const prompt = $('#prompt');
    const target = p.bindTarget;
    if (p.binding) { prompt.classList.remove('hidden'); $('.p', prompt).textContent = 'Binding…'; $('.q', prompt).textContent = ''; }
    else if (target) {
      prompt.classList.remove('hidden');
      const pct = Math.round(target.captureChance * 100);
      const col = pct >= 70 ? '#8be3b4' : pct >= 40 ? '#fac75a' : '#f2735e';
      $('.p', prompt).innerHTML = `<kbd>${input.prompt('Bind')}</kbd> Bind ${esc(target.name)} <span style="color:${col}">${pct}%</span>`;
      const aw = target.awareness;
      if (aw === 'Exhausted') $('.q', prompt).textContent = 'Exhausted — guaranteed bind';
      else {
        const state = { Asleep: 'Asleep ×2.0', Unaware: 'Unaware ×1.5', Hostile: 'Hostile ×0.85' }[aw] || 'Alert ×1.0';
        const res = target.failedBinds > 0 ? `  ·  Resonance +${Math.round(target.failedBinds * CaptureRules.ResonancePerFailure * 100)}%` : '';
        $('.q', prompt).textContent = `${state}  ·  HP ${Math.round(target.health.fraction * 100)}% (lower = easier)${res}`;
      }
    } else prompt.classList.add('hidden');
    // Enemy
    const enemy = g.battle.primary, ep = $('#enemy');
    if (enemy && !enemy.removed) {
      ep.classList.remove('hidden');
      $('.n', ep).innerHTML = `${esc(enemy.name)} <small>Lv ${enemy.record.level} · ${enemy.record.personality} · ${enemy.species.element}</small>`;
      $('i', ep).style.width = `${enemy.health.fraction * 100}%`;
    } else ep.classList.add('hidden');
    // Markers
    const mk = $('#markers');
    let used = 0;
    for (const c of g.creatures) {
      if (!c.isWild) continue;
      const ind = c.brain.indicator;
      if (!ind) continue;
      if (c.position.distanceToSquared(g.camera3d.position) > 45 * 45) continue;
      const s = this.project(c.markerPoint);
      if (!s) continue;
      let m = this.markers[used];
      if (!m) { m = document.createElement('div'); m.className = 'marker'; mk.appendChild(m); this.markers.push(m); }
      m.style.display = '';
      m.style.left = `${s.x}px`; m.style.top = `${s.y}px`;
      const map = {
        suspicious: ['?', `rgba(250,199,90,${0.45 + c.suspicion * 0.55})`], noticed: ['!', '#fac75a'], hostile: ['!!', '#f2735e'],
        sleep: ['Zz', '#b3d9ff'], exhausted: ['100%', '#8be3b4'],
      }[ind];
      m.textContent = map[0]; m.style.color = map[1];
      used++;
    }
    for (let i = used; i < this.markers.length; i++) this.markers[i].style.display = 'none';
    // Damage numbers
    const now = performance.now() / 1000;
    this.damage = this.damage.filter((d) => {
      const age = now - d.born;
      if (age > 0.9) { d.el.remove(); return false; }
      const s = this.project(d.world.clone().setY(d.world.y + age * 1.2));
      if (!s) { d.el.style.display = 'none'; return true; }
      d.el.style.display = '';
      d.el.style.left = `${s.x}px`; d.el.style.top = `${s.y}px`;
      d.el.style.opacity = Math.min(1, (0.9 - age) * 3);
      return true;
    });
    // FPS + hint + lock hint
    const fpsEl = $('#fps');
    fpsEl.classList.toggle('hidden', !g.settings.showFps);
    this.fps.frames++; this.fps.time += dt;
    if (this.fps.time > 0.5) { fpsEl.textContent = `${Math.round(this.fps.frames / this.fps.time)} FPS`; this.fps.frames = 0; this.fps.time = 0; }
    const hint = $('#hint');
    hint.classList.toggle('hidden', !(g.settings.showHint || this.hintExpanded));
    const P = (a) => `<kbd>${input.prompt(a)}</kbd>`;
    hint.innerHTML = this.hintExpanded
      ? `Move <kbd>WASD</kbd> · Run ${P('Run')} · Jump ${P('Jump')}<br>Sneak ${P('Sneak')} · Dodge ${P('Dodge')}<br>Strike ${P('Attack')} · Spark ${P('Spark')} · Bind ${P('Bind')}<br>Bestiary ${P('Bestiary')} · Menu ${P('Pause')} · Hide <kbd>H</kbd>`
      : `<kbd>H</kbd> Controls &nbsp; ${P('Pause')} Menu`;
    $('#lockhint').classList.toggle('hidden', input.locked || input.lastDevice === 'pad' || g.time - g.playStart < 0.5);
  }
}

function hash(s) { let h = 0; for (let i = 0; i < (s || '').length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return Math.abs(h); }
