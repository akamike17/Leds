// Verificación de coordenadas EXACTAS por herramienta (ins.txt: cada operación
// gráfica comprueba coordenadas/píxeles esperados, no "lit > 0").
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, pixelCoords, canvasBox } from './framebuffer-utils.js';

// Escala de píxel = 10 (canvas.style width = width*10). físico -> lógico = /10.
const S = 10;

test('pencil un click deja exactamente 1 px en la celda lógica', async ({ page }) => {
  await createProject(page, { name: 'PencilExact', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
  await page.mouse.click(box.x + 3 * S, box.y + 3 * S);
  await page.waitForTimeout(120);
  const fb = await readFramebuffer(page);
  expect(fb.lit).toBe(1);
  expect(fb.pixels[0]).toMatchObject({ x: 3, y: 3 });
});

test('line horizontal de (4,4)->(12,4) deja píxeles en x=4..12, y=4', async ({ page }) => {
  await createProject(page, { name: 'LineExact', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta línea' }).click();
  await page.mouse.move(box.x + 4 * S, box.y + 4 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 12 * S, box.y + 4 * S, { steps: 5 });
  await page.mouse.up();
  await page.waitForTimeout(120);
  const fb = await readFramebuffer(page);
  const coords = pixelCoords(fb);
  // 9 píxeles, todos en y=4, x de 4 a 12 (Bresenham con pasos exactos)
  expect(fb.pixels.length).toBe(9);
  for (let x = 4; x <= 12; x++) expect(coords.has(`${x},4`)).toBe(true);
});

test('rect de (5,6)->(10,9) deja el perímetro exacto (sin interior)', async ({ page }) => {
  await createProject(page, { name: 'RectExact', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta rectángulo' }).click();
  await page.mouse.move(box.x + 5 * S, box.y + 6 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 10 * S, box.y + 9 * S, { steps: 5 });
  await page.mouse.up();
  await page.waitForTimeout(120);
  const fb = await readFramebuffer(page);
  const coords = pixelCoords(fb);
  // ancho 6 (x=5..10), alto 4 (y=6..9) -> perímetro = 2*6 + 2*4 - 4 = 16
  expect(fb.pixels.length).toBe(16);
  // esquinas
  expect(coords.has('5,6')).toBe(true);
  expect(coords.has('10,6')).toBe(true);
  expect(coords.has('5,9')).toBe(true);
  expect(coords.has('10,9')).toBe(true);
  // interior (6,7) NO encendido
  expect(coords.has('6,7')).toBe(false);
  expect(coords.has('7,7')).toBe(false);
});

test('elipse deja un anillo (centro vacío, borde encendido, simétrica)', async ({ page }) => {
  await createProject(page, { name: 'EllipseExact', width: '16', height: '16' });
  const box = await canvasBox(page);
  await page.getByRole('button', { name: 'Herramienta elipse' }).click();
  // drag de (3,3) a (12,11) -> elipse size 10x9, position (3,3)
  await page.mouse.move(box.x + 3 * S, box.y + 3 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 12 * S, box.y + 11 * S, { steps: 6 });
  await page.mouse.up();
  await page.waitForTimeout(120);
  const fb = await readFramebuffer(page);
  const coords = pixelCoords(fb);
  // Centro matemático (7,7) VACÍO.
  expect(coords.has('7,7')).toBe(false);
  // Fila central y=7: extremos x=3 y x=12 encendidos (eje horizontal).
  expect(coords.has('3,7')).toBe(true);
  expect(coords.has('12,7')).toBe(true);
  // Fila superior del anillo y=4 y fila inferior y=10 (simetría vertical).
  expect(coords.has('5,4')).toBe(true);
  expect(coords.has('10,4')).toBe(true);
  expect(coords.has('5,10')).toBe(true);
  expect(coords.has('10,10')).toBe(true);
  // El vacío central se extiende a (7,4) y (7,10) (fuera del anillo en el eje vertical).
  expect(coords.has('7,4')).toBe(false);
  expect(coords.has('7,10')).toBe(false);
  // 18 píxeles de perímetro (2*ancho_anillo + 2*alto_anillo interior)
  expect(fb.pixels.length).toBe(18);
});

test('fill llena una región delimitada y NO se desborda', async ({ page }) => {
  await createProject(page, { name: 'FillExact', width: '16', height: '16' });
  const box = await canvasBox(page);
  // dibujar un rect como borde (5,5)->(10,10) para delimitar una región interior
  await page.getByRole('button', { name: 'Herramienta rectángulo' }).click();
  await page.mouse.move(box.x + 5 * S, box.y + 5 * S);
  await page.mouse.down();
  await page.mouse.move(box.x + 10 * S, box.y + 10 * S, { steps: 5 });
  await page.mouse.up();
  await page.waitForTimeout(100);
  const before = await readFramebuffer(page);
  // rellenar el interior
  await page.getByRole('button', { name: 'Herramienta relleno' }).click();
  await page.mouse.click(box.x + 7 * S, box.y + 7 * S);
  await page.waitForTimeout(150);
  const fb = await readFramebuffer(page);
  const coords = pixelCoords(fb);
  // el interior de la caja (6..9, 6..9) debe estar lleno, pero el exterior de la caja NO
  expect(coords.has('6,6')).toBe(true);
  expect(coords.has('9,9')).toBe(true);
  expect(coords.has('6,9')).toBe(true);
  expect(coords.has('9,6')).toBe(true);
  // fuera de la caja (0,0) debe seguir vacío
  expect(coords.has('0,0')).toBe(false);
  expect(coords.has('14,14')).toBe(false);
  console.log('FILL antes=%s después=%s', before.lit, fb.lit);
});