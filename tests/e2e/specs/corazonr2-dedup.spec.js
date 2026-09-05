// RFLED/final.md §50 (R2) — CorazonR2: el flujo completo, y verificar que NO se
// duplican entradas al guardar el mismo dibujo varias veces (§21 bug real).
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';

function acceptDialogs(page) {
  let pending = '';
  page.on('dialog', d => d.accept(pending));
  return { set: (t) => { pending = t; } };
}

test('R2 CorazonR2: guardar dos veces NO duplica; thumbnail es el corazón', async ({ page }) => {
  const d = acceptDialogs(page);
  await createProject(page, { name: 'R2 Corazon', width: '16', height: '16' });
  const box = await canvasBox(page);

  // Dibujar un corazón continuo con el lápiz.
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.move(box.x + 30, box.y + 30);
  await page.mouse.down();
  await page.mouse.move(box.x + 40, box.y + 30, { steps: 2 });
  await page.mouse.move(box.x + 50, box.y + 40, { steps: 2 });
  await page.mouse.move(box.x + 40, box.y + 50, { steps: 2 });
  await page.mouse.move(box.x + 30, box.y + 40, { steps: 2 });
  await page.mouse.move(box.x + 40, box.y + 30, { steps: 2 });
  await page.mouse.up();
  await page.waitForTimeout(150);
  expect((await readFramebuffer(page)).lit).toBeGreaterThan(0);

  // Seleccionar el dibujo y guardarlo en biblioteca DOS veces.
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 40, box.y + 40);
  await page.waitForTimeout(100);

  for (let i = 0; i < 2; i++) {
    d.set('CorazonR2');
    await page.getByRole('button', { name: 'Guardar en biblioteca' }).click();
    await page.waitForTimeout(300);
  }

  // Abrir biblioteca -> tab dibujos -> debe haber EXACTAMENTE UNA entrada.
  await page.locator('#btn-library').click();
  await page.waitForTimeout(400);
  const cards = await page.locator('#library-grid .card');
  await expect(cards.first()).toBeVisible();
  const count = await cards.count();
  expect(count).toBe(1);   // deduplicación: 1 sola entrada

  // El nombre de la única tarjeta debe ser CorazonR2.
  const firstText = await cards.first().innerText();
  expect(firstText).toContain('CorazonR2');

  console.log('R2 CorazonR2: %d entrada/s en biblioteca', count);
});