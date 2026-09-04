// Selección (click/Ctrl/Shift/rect), mover, duplicar, borrar — con píxeles exactos.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, pixelCoords, canvasBox } from './framebuffer-utils.js';
const S = 10;

// dos puntos de lápiz separados
async function drawTwoPoints(page, box) {
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.mouse.click(box.x + 8 * S, box.y + 8 * S);
  await page.waitForTimeout(120);
}

test('Ctrl+click y Shift+click multiseleccionan; click solo reemplaza', async ({ page }) => {
  await createProject(page, { name: 'MultiSel', width: '16', height: '16' });
  const box = await canvasBox(page);
  await drawTwoPoints(page, box);

  // seleccionar el primero (4,4)
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);
  await expect(page.locator('#stat-selection')).toHaveText('1 seleccionado');

  // Shift+click añade el segundo (keyboard.down para que shiftKey llegue al pointerdown)
  await page.keyboard.down('Shift');
  await page.mouse.click(box.x + 8 * S, box.y + 8 * S);
  await page.keyboard.up('Shift');
  await page.waitForTimeout(80);
  await expect(page.locator('#stat-selection')).toHaveText('2 seleccionados');

  // click simple reemplaza a 1
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);
  await expect(page.locator('#stat-selection')).toHaveText('1 seleccionado');
});

test('duplicar (Ctrl+D) crea un objeto nuevo desplazado +1,+1', async ({ page }) => {
  await createProject(page, { name: 'DupExact', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(100);
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(100);
  await page.keyboard.press('Control+d');
  await page.waitForTimeout(150);
  const fb = await readFramebuffer(page);
  const coords = pixelCoords(fb);
  // original en (4,4) + copia en (5,5)
  expect(coords.has('4,4')).toBe(true);
  expect(coords.has('5,5')).toBe(true);
  expect(fb.pixels.length).toBe(2);
});

test('mover arrastra un objeto una distancia exacta en píxeles', async ({ page }) => {
  await createProject(page, { name: 'MoveExact', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(100);
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);
  // arrastrar de (4,4) a (8,8) -> desplazamiento +4,+4
  await page.mouse.move(box.x + 4 * S, box.y + 4 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 8 * S, box.y + 8 * S, { steps: 5 });
  await page.mouse.up();
  await page.waitForTimeout(120);
  const fb = await readFramebuffer(page);
  const coords = pixelCoords(fb);
  expect(coords.has('8,8')).toBe(true);
  expect(coords.has('4,4')).toBe(false);
  expect(fb.pixels.length).toBe(1);
});

test('borrar (Delete) elimina el objeto seleccionado del framebuffer', async ({ page }) => {
  await createProject(page, { name: 'DelExact', width: '16', height: '16' });
  const box = await canvasBox(page);
  await drawTwoPoints(page, box);
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 4 * S, box.y + 4 * S);
  await page.waitForTimeout(80);
  await page.keyboard.press('Delete');
  await page.waitForTimeout(120);
  const fb = await readFramebuffer(page);
  const coords = pixelCoords(fb);
  expect(coords.has('4,4')).toBe(false);   // borrado
  expect(coords.has('8,8')).toBe(true);    // el otro sigue
  expect(fb.pixels.length).toBe(1);
});