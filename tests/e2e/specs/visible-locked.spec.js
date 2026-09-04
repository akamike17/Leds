// Visible/Locked respetados (P1): visible=false no renderiza; locked impide borrar.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';
const S = 10;

test('visible=false oculta el objeto del framebuffer; locked impide borrar', async ({ page }) => {
  await createProject(page, { name: 'VisLock', width: '16', height: '16' });
  const box = await canvasBox(page);

  // dibujar un punto
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(100);
  expect((await readFramebuffer(page)).lit).toBe(1);

  // seleccionar y marcar visible=false
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);
  await page.locator('#inspector-content [data-field="visible"]').uncheck();
  await page.waitForTimeout(100);
  expect((await readFramebuffer(page)).lit).toBe(0);   // oculto

  // volver a marcar visible
  await page.locator('#inspector-content [data-field="visible"]').check();
  await page.waitForTimeout(100);
  expect((await readFramebuffer(page)).lit).toBe(1);   // visible de nuevo

  // marcar locked y borrar: no debe borrarse
  await page.locator('#inspector-content [data-field="locked"]').check();
  await page.waitForTimeout(80);
  await page.keyboard.press('Delete');
  await page.waitForTimeout(120);
  // locked debe impedir el borrado → sigue 1 px
  expect((await readFramebuffer(page)).lit).toBe(1);
});