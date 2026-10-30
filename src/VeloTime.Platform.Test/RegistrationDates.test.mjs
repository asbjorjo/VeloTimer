import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("../WebUI.Mud.Client/wwwroot/registrationDates.js", import.meta.url), "utf8");
const dates = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);

function inTimeZone(zone, action) {
    const previous = process.env.TZ;
    process.env.TZ = zone;
    try {
        action();
    } finally {
        if (previous === undefined) delete process.env.TZ;
        else process.env.TZ = previous;
    }
}

for (const zone of ["UTC", "Europe/Oslo", "America/Los_Angeles", "Asia/Kathmandu"]) {
    test(`today uses the user's calendar date in ${zone}`, () => inTimeZone(zone, () => {
        const now = new Date();
        const expected = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
        assert.equal(dates.today(), expected);
    }));
}

test("today is browser-local even when the UTC calendar day differs", () => {
    const NativeDate = Date;
    globalThis.Date = class extends NativeDate {
        constructor(...args) {
            super(...(args.length ? args : ["2026-03-29T00:30:00Z"]));
        }
    };
    try {
        inTimeZone("America/Los_Angeles", () => assert.equal(dates.today(), "2026-03-28"));
        inTimeZone("Europe/Oslo", () => assert.equal(dates.today(), "2026-03-29"));
    } finally {
        globalThis.Date = NativeDate;
    }
});

for (const [zone, day, from, to, hours] of [
    ["UTC", "2026-03-29", "2026-03-29T00:00:00.000Z", "2026-03-30T00:00:00.000Z", 24],
    ["Europe/Oslo", "2026-03-29", "2026-03-28T23:00:00.000Z", "2026-03-29T22:00:00.000Z", 23],
    ["Europe/Oslo", "2026-10-25", "2026-10-24T22:00:00.000Z", "2026-10-25T23:00:00.000Z", 25],
    ["America/Los_Angeles", "2026-03-08", "2026-03-08T08:00:00.000Z", "2026-03-09T07:00:00.000Z", 23],
    ["America/Los_Angeles", "2026-11-01", "2026-11-01T07:00:00.000Z", "2026-11-02T08:00:00.000Z", 25],
    ["Asia/Kathmandu", "2026-03-29", "2026-03-28T18:15:00.000Z", "2026-03-29T18:15:00.000Z", 24]
]) {
    test(`same-day inclusive registration in ${zone} on ${day}`, () => inTimeZone(zone, () => {
        const period = dates.period(day, day);
        assert.deepEqual(period, { ownedFrom: from, ownedTo: to });
        assert.equal((new Date(period.ownedTo) - new Date(period.ownedFrom)) / 3600000, hours);
    }));
}

test("blank end means no expiry", () => inTimeZone("Europe/Oslo", () => {
    for (const end of [null, undefined, ""])
        assert.deepEqual(dates.period("2026-03-29", end), { ownedFrom: "2026-03-28T23:00:00.000Z", ownedTo: null });
}));

test("invalid or reversed calendar dates are rejected", () => {
    for (const start of [null, undefined, "", "2026-02-30", "2026-13-01", "29/03/2026"])
        assert.throws(() => dates.period(start, null), /valid registration date/);
    assert.throws(() => dates.period("2026-03-29", "2026-02-30"), /valid registration date/);
    assert.throws(() => dates.period("2026-03-29", "2026-03-28"), /cannot be before/);
});

test("confirmation formats inclusive local coverage in the selected locale", () => inTimeZone("Europe/Oslo", () => {
    const start = "2026-03-28T23:00:00.000Z";
    const end = "2026-03-29T22:00:00.000Z";
    const options = { dateStyle: "short", timeStyle: "medium" };
    const norwegian = new Intl.DateTimeFormat("nb-NO", options);
    const described = dates.describe(start, end, "nb-NO");
    assert.deepEqual(described, {
        from: norwegian.format(new Date(start)),
        through: norwegian.format(new Date(new Date(end).getTime() - 1))
    });

    const english = dates.describe(start, end, "en-US");
    assert.equal(english.from, new Intl.DateTimeFormat("en-US", options).format(new Date(start)));
    assert.notEqual(english.from, described.from);

    assert.deepEqual(dates.describe(start, null, "nb-NO"), {
        from: norwegian.format(new Date(start)),
        through: null
    });
    assert.throws(() => dates.describe("invalid", null, "nb-NO"), /start is invalid/);
    assert.throws(() => dates.describe(start, "invalid", "nb-NO"), /end is invalid/);
}));
