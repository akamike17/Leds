// R2 exacto + R3: dibujo continuo = 1 DrawingObject y 1 Undo; undo/redo vacío seguro.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';
const S = 10;

test('dibujo continuo crea exactamente 1 objeto y 1 step de Undo', async ({ page }) => {
  await createProject(page, { name: 'R2Undo', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  // trazo continuo de varios puntos
  await page.mouse.move(box.x + 3 * S, box.y + 3 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 5 * S, box.y + 3 * S, { steps: 2 });
  await page.mouse.move(box.x + 5 * S, box.y + 5 * S, { steps: 2 });
  await page.mouse.move(box.x + 3 * S, box.y + 5 * S, { steps: 2 });
  await page.mouse.move(box.x + 3 * S, box.y + 3 * S, { steps: 2 });
  await page.mouse.up();
  await page.waitForTimeout(120);
  const after = await readFramebuffer(page);
  expect(after.pixels.length).toBeGreaterThan(0);

  const count = await page.evaluate(() => {
    // el editor expone module ES; contamos vía selección/tooling no disponible,
    // así que verificamos UNDO: un único Ctrl+Z restaura el canvas vacío.
    return null;
  });

  // Un único Undo debe borrar TODO el trazo (1 DrawingObject = 1 operación):
  await page.keyboard.press('Control+z');
  await page.waitForTimeout(120);
  expect((await readFramebuffer(page)).lit).toBe(0);

  // Redo restaura el trazo completo
  await page.keyboard.press('Control+y');
  await page.waitForTimeout(120);
  expect((await readFramebuffer(page)).lit).toBe(after.lit);
});

test('R3: undo/redo vacío no rompe; texto vacío no crea objeto', async ({ page }) => {
  let pending = '';
  page.on('dialog', d => d.accept(pending));
  await createProject(page, { name: 'R3Edge', width: '16', height: '16' });
  const box = await canvasBox(page);

  // undo/redo sobre histórico vacío → no-op sin error
  await page.keyboard.press('Control+z');
  await page.keyboard.press('Control+y');
  await page.waitForTimeout(100);
  expect((await readFramebuffer(page)).lit).toBe(0);

  // texto vacío (accept('')) → no crea objeto
  pending = '';
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(120);
  expect((await readFramebuffer(page)).lit).toBe(0);

  // Delete sin selección → no-op sin error
  await page.keyboard.press('Delete');
  await page.waitForTimeout(100);
  expect((await readFramebuffer(page)).lit).toBe(0);
});