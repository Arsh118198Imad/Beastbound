// Instanced grass: one finely shaped blade (curved, tapering to a sharp tip) drawn many times.
// Every blade has its own position, facing, height, width, bend and colour; the vertex shader adds
// wind and bends blades away from "pushers" (the Warden, creatures and the Warden's recent trail).
import * as THREE from 'three';

export const MAX_PUSHERS = 16;

/** Blade template: x in [-0.5, 0.5] across, y in [0, 1] up. 9 vertices, 7 triangles, sharp tip. */
function bladeGeometry() {
  const rows = [0, 0.28, 0.55, 0.8];
  const pos = [], uv = [], idx = [];
  for (const y of rows) {
    const w = (1 - Math.pow(y, 1.5)) * (0.85 + 0.15 * Math.sin(Math.min(1, y * 3) * Math.PI / 2));
    pos.push(-0.5 * w, y, 0, 0.5 * w, y, 0);
    uv.push(0.5 - 0.5 * w, y, 0.5 + 0.5 * w, y);
  }
  pos.push(0, 1, 0); uv.push(0.5, 1);
  for (let r = 0; r < rows.length - 1; r++) {
    const a = r * 2;
    idx.push(a, a + 1, a + 3, a, a + 3, a + 2);
  }
  const last = (rows.length - 1) * 2;
  idx.push(last, last + 1, rows.length * 2);
  const g = new THREE.InstancedBufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx);
  return g;
}

export class GrassField {
  constructor() {
    this.template = bladeGeometry();
    this.uniforms = {
      uTime: { value: 0 },
      uPush: { value: Array.from({ length: MAX_PUSHERS }, () => new THREE.Vector4(0, -999, 0, 0)) },
    };
    this.material = this.makeMaterial();
  }

  /** blades: arrays [x, y, z, rot, height, width, bend, seed, r, g, b] per blade -> one draw call. */
  buildChunk(blades, cx, cz, size) {
    const n = blades.length / 11;
    const geo = new THREE.InstancedBufferGeometry();
    geo.index = this.template.index;
    geo.setAttribute('position', this.template.attributes.position);
    geo.setAttribute('uv', this.template.attributes.uv);
    const a = new Float32Array(n * 4), b = new Float32Array(n * 4), c = new Float32Array(n * 3);
    let maxH = 0, minY = Infinity, maxY = -Infinity;
    for (let i = 0; i < n; i++) {
      const o = i * 11;
      a.set([blades[o], blades[o + 1], blades[o + 2], blades[o + 3]], i * 4);
      b.set([blades[o + 4], blades[o + 5], blades[o + 6], blades[o + 7]], i * 4);
      c.set([blades[o + 8], blades[o + 9], blades[o + 10]], i * 3);
      maxH = Math.max(maxH, blades[o + 4]); minY = Math.min(minY, blades[o + 1]); maxY = Math.max(maxY, blades[o + 1]);
    }
    geo.setAttribute('aPos', new THREE.InstancedBufferAttribute(a, 4));
    geo.setAttribute('aShape', new THREE.InstancedBufferAttribute(b, 4));
    geo.setAttribute('aCol', new THREE.InstancedBufferAttribute(c, 3));
    geo.instanceCount = n;
    // Blades are placed in world space by the shader, so give the chunk its true bounds for culling.
    const cy = (minY + maxY) / 2;
    geo.boundingSphere = new THREE.Sphere(new THREE.Vector3(cx, cy, cz), Math.hypot(size * 0.75, (maxY - minY) / 2 + maxH + 1));
    geo.boundingBox = new THREE.Box3(new THREE.Vector3(cx - size, minY - 1, cz - size), new THREE.Vector3(cx + size, maxY + maxH + 1, cz + size));
    const mesh = new THREE.Mesh(geo, this.material);
    mesh.receiveShadow = true;
    mesh.castShadow = false;
    return mesh;
  }

  /** pushers: [{x, y, z, r}] — positions that flatten and part the grass. */
  setPushers(list) {
    const u = this.uniforms.uPush.value;
    for (let i = 0; i < MAX_PUSHERS; i++) {
      const p = list[i];
      if (p) u[i].set(p.x, p.y, p.z, p.r); else u[i].set(0, -999, 0, 0);
    }
  }

