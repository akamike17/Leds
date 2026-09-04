// Biblioteca con imágenes importadas (P0): importar -> listar en biblioteca -> insertar.
import { test, expect } from '@playwright/test';
import path from 'path';
import { createProject, readFramebuffer } from './framebuffer-utils.js';

test('importar imagen la guarda en la biblioteca y se puede insertar desde "Imágenes"', async ({ page }) => {
  await createProject(page, { name: 'LibImg', width: '16', height: '16' });

  // importar la imagen de prueba
  await page.locator('#image-file').setInputFiles(path.join(__dirname, '..', 'test-red.png'));
  await page.waitForTimeout(500);
  const afterImport = await readFramebuffer(page);
  expect(afterImport.lit).toBeGreaterThan(0);

  // borrar el objeto del proyecto (para probar reinsertar desde biblioteca)
  await page.getByRole('button', { name: 'Herramienta selección' }).click();
  const canvas = page.locator('#led-canvas');
  const box = await canvas.boundingBox();
  await page.mouse.click(box.x + 8, box.y + 8);
  await page.waitForTimeout(80);
  await page.keyboard.press('Delete');
  await page.waitForTimeout(100);
  expect((await readFramebuffer(page)).lit).toBe(0);

  // abrir biblioteca -> tab Imágenes -> debe listar la imagen importada
  await page.getByRole('button', { name: 'Biblioteca' }).click();
  await page.locator('[data-lib-tab="images"]').click();
  await page.waitForTimeout(300);
  const imgCards = await page.locator('#library-grid .card').count();
  console.log('IMAGES IN LIBRARY:', imgCards);
  expect(imgCards).toBeGreaterThanOrEqual(1);

  // insertar la primera imagen de la biblioteca
  await page.locator('#library-grid .card button').first().click();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBeGreaterThan(0);

  // guardar + reabrir conserva
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const lit = (await readFramebuffer(page)).lit;
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBe(lit);
});