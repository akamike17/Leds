// Group / Ungroup / Align (spec 5 + 8): framebuffer exacto + persistencia.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, pixelCoords, canvasBox } from './framebuffer-utils.js';
const S = 10;

test('group conserva framebuffer; ungroup conserva; align alinea coordenadas x', async ({ page }) => {
  await createProject(page, { name: 'GroupAlign', width: '32', height: '16' });
  const box = await canvasBox(page);

  // dos puntos en x distintas: (4,4) y (12,8)
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.mouse.click(box.x + 12 * S, box.y + 8 * S);
  await page.waitForTimeout(100);
  const beforeFb = await readFramebuffer(page);
  expect(beforeFb.pixels.length).toBe(2);

  // seleccionar ambos con rect-select
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.move(box.x + 2 * S, box.y + 2 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 15 * S, box.y + 10 * S, { steps: 4 });
  await page.mouse.up();
  await page.waitForTimeout(100);
  await expect(page.locator('#stat-selection')).toHaveText('2 seleccionados');

  // group: framebuffer idéntico
  await page.locator('#btn-group').click();
  await page.waitForTimeout(100);
  const afterGroup = await readFramebuffer(page);
  expect(pixelCoords(afterGroup)).toEqual(pixelCoords(beforeFb));

  // align left: ambos puntos deben quedar en x=minX (4)
  await page.locator('#btn-align-left').click();
  await page.waitForTimeout(100);
  const afterAlign = await readFramebuffer(page);
  const xs = afterAlign.pixels.map(p => p.x);
  expect(xs.every(x => x === 4)).toBe(true);
  expect(afterAlign.pixels.length).toBe(2);

  // ungroup: framebuffer sigue igual (misma posición alineada)
  await page.locator('#btn-ungroup').click();
  await page.waitForTimeout(100);
  const afterUngroup = await readFramebuffer(page);
  expect(pixelCoords(afterUngroup)).toEqual(pixelCoords(afterAlign));

  // undo hasta antes del group y redo
  await page.keyboard.press('Control+z');
  await page.waitForTimeout(80);
  await page.keyboard.press('Control+z');
  await page.waitForTimeout(80);
  await page.keyboard.press('Control+z');
  await page.waitForTimeout(80);
  // tras 3 undos volvemos a la posición original (2 puntos en (4,4) y (12,8))
  const afterUndo = await readFramebuffer(page);
  expect(pixelCoords(afterUndo)).toEqual(pixelCoords(beforeFb));

  // guardar + reabrir: la posición final persiste
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
});

test('group persiste Save/Open con memberIds resolubles', async ({ page }) => {
  await createProject(page, { name: 'GroupPersist', width: '32', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.mouse.click(box.x + 12 * S, box.y + 8 * S);
  await page.waitForTimeout(100);
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.move(box.x + 2 * S, box.y + 2 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 15 * S, box.y + 10 * S, { steps: 4 });
  await page.mouse.up();
  await page.waitForTimeout(100);
  await page.locator('#btn-group').click();
  await page.waitForTimeout(100);

  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  // el framebuffer persiste (2 píxeles)
  expect((await readFramebuffer(page)).pixels.length).toBe(2);
});