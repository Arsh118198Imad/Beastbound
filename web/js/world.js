// Sunmeadow Vale: terrain, water, sky, day/night, vegetation, landmarks, colliders and nests.
// Port of Assets/Beastbound/Scripts/Runtime/World (same layout, same rules).
import * as THREE from 'three';
import {
  clamp, clamp01, lerp, smoothstep, smoothMax, damp, Noise, makeRng, distanceToPolyline,
  srgb, GeometryBatch, rockGeometry,
} from './util.js';
import { Personality } from './rules.js';
import { SkySystem } from './render/sky.js';
import { createWater } from './render/water.js';
import { mergeVertices } from 'three/addons/utils/BufferGeometryUtils.js';
import { Models } from './render/models.js';
import { ArtStyle } from './render/style.js';
import { GrassField } from './render/grass.js';

const GRASS_LAYER_REACH = [1, 0.55, 0.32];

export const Layout = {
  size: 260,
  playableRadius: 92,
  waterLevel: 0,
  waystone: [-6, -10],
  playerSpawn: [-6, 2.5],
  lake: [40, 28],
  lakeRadius: 19,
  ruins: [-50, -42],
  stream: [[-95, 62], [-70, 55], [-48, 60], [-25, 50], [-5, 52], [14, 40], [26, 32]],
  paths: [
    [[-6, -10], [4, 2], [14, 12], [22, 14]],
    [[-6, -10], [-22, -22], [-38, -30], [-50, -42]],
    [[-6, -10], [12, -22], [30, -34], [48, -36]],
    [[4, 2], [-10, 22], [-24, 34]],
  ],
  nests: [
    { id: 'nest_meadow', xz: [22, -16], personality: Personality.Bold, level: 4, vigor: 1.03 },
    { id: 'nest_lakeshore', xz: [18, 38], personality: Personality.Timid, level: 3, vigor: 1.07 },
    { id: 'nest_ruins', xz: [-42, -32], personality: Personality.Sleepy, level: 5, vigor: 0.98 },
    { id: 'nest_grove', xz: [-24, 30], personality: Personality.Curious, level: 3, vigor: 1.0 },
    { id: 'nest_knoll', xz: [50, -30], personality: Personality.Playful, level: 4, vigor: 0.95 },
    { id: 'nest_westwood', xz: [-64, 6], speciesId: 'thalorix', personality: Personality.Stubborn, level: 7, vigor: 1.04 },
  ],
};

// ====================================================================== Heightfield

export class TerrainField {
  constructor(seed, resolution = 177) {
    this.res = resolution;
    this.size = Layout.size;
    this.half = this.size / 2;
    this.cell = this.size / (this.res - 1);
    this.noise = new Noise(seed, 16);
    this.h = new Float32Array(this.res * this.res);
    for (let z = 0; z < this.res; z++)
      for (let x = 0; x < this.res; x++)
        this.h[z * this.res + x] = this.compute(-this.half + x * this.cell, -this.half + z * this.cell);
  }

  compute(x, z) {
    const n = this.noise;
    const d = Math.hypot(x, z);
    let h = 1.4 + (n.fbm(x * 0.011, z * 0.011, 4, 0) - 0.5) * 8;
    h += (n.fbm(x * 0.05, z * 0.05, 2, 6) - 0.5) * 1.0;
    h = smoothMax(h, 0.55, 0.6);
    h += bump(x, z, 52, -30, 16, 5) + bump(x, z, -28, 36, 14, 3.5);
    const ring = smoothstep(80, 128, d + (n.sample(x * 0.02, z * 0.02, 8) - 0.5) * 18);
    h += ring * (16 + n.ridged(x * 0.012, z * 0.012, 4, 9) * 46);
    const lakeDist = Math.hypot(x - Layout.lake[0], z - Layout.lake[1]) + (n.sample(x * 0.06, z * 0.06, 12) - 0.5) * 7;
    h = lerp(h, -1.15, 1 - smoothstep(Layout.lakeRadius - 8, Layout.lakeRadius + 6, lakeDist));
    const sd = distanceToPolyline(x, z, Layout.stream) + (n.sample(x * 0.08, z * 0.08, 13) - 0.5) * 2;
    h = lerp(h, -0.55, (1 - smoothstep(1.6, 5.5, sd)) * (1 - ring));
    h = lerp(h, 1.3, 1 - smoothstep(7, 16, Math.hypot(x - Layout.waystone[0], z - Layout.waystone[1])));
    h = lerp(h, 1.6, 1 - smoothstep(8, 18, Math.hypot(x - Layout.ruins[0], z - Layout.ruins[1])));
    return h;
  }

  pathMask(x, z) {
    let best = Infinity;
    for (const p of Layout.paths) best = Math.min(best, distanceToPolyline(x, z, p));
    best += (this.noise.sample(x * 0.3, z * 0.3, 14) - 0.5) * 0.8;
    return 1 - smoothstep(0.9, 2.0, best);
  }

  forestMask(x, z) { return smoothstep(0.5, 0.66, this.noise.fbm(x * 0.018, z * 0.018, 3, 3)); }

  heightAt(x, z) {
    const r = this.res;
    let fx = clamp((x + this.half) / this.cell, 0, r - 1.0001);
    let fz = clamp((z + this.half) / this.cell, 0, r - 1.0001);
    const ix = fx | 0, iz = fz | 0, tx = fx - ix, tz = fz - iz;
    const h00 = this.h[iz * r + ix], h10 = this.h[iz * r + ix + 1];
    const h01 = this.h[(iz + 1) * r + ix], h11 = this.h[(iz + 1) * r + ix + 1];
    if (tx >= tz) return h00 + (h10 - h00) * tx + (h11 - h10) * tz;
    return h00 + (h11 - h01) * tx + (h01 - h00) * tz;
  }

  normalAt(x, z, out = new THREE.Vector3()) {
    const e = this.cell;
    return out.set(this.heightAt(x - e, z) - this.heightAt(x + e, z), 2 * e,
      this.heightAt(x, z - e) - this.heightAt(x, z + e)).normalize();
  }

  slopeAt(x, z) { return Math.acos(clamp(this.normalAt(x, z).y, -1, 1)) * 180 / Math.PI; }

