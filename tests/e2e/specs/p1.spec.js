// Verificación P1: portada, devices, playback, animación en preview, send guarda antes.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';

test('portada Home muestra accesos directos (no Welcome)', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'DSLetras' })).toBeVisible();
  await expect(page.locator('main').getByRole('link', { name: 'Nuevo proyecto' })).toBeVisible();
  await expect(page.locator('main').getByRole('link', { name: 'Abrir proyecto' })).toBeVisible();
  await expect(page.locator('main').getByRole('link', { name: 'Biblioteca' })).toBeVisible();
  await expect(page.locator('main').getByRole('link', { name: 'Dispositivos' })).toBeVisible();
});

test('Devices lista simulador y firmware (no placeholder)', async ({ page }) => {
  await page.goto('/Devices');
  await expect(page.getByRole('heading', { name: 'Dispositivos' })).toBeVisible();
  await expect(page.locator('#devices-body')).toContainText('Simulator');
});

test('Playback muestra controles de compilar/enviar (no placeholder)', async ({ page }) => {
  await page.goto('/Playback');
  await expect(page.getByRole('heading', { name: 'Reproducción / despliegue' })).toBeVisible();
  await expect(page.locator('#play-send')).toBeVisible();
  await expect(page.locator('#play-target')).toBeVisible();
});

test('animación blink cambia el framebuffer al reproducir', async ({ page }) => {
  await createProject(page, { name: 'Anim', width: '32', height: '16' });
  const box = await canvasBox(page);
  // añadir texto rellenado
  await page.getByRole('button', { name: 'Herramienta relleno' }).click();
  await page.mouse.click(box.x + 80, box.y + 80);
  await page.waitForTimeout(100);
  // seleccionar el relleno y asignar animación Blink via inspector
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 80, box.y + 80);
  await page.waitForTimeout(100);
  // elegir tipo Blink (value 1)
  await page.locator('#inspector-content [data-field="animKind"]').selectOption('1');
  await page.waitForTimeout(100);
  // medir t0 (blink fase on -> encendido)
  const on = await readFramebuffer(page);
  expect(on.lit).toBeGreaterThan(0);
  // reproducir un instante para avanzar el tiempo
  await page.locator('#btn-play').click();
  await page.waitForTimeout(650); // medio ciclo de Blink normal(1000ms)/2=500 -> apagado
  const off = await readFramebuffer(page);
  await page.locator('#btn-play').click();
  console.log('BLINK on lit=%s, después lit=%s', on.lit, off.lit);
  // el framebuffer cambió (no sólo aria-pressed)
  expect(off.lit).not.toBe(on.lit);
});

test('send guarda el estado actual antes de enviar (no copia vieja)', async ({ page }) => {
  await createProject(page, { name: 'SendGuard', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  page.on('dialog', d => d.accept('HI'));
  await page.mouse.click(box.x + 30, box.y + 30);
  await page.waitForTimeout(150);
  // NO guardar manualmente; enviar debe guardar primero
  await page.locator('#btn-send').click();
  await expect(page.locator('#stat-send')).toContainText('Enviado', { timeout: 10_000 });
  // tras enviar, el proyecto queda guardado (dirty limpio)
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 5_000 });
});