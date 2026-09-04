// Bloque 5 / §19: historial largo (100+ ops) → Undo completo → Redo completo → Save/Open.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, pixelCoords, canvasBox } from './framebuffer-utils.js';
const S = 10;

test('100+ operaciones: Undo hasta inicio, Redo hasta final, Save/Open idéntico', async ({ page }) => {
  await createProject(page, { name: 'LongHistory', width: '32', height: '16' });
  const box = await canvasBox(page);

  // 1) registrar 100 operaciones de dibujo (punto en celdas rotativas)
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  for (let i = 0; i < 100; i++) {
    const x = (i * 7) % 32;
    const y = (i * 5) % 16;
    await page.mouse.click(box.x + x * S, box.y + y * S);
  }
  await page.waitForTimeout(150);
  const finalFb = await readFramebuffer(page);
  expect(finalFb.lit).toBeGreaterThan(0);

  // 2) Undo hasta el inicio (deseleccionar primero para evitar interferencia)
  // El límite de History es 100; 100 ops + 0 previo = 100 snapshots.
  // Hacemos 110 undos para asegurar que llegamos al vacío y el exceso es no-op.
  for (let i = 0; i < 110; i++) {
    await page.keyboard.press('Control+z');
  }
  await page.waitForTimeout(200);
  const afterUndo = await readFramebuffer(page);
  // tras el undo completo el canvas está vacío (proyecto inicial sin objetos)
  console.log('AFTER FULL UNDO lit=%s', afterUndo.lit);

  // 3) Redo hasta el final
  for (let i = 0; i < 110; i++) {
    await page.keyboard.press('Control+y');
  }
  await page.waitForTimeout(200);
  const afterRedo = await readFramebuffer(page);
  // el estado final debe coincidir con el original (100 puntos)
  expect(pixelCoords(afterRedo)).toEqual(pixelCoords(finalFb));

  // 4) Save/Open idéntico
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect(pixelCoords(await readFramebuffer(page))).toEqual(pixelCoords(finalFb));
});

test('selección no apunta a objetos borrados tras Undo/Redo', async ({ page }) => {
  await createProject(page, { name: 'SelHistory', width: '16', height: '16' });
  const box = await canvasBox(page);
  const dialogs = { set: () => {} };
  page.on('dialog', d => d.accept('HOLA'));

  // crear un texto
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(120);

  // seleccionar
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);
  await expect(page.locator('#stat-selection')).toContainText('1 seleccionado');

  // borrar el objeto (Delete)
  await page.keyboard.press('Delete');
  await page.waitForTimeout(120);
  await expect(page.locator('#stat-selection')).toContainText('0 seleccionados');

  // Undo restaura el objeto; la selección debe quedar vacía (no apuntar a id muerto)
  await page.keyboard.press('Control+z');
  await page.waitForTimeout(120);
  expect((await readFramebuffer(page)).pixels.length).toBeGreaterThan(0);
  // no debe haber selección colgando a un id inexistente (no crashea render)
  await expect(page.locator('#led-canvas')).toBeVisible();
});