  buildGeometry() {
    const r = this.res, pos = new Float32Array(r * r * 3), uv = new Float32Array(r * r * 2);
    for (let z = 0; z < r; z++) for (let x = 0; x < r; x++) {
      const i = z * r + x;
      pos[i * 3] = -this.half + x * this.cell; pos[i * 3 + 1] = this.h[i]; pos[i * 3 + 2] = -this.half + z * this.cell;
      uv[i * 2] = x / (r - 1); uv[i * 2 + 1] = 1 - z / (r - 1);
    }
    const idx = [];
    for (let z = 0; z < r - 1; z++) for (let x = 0; x < r - 1; x++) {
      const a = z * r + x, b = a + 1, c = a + r, d = c + 1;
      // Counter-clockwise from above (three.js front faces), split along the a-d diagonal.
      idx.push(a, d, b, a, c, d);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    geo.setIndex(idx);
    geo.computeVertexNormals();
    return geo;
  }

  buildTexture(size) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = size;
    const ctx = canvas.getContext('2d');
    const img = ctx.createImageData(size, size);
    const C = (r, g, b) => [r, g, b];
    const sandWet = C(0.45, 0.42, 0.32), lakeBed = C(0.22, 0.3, 0.26), sand = C(0.86, 0.79, 0.6);
    const lush = C(0.2, 0.33, 0.1), sun = C(0.42, 0.47, 0.18), forest = C(0.15, 0.22, 0.08);
    const dirt = C(0.55, 0.43, 0.3), rock = C(0.44, 0.42, 0.4), rockDark = C(0.27, 0.26, 0.26), snow = C(0.94, 0.96, 1);
    const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
    const nrm = new THREE.Vector3();
    for (let py = 0; py < size; py++) {
      const z = -this.half + (py + 0.5) / size * this.size;
      for (let px = 0; px < size; px++) {
        const x = -this.half + (px + 0.5) / size * this.size;
        const h = this.heightAt(x, z);
        this.normalAt(x, z, nrm);
        const detail = this.noise.sample(x * 0.35, z * 0.35, 15);
        let c;
        if (h < -0.02) c = mix(sandWet, lakeBed, clamp01(-h / 1.2));
        else if (h < 0.45) c = mix(sand, sun, smoothstep(0.25, 0.45, h));
        else {
          c = mix(lush, sun, smoothstep(0.35, 0.7, this.noise.fbm(x * 0.04, z * 0.04, 2, 4)));
          c = mix(c, forest, this.forestMask(x, z) * 0.75);
          if (Math.abs(x) < 100 && Math.abs(z) < 100) c = mix(c, dirt, this.pathMask(x, z) * 0.9);
        }
        const steep = smoothstep(0.86, 0.7, nrm.y);
        const high = smoothstep(14, 24, h);
        c = mix(c, mix(rock, rockDark, detail), Math.max(steep, high * 0.85));
        c = mix(c, snow, smoothstep(36, 44, h + detail * 4) * smoothstep(0.6, 0.8, nrm.y));
        const k = 0.9 + detail * 0.2;
        // Canvas row 0 is the top of the image; uv.y was flipped to match.
        const o = (py * size + px) * 4;
        img.data[o] = clamp(c[0] * k * 255, 0, 255);
        img.data[o + 1] = clamp(c[1] * k * 255, 0, 255);
        img.data[o + 2] = clamp(c[2] * k * 255, 0, 255);
        img.data[o + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);
    const tex = new THREE.CanvasTexture(canvas);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 4;
    return tex;
  }
}

function bump(x, z, cx, cz, radius, height) {
  const t = 1 - clamp01(Math.hypot(x - cx, z - cz) / radius);
  return t * t * (3 - 2 * t) * height;
}

// ====================================================================== Colours

const SW = {
  trunk: srgb(0.42, 0.29, 0.19), trunkDark: srgb(0.3, 0.21, 0.15), pine: srgb(0.12, 0.26, 0.15), pineDark: srgb(0.07, 0.17, 0.1),
  leaf: srgb(0.2, 0.34, 0.11), leafLight: srgb(0.34, 0.45, 0.15), autumn: srgb(0.7, 0.45, 0.14), blossom: srgb(0.95, 0.8, 0.86),
  blossomLight: srgb(1, 0.93, 0.95), rock: srgb(0.55, 0.55, 0.57), rockDark: srgb(0.4, 0.4, 0.44), rockMoss: srgb(0.42, 0.52, 0.33),
  stem: srgb(0.3, 0.5, 0.2), mushCap: srgb(0.86, 0.25, 0.2), mushStem: srgb(0.95, 0.9, 0.8), reed: srgb(0.55, 0.6, 0.3),
  flowers: [srgb(1, 0.85, 0.25), srgb(0.62, 0.45, 0.95), srgb(0.97, 0.96, 0.92), srgb(0.95, 0.32, 0.3), srgb(0.35, 0.62, 1)],
  grassRoot: [srgb(0.08, 0.15, 0.04), srgb(0.1, 0.17, 0.05), srgb(0.12, 0.18, 0.06), srgb(0.09, 0.14, 0.05), srgb(0.14, 0.19, 0.06), srgb(0.11, 0.16, 0.05)],
  grassTip: [srgb(0.36, 0.46, 0.16), srgb(0.44, 0.5, 0.2), srgb(0.55, 0.55, 0.26), srgb(0.32, 0.42, 0.15), srgb(0.62, 0.58, 0.3), srgb(0.4, 0.48, 0.18)],
};

// ====================================================================== World

export class World {
  constructor(scene, seed, renderer) {
    this.renderer = renderer;
    const t0 = performance.now();
    this.scene = scene;
    this.seed = seed;
    this.rng = makeRng(seed);
    this.terrain = new TerrainField(seed);
    this.root = new THREE.Group();
    this.root.name = 'World';
    scene.add(this.root);

    this.circles = [];   // {x, z, r, top}
    this.boxes = [];     // {x, z, hx, hz, c, s, top}
    this.grid = new Map();
    this.grassChunks = [];
    this.treePositions = [];
    this.nests = [];
    this.time = 0;
    this.hour = 9.5;
    this.dayLengthMinutes = 24;

    this.buildLighting();
    this.skySystem = new SkySystem(renderer, scene);
    this.buildTerrain();
    this.buildWater();
    this.buildFarMountains();
    this.buildWaystone();
    this.buildRuins();
    this.scatterTrees();
    this.scatterRocks();
    this.scatterGrass();
    this.scatterFlora();
    this.buildMotes();
    this.indexColliders();
    this.defineNests();

    const sp = Layout.playerSpawn;
    this.playerSpawn = new THREE.Vector3(sp[0], this.heightAt(sp[0], sp[1]) + 0.05, sp[1]);
    this.playerSpawnYaw = Math.atan2(Layout.lake[0] - sp[0], Layout.lake[1] - sp[1]);
    this.applyHour();
    this.genMs = performance.now() - t0;
  }

  // ------------------------------------------------------------------ queries
  heightAt(x, z) { return this.terrain.heightAt(x, z); }
  waterDepth(x, z) { return Math.max(0, Layout.waterLevel - this.heightAt(x, z)); }
  insidePlayArea(x, z, margin = 0) { return Math.hypot(x, z) <= Layout.playableRadius - margin; }
  isWalkable(x, z, maxSlope = 32) {
    if (!this.insidePlayArea(x, z, 3)) return false;
    if (this.heightAt(x, z) < Layout.waterLevel + 0.15) return false;
    return this.terrain.slopeAt(x, z) <= maxSlope && !this.pointBlocked(x, z, 0.6);
  }
  findWalkableNear(x, z, radius, attempts = 12) {
    if (this.isWalkable(x, z)) return new THREE.Vector3(x, this.heightAt(x, z), z);
    for (let i = 0; i < attempts; i++) {
      const a = Math.random() * Math.PI * 2, r = Math.sqrt(Math.random()) * radius;
      const cx = x + Math.cos(a) * r, cz = z + Math.sin(a) * r;
      if (this.isWalkable(cx, cz)) return new THREE.Vector3(cx, this.heightAt(cx, cz), cz);
    }
    return null;
  }

  // ------------------------------------------------------------------ collision
  addCircle(x, z, r, top) { this.circles.push({ x, z, r, top }); }
  addBox(x, z, hx, hz, rot, top) { this.boxes.push({ x, z, hx, hz, c: Math.cos(rot), s: Math.sin(rot), top }); }

  indexColliders() {
    const key = (cx, cz) => cx * 1000 + cz;
    const put = (obj, ext) => {
      for (let cx = Math.floor((obj.x - ext) / 10); cx <= Math.floor((obj.x + ext) / 10); cx++)
        for (let cz = Math.floor((obj.z - ext) / 10); cz <= Math.floor((obj.z + ext) / 10); cz++) {
          const k = key(cx, cz);
          if (!this.grid.has(k)) this.grid.set(k, []);
          this.grid.get(k).push(obj);
        }
    };
    for (const c of this.circles) { c.type = 0; put(c, c.r); }
    for (const b of this.boxes) { b.type = 1; put(b, Math.hypot(b.hx, b.hz)); }
  }

  nearby(x, z) { return this.grid.get(Math.floor(x / 10) * 1000 + Math.floor(z / 10)) || EMPTY; }

  /** Pushes a vertical capsule (centre x,z, radius, feet y) out of props. Mutates pos. */
  resolve(pos, radius) {
    for (let iter = 0; iter < 2; iter++) {
      for (const o of this.nearby(pos.x, pos.z)) {
        if (pos.y > o.top - 0.05) continue;
        if (o.type === 0) {
          const dx = pos.x - o.x, dz = pos.z - o.z, d = Math.hypot(dx, dz), min = o.r + radius;
          if (d < min && d > 1e-5) { pos.x = o.x + dx / d * min; pos.z = o.z + dz / d * min; }
        } else {
          // to box-local
          const dx = pos.x - o.x, dz = pos.z - o.z;
          const lx = dx * o.c + dz * o.s, lz = -dx * o.s + dz * o.c;
          const qx = clamp(lx, -o.hx, o.hx), qz = clamp(lz, -o.hz, o.hz);
          let ex = lx - qx, ez = lz - qz, d = Math.hypot(ex, ez);
          if (d < radius) {
            let nx, nz;
            if (d > 1e-5) { nx = ex / d; nz = ez / d; }
            else { // inside: push along the shallowest axis
              const px = o.hx - Math.abs(lx), pz = o.hz - Math.abs(lz);
              if (px < pz) { nx = Math.sign(lx) || 1; nz = 0; d = -px; } else { nx = 0; nz = Math.sign(lz) || 1; d = -pz; }
            }
            const push = radius - d;
            const wx = nx * o.c - nz * o.s, wz = nx * o.s + nz * o.c;
            pos.x += wx * push; pos.z += wz * push;
          }
        }
      }
    }
  }

  pointBlocked(x, z, radius) {
    const p = { x, y: 0, z };
    this.resolve(p, radius);
    return Math.abs(p.x - x) > 0.01 || Math.abs(p.z - z) > 0.01;
  }

  /** True if terrain or a prop blocks the straight line a->b. */
  segmentBlocked(a, b) {
    const dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
    const len = Math.hypot(dx, dy, dz);
    const steps = Math.max(2, Math.ceil(len / 1.2));
    for (let i = 1; i < steps; i++) {
      const t = i / steps;
      const x = a.x + dx * t, y = a.y + dy * t, z = a.z + dz * t;
      if (y < this.heightAt(x, z) - 0.1) return true;
      for (const o of this.nearby(x, z)) {
        if (y > o.top) continue;
        if (o.type === 0) { if (Math.hypot(x - o.x, z - o.z) < o.r * 0.9) return true; }
        else {
          const ex = x - o.x, ez = z - o.z;
          if (Math.abs(ex * o.c + ez * o.s) < o.hx && Math.abs(-ex * o.s + ez * o.c) < o.hz) return true;
        }
      }
    }
    return false;
  }

  /** Largest t in [0,1] the camera can move along a->b before hitting terrain/props. */
  cameraCast(a, b, radius = 0.3) {
    const dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
    const len = Math.hypot(dx, dy, dz);
    const steps = Math.max(4, Math.ceil(len / 0.25));
    for (let i = 1; i <= steps; i++) {
      const t = i / steps;
      const x = a.x + dx * t, y = a.y + dy * t, z = a.z + dz * t;
      if (y < Math.max(this.heightAt(x, z), Layout.waterLevel) + radius) return Math.max(0, (i - 1) / steps);
      for (const o of this.nearby(x, z)) {
        if (y > o.top + radius) continue;
        if (o.type === 0 && Math.hypot(x - o.x, z - o.z) < o.r + radius) return Math.max(0, (i - 1) / steps);
      }
    }
    return 1;
  }

  // ------------------------------------------------------------------ build: lighting & sky
  buildLighting() {
    this.hemi = new THREE.HemisphereLight(0xbfd9ff, 0x4d4a32, 0.4); // sky IBL provides most ambient light
    this.scene.add(this.hemi);
    this.sun = new THREE.DirectionalLight(0xfff3e0, 2.6);
    this.sun.castShadow = true;
    this.sun.shadow.mapSize.set(2048, 2048);
    this.sun.shadow.radius = 3;
    this.sun.shadow.bias = -0.0005;
    this.sun.shadow.normalBias = 0.04;
    const sc = this.sun.shadow.camera;
    sc.left = -45; sc.right = 45; sc.top = 45; sc.bottom = -45; sc.near = 1; sc.far = 260;
    this.scene.add(this.sun);
    this.scene.add(this.sun.target);
    this.scene.fog = new THREE.FogExp2(0xb8cde0, 0.0042);
  }

  buildTerrain() {
    const geo = this.terrain.buildGeometry();
    const mat = new THREE.MeshStandardMaterial({ map: this.terrain.buildTexture(1024), roughness: 0.92, metalness: 0 });
    const detail = detailTexture();
    mat.onBeforeCompile = (shader) => {
      shader.uniforms.tDetail = { value: detail };
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', '#include <common>\nuniform sampler2D tDetail;')
        .replace('#include <map_fragment>', `#include <map_fragment>
          // Two scales of tiling detail break up the colour map into soil, pebbles and grass clumps.
          float d1 = texture2D(tDetail, vMapUv * 190.0).r;
          float d2 = texture2D(tDetail, vMapUv * 47.0 + 0.37).g;
          diffuseColor.rgb *= mix(0.72, 1.22, d1 * 0.65 + d2 * 0.35);`)
        .replace('#include <roughnessmap_fragment>', `#include <roughnessmap_fragment>
          roughnessFactor = clamp(roughnessFactor - (texture2D(tDetail, vMapUv * 190.0).b - 0.5) * 0.25, 0.4, 1.0);`);
    };
    const mesh = new THREE.Mesh(geo, mat);
    mesh.receiveShadow = true;
    this.root.add(mesh);
    this.terrainMesh = mesh;
  }

  buildWater() {
    this.water = createWater(Layout.size, 512);
    this.water.position.y = Layout.waterLevel;
    this.root.add(this.water);
  }

  buildFarMountains() {
    const rng = this.rng;
    const batch = new GeometryBatch();
    const m = new THREE.Matrix4();
    const far = srgb(0.46, 0.53, 0.64), snow = srgb(0.9, 0.93, 1);
    for (let i = 0; i < 18; i++) {
      const a = (i + rng() * 0.6) / 18 * Math.PI * 2, r = 215 + rng() * 80;
      const w = 70 + rng() * 70, hgt = 50 + rng() * 60;
      const geo = rockGeometry(this.seed + 300 + i, 0.32, 2);
      const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(0, rng() * 6.28, 0));
      m.compose(new THREE.Vector3(Math.cos(a) * r, -12, Math.sin(a) * r), q, new THREE.Vector3(w, hgt, w * 0.8));
      batch.add(geo, m, far);
      if (hgt > 80) {
        m.compose(new THREE.Vector3(Math.cos(a) * r, -12 + hgt * 0.36, Math.sin(a) * r), q, new THREE.Vector3(w * 0.42, hgt * 0.3, w * 0.34));
        batch.add(geo, m, snow);
      }
    }
    const mesh = new THREE.Mesh(batch.build(), new THREE.MeshLambertMaterial({ vertexColors: true }));
    this.root.add(mesh);
  }

  // ------------------------------------------------------------------ landmarks
  stoneMat() {
    if (!this._stone) {
      this.envKit = Models.has('env');
      if (this.envKit) {
        // Blender cut-stone blocks: vertex colours carry moss and crevices; detail is added in world space.
        this._stone = stoneDetailMaterial(new THREE.Color(1, 1, 1));
        this._moss = stoneDetailMaterial(srgb(0.72, 0.9, 0.55));
      } else {
        this._stone = new THREE.MeshStandardMaterial({ color: srgb(0.7, 0.66, 0.58), roughness: 0.9 });
        this._moss = new THREE.MeshStandardMaterial({ color: srgb(0.45, 0.58, 0.32), roughness: 1 });
      }
    }
    return this._stone;
  }

  /** Box-shaped stone: a chipped Blender block (stretched to size) when the kit loaded, else a plain box. */
  blockGeometry(sx, sy, sz) {
    this.stoneMat();
    if (this.envKit) {
      const g = Models.geometry('env', this.rng() < 0.3 ? 'block1' : 'block0');
      if (g) return g.clone().scale(sx, sy, sz);
    }
    return new THREE.BoxGeometry(sx, sy, sz);
  }

  block(parent, x, y, z, sx, sy, sz, rotY, mat, collide = true, rotZ = 0) {
    const mesh = new THREE.Mesh(this.blockGeometry(sx, sy, sz), mat || this.stoneMat());
    mesh.position.set(x, y, z);
    mesh.rotation.set(0, rotY, rotZ);
    mesh.castShadow = mesh.receiveShadow = true;
    parent.add(mesh);
    if (collide) {
      const wp = new THREE.Vector3(); mesh.getWorldPosition(wp);
      const wq = new THREE.Quaternion(); mesh.getWorldQuaternion(wq);
      const yaw = new THREE.Euler().setFromQuaternion(wq, 'YXZ').y;
      this.addBox(wp.x, wp.z, sx / 2, sz / 2, -yaw, wp.y + sy / 2);
    }
    return mesh;
  }

  buildWaystone() {
    const [wx, wz] = Layout.waystone;
    const wy = this.heightAt(wx, wz);
    const g = new THREE.Group();
    g.position.set(wx, wy, wz);
    this.root.add(g);
    const ped = new THREE.Mesh(flatColors(new THREE.CylinderGeometry(1.15, 1.5, 0.7, 8), 0.16), this.stoneMat());
    ped.position.y = 0.2; ped.castShadow = ped.receiveShadow = true;
    g.add(ped);
    this.addCircle(wx, wz, 1.45, wy + 0.55);
    const glowMat = new THREE.MeshStandardMaterial({ color: srgb(0.4, 0.95, 0.9), emissive: srgb(0.3, 1, 0.95), emissiveIntensity: 1.6, roughness: 0.2 });
    this.waystoneCrystal = new THREE.Mesh(new THREE.OctahedronGeometry(0.8, 0), glowMat);
    this.waystoneCrystal.scale.set(0.75, 1.6, 0.75);
    this.waystoneCrystal.position.y = 2;
    g.add(this.waystoneCrystal);
    const light = new THREE.PointLight(0x73ffeb, 6, 14, 1.6);
    light.position.y = 2.2;
    g.add(light);
    const rng = this.rng;
    for (let i = 0; i < 8; i++) {
      const a = i / 8 * Math.PI * 2 + 0.2;
      const x = Math.cos(a) * 7.5, z = Math.sin(a) * 7.5;
      const y = this.heightAt(wx + x, wz + z) - wy;
      const broken = i === 2 || i === 5;
      const h = broken ? 1.1 + rng() * 0.5 : 2.4 + rng();
      const s = this.block(g, x, y + h / 2 - 0.2, z, 0.95, h, 0.6, -a + Math.PI / 2, null, true, (rng() - 0.5) * 0.12);
      if (!this.envKit) {
        const moss = new THREE.Mesh(new THREE.SphereGeometry(0.5, 10, 6), this._moss);
        moss.scale.set(1.05, 0.14, 0.7); moss.position.y = h / 2;
        s.add(moss);
      }
      if (broken) this.block(g, x + Math.cos(a + 0.4) * 1.8, y + 0.3, z + Math.sin(a + 0.4) * 1.8, 0.9, 0.6, 1.6, rng() * 3, null, true, 0.1);
    }
  }

  buildRuins() {
    const [rx, rz] = Layout.ruins;
    const g = new THREE.Group();
    g.position.set(rx, this.heightAt(rx, rz), rz);
    g.rotation.y = 35 * Math.PI / 180;
    this.root.add(g);
    g.updateMatrixWorld();
    const st = this.stoneMat(), moss = this._moss;
    this.block(g, -2.6, 2.4, 0, 1.2, 5.2, 1.2, 0);
    this.block(g, 2.6, 2.4, 0, 1.2, 5.2, 1.2, 0);
    g.updateMatrixWorld();
    const lintel = this.block(g, 0, 5.4, 0, 7, 1, 1.4, 0, st, false, -0.05);
    if (!this.envKit) {
      const lm = new THREE.Mesh(new THREE.SphereGeometry(0.5, 10, 6), moss);
      lm.scale.set(5, 0.3, 1.4); lm.position.y = 0.5; lintel.add(lm);
    }
    this.block(g, -7, 0.8, 4, 5, 1.8, 0.9, 0.26);
    this.block(g, 6.5, 0.6, 5, 4, 1.3, 0.9, -0.35);
    this.block(g, 0.5, 0.5, 9, 6, 1.1, 0.9, 0.05, moss);
    const rng = this.rng;
    for (let i = 0; i < 9; i++) {
      const x = rng() * 18 - 9, z = rng() * 16 - 6;
      if (Math.abs(x) < 3.5 && Math.abs(z) < 2) continue;
      const s = 0.6 + rng() * 0.7;
      g.updateMatrixWorld();
      this.block(g, x, 0.3, z, s * 1.3, s * 0.8, s, rng() * 1.5, i % 3 === 0 ? moss : st, true, rng() * 0.17);
    }
    const rune = new THREE.MeshStandardMaterial({ color: srgb(0.7, 0.6, 1), emissive: srgb(0.75, 0.55, 1), emissiveIntensity: 1.8 });
    for (const [x, y, z, s] of [[-2.6, 5.4, 0.75, 0.5], [-6, 0.4, 6, 1], [5, 0.3, 8, 0.8]]) {
      const c = new THREE.Mesh(new THREE.OctahedronGeometry(0.3, 0), rune);
      c.scale.set(s, s * 2, s); c.position.set(x, y, z); g.add(c);
    }
  }

  // ------------------------------------------------------------------ scatter
  nearLandmark(x, z, wClear, rClear) {
    return Math.hypot(x - Layout.waystone[0], z - Layout.waystone[1]) < wClear || Math.hypot(x - Layout.ruins[0], z - Layout.ruins[1]) < rClear;
  }

  scatterTrees() {
    const rng = this.rng, T = this.terrain;
    const batch = new GeometryBatch();   // trunks + dense inner canopy cores
    const leaves = new CardBatch();      // alpha-cut leaf cards (broadleaf)
    const needles = new CardBatch();     // alpha-cut needle cards (pines)
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler();
    const trunkP = new THREE.CylinderGeometry(0.12, 0.3, 7.2, 8).translate(0, 3.6, 0);
    const trunkB = new THREE.CylinderGeometry(0.17, 0.38, 3.6, 8).translate(0, 1.8, 0);
    const branch = new THREE.CylinderGeometry(0.05, 0.11, 1.8, 6).translate(0, 0.9, 0);
    const core = [0, 1, 2, 3].map((i) => foliageGeometry(this.seed + 100 + i));
    const coneCore = new THREE.ConeGeometry(1, 1, 10).translate(0, 0.5, 0);
    const dark = (c, k) => c.clone().multiplyScalar(k);
    let placed = 0;
    for (let attempt = 0; attempt < 6000 && placed < 260; attempt++) {
      const x = rng.range(-125, 125), z = rng.range(-125, 125);
      const h = T.heightAt(x, z);
      if (h < 0.6 || h > 30 || T.slopeAt(x, z) > 30 || this.nearLandmark(x, z, 13, 11) || T.pathMask(x, z) > 0.1) continue;
      if (Math.hypot(x - Layout.playerSpawn[0], z - Layout.playerSpawn[1]) < 9) continue;
      if (Layout.nests.some((n) => n.speciesId && Math.hypot(x - n.xz[0], z - n.xz[1]) < 7)) continue;
      const forest = T.forestMask(x, z);
      const chance = 0.035 + forest * 0.9 + smoothstep(80, 110, Math.hypot(x, z)) * 0.3;
      if (rng() > chance) continue;
      if (this.treePositions.some((t) => (t.x - x) ** 2 + (t.z - z) ** 2 < 12)) continue;
      const pos = new THREE.Vector3(x, h, z);
      this.treePositions.push(pos);
      placed++;
      const s = rng.range(0.8, 1.35);
      q.setFromEuler(e.set(0, rng() * 6.28, 0));
      const S = new THREE.Vector3(s, s, s);
      if (h > 8 || (forest > 0.5 && rng() < 0.55)) {
        // Conifer: tall trunk, dark tiered core, drooping needle branches.
        const tone = rng.range(0.8, 1.15);
        batch.add(trunkP, m.compose(pos.clone().setY(h - 0.2), q, S), SW.trunkDark);
        const tiers = 6;
        for (let t = 0; t < tiers; t++) {
          const k = t / (tiers - 1);
          const ty = h + (1.8 + k * 5.4) * s;
          const R = (2.1 - k * 1.55) * s;
          batch.add(coneCore, m.compose(new THREE.Vector3(x, ty - 0.5 * s, z), q, new THREE.Vector3(R * 0.38, 1.5 * s, R * 0.38)), dark(SW.pineDark, 0.45));
          const n = Math.round(14 + (1 - k) * 12);
          for (let i = 0; i < n; i++) {
            const a = (i / n) * Math.PI * 2 + rng() * 0.5 + t;
            const out = new THREE.Vector3(Math.cos(a), -0.35 - rng() * 0.2, Math.sin(a)).normalize();
            const c = new THREE.Vector3(x, ty, z).addScaledVector(out, R * 0.55);
            needles.addBranch(c, out, R * 1.15, R * 0.7, (i % 3 ? SW.pine : SW.pineDark).clone().multiplyScalar(tone * rng.range(0.85, 1.1)), 0.6 + k * 0.4);
          }
        }
      } else {
        // Broadleaf: trunk with a few limbs, dark core masses, and many leaf cards around them.
        const blossom = this.nearLandmark(x, z, 40, 30) && rng() < 0.4;
        const main = blossom ? SW.blossom : (rng() < 0.06 ? SW.autumn : SW.leaf);
        const light = blossom ? SW.blossomLight : SW.leafLight;
        batch.add(trunkB, m.compose(pos.clone().setY(h - 0.2), q, S), SW.trunk);
        const blobs = [{ p: new THREE.Vector3(0, 4.3, 0), r: 2.2 }];
        const nb = 3 + rng.int(3);
        for (let i = 0; i < nb; i++) {
          const a = i / nb * Math.PI * 2 + rng();
          blobs.push({ p: new THREE.Vector3(Math.cos(a) * 1.6, 3.5 + rng() * 1.4, Math.sin(a) * 1.6), r: rng.range(1.2, 1.7) });
          const dir = new THREE.Vector3(Math.cos(a) * 1.6, 1.6, Math.sin(a) * 1.6).normalize();
          batch.add(branch, m.compose(new THREE.Vector3(x, h + 2.4 * s, z), new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir), S), SW.trunk);
        }
        for (const b of blobs) {
          const c = new THREE.Vector3(x, h, z).addScaledVector(b.p, s);
          const r = b.r * s;
          batch.add(core[rng.int(4)], m.compose(c, q, new THREE.Vector3(r * 0.55, r * 0.45, r * 0.55)), dark(main, 0.4)); // hidden inner mass
          const cards = Math.round(30 + b.r * 16);
          for (let i = 0; i < cards; i++) {
            const dir = new THREE.Vector3(rng() * 2 - 1, rng() * 1.6 - 0.5, rng() * 2 - 1).normalize();
            const cp = c.clone().add(new THREE.Vector3(dir.x * r, dir.y * r * 0.8, dir.z * r).multiplyScalar(rng.range(0.6, 1.0)));
            const col = (rng() < 0.45 ? light : main).clone().multiplyScalar(rng.range(0.8, 1.1) * (0.75 + 0.35 * Math.max(0, dir.y)));
            leaves.addCard(cp, dir, rng.range(1.25, 1.85) * s, col, rng, (cp.y - h) / (6 * s));
          }
        }
      }
      this.addCircle(x, z, 0.32 * s + 0.05, h + 4 * s);
    }
    const treeMat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.9 });
    treeMat.onBeforeCompile = (shader) => { nearCameraFade(shader); };
    const mesh = new THREE.Mesh(batch.build(), treeMat);
    mesh.castShadow = mesh.receiveShadow = true;
    this.root.add(mesh);
    for (const [cards, tex] of [[leaves, leafTexture()], [needles, needleTexture()]]) {
      // alphaToCoverage keeps cut-out leaf edges crisp but antialiased under MSAA.
      const mat = new THREE.MeshStandardMaterial({ map: tex, vertexColors: true, alphaTest: 0.5, alphaToCoverage: true, side: THREE.DoubleSide, roughness: 0.7, metalness: 0 });
      mat.onBeforeCompile = (shader) => {
        shader.uniforms.uTime = this.grassUniforms ? this.grassUniforms.uTime : (this.grassUniforms = { uTime: { value: 0 } }).uTime;
        shader.vertexShader = shader.vertexShader
          .replace('#include <common>', '#include <common>\nattribute float sway;\nuniform float uTime;')
          .replace('#include <begin_vertex>', `#include <begin_vertex>
            float w = sin(uTime * 1.3 + position.x * 0.21 + position.z * 0.17) + 0.4 * sin(uTime * 2.9 + position.y * 0.8);
            transformed.xz += w * 0.06 * sway;`);
        shader.fragmentShader = shader.fragmentShader.replace('#include <normal_fragment_begin>', '#include <normal_fragment_begin>\n      normal = normalize(vNormal); // leaves lit from the canopy shape, both faces');
        nearCameraFade(shader);
      };
      const lm = new THREE.Mesh(cards.build(), mat);
      lm.castShadow = lm.receiveShadow = true;
      this.root.add(lm);
    }
  }

  scatterRocks() {
    const rng = this.rng, T = this.terrain;
    const batch = new GeometryBatch();
    this.stoneMat();
    const kit = this.envKit ? [0, 1, 2, 3, 4, 5].map((i) => Models.geometry('env', `rock${i}`)) : [];
    const rocks = kit.length && kit.every(Boolean) ? kit : [0, 1, 2, 3, 4, 5].map((i) => rockGeometry(this.seed + 200 + i, 0.3, 1));
    const useKit = rocks === kit;
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler();
    let placed = 0;
    for (let attempt = 0; attempt < 3000 && placed < 110; attempt++) {
      const x = rng.range(-122, 122), z = rng.range(-122, 122), d = Math.hypot(x, z), h = T.heightAt(x, z);
      if (h < -0.6 || h > 40 || this.nearLandmark(x, z, 10, 0) || T.pathMask(x, z) > 0.2) continue;
      if (T.slopeAt(x, z) > 32) continue; // boulders on cliffs overhang and look like they float
      if (Math.hypot(x - Layout.playerSpawn[0], z - Layout.playerSpawn[1]) < 6) continue;
      const chance = 0.08 + smoothstep(60, 100, d) * 0.5 + (Math.hypot(x - Layout.ruins[0], z - Layout.ruins[1]) < 25 ? 0.4 : 0);
      if (rng() > chance) continue;
      placed++;
      const big = d > 75 && rng() < 0.5;
      const s = big ? rng.range(2.2, 4.5) : rng.range(0.45, 1.9);
      const sc = new THREE.Vector3(s * rng.range(0.9, 1.4), s * rng.range(0.6, 1), s * rng.range(0.9, 1.3));
      q.setFromEuler(e.set(rng.range(-0.14, 0.14), rng() * 6.28, rng.range(-0.14, 0.14)));
      const col = useKit ? new THREE.Color().setScalar(rng.range(0.85, 1.2)) : rng() < 0.35 ? SW.rockMoss : (rng() < 0.5 ? SW.rock : SW.rockDark);
      // Seat the rock on the lowest ground under its footprint so no side hangs in the air.
      const rr = Math.max(sc.x, sc.z) * 0.8;
      const low = Math.min(h, T.heightAt(x + rr, z), T.heightAt(x - rr, z), T.heightAt(x, z + rr), T.heightAt(x, z - rr));
      const y = low - sc.y * 0.1;
      batch.add(rocks[rng.int(6)], m.compose(new THREE.Vector3(x, y, z), q, sc), col);
      if (s > 0.6) this.addCircle(x, z, Math.min(sc.x, sc.z) * 0.85, y + sc.y * 0.9);
    }
    const mat = useKit ? stoneDetailMaterial(new THREE.Color(1, 1, 1)) : new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.95 });
    mat.vertexColors = true;
    const mesh = new THREE.Mesh(batch.build(), mat);
    mesh.castShadow = mesh.receiveShadow = true;
    this.root.add(mesh);
  }

  scatterGrass() {
    // Instanced blades (render/grass.js): curved, sharp-tipped, textured in the shader, bent by wind and
    // by anything walking through them. Chunks of 20 m x 3 layers allow distance and quality culling.
    const rng = this.rng, T = this.terrain;
    const chunk = 20, spacing = 0.62;
    this.grassUniforms = this.grassUniforms || { uTime: { value: 0 } };
    this.grassField = new GrassField();
    this.grassField.uniforms.uTime = this.grassUniforms.uTime;
    const buckets = new Map();
    let index = 0;
    for (let z = -100; z < 100; z += spacing) {
      for (let x = -100; x < 100; x += spacing) {
        index++;
        const px = x + rng.range(-0.6, 0.6) * spacing, pz = z + rng.range(-0.6, 0.6) * spacing;
        if (Math.hypot(px, pz) > 98) continue;
        const h = T.heightAt(px, pz);
        if (h < 0.3 || h > 16 || T.slopeAt(px, pz) > 28) continue;
        let density = 0.8 * (1 - T.pathMask(px, pz) * 0.92) * (1 - T.forestMask(px, pz) * 0.45);
        if (this.nearLandmark(px, pz, 3, 5)) density *= 0.3;
        if (rng() > density) continue;
        const layer = index % 3;
        const ix = Math.floor(px / chunk), iz = Math.floor(pz / chunk);
        const key = `${ix}_${iz}_${layer}`;
        if (!buckets.has(key)) buckets.set(key, { blades: [], cx: (ix + 0.5) * chunk, cz: (iz + 0.5) * chunk, layer });
        const out = buckets.get(key).blades;
        const variant = rng.int(SW.grassTip.length);
        const tipCol = SW.grassTip[variant];
        const lush = 1 - T.forestMask(px, pz) * 0.3;
        const clumpH = rng.range(0.75, 1.25);
        const blades = 6 + rng.int(5);
        for (let i = 0; i < blades; i++) {
          const ang = rng() * Math.PI * 2, off = Math.sqrt(rng()) * 0.3;
          const bx = px + Math.cos(ang) * off, bz = pz + Math.sin(ang) * off;
          const k = rng.range(0.85, 1.12);
          out.push(bx, T.heightAt(bx, bz) - 0.02, bz, ang + rng.range(-0.6, 0.6),
            rng.range(0.24, 0.6) * clumpH * lush, rng.range(0.035, 0.065), rng.range(0.05, 0.32), rng(),
            tipCol.r * k, tipCol.g * k, tipCol.b * k);
        }
      }
    }
    for (const g of buckets.values()) {
      const mesh = this.grassField.buildChunk(g.blades, g.cx, g.cz, chunk);
      this.root.add(mesh);
      this.grassChunks.push({ mesh, cx: g.cx, cz: g.cz, layer: g.layer });
    }
  }

  /** Things that part the grass: [{x, y, z, r}] (the Warden, its recent trail, nearby creatures). */
  setGrassPushers(list) { if (this.grassField) this.grassField.setPushers(list); }

  scatterFlora() {
    const rng = this.rng, T = this.terrain;
    const batch = new GeometryBatch();
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler();
    const flowers = new CardBatch();
    const UP = new THREE.Vector3(0, 1, 0);
    const reed = new THREE.ConeGeometry(0.035, 1.3, 4).translate(0, 0.65, 0);
    const mstem = new THREE.CylinderGeometry(0.04, 0.05, 0.18, 6).translate(0, 0.09, 0);
    const cap = new THREE.SphereGeometry(0.5, 8, 5, 0, Math.PI * 2, 0, Math.PI / 2);
    for (let i = 0; i < 2600; i++) {
      const x = rng.range(-95, 95), z = rng.range(-95, 95);
      if (T.noise.fbm(x * 0.05, z * 0.05, 2, 10) < 0.52) continue;
      const h = T.heightAt(x, z);
      if (h < 0.5 || h > 12 || T.slopeAt(x, z) > 25 || T.pathMask(x, z) > 0.3) continue;
      // Wildflower patch: two crossed cards from the flower atlas (poppy, daisy, lavender, buttercup).
      const ci = clamp(Math.floor(T.noise.sample(x * 0.02, z * 0.02, 11) * 4), 0, 3);
      const sz = rng.range(0.42, 0.7), rot = rng() * Math.PI;
      const tone = new THREE.Color().setScalar(rng.range(0.85, 1.1));
      for (const a of [rot, rot + Math.PI / 2]) {
        const dx = Math.cos(a) * sz / 2, dz = Math.sin(a) * sz / 2, y0 = h - 0.03, y1 = h + sz * 0.95;
        flowers.quad(new THREE.Vector3(x - dx, y0, z - dz), new THREE.Vector3(x + dx, y0, z + dz), new THREE.Vector3(x + dx, y1, z + dz), new THREE.Vector3(x - dx, y1, z - dz),
          UP, tone, 1, [ci / 4 + 0.004, 0, (ci + 1) / 4 - 0.004, 1]);
      }
    }
    for (const t of this.treePositions) {
      if (rng() > 0.35) continue;
      for (let i = 0, n = 1 + rng.int(3); i < n; i++) {
        const a = rng() * 6.28, r = rng.range(0.7, 1.6);
        const x = t.x + Math.cos(a) * r, z = t.z + Math.sin(a) * r, h = T.heightAt(x, z) - 0.02, s = rng.range(0.7, 1.6);
        q.identity();
        batch.add(mstem, m.compose(new THREE.Vector3(x, h, z), q, new THREE.Vector3(s, s, s)), SW.mushStem);
        batch.add(cap, m.compose(new THREE.Vector3(x, h + 0.17 * s, z), q, new THREE.Vector3(0.24 * s, 0.14 * s, 0.24 * s)), SW.mushCap);
      }
    }
    for (let i = 0; i < 9000; i++) {
      const x = rng.range(-100, 100), z = rng.range(-100, 100), h = T.heightAt(x, z);
      if (h < -0.35 || h > 0.35) continue;
      for (let r = 0, n = 3 + rng.int(4); r < n; r++) {
        const rx = x + rng.range(-0.4, 0.4), rz = z + rng.range(-0.4, 0.4);
        q.setFromEuler(e.set(rng.range(-0.17, 0.17), 0, rng.range(-0.17, 0.17)));
        batch.add(reed, m.compose(new THREE.Vector3(rx, T.heightAt(rx, rz) - 0.05, rz), q, new THREE.Vector3(1, rng.range(0.6, 1.2), 1)), SW.reed);
      }
    }
    const mesh = new THREE.Mesh(batch.build(), new THREE.MeshLambertMaterial({ vertexColors: true }));
    mesh.receiveShadow = true;
    this.root.add(mesh);
    const fmat = new THREE.MeshStandardMaterial({ map: flowerAtlas(), vertexColors: true, alphaTest: 0.5, alphaToCoverage: true, side: THREE.DoubleSide, roughness: 0.7 });
    fmat.onBeforeCompile = (shader) => {
      shader.uniforms.uTime = this.grassUniforms.uTime;
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nattribute float sway;\nuniform float uTime;')
        .replace('#include <begin_vertex>', `#include <begin_vertex>
          float fw = sin(uTime * 1.5 + position.x * 0.3 + position.z * 0.2) * 0.06 * uv.y * sway;
          transformed.x += fw; transformed.z += fw * 0.5;`);
      shader.fragmentShader = shader.fragmentShader.replace('#include <normal_fragment_begin>', '#include <normal_fragment_begin>\n      normal = normalize(vNormal);');
    };
    const fmesh = new THREE.Mesh(flowers.build(), fmat);
    fmesh.receiveShadow = true;
    this.root.add(fmesh);
  }

  buildMotes() {
    const n = 60;
    this.motePositions = new Float32Array(n * 3);
    this.moteSeeds = Array.from({ length: n }, () => [Math.random() * 100, Math.random() * 100, Math.random() * 100]);
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(this.motePositions, 3));
    const c = document.createElement('canvas'); c.width = c.height = 32;
    const ctx = c.getContext('2d'); const grd = ctx.createRadialGradient(16, 16, 0, 16, 16, 16);
    grd.addColorStop(0, 'rgba(255,255,255,1)'); grd.addColorStop(0.4, 'rgba(255,255,255,0.5)'); grd.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = grd; ctx.fillRect(0, 0, 32, 32);
    this.moteMat = new THREE.PointsMaterial({ size: 0.18, map: new THREE.CanvasTexture(c), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, color: 0xfff2b0 });
    this.motes = new THREE.Points(geo, this.moteMat);
    this.motes.frustumCulled = false;
    this.scene.add(this.motes);
  }

  defineNests() {
    for (const n of Layout.nests) {
      const p = this.findWalkableNear(n.xz[0], n.xz[1], 8, 40) || new THREE.Vector3(n.xz[0], this.heightAt(n.xz[0], n.xz[1]), n.xz[1]);
      this.nests.push({ ...n, speciesId: n.speciesId || 'pebbloom', position: p, homeRadius: n.speciesId === 'thalorix' ? 14 : 11 });
    }
  }

  // ------------------------------------------------------------------ day / night + animation
  get isNight() { return this.hour < 5.5 || this.hour > 20; }

  setHour(h) { this.hour = ((h % 24) + 24) % 24; this.applyHour(); }

  applyHour() {
    const sunAngle = (this.hour - 6) / 12 * Math.PI;
    const elevation = Math.sin(sunAngle);
    this.daylight = clamp01(elevation * 2.2 + 0.25);
    const moon = elevation < -0.05;
    const ang = moon ? sunAngle + Math.PI : sunAngle;
    this.sunDir = new THREE.Vector3(Math.cos(ang) * 0.8, Math.max(0.12, Math.abs(Math.sin(ang))), 0.45).normalize();
    this.realSunDir = new THREE.Vector3(Math.cos(sunAngle) * 0.8, Math.sin(sunAngle), 0.45).normalize();
    const t = this.daylight;
    const dusk = 1 - Math.abs(elevation * 2.2 - 0.35) * 1.4; // peaks near sunrise/sunset
    const toon = ArtStyle.stylized;
    const warm = clamp01(dusk) * (moon ? 0 : 1) * (toon ? 0.6 : 1); // stylized: cleaner, less orange light
    const sunCol = new THREE.Color().lerpColors(srgb(1, 0.93, 0.8), srgb(1, 0.62, 0.32), warm);
    if (moon) sunCol.copy(srgb(0.55, 0.65, 1));
    this.sun.color.copy(sunCol);
    this.sun.intensity = moon ? 0.9 : lerp(2.2, 4.2, clamp01(elevation * 1.6));
    this.hemi.color.lerpColors(srgb(0.32, 0.4, 0.66), srgb(0.72, 0.84, 1), t); // blue, readable nights
    this.hemi.groundColor.lerpColors(srgb(0.1, 0.12, 0.18), srgb(0.35, 0.33, 0.24), t);
    const horizon = new THREE.Color().lerpColors(srgb(0.08, 0.12, 0.24), srgb(0.72, 0.84, 0.95), t).lerp(srgb(1, 0.6, 0.42), warm * 0.6);
    this.sunColor = sunCol;
    this.hemi.intensity = toon ? lerp(0.5, 0.78, t) : lerp(0.35, 0.4, t); // cel shading needs a brighter fill
    this.scene.fog.color.copy(horizon).lerp(srgb(0.9, 0.74, 0.55), (toon ? 0.2 : 0.35) + warm * 0.45); // atmospheric haze
    this.moteMat.color.copy(t < 0.35 ? srgb(0.75, 1, 0.45) : srgb(1, 0.95, 0.7));
    this.moteMat.size = t < 0.35 ? 0.28 : 0.16;
  }

  update(dt, cameraPos, focus, grassLayers, grassDistance) {
    this.time += dt;
    if (this.dayLengthMinutes > 0.01) {
      this.hour = (this.hour + dt * 24 / (this.dayLengthMinutes * 60)) % 24;
    }
    this.applyHour();
    // Shadows follow the player
    this.sun.target.position.copy(focus);
    this.sun.position.copy(focus).addScaledVector(this.sunDir, 120);
    this.skySystem.update(dt, this.realSunDir, cameraPos, this.sunColor, this.daylight);
    this.grassUniforms.uTime.value = this.time;
    if (this.waystoneCrystal) {
      this.waystoneCrystal.position.y = 2 + Math.sin(this.time * 1.3) * 0.18;
      this.waystoneCrystal.rotation.y += dt * 0.45;
    }
    // Water ripples + sun glints
    this.water.material.uniforms.time.value += dt * 0.8;
    this.water.material.uniforms.sunDirection.value.copy(this.realSunDir.y > 0 ? this.realSunDir : this.sunDir);
    this.water.material.uniforms.sunColor.value.copy(this.sunColor);
    // Grass culling by distance + quality layers
    for (const g of this.grassChunks) {
      // Density falls off with distance: extra layers only near the camera (keeps laptops fast).
      const d = Math.hypot(cameraPos.x - g.cx, cameraPos.z - g.cz) - 14;
      g.mesh.visible = g.layer < grassLayers && d < grassDistance * GRASS_LAYER_REACH[g.layer];
    }
    // Ambient motes around the camera
    const night = this.daylight < 0.35, speed = night ? 0.25 : 0.12;
    for (let i = 0; i < this.moteSeeds.length; i++) {
      const s = this.moteSeeds[i];
      this.motePositions[i * 3] = cameraPos.x + (Math.sin(s[0] + this.time * speed * 1.3) * 0.5 + Math.sin(s[1] * 2 + this.time * speed)) * 9;
      this.motePositions[i * 3 + 1] = cameraPos.y - 1 + Math.sin(s[1] + this.time * speed * 2) * 2.5;
      this.motePositions[i * 3 + 2] = cameraPos.z + (Math.cos(s[2] + this.time * speed * 1.1) * 0.5 + Math.cos(s[0] * 2 + this.time * speed)) * 9;
    }
    this.motes.geometry.attributes.position.needsUpdate = true;
  }
}

