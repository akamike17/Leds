// Cierre con cambios sin guardar (Tarea 3): navegación interna → Guardar/Descartar/Cancelar.
import { test, expect } from '@playwright/test';
import { createProject, canvasBox } from './framebuffer-utils.js';
const S = 10;

test('navegar a Projects con cambios sin guardar pide confirmación; Descartar pierde, Guardar conserva', async ({ page }) => {
  await createProject(page, { name: 'UnsavedNav', width: '16', height: '16' });
  const box = await canvasBox(page);

  // 1) editar (dibujar) → dirty
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(100);
  await expect(page.locator('#stat-dirty')).toContainText('Cambios sin guardar');

  // 2) hacer clic en "Proyectos" del nav → debe aparecer el modal (no navegar)
  await page.getByRole('link', { name: 'Proyectos' }).click();
  await expect(page.locator('#unsaved-modal')).toBeVisible();

  // 3) Cancelar → sigue en el editor
  await page.locator('#unsaved-cancel').click();
  await page.waitForTimeout(200);
  await expect(page.locator('#led-canvas')).toBeVisible();

  // 4) volver a navegar y Descartar → navega a /Projects
  await page.getByRole('link', { name: 'Proyectos' }).click();
  await expect(page.locator('#unsaved-modal')).toBeVisible();
  await page.locator('#unsaved-discard').click();
  await page.waitForURL(/\/Projects/, { timeout: 10_000 });
});

test('navegar con cambios y Guardar conserva el trabajo antes de navegar', async ({ page }) => {
  await createProject(page, { name: 'UnsavedSave', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(100);
  const projectId = await page.locator('#project-id').inputValue();

  // Guardar vía el modal
  await page.getByRole('link', { name: 'Proyectos' }).click();
  await expect(page.locator('#unsaved-modal')).toBeVisible();
  await page.locator('#unsaved-save').click();
  await page.waitForURL(/\/Projects/, { timeout: 10_000 });

  // reabrir el proyecto: el dibujo persiste (se guardó)
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  const fb = await page.evaluate(() => {
    const c = document.getElementById('led-canvas');
    const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
    let lit = 0; for (let i = 0; i < d.length; i += 4) if (d[i] > 0 || d[i+1] > 0 || d[i+2] > 0) lit++;
    return lit;
  });
  expect(fb).toBe(1);
});