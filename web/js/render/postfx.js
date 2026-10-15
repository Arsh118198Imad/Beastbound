// Cinematic post-processing: MSAA HDR render -> bloom -> god rays + sharpen + grade + vignette -> tone map.
import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { ArtStyle } from './style.js';

const CinematicShader = {
  uniforms: {
    tDiffuse: { value: null },
    resolution: { value: new THREE.Vector2(1, 1) },
    sunUv: { value: new THREE.Vector2(0.5, 0.5) },
    sunStrength: { value: 0 },
    rayColor: { value: new THREE.Color(1, 0.8, 0.55) },
    rayThreshold: { value: 1.2 },
    sharpen: { value: 0.35 },
    saturation: { value: 1.12 },
    tint: { value: new THREE.Color(1.03, 1.0, 0.95) },
    vignette: { value: 0.45 },
    godRays: { value: 1 },
  },
  vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`,
  fragmentShader: `
    uniform sampler2D tDiffuse;
    uniform vec2 resolution, sunUv;
    uniform float sunStrength, rayThreshold, sharpen, saturation, vignette, godRays;
    uniform vec3 rayColor, tint;
    varying vec2 vUv;
    const vec3 LUM = vec3(0.2126, 0.7152, 0.0722);
    void main() {
      vec2 px = 1.0 / resolution;
      vec3 c = texture2D(tDiffuse, vUv).rgb;
      // Contrast-adaptive style sharpening (crisper foliage and edges).
      vec3 n = texture2D(tDiffuse, vUv + vec2(px.x, 0.0)).rgb + texture2D(tDiffuse, vUv - vec2(px.x, 0.0)).rgb
             + texture2D(tDiffuse, vUv + vec2(0.0, px.y)).rgb + texture2D(tDiffuse, vUv - vec2(0.0, px.y)).rgb;
      vec3 detail = c - n * 0.25;
      float lc = dot(c, LUM);
      c = max(c + detail * sharpen / (1.0 + lc), 0.0);
      // Screen-space god rays: march toward the sun, gathering bright sky pixels.
      if (godRays > 0.5 && sunStrength > 0.001) {
        const int SAMPLES = 40;
        vec2 delta = (vUv - sunUv) * (0.9 / float(SAMPLES));
        vec2 uv = vUv;
        float decay = 1.0;
        vec3 acc = vec3(0.0);
        for (int i = 0; i < SAMPLES; i++) {
          uv -= delta;
          vec3 s = texture2D(tDiffuse, clamp(uv, 0.0, 1.0)).rgb;
          acc += max(dot(s, LUM) - rayThreshold, 0.0) * decay;
          decay *= 0.965;
        }
        c += acc / float(SAMPLES) * rayColor * sunStrength * 1.6;
      }
      // Grade
      float l = dot(c, LUM);
      c = mix(vec3(l), c, saturation) * tint;
      float d = distance(vUv, vec2(0.5));
      c *= mix(1.0, smoothstep(0.95, 0.25, d), vignette);
      gl_FragColor = vec4(c, 1.0);
    }`,
};

export class PostFX {
  constructor(renderer, scene, camera) {
    this.renderer = renderer;
    this.camera = camera;
    const size = renderer.getSize(new THREE.Vector2());
    const pr = renderer.getPixelRatio();
    const target = new THREE.WebGLRenderTarget(size.x * pr, size.y * pr, { type: THREE.HalfFloatType, samples: 4 });
    this.composer = new EffectComposer(renderer, target);
    this.composer.addPass(new RenderPass(scene, camera));
    this.bloom = new UnrealBloomPass(new THREE.Vector2(size.x, size.y), 0.32, 0.55, 1.05);
    this.composer.addPass(this.bloom);
    this.cine = new ShaderPass(CinematicShader);
    this.composer.addPass(this.cine);
    this.composer.addPass(new OutputPass());
    this.enabled = true;
    this.tmp = new THREE.Vector3();
  }

  setSize(w, h, pixelRatio) {
    this.composer.setPixelRatio(pixelRatio);
    this.composer.setSize(w, h);
    this.cine.uniforms.resolution.value.set(w * pixelRatio, h * pixelRatio);
  }

  /** quality: 0 Low .. 3 Ultra */
  setQuality(q) {
    this.enabled = q > 0;
    this.bloom.enabled = q >= 1;
    this.cine.uniforms.godRays.value = q >= 1 ? 1 : 0;
    this.cine.uniforms.sharpen.value = q >= 2 ? 0.4 : 0.25;
    if (ArtStyle.stylized) {
      // Brighter, cleaner grade: more colour, light vignette, gentler sharpening and sun shafts.
      const u = this.cine.uniforms;
      u.saturation.value = 1.24; u.vignette.value = 0.28; u.sharpen.value *= 0.6; u.tint.value.setRGB(1.0, 1.0, 0.98);
      this.rayScale = 0.6;
    } else this.rayScale = 1;
  }

  update(sunDir, sunColor, daylight) {
    const cam = this.camera;
    const p = this.tmp.copy(cam.position).addScaledVector(sunDir, 500).project(cam);
    const forward = new THREE.Vector3();
    cam.getWorldDirection(forward);
    const facing = forward.dot(sunDir);
    const onScreen = 1 - THREE.MathUtils.smoothstep(Math.max(Math.abs(p.x), Math.abs(p.y)), 1.0, 1.8);
    this.cine.uniforms.sunUv.value.set(p.x * 0.5 + 0.5, p.y * 0.5 + 0.5);
    this.cine.uniforms.sunStrength.value = facing > 0 ? THREE.MathUtils.smoothstep(facing, 0.0, 0.5) * onScreen * daylight * (this.rayScale ?? 1) : 0;
    this.cine.uniforms.rayColor.value.copy(sunColor);
  }

  render() { this.composer.render(); }
}