const EMPTY = [];

/** Quads with UVs, colours and a wind weight, merged into one mesh (leaf and needle cards). */
/** Screen-door dissolve for geometry right in front of the camera, so canopies never swallow the view. */
/** Also dissolves anything standing between the camera and the player (uFocusView = player chest in view space). */
export const occlusionUniforms = { uFocusView: { value: new THREE.Vector3(0, 0, 1) } };
function nearCameraFade(shader, near = 1.4, far = 3.6) {
  shader.uniforms.uFocusView = occlusionUniforms.uFocusView;
  shader.fragmentShader = shader.fragmentShader
    .replace('#include <common>', '#include <common>\nuniform vec3 uFocusView;')
    .replace('#include <clipping_planes_fragment>', `#include <clipping_planes_fragment>
    {
      vec2 fc = mod(floor(gl_FragCoord.xy), 4.0);
      float bayer = (mod(fc.x + fc.y * 2.0, 4.0) * 4.0 + mod(fc.x * 3.0 + fc.y, 4.0)) / 16.0 + 1.0 / 32.0;
      vec3 p = -vViewPosition;
      float keep = smoothstep(${near.toFixed(2)}, ${far.toFixed(2)}, length(p));
      float fl = dot(uFocusView, uFocusView);
      if (fl > 0.01) {
        float t = dot(p, uFocusView) / fl;
        if (t > 0.0 && t < 0.92) keep = min(keep, smoothstep(1.0, 2.2, length(p - uFocusView * t)));
      }
      if (keep < bayer) discard;
    }`);
}

