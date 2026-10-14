// Reflective water (three.js Water addon) with a procedurally generated, tileable ripple normal map.
import * as THREE from 'three';
import { Water } from 'three/addons/objects/Water.js';

function rippleNormalTexture(size = 256) {
  // Tileable height field from summed periodic waves, converted to a normal map.
  const h = new Float32Array(size * size);
  const waves = [];
  let s = 7;
  const rnd = () => { s = (s * 16807) % 2147483647; return s / 2147483647; };
  for (let i = 0; i < 24; i++) {
    const kx = Math.round((rnd() - 0.5) * 16), ky = Math.round((rnd() - 0.5) * 16);
    if (!kx && !ky) continue;
    waves.push([kx, ky, rnd() * Math.PI * 2, 1 / Math.hypot(kx, ky)]);
  }
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    let v = 0;
    for (const [kx, ky, ph, a] of waves) v += Math.sin(((kx * x + ky * y) / size) * Math.PI * 2 + ph) * a;
    h[y * size + x] = v;
  }
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext('2d');
  const img = ctx.createImageData(size, size);
  const at = (x, y) => h[((y + size) % size) * size + ((x + size) % size)];
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const dx = (at(x + 1, y) - at(x - 1, y)) * 1.2, dy = (at(x, y + 1) - at(x, y - 1)) * 1.2;
    const l = Math.hypot(dx, dy, 1);
    const o = (y * size + x) * 4;
    img.data[o] = (-dx / l * 0.5 + 0.5) * 255;
    img.data[o + 1] = (-dy / l * 0.5 + 0.5) * 255;
    img.data[o + 2] = (1 / l * 0.5 + 0.5) * 255;
    img.data[o + 3] = 255;
  }
  ctx.putImageData(img, 0, 0);
  const tex = new THREE.CanvasTexture(canvas);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  return tex;
}

export function createWater(size, resolution = 512) {
  const geo = new THREE.PlaneGeometry(size, size);
  const water = new Water(geo, {
    textureWidth: resolution,
    textureHeight: resolution,
    waterNormals: rippleNormalTexture(),
    sunDirection: new THREE.Vector3(0.3, 0.8, 0.2),
    sunColor: 0xfff0d8,
    waterColor: 0x0f3a46,
    distortionScale: 2.2,
    fog: true,
  });
  water.rotation.x = -Math.PI / 2;
  water.material.uniforms.size.value = 3.5;
  return water;
}
