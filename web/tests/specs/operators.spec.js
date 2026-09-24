// @ts-check
const { test, expect } = require('@playwright/test');

test.describe('Operator guide', () => {
  test('searches operators and marks deprecated ones', async ({ page }) => {
    await page.goto('/#operators');
    await expect(page.locator('#panel-operators')).toBeVisible();
    await expect(page.locator('#op-list .operator').first()).toBeVisible();
    const total = await page.locator('#op-list .operator').count();
    expect(total).toBeGreaterThan(20);

    await page.fill('#op-search', 'cache');
    await expect(page.locator('#op-list .operator')).toHaveCount(1);
    const card = page.locator('#op-list .operator').first();
    await expect(card.locator('.op-token')).toHaveText('cache:');
    await expect(card.locator('.badge')).toHaveText('deprecated');
    await expect(card.locator('.op-caveats')).toContainText('Wayback');
    await expect(card.locator('.op-source a').first()).toHaveAttribute('rel', /noopener/);
  });

  test('support filter narrows to deprecated and reliable sets', async ({ page }) => {
    await page.goto('/#operators');
    await page.check('input[name="op-filter"][value="deprecated"]');
    const cards = page.locator('#op-list .operator');
    await expect(cards.first()).toBeVisible();
    for (const badge of await cards.locator('.badge').allInnerTexts()) expect(badge).toBe('deprecated');

    await page.check('input[name="op-filter"][value="reliable"]');
    await expect(cards.first()).toBeVisible();
    for (const badge of await cards.locator('.badge').allInnerTexts()) expect(['official', 'working']).toContain(badge);
    await expect(page.locator('#op-count')).toContainText('of');
  });
});
