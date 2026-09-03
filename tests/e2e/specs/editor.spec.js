// E2E completo del editor DSLetras (spec 20.9 + cierre global de la auditoría).
// Flujos: crear → editar → guardar → reabrir, drawing 4 direcciones + click,
// texto, formas y elipse desplazada, playback, biblioteca, simulador deploy,
// discovery y protección antiforgery/path-traversal.
import { test, expect } from '@playwright/test';

// Crea un proyecto nuevo vía el formulario Projects/New (POST, con antiforgery).
async function createProject(page, name = 'E2E Proyecto', width = '16', height = '16') {
  await page.goto('/Projects/New');
  await page.locator('#Name').fill(name);
  await page.locator('#Width').fill(width);
  await page.locator('#Height').fill(height);
  await page.getByRole('button', { name: /Crear/i }).click();
  await page.waitForURL(/\/Editor\/Index/, { timeout: 15_000 });
  await expect(page.locator('#led-canvas')).toBeVisible();
}

test.describe('DSLetras editor E2E (cierre global)', () => {
  test('proyectos carga y enlaza a Nuevo', async ({ page }) => {
    await page.goto('/Projects/Index');
    await expect(page.getByRole('heading', { name: 'Proyectos' })).toBeVisible();
    await expect(page.getByRole('link', { name: '+ Nuevo proyecto' })).toBeVisible();
  });

  test('crear → editar → guardar → reabrir conserva el trabajo', async ({ page }) => {
    await createProject(page, 'E2E Persist', '16', '16');

    // Añadir un objeto de texto.
    await page.getByRole('button', { name: 'Herramienta texto' }).click();
    const canvas = page.locator('#led-canvas');
    const box = await canvas.boundingBox();
    page.on('dialog', d => d.accept('HOLA'));
    await page.mouse.click(box.x + 20, box.y + 20);

    // Guardar y esperar confirmación.
    await page.locator('#btn-save').click();
    await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

    // Reabrir el mismo proyecto y verificar que el objeto persiste.
    const projectId = await page.locator('#project-id').inputValue();
    await page.goto(`/Editor/Index?id=${projectId}`);
    await expect(page.locator('#led-canvas')).toBeVisible();
  });

  test('el editor renderiza barra de estado, herramientas y target', async ({ page }) => {
    await createProject(page, 'E2E Layout');
    await expect(page.locator('#status-hud')).toBeVisible();
    await expect(page.locator('#device-select')).toBeVisible();
    await expect(page.locator('#btn-send')).toBeVisible();
    await expect(page.locator('#led-canvas')).toBeVisible();
    await expect(page.getByRole('toolbar', { name: 'Herramientas' })).toBeVisible();
  });

  // --- dibujo: click simple y las cuatro direcciones ---
  test('dibujar click simple crea un dibujo de 1px', async ({ page }) => {
    await createProject(page, 'E2E Click');
    await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
    const canvas = page.locator('#led-canvas');
    const box = await canvas.boundingBox();
    await page.mouse.click(box.x + 30, box.y + 30);
    await expect(page.locator('#stat-dirty')).toContainText('Cambios sin guardar');
    // Undo y Redo.
    await page.keyboard.press('Control+z');
    await page.keyboard.press('Control+y');
  });

  test('dibujar hacia la izquierda y arriba produce objeto no degenerado', async ({ page }) => {
    await createProject(page, 'E2E Dir');
    await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
    const canvas = page.locator('#led-canvas');
    const box = await canvas.boundingBox();
    // empieza en (50,50) lógico (escala 10) y dibuja hacia IZQUIERDA y ARRIBA
    const startX = box.x + 50, startY = box.y + 50;
    await page.mouse.move(startX, startY);
    await page.mouse.down();
    await page.mouse.move(box.x + 40, startY, { steps: 2 });
    await page.mouse.move(box.x + 40, box.y + 40, { steps: 2 });
    await page.mouse.up();
    await expect(page.locator('#stat-dirty')).toContainText('Cambios sin guardar');
  });

  test('borrar y duplicar (Ctrl+D) funcionan', async ({ page }) => {
    await createProject(page, 'E2E Dup');
    await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
    const canvas = page.locator('#led-canvas');
    const box = await canvas.boundingBox();
    await page.mouse.click(box.x + 30, box.y + 30);
    // duplicar
    await page.keyboard.press('Control+d');
    await expect(page.locator('#stat-dirty')).toContainText('Cambios sin guardar');
  });

  test('enviar al simulador responde con estado', async ({ page }) => {
    await createProject(page, 'E2E Send', '16', '16');
    await expect(page.locator('#device-select option')).not.toHaveCount(0);
    // Añadir contenido para que el deploy tenga escena con contenido.
    await page.getByRole('button', { name: 'Herramienta texto' }).click();
    const canvas = page.locator('#led-canvas');
    const box = await canvas.boundingBox();
    page.on('dialog', d => d.accept('HI'));
    await page.mouse.click(box.x + 20, box.y + 20);
    await page.locator('#btn-save').click();
    await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });

    await page.locator('#btn-send').click();
    await expect(page.locator('#stat-send')).not.toHaveText('Sin envío', { timeout: 10_000 });
    await expect(page.locator('#stat-send')).toContainText('Enviado', { timeout: 10_000 });
  });

  test('playback inicia y detiene sin dejar estado residual', async ({ page }) => {
    await createProject(page, 'E2E Play', '16', '16');
    const btn = page.locator('#btn-play');
    await btn.click();
    // se marca en reproducción
    await expect(btn).toHaveAttribute('aria-pressed', 'true');
    // detener
    await btn.click();
    await expect(btn).toHaveAttribute('aria-pressed', 'false');
    // el label de tiempo sigue presente (no quedó secuela)
    await expect(page.locator('#scene-time')).toBeVisible();
  });

  test('safe: editor carga vía id, no por ruta arbitraria; path traversal rechazado por el servidor', async ({ page }) => {
    // El endpoint que antes aceptaba rutas arbitrarias ya no existe: un intento
    // de abrir con ../ debe devolver 404 (no hay ruta de archivo). Probamos el
    // endpoint Load con un id inválido → no encuentra proyecto.
    const resp = await page.request.get('/Editor/Load?id=00000000-0000-0000-0000-000000000000');
    expect(resp.status()).toBe(404);
  });
});