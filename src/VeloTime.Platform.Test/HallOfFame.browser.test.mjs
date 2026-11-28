import test from 'node:test';
import assert from 'node:assert/strict';

const baseUrl = process.env.VELOTIMER_PUBLIC_STATISTICS_BROWSER_URL;

test('Hall of Fame preserves URL selections and fixed skeleton table geometry', {
  skip: !baseUrl && 'Requires the dedicated Kestrel test harness URL.',
  timeout: 90_000
}, async () => {
  const { chromium } = await import(process.env.VELOTIMER_PLAYWRIGHT_MODULE || 'playwright');
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {})
  });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const scenario = async mode => {
    assert.equal((await page.request.post(`${baseUrl}/__test/scenario/${mode}`)).status(), 200);
  };
  const table = () => page.locator('.hall-of-fame-table:visible');
  const waitForName = name => page.waitForFunction(expected =>
    [...document.querySelectorAll('tbody th[scope="row"]')].some(cell => cell.textContent === expected), name);
  const requestCount = async () => (await (await page.request.get(baseUrl + '/__test/evidence')).json()).hallOfFameRequests;
  const categoryRequestCounts = async () =>
    (await (await page.request.get(baseUrl + '/__test/evidence')).json()).hallOfFameCategoryRequests;
  const assertCategoryDelta = async (before, expected, message) => {
    const after = await categoryRequestCounts();
    const delta = Object.fromEntries([...new Set([...Object.keys(before), ...Object.keys(after)])]
      .map(category => [category, (after[category] || 0) - (before[category] || 0)])
      .filter(([, count]) => count !== 0)
      .sort(([left], [right]) => left.localeCompare(right)));
    assert.deepEqual(delta, expected, message);
  };
  const waitForRequestCount = count => page.waitForFunction(async ({ base, expected }) =>
    (await (await fetch(base + '/__test/evidence')).json()).hallOfFameRequests >= expected,
    { base: baseUrl, expected: count });
  const geometry = () => table().evaluate(element => {
    const table = element.querySelector('table');
    const bounds = table.getBoundingClientRect();
    return {
      height: bounds.height,
      width: bounds.width,
      columns: [...table.querySelectorAll('thead th')].map(cell => {
        const rect = cell.getBoundingClientRect();
        return { x: rect.x - bounds.x, width: rect.width };
      })
    };
  });
  const stable = (before, after) => {
    assert.ok(Math.abs(before.height - after.height) <= 1, 'reserved table height is stable');
    assert.ok(Math.abs(before.width - after.width) <= 1, 'table width is stable');
    assert.equal(before.columns.length, after.columns.length);
    before.columns.forEach((column, index) => {
      assert.ok(Math.abs(column.x - after.columns[index].x) <= 1, 'column position is stable');
      assert.ok(Math.abs(column.width - after.columns[index].width) <= 1, 'column width is stable');
    });
  };
  try {
    await scenario('hof-delayed');
    const beforeInitialPreload = await categoryRequestCounts();
    await page.goto(`${baseUrl}/hall-of-fame`);
    await table().locator('.skeleton-row').first().waitFor();
    assert.equal(await table().getAttribute('aria-busy'), 'true');
    assert.equal(await table().locator('.skeleton-row').count(), 10);
    assert.equal(await table().locator('.mud-skeleton').count(), 40);
    const before = await geometry();
    await waitForName('lap global');
    await waitForRequestCount(6);
    await assertCategoryDelta(beforeInitialPreload,
      { '1000m': 1, '200m': 1, '3000m': 1, '4000m': 1, lap: 1, laps: 1 },
      'initial preload requests every category exactly once');
    stable(before, await geometry());
    assert.equal(await table().locator('.placeholder-row').count(), 9);
    assert.equal(await page.locator('a[href="hall-of-fame"]').count(), 1);
    assert.deepEqual(await table().locator('thead th').allTextContents(),
      ['Profile', 'Time', 'Speed (km/h)', 'Facility']);

    const beforeTab = await requestCount();
    const beforeTabCategories = await categoryRequestCounts();
    await page.getByRole('tab', { name: '200m', exact: true }).click();
    await waitForName('200m global');
    assert.equal(await requestCount(), beforeTab, 'tab changes use preloaded results');
    await assertCategoryDelta(beforeTabCategories, {}, 'tab changes issue no category requests');
    assert.equal(new URL(page.url()).searchParams.get('tab'), '200m');
    await page.getByLabel('Facility', { exact: true }).locator('xpath=..').click();
    const beforeFacility = await requestCount();
    const beforeFacilityCategories = await categoryRequestCounts();
    await page.getByRole('option', { name: 'Zulu', exact: true }).click();
    await waitForName('200m selected');
    await waitForRequestCount(beforeFacility + 6);
    assert.equal(await requestCount() - beforeFacility, 6, 'facility selection preloads all categories');
    await assertCategoryDelta(beforeFacilityCategories,
      { '1000m': 1, '200m': 1, '3000m': 1, '4000m': 1, lap: 1, laps: 1 },
      'facility selection preloads every category exactly once');
    const selectedUrl = page.url();
    assert.ok(new URL(selectedUrl).searchParams.has('facilityId'));
    await page.getByLabel('Facility', { exact: true }).locator('xpath=..').click();
    const beforeGlobal = await requestCount();
    const beforeGlobalCategories = await categoryRequestCounts();
    await page.getByRole('option', { name: 'All facilities', exact: true }).click();
    await waitForName('200m global');
    assert.equal(await requestCount(), beforeGlobal, 'returning to all facilities uses cached results');
    await assertCategoryDelta(beforeGlobalCategories, {}, 'returning to all facilities issues no requests');
    assert.equal(new URL(page.url()).searchParams.has('facilityId'), false);
    await page.getByLabel('Facility', { exact: true }).locator('xpath=..').click();
    const beforeCachedFacility = await requestCount();
    const beforeCachedFacilityCategories = await categoryRequestCounts();
    await page.getByRole('option', { name: 'Zulu', exact: true }).click();
    await waitForName('200m selected');
    assert.equal(await requestCount(), beforeCachedFacility, 'revisiting a facility uses cached results');
    await assertCategoryDelta(beforeCachedFacilityCategories, {}, 'revisiting a facility issues no requests');
    await page.reload();
    await waitForName('200m selected');
    assert.equal(page.url(), selectedUrl);
    await page.getByRole('tab', { name: '3000m', exact: true }).click();
    await waitForName('3000m selected');
    await page.goBack();
    await waitForName('200m selected');
    await page.goForward();
    await waitForName('3000m selected');

    for (const width of [1280, 390]) {
      await page.setViewportSize({ width, height: 900 });
      if (width === 390) {
        await page.getByRole('tab', { name: 'Lap', exact: true }).press('ArrowLeft');
        await page.waitForFunction(() => document.activeElement?.textContent === 'Lap count');
        await page.getByRole('tab', { name: 'Lap count', exact: true }).press('Enter');
      }
      else await page.getByRole('tab', { name: 'Lap count', exact: true }).click();
      await table().locator('table.laps').waitFor();
      await waitForName('laps selected');
      assert.equal(await table().locator('.skeleton-row').count(), 0);
      assert.deepEqual(await table().locator('thead th').allTextContents(), ['Profile', 'Lap count', 'Total distance']);
      assert.equal(await table().getByText('Arena', { exact: true }).count(), 0);
      if (width === 390) {
        await page.getByRole('tab', { name: 'Lap count', exact: true }).press('ArrowRight');
        await page.waitForFunction(() => document.activeElement?.textContent === 'Lap');
        await page.getByRole('tab', { name: 'Lap', exact: true }).press('Enter');
      }
      else await page.getByRole('tab', { name: 'Lap', exact: true }).click();
      await table().locator('table.times').waitFor();
      await waitForName('lap selected');
      assert.equal(await table().locator('.skeleton-row').count(), 0);
    }

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.getByRole('tab', { name: '1000m', exact: true }).click();
    await waitForName('1000m selected');
    assert.equal(await table().locator('.skeleton-row').count(), 0);

    // Switching tabs while all initial requests are in flight must not duplicate or discard results.
    await scenario('hof-delayed');
    const beforeInFlight = await requestCount();
    const beforeInFlightCategories = await categoryRequestCounts();
    await page.goto(baseUrl + '/hall-of-fame');
    await table().locator('.skeleton-row').first().waitFor();
    await page.getByRole('tab', { name: '4000m', exact: true }).click();
    await page.getByRole('tab', { name: '200m', exact: true }).click();
    await waitForName('200m global');
    await page.waitForTimeout(1200);
    assert.equal(await table().locator('tbody th[scope="row"]').first().textContent(), '200m global');
    assert.equal(await requestCount() - beforeInFlight, 6, 'in-flight tab changes reuse the initial preload');
    await assertCategoryDelta(beforeInFlightCategories,
      { '1000m': 1, '200m': 1, '3000m': 1, '4000m': 1, lap: 1, laps: 1 },
      'in-flight tab changes do not duplicate or discard category requests');

    await scenario('hof-partial-error');
    const beforePartial = await requestCount();
    await page.goto(baseUrl + '/hall-of-fame');
    await waitForName('lap global');
    await waitForRequestCount(beforePartial + 6);
    await page.getByRole('tab', { name: '200m', exact: true }).click();
    await page.getByRole('alert').filter({ hasText: 'Hall of Fame could not be loaded' }).waitFor();
    await page.getByRole('tab', { name: '3000m', exact: true }).click();
    await waitForName('3000m global');
    await scenario('normal');
    const beforeRetry = await requestCount();
    const beforeRetryCategories = await categoryRequestCounts();
    await page.getByRole('tab', { name: '200m', exact: true }).click();
    await page.getByRole('button', { name: 'Retry', exact: true }).click();
    await waitForName('200m global');
    assert.equal(await requestCount() - beforeRetry, 1, 'retry fetches only the failed category');
    await assertCategoryDelta(beforeRetryCategories, { '200m': 1 }, 'retry requests only the failed category');

    await scenario('hof-empty');
    await page.reload();
    await table().getByText('No eligible public results yet.', { exact: true }).waitFor();
    assert.equal(await table().locator('tbody tr').count(), 10);
    await page.goto(`${baseUrl}/hall-of-fame?tab=lap&category=laps`);
    await page.getByRole('alert').filter({ hasText: 'Select an available facility' }).waitFor();
    await page.getByRole('link', { name: 'Show global lap rankings', exact: true }).click();
    await table().getByText('No eligible public results yet.', { exact: true }).waitFor();
    const beforeUnavailableFacility = await categoryRequestCounts();
    await page.goto(`${baseUrl}/hall-of-fame?tab=lap&facilityId=8a6b1234-5678-90ab-cdef-1234567890ab`);
    await page.getByRole('alert').filter({ hasText: 'Select an available facility' }).waitFor();
    await assertCategoryDelta(beforeUnavailableFacility, {},
      'a well-formed but unavailable facility is rejected before Hall of Fame requests');
    await scenario('results-error');
    await page.reload();
    await page.getByRole('alert').filter({ hasText: 'Hall of Fame could not be loaded' }).waitFor();
    await scenario('normal');
    await page.getByRole('button', { name: 'Retry', exact: true }).click();
    await waitForName('lap global');
    for (const mode of ['hof-facility-api-error', 'hof-facility-invalid-data']) {
      await scenario(mode);
      await page.reload();
      await page.getByRole('alert').filter({ hasText: 'Hall of Fame could not be loaded' }).waitFor();
      assert.equal(await page.getByText('secret', { exact: false }).count(), 0);
      await scenario('normal');
      await page.getByRole('button', { name: 'Retry', exact: true }).click();
      await waitForName('lap global');
    }
    assert.deepEqual(errors, []);
  } finally {
    await browser.close();
  }
});
