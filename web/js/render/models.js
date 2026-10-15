// Loads the Blender-made .glb models once and hands out independent (skinned) copies.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';
import { addOutlines } from './style.js';

const FILES = {
  thalorix: 'assets/models/thalorix.glb', pebbloom: 'assets/models/pebbloom.glb', warden: 'assets/models/warden.glb',
  env: 'assets/models/env.glb', // rocks and cut-stone blocks (static, vertex-coloured)
};
const loaded = new Map();
const OUTLINE = { thalorix: 0.011, pebbloom: 0.009, warden: 0.007 }; // ink line width in model units

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
  /** The geometry of one named mesh inside a loaded model file (static props), or null. */
  geometry(name, meshName) {
    const gltf = loaded.get(name);
    const o = gltf && gltf.scene.getObjectByName(meshName);
    return o && o.isMesh ? o.geometry : null;
  },
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
    addOutlines(root, OUTLINE[name] || 0.009); // stylized art style only
    return { root, bones, materials: [...materials.values()] };
  },
};

const toRad = Math.PI / 180;

/**
 * Poses named bones in model space, so procedural animation code can say "pitch this leg 30° forward"
 * without caring how each bone's local axes were exported. Rotations use the rig's rest pose as base.
 */
export class BonePoser {
  constructor(root, bones) {
    this.bones = bones;
    root.updateMatrixWorld(true);
    this.rest = {}; this.restPos = {}; this.parentRot = {}; this.parentInv = {};
    const tmp = new THREE.Quaternion();
    const rootInv = root.getWorldQuaternion(new THREE.Quaternion()).invert(); // measure relative to the model root
    for (const [name, b] of Object.entries(bones)) {
      this.rest[name] = b.quaternion.clone();
      this.restPos[name] = b.position.clone();
      b.parent.getWorldQuaternion(tmp).premultiply(rootInv);
      this.parentRot[name] = tmp.clone();
      this.parentInv[name] = tmp.clone().invert();
    }
    this.q = new THREE.Quaternion(); this.e = new THREE.Euler(); this.v = new THREE.Vector3();
  }
  /** Model-space rotation in degrees (x pitches forward/down, y turns, z rolls), applied over the rest pose. */
  set(name, x, y = 0, z = 0) {
    const b = this.bones[name];
    if (!b) return;
    this.q.setFromEuler(this.e.set(x * toRad, y * toRad, z * toRad, 'YXZ'));
    b.quaternion.copy(this.parentInv[name]).multiply(this.q).multiply(this.parentRot[name]).multiply(this.rest[name]);
  }
  /** Model-space translation offset from the rest position (metres, before the model's own scale). */
  offset(name, x, y, z) {
    const b = this.bones[name];
    if (!b) return;
    b.position.copy(this.restPos[name]).add(this.v.set(x, y, z).applyQuaternion(this.parentInv[name]));
  }
}
