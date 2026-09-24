// @ts-check
const { test, expect } = require('@playwright/test');

test.describe('Username search', () => {
  test('expands a handle into unverified profile URLs and queries', async ({ page }) => {
    await page.goto('/#handles');
    await expect(page.locator('#panel-handles')).toBeVisible();
    await expect(page.locator('#handle-categories input').first()).toBeVisible();

    await page.fill('#handle', '@alice42');
    await expect(page.locator('#handle-hint')).toContainText('leading @');
    await page.check('#handle-categories input[value="developer"]');
    await page.click('#expand');

    await expect(page.locator('#handles-results .profile').first()).toBeVisible();
    await expect(page.locator('#handles-results .notice')).toContainText('NOT verified');
    const github = page.locator('#handles-results .profile', { hasText: 'GitHub' }).first();
    await expect(github.locator('.profile-url')).toHaveAttribute('href', 'https://github.com/alice42');
    await expect(github.locator('.profile-url')).toHaveAttribute('rel', /noopener/);
    await expect(github.locator('.status')).toHaveText('not checked');
    await expect(github.locator('.query code')).toContainText('site:github.com "alice42"');

    for (const title of await page.locator('#handles-results .group-title').allInnerTexts()) {
      expect(/^(developer|search-engine)/i.test(title)).toBeTruthy();
    }
    await expect(page.locator('#handles-status')).toContainText('alice42');
  });

  test('rejects handles with whitespace', async ({ page }) => {
    await page.goto('/#handles');
    await page.fill('#handle', 'alice smith');
    await page.click('#expand');
    await expect(page.locator('#handles-error')).toContainText('whitespace');
  });
});
