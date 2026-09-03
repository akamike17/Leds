// Evaluador de animaciones en JS, espejo 1:1 de Domain/Rendering/AnimationEvaluator.cs
// (invariante 4: misma entrada → mismos píxeles entre preview y compilación).
// Puro/determinista: no usa reloj ni estado mutable; el viewport viaja como argumento.

const CycleLengths = {
  slow: 2000, normal: 1000, fast: 500,
};

const Slot = { Entrance: 'entrance', Main: 'main', Exit: 'exit' };
const Kind = { Fixed: 'fixed', Blink: 'blink', Marquee: 'marquee', Slide: 'slide', Pulse: 'pulse', Wipe: 'wipe', Frame: 'frame' };
const Dir = { Left: 'left', Right: 'right', Up: 'up', Down: 'down' };

function presetMs(speed) {
  return CycleLengths[speed] ?? 1000;
}

// animationKind/speedPreset/direction llegan como string (JSON camelCase);
// aquí sólo interesa el valor numérico del enum de C# si viene como número.
function cycleFor(def) {
  return presetMs(def.speedPreset);
}

export function resolveActive(animations, tMs, timing) {
  const list = (animations || []).filter(a => a != null);
  if (list.length === 0) return null;

  const start = timing?.start ?? 0;
  const end = timing?.end ?? 0;
  const dur = end - start;
  if (dur <= 0) return null;

  const local = tMs - start;
  const entrance = list.find(a => a.slot === 'entrance' || a.slot === 0);
  const exit = list.find(a => a.slot === 'exit' || a.slot === 2);
  const main = list.find(a => a.slot === 'main' || a.slot === 1) || list[0];

  const entranceEnd = dur / 5;
  const exitStart = dur * 4 / 5;

  if (entrance != null && local < entranceEnd) return entrance;
  if (exit != null && local >= exitStart) return exit;
  return main;
}

function kindOf(def) {
  const k = def.kind;
  if (typeof k === 'number') return k;
  return (Kind[k] ?? k) ?? 'fixed';
}

function dirOf(def) {
  const d = def.direction;
  if (d == null) return 'left';
  if (typeof d === 'number') return d;
  return Dir[d] ?? d ?? 'left';
}

// Devuelve { visible, offsetX, offsetY, brightness, clip }
export function evaluate(obj, tMs, viewportWidth = 32) {
  const timing = obj.timing || {};
  const start = timing.start ?? 0;
  const end = timing.end ?? 0;
  if (tMs < start || tMs >= end) {
    return { visible: false, offsetX: 0, offsetY: 0, brightness: 1, clip: null };
  }

  const local = tMs - start;
  const def = resolveActive(obj.animations, tMs, timing);
  if (def == null || kindOf(def) === 'fixed' || kindOf(def) === 0) {
    return { visible: true, offsetX: 0, offsetY: 0, brightness: 1, clip: null };
  }

  const cycle = cycleFor(def);

  switch (kindOf(def)) {
    case 'blink':
    case 1: {
      const half = Math.floor(cycle / 2);
      const visible = (Math.floor(local / (half || 1)) % 2) === 0;
      return { visible, offsetX: 0, offsetY: 0, brightness: 1, clip: null };
    }
    case 'pulse':
    case 4: {
      const phase = ((local % cycle) + cycle) % cycle / cycle;
      const b = 0.5 + 0.5 * Math.cos(2 * Math.PI * phase);
      return { visible: true, offsetX: 0, offsetY: 0, brightness: b, clip: null };
    }
    case 'slide':
    case 3: {
      const w = obj.size?.width ?? 0;
      let progress = Math.min(1, Math.max(0, local / cycle));
      if ((def.slot === 'exit' || def.slot === 2)) progress = 1 - progress;
      const amount = Math.round(progress * w);
      const dir = dirOf(def);
      if (dir === 'left' || dir === 0) return { visible: true, offsetX: amount, offsetY: 0, brightness: 1, clip: null };
      if (dir === 'right' || dir === 1) return { visible: true, offsetX: -amount, offsetY: 0, brightness: 1, clip: null };
      if (dir === 'up' || dir === 2) return { visible: true, offsetX: 0, offsetY: amount, brightness: 1, clip: null };
      if (dir === 'down' || dir === 3) return { visible: true, offsetX: 0, offsetY: -amount, brightness: 1, clip: null };
      return { visible: true, offsetX: 0, offsetY: 0, brightness: 1, clip: null };
    }
    case 'marquee':
    case 2: {
      const w = obj.size?.width ?? 0;
      const progress = ((local % cycle) + cycle) % cycle / cycle;
      const travel = w + viewportWidth;
      let off = Math.round(progress * travel);
      const dir = dirOf(def);
      if (dir === 'right' || dir === 1) off = travel - off;
      return { visible: true, offsetX: -off, offsetY: 0, brightness: 1, clip: null };
    }
    case 'wipe':
    case 5: {
      const w = obj.size?.width ?? 0, h = obj.size?.height ?? 0;
      const progress = Math.min(1, Math.max(0, local / cycle));
      const dir = dirOf(def);
      if (dir === 'left' || dir === 0) return { visible: true, offsetX: 0, offsetY: 0, brightness: 1, clip: { x: 0, y: 0, w: Math.round(w * progress), h } };
      if (dir === 'right' || dir === 1) { const sx = Math.round(w * (1 - progress)); return { visible: true, offsetX: 0, offsetY: 0, brightness: 1, clip: { x: sx, y: 0, w: w - sx, h } }; }
      if (dir === 'up' || dir === 2) return { visible: true, offsetX: 0, offsetY: 0, brightness: 1, clip: { x: 0, y: 0, w, h: Math.round(h * progress) } };
      if (dir === 'down' || dir === 3) { const sy = Math.round(h * (1 - progress)); return { visible: true, offsetX: 0, offsetY: 0, brightness: 1, clip: { x: 0, y: sy, w, h: h - sy } }; }
      return { visible: true, offsetX: 0, offsetY: 0, brightness: 1, clip: null };
    }
    case 'frame':
    case 6: {
      const stepMs = 125;
      const frameCount = Math.max(1, Math.floor(cycle / stepMs));
      const frame = Math.floor(local / stepMs) % frameCount;
      return { visible: (frame % 2) === 0, offsetX: 0, offsetY: 0, brightness: 1, clip: null };
    }
    default:
      return { visible: true, offsetX: 0, offsetY: 0, brightness: 1, clip: null };
  }
}