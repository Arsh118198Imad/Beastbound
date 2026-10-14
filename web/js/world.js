// Sunmeadow Vale: terrain, water, sky, day/night, vegetation, landmarks, colliders and nests.
// Port of Assets/Beastbound/Scripts/Runtime/World (same layout, same rules).
import * as THREE from './vendor/three.module.min.js';
import {
  clamp, clamp01, lerp, smoothstep, smoothMax, damp, Noise, makeRng, distanceToPolyline,
  srgb, GeometryBatch, rockGeometry,
} from './util.js';
import { Personality } from './rules.js';

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
    const lush = C(0.33, 0.58, 0.2), sun = C(0.58, 0.7, 0.27), forest = C(0.24, 0.38, 0.16);
    const dirt = C(0.62, 0.5, 0.34), rock = C(0.47, 0.47, 0.5), rockDark = C(0.33, 0.33, 0.37), snow = C(0.94, 0.96, 1);
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
  trunk: srgb(0.42, 0.29, 0.19), trunkDark: srgb(0.3, 0.21, 0.15), pine: srgb(0.16, 0.4, 0.24), pineDark: srgb(0.11, 0.3, 0.2),
  leaf: srgb(0.3, 0.56, 0.2), leafLight: srgb(0.5, 0.7, 0.25), autumn: srgb(0.88, 0.6, 0.2), blossom: srgb(0.98, 0.62, 0.74),
  blossomLight: srgb(1, 0.82, 0.88), rock: srgb(0.55, 0.55, 0.57), rockDark: srgb(0.4, 0.4, 0.44), rockMoss: srgb(0.42, 0.52, 0.33),
  stem: srgb(0.3, 0.5, 0.2), mushCap: srgb(0.86, 0.25, 0.2), mushStem: srgb(0.95, 0.9, 0.8), reed: srgb(0.55, 0.6, 0.3),
  flowers: [srgb(1, 0.85, 0.25), srgb(0.62, 0.45, 0.95), srgb(0.97, 0.96, 0.92), srgb(0.95, 0.32, 0.3), srgb(0.35, 0.62, 1)],
  grassRoot: [srgb(0.13, 0.3, 0.1), srgb(0.16, 0.33, 0.1), srgb(0.2, 0.35, 0.12), srgb(0.15, 0.28, 0.12), srgb(0.22, 0.36, 0.1), srgb(0.18, 0.31, 0.12)],
  grassTip: [srgb(0.48, 0.74, 0.28), srgb(0.6, 0.78, 0.3), srgb(0.72, 0.8, 0.36), srgb(0.42, 0.68, 0.3), srgb(0.78, 0.78, 0.4), srgb(0.55, 0.75, 0.32)],
};

// ====================================================================== World

export class World {
  constructor(scene, seed) {
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
    this.buildSky();
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
    this.hemi = new THREE.HemisphereLight(0xbfd9ff, 0x4d4a32, 1.1);
    this.scene.add(this.hemi);
    this.sun = new THREE.DirectionalLight(0xfff3e0, 2.6);
    this.sun.castShadow = true;
    this.sun.shadow.mapSize.set(2048, 2048);
    this.sun.shadow.bias = -0.0005;
    this.sun.shadow.normalBias = 0.04;
    const sc = this.sun.shadow.camera;
    sc.left = -45; sc.right = 45; sc.top = 45; sc.bottom = -45; sc.near = 1; sc.far = 260;
    this.scene.add(this.sun);
    this.scene.add(this.sun.target);
    this.scene.fog = new THREE.FogExp2(0xb8cde0, 0.0042);
  }

