// Physical sky (three.js Sky addon), a drifting cloud layer, and image-based lighting from the sky.
import * as THREE from 'three';
import { Sky } from 'three/addons/objects/Sky.js';
import { ArtStyle } from './style.js';

const CLOUD_VS = `
varying vec3 vDir;
void main() {
  vDir = normalize(position);
  vec4 p = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  gl_Position = p;
  gl_Position.z = gl_Position.w * 0.99999;
}`;

const CLOUD_FS = `
uniform float uTime;
uniform vec3 uSunDir;
uniform vec3 uSunColor;
uniform vec3 uShadowColor;
uniform float uCover;
varying vec3 vDir;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
  vec2 i = floor(p), f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}
float fbm(vec2 p) {
  float s = 0.0, a = 0.5;
  for (int i = 0; i < 6; i++) { s += a * noise(p); p = p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
  return s;
}
void main() {
  vec3 d = normalize(vDir);
  if (d.y < 0.015) discard;
  // Project onto a flat cloud deck for natural perspective toward the horizon.
  vec2 uv = d.xz / (d.y + 0.12) * 1.4 + vec2(uTime * 0.006, uTime * 0.002);
  float n = fbm(uv);
  float cov = smoothstep(uCover, uCover + 0.32, n);
  if (cov < 0.01) discard;
  // Cheap self-shadowing: sample toward the sun.
  float n2 = fbm(uv + normalize(uSunDir.xz + 1e-4) * 0.08);
  float lit = clamp(0.55 + (n - n2) * 3.0, 0.0, 1.0);
  float toSun = pow(max(dot(d, normalize(uSunDir)), 0.0), 6.0);
  vec3 col = mix(uShadowColor, uSunColor, lit) + uSunColor * toSun * 1.5;
  float fade = smoothstep(0.015, 0.25, d.y);
  gl_FragColor = vec4(col, cov * fade * 0.92);
  #include <colorspace_fragment>
}`;

export class SkySystem {
  constructor(renderer, scene) {
    this.renderer = renderer;
    this.scene = scene;
    this.sky = new Sky();
    this.sky.scale.setScalar(1200);
    this.sky.frustumCulled = false;
    const u = this.sky.material.uniforms;
    u.turbidity.value = 7;
    u.rayleigh.value = 1.9;
    u.mieCoefficient.value = 0.008;
    u.mieDirectionalG.value = 0.86;
    scene.add(this.sky);

    this.cloudUniforms = {
      uTime: { value: 0 }, uSunDir: { value: new THREE.Vector3(0, 1, 0) },
      uSunColor: { value: new THREE.Color(1, 0.9, 0.75) }, uShadowColor: { value: new THREE.Color(0.45, 0.5, 0.6) },
      uCover: { value: 0.5 },
    };
    this.clouds = new THREE.Mesh(
      new THREE.SphereGeometry(1100, 48, 24, 0, Math.PI * 2, 0, Math.PI / 2),
      new THREE.ShaderMaterial({
        uniforms: this.cloudUniforms, vertexShader: CLOUD_VS, fragmentShader: CLOUD_FS,
        transparent: true, depthWrite: false, side: THREE.BackSide, fog: false,
      }),
    );
    this.clouds.frustumCulled = false;
    this.clouds.renderOrder = 1;
    scene.add(this.clouds);

    // Environment map (reflections + ambient light) rendered from a sky-only scene.
    this.pmrem = new THREE.PMREMGenerator(renderer);
    this.envScene = new THREE.Scene();
    this.envSky = new Sky();
    this.envSky.material = this.sky.material;
    this.envSky.scale.setScalar(100);
    this.envScene.add(this.envSky);
    this.envTarget = null;
    this.lastEnvSun = new THREE.Vector3(9, 9, 9);
  }

  /** realSunDir: true sun direction (may be below the horizon). */
  update(dt, realSunDir, cameraPos, sunColor, daylight) {
    this.sky.position.copy(cameraPos);
    this.clouds.position.copy(cameraPos);
    this.sky.material.uniforms.sunPosition.value.copy(realSunDir);
    this.cloudUniforms.uTime.value += dt;
    this.cloudUniforms.uSunDir.value.copy(realSunDir.y > -0.05 ? realSunDir : new THREE.Vector3(0.2, 1, 0.1));
    const night = 1 - daylight;
    this.cloudUniforms.uSunColor.value.copy(sunColor).multiplyScalar(THREE.MathUtils.lerp(1.6, 0.12, night));
    this.cloudUniforms.uShadowColor.value.setRGB(0.32, 0.36, 0.46).multiplyScalar(THREE.MathUtils.lerp(1, 0.08, night));
    // Re-bake image-based lighting when the sun has moved noticeably.
    if (realSunDir.distanceTo(this.lastEnvSun) > 0.04) {
      this.lastEnvSun.copy(realSunDir);
      if (this.envTarget) this.envTarget.dispose();
      this.envTarget = this.pmrem.fromScene(this.envScene, 0, 0.1, 200);
      this.scene.environment = this.envTarget.texture;
      this.scene.environmentIntensity = THREE.MathUtils.lerp(0.55, 0.22, night) * (ArtStyle.stylized ? 1.35 : 1);
    }
  }
}
