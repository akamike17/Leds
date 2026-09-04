// Renderer del editor (Canvas 2D). Debe coincidir píxel a píxel con
// Domain/Rendering/SceneRenderer.cs (invariante 4: misma entrada → mismos píxeles).
// Implementa el MISMO pipeline que C#: timing + animaciones (vía animation-evaluator),
// brightness, clips (wipe), texto, dibujo, formas, icono e imagen indexada.
import { Font5x7 } from './font5x7.js';
import { evaluate } from './animation-evaluator.js';

function toCss(c, brightness = 1) {
  const f = Math.max(0, Math.min(1, brightness));
  if (f === 0) return '#000000';
  if (f === 1) return `#${hex(c.r)}${hex(c.g)}${hex(c.b)}`;
  return `#${hex(Math.round(c.r * f))}${hex(Math.round(c.g * f))}${hex(Math.round(c.b * f))}`;
}
function hex(v) { return Math.max(0, Math.min(255, v)).toString(16).padStart(2, '0'); }

function clipped(clip, x, y) {
  if (!clip) return false;
  return !(x >= clip.x && x < clip.x + clip.w && y >= clip.y && y < clip.y + clip.h);
}

export class Renderer {
  constructor(canvas, ctx) {
    this.canvas = canvas;
    this.ctx = ctx;
    this.scale = 10;
    this.embeddedAssets = {};
  }

