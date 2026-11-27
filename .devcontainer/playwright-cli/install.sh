#!/bin/sh
set -eu

VERSION="${VERSION:-latest}"
INSTALL_SKILLS="${INSTALLSKILLS:-true}"

# ---------------------------------------------------------------------------
# Package manager helpers
# ---------------------------------------------------------------------------

detect_package_manager() {
    for pm in apt-get apk dnf yum; do
        if command -v "$pm" >/dev/null 2>&1; then
            case "$pm" in
                apt-get) echo "apt" ;;
                *) echo "$pm" ;;
            esac
            return 0
        fi
    done
    echo "ERROR: No supported package manager found (apt-get, apk, dnf, yum)." >&2
    return 1
}

install_packages() {
    pkg_manager="$1"; shift
    case "$pkg_manager" in
        apt)     apt-get update -y && apt-get install -y "$@" ;;
        apk)     apk add --no-cache "$@" ;;
        dnf|yum) "$pkg_manager" install -y "$@" ;;
        *)       echo "ERROR: Unsupported package manager: $pkg_manager" >&2; return 1 ;;
    esac
}

# ---------------------------------------------------------------------------
# Node.js / npm
# ---------------------------------------------------------------------------

# Playwright CLI requires Node.js >= 18.
ensure_node_npm() {
    pkg_manager="$1"

    # Check whether an acceptable Node.js is already present.
    node_ok=0
    if command -v node >/dev/null 2>&1; then
        node_major=$(node --version 2>/dev/null | sed 's/v//' | cut -d. -f1)
        if [ "${node_major:-0}" -ge 18 ] 2>/dev/null; then
            node_ok=1
        fi
    fi

    if [ "$node_ok" = "1" ]; then
        if command -v npm >/dev/null 2>&1; then
            echo "Node.js $(node --version) and npm $(npm --version) already available — skipping installation."
            return 0
        fi
        # Node.js is present and new enough, but npm is missing.
        echo "Node.js $(node --version) found but npm missing — installing npm separately..."
        case "$pkg_manager" in
            apt)     install_packages apt npm ;;
            apk)     install_packages apk npm ;;
            dnf|yum) install_packages "$pkg_manager" npm ;;
            *)       echo "ERROR: Cannot install npm: unsupported package manager." >&2; return 1 ;;
        esac
        echo "Node.js $(node --version) and npm $(npm --version) installed."
        return 0
    fi

    # Node.js is absent or too old — install a fresh LTS release.
    echo "Node.js not found or too old (need >= 18). Installing Node.js 20 LTS..."

    case "$pkg_manager" in
        apt)
            # Use the NodeSource setup script which handles GPG, repo, and
            # apt pinning so that NodeSource's nodejs (bundled with npm) is
            # preferred over any distro-provided nodejs package.
            install_packages apt ca-certificates curl
            curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
            install_packages apt nodejs
            ;;
        apk)
            # Alpine 3.17+ ships Node.js 18+.
            install_packages apk nodejs npm
            ;;
        dnf|yum)
            install_packages "$pkg_manager" nodejs npm
            ;;
        *)
            echo "ERROR: Cannot install Node.js: no supported package manager." >&2
            return 1
            ;;
    esac

    echo "Node.js $(node --version) and npm $(npm --version) installed."
}

# ---------------------------------------------------------------------------
# Playwright CLI
# ---------------------------------------------------------------------------

install_playwright_cli() {
    version="$1"

    if [ "$version" = "latest" ]; then
        pkg="@playwright/cli"
    else
        pkg="@playwright/cli@${version}"
    fi

    echo "Installing Playwright CLI: $pkg"
    npm install -g "$pkg"

    if ! command -v playwright-cli >/dev/null 2>&1; then
        echo "ERROR: 'playwright-cli' command not found after installation." >&2
        return 1
    fi

    echo "Playwright CLI installed: $(playwright-cli --version 2>/dev/null || echo '(version check unavailable)')"
}

# ---------------------------------------------------------------------------
# Playwright skills
# ---------------------------------------------------------------------------

install_playwright_skills() {
    echo "Installing Playwright skills..."
    # Run as the target user if possible so skills are installed in the right home.
    if [ -n "${_REMOTE_USER:-}" ] && [ "${_REMOTE_USER}" != "root" ]; then
        su - "$_REMOTE_USER" -c "playwright-cli install --skills" || \
            playwright-cli install --skills
    else
        playwright-cli install --skills
    fi
    echo "Playwright skills installed."
}

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

main() {
    echo "Activating feature 'playwright-cli' (version: ${VERSION}, installSkills: ${INSTALL_SKILLS})"
    PKG_MANAGER=$(detect_package_manager)
    ensure_node_npm "$PKG_MANAGER"
    install_playwright_cli "$VERSION"

    if [ "$INSTALL_SKILLS" = "true" ]; then
        install_playwright_skills
    fi

    echo "Feature 'playwright-cli' installation complete."
}

main