import test from 'node:test';
import assert from 'node:assert/strict';

const baseUrl = process.env.VELOTIMER_PUBLIC_STATISTICS_BROWSER_URL;

test('real Results router restores refresh/history and server-backed paging/filters', {
  skip: !baseUrl && 'Requires the dedicated Kestrel test harness URL.',
  timeout: 90_000
}, async () => {
  // No implicit downloads: validation must provision Playwright and its browser first.
  const { chromium } = await import(process.env.VELOTIMER_PLAYWRIGHT_MODULE || 'playwright');
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {})
  });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') console.error('BROWSER CONSOLE:', message.text());
  });
  page.on('requestfailed', request => console.error('BROWSER REQUEST FAILED:', request.url(), request.failure()));
  const id = index => `${index.toString(16).padStart(8, '0')}-0000-0000-0000-000000000001`;
  const canonical = `/statistics/${id(1)}/${id(3)}/${id(4)}`;
  const names = () => page.locator('tbody th[scope="row"]').allTextContents();
  const input = label => page.getByLabel(label, { exact: true });
  const choose = async (label, option) => {
    await input(label).locator('xpath=..').click();
    await page.getByRole('option', { name: option, exact: true }).click();
  };
  const next = () => page.getByRole('button', { name: 'Next results', exact: true });
  const previous = () => page.getByRole('button', { name: 'Previous results', exact: true });
  const scenario = async mode => {
    const response = await page.request.post(`${baseUrl}/__test/scenario/${mode}`);
    assert.equal(response.status(), 200);
  };
  const evidence = async () => (await page.request.get(`${baseUrl}/__test/evidence`)).json();
  const alert = text => page.getByRole('alert').filter({ hasText: text }).waitFor();
  const waitForName = text => page.waitForFunction(expected =>
    document.querySelector('tbody th[scope="row"]')?.textContent === expected, text);
  try {
    await page.goto(`${baseUrl}/statistics`);
    await waitForName('Rider 60 Group A');
    assert.equal(new URL(page.url()).pathname, canonical);
    assert.equal((await names()).length, 25);
    assert.equal(await input('Facility').inputValue(), id(1));
    assert.equal(await input('Statistics item').inputValue(), id(3));
    assert.equal(await input('Configuration').inputValue(), id(4));
    assert.deepEqual(await page.locator('thead th').allTextContents(), ['Profile', 'Duration', 'Average speed (km/h)']);
    assert.deepEqual(await page.locator('tbody tr').first().locator('td').allTextContents(), ['00:01:00.000', '96.000']);
    assert.equal(await previous().isDisabled(), true);
    const navigation = page.locator('a[href="statistics"]');
    assert.equal(await navigation.count(), 1, 'real public navigation link');
    await next().click();
    await waitForName('Rider 35 Group B');
    const nextUrl = page.url();
    assert.equal(new URL(nextUrl).searchParams.get('direction'), 'next');
    assert.match(new URL(nextUrl).searchParams.get('cursor'), /\.1234560\+00:00\|/);
    const secondNames = await names();
    assert.equal(secondNames.length, 25);

    await page.reload();
    await waitForName('Rider 35 Group B');
    assert.equal(page.url(), nextUrl);
    assert.deepEqual(await names(), secondNames);
    await page.goBack();
    await waitForName('Rider 60 Group A');
    assert.equal(new URL(page.url()).search, '');
    await page.goForward();
    await waitForName('Rider 35 Group B');
    assert.equal(page.url(), nextUrl);
    await previous().click();
    await waitForName('Rider 60 Group A');
    assert.equal(new URL(page.url()).searchParams.get('direction'), 'previous');

    await input('Profile name contains').fill('Group A');
    await input('Minimum duration (hh:mm:ss.fff)').fill('00:00:20.000');
    await input('Maximum duration (hh:mm:ss.fff)').fill('00:00:40.000');
    await input('Minimum speed (km/h)').fill('56');
    await input('Maximum speed (km/h)').fill('76');
    assert.equal(page.url().includes('minTime='), false, 'typing remains uncommitted draft state');
    assert.equal((await names())[0], 'Rider 60 Group A', 'typing does not filter loaded page');
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
    await waitForName('Rider 40 Group A');
    const filteredUrl = page.url();
    const query = new URL(filteredUrl).searchParams;
    assert.equal(query.get('name'), 'Group A');
    assert.equal(query.get('minTime'), '00:00:20.000');
    assert.equal(query.get('maxTime'), '00:00:40.000');
    assert.equal(query.get('minSpeed'), '56');
    assert.equal(query.get('maxSpeed'), '76');
    assert.equal(query.has('cursor'), false, 'applied filters reset paging');
    assert.equal(query.has('direction'), false);
    const filteredNames = Array.from({ length: 11 }, (_, index) => `Rider ${40 - index * 2} Group A`);
    assert.deepEqual(await names(), filteredNames);
    await page.reload();
    await waitForName('Rider 40 Group A');
    assert.equal(page.url(), filteredUrl);
    assert.deepEqual(await names(), filteredNames);
    assert.equal(await input('Profile name contains').inputValue(), 'Group A');
    await page.goBack();
    await waitForName('Rider 60 Group A');
    await page.goForward();
    await waitForName('Rider 40 Group A');

    const draftEvidence = await evidence();
    await input('Minimum speed (km/h)').fill('-1');
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
    await alert('filters are invalid');
    assert.equal(page.url(), filteredUrl, 'invalid draft must not change committed URL');
    assert.equal((await evidence()).requests, draftEvidence.requests, 'invalid draft never reaches service');
    assert.deepEqual(await names(), filteredNames, 'invalid draft preserves committed results');

    await page.getByRole('button', { name: 'Clear filters', exact: true }).click();
    await waitForName('Rider 60 Group A');
    assert.equal(new URL(page.url()).search, '');
    await next().click();
    await waitForName('Rider 35 Group B');
    await choose('Facility', 'Zulu');
    await waitForName('Rider 60 Group A');
    assert.equal(new URL(page.url()).pathname, `/statistics/${id(2)}/${id(3)}/${id(5)}`);
    assert.equal(new URL(page.url()).searchParams.has('cursor'), false);
    await page.reload();
    await waitForName('Rider 60 Group A');
    assert.equal(await input('Facility').inputValue(), id(2));
    await navigation.click();
    await page.waitForURL(`${baseUrl}${canonical}`);
    await waitForName('Rider 60 Group A');
    assert.equal(new URL(page.url()).pathname, canonical);

    // Intentional item/configuration changes reset the composite cursor.
    await next().click();
    await waitForName('Rider 35 Group B');
    await choose('Configuration', '3 repetitions: Start – Finish');
    await page.waitForURL(`**/statistics/${id(1)}/${id(3)}/${id(9)}`);
    await waitForName('Rider 60 Group A');
    assert.equal(new URL(page.url()).pathname, `/statistics/${id(1)}/${id(3)}/${id(9)}`);
    assert.equal(new URL(page.url()).search, '');
    await choose('Statistics item', 'Sprint');
    await page.waitForURL(`**/statistics/${id(1)}/${id(7)}/${id(8)}`);
    await waitForName('Rider 60 Group A');
    // Both selections show the same mocked rows. URL and row text alone can
    // settle before Blazor updates the new configuration's input value.
    await page.waitForFunction(expected =>
      [...document.querySelectorAll('input')].some(element => element.value === expected), id(8));
    assert.equal(await input('Configuration').inputValue(), id(8));
    await page.reload();
    await waitForName('Rider 60 Group A');
    assert.equal(await input('Statistics item').inputValue(), id(7));

    // Supplied links restore every filter, including the duration day prefix.
    const direct = `${baseUrl}${canonical}?name=Group+A&minTime=1.00%3A00%3A00.000&maxTime=2.00%3A00%3A00.000&minSpeed=0&maxSpeed=100`;
    await page.goto(direct);
    await page.getByRole('status').filter({ hasText: 'No public results match' }).waitFor();
    assert.equal(await input('Minimum duration (hh:mm:ss.fff)').inputValue(), '1.00:00:00.000');
    assert.equal(await input('Maximum duration (hh:mm:ss.fff)').inputValue(), '2.00:00:00.000');
    assert.equal(await input('Minimum speed (km/h)').inputValue(), '0');
    await page.reload();
    await page.getByRole('status').filter({ hasText: 'No public results match' }).waitFor();
    assert.equal(await names().then(rows => rows.length), 0);

    for (const query of ['minSpeed=NaN', 'minSpeed=-1', 'minSpeed=20&maxSpeed=10',
      'minTime=00%3A00%3A01.0001', 'direction=next', 'cursor=bad&direction=previous', 'offset=0']) {
      const before = (await evidence()).requests;
      await page.goto(`${baseUrl}${canonical}?${query}`);
      await alert('URL filters are invalid');
      assert.equal((await evidence()).requests, before, `invalid URL ${query} must not request results`);
      assert.equal(await names().then(rows => rows.length), 0);
    }
    await page.goto(`${baseUrl}${canonical}?name=a&name=b`);
    await alert('repeated query parameter');
    await page.goto(`${baseUrl}/statistics/${id(2)}/${id(3)}/${id(4)}`);
    await alert('selection is invalid or unavailable');
    assert.equal(new URL(page.url()).pathname, `/statistics/${id(2)}/${id(3)}/${id(4)}`, 'no silent fallback');

    for (const mode of ['no-facilities', 'no-lap', 'no-configurations']) {
      await scenario(mode);
      const before = (await evidence()).requests;
      await page.goto(`${baseUrl}/statistics`);
      await page.getByRole('status').filter({ hasText: 'default public-results selection is unavailable' }).waitFor();
      assert.equal(new URL(page.url()).pathname, '/statistics');
      assert.equal((await evidence()).requests, before);
    }
    for (const mode of ['discovery-error', 'results-error', 'denied']) {
      await scenario(mode);
      await page.goto(`${baseUrl}${canonical}`);
      await alert(mode === 'denied' ? 'access was denied' : 'could not be loaded');
      assert.equal(await page.getByText('No public results match', { exact: false }).count(), 0);
      assert.equal(await page.getByText('secret', { exact: false }).count(), 0);
      await scenario('normal');
      await page.getByRole('button', { name: 'Retry', exact: true }).click();
      await waitForName('Rider 60 Group A');
    }

    await scenario('delayed');
    await page.goto(`${baseUrl}${canonical}`);
    await waitForName('Rider 60 Group A');
    const before = await evidence();
    await input('Profile name contains').fill('slow');
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
    await page.getByLabel('Loading public results').waitFor();
    await page.getByRole('button', { name: 'Clear filters', exact: true }).click();
    await waitForName('Rider 60 Group A');
    await page.waitForFunction(async ({ baseUrl, completed }) =>
      (await (await fetch(`${baseUrl}/__test/evidence`)).json()).completedDelayed > completed,
    { baseUrl, completed: before.completedDelayed });
    assert.equal((await evidence()).cancelled, before.cancelled + 1, 'superseded result request cancelled');
    assert.equal(await page.getByText('Stale secret', { exact: true }).count(), 0, 'late payload ignored');
    const beforeDispose = await evidence();
    await input('Profile name contains').fill('slow');
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
    await page.getByLabel('Loading public results').waitFor();
    await page.locator('a[href=""]').click();
    await page.waitForURL(baseUrl + '/');
    await page.waitForFunction(async ({ baseUrl, completed }) =>
      (await (await fetch(`${baseUrl}/__test/evidence`)).json()).completedDelayed > completed,
    { baseUrl, completed: beforeDispose.completedDelayed });
    assert.equal((await evidence()).cancelled, beforeDispose.cancelled + 1, 'disposed page request cancelled');
    assert.equal(await page.getByText('Stale secret', { exact: true }).count(), 0);
    await scenario('normal');

    // Real responsive controls and Norwegian resource/numeric presentation.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${baseUrl}${canonical}`);
    await waitForName('Rider 60 Group A');
    assert.equal(await input('Profile name contains').isVisible(), true);
    const region = page.locator('div[role="region"]');
    assert.equal(await region.getAttribute('tabindex'), '0', 'overflow table remains keyboard reachable');
    const norwegian = await browser.newContext({ locale: 'nb-NO' });
    try {
      await norwegian.addCookies([{ name: '.AspNetCore.Culture', value: 'c=nb-NO|uic=nb-NO', url: baseUrl }]);
      const translated = await norwegian.newPage();
      await translated.goto(`${baseUrl}${canonical}`);
      await translated.getByRole('heading', { name: 'Offentlige statistikkresultater', exact: true }).waitFor();
      await translated.locator('tbody th[scope="row"]').first().waitFor();
      assert.deepEqual(await translated.locator('thead th').allTextContents(),
        ['Profil', 'Varighet', 'Gjennomsnittshastighet (km/t)']);
      assert.deepEqual(await translated.locator('tbody tr').first().locator('td').allTextContents(),
        ['00:01:00.000', '96,000']);
      assert.equal(await translated.getByRole('button', { name: 'Neste resultater', exact: true }).count(), 1);
    } finally {
      await norwegian.close();
    }
    assert.deepEqual(errors, [], 'no browser runtime errors');
  } catch (error) {
    console.error('BROWSER FAILURE URL:', page.url());
    console.error('BROWSER RUNTIME ERRORS:', JSON.stringify(errors));
    console.error('BROWSER BODY:', (await page.locator('body').innerText()).slice(0, 5000));
    throw error;
  } finally {
    await browser.close();
  }
});
