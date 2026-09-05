// RFLED/final.md §19 — icon picker integrado en el editor (sin ir a /Library).
// La herramienta "Icono" abre un modal con búsqueda e inserta en el punto del click,
// verificando que el resultado es un icono real (píxeles), no una cruz/placeholder.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';

test('icon picker integrado: herramienta Icono -> buscar -> insertar en canvas', async ({ page }) => {
  await createProject(page, { name: 'IconPicker', width: '32', height: '16' });
  const box = await canvasBox(page);

  // Herramienta "Icono" (nueva) en el panel de herramientas.
  await page.getByRole('button', { name: 'Herramienta icono' }).click();
  // Click en el canvas para abrir el picker anclado a esa posición.
  await page.mouse.click(box.x + 40, box.y + 40);
  await page.waitForTimeout(400);

  const modal = page.locator('#icon-picker-modal');
  await expect(modal).toBeVisible();

  // Buscar "herramienta" -> debe aparecer el icono wrench.
  await page.locator('#icon-picker-search').fill('herramienta');
  await page.waitForTimeout(300);

  const cards = page.locator('#icon-picker-grid .card');
  await expect(cards.first()).toBeVisible();
  const firstText = await cards.first().innerText();
  expect(firstText.toLowerCase()).toContain('herramienta');

  // Insertar el primer resultado.
  await cards.first().click();
  await page.waitForTimeout(400);

  // El icono debe materializarse en píxeles reales (lit > 0), no cruz negra.
  const fb = await readFramebuffer(page);
  expect(fb.lit).toBeGreaterThan(0);

  console.log('icon picker lit=%d', fb.lit);
});

test('botón Nuevo abre modal de matriz y crea proyecto nuevo', async ({ page }) => {
  await createProject(page, { name: 'Tmp', width: '16', height: '16' });

  await page.getByRole('button', { name: /Nuevo/ }).click();
  const modal = page.locator('#new-modal');
  await expect(modal).toBeVisible();

  // Elegir 32x32 y crear.
  await page.locator('#new-matrix').selectOption('32,32');
  await page.locator('#new-name').fill('NuevoTest');
  await page.locator('#btn-new-create').click();

  // Debe navegar al editor con el proyecto nuevo.
  await page.waitForURL(/\/Editor\/Index/, { timeout: 10_000 });
  await expect(page.locator('#led-canvas')).toBeVisible();
  // La navegación vía window.location recarga el frame; esperar a que el canvas
  // quedé estable antes de leer su tamaño (waitForFunction, no poll de evaluate
  // que se interrumpe con la navegación).
  await page.waitForFunction(() => {
    const c = document.getElementById('led-canvas');
    return c && c.width === 32;
  }, { timeout: 10_000 });
  const dims = await page.evaluate(() => {
    const c = document.getElementById('led-canvas');
    return { w: c.width, h: c.height };
  });
  expect(dims.w).toBe(32);
  expect(dims.h).toBe(32);
});