// Art style switch. "Stylized" (default): cel-shaded light with soft two-step bands, ink outlines on
// characters and creatures, brighter colour grade. "Realistic": the smooth physically based look.
// The style is applied once at start-up (it patches shader code before anything compiles).
import * as THREE from 'three';

export const ArtStyles = ['Stylized', 'Realistic'];
export const ArtStyle = { name: 'Stylized', get stylized() { return this.name === 'Stylized'; } };

const DIFFUSE = 'float dotNL = saturate( dot( geometryNormal, directLight.direction ) );';
// Two soft-edged bands: lit / half-lit / shadow side, so forms read clearly from a distance.
// Must stay exactly 0 for N·L <= 0: three's specular term divides by ~0 at grazing angles and only the
// diffuse factor keeps it in check (otherwise silhouettes sparkle with white pixels).
const TOON = 'float nlRaw = dot( geometryNormal, directLight.direction ); float dotNL = smoothstep( 0.0, 0.1, nlRaw ) * 0.72 + smoothstep( 0.4, 0.5, nlRaw ) * 0.28;';

export function applyArtStyle(index) {
  ArtStyle.name = ArtStyles[index] || ArtStyles[0];
  if (!ArtStyle.stylized) return;
  for (const key of ['lights_physical_pars_fragment', 'lights_lambert_pars_fragment', 'lights_phong_pars_fragment']) {
    if (THREE.ShaderChunk[key] && THREE.ShaderChunk[key].includes(DIFFUSE)) THREE.ShaderChunk[key] = THREE.ShaderChunk[key].replace(DIFFUSE, TOON);
  }
}

const outlineMats = new Map();
function outlineMaterial(width) {
  if (!outlineMats.has(width)) {
    const m = new THREE.MeshBasicMaterial({ color: 0x15100c, side: THREE.BackSide });
    m.onBeforeCompile = (shader) => {
      shader.vertexShader = shader.vertexShader.replace('#include <begin_vertex>', `#include <begin_vertex>\ntransformed += normalize( normal ) * ${width.toFixed(4)};`);
    };
    outlineMats.set(width, m);
  }
  return outlineMats.get(width);
}

/** Inverted-hull ink outline for every (skinned) mesh under root. Width is in the model's own units. */
export function addOutlines(root, width = 0.012) {
  if (!ArtStyle.stylized) return;
  const meshes = [];
  root.traverse((o) => { if (o.isMesh && !o.userData.outline) meshes.push(o); });
  for (const o of meshes) {
    const shell = o.isSkinnedMesh ? new THREE.SkinnedMesh(o.geometry, outlineMaterial(width)) : new THREE.Mesh(o.geometry, outlineMaterial(width));
    if (o.isSkinnedMesh) shell.bind(o.skeleton, o.bindMatrix);
    shell.position.copy(o.position); shell.quaternion.copy(o.quaternion); shell.scale.copy(o.scale);
    shell.userData.outline = true;
    shell.castShadow = false; shell.receiveShadow = false; shell.frustumCulled = false;
    o.parent.add(shell);
  }
}
