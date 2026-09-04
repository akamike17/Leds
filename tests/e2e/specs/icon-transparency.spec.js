// Transparencia de icono (spec 14): un píxel "transparente" no debe borrar objetos debajo.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';
const S = 10;

test('icono con fondo transparente no borra objetos debajo', async ({ page }) => {
  await createProject(page, { name: 'IconTransp', width: '32', height: '16' });
  const box = await canvasBox(page);

  // 1) llenar todo el canvas de blanco
  await page.getByRole('button', { name: 'Herramienta relleno' }).click();
  await page.mouse.click(box.x + 16 * S, box.y + 8 * S);
  await page.waitForTimeout(100);
  const before = await readFramebuffer(page);
  expect(before.lit).toBe(32 * 16);

  // 2) insertar un icono (Corazón) sobre el fondo blanco
  await page.getByRole('button', { name: 'Biblioteca' }).click();
  await page.locator('[data-lib-tab="icons"]').click();
  await page.waitForTimeout(300);
  await page.locator('#library-grid .card button').first().click();
  await page.waitForTimeout(200);

  const after = await readFramebuffer(page);
  // El fondo transparente del icono NO borra: el área sigue iluminada (512 px),
  // sólo se superponen los píxeles encendidos del icono (blanco sobre blanco = idéntico).
  expect(after.lit).toBe(before.lit);

  // guardar + reabrir: la transparencia persiste
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBe(32 * 16);
});