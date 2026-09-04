// Bloque 4 / §4: escenas y capas — aislamiento, capa activa recibe objetos, persistencia.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';
const S = 10;

test('cambiar de capa no altera la otra; capa activa recibe objetos nuevos; orden visual', async ({ page }) => {
  await createProject(page, { name: 'LayersIso', width: '16', height: '16' });
  const box = await canvasBox(page);

  // 1) dibujar en capa 1 (default)
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);
  const layer1Lit = (await readFramebuffer(page)).lit;
  expect(layer1Lit).toBe(1);

  // 2) añadir capa 2 (se convierte en activa)
  await page.locator('#btn-add-layer').click();
  await page.waitForTimeout(80);
  // capa 2 activa: dibujar un punto distinto
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 8 * S, box.y + 8 * S);
  await page.waitForTimeout(80);
  // ahora hay 2 píxeles (ambas capas visibles por defecto)
  const both = await readFramebuffer(page);
  expect(both.lit).toBe(2);

  // 3) cambiar a capa 1 vía selector: el punto de la capa 1 sigue, el de capa 2 también
  await page.locator('#layer-select').selectOption('0');
  await page.waitForTimeout(80);
  expect((await readFramebuffer(page)).lit).toBe(2);

  // 4) guardar + reabrir: se conservan 2 puntos + 2 capas
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  await expect(page.locator('#layer-select option')).toHaveCount(2);
  expect((await readFramebuffer(page)).lit).toBe(2);
});

test('cambiar de escena no altera la otra escena', async ({ page }) => {
  await createProject(page, { name: 'SceneIso', width: '16', height: '16' });
  const box = await canvasBox(page);

  // dibujar en escena 1
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);

  // añadir escena 2 (activa) y dibujar distinto
  await page.locator('#btn-add-scene').click();
  await page.waitForTimeout(80);
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 12 * S, box.y + 12 * S);
  await page.waitForTimeout(80);
  // escena 2 tiene solo 1 punto (12,12)
  const scene2 = await readFramebuffer(page);
  expect(scene2.lit).toBe(1);
  expect(scene2.pixels[0]).toMatchObject({ x: 12, y: 12 });

  // volver a escena 1: tiene solo 1 punto (4,4), no el (12,12)
  await page.locator('#scene-select').selectOption('0');
  await page.waitForTimeout(80);
  const scene1 = await readFramebuffer(page);
  expect(scene1.lit).toBe(1);
  expect(scene1.pixels[0]).toMatchObject({ x: 4, y: 4 });
});