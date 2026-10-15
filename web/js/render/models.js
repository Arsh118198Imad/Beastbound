// Loads the Blender-made .glb models once and hands out independent (skinned) copies.
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';

const FILES = { thalorix: 'assets/models/thalorix.glb' };
const loaded = new Map();

export const Models = {
  /** Loads every model; a model that fails to load is simply unavailable (callers fall back). */
  async preload(onProgress) {
    const loader = new GLTFLoader();
    const names = Object.keys(FILES);
    let done = 0;
    await Promise.all(names.map(async (name) => {
      try {
        const gltf = await loader.loadAsync(FILES[name]);
        gltf.scene.traverse((o) => {
          if (!o.isMesh) return;
          o.castShadow = true;
          o.receiveShadow = true;
          o.frustumCulled = false; // poses (lying down, lunges) move far from the bind-pose bounds
        });
        loaded.set(name, gltf);
      } catch (e) {
        console.info(`Beastbound: model "${name}" unavailable (${e.message}); using fallback.`);
      }
      done++;
      if (onProgress) onProgress(done / names.length);
    }));
  },
  has(name) { return loaded.has(name); },
  /** Independent copy with its own skeleton and materials (so glow can pulse per creature). */
  instance(name) {
    const gltf = loaded.get(name);
    if (!gltf) return null;
    const root = SkeletonUtils.clone(gltf.scene);
    const materials = new Map();
    root.traverse((o) => {
      if (!o.isMesh) return;
      if (!materials.has(o.material)) materials.set(o.material, o.material.clone());
      o.material = materials.get(o.material);
    });
    const bones = {};
    root.traverse((o) => { if (o.isBone) bones[o.name] = o; });
    return { root, bones, materials: [...materials.values()] };
  },
};
