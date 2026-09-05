// final.md §51 (R3) — fuentes: cambiar la fuente del texto (5x7 -> 3x5) cambia el
// render real (framebuffer), y persiste tras Save/Open.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';

function acceptDialogs(page) {
  let pending = '';
  page.on('dialog', d => d.accept(pending));
  return { set: (t) => { pending = t; } };
}

test('R3 fuentes: cambiar a 3x5 cambia el framebuffer y persiste', async ({ page }) => {
  const d = acceptDialogs(page);
  await createProject(page, { name: 'R3 Fonts', width: '32', height: '16' });
  const box = await canvasBox(page);

  // Texto "MG" con fuente 5x7 (default).
  d.set('MG');
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 20, box.y + 20);
  await page.waitForTimeout(120);
  const lit5x7 = (await readFramebuffer(page)).lit;
  expect(lit5x7).toBeGreaterThan(0);

  // Seleccionar el texto y abrir el inspector.
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 20, box.y + 20);
  await page.waitForTimeout(100);

  // El inspector debe mostrar el selector de fuente; cambiar a 3x5.
  const fontSelect = page.locator('#inspector-content [data-field="font"]');
  await expect(fontSelect).toBeVisible();
  await fontSelect.selectOption('3x5');
  await page.waitForTimeout(200);

  const lit3x5 = (await readFramebuffer(page)).lit;
  // 3x5 es más compacta: menos píxeles encendidos que 5x7 para el mismo texto.
  expect(lit3x5).toBeGreaterThan(0);
  expect(lit3x5).toBeLessThan(lit5x7);

  // Save/Open conserva la fuente.
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBe(lit3x5);

  console.log('R3 fonts: 5x7 lit=%d -> 3x5 lit=%d', lit5x7, lit3x5);
});