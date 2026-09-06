import { test, expect } from '@playwright/test';
import { createProject } from './framebuffer-utils.js';

test('simulator modal reuses renderer and releases it on close', async ({ page }) => {
  await createProject(page, { name: 'ModalLifecycle', width: '32', height: '16' });
  await page.locator('#btn-preview').click();
  const modal = page.locator('#simulator-modal');
  await expect(modal).toBeVisible();
  await page.locator('#simulator-play').click();
  await page.waitForTimeout(150);
  await page.locator('#simulator-stop').click();
  await page.locator('#simulator-modal [data-bs-dismiss="modal"]').click();
  await expect(modal).toBeHidden();
  expect(await page.evaluate(() => ({ canvas: !!window.__atlasLastPreviewCanvas, renderers: window.__atlasPreviewRendererCount ?? 0 }))).toEqual({ canvas: false, renderers: 0 });
  await page.locator('#btn-preview').click();
  await expect(modal).toBeVisible();
  await page.locator('#simulator-modal [data-bs-dismiss="modal"]').click();
  await expect(modal).toBeHidden();
});

test('simulator modal exposes compiled frame endpoint status', async ({ page }) => {
  await createProject(page, { name: 'ModalParity', width: '16', height: '16' });
  await page.locator('#btn-preview').click();
  await expect(page.locator('#simulator-parity-status')).toContainText('Vista local');
  const response = await page.request.get('/Deploy/SimulatorFrame?timeMs=0');
  expect(response.ok()).toBeTruthy();
  await page.locator('#simulator-modal [data-bs-dismiss="modal"]').click();
});
