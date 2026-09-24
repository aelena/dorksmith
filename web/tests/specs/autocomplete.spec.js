// @ts-check
const { test, expect } = require('@playwright/test');

test.describe('Autocomplete', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('#intent option')).not.toHaveCount(0);
    await page.locator('#input').focus();
  });

  test('suggests operators by prefix and completes with keyboard', async ({ page }) => {
    await page.keyboard.type('si');
    const list = page.locator('#suggestions');
    await expect(list).toBeVisible();
    await expect(list.locator('li').first()).toContainText('site:');
    await expect(page.locator('#input')).toHaveAttribute('aria-expanded', 'true');

    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('Enter');
    await expect(page.locator('#input')).toHaveValue('site:');
  });

  test('suggests file extensions after filetype: and closes on Escape', async ({ page }) => {
    await page.keyboard.type('filetype:p');
    const list = page.locator('#suggestions');
    await expect(list).toBeVisible();
    await expect(list.locator('li').first().locator('.sg-token')).toHaveText('pdf');
    await page.keyboard.press('Escape');
    await expect(list).toBeHidden();
    await expect(page.locator('#input')).toHaveAttribute('aria-expanded', 'false');
  });

  test('suggests intents for a detected domain and warns about deprecated operators', async ({ page }) => {
    await page.keyboard.type('example.com');
    const list = page.locator('#suggestions');
    await expect(list).toBeVisible();
    await expect(list.locator('.sg-kind').first()).toHaveText('intent');

    await page.fill('#input', 'cache:example.com');
    await expect(page.locator('#input-warnings li').first()).toContainText('deprecated');
  });
});
