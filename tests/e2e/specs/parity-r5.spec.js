// R5 / Tarea 4: equivalencia fuerte editor JS == simulador C# (RGB exacto, no lit count).
// Para cada tipo de objeto, comparamos el framebuffer completo del editor (renderer JS)
// contra el framebuffer compilado del simulador (SceneRenderer C# → SceneCompiler).
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer } from './framebuffer-utils.js';
const S = 10;

function acceptDialogs(page) {
  let pending = '';
  page.on('dialog', d => d.accept(pending));
  return { set: (t) => { pending = t; } };
}

// comparar editor fb (set de "x,y#rrggbb") vs simulator frame (lista x,y,r,g,b)
function signature(fb) {
  return new Set(fb.pixels.map(p => `${p.x},${p.y}#${p.r},${p.g},${p.b}`));
}
function simSignature(sim) {
  return new Set((sim.pixels || []).map(p => `${p.x},${p.y}#${p.r},${p.g},${p.b}`));
}

async function sendAndGetSim(page) {
  await page.locator('#btn-send').click();
  await expect(page.locator('#stat-send')).toContainText('Enviado', { timeout: 10_000 });
  return (await page.request.get('/Deploy/SimulatorFrame?timeMs=0')).json();
}

test('R5: texto HOLA editor == simulador (RGB exacto)', async ({ page }) => {
  const d = acceptDialogs(page);
  await createProject(page, { name: 'R5Text', width: '32', height: '16' });
  const box = await page.locator('#led-canvas').boundingBox();
  d.set('HOLA');
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(100);
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

  const editor = signature(await readFramebuffer(page));
  const sim = await sendAndGetSim(page);
  expect(sim.success).toBe(true);
  expect(simSignature(sim)).toEqual(editor);
});

test('R5: elipse editor == simulador (RGB exacto)', async ({ page }) => {
  await createProject(page, { name: 'R5Ellipse', width: '16', height: '16' });
  const box = await page.locator('#led-canvas').boundingBox();
  await page.getByRole('button', { name: 'Herramienta elipse' }).click();
  await page.mouse.move(box.x + 3 * S, box.y + 3 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 12 * S, box.y + 11 * S, { steps: 6 });
  await page.mouse.up();
  await page.waitForTimeout(100);
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

  const editor = signature(await readFramebuffer(page));
  const sim = await sendAndGetSim(page);
  expect(simSignature(sim)).toEqual(editor);
});

test('R5: rect y línea editor == simulador (RGB exacto)', async ({ page }) => {
  await createProject(page, { name: 'R5Rect', width: '16', height: '16' });
  const box = await page.locator('#led-canvas').boundingBox();
  await page.getByRole('button', { name: 'Herramienta rectángulo' }).click();
  await page.mouse.move(box.x + 5 * S, box.y + 5 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 10 * S, box.y + 10 * S, { steps: 5 });
  await page.mouse.up();
  await page.waitForTimeout(100);
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

  const editor = signature(await readFramebuffer(page));
  const sim = await sendAndGetSim(page);
  expect(simSignature(sim)).toEqual(editor);
});

test('R5: icono (transparente) editor == simulador (RGB exacto)', async ({ page }) => {
  await createProject(page, { name: 'R5Icon', width: '32', height: '16' });
  const box = await page.locator('#led-canvas').boundingBox();
  // relleno blanco de fondo
  await page.getByRole('button', { name: 'Herramienta relleno' }).click();
  await page.mouse.click(box.x + 16 * S, box.y + 8 * S);
  await page.waitForTimeout(100);
  // icono encima
  await page.getByRole('button', { name: 'Biblioteca' }).click();
  await page.locator('[data-lib-tab="icons"]').click();
  await page.waitForTimeout(300);
  await page.locator('#library-grid .card button').first().click();
  await page.waitForTimeout(400);
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

  const editor = signature(await readFramebuffer(page));
  const sim = await sendAndGetSim(page);
  expect(simSignature(sim)).toEqual(editor);
});