class CardBatch {
  constructor() { this.pos = []; this.nor = []; this.uv = []; this.col = []; this.sway = []; this.idx = []; }
  quad(a, b, c, d, n, col, sway, uvRect = null) {
    const s = this.pos.length / 3;
    for (const v of [a, b, c, d]) { this.pos.push(v.x, v.y, v.z); this.nor.push(n.x, n.y, n.z); this.col.push(col.r, col.g, col.b); this.sway.push(sway); }
    const [u0, v0, u1, v1] = uvRect || [0, 0, 1, 1];
    this.uv.push(u0, v0, u1, v0, u1, v1, u0, v1);
    this.idx.push(s, s + 1, s + 2, s, s + 2, s + 3);
  }
  /** Leaf cluster card at p, facing roughly along `out` (randomly rotated), size in metres. */
  addCard(p, out, size, col, rng, sway) {
    const n = out.clone().lerp(new THREE.Vector3(0, 1, 0), 0.25).normalize();
    const facing = new THREE.Vector3(rng() * 2 - 1, rng() * 2 - 1, rng() * 2 - 1).normalize().lerp(out, 0.5).normalize();
    const u = new THREE.Vector3().crossVectors(facing, Math.abs(facing.y) > 0.9 ? new THREE.Vector3(1, 0, 0) : new THREE.Vector3(0, 1, 0)).normalize();
    const v = new THREE.Vector3().crossVectors(u, facing).normalize();
    u.applyAxisAngle(facing, rng() * Math.PI * 2); v.applyAxisAngle(facing, 0); v.crossVectors(u, facing).normalize();
    const h = size / 2;
    this.quad(p.clone().addScaledVector(u, -h).addScaledVector(v, -h), p.clone().addScaledVector(u, h).addScaledVector(v, -h),
      p.clone().addScaledVector(u, h).addScaledVector(v, h), p.clone().addScaledVector(u, -h).addScaledVector(v, h), n, col, Math.max(0.2, sway));
  }
  /** Drooping conifer branch card from p outward along `out`. */
  addBranch(p, out, length, width, col, sway) {
    const side = new THREE.Vector3(-out.z, 0, out.x).normalize();
    const base = p.clone().addScaledVector(out, -length * 0.45), tip = p.clone().addScaledVector(out, length * 0.55);
    const n = new THREE.Vector3(out.x, 0.8, out.z).normalize();
    this.quad(base.clone().addScaledVector(side, -width / 2), base.clone().addScaledVector(side, width / 2),
      tip.clone().addScaledVector(side, width / 2), tip.clone().addScaledVector(side, -width / 2), n, col, sway);
  }
  build() {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(this.nor, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(this.uv, 2));
    g.setAttribute('color', new THREE.Float32BufferAttribute(this.col, 3));
    g.setAttribute('sway', new THREE.Float32BufferAttribute(this.sway, 1));
    g.setIndex(this.idx);
    g.computeBoundingSphere();
    return g;
  }
}

