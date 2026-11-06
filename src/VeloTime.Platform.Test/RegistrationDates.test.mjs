import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
// Calendar/DST coverage now lives in DisplayPresentationTests for the shared .NET presenter.
const source = await readFile(new URL("../WebUI.Mud.Client/wwwroot/registrationDates.js", import.meta.url), "utf8");
const module = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
test("exports only browser identity", () => assert.deepEqual(Object.keys(module), ["browserPresentation"]));
for (const [zone, language, expected] of [["Europe/Oslo", "nb-NO", "nb-NO"], ["America/New_York", "en-US", "en-US"], ["Pacific/Auckland", "", "en-NZ"]]) {
    test(`browser identity ${zone} is independent of process timezone`, () => {
        const original = Intl.DateTimeFormat;
        const descriptor = Object.getOwnPropertyDescriptor(globalThis, "navigator");
        try {
            Intl.DateTimeFormat = () => ({ resolvedOptions: () => ({ timeZone: zone, locale: "en-NZ" }) });
            Object.defineProperty(globalThis, "navigator", { configurable: true, value: { language } });
            assert.deepEqual(module.browserPresentation(), { timeZone: zone, locale: expected });
        } finally {
            Intl.DateTimeFormat = original;
            if (descriptor) Object.defineProperty(globalThis, "navigator", descriptor);
            else delete globalThis.navigator;
        }
    });
}
