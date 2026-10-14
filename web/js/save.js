// Local saves in the browser's localStorage only. No accounts, no servers, no tracking.
const JOURNEY = 'beastbound.journey.v1';
const BACKUP = 'beastbound.journey.v1.bak';
const SETTINGS = 'beastbound.settings.v1';

export const QualityNames = ['Low', 'Medium', 'High', 'Ultra'];
export const FrameCaps = [30, 60, 120, 0];

export function defaultSettings() {
  return {
    mouseSensitivity: 2.2, invertY: false, fov: 60, master: 0.8, sfx: 0.9, music: 0.6,
    quality: 2, dayLength: 24, showFps: false, showHint: true, frameCap: 3,
  };
}

function storage() {
  try { const s = window.localStorage; s.setItem('__bb', '1'); s.removeItem('__bb'); return s; } catch (e) { return null; }
}

export const Saves = {
  available: !!storage(),
  hasSave() { const s = storage(); return !!(s && (s.getItem(JOURNEY) || s.getItem(BACKUP))); },
  save(data) {
    const s = storage();
    if (!s) return 'Browser storage is disabled, so progress cannot be saved.';
    try {
      data.version = 1; data.savedAt = new Date().toISOString();
      const prev = s.getItem(JOURNEY);
      if (prev) s.setItem(BACKUP, prev);
      s.setItem(JOURNEY, JSON.stringify(data));
      return null;
    } catch (e) { return e.message || 'Could not save.'; }
  },
  load() {
    const s = storage();
    if (!s) return { data: null, error: 'Browser storage is disabled.' };
    for (const key of [JOURNEY, BACKUP]) {
      try {
        const raw = s.getItem(key);
        if (!raw) continue;
        const d = JSON.parse(raw);
        if (d && typeof d === 'object') {
          d.collection = Array.isArray(d.collection) ? d.collection.filter((r) => r && r.uid && r.speciesId) : [];
          d.capturedNests = Array.isArray(d.capturedNests) ? d.capturedNests : [];
          d.seenSpecies = Array.isArray(d.seenSpecies) ? d.seenSpecies : [];
          return { data: d, error: null };
        }
      } catch (e) { /* try backup */ }
    }
    return { data: null, error: 'No saved journey found.' };
  },
  deleteAll() { const s = storage(); if (s) { s.removeItem(JOURNEY); s.removeItem(BACKUP); } },
  loadSettings() {
    const d = defaultSettings();
    const s = storage();
    try { Object.assign(d, JSON.parse((s && s.getItem(SETTINGS)) || '{}')); } catch (e) { /* defaults */ }
    return sanitize(d);
  },
  saveSettings(st) { const s = storage(); if (s) try { s.setItem(SETTINGS, JSON.stringify(sanitize(st))); } catch (e) { /* ignore */ } },
};

function sanitize(s) {
  const c = (v, a, b, f) => (Number.isFinite(v) ? Math.min(b, Math.max(a, v)) : f);
  s.mouseSensitivity = c(s.mouseSensitivity, 0.2, 8, 2.2);
  s.fov = c(s.fov, 50, 100, 60);
  s.master = c(s.master, 0, 1, 0.8); s.sfx = c(s.sfx, 0, 1, 0.9); s.music = c(s.music, 0, 1, 0.6);
  s.dayLength = c(s.dayLength, 2, 120, 24);
  s.quality = Number.isInteger(s.quality) && s.quality >= 0 && s.quality < 4 ? s.quality : 2;
  s.frameCap = Number.isInteger(s.frameCap) && s.frameCap >= 0 && s.frameCap < FrameCaps.length ? s.frameCap : 3;
  s.invertY = !!s.invertY; s.showFps = !!s.showFps; s.showHint = s.showHint !== false;
  return s;
}