/** Canvas-drawn sprig of sharp, serrated leaves (near-white, tinted per card by vertex colour). */
function leafTexture(size = 512) {
  const c = document.createElement('canvas'); c.width = c.height = size;
  const ctx = c.getContext('2d');
  const rng = makeRng(91);
  const leaf = (x, y, len, wid, ang, tone) => {
    ctx.save(); ctx.translate(x, y); ctx.rotate(ang);
    // Outline: rounded base, widest at ~35%, long acute tip, small serrations along both edges.
    const pts = (side) => {
      const out = [];
      for (let i = 0; i <= 14; i++) {
        const t = i / 14;
        let w = Math.sin(Math.pow(t, 0.75) * Math.PI) * (1 - t * 0.35) * wid;
        if (i > 1 && i < 14) w *= i % 2 ? 1.0 : 0.86; // serrated edge
        out.push([t * len, side * w]);
      }
      return out;
    };
    const top = pts(-1), bottom = pts(1).reverse();
    ctx.beginPath();
    ctx.moveTo(0, 0);
    for (const [px, py] of top) ctx.lineTo(px, py);
    for (const [px, py] of bottom) ctx.lineTo(px, py);
    ctx.closePath();
    const g = ctx.createLinearGradient(0, -wid, 0, wid);  // folded leaf: one half lighter
    g.addColorStop(0, `rgb(${tone + 30},${tone + 30},${tone + 18})`);
    g.addColorStop(0.5, `rgb(${tone},${tone},${tone - 12})`);
    g.addColorStop(1, `rgb(${tone - 50},${tone - 48},${tone - 60})`);
    ctx.fillStyle = g; ctx.fill();
    ctx.strokeStyle = 'rgba(40,40,20,0.55)'; ctx.lineWidth = 1.2;
    ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(len * 0.96, 0); ctx.stroke();             // midrib
    ctx.lineWidth = 0.7; ctx.strokeStyle = 'rgba(40,40,20,0.35)';
    for (let k = 1; k <= 5; k++) {                                                          // side veins
      const vx = len * k / 6.5;
      for (const sd of [-1, 1]) { ctx.beginPath(); ctx.moveTo(vx, 0); ctx.lineTo(vx + len * 0.12, sd * wid * 0.7 * (1 - k / 8)); ctx.stroke(); }
    }
    ctx.restore();
  };
  // A few twigs, each carrying alternate leaves on short stalks.
  for (let tw = 0; tw < 4; tw++) {
    const x0 = size * (0.15 + rng() * 0.7), y0 = size * (0.85 + rng() * 0.1), ang0 = -Math.PI / 2 + (rng() - 0.5) * 1.2;
    const L = size * (0.55 + rng() * 0.25);
    ctx.strokeStyle = 'rgb(110,95,70)'; ctx.lineWidth = 3;
    ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x0 + Math.cos(ang0) * L, y0 + Math.sin(ang0) * L); ctx.stroke();
    for (let k = 0; k < 9; k++) {
      const t = 0.12 + k * 0.1, side = k % 2 ? 1 : -1;
      const bx = x0 + Math.cos(ang0) * L * t, by = y0 + Math.sin(ang0) * L * t;
      const a = ang0 + side * (0.7 + rng() * 0.4);
      const len = size * (0.12 + rng() * 0.06) * (1 - t * 0.35), wid = len * (0.26 + rng() * 0.06);
      const sx = bx + Math.cos(a) * 8, sy = by + Math.sin(a) * 8;
      ctx.lineWidth = 1.6; ctx.beginPath(); ctx.moveTo(bx, by); ctx.lineTo(sx, sy); ctx.stroke(); // stalk
      leaf(sx, sy, len, wid, a, Math.round(170 + rng() * 70));
    }
    leaf(x0 + Math.cos(ang0) * L, y0 + Math.sin(ang0) * L, size * 0.12, size * 0.032, ang0, 220); // terminal leaf
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  return t;
}

