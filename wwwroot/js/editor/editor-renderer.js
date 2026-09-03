// Renderer del editor (Canvas 2D). Debe coincidir píxel a píxel con
// Domain/Rendering/SceneRenderer.cs (invariante 4: misma entrada → mismos píxeles).
import { Font5x7 } from './font5x7.js';

function toCss(c) {
  return `#${c.r.toString(16).padStart(2, '0')}${c.g.toString(16).padStart(2, '0')}${c.b.toString(16).padStart(2, '0')}`;
}

export class Renderer {
  constructor(canvas, ctx) {
    this.canvas = canvas;
    this.ctx = ctx;
    this.scale = 10;
  }

  renderScene(scene, timeMs) {
    const { ctx, canvas } = this;
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    const layers = [...(scene.layers || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
    for (const layer of layers) {
      if (layer.visible === false) continue;
      for (const obj of layer.objects || []) {
        if (obj.visible === false) continue;
        const start = obj.timing?.start ?? 0;
        const end = obj.timing?.end ?? (scene.duration ?? 0);
        if (timeMs < start || timeMs >= end) continue;
        this.renderObject(obj);
      }
    }
  }

  renderObject(obj) {
    switch (obj.kind) {
      case 'text': this.renderText(obj); break;
      case 'drawing': this.renderDrawing(obj); break;
      case 'shape': this.renderShape(obj); break;
      case 'icon':
      case 'image': break; // slices posteriores
    }
  }

  renderText(t) {
    if (!t.text) return;
    const ctx = this.ctx;
    ctx.fillStyle = toCss(t.color || { r: 255, g: 255, b: 255 });
    let x = t.position?.x ?? 0;
    const y = t.position?.y ?? 0;
    for (const ch of t.text) {
      const g = Font5x7.get(ch);
      if (!g) { x += 6; continue; }
      for (let row = 0; row < 7; row++) {
        const bits = g[row];
        for (let col = 0; col < 5; col++) {
          if (bits & (1 << col)) ctx.fillRect(x + col, y + row, 1, 1);
        }
      }
      x += 6;
    }
  }

  renderDrawing(d) {
    const ctx = this.ctx;
    const color = toCss((d.palette && d.palette[0]) || { r: 255, g: 255, b: 255 });
    ctx.fillStyle = color;
    const w = d.size?.width ?? 0;
    const h = d.size?.height ?? 0;
    const px = d.position?.x ?? 0;
    const py = d.position?.y ?? 0;
    const data = d.pixelData || [];
    for (let y = 0; y < h; y++)
      for (let x = 0; x < w; x++) {
        if (data[y * w + x]) ctx.fillRect(px + x, py + y, 1, 1);
      }
  }

  renderShape(s) {
    const ctx = this.ctx;
    const x = s.position?.x ?? 0, y = s.position?.y ?? 0;
    const w = s.size?.width ?? 0, h = s.size?.height ?? 0;
    const stroke = toCss(s.strokeColor || { r: 255, g: 255, b: 255 });
    const fill = toCss(s.fillColor || { r: 0, g: 0, b: 0 });
    if (s.shape === 'rectangle' || s.shape === 0) {
      for (let i = 0; i < w; i++) for (let j = 0; j < h; j++) {
        const border = i === 0 || i === w - 1 || j === 0 || j === h - 1;
        ctx.fillStyle = border ? stroke : fill;
        if (border || s.filled) ctx.fillRect(x + i, y + j, 1, 1);
      }
    } else if (s.shape === 'ellipse' || s.shape === 2) {
      const cx = x + (w - 1) / 2, cy = y + (h - 1) / 2;
      const rx = (w - 1) / 2, ry = (h - 1) / 2;
      for (let i = 0; i < w; i++) for (let j = 0; j < h; j++) {
        const nx = (i - cx) / Math.max(rx, 0.5), ny = (j - cy) / Math.max(ry, 0.5);
        const v = nx * nx + ny * ny;
        if (v <= 1) {
          const border = v >= 0.65;
          ctx.fillStyle = border ? stroke : fill;
          if (s.filled || border) ctx.fillRect(x + i, y + j, 1, 1);
        }
      }
    } else if (s.shape === 'line' || s.shape === 1) {
      // Bresenham
      let x0 = x, y0 = y, x1 = x + w - 1, y1 = y + h - 1;
      const dx = Math.abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
      const dy = -Math.abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
      let err = dx + dy;
      ctx.fillStyle = stroke;
      for (;;) {
        ctx.fillRect(x0, y0, 1, 1);
        if (x0 === x1 && y0 === y1) break;
        const e2 = 2 * err;
        if (e2 >= dy) { err += dy; x0 += sx; }
        if (e2 <= dx) { err += dx; y0 += sy; }
      }
    }
  }
}