  buildSky() {
    this.skyUniforms = {
      topColor: { value: new THREE.Color() }, horizonColor: { value: new THREE.Color() },
      sunDir: { value: new THREE.Vector3(0, 1, 0) }, sunColor: { value: new THREE.Color() }, night: { value: 0 },
    };
    const mat = new THREE.ShaderMaterial({
      uniforms: this.skyUniforms, side: THREE.BackSide, depthWrite: false, fog: false,
      vertexShader: `varying vec3 vDir; void main(){ vDir = normalize(position); vec4 p = modelViewMatrix*vec4(position,1.0); gl_Position = projectionMatrix*p; gl_Position.z = gl_Position.w; }`,
      fragmentShader: `uniform vec3 topColor; uniform vec3 horizonColor; uniform vec3 sunDir; uniform vec3 sunColor; uniform float night; varying vec3 vDir;
        float hash(vec3 p){ p = fract(p*0.3183099+.1); p*=17.0; return fract(p.x*p.y*p.z*(p.x+p.y+p.z)); }
        void main(){ vec3 d = normalize(vDir); float h = clamp(d.y,0.0,1.0);
          vec3 col = mix(horizonColor, topColor, pow(h,0.55));
          col = mix(col, horizonColor*0.7, clamp(-d.y*3.0,0.0,1.0));
          float s = max(dot(d, normalize(sunDir)),0.0);
          col += sunColor*(pow(s,900.0)*6.0 + pow(s,12.0)*0.25);
          float star = step(0.9975, hash(floor(d*300.0))) * night * h;
          col += vec3(star);
          gl_FragColor = vec4(col,1.0);
          #include <colorspace_fragment>
        }`,
    });
    this.sky = new THREE.Mesh(new THREE.SphereGeometry(900, 32, 16), mat);
    this.sky.frustumCulled = false;
    this.sky.renderOrder = -1;
    this.scene.add(this.sky);
  }

  buildTerrain() {
    const geo = this.terrain.buildGeometry();
    const mat = new THREE.MeshStandardMaterial({ map: this.terrain.buildTexture(512), roughness: 0.95, metalness: 0 });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.receiveShadow = true;
    this.root.add(mesh);
    this.terrainMesh = mesh;
  }

