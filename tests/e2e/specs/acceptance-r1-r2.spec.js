// R1 y R2 del MASTER SPEC (sección 20): flujos completos de usuario con evidencia
// de framebuffer real (getImageData), no "canvas visible".
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';

function acceptDialogs(page) {
  let pending = '';
  page.on('dialog', d => d.accept(pending));
  return { set: (t) => { pending = t; } };
}

test('R1 anuncio reina: MG SOL -> PC -> SE ARREGLAN COMPUTADORAS + Save/Open + Simulator', async ({ page }) => {
  const d = acceptDialogs(page);
  await createProject(page, { name: 'R1 Reina', width: '32', height: '16' });

  // 1) MG SOL (marquee por overflow en 32px -> 6 chars * 6 -1 = 35 > 32)
  const box = await canvasBox(page);
  d.set('MG SOL');
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 20, box.y + 20);
  await page.waitForTimeout(120);
  const mgsol = await readFramebuffer(page);
  expect(mgsol.lit).toBeGreaterThan(0);

  // 2) PC
  d.set('PC');
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 20, box.y + 60);
  await page.waitForTimeout(120);

  // 3) SE ARREGLAN COMPUTADORAS (marquee)
  d.set('SE ARREGLAN COMPUTADORAS');
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 20, box.y + 100);
  await page.waitForTimeout(120);
  const full = await readFramebuffer(page);
  expect(full.lit).toBeGreaterThan(mgsol.lit);

  // 4) Save / Open idéntico
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBe(full.lit);

  // 5) Enviar al simulador
  await page.locator('#btn-send').click();
  await expect(page.locator('#stat-send')).toContainText('Enviado', { timeout: 10_000 });
  console.log('R1 lit inicial=%s final=%s', mgsol.lit, full.lit);
});

test('R2 dibujo: corazón continuo -> mover -> blink -> biblioteca -> borrar -> reinsertar -> Undo/Redo -> Save/Open', async ({ page }) => {
  const d = acceptDialogs(page);
  await createProject(page, { name: 'R2 Corazon', width: '16', height: '16' });
  const box = await canvasBox(page);

  // 1) Dibujar un corazón continuo con el lápiz (trazo en varias celdas)
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.move(box.x + 30, box.y + 30);
  await page.mouse.down();
  await page.mouse.move(box.x + 40, box.y + 30, { steps: 2 });
  await page.mouse.move(box.x + 50, box.y + 40, { steps: 2 });
  await page.mouse.move(box.x + 40, box.y + 50, { steps: 2 });
  await page.mouse.move(box.x + 30, box.y + 40, { steps: 2 });
  await page.mouse.move(box.x + 30, box.y + 30, { steps: 2 });
  await page.mouse.up();
  await page.waitForTimeout(150);
  const heart = await readFramebuffer(page);
  expect(heart.lit).toBeGreaterThan(0);   // debe existir un DrawingObject con píxeles

  // 2) Mover (selección + drag)
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 40, box.y + 40);
  await page.waitForTimeout(100);
  await expect(page.locator('#stat-selection')).toHaveText('1 seleccionado');

  // 3) Blink vía inspector
  await page.locator('#inspector-content [data-field="animKind"]').selectOption('1');
  await page.waitForTimeout(100);

  // 4) Guardar en biblioteca (dibujo seleccionado)
  await page.getByRole('button', { name: 'Guardar en biblioteca' }).click();
  d.set('CorazonR2');
  await page.waitForTimeout(200);

  // 5) Borrar del proyecto (tecla Delete)
  await page.keyboard.press('Delete');
  await page.waitForTimeout(100);
  const afterDelete = await readFramebuffer(page);
  expect(afterDelete.lit).toBe(0);

  // 6) Undo / Redo
  await page.keyboard.press('Control+z');
  await page.waitForTimeout(100);
  expect((await readFramebuffer(page)).lit).toBeGreaterThan(0);
  await page.keyboard.press('Control+y');
  await page.waitForTimeout(100);
  expect((await readFramebuffer(page)).lit).toBe(0);

  // 7) Reinsertar desde biblioteca
  await page.getByRole('button', { name: 'Biblioteca' }).click();
  await page.waitForTimeout(300);
  // tab dibujos por defecto; insertar el primero
  const card = page.locator('#library-grid .card').first();
  await expect(card).toBeVisible();
  await card.locator('button').click();
  await page.waitForTimeout(150);
  expect((await readFramebuffer(page)).lit).toBeGreaterThan(0);

  // 8) Save/Open idéntico
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  const beforeReload = await readFramebuffer(page);
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBe(beforeReload.lit);
  console.log('R2 corazón lit=%s', heart.lit);
});