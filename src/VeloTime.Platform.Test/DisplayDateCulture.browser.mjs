// Opt-in: start DisplaySettingsBrowserHostTests first, then set DISPLAY_BROWSER_URL.
// PLAYWRIGHT_MODULE may point to an existing Playwright index.mjs; no install is performed.
import test from 'node:test';
import assert from 'node:assert/strict';

const base = process.env.DISPLAY_BROWSER_URL;
test('real settings selector: opposing locales, sticky overrides, reset, failed save and discard',
  { skip: !base, timeout: 120000 }, async () => {
    const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
    const browser = await chromium.launch({ headless: true });
    const labels = {
      en: { date: 'Date and time format', save: 'Save', follow: 'Follow UI language (default)', browser: 'Browser format (current device)' },
      'nb-NO': { date: 'Dato- og klokkeslettformat', save: 'Lagre', follow: 'Følg grensesnittspråket (standard)', browser: 'Nettleserformat (denne enheten)' }
    };
    try {
      for (const [ui, locale] of [['en', 'nb-NO'], ['nb-NO', 'en-US']]) {
        const context = await browser.newContext({ locale, timezoneId: 'Europe/Oslo' });
        try {
          const page = await context.newPage();
          const errors = [];
          page.on('pageerror', error => errors.push(error.message));
          assert.ok((await context.request.post(`${base}/__test/reset`)).ok());
          let language = ui;
          async function setLanguage(value) {
            language = value;
            await context.addCookies([{ name: '.AspNetCore.Culture', value: `c=${value}|uic=${value}`, url: base }]);
          }
          async function profile() {
            await page.goto(`${base}/profile`);
            await page.locator(`input[aria-label="${labels[language].date}"]`).waitFor({ state: 'attached' });
            await page.waitForFunction(() => !document.querySelector('.mud-progress-linear'));
          }
          const selector = () => page.locator(`input[aria-label="${labels[language].date}"]`);
          async function choose(text) {
            await selector().locator('..').locator('div[tabindex="0"]').click();
            await page.locator('.mud-popover-open').getByText(text, { exact: true }).click();
          }
          async function evidence() {
            return (await context.request.get(`${base}/__test/evidence`)).json();
          }
          async function save(expected) {
            const count = (await evidence()).submitted.length;
            await page.getByRole('button', { name: labels[language].save, exact: true }).click();
            await page.waitForFunction(async ({ base, count }) =>
              (await (await fetch(`${base}/__test/evidence`)).json()).submitted.length > count, { base, count });
            assert.equal((await evidence()).persisted.dateTimeCulture, expected);
          }
          async function dates(expectedCulture) {
            await page.goto(`${base}/transponders`);
            const row = page.getByRole('row').filter({ hasText: 'Oslo repeated hour' });
            await row.waitFor();
            const text = await row.innerText();
            assert.match(text, expectedCulture === 'nb-NO' ? /25\.10\.2026/ : /10\/25\/2026/);
            assert.match(text, /\+02:00 \[Europe\/Oslo\]/);
            assert.match(text, /\+01:00 \[Europe\/Oslo\]/);
          }
          await setLanguage(ui);
          await profile();
          assert.equal(await selector().inputValue(), '');
          await dates(ui);
          const otherUi = ui === 'en' ? 'nb-NO' : 'en';
          await setLanguage(otherUi);
          await dates(otherUi); // null follows the newly resolved UI language
          await setLanguage(ui);
          await profile();
          for (const [value, text, culture] of [
            ['browser', labels[ui].browser, locale === 'nb-NO' ? 'nb-NO' : 'en'],
            ['en', 'English', 'en'],
            ['nb-NO', 'Norsk bokmål', 'nb-NO']
          ]) {
            await choose(text);
            await save(value);
            await profile(); // persisted selector survives reload
            assert.equal(await selector().inputValue(), value);
            await dates(culture);
            await setLanguage(otherUi);
            await dates(culture); // explicit override remains sticky across UI language changes
            await setLanguage(ui);
            await profile();
          }
          await choose(labels[ui].follow);
          await save(null);
          await dates(ui);

          // Failure/discard behavior uses English labels to keep this shared workflow unambiguous.
          await setLanguage('en');
          await profile();
          await choose('English');
          assert.ok((await context.request.post(`${base}/__test/fail-next/save`)).ok());
          await save(null);
          await page.getByRole('alert').waitFor();
          assert.equal(await selector().inputValue(), 'en'); // draft retained, persisted default unchanged
          await page.getByRole('button', { name: 'Discard changes?', exact: true }).click();
          await page.getByRole('dialog').getByRole('button', { name: 'Discard changes?', exact: true }).click();
          await page.waitForFunction(() =>
            document.querySelector('input[aria-label="Date and time format"]').value === '');
          const final = await evidence();
          assert.deepEqual(final.persisted, {
            distanceUnit: 'm', speedUnit: 'km/h', timeMode: 'browser', timeZoneId: null, dateTimeCulture: null
          });
          assert.deepEqual(final.ownershipSubmissions, []);
          assert.deepEqual(errors, []);
        } finally { await context.close(); }
      }
    } finally { await browser.close(); }
  });
