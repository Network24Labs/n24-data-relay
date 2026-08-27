#!/usr/bin/env bash
# deploy/packaging/build-deb.sh — N24 Data Relay .deb package builder
#
# Usage: ./deploy/packaging/build-deb.sh [version]
#
# If version is omitted, reads <Version> from the .csproj.
# Output: dist/n24-data-relay_<VERSION>_amd64.deb
#
# After building, sign with:
#   ./sign-release.sh ./dist/n24-data-relay_<VERSION>_amd64.deb <VERSION>

set -euo pipefail

# ── Paths ─────────────────────────────────────────────────────────────────────
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CSPROJ="$REPO_ROOT/src/N24DataRelay/N24DataRelay.csproj"
APPSETTINGS="$REPO_ROOT/src/N24DataRelay/appsettings.json"
SYSTEMD_UNIT="$REPO_ROOT/deploy/packaging/n24-data-relay.service"
DIST_DIR="$REPO_ROOT/dist"
STAGING_DIR="$DIST_DIR/build/deb-staging"
PACKAGE_NAME="n24-data-relay"

# ── Version ───────────────────────────────────────────────────────────────────
if [[ $# -ge 1 && -n "$1" ]]; then
    VERSION="$1"
elif [[ -n "${N24_VERSION:-}" ]]; then
    VERSION="$N24_VERSION"
else
    VERSION=$(grep -oP '<Version>\K[^<]+' "$CSPROJ" 2>/dev/null || true)
    if [[ -z "$VERSION" ]]; then
        echo "Warning: Could not read version from .csproj, using 0.0.0"
        VERSION="0.0.0"
    fi
fi

DEB_FILE="$DIST_DIR/${PACKAGE_NAME}_${VERSION}_amd64.deb"

echo "── N24 Data Relay .deb Builder ─────────────────────────────────────────"
echo "  Version : $VERSION"
echo "  Output  : $DEB_FILE"
echo ""

# ── Preflight ─────────────────────────────────────────────────────────────────
for cmd in dotnet dpkg-deb; do
    if ! command -v "$cmd" &>/dev/null; then
        echo "Error: Required tool not found: $cmd"
        exit 1
    fi
done

if [[ ! -f "$CSPROJ" ]]; then
    echo "Error: Project file not found: $CSPROJ"
    exit 1
fi

if [[ ! -f "$APPSETTINGS" ]]; then
    echo "Error: appsettings.json not found: $APPSETTINGS"
    exit 1
fi

if [[ ! -f "$SYSTEMD_UNIT" ]]; then
    echo "Error: systemd unit not found: $SYSTEMD_UNIT"
    exit 1
fi

# ── Clean staging ─────────────────────────────────────────────────────────────
echo "── Cleaning staging directory ──────────────────────────────────────────"
rm -rf "$STAGING_DIR"
mkdir -p "$DIST_DIR"

# ── dotnet publish ────────────────────────────────────────────────────────────
echo "── Publishing (self-contained, linux-x64) ──────────────────────────────"
dotnet publish "$CSPROJ" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "$STAGING_DIR/opt/n24-data-relay"

echo ""

# ── Stage filesystem layout ───────────────────────────────────────────────────
echo "── Staging package layout ──────────────────────────────────────────────"

# systemd unit
mkdir -p "$STAGING_DIR/lib/systemd/system"
cp "$SYSTEMD_UNIT" "$STAGING_DIR/lib/systemd/system/n24-data-relay.service"
echo "  /lib/systemd/system/n24-data-relay.service"

# default config (ships in package; postinst installs to /etc only if missing)
mkdir -p "$STAGING_DIR/etc/n24-data-relay"
cp "$APPSETTINGS" "$STAGING_DIR/etc/n24-data-relay/appsettings.json"
echo "  /etc/n24-data-relay/appsettings.json"

# ── DEBIAN control files ──────────────────────────────────────────────────────
echo "── Writing DEBIAN control files ────────────────────────────────────────"
mkdir -p "$STAGING_DIR/DEBIAN"

# control
cat > "$STAGING_DIR/DEBIAN/control" << EOF
Package: $PACKAGE_NAME
Version: $VERSION
Architecture: amd64
Maintainer: Michael Becker <mike@network24.tv>
Depends: libssl3, libicu70 | libicu72 | libicu74
Description: N24 Data Relay
 Secure file transfer service for DMZ-to-OT network relay.
 Provides a Kestrel web portal for upload management and automated
 transfer via SSH (SCP) or SMB to isolated network segments.
 Supports local account and Entra ID authentication.
EOF
echo "  DEBIAN/control"

# postinst
cat > "$STAGING_DIR/DEBIAN/postinst" << 'POSTINST'
#!/bin/bash
set -e

PKG_CONFIG="/etc/n24-data-relay/appsettings.json"
PKG_DEFAULT="/etc/n24-data-relay/appsettings.json.dpkg-new"
SERVICE_USER="n24-data-relay"

# Create service user/group if not present
if ! id "$SERVICE_USER" &>/dev/null; then
    useradd --system \
            --no-create-home \
            --shell /usr/sbin/nologin \
            --home-dir /var/lib/n24-data-relay \
            "$SERVICE_USER"
    echo "Created system user: $SERVICE_USER"
fi

# Create required directories
install -d -m 755 /etc/n24-data-relay
install -d -m 700 -o "$SERVICE_USER" -g "$SERVICE_USER" /etc/n24-data-relay/keys
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_USER" /var/lib/n24-data-relay
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_USER" /var/lib/n24-data-relay/uploads
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_USER" /var/lib/n24-data-relay/uploads/transfer
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_USER" /var/lib/n24-data-relay/archive
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_USER" /var/lib/n24-data-relay/temp
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_USER" /var/log/n24-data-relay

# Install default config only if not already present (preserves admin edits on upgrade)
if [[ ! -f "$PKG_CONFIG" ]]; then
    cp /etc/n24-data-relay/appsettings.json.dpkg-new "$PKG_CONFIG" 2>/dev/null || \
    cp /etc/n24-data-relay/appsettings.json "$PKG_CONFIG" 2>/dev/null || true
    chown root:"$SERVICE_USER" "$PKG_CONFIG"
    chmod 640 "$PKG_CONFIG"
    echo "Installed default config: $PKG_CONFIG"
    echo "IMPORTANT: Edit $PKG_CONFIG before starting the service."
else
    echo "Existing config preserved: $PKG_CONFIG"
fi

# Set ownership on the binary directory
chown -R root:root /opt/n24-data-relay
chmod 755 /opt/n24-data-relay/N24DataRelay

systemctl daemon-reload

echo ""
echo "N24 Data Relay installed."
echo "  Config : $PKG_CONFIG"
echo "  Enable : systemctl enable n24-data-relay"
echo "  Start  : systemctl start n24-data-relay"
echo ""
POSTINST
chmod 755 "$STAGING_DIR/DEBIAN/postinst"
echo "  DEBIAN/postinst"

# prerm
cat > "$STAGING_DIR/DEBIAN/prerm" << 'PRERM'
#!/bin/bash
set -e

case "$1" in
    remove|upgrade|deconfigure)
        if systemctl is-active --quiet n24-data-relay 2>/dev/null; then
            echo "Stopping n24-data-relay service..."
            systemctl stop n24-data-relay || true
        fi
        if systemctl is-enabled --quiet n24-data-relay 2>/dev/null; then
            systemctl disable n24-data-relay || true
        fi
        ;;
esac
PRERM
chmod 755 "$STAGING_DIR/DEBIAN/prerm"
echo "  DEBIAN/prerm"

# postrm
cat > "$STAGING_DIR/DEBIAN/postrm" << 'POSTRM'
#!/bin/bash
set -e

case "$1" in
    purge)
        # Remove data directories on purge only — preserves data on remove/upgrade
        rm -rf /var/lib/n24-data-relay
        rm -rf /var/log/n24-data-relay
        rm -rf /etc/n24-data-relay
        # Remove service user
        if id n24-data-relay &>/dev/null; then
            userdel n24-data-relay || true
        fi
        echo "N24 Data Relay purged."
        ;;
    remove|upgrade|failed-upgrade|abort-install|abort-upgrade|disappear)
        # Do not remove data or config on regular remove/upgrade
        ;;
esac

systemctl daemon-reload || true
POSTRM
chmod 755 "$STAGING_DIR/DEBIAN/postrm"
echo "  DEBIAN/postrm"

# ── Build .deb ────────────────────────────────────────────────────────────────
echo ""
echo "── Building .deb ───────────────────────────────────────────────────────"
dpkg-deb --root-owner-group -b "$STAGING_DIR" "$DEB_FILE"

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "── Done ────────────────────────────────────────────────────────────────"
DEB_SIZE=$(du -sh "$DEB_FILE" | cut -f1)
echo "  Package : $DEB_FILE ($DEB_SIZE)"
echo ""
echo "  To sign and release:"
echo "    ./sign-release.sh $DEB_FILE $VERSION"
echo ""
