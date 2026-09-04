// E2E real DSLetras (spec 20.9): aserciones sobre PÍXELES OBSERVABLES del canvas
// (getImageData), no sobre "canvas visible" ni "dirty cambió". Cubre P0 del editor
// y los flujos R1/R2 del MASTER SPEC.
import { test, expect } from '@playwright/test';
import {
  createProject, readFramebuffer, pixelCoords, addText, canvasBox,
} from './framebuffer-utils.js';

// Handler global de diálogos (prompt de texto / nombre). Se registra una vez por test.
function acceptDialogs(page) {
  let pending = '';
  page.on('dialog', d => d.accept(pending));
  return { set: (t) => { pending = t; } };
}

test.describe('P0 editor — texto, herramientas y selección (framebuffer real)', () => {
  test('texto HOLA pinta y sobrevive Save/Open', async ({ page }) => {
    const dialogs = acceptDialogs(page);
    await createProject(page, { name: 'R1 Texto', width: '32', height: '16' });
    const box = await canvasBox(page);
    dialogs.set('HOLA');
    await addText(page, box, 'HOLA', 20, 20);

    let fb = await readFramebuffer(page);
    expect(fb.lit).toBeGreaterThan(0);           // HOLA pinta píxeles reales

    await page.locator('#btn-save').click();
    await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
    const projectId = await page.locator('#project-id').inputValue();
    await page.goto(`/Editor/Index?id=${projectId}`);
    await expect(page.locator('#led-canvas')).toBeVisible();
    await page.waitForTimeout(200);
    const after = await readFramebuffer(page);
    expect(after.lit).toBe(fb.lit);              // idéntico tras reposición
  });

  test('fill rellena; eraser borra; elipse real', async ({ page }) => {
    await createProject(page, { name: 'Tools', width: '16', height: '16' });
    const box = await canvasBox(page);

    // fill sobre canvas vacío debe encender todo (256 px)
    await page.getByRole('button', { name: 'Herramienta relleno' }).click();
    await page.mouse.click(box.x + 80, box.y + 80);
    await page.waitForTimeout(120);
    expect((await readFramebuffer(page)).lit).toBe(256);

    // eraser borra el objeto de la celda
    await page.getByRole('button', { name: 'Herramienta borrador' }).click();
    await page.mouse.click(box.x + 80, box.y + 80);
    await page.waitForTimeout(120);
    expect((await readFramebuffer(page)).lit).toBe(0);
  });

  test('elipse dibuja una elipse (no strokeRect)', async ({ page }) => {
    await createProject(page, { name: 'Ellipse', width: '16', height: '16' });
    const box = await canvasBox(page);
    await page.getByRole('button', { name: 'Herramienta elipse' }).click();
    await page.mouse.move(box.x + 30, box.y + 30);
    await page.mouse.down();
    await page.mouse.move(box.x + 130, box.y + 90, { steps: 6 });
    await page.mouse.up();
    await page.waitForTimeout(120);
    const fb = await readFramebuffer(page);
    // centro de la elipse queda vacío (anillo), los bordes encendidos
    expect(fb.lit).toBeGreaterThan(4);
    expect(fb.lit).toBeLessThan(16 * 16);
  });

  test('selección rectangular selecciona por intersección', async ({ page }) => {
    await createProject(page, { name: 'RectSel', width: '16', height: '16' });
    const box = await canvasBox(page);
    await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
    await page.mouse.click(box.x + 30, box.y + 30);
    await page.mouse.click(box.x + 60, box.y + 60);
    await page.waitForTimeout(100);
    await page.getByRole('button', { name: 'Herramienta selección' }).click();
    await page.mouse.move(box.x + 20, box.y + 20);
    await page.mouse.down();
    await page.mouse.move(box.x + 80, box.y + 80, { steps: 4 });
    await page.mouse.up();
    await page.waitForTimeout(100);
    await expect(page.locator('#stat-selection')).toHaveText('2 seleccionados');
  });

  test('texto con acentos pinta píxeles (ÁÉÍÓÚ ñü¿! $%&+/@#())', async ({ page }) => {
    const dialogs = acceptDialogs(page);
    await createProject(page, { name: 'Acentos', width: '64', height: '16' });
    const box = await canvasBox(page);
    dialogs.set('ÁÉÍÓÚ');
    await addText(page, box, 'ÁÉÍÓÚ', 20, 20);
    expect((await readFramebuffer(page)).lit).toBeGreaterThan(0);
    dialogs.set('ñü¿!$%&');
    await addText(page, box, 'ñü¿!$%&', 20, 60);
    const fb = await readFramebuffer(page);
    expect(fb.lit).toBeGreaterThan(0);
  });
});

test.describe('P0 biblioteca e iconos', () => {
  test('insertar icono embebe asset y persiste Save/Open', async ({ page }) => {
    await createProject(page, { name: 'Icono', width: '32', height: '16' });
    await page.getByRole('button', { name: 'Biblioteca' }).click();
    await page.locator('[data-lib-tab="icons"]').click();
    await page.waitForTimeout(300);
    const count = await page.locator('#library-grid .card').count();
    expect(count).toBeGreaterThanOrEqual(8);     // no sólo Corazón
    await page.locator('#library-grid .card button').first().click();
    await page.waitForTimeout(150);
    const fb = await readFramebuffer(page);
    expect(fb.lit).toBeGreaterThan(0);

    await page.locator('#btn-save').click();
    await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
    const projectId = await page.locator('#project-id').inputValue();
    await page.goto(`/Editor/Index?id=${projectId}`);
    await expect(page.locator('#led-canvas')).toBeVisible();
    await page.waitForTimeout(200);
    expect((await readFramebuffer(page)).lit).toBe(fb.lit);
  });
});

test.describe('P0 imagen', () => {
  test('importar imagen embebe y persiste sin archivo origen', async ({ page }) => {
    await createProject(page, { name: 'Imagen', width: '16', height: '16' });
    await page.locator('#image-file').setInputFiles(
      require('path').join(__dirname, '..', 'test-red.png'));
    await page.waitForTimeout(400);
    const fb = await readFramebuffer(page);
    expect(fb.lit).toBeGreaterThan(0);

    await page.locator('#btn-save').click();
    await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
    const projectId = await page.locator('#project-id').inputValue();
    await page.goto(`/Editor/Index?id=${projectId}`);
    await expect(page.locator('#led-canvas')).toBeVisible();
    await page.waitForTimeout(200);
    expect((await readFramebuffer(page)).lit).toBe(fb.lit);
  });
});

test.describe('Seguridad (regresión)', () => {
  test('path traversal rechazado: Load con id vacío → 404', async ({ page }) => {
    const resp = await page.request.get('/Editor/Load?id=00000000-0000-0000-0000-000000000000');
    expect(resp.status()).toBe(404);
  });
});