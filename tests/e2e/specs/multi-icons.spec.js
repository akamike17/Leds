// Iconos (spec 14): los iconos incluidos existen con nombre/categoría; se
// insertan iconos reales (no sólo Corazón) y persisten.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer } from './framebuffer-utils.js';

test('los 21 iconos incluidos existen (Corazón/Estrella/Flechas/Teléfono/Carrito/Engranaje/Wi-Fi) y se insertan', async ({ page }) => {
  await createProject(page, { name: 'MultiIcons', width: '64', height: '16' });

  await page.getByRole('button', { name: 'Biblioteca' }).click();
  await page.locator('[data-lib-tab="icons"]').click();
  await page.waitForTimeout(300);

  const cards = await page.locator('#library-grid .card').allTextContents();
  expect(cards.length).toBeGreaterThanOrEqual(21);
  const names = cards.map(t => t.trim());
  for (const want of ['Corazón', 'Estrella', 'Flecha', 'Teléfono', 'Carrito', 'Engranaje', 'Wi-Fi']) {
    expect(names.some(n => n.toLowerCase().includes(want.toLowerCase()))).toBe(true);
  }

  // insertar Estrella (índice 1, distinto de Corazón) y verificar píxeles
  await page.locator('#library-grid .card button').nth(1).click();
  await page.waitForTimeout(400);
  let fb = await readFramebuffer(page);
  expect(fb.lit).toBeGreaterThan(0);
  const star = fb.lit;

  // insertar Corazón (índice 0) — otro icono distinto
  await page.getByRole('button', { name: 'Biblioteca' }).click();
  await page.waitForTimeout(400);
  await page.locator('[data-lib-tab="icons"]').click();
  await page.waitForTimeout(300);
  await page.locator('#library-grid .card button').nth(0).click();
  await page.waitForTimeout(400);
  fb = await readFramebuffer(page);
  expect(fb.lit).toBeGreaterThan(0);
  expect(fb.lit).toBeGreaterThan(star);   // dos iconos superpuestos > uno

  // guardar + reabrir conserva
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBeGreaterThan(0);
});