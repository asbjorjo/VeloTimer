function parseDate(value) {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value ?? ""))
        throw new Error("Enter a valid registration date.");
    const [year, month, day] = value.split("-").map(Number);
    const date = new Date(0);
    date.setFullYear(year, month - 1, day);
    date.setHours(0, 0, 0, 0);
    if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day)
        throw new Error("Enter a valid registration date.");
    return date;
}

function calendarDate(date) {
    return `${String(date.getFullYear()).padStart(4, "0")}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

export function today() {
    return calendarDate(new Date());
}

export function period(start, end) {
    const from = parseDate(start);
    let to = null;
    if (end) {
        to = parseDate(end);
        if (to < from)
            throw new Error("Registration end date cannot be before its start date.");
        to.setDate(to.getDate() + 1);
        to.setHours(0, 0, 0, 0);
    }
    return { ownedFrom: from.toISOString(), ownedTo: to?.toISOString() ?? null };
}

export function describe(start, end) {
    const from = new Date(start);
    if (!Number.isFinite(from.getTime()))
        throw new Error("The existing registration start is invalid.");
    if (!end)
        return `${from.toLocaleString()} onwards (no expiry)`;
    const to = new Date(end);
    if (!Number.isFinite(to.getTime()))
        throw new Error("The existing registration end is invalid.");
    // Ownership ends exclusively; show the last included instant in local time.
    return `${from.toLocaleString()} through ${new Date(to.getTime() - 1).toLocaleString()}`;
}
