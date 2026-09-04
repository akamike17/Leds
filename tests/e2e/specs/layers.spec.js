// Verificación capas/escenas + autosave (P1).
import { test, expect } from '@playwright/test';
import { createProject } from './framebuffer-utils.js';

async function canvasBox(page) { return page.locator('#led-canvas').boundingBox(); }

test('añadir escena y capa; selector refleja selección', async ({ page }) => {
  await createProject(page, { name: 'Layers', width: '16', height: '16' });
  // escena inicial
  await expect(page.locator('#scene-select option')).toHaveCount(1);
  await expect(page.locator('#layer-select option')).toHaveCount(1);

  // añadir capa
  await page.locator('#btn-add-layer').click();
  await expect(page.locator('#layer-select option')).toHaveCount(2);

  // añadir escena
  await page.locator('#btn-add-scene').click();
  await expect(page.locator('#scene-select option')).toHaveCount(2);
  // la nueva escena seleccionada tiene 1 capa
  await expect(page.locator('#layer-select option')).toHaveCount(1);

  // guardar + reabrir conserva escenas/capas
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  await expect(page.locator('#scene-select option')).toHaveCount(2);
});