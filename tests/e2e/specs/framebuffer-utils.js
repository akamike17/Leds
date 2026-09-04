// Helpers E2E: lectura del framebuffer real del canvas (getImageData) y utilidades
// de creación de proyectos. Reemplazan las aserciones de "canvas visible" y
// "dirty cambió" por aserciones de PÍXELES OBSERVABLES (spec 20.9 / ins.txt P0).
import { expect } from '@playwright/test';

// Crea un proyecto nuevo vía Projects/New (POST con antiforgery) y espera al editor.
export async function createProject(page, { name = 'E2E', width = '32', height = '16' } = {}) {
  await page.goto('/Projects/New');
  await page.locator('#Name').fill(name);
  await page.locator('#Width').fill(width);
  await page.locator('#Height').fill(height);
  await page.getByRole('button', { name: /Crear/i }).click();
  await page.waitForURL(/\/Editor\/Index/, { timeout: 15_000 });
  await expect(page.locator('#led-canvas')).toBeVisible();
}

// Lee el framebuffer del canvas del editor. Devuelve { width, height, lit, pixels:[{x,y,r,g,b}] }.
export async function readFramebuffer(page) {
  return page.evaluate(() => {
    const c = document.getElementById('led-canvas');
    const ctx = c.getContext('2d');
    const data = ctx.getImageData(0, 0, c.width, c.height).data;
    let lit = 0;
    const pixels = [];
    for (let y = 0; y < c.height; y++) {
      for (let x = 0; x < c.width; x++) {
        const o = (y * c.width + x) * 4;
        const r = data[o], g = data[o + 1], b = data[o + 2];
        if (r > 0 || g > 0 || b > 0) {
          lit++;
          pixels.push({ x, y, r, g, b });
        }
      }
    }
    return { width: c.width, height: c.height, lit, pixels };
  });
}

// Conjunto de coordenadas {x,y} de píxeles encendidos.
export function pixelCoords(fb) {
  return new Set(fb.pixels.map(p => `${p.x},${p.y}`));
}

// Añade un objeto de texto aceptando el diálogo prompt con el texto indicado.
export async function addText(page, box, text, x, y) {
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + x, box.y + y);
  await page.waitForTimeout(120);
}

// Obtiene el bounding box del canvas.
export async function canvasBox(page) {
  return page.locator('#led-canvas').boundingBox();
}