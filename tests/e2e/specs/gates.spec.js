// Gates finales: sin errores de consola JS ni HTTP 4xx/5xx en flujos principales.
import { test, expect } from '@playwright/test';
import { createProject } from './framebuffer-utils.js';

test('sin errores de consola ni HTTP 4xx/5xx en el editor', async ({ page }) => {
  const consoleErrors = [];
  const httpErrors = [];
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  page.on('pageerror', e => consoleErrors.push(e.message));
  page.on('response', r => { if (r.status() >= 400) httpErrors.push(`${r.status()} ${r.url()}`); });

  await createProject(page, { name: 'Gate', width: '32', height: '16' });
  // tocar varias rutas/acciones que disparan fetch
  await page.getByRole('button', { name: 'Biblioteca' }).click();
  await page.waitForTimeout(300);
  await page.locator('[data-lib-tab="icons"]').click();
  await page.waitForTimeout(200);
  // cerrar el modal
  await page.locator('#library-modal .btn-close').click().catch(() => {});
  // guardar
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

  console.log('CONSOLE ERRORS:', JSON.stringify(consoleErrors));
  console.log('HTTP ERRORS:', JSON.stringify(httpErrors));
  // ignoramos 404 de /favicon u otros recursos estáticos no críticos
  const realHttpErrors = httpErrors.filter(u => !u.includes('/favicon'));
  const realConsoleErrors = consoleErrors.filter(e => !/favicon/i.test(e));
  expect(realConsoleErrors).toEqual([]);
  expect(realHttpErrors).toEqual([]);
});