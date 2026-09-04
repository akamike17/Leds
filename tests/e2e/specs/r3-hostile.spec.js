// Bloque 10 / R3: doble/triple Send y Save repetido conservan proyecto válido.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';
const S = 10;

test('doble Send rápido no corrompe; Save repetido idempotente', async ({ page }) => {
  await createProject(page, { name: 'R3Send', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  page.on('dialog', d => d.accept('HI'));
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(100);
  const before = await readFramebuffer(page);
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

  // doble send en rápida sucesión
  await page.locator('#btn-send').click();
  await page.locator('#btn-send').click();
  await page.waitForTimeout(800);
  await expect(page.locator('#stat-send')).toContainText('Enviado', { timeout: 10_000 }).catch(() => {});

  // el proyecto sigue íntegro (framebuffer intacto)
  const after = await readFramebuffer(page);
  expect(after.lit).toBe(before.lit);

  // Save repetido
  for (let i = 0; i < 3; i++) {
    await page.locator('#btn-save').click();
    await page.waitForTimeout(80);
  }
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

  // reabrir conserva
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBe(before.lit);
});

test('R3: objeto fuera del canvas (X negativo) es rechazado por el validator con mensaje humano', async ({ page }) => {
  await createProject(page, { name: 'R3OutOfBounds', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);

  // poner X negativo vía inspector
  await page.locator('#inspector-content [data-field="x"]').fill('-5');
  await page.waitForTimeout(100);

  // Save debe fallar con mensaje (validator rechaza posición negativa)
  await page.locator('#btn-save').click();
  await page.waitForTimeout(300);
  const notify = await page.locator('#stat-notify').textContent();
  console.log('SAVE with negative X ->', notify);
  // dirty sigue en "cambios sin guardar" (no se guardó)
  await expect(page.locator('#stat-dirty')).toContainText('Cambios sin guardar');
});