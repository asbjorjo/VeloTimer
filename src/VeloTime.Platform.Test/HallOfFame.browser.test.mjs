import test from 'node:test';
import assert from 'node:assert/strict';

const baseURL = process.env.VELOTIMER_HALL_OF_FAME_BROWSER_URL;
test('real Hall of Fame router, ordered public table and administrator draft/conflict workflow', {
  skip: !baseURL && 'Requires the dedicated Kestrel Hall of Fame harness.',
  timeout: 150_000
}, async () => {
  const { chromium } = await import(process.env.VELOTIMER_PLAYWRIGHT_MODULE || 'playwright');
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {})
  });
  const context = await browser.newContext({ baseURL, locale: 'de-DE', timezoneId: 'Europe/Oslo', viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  try {
    await page.goto('/hall-of-fame');
    await page.getByRole('table').first().waitFor();
    assert.equal(await page.getByRole('table').count(), 2);
    const first = page.getByRole('table').first();
    assert.deepEqual(await first.locator('thead th').allTextContents(),
      ['Rank', 'Name', 'Completed-result count', 'Speed of best result', 'Date of best result']);
    assert.match(await first.textContent(), /2\.147\.483\.770/);
    assert.match(await first.textContent(), /45,000 km\/h/);
    assert.match(await first.textContent(), /\+01:00 \[Europe\/Oslo\]/);
    // Close the initially-open mobile drawer before testing content-region focus.
    await page.getByRole('button', { name: 'Toggle navigation' }).click();
    const region = page.locator('div[role="region"]').first();
    await region.focus();
    assert.equal(await region.evaluate(element => element === document.activeElement), true);
    assert.equal(await region.evaluate(element => element.scrollWidth > element.clientWidth), true);
    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto('/admin/statistics/hall-of-fame');
    const rows = page.getByLabel('Row limit (1–100)', { exact: true });
    await rows.first().waitFor();
    assert.equal(await rows.count(), 2);
    assert.equal(await rows.first().inputValue(), '10');
    const save = page.getByRole('button', { name: 'Save', exact: true });
    assert.equal(await save.isDisabled(), true);

    // Missing and nonnumeric input must retain the draft, never silently save a default.
    for (const invalid of ['', 'nonsense']) {
      await rows.first().fill(invalid);
      await rows.first().press('Tab');
      await save.click();
      await page.getByRole('alert').waitFor();
      assert.equal((await (await context.request.get('/__test/hall/state')).json()).version, 7);
    }
    await rows.first().fill('100');
    await rows.first().press('Tab');
    await save.click();
    await page.getByRole('status').filter({ hasText: /^All Hall of Fame settings saved\.$/ }).waitFor();
    let settings = await (await context.request.get('/__test/hall/state')).json();
    assert.equal(settings.version, 8);
    assert.equal(settings.sections[0].rowCount, 100);

    // A real concurrent save is surfaced as 409-equivalent conflict and disables blind retries.
    await rows.first().fill('1');
    await rows.first().press('Tab');
    await context.request.post('/__test/hall/conflict');
    await save.click();
    await page.getByRole('alert').filter({ hasText: /settings have changed/i }).waitFor();
    assert.equal(await save.isDisabled(), true);
    assert.equal(await rows.first().inputValue(), '1');
    await page.getByRole('button', { name: /Reload settings/ }).click();
    await page.getByRole('button', { name: /Keep editing/ }).click();
    assert.equal(await rows.first().inputValue(), '1');
    await page.getByRole('button', { name: /Reload settings/ }).click();
    await page.getByRole('button', { name: /Discard/ }).click();
    await page.waitForFunction(() => [...document.querySelectorAll('input')].some(input => input.value === '100'));
    assert.equal(await save.isDisabled(), true);
    await rows.first().fill('1');
    await rows.first().press('Tab');
    await save.click();
    await page.getByRole('status').filter({ hasText: /^All Hall of Fame settings saved\.$/ }).waitFor();
    settings = await (await context.request.get('/__test/hall/state')).json();
    assert.equal(settings.version, 10);
    assert.equal(settings.sections[0].rowCount, 1);

    // Keyboard-operable reordering changes the whole saved document and public rendering.
    const editors = page.locator('section[aria-label^="Section "]');
    const moveSpeed = editors.first().getByRole('button', { name: 'Move Section 1: Browser lap: Speed of best result up', exact: true });
    await moveSpeed.focus();
    await page.keyboard.press('Enter');
    await editors.nth(1).getByRole('button', { name: 'Move Section 2: Browser lap up', exact: true }).focus();
    await page.keyboard.press('Enter');
    await page.getByRole('link', { name: 'Hall of Fame', exact: true }).click();
    await page.getByRole('button', { name: 'Keep editing', exact: true }).click();
    assert.match(page.url(), /\/admin\/statistics\/hall-of-fame$/);

    // Failed reload after confirmed discard must still preserve the draft.
    await context.request.post('/__test/hall/load-failure/true');
    await page.getByRole('button', { name: /Reload settings/ }).click();
    await page.getByRole('button', { name: 'Discard changes', exact: true }).click();
    await page.getByRole('alert').waitFor();
    assert.equal(await save.isDisabled(), false);
    assert.equal(await rows.nth(1).inputValue(), '1'); // The edited section remains second.
    await context.request.post('/__test/hall/load-failure/false');
    await save.click();
    await page.getByRole('status').filter({ hasText: /^All Hall of Fame settings saved\.$/ }).waitFor();
    settings = await (await context.request.get('/__test/hall/state')).json();
    assert.equal(settings.version, 11);
    assert.deepEqual(settings.sections.map(section => section.rankingMode), [0, 1]);
    assert.deepEqual(settings.sections[1].columns, [1, 2, 4]);
    await page.getByRole('link', { name: 'Hall of Fame', exact: true }).click();
    await page.getByRole('table').first().waitFor();
    assert.deepEqual(await page.getByRole('table').first().locator('thead th').allTextContents(), ['Rank', 'Name', 'Best time']);
    assert.deepEqual(await page.getByRole('table').nth(1).locator('thead th').allTextContents(),
      ['Rank', 'Name', 'Speed of best result', 'Completed-result count', 'Date of best result']);
    assert.deepEqual(errors, []);
  } catch (error) {
    console.error('Browser errors:', errors);
    console.error('Page text:', await page.locator('body').innerText());
    throw error;
  } finally {
    await browser.close();
  }
});
