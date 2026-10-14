// Unified keyboard + mouse (pointer lock) + gamepad input (port of Runtime/Controls/GameInput).
export class Input {
  constructor(canvas) {
    this.canvas = canvas;
    this.keys = new Set();
    this.pressed = new Set();      // keys pressed this frame
    this.mouseButtons = new Set();
    this.mousePressed = new Set();
    this.mouseDelta = { x: 0, y: 0 };
    this.wheel = 0;
    this.gamepadPrev = [];
    this.gamepadNow = [];
    this.gamepadAxes = [0, 0, 0, 0];
    this.lastDevice = 'kbm';
    this.sneakToggled = false;
    this.gameplayEnabled = false;
    this.locked = false;

    window.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      this.keys.add(e.code); this.pressed.add(e.code); this.lastDevice = 'kbm';
      if (['Tab', 'Space', 'ArrowUp', 'ArrowDown', 'F1'].includes(e.code) && document.activeElement?.tagName !== 'INPUT') e.preventDefault();
    });
    window.addEventListener('keyup', (e) => this.keys.delete(e.code));
    window.addEventListener('blur', () => { this.keys.clear(); this.mouseButtons.clear(); });
    canvas.addEventListener('mousedown', (e) => { this.mouseButtons.add(e.button); this.mousePressed.add(e.button); this.lastDevice = 'kbm'; });
    window.addEventListener('mouseup', (e) => this.mouseButtons.delete(e.button));
    window.addEventListener('mousemove', (e) => {
      if (!this.locked) return;
      this.mouseDelta.x += e.movementX; this.mouseDelta.y += e.movementY;
      if (Math.abs(e.movementX) + Math.abs(e.movementY) > 2) this.lastDevice = 'kbm';
    });
    canvas.addEventListener('wheel', (e) => { this.wheel += Math.sign(e.deltaY); e.preventDefault(); }, { passive: false });
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());
    document.addEventListener('pointerlockchange', () => {
      const was = this.locked;
      this.locked = document.pointerLockElement === canvas;
      // Browsers exit pointer lock on Esc; ignore that same Esc so it does not also close the pause menu.
      if (was && !this.locked) this.suppressEscUntil = performance.now() + 350;
    });
  }

  requestLock() {
    if (this.locked || !this.canvas.requestPointerLock) return;
    try {
      const r = this.canvas.requestPointerLock();
      if (r && r.catch) r.catch(() => {});
    } catch (e) { /* not allowed right now (e.g. browser cooldown); the user can click the game */ }
  }
  releaseLock() { if (document.pointerLockElement) document.exitPointerLock(); }

  /** Call once per frame before gameplay reads input. */
  poll() {
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    const pad = Array.from(pads || []).find((p) => p && p.connected);
    this.gamepadPrev = this.gamepadNow;
    if (pad) {
      this.gamepadNow = pad.buttons.map((b) => b.pressed || b.value > 0.5);
      this.gamepadAxes = pad.axes.slice(0, 4).map((v) => (Math.abs(v) < 0.15 ? 0 : (v - Math.sign(v) * 0.15) / 0.85));
      if (this.gamepadNow.some((b, i) => b && !this.gamepadPrev[i]) || this.gamepadAxes.some((a) => Math.abs(a) > 0.3)) this.lastDevice = 'pad';
    } else {
      this.gamepadNow = [];
      this.gamepadAxes = [0, 0, 0, 0];
    }
    if (this.keyPressed('KeyC') || this.padPressed(11)) this.sneakToggled = !this.sneakToggled;
  }

  /** Call at the end of each frame. */
  endFrame() {
    this.pressed.clear(); this.mousePressed.clear();
    this.mouseDelta.x = 0; this.mouseDelta.y = 0; this.wheel = 0;
  }

  keyPressed(code) {
    if (code === 'Escape' && performance.now() < (this.suppressEscUntil || 0)) return false;
    return this.pressed.has(code);
  }
  padHeld(i) { return !!this.gamepadNow[i]; }
  padPressed(i) { return !!this.gamepadNow[i] && !this.gamepadPrev[i]; }

  // Standard gamepad mapping: 0 A, 1 B, 2 X, 3 Y, 4 LB, 5 RB, 6 LT, 7 RT, 8 View, 9 Start, 10 L3, 11 R3, 12-15 D-pad
  get move() {
    if (!this.gameplayEnabled) return { x: 0, y: 0 };
    let x = 0, y = 0;
    if (this.keys.has('KeyW') || this.keys.has('ArrowUp')) y += 1;
    if (this.keys.has('KeyS') || this.keys.has('ArrowDown')) y -= 1;
    if (this.keys.has('KeyD') || this.keys.has('ArrowRight')) x += 1;
    if (this.keys.has('KeyA') || this.keys.has('ArrowLeft')) x -= 1;
    const sx = this.gamepadAxes[0] || 0, sy = -(this.gamepadAxes[1] || 0);
    if (sx * sx + sy * sy > x * x + y * y) { x = sx; y = sy; }
    const m = Math.hypot(x, y);
    return m > 1 ? { x: x / m, y: y / m } : { x, y };
  }
  get lookMouse() { return this.gameplayEnabled ? { x: this.mouseDelta.x * 0.1, y: -this.mouseDelta.y * 0.1 } : { x: 0, y: 0 }; }
  get lookStick() { return this.gameplayEnabled ? { x: this.gamepadAxes[2] || 0, y: -(this.gamepadAxes[3] || 0) } : { x: 0, y: 0 }; }
  get zoom() {
    if (!this.gameplayEnabled) return 0;
    if (this.wheel) return -Math.sign(this.wheel);
    if (this.padPressed(12)) return 1;
    if (this.padPressed(13)) return -1;
    return 0;
  }
  get run() { return this.gameplayEnabled && (this.keys.has('ShiftLeft') || this.keys.has('ShiftRight') || this.padHeld(4)); }
  get sneak() { return this.gameplayEnabled && (this.sneakToggled || this.keys.has('ControlLeft')); }
  get jumpPressed() { return this.gameplayEnabled && (this.keyPressed('Space') || this.padPressed(0)); }
  get jumpHeld() { return this.gameplayEnabled && (this.keys.has('Space') || this.padHeld(0)); }
  get attackPressed() { return this.gameplayEnabled && ((this.locked && this.mousePressed.has(0)) || this.keyPressed('KeyJ') || this.padPressed(2) || this.padPressed(7)); }
  get sparkPressed() { return this.gameplayEnabled && ((this.locked && this.mousePressed.has(2)) || this.keyPressed('KeyK') || this.padPressed(3) || this.padPressed(6)); }
  get dodgePressed() { return this.gameplayEnabled && (this.keyPressed('KeyQ') || this.padPressed(1)); }
  get bindPressed() { return this.gameplayEnabled && (this.keyPressed('KeyE') || this.padPressed(5)); }
  get pausePressed() { return this.keyPressed('Escape') || this.keyPressed('KeyP') || this.padPressed(9); }
  get bestiaryPressed() { return this.keyPressed('Tab') || this.padPressed(8); }
  get helpPressed() { return this.keyPressed('F1') || this.keyPressed('KeyH'); }
  get cancelPressed() { return this.keyPressed('Escape') || this.padPressed(1); }

  prompt(action) {
    const pad = this.lastDevice === 'pad';
    const map = {
      Bind: pad ? 'RB' : 'E', Attack: pad ? 'X' : 'LMB', Spark: pad ? 'Y' : 'RMB', Dodge: pad ? 'B' : 'Q',
      Sneak: pad ? 'R3' : 'Ctrl / C', Run: pad ? 'LB' : 'Shift', Jump: pad ? 'A' : 'Space', Bestiary: pad ? 'View' : 'Tab',
      Pause: pad ? 'Start' : 'Esc', Help: 'H',
    };
    return map[action] || action;
  }
}