  buildWater() {
    const geo = new THREE.PlaneGeometry(Layout.size, Layout.size, 64, 64);
    geo.rotateX(-Math.PI / 2);
    this.waterBase = Float32Array.from(geo.attributes.position.array);
    const mat = new THREE.MeshStandardMaterial({
      color: srgb(0.16, 0.42, 0.55), roughness: 0.12, metalness: 0.25, transparent: true, opacity: 0.82,
    });
    this.water = new THREE.Mesh(geo, mat);
    this.water.position.y = Layout.waterLevel;
    this.water.receiveShadow = true;
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
      this._stone = new THREE.MeshStandardMaterial({ color: srgb(0.7, 0.66, 0.58), roughness: 0.9 });
      this._moss = new THREE.MeshStandardMaterial({ color: srgb(0.45, 0.58, 0.32), roughness: 1 });
    }
    return this._stone;
  }

  block(parent, x, y, z, sx, sy, sz, rotY, mat, collide = true, rotZ = 0) {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(sx, sy, sz), mat || this.stoneMat());
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
    const ped = new THREE.Mesh(new THREE.CylinderGeometry(1.15, 1.5, 0.7, 8), this.stoneMat());
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
      const moss = new THREE.Mesh(new THREE.SphereGeometry(0.5, 10, 6), this._moss);
      moss.scale.set(1.05, 0.14, 0.7); moss.position.y = h / 2;
      s.add(moss);
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
    const lm = new THREE.Mesh(new THREE.SphereGeometry(0.5, 10, 6), moss);
    lm.scale.set(5, 0.3, 1.4); lm.position.y = 0.5; lintel.add(lm);
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
    const batch = new GeometryBatch();
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler();
    const trunkP = new THREE.CylinderGeometry(0.14, 0.28, 2.4, 7).translate(0, 1.2, 0);
    const trunkB = new THREE.CylinderGeometry(0.2, 0.34, 3, 7).translate(0, 1.5, 0);
    const cones = [new THREE.ConeGeometry(1.9, 3.2, 9).translate(0, 1.6, 0), new THREE.ConeGeometry(1.5, 2.8, 9).translate(0, 1.4, 0), new THREE.ConeGeometry(1.05, 2.4, 9).translate(0, 1.2, 0)];
    const canopies = [0, 1, 2, 3].map((i) => rockGeometry(this.seed + 100 + i, 0.16, 1));
    let placed = 0;
    for (let attempt = 0; attempt < 6000 && placed < 260; attempt++) {
      const x = rng.range(-125, 125), z = rng.range(-125, 125);
      const h = T.heightAt(x, z);
      if (h < 0.6 || h > 30 || T.slopeAt(x, z) > 30 || this.nearLandmark(x, z, 13, 11) || T.pathMask(x, z) > 0.1) continue;
      if (Math.hypot(x - Layout.playerSpawn[0], z - Layout.playerSpawn[1]) < 9) continue;
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
        batch.add(trunkP, m.compose(pos.clone().setY(h - 0.2), q, S), SW.trunkDark);
        batch.add(cones[0], m.compose(pos.clone().setY(h + 1.6 * s), q, S), SW.pineDark);
        batch.add(cones[1], m.compose(pos.clone().setY(h + 3.1 * s), q, S), SW.pine);
        batch.add(cones[2], m.compose(pos.clone().setY(h + 4.5 * s), q, S), SW.pineDark);
      } else {
        const blossom = this.nearLandmark(x, z, 40, 30) && rng() < 0.4;
        const main = blossom ? SW.blossom : (rng() < 0.06 ? SW.autumn : SW.leaf);
        const light = blossom ? SW.blossomLight : SW.leafLight;
        batch.add(trunkB, m.compose(pos.clone().setY(h - 0.2), q, S), SW.trunk);
        batch.add(canopies[rng.int(4)], m.compose(pos.clone().setY(h + 3.9 * s), q, new THREE.Vector3(2.4 * s, 1.9 * s, 2.4 * s)), main);
        const blobs = 3 + rng.int(2);
        for (let i = 0; i < blobs; i++) {
          const a = i / blobs * Math.PI * 2 + rng();
          const off = new THREE.Vector3(Math.cos(a) * 1.3, rng.range(-0.3, 0.6), Math.sin(a) * 1.3).multiplyScalar(s);
          const k = rng.range(1.3, 1.8) * s;
          batch.add(canopies[rng.int(4)], m.compose(pos.clone().setY(h + 3.5 * s).add(off), q, new THREE.Vector3(k, k, k)), i % 2 ? main : light);
        }
      }
      this.addCircle(x, z, 0.32 * s + 0.05, h + 4 * s);
    }
    const mesh = new THREE.Mesh(batch.build(), new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.85 }));
    mesh.castShadow = mesh.receiveShadow = true;
    this.root.add(mesh);
  }

  scatterRocks() {
    const rng = this.rng, T = this.terrain;
    const batch = new GeometryBatch();
    const rocks = [0, 1, 2, 3, 4, 5].map((i) => rockGeometry(this.seed + 200 + i, 0.3, 1));
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
      const col = rng() < 0.35 ? SW.rockMoss : (rng() < 0.5 ? SW.rock : SW.rockDark);
      // Seat the rock on the lowest ground under its footprint so no side hangs in the air.
      const rr = Math.max(sc.x, sc.z) * 0.8;
      const low = Math.min(h, T.heightAt(x + rr, z), T.heightAt(x - rr, z), T.heightAt(x, z + rr), T.heightAt(x, z - rr));
      const y = low - sc.y * 0.1;
      batch.add(rocks[rng.int(6)], m.compose(new THREE.Vector3(x, y, z), q, sc), col);
      if (s > 0.6) this.addCircle(x, z, Math.min(sc.x, sc.z) * 0.85, y + sc.y * 0.9);
    }
    const mesh = new THREE.Mesh(batch.build(), new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.95 }));
    mesh.castShadow = mesh.receiveShadow = true;
    this.root.add(mesh);
  }

  scatterGrass() {
    const rng = this.rng, T = this.terrain;
    const chunk = 40;
    const batches = new Map();
    const spacing = 1.25;
    let index = 0;
    const up = new THREE.Vector3(), a = new THREE.Vector3(), b = new THREE.Vector3(), tip = new THREE.Vector3();
    const tmpN = new THREE.Vector3();
    for (let z = -100; z < 100; z += spacing) {
      for (let x = -100; x < 100; x += spacing) {
        index++;
        const px = x + rng.range(-0.6, 0.6) * spacing, pz = z + rng.range(-0.6, 0.6) * spacing;
        if (Math.hypot(px, pz) > 98) continue;
        const h = T.heightAt(px, pz);
        if (h < 0.3 || h > 16 || T.slopeAt(px, pz) > 28) continue;
        let density = 0.75 * (1 - T.pathMask(px, pz) * 0.9) * (1 - T.forestMask(px, pz) * 0.45);
        if (this.nearLandmark(px, pz, 3, 5)) density *= 0.3;
        if (rng() > density) continue;
        const layer = index % 3;
        const key = `${Math.floor(px / chunk)}_${Math.floor(pz / chunk)}_${layer}`;
        if (!batches.has(key)) batches.set(key, { batch: new GeometryBatch('sway'), cx: (Math.floor(px / chunk) + 0.5) * chunk, cz: (Math.floor(pz / chunk) + 0.5) * chunk, layer });
        const batch = batches.get(key).batch;
        T.normalAt(px, pz, tmpN);
        up.copy(tmpN).lerp(new THREE.Vector3(0, 1, 0), 0.6).normalize();
        const variant = rng.int(SW.grassRoot.length);
        const blades = 3 + rng.int(3);
        for (let i = 0; i < blades; i++) {
          const ang = rng() * Math.PI * 2;
          const dx = Math.cos(ang), dz = Math.sin(ang);
          const off = rng() * 0.18;
          const bx = px + dx * off, bz = pz + dz * off, by = h - 0.03;
          const height = rng.range(0.32, 0.78), width = rng.range(0.06, 0.11);
          const lean = rng.range(0.05, 0.22);
          a.set(bx - dz * width / 2, by, bz + dx * width / 2);
          b.set(bx + dz * width / 2, by, bz - dx * width / 2);
          tip.set(bx + dx * lean, by + height, bz + dz * lean);
          batch.addTriangle(a, tip, b, up, SW.grassRoot[variant], SW.grassTip[variant], SW.grassRoot[variant], 0, height, 0);
        }
      }
    }
    this.grassUniforms = { uTime: { value: 0 } };
    const mat = new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide });
    mat.onBeforeCompile = (shader) => {
      shader.uniforms.uTime = this.grassUniforms.uTime;
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nattribute float sway;\nuniform float uTime;')
        .replace('#include <begin_vertex>', `#include <begin_vertex>
          float w = sin(uTime*1.6 + position.x*0.35 + position.z*0.25) + 0.5*sin(uTime*3.1 + position.x*0.9);
          transformed.x += w * 0.09 * sway; transformed.z += w * 0.05 * sway;`);
    };
    for (const g of batches.values()) {
      const mesh = new THREE.Mesh(g.batch.build(), mat);
      mesh.receiveShadow = true;
      this.root.add(mesh);
      this.grassChunks.push({ mesh, cx: g.cx, cz: g.cz, layer: g.layer });
    }
  }

  scatterFlora() {
    const rng = this.rng, T = this.terrain;
    const batch = new GeometryBatch();
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler();
    const stem = new THREE.CylinderGeometry(0.01, 0.014, 0.32, 4).translate(0, 0.16, 0);
    const head = new THREE.OctahedronGeometry(0.08, 0);
    const reed = new THREE.ConeGeometry(0.035, 1.3, 4).translate(0, 0.65, 0);
    const mstem = new THREE.CylinderGeometry(0.04, 0.05, 0.18, 6).translate(0, 0.09, 0);
    const cap = new THREE.SphereGeometry(0.5, 8, 5, 0, Math.PI * 2, 0, Math.PI / 2);
    for (let i = 0; i < 2600; i++) {
      const x = rng.range(-95, 95), z = rng.range(-95, 95);
      if (T.noise.fbm(x * 0.05, z * 0.05, 2, 10) < 0.52) continue;
      const h = T.heightAt(x, z);
      if (h < 0.5 || h > 12 || T.slopeAt(x, z) > 25 || T.pathMask(x, z) > 0.3) continue;
      const ci = clamp(Math.floor(T.noise.sample(x * 0.02, z * 0.02, 11) * 5), 0, 4);
      const s = rng.range(0.8, 1.4);
      q.setFromEuler(e.set(rng.range(-0.14, 0.14), rng() * 6.28, rng.range(-0.14, 0.14)));
      batch.add(stem, m.compose(new THREE.Vector3(x, h - 0.02, z), q, new THREE.Vector3(s, s, s)), SW.stem);
      batch.add(head, m.compose(new THREE.Vector3(x, h + 0.3 * s, z), q, new THREE.Vector3(s * 1.3, s * 0.6, s * 1.3)), SW.flowers[ci]);
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
      this.nests.push({ ...n, speciesId: 'pebbloom', position: p, homeRadius: 11 });
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
    const t = this.daylight;
    const dusk = 1 - Math.abs(elevation * 2.2 - 0.35) * 1.4; // peaks near sunrise/sunset
    const warm = clamp01(dusk) * (moon ? 0 : 1);
    const sunCol = new THREE.Color().lerpColors(srgb(1, 0.97, 0.9), srgb(1, 0.55, 0.3), warm);
    if (moon) sunCol.copy(srgb(0.55, 0.65, 1));
    this.sun.color.copy(sunCol);
    this.sun.intensity = moon ? 0.95 : lerp(0.8, 2.8, clamp01(elevation * 1.6));
    this.hemi.color.lerpColors(srgb(0.32, 0.4, 0.66), srgb(0.72, 0.84, 1), t); // blue, readable nights
    this.hemi.groundColor.lerpColors(srgb(0.1, 0.12, 0.18), srgb(0.35, 0.33, 0.24), t);
    this.hemi.intensity = lerp(1.05, 1.15, t);
    const top = new THREE.Color().lerpColors(srgb(0.02, 0.04, 0.12), srgb(0.24, 0.5, 0.86), t);
    const horizon = new THREE.Color().lerpColors(srgb(0.08, 0.12, 0.24), srgb(0.72, 0.84, 0.95), t).lerp(srgb(1, 0.6, 0.42), warm * 0.6);
    this.skyUniforms.topColor.value.copy(top);
    this.skyUniforms.horizonColor.value.copy(horizon);
    this.skyUniforms.sunDir.value.copy(this.sunDir);
    this.skyUniforms.sunColor.value.copy(moon ? srgb(0.5, 0.55, 0.7) : sunCol);
    this.skyUniforms.night.value = 1 - t;
    this.scene.fog.color.copy(horizon);
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
    this.sky.position.copy(cameraPos);
    this.grassUniforms.uTime.value = this.time;
    if (this.waystoneCrystal) {
      this.waystoneCrystal.position.y = 2 + Math.sin(this.time * 1.3) * 0.18;
      this.waystoneCrystal.rotation.y += dt * 0.45;
    }
    // Water swell (every other frame is plenty)
    this._waterTick = (this._waterTick || 0) + 1;
    if (this._waterTick % 2 === 0) {
      const p = this.water.geometry.attributes.position, base = this.waterBase, tt = this.time;
      for (let i = 0; i < p.count; i++) {
        const x = base[i * 3], z = base[i * 3 + 2];
        p.array[i * 3 + 1] = Math.sin(x * 0.25 + tt * 1.1) * 0.035 + Math.sin(z * 0.31 + tt * 0.8) * 0.03;
      }
      p.needsUpdate = true;
      this.water.geometry.computeVertexNormals();
    }
    // Grass culling by distance + quality layers
    for (const g of this.grassChunks) {
      const d = Math.hypot(cameraPos.x - g.cx, cameraPos.z - g.cz) - 29;
      g.mesh.visible = g.layer < grassLayers && d < grassDistance;
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
