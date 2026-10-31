// Browser identity only. Calendar boundaries and DST validation use the shared .NET presenter.
export function browserPresentation() {
    return { timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        locale: navigator.language || Intl.DateTimeFormat().resolvedOptions().locale };
}
