// Small math + noise helpers shared by every module.
import * as THREE from 'three';

export const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
export const clamp01 = (v) => clamp(v, 0, 1);
export const lerp = (a, b, t) => a + (b - a) * t;
export const invLerp = (a, b, v) => clamp01((v - a) / (b - a));
export const smoothstep = (e0, e1, x) => {
  const t = clamp01((x - e0) / (e1 - e0));
  return t * t * (3 - 2 * t);
};
export const smoothMax = (a, b, k) => (a + b + Math.sqrt((a - b) * (a - b) + k)) * 0.5;
export const damp = (rate, dt) => 1 - Math.exp(-rate * dt);
export const moveTowards = (cur, target, maxDelta) =>
  Math.abs(target - cur) <= maxDelta ? target : cur + Math.sign(target - cur) * maxDelta;
export const angleDiffDeg = (a, b) => {
  let d = ((b - a) % 360 + 540) % 360 - 180;
  return d;
};

/** Seeded RNG (mulberry32). Same seed = same world. */
export function makeRng(seed) {
  let a = seed >>> 0;
  const next = () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  next.range = (min, max) => min + next() * (max - min);
  next.int = (n) => Math.floor(next() * n);
  return next;
}

/** Classic improved Perlin noise in [0,1], with seeded permutation and per-layer offsets. */
export class Noise {
  constructor(seed, layers = 16) {
    const rng = makeRng(seed);
    const p = new Uint8Array(256);
    for (let i = 0; i < 256; i++) p[i] = i;
    for (let i = 255; i > 0; i--) {
      const j = Math.floor(rng() * (i + 1));
      const t = p[i]; p[i] = p[j]; p[j] = t;
    }
    this.perm = new Uint8Array(512);
    for (let i = 0; i < 512; i++) this.perm[i] = p[i & 255];
    this.offsets = [];
    for (let i = 0; i < layers; i++) this.offsets.push([rng() * 512 - 256, rng() * 512 - 256]);
  }

  raw(x, y) {
    const perm = this.perm;
    const X = Math.floor(x) & 255, Y = Math.floor(y) & 255;
    x -= Math.floor(x); y -= Math.floor(y);
    const u = x * x * x * (x * (x * 6 - 15) + 10);
    const v = y * y * y * (y * (y * 6 - 15) + 10);
    const g = (h, gx, gy) => {
      switch (h & 7) {
        case 0: return gx + gy; case 1: return -gx + gy; case 2: return gx - gy; case 3: return -gx - gy;
        case 4: return gx; case 5: return -gx; case 6: return gy; default: return -gy;
      }
    };
    const aa = perm[perm[X] + Y], ab = perm[perm[X] + Y + 1];
    const ba = perm[perm[X + 1] + Y], bb = perm[perm[X + 1] + Y + 1];
    const x1 = lerp(g(aa, x, y), g(ba, x - 1, y), u);
    const x2 = lerp(g(ab, x, y - 1), g(bb, x - 1, y - 1), u);
    return lerp(x1, x2, v); // roughly [-1,1]
  }

  sample(x, z, layer = 0) {
    const o = this.offsets[layer % this.offsets.length];
    return clamp01(this.raw(x + o[0], z + o[1]) * 0.5 + 0.5);
  }

  fbm(x, z, octaves, layer = 0) {
    let amp = 1, freq = 1, sum = 0, norm = 0;
    for (let i = 0; i < octaves; i++) {
      sum += this.sample(x * freq, z * freq, layer + i) * amp;
      norm += amp; amp *= 0.5; freq *= 2;
    }
    return sum / norm;
  }

  ridged(x, z, octaves, layer = 0) {
    let amp = 1, freq = 1, sum = 0, norm = 0;
    for (let i = 0; i < octaves; i++) {
      const n = 1 - Math.abs(this.sample(x * freq, z * freq, layer + i) * 2 - 1);
      sum += n * n * amp; norm += amp; amp *= 0.5; freq *= 2;
    }
    return sum / norm;
  }
}

export function distanceToPolyline(px, pz, pts) {
  let best = Infinity;
  for (let i = 0; i < pts.length - 1; i++) {
    const ax = pts[i][0], az = pts[i][1], bx = pts[i + 1][0], bz = pts[i + 1][1];
    const abx = bx - ax, abz = bz - az;
    const t = clamp01(((px - ax) * abx + (pz - az) * abz) / Math.max(1e-4, abx * abx + abz * abz));
    const dx = ax + abx * t - px, dz = az + abz * t - pz;
    best = Math.min(best, Math.sqrt(dx * dx + dz * dz));
  }
  return best;
}

