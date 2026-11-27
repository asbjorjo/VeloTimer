# Playwright CLI

Installs [Playwright CLI](https://github.com/microsoft/playwright-cli) (`@playwright/cli`) globally via npm.

Playwright CLI provides a token-efficient CLI interface for browser automation, designed for coding agents such as Claude Code and GitHub Copilot.

## Usage

```json
"features": {
    "ghcr.io/cjcrobin/devcontainer-features/playwright-cli:1": {}
}
```

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `version` | string | `latest` | Version of `@playwright/cli` to install (e.g. `latest`, `0.1.17`). |
| `installSkills` | boolean | `true` | Run `playwright-cli install --skills` after installation to install Playwright skills for coding agents. |

## Requirements

- Node.js 18 or newer (installed automatically if not present)

## Supported Distributions

| Distribution | Package Manager |
|---|---|
| Ubuntu / Debian | `apt-get` + NodeSource |
| Alpine Linux | `apk` |
| Fedora / RHEL / CentOS | `dnf` / `yum` |

## Example: Pin a specific version

```json
"features": {
    "ghcr.io/cjcrobin/devcontainer-features/playwright-cli:1": {
        "version": "0.1.17",
        "installSkills": false
    }
}
```

## Notes

- If Node.js >= 18 is already present in the image, the feature will skip Node installation.
- Playwright skills are installed at feature-build time so that coding agents can discover and use them immediately.
- See [playwright-cli documentation](https://github.com/microsoft/playwright-cli) for available commands.