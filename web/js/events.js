// Tiny event hub for cross-system news (toasts, objectives, battle state).
const handlers = new Map();

export const Events = {
  on(name, fn) {
    if (!handlers.has(name)) handlers.set(name, []);
    handlers.get(name).push(fn);
    return () => this.off(name, fn);
  },
  off(name, fn) {
    const list = handlers.get(name);
    if (list) { const i = list.indexOf(fn); if (i >= 0) list.splice(i, 1); }
  },
  emit(name, ...args) {
    const list = handlers.get(name);
    if (!list) return;
    for (const fn of [...list]) {
      try { fn(...args); } catch (e) { console.error(`[Beastbound] handler for "${name}" failed`, e); }
    }
  },
  notify(message, kind = 'info') { this.emit('notify', message, kind); },
};
