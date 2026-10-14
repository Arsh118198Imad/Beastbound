// Third-person orbit camera with collision, zoom, sprint FOV kick, shake and a title-screen orbit.
import * as THREE from './vendor/three.module.min.js';
import { clamp, lerp, damp } from './util.js';
import { Layout } from './world.js';

export class OrbitCamera {
  constructor(camera) {
    this.camera = camera;
    this.yaw = 0; this.pitch = 14 * Math.PI / 180;
    this.distance = 5.2; this.current = 5.2;
    this.sensitivity = 2.2; this.invertY = false; this.baseFov = 60;
    this.pivot = new THREE.Vector3();
    this.shakeAmount = 0; this.shakeUntil = 0; this.orbit = 0; this.mode = 'title';
  }

  snap(target, yaw) {
    this.yaw = yaw; this.pitch = 14 * Math.PI / 180;
    this.pivot.copy(target).y += 1.55;
    this.current = this.distance;
  }
  shake(amount, duration) { this.shakeAmount = Math.max(this.shakeAmount, amount); this.shakeUntil = Math.max(this.shakeUntil, performance.now() / 1000 + duration); }

  update(dt, game) {
    const cam = this.camera, world = game.world;
    if (this.mode === 'title') {
      this.orbit += dt * 4 * Math.PI / 180;
      const f = world.playerSpawn.clone().add(new THREE.Vector3(15, 0, 15));
      const pos = f.clone().add(new THREE.Vector3(Math.cos(this.orbit) * 55, 0, Math.sin(this.orbit) * 55));
      pos.y = Math.max(world.heightAt(pos.x, pos.z), 0) + 16;
      cam.position.copy(pos);
      cam.lookAt(f.x, f.y + 4, f.z);
      cam.fov = lerp(cam.fov, 55, damp(3, dt)); cam.updateProjectionMatrix();
      return;
    }
    const input = game.input, player = game.player;
    const k = this.sensitivity * Math.PI / 180;
    const m = input.lookMouse, s = input.lookStick;
    const ySign = this.invertY ? 1 : -1;
    this.yaw -= m.x * k + s.x * 190 * (this.sensitivity / 2.2) * dt * Math.PI / 180;
    this.pitch += ySign * (m.y * k + s.y * 133 * (this.sensitivity / 2.2) * dt * Math.PI / 180);
    this.pitch = clamp(this.pitch, -35 * Math.PI / 180, 70 * Math.PI / 180);
    const z = input.zoom;
    if (z) this.distance = clamp(this.distance - z * 0.7, 2.2, 9);

    const target = player.position.clone(); target.y += 1.55;
    this.pivot.lerp(target, damp(14, dt));
    const look = new THREE.Vector3(Math.sin(this.yaw) * Math.cos(this.pitch), -Math.sin(this.pitch), Math.cos(this.yaw) * Math.cos(this.pitch));
    const right = new THREE.Vector3(-Math.cos(this.yaw), 0, Math.sin(this.yaw)); // screen-right
    const pivot = this.pivot.clone().addScaledVector(right, 0.35);
    const desired = pivot.clone().addScaledVector(look, -this.distance);
    const t = world.cameraCast(pivot, desired, 0.3);
    const wanted = Math.max(0.6, this.distance * t);
    this.current = lerp(this.current, wanted, damp(wanted < this.current ? 25 : 4, dt));
    const pos = pivot.clone().addScaledVector(look, -this.current);
    const floor = Math.max(world.heightAt(pos.x, pos.z), Layout.waterLevel) + 0.35;
    if (pos.y < floor) pos.y = floor;
    if (performance.now() / 1000 < this.shakeUntil) {
      pos.x += (Math.random() - 0.5) * this.shakeAmount * 0.7;
      pos.y += (Math.random() - 0.5) * this.shakeAmount * 0.7;
      pos.z += (Math.random() - 0.5) * this.shakeAmount * 0.7;
    } else this.shakeAmount = 0;
    cam.position.copy(pos);
    cam.lookAt(pivot);
    const fov = this.baseFov + (player.running ? 6 : 0);
    cam.fov = lerp(cam.fov, fov, damp(5, dt));
    cam.updateProjectionMatrix();
  }
}
