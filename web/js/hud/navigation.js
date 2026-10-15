// HUD navigation: a compass strip with landmark markers and a rotating, circular minimap.
// North is world -Z. Both follow the camera heading, so "up" on the minimap is where you are looking.
import * as THREE from 'three';
import { Layout } from '../world.js';

const DEG = 180 / Math.PI;
const wrap = (d) => ((d + 540) % 360) - 180;
const MAP_EXTENT = 200;     // metres covered by the pre-rendered map image
const MAP_RES = 256;        // pixels
const VIEW_RADIUS = 42;     // metres shown from the centre of the minimap to its rim

export class Navigation {
  constructor(game, compassEl, minimapCanvas) {
    this.game = game;
    this.compass = compassEl;
    this.canvas = minimapCanvas;
    this.ctx = minimapCanvas.getContext('2d');
    this.frame = 0;
    this.items = [];
    this.dir = new THREE.Vector3();
    const strip = compassEl.querySelector('.strip');
    const add = (label, bearingFn, cls) => {
      const el = document.createElement('span');
      el.className = cls;
      el.textContent = label;
      strip.appendChild(el);
      this.items.push({ el, bearingFn });
    };
    [['N', 0], ['NE', 45], ['E', 90], ['SE', 135], ['S', 180], ['SW', 225], ['W', 270], ['NW', 315]]
      .forEach(([l, b]) => add(l, () => b, l.length === 1 ? 'cardinal' : 'inter'));
    for (let b = 15; b < 360; b += 15) if (b % 45) add('', () => b, 'tick');
    const at = (x, z) => () => this.bearingTo(x, z);
    add('◆', at(...Layout.waystone), 'mark waystone');
    add('⌂', at(...Layout.ruins), 'mark ruins');
    add('≈', at(...Layout.lake), 'mark lake');
    const guardian = Layout.nests.find((n) => n.speciesId === 'thalorix');
    if (guardian) add('✦', at(...guardian.xz), 'mark guardian');
    this.mapImage = this.renderMap();
  }

  bearingTo(x, z) {
    const p = this.game.player.position;
    return Math.atan2(x - p.x, -(z - p.z)) * DEG;
  }

  get heading() {
    const f = this.game.camera3d.getWorldDirection(this.dir);
    return Math.atan2(f.x, -f.z) * DEG;
  }

  /** One-time top-down map of the vale: hill-shaded terrain, forest, paths, water, landmarks. */
  renderMap() {
    const w = this.game.world, T = w.terrain;
    const c = document.createElement('canvas');
    c.width = c.height = MAP_RES;
    const ctx = c.getContext('2d');
    const img = ctx.createImageData(MAP_RES, MAP_RES);
    const step = MAP_EXTENT / MAP_RES;
    for (let j = 0; j < MAP_RES; j++) {
      for (let i = 0; i < MAP_RES; i++) {
        const x = (i + 0.5) * step - MAP_EXTENT / 2, z = (j + 0.5) * step - MAP_EXTENT / 2;
        const h = w.heightAt(x, z);
        const shade = Math.max(0.55, Math.min(1.25, 1 + (w.heightAt(x - step, z - step) - h) * 0.6));
        let r, g, b;
        if (w.waterDepth(x, z) > 0.05) { const d = Math.min(1, w.waterDepth(x, z) / 2); r = 40 - d * 15; g = 92 - d * 20; b = 104 - d * 10; }
        else {
          const forest = T.forestMask(x, z), path = T.pathMask(x, z), high = Math.min(1, Math.max(0, (h - 9) / 14));
          r = 70 - forest * 30 + high * 50; g = 96 - forest * 30 + high * 30; b = 52 - forest * 14 + high * 40;
          if (path > 0.25) { const k = Math.min(1, (path - 0.25) * 2); r += (150 - r) * k; g += (124 - g) * k; b += (82 - b) * k; }
          r *= shade; g *= shade; b *= shade;
        }
        const out = Math.hypot(x, z) > Layout.playableRadius;
        const o = (j * MAP_RES + i) * 4;
        img.data[o] = out ? r * 0.35 : r; img.data[o + 1] = out ? g * 0.35 : g; img.data[o + 2] = out ? b * 0.35 : b; img.data[o + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);
    return c;
  }

  update() {
    const g = this.game;
    const h = this.heading;
    // Compass: ±90° visible across the strip.
    const width = this.compass.clientWidth || 520;
    for (const it of this.items) {
      const d = wrap(it.bearingFn() - h);
      const vis = Math.abs(d) < 90;
      it.el.style.display = vis ? '' : 'none';
      if (vis) {
        it.el.style.left = `${width / 2 + (d / 90) * (width / 2)}px`;
        it.el.style.opacity = String(1 - Math.max(0, Math.abs(d) - 55) / 35);
      }
    }
    // Minimap: redraw at ~15 Hz.
    if (this.frame++ % 4) return;
    const ctx = this.ctx, size = this.canvas.width, r = size / 2, ppm = (r - 6) / VIEW_RADIUS;
    const p = g.player.position;
    ctx.clearRect(0, 0, size, size);
    ctx.save();
    ctx.beginPath(); ctx.arc(r, r, r - 4, 0, Math.PI * 2); ctx.clip();
    ctx.fillStyle = '#0d0b09'; ctx.fillRect(0, 0, size, size);
    ctx.translate(r, r);
    ctx.rotate(-h / DEG);
    ctx.scale(ppm, ppm);
    ctx.translate(-p.x, -p.z);
    ctx.globalAlpha = 0.92;
    ctx.drawImage(this.mapImage, -MAP_EXTENT / 2, -MAP_EXTENT / 2, MAP_EXTENT, MAP_EXTENT);
    ctx.globalAlpha = 1;
    // Landmarks.
    const icon = (x, z, color, rad) => { ctx.beginPath(); ctx.arc(x, z, rad / ppm, 0, Math.PI * 2); ctx.fillStyle = color; ctx.fill(); };
    icon(Layout.waystone[0], Layout.waystone[1], '#7ff5e4', 4);
    icon(Layout.ruins[0], Layout.ruins[1], '#c9b27a', 4);
    // Creatures: partner always, wild ones only when close enough to sense.
    for (const c of g.creatures) {
      const d = Math.hypot(c.position.x - p.x, c.position.z - p.z);
      if (c.isWild && d > 34) continue;
      const big = c.species.id === 'thalorix';
      const col = !c.isWild ? '#8be3b4' : c.brain.state === 'Combat' ? '#f2735e' : big ? '#8fb8ff' : '#f0c868';
      icon(c.position.x, c.position.z, col, big ? 4.5 : 3);
    }
    ctx.restore();
    // Player arrow (camera-relative), rim and north tick.
    const yaw = g.player.yaw;
    const ph = Math.atan2(Math.sin(yaw), -Math.cos(yaw)) * DEG;
    ctx.save();
    ctx.translate(r, r);
    ctx.rotate((ph - h) / DEG);
    ctx.beginPath(); ctx.moveTo(0, -8); ctx.lineTo(6, 6); ctx.lineTo(0, 3); ctx.lineTo(-6, 6); ctx.closePath();
    ctx.fillStyle = '#f6e3b0'; ctx.strokeStyle = '#2a1e10'; ctx.lineWidth = 1.5; ctx.fill(); ctx.stroke();
    ctx.restore();
    const na = (-h - 90) / DEG;
    ctx.fillStyle = '#e3bd6b';
    ctx.font = '700 12px Cinzel, Georgia, serif';
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText('N', r + Math.cos(na) * (r - 13), r + Math.sin(na) * (r - 13));
  }
}