/** Wildflower atlas, 4 cells: poppies, daisies, lavender, buttercups (stems + petals, alpha cut-out). */
function flowerAtlas() {
  const W = 1024, H = 256, cell = 256;
  const c = document.createElement('canvas'); c.width = W; c.height = H;
  const ctx = c.getContext('2d');
  const rng = makeRng(57);
  const stem = (x0, y0, x1, y1) => {
    ctx.strokeStyle = 'rgb(70,120,45)'; ctx.lineWidth = 3; ctx.lineCap = 'round';
    ctx.beginPath(); ctx.moveTo(x0, y0); ctx.quadraticCurveTo((x0 + x1) / 2 + (rng() - 0.5) * 20, (y0 + y1) / 2, x1, y1); ctx.stroke();
    // a couple of narrow leaves on the stem
    for (let k = 0; k < 2; k++) {
      const t = 0.25 + k * 0.25, lx = x0 + (x1 - x0) * t, ly = y0 + (y1 - y0) * t, sd = k ? 1 : -1;
      ctx.fillStyle = 'rgb(60,110,40)';
      ctx.beginPath(); ctx.moveTo(lx, ly); ctx.quadraticCurveTo(lx + sd * 16, ly - 10, lx + sd * 30, ly - 26); ctx.quadraticCurveTo(lx + sd * 10, ly - 8, lx, ly); ctx.fill();
    }
  };
  const petals = (x, y, n, len, wid, fill, rot = 0) => {
    for (let i = 0; i < n; i++) {
      const a = rot + i / n * Math.PI * 2;
      ctx.save(); ctx.translate(x, y); ctx.rotate(a);
      ctx.fillStyle = fill;
      ctx.beginPath(); ctx.moveTo(0, 0); ctx.quadraticCurveTo(len * 0.5, -wid, len, 0); ctx.quadraticCurveTo(len * 0.5, wid, 0, 0); ctx.fill();
      ctx.restore();
    }
  };
  for (let ci = 0; ci < 4; ci++) {
    const ox = ci * cell;
    for (let f = 0; f < 5; f++) {
      const bx = ox + 40 + rng() * 176, top = 40 + rng() * 90;
      stem(bx + (rng() - 0.5) * 20, H, bx, top);
      if (ci === 0) {        // poppy: four broad red petals, dark centre
        petals(bx, top, 4, 26, 18, `rgb(${210 + rng() * 40 | 0},40,35)`, rng());
        ctx.fillStyle = 'rgb(30,20,25)'; ctx.beginPath(); ctx.arc(bx, top, 6, 0, Math.PI * 2); ctx.fill();
      } else if (ci === 1) { // daisy: many slim white petals, yellow centre
        petals(bx, top, 14, 22, 5, 'rgb(250,250,244)', rng());
        ctx.fillStyle = 'rgb(240,190,40)'; ctx.beginPath(); ctx.arc(bx, top, 7, 0, Math.PI * 2); ctx.fill();
      } else if (ci === 2) { // lavender: spike of small purple buds
        for (let k = 0; k < 12; k++) {
          ctx.fillStyle = `rgb(${130 + rng() * 40 | 0},${90 + rng() * 30 | 0},${200 + rng() * 40 | 0})`;
          ctx.beginPath(); ctx.ellipse(bx + (k % 2 ? 4 : -4), top + k * 7, 5, 6, 0, 0, Math.PI * 2); ctx.fill();
        }
      } else {               // buttercup: five glossy yellow cupped petals
        petals(bx, top, 5, 17, 11, 'rgb(250,205,40)', rng());
        ctx.fillStyle = 'rgb(255,235,120)'; ctx.beginPath(); ctx.arc(bx, top, 5, 0, Math.PI * 2); ctx.fill();
      }
    }
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  return t;
}

/** Canvas-drawn conifer spray: branching twigs with fine, sharply tapered needles. */
function needleTexture(size = 512) {
  const c = document.createElement('canvas'); c.width = c.height = size;
  const ctx = c.getContext('2d');
  const rng = makeRng(93);
  const needle = (x, y, ang, len, tone) => {
    const w = 1.6, ca = Math.cos(ang), sa = Math.sin(ang);
    ctx.fillStyle = `rgb(${tone},${tone},${Math.round(tone * 0.9)})`;
    ctx.beginPath();
    ctx.moveTo(x - sa * w, y + ca * w); ctx.lineTo(x + sa * w, y - ca * w); ctx.lineTo(x + ca * len, y + sa * len);
    ctx.closePath(); ctx.fill();
  };
  const twig = (x, y, ang, len, depth) => {
    const ex = x + Math.cos(ang) * len, ey = y + Math.sin(ang) * len;
    for (let t = 0.04; t < 1; t += 0.035) {
      const px = x + (ex - x) * t, py = y + (ey - y) * t;
      const nl = size * 0.055 * (0.55 + 0.45 * Math.sin(Math.min(1, t * 1.2) * Math.PI)) * (depth ? 0.8 : 1);
      for (const sd of [-1, 1]) needle(px, py, ang + sd * (0.55 + rng() * 0.25) - 0.25, nl * (0.8 + rng() * 0.3), Math.round(150 + rng() * 105));
    }
    ctx.strokeStyle = 'rgb(105,85,62)'; ctx.lineWidth = depth ? 1.5 : 2.6;
    ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(ex, ey); ctx.stroke();
    if (depth < 1) for (let k = 0; k < 4; k++) {
      const t = 0.2 + k * 0.18, px = x + (ex - x) * t, py = y + (ey - y) * t;
      twig(px, py, ang + (k % 2 ? 0.75 : -0.75), len * 0.38, depth + 1);
    }
  };
  for (let i = 0; i < 2; i++) twig(size * (0.38 + i * 0.24), size * 0.98, -Math.PI / 2 + (i - 0.5) * 0.15, size * 0.92, 0);
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  return t;
}

/** Soft, lumpy leaf mass for broadleaf canopies (smooth normals read as foliage, not rock). */
function foliageGeometry(seed) {
  const noise = new Noise(seed, 3);
  let geo = new THREE.IcosahedronGeometry(1, 3);
  geo.deleteAttribute('normal');
  geo.deleteAttribute('uv');
  geo = mergeVertices(geo);
  const p = geo.attributes.position, v = new THREE.Vector3();
  for (let i = 0; i < p.count; i++) {
    v.fromBufferAttribute(p, i);
    const n = noise.sample(v.x * 2.2 + 3, v.z * 2.2, 0) * 0.6 + noise.sample(v.y * 4.5, v.x * 4.5, 1) * 0.4;
    v.multiplyScalar(0.82 + n * 0.42);
    if (v.y < -0.3) v.y = -0.3 + (v.y + 0.3) * 0.35;
    p.setXYZ(i, v.x, v.y, v.z);
  }
  geo.computeVertexNormals();
  return geo;
}

/** Tileable multi-octave value noise in R, G, B channels for terrain micro detail. */
function detailTexture(size = 256) {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext('2d');
  const img = ctx.createImageData(size, size);
  const rng = makeRng(77);
  const grids = [8, 16, 32, 64].map((g) => ({ g, v: Array.from({ length: g * g }, () => rng()) }));
  const sample = (grid, x, y) => {
    const g = grid.g, fx = x / size * g, fy = y / size * g;
    const ix = Math.floor(fx), iy = Math.floor(fy), tx = fx - ix, ty = fy - iy;
    const at = (a, b) => grid.v[((b % g + g) % g) * g + ((a % g + g) % g)];
    const sx = tx * tx * (3 - 2 * tx), sy = ty * ty * (3 - 2 * ty);
    return lerp(lerp(at(ix, iy), at(ix + 1, iy), sx), lerp(at(ix, iy + 1), at(ix + 1, iy + 1), sx), sy);
  };
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const r = sample(grids[1], x, y) * 0.35 + sample(grids[2], x, y) * 0.35 + sample(grids[3], x, y) * 0.3;
    const g2 = sample(grids[0], x, y) * 0.6 + sample(grids[1], x, y) * 0.4;
    const b = sample(grids[3], x, y);
    const o = (y * size + x) * 4;
    img.data[o] = r * 255; img.data[o + 1] = g2 * 255; img.data[o + 2] = b * 255; img.data[o + 3] = 255;
  }
  ctx.putImageData(img, 0, 0);
  const tex = new THREE.CanvasTexture(canvas);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.colorSpace = THREE.NoColorSpace;
  return tex;
}