  makeMaterial() {
    const mat = new THREE.MeshStandardMaterial({ side: THREE.DoubleSide, roughness: 0.78, metalness: 0 });
    mat.onBeforeCompile = (shader) => {
      shader.uniforms.uTime = this.uniforms.uTime;
      shader.uniforms.uPush = this.uniforms.uPush;
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', `#include <common>
          attribute vec4 aPos;   // x, y, z, facing
          attribute vec4 aShape; // height, width, bend, seed
          attribute vec3 aCol;
          uniform float uTime;
          uniform vec4 uPush[${MAX_PUSHERS}];
          varying vec2 vBlade;
          varying vec3 vBladeCol;
          varying float vSeed;`)
        .replace('#include <beginnormal_vertex>', `
          vec2 bDir = vec2(sin(aPos.w), cos(aPos.w));
          vec2 bSide = vec2(bDir.y, -bDir.x);
          // Face normal tilted up, plus a fold along the midrib so each blade catches light on two planes.
          vec3 objectNormal = normalize(vec3(bDir.x, 0.0, bDir.y) * 0.55 + vec3(0.0, 0.85, 0.0) + vec3(bSide.x, 0.0, bSide.y) * sign(position.x) * 0.35);`)
        .replace('#include <begin_vertex>', `
          float hgt = aShape.x, curve = position.y * position.y;
          // Wind: slow sway plus a faster flutter that varies per blade.
          float wt = uTime * 1.35 + aPos.x * 0.19 + aPos.z * 0.23;
          float gust = sin(wt) * 0.6 + sin(wt * 2.7 + aShape.w * 6.283) * 0.25 + sin(uTime * 0.4 + aPos.x * 0.03) * 0.35;
          vec2 bend = bDir * aShape.z + normalize(vec2(1.0, 0.35)) * gust * 0.16;
          // Trampling: blades part away from nearby pushers and lie flatter.
          float flatten = 0.0;
          for (int i = 0; i < ${MAX_PUSHERS}; i++) {
            vec4 p = uPush[i];
            vec2 d = aPos.xz - p.xz;
            float l = length(d);
            float k = (1.0 - smoothstep(p.w * 0.3, p.w, l)) * step(abs(aPos.y - p.y), 1.6);
            bend += (l > 0.001 ? d / l : bDir) * k * 1.1;
            flatten = max(flatten, k);
          }
          float h = hgt * (1.0 - flatten * 0.6);
          vec2 off = bend * curve * h;
          vec3 transformed = aPos.xyz + vec3(bSide.x, 0.0, bSide.y) * position.x * aShape.y + vec3(0.0, position.y * h, 0.0) + vec3(off.x, 0.0, off.y);
          transformed.y -= dot(off, off) * 0.5 / max(h, 0.05); // keep blade length roughly constant when bent
          vBlade = uv;
          vBladeCol = aCol;
          vSeed = aShape.w;`);
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', '#include <common>\nvarying vec2 vBlade;\nvarying vec3 vBladeCol;\nvarying float vSeed;')
        .replace('#include <color_fragment>', `#include <color_fragment>
          // Blade texture: root-to-tip gradient, bright midrib, fine parallel veins, dry tips, ground shade.
          float bu = vBlade.x, bv = vBlade.y;
          vec3 gcol = mix(vBladeCol * 0.42, vBladeCol * 1.12, smoothstep(0.0, 0.85, bv));
          float rib = 1.0 - smoothstep(0.0, 0.06, abs(bu - 0.5));
          gcol *= 0.9 + 0.16 * rib + 0.05 * sin(bu * 70.0);
          gcol = mix(gcol, vec3(0.55, 0.5, 0.24), smoothstep(0.72, 1.0, bv) * step(0.74, vSeed) * 0.75);
          gcol *= mix(0.5, 1.0, smoothstep(0.0, 0.32, bv));
          diffuseColor.rgb = gcol;`)
        .replace('#include <normal_fragment_begin>', '#include <normal_fragment_begin>\n      normal = normalize(vNormal); // both faces lit (no black back faces)')
        .replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\n      totalEmissiveRadiance += diffuseColor.rgb * 0.1 * vBlade.y; // light glowing through thin blades');
    };
    return mat;
  }
}