/** Colour given in sRGB 0..1 (or hex) -> THREE.Color in linear working space. */
export function srgb(r, g, b) {
  if (typeof r === 'number' && g === undefined) return new THREE.Color(r);
  return new THREE.Color().setRGB(r, g, b, THREE.SRGBColorSpace);
}

/**
 * Accumulates many small geometries into one BufferGeometry with per-vertex colour (one draw call).
 * Optional extra float attribute per vertex (e.g. "sway" for wind-animated grass).
 */
export class GeometryBatch {
  constructor(extraAttr = null) {
    this.pos = []; this.nor = []; this.col = []; this.idx = []; this.extra = extraAttr ? [] : null;
    this.extraName = extraAttr;
  }
  get vertexCount() { return this.pos.length / 3; }
  get empty() { return this.pos.length === 0; }

  /** Adds a THREE geometry transformed by a Matrix4, coloured with a THREE.Color (linear). */
  add(geo, matrix, color, extraValue = 0) {
    const g = geo.index ? geo : geo;
    const p = g.attributes.position, n = g.attributes.normal, vc = g.attributes.color;
    const start = this.vertexCount;
    const v = new THREE.Vector3(), nn = new THREE.Vector3();
    const nm = new THREE.Matrix3().getNormalMatrix(matrix);
    for (let i = 0; i < p.count; i++) {
      v.fromBufferAttribute(p, i).applyMatrix4(matrix);
      this.pos.push(v.x, v.y, v.z);
      if (n) { nn.fromBufferAttribute(n, i).applyMatrix3(nm).normalize(); this.nor.push(nn.x, nn.y, nn.z); }
      else this.nor.push(0, 1, 0);
      if (vc) this.col.push(color.r * vc.getX(i), color.g * vc.getY(i), color.b * vc.getZ(i));
      else this.col.push(color.r, color.g, color.b);
      if (this.extra) this.extra.push(extraValue);
    }
    if (g.index) {
      const ix = g.index.array;
      for (let i = 0; i < ix.length; i++) this.idx.push(start + ix[i]);
    } else {
      for (let i = 0; i < p.count; i++) this.idx.push(start + i);
    }
  }

  addTriangle(a, b, c, normal, ca, cb, cc, ea = 0, eb = 0, ec = 0) {
    const s = this.vertexCount;
    this.pos.push(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z);
    for (let i = 0; i < 3; i++) this.nor.push(normal.x, normal.y, normal.z);
    this.col.push(ca.r, ca.g, ca.b, cb.r, cb.g, cb.b, cc.r, cc.g, cc.b);
    if (this.extra) this.extra.push(ea, eb, ec);
    this.idx.push(s, s + 1, s + 2);
  }

  build() {
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
    geo.setAttribute('normal', new THREE.Float32BufferAttribute(this.nor, 3));
    geo.setAttribute('color', new THREE.Float32BufferAttribute(this.col, 3));
    if (this.extra) geo.setAttribute(this.extraName, new THREE.Float32BufferAttribute(this.extra, 1));
    geo.setIndex(this.idx);
    geo.computeBoundingSphere();
    return geo;
  }
}

/** Flat-shaded, noise-displaced icosahedron: stylised rock / canopy / mountain. */
export function rockGeometry(seed, roughness = 0.25, detail = 1) {
  const geo = new THREE.IcosahedronGeometry(1, detail);
  const noise = new Noise(seed, 2);
  const p = geo.attributes.position;
  const v = new THREE.Vector3();
  // Icosahedron geometry is non-indexed; displace by position so shared corners stay together.
  for (let i = 0; i < p.count; i++) {
    v.fromBufferAttribute(p, i);
    const n = noise.sample(v.x * 1.7, v.z * 1.7, 0) * 0.7 + noise.sample(v.y * 2.3, v.x * 2.3, 1) * 0.3;
    v.multiplyScalar(1 + (n - 0.5) * 2 * roughness);
    if (v.y < -0.35) v.y = -0.35 + (v.y + 0.35) * 0.2;
    p.setXYZ(i, v.x, v.y, v.z);
  }
  geo.computeVertexNormals(); // non-indexed => flat shading
  return geo;
}