  // Paridad con SceneRenderer.Render: layers ordenadas → objetos visibles → animación.
  renderScene(scene, timeMs) {
    const { ctx, canvas } = this;
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    const layers = [...(scene.layers || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
    for (const layer of layers) {
      if (layer.visible === false) continue;
      for (const obj of layer.objects || []) {
        if (obj.visible === false) continue;
        const state = evaluate(obj, timeMs, canvas.width);
        if (!state.visible) continue;
        this.renderObject(obj, state);
      }
    }
  }

  // Devuelve true si el píxel lógico (x,y) está "encendido" (no negro) en el canvas.
  // Se usa para flood-fill y borrado semántico sin mantener un framebuffer paralelo.
  pixelAt(x, y) {
    if (x < 0 || y < 0 || x >= this.canvas.width || y >= this.canvas.height) return false;
    const d = this.ctx.getImageData(x, y, 1, 1).data;
    return d[0] > 0 || d[1] > 0 || d[2] > 0;
  }

  renderObject(obj, state) {
    switch (obj.kind) {
      case 'text': this.renderText(obj, state.offsetX, state.offsetY, state.brightness); break;
      case 'drawing': this.renderDrawing(obj, state.offsetX, state.offsetY, state.brightness, state.clip); break;
      case 'shape': this.renderShape(obj, state.offsetX, state.offsetY, state.brightness, state.clip); break;
      case 'icon': this.renderIcon(obj, state.offsetX, state.offsetY, state.brightness, state.clip); break;
      case 'image': this.renderImage(obj, state.offsetX, state.offsetY, state.brightness, state.clip); break;
    }
  }

  renderText(t, ox, oy, brightness) {
    if (!t.text) return;
    const ctx = this.ctx;
    const x = (t.position?.x ?? 0) + ox;
    const y = (t.position?.y ?? 0) + oy;
    const color = toCss(t.color || { r: 255, g: 255, b: 255 }, brightness);
    let curX = x;
    for (const ch of t.text) {
      const g = Font5x7.get(ch);
      if (!g) { curX += 6; continue; }
      ctx.fillStyle = color;
      for (let row = 0; row < 7; row++) {
        const bits = g[row];
        for (let col = 0; col < 5; col++) {
          if (bits & (1 << col)) ctx.fillRect(curX + col, y + row, 1, 1);
        }
      }
      curX += 6;
    }
  }

  renderDrawing(d, ox, oy, brightness, clip) {
    const ctx = this.ctx;
    const color = toCss((d.palette && d.palette[0]) || { r: 255, g: 255, b: 255 }, brightness);
    ctx.fillStyle = color;
    const w = d.size?.width ?? 0;
    const h = d.size?.height ?? 0;
    const px = (d.position?.x ?? 0) + ox;
    const py = (d.position?.y ?? 0) + oy;
    const data = d.pixelData || [];
    for (let y = 0; y < h; y++)
      for (let x = 0; x < w; x++) {
        if (clipped(clip, x, y)) continue;
        const idx = y * w + x;
        if (idx >= data.length) continue;
        if (data[idx]) ctx.fillRect(px + x, py + y, 1, 1);
      }
  }

  renderShape(s, ox, oy, brightness, clip) {
    const ctx = this.ctx;
    const x = (s.position?.x ?? 0) + ox, y = (s.position?.y ?? 0) + oy;
    const w = s.size?.width ?? 0, h = s.size?.height ?? 0;
    const stroke = toCss(s.strokeColor || { r: 255, g: 255, b: 255 }, brightness);
    const fill = toCss(s.fillColor || { r: 0, g: 0, b: 0 }, brightness);
    if (s.shape === 'rectangle' || s.shape === 0) {
      for (let i = 0; i < w; i++) for (let j = 0; j < h; j++) {
        if (clipped(clip, i, j)) continue;
        const border = i === 0 || i === w - 1 || j === 0 || j === h - 1;
        ctx.fillStyle = border ? stroke : fill;
        if (border || s.filled) ctx.fillRect(x + i, y + j, 1, 1);
      }
    } else if (s.shape === 'ellipse' || s.shape === 2) {
      // i/j son coordenadas locales; x/y sólo en el fillRect final (paridad con C#).
      const cx = (w - 1) / 2, cy = (h - 1) / 2;
      const rx = (w - 1) / 2, ry = (h - 1) / 2;
      for (let i = 0; i < w; i++) for (let j = 0; j < h; j++) {
        if (clipped(clip, i, j)) continue;
        const nx = (i - cx) / Math.max(rx, 0.5), ny = (j - cy) / Math.max(ry, 0.5);
        const v = nx * nx + ny * ny;
        if (v <= 1) {
          const border = v >= 0.65;
          ctx.fillStyle = border ? stroke : fill;
          if (s.filled || border) ctx.fillRect(x + i, y + j, 1, 1);
        }
      }
    } else if (s.shape === 'line' || s.shape === 1) {
      let x0 = x, y0 = y, x1 = x + w - 1, y1 = y + h - 1;
      const dx = Math.abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
      const dy = -Math.abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
      let err = dx + dy;
      ctx.fillStyle = stroke;
      const maxSteps = dx + Math.abs(dy) + 1;
      for (let step = 0; step < maxSteps; step++) {
        if (!clipped(clip, x0 - x, y0 - y)) ctx.fillRect(x0, y0, 1, 1);
        if (x0 === x1 && y0 === y1) break;
        const e2 = 2 * err;
        if (e2 >= dy) { err += dy; x0 += sx; }
        if (e2 <= dx) { err += dx; y0 += sy; }
      }
    }
  }

  // ----- icono / imagen (indexadas, via embeddedAssets) -----

  renderIcon(icon, ox, oy, brightness, clip) {
    if (icon.assetId == null) return;
    const asset = this.resolveAsset(icon.assetId);
    if (!asset) return;
    this.drawIndexed(asset, (icon.position?.x ?? 0) + ox, (icon.position?.y ?? 0) + oy, brightness, clip,
      (icon.paletteMode === 'tint' || icon.paletteMode === 1) ? icon.tint : null);
  }

  renderImage(image, ox, oy, brightness, clip) {
    if (image.assetId == null) return;
    const asset = this.resolveAsset(image.assetId);
    if (!asset) return;
    this.drawIndexed(asset, (image.position?.x ?? 0) + ox, (image.position?.y ?? 0) + oy, brightness, clip, null);
  }

  // Los assets embebidos llegan como assetId -> JSON base64 (mismo esquema que C#).
  resolveAsset(assetId) {
    const key = typeof assetId === 'string' ? assetId : assetId?.value ?? assetId;
    const json = this.embeddedAssets[key];
    if (!json) return null;
    try {
      const root = JSON.parse(json);
      const width = root.width, height = root.height;
      const pixels = Array.from(Uint8Array.from(atob(root.pixels), c => c.charCodeAt(0)));
      let palette = root.palette || [];
      const transparentIndex = root.transparentIndex != null ? root.transparentIndex : -1;
      return { width, height, pixels, palette, transparentIndex };
    } catch {
      return null;
    }
  }

  drawIndexed(asset, px, py, brightness, clip, tint) {
    const ctx = this.ctx;
    let palette = asset.palette;
    if (palette.length === 0) palette = [{ r: 255, g: 255, b: 255 }];
    // Transparencia (spec 14): el asset declara su índice de fondo transparente;
    // ese índice no se pinta para no borrar objetos debajo.
    const transparentIndex = asset.transparentIndex ?? -1;
    for (let y = 0; y < asset.height; y++)
      for (let x = 0; x < asset.width; x++) {
        if (clipped(clip, x, y)) continue;
        const idx = y * asset.width + x;
        if (idx >= asset.pixels.length) continue;
        const pi = asset.pixels[idx];
        if (pi < 0 || pi >= palette.length) continue;
        if (pi === transparentIndex) continue;
        let color = palette[pi];
        if (tint) color = tint;
        ctx.fillStyle = toCss(color, brightness);
        ctx.fillRect(px + x, py + y, 1, 1);
      }
  }
}