// Animación (frames en varios timestamps) + Send (framebuffer enviado real al simulador).
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';
const S = 10;

function acceptDialogs(page) {
  let pending = '';
  page.on('dialog', d => d.accept(pending));
  return { set: (t) => { pending = t; } };
}

test('animación blink alterna el framebuffer en varios timestamps', async ({ page }) => {
  await createProject(page, { name: 'AnimBlink', width: '16', height: '16' });
  const box = await canvasBox(page);
  // rellenar todo
  await page.getByRole('button', { name: 'Herramienta relleno' }).click();
  await page.mouse.click(box.x + 8 * S, box.y + 8 * S);
  await page.waitForTimeout(100);
  const base = await readFramebuffer(page);
  expect(base.lit).toBe(256);

  // seleccionar y asignar Blink
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 8 * S, box.y + 8 * S);
  await page.waitForTimeout(80);
  await page.locator('#inspector-content [data-field="animKind"]').selectOption('1');
  await page.waitForTimeout(80);

  const samples = [];
  await page.locator('#btn-play').click();
  for (let i = 0; i < 5; i++) {
    await page.waitForTimeout(120);
    samples.push((await readFramebuffer(page)).lit);
  }
  await page.locator('#btn-play').click();
  console.log('BLINK samples (lit):', JSON.stringify(samples));
  // blink alterna encendido/apagado: la muestra cambia entre 256 y 0
  expect(Math.max(...samples)).toBe(256);
  expect(Math.min(...samples)).toBe(0);
});

test('animación slide/marquee desplaza el objeto (offset cambia con el tiempo)', async ({ page }) => {
  const d = acceptDialogs(page);
  await createProject(page, { name: 'AnimSlide', width: '16', height: '16' });
  const box = await canvasBox(page);
  d.set('HI');
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 2 * S, box.y + 2 * S);
  await page.waitForTimeout(100);
  const base = await readFramebuffer(page);
  // seleccionar y asignar Slide (value 3, direction left=0)
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  await page.mouse.click(box.x + 2 * S, box.y + 2 * S);
  await page.waitForTimeout(80);
  await page.locator('#inspector-content [data-field="animKind"]').selectOption('3');
  await page.locator('#inspector-content [data-field="animSpeed"]').selectOption('1'); // normal 1000ms
  await page.waitForTimeout(80);

  // avanzar el tiempo de reproducción y medir el min-x de los píxeles (offset)
  await page.locator('#btn-play').click();
  await page.waitForTimeout(400);
  const mid = await readFramebuffer(page);
  await page.waitForTimeout(400);
  const later = await readFramebuffer(page);
  await page.locator('#btn-play').click();

  const minX = f => f.pixels.length ? Math.min(...f.pixels.map(p => p.x)) : -1;
  console.log('SLIDE minX base=%s mid=%s later=%s', minX(base), minX(mid), minX(later));
  // el slide desplaza el objeto: la posición X mínima cambia entre instantes
  expect(minX(mid) !== minX(later) || mid.lit !== later.lit).toBe(true);
});

test('Send demuestra el framebuffer enviado al simulador (no sólo "Enviado")', async ({ page }) => {
  const d = acceptDialogs(page);
  await createProject(page, { name: 'SendFb', width: '16', height: '16' });
  const box = await canvasBox(page);
  d.set('HI');
  await page.getByRole('button', { name: 'Herramienta texto' }).click();
  await page.mouse.click(box.x + 2 * S, box.y + 2 * S);
  await page.waitForTimeout(100);

  // capturar el framebuffer del EDITOR antes de enviar
  const editorFb = await readFramebuffer(page);
  const editorCoords = new Set(editorFb.pixels.map(p => `${p.x},${p.y}`));

  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  await page.locator('#btn-send').click();
  await expect(page.locator('#stat-send')).toContainText('Enviado', { timeout: 10_000 });

  // leer el framebuffer REAL que recibió el simulador
  const sim = await (await page.request.get('/Deploy/SimulatorFrame?timeMs=0')).json();
  expect(sim.success).toBe(true);
  const simCoords = new Set((sim.pixels || []).map(p => `${p.x},${p.y}`));
  console.log('SEND editor lit=%s simulator lit=%s checksum=%s',
    editorFb.lit, sim.lit, sim.checksum);

  // equivalencia R5: editor framebuffer == simulador framebuffer (mismos píxeles encendidos)
  expect(sim.lit).toBe(editorFb.lit);
  expect(simCoords).toEqual(editorCoords);
});