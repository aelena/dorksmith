// @ts-check
const { test, expect } = require('@playwright/test');

test.describe('Generator', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('#intent option')).not.toHaveCount(0);
  });

  test('infers the input type, generates variants and builds Google links client-side', async ({ page }) => {
    await page.fill('#input', 'example.com');
    await page.keyboard.press('Escape');
    await expect(page.locator('#input-type')).toHaveValue('domain');
    await expect(page.locator('#input-hint')).toContainText('Detected: Domain');

    await page.selectOption('#intent', 'public-documents');
    await expect(page.locator('#intent-description')).toContainText('Reports');
    await page.check('#filetypes input[value="pdf"]');
    await page.fill('#exclude', 'jobs');
    await page.click('#generate');

    const cards = page.locator('#results .variant');
    await expect(cards.first()).toBeVisible();
    expect(await cards.count()).toBeGreaterThanOrEqual(3);

    const firstQuery = await cards.first().locator('.query code').innerText();
    expect(firstQuery).toBe('site:example.com filetype:pdf -jobs');
    await expect(cards.first().locator('.variant-label')).toContainText('Balanced');
    await expect(cards.first().locator('.chip').first()).toHaveText('site:');

    const href = await cards.first().locator('a.open').getAttribute('href');
    expect(href).toBe('https://www.google.com/search?q=' + encodeURIComponent(firstQuery));
    await expect(cards.first().locator('a.open')).toHaveAttribute('rel', /noopener/);
    await expect(page.locator('#results-meta')).toContainText('catalog');
  });

  test('Ctrl+Enter generates and the defensive notice appears for exposure intents', async ({ page }) => {
    await page.fill('#input', 'example.com');
    await page.keyboard.press('Escape');
    await page.selectOption('#intent', 'exposed-config-files');
    await expect(page.locator('#intent-safety')).toBeVisible();
    await page.locator('#input').press('Control+Enter');
    await expect(page.locator('#results .variant').first()).toBeVisible();
    await expect(page.locator('#results .variant').first().locator('.query code')).toContainText('filetype:env');
  });

  test('shows a field-level error for invalid input and a 422 hint for missing options', async ({ page }) => {
    await page.fill('#input', 'not a domain');
    await page.selectOption('#input-type', 'domain');
    await page.click('#generate');
    await expect(page.locator('#form-error')).toContainText('valid domain');
    await expect(page.locator('#input')).toHaveAttribute('aria-invalid', 'true');

    await page.fill('#input', 'Alice Smith');
    await page.selectOption('#input-type', 'person');
    await page.selectOption('#intent', 'person-organization');
    await page.click('#generate');
    await expect(page.locator('#form-error')).toContainText('options.organization');
  });

  test('copies a query to the clipboard and confirms', async ({ page }) => {
    await page.fill('#input', 'quarterly roadmap');
    await page.click('#generate');
    const first = page.locator('#results .variant').first();
    await expect(first).toBeVisible();
    const query = await first.locator('.query code').innerText();
    await first.locator('button.copy').click();
    await expect(page.locator('#toast')).toBeVisible();
    await expect(page.locator('#toast')).toContainText('Copied');
    const clip = await page.evaluate(() => navigator.clipboard.readText());
    expect(clip).toBe(query);
  });

  test('renders a rate-limit error with retry information', async ({ page }) => {
    await page.route('**/api/v1/dorks/generate', route => route.fulfill({
      status: 429,
      contentType: 'application/json',
      headers: { 'Retry-After': '1140' },
      body: JSON.stringify({ error: 'rate_limit_exceeded', message: 'Hourly request limit reached.', retryAfterSeconds: 1140 }),
    }));
    await page.fill('#input', 'example.com');
    await page.click('#generate');
    const box = page.locator('#results-error');
    await expect(box).toBeVisible();
    await expect(box).toContainText('Rate limit reached');
    await expect(box).toContainText('19 minutes');
  });

  test('"/" focuses the target box and Alt+3 switches to the operator guide', async ({ page }) => {
    await page.locator('body').click();
    await page.keyboard.press('/');
    await expect(page.locator('#input')).toBeFocused();
    await page.keyboard.press('Alt+3');
    await expect(page.locator('#panel-operators')).toBeVisible();
    await expect(page.locator('#panel-generator')).toBeHidden();
  });
});