let stoneDetailTex = null;
/** Stone material with tri-planar, world-space detail (grain, pits, lichen speckle) on top of vertex colour. */
function stoneDetailMaterial(tint) {
  stoneDetailTex = stoneDetailTex || detailTexture();
  const mat = new THREE.MeshStandardMaterial({ color: tint, vertexColors: true, roughness: 0.9, metalness: 0 });
  mat.onBeforeCompile = (shader) => {
    shader.uniforms.tDetail = { value: stoneDetailTex };
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', '#include <common>\nvarying vec3 vWPos;\nvarying vec3 vWNrm;')
      .replace('#include <begin_vertex>', '#include <begin_vertex>\nvWPos = (modelMatrix * vec4(transformed, 1.0)).xyz;\nvWNrm = normalize(mat3(modelMatrix) * objectNormal);');
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <common>', '#include <common>\nuniform sampler2D tDetail;\nvarying vec3 vWPos;\nvarying vec3 vWNrm;')
      .replace('#include <color_fragment>', `#include <color_fragment>
        vec3 bw = pow(abs(normalize(vWNrm)), vec3(4.0)); bw /= dot(bw, vec3(1.0));
        float d1 = texture2D(tDetail, vWPos.zy * 0.45).r * bw.x + texture2D(tDetail, vWPos.xz * 0.45).r * bw.y + texture2D(tDetail, vWPos.xy * 0.45).r * bw.z;
        float d2 = texture2D(tDetail, vWPos.zy * 1.9).g * bw.x + texture2D(tDetail, vWPos.xz * 1.9).g * bw.y + texture2D(tDetail, vWPos.xy * 1.9).g * bw.z;
        diffuseColor.rgb *= mix(0.62, 1.32, d1 * 0.55 + d2 * 0.45);`)
      .replace('#include <roughnessmap_fragment>', '#include <roughnessmap_fragment>\nroughnessFactor = clamp(roughnessFactor - 0.12 + (vWNrm.y > 0.6 ? 0.08 : 0.0), 0.5, 1.0);');
  };
  return mat;
}

/** Adds a uniform grey vertex-colour attribute so plain geometry works with vertex-coloured materials. */
function flatColors(geo, value = 1) {
  if (!geo.attributes.color) geo.setAttribute('color', new THREE.Float32BufferAttribute(new Float32Array(geo.attributes.position.count * 3).fill(value), 3));
  return geo;
}
