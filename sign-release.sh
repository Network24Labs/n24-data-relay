#!/usr/bin/env bash
# sign-release.sh — N24 Data Relay release signing script
# Signs a .deb artifact using YubiKey PIV slot 9a via PKCS#11
# Produces: .deb, .sha256, .sig, and a verification instructions file
#
# Usage: ./sign-release.sh <path-to.deb> [version]
# Example: ./sign-release.sh ./dist/n24-data-relay_1.0.0_amd64.deb 1.0.0

set -euo pipefail

# ── Configuration ────────────────────────────────────────────────────────────
PKCS11_MODULE="/usr/lib/x86_64-linux-gnu/libykcs11.so"
YUBIKEY_SLOT="01"                          # Slot 9a = ID 01 in libykcs11
SIGNING_CERT="/tmp/n24_signing_cert.pem"   # Exported from YubiKey at runtime
APP_NAME="n24-data-relay"
PUBLISHER="Michael Becker"
CERT_SHA2_FPR="B6:6E:29:26:C1:6D:DE:53:8D:97:60:68:9E:AF:AB:C1:19:5D:26:27:D0:F6:E0:CA:07:C3:D3:AA:7F:04:5C:30"

# ── Argument handling ─────────────────────────────────────────────────────────
if [[ $# -lt 1 ]]; then
    echo "Usage: $0 <path-to.deb> [version]"
    exit 1
fi

DEB_FILE="$1"
VERSION="${2:-$(date +%Y%m%d)}"

if [[ ! -f "$DEB_FILE" ]]; then
    echo "Error: File not found: $DEB_FILE"
    exit 1
fi

if [[ "$DEB_FILE" != *.deb ]]; then
    echo "Error: Expected a .deb file"
    exit 1
fi

DEB_DIR="$(dirname "$DEB_FILE")"
DEB_BASE="$(basename "$DEB_FILE" .deb)"
CHECKSUM_FILE="${DEB_DIR}/${DEB_BASE}.sha256"
SIG_FILE="${DEB_DIR}/${DEB_BASE}.sig"
VERIFY_FILE="${DEB_DIR}/VERIFY.txt"

# ── Preflight checks ──────────────────────────────────────────────────────────
echo "── N24 Data Relay Release Signing ──────────────────────────────────────"
echo "  Package : $DEB_FILE"
echo "  Version : $VERSION"
echo ""

# Check dependencies
for cmd in pkcs11-tool openssl sha256sum ykman; do
    if ! command -v "$cmd" &>/dev/null; then
        echo "Error: Required tool not found: $cmd"
        exit 1
    fi
done

# Check pcscd is running
if ! systemctl is-active --quiet pcscd; then
    echo "Error: pcscd is not running. Start it with: sudo systemctl start pcscd"
    exit 1
fi

# Check YubiKey is present
if ! ykman info &>/dev/null; then
    echo "Error: YubiKey not detected. Insert your YubiKey and try again."
    exit 1
fi

echo "  YubiKey : detected"

# ── Export signing cert from YubiKey ─────────────────────────────────────────
echo "  Cert    : exporting from slot 9a..."
ykman piv certificates export 9a "$SIGNING_CERT"

# Verify it's the right cert
CERT_SUBJECT=$(openssl x509 -in "$SIGNING_CERT" -subject -noout)
echo "  Cert    : $CERT_SUBJECT"

# ── Generate SHA256 checksum ──────────────────────────────────────────────────
echo ""
echo "── Generating checksum ─────────────────────────────────────────────────"
sha256sum "$DEB_FILE" > "$CHECKSUM_FILE"
CHECKSUM=$(cat "$CHECKSUM_FILE" | awk '{print $1}')
echo "  SHA256  : $CHECKSUM"
echo "  Written : $CHECKSUM_FILE"

# ── Sign the checksum file with YubiKey ──────────────────────────────────────
echo ""
echo "── Signing with YubiKey PIV (slot 9a) ──────────────────────────────────"
echo "  You will be prompted for your PIV PIN."
echo ""

pkcs11-tool \
    --module "$PKCS11_MODULE" \
    --sign \
    --slot 0 \
    --id "$YUBIKEY_SLOT" \
    --mechanism ECDSA-SHA384 \
    --signature-format openssl \
    --input-file "$CHECKSUM_FILE" \
    --output-file "$SIG_FILE" \
    --login

echo ""
echo "  Signature written: $SIG_FILE"

# ── Verify the signature immediately ─────────────────────────────────────────
echo ""
echo "── Verifying signature ─────────────────────────────────────────────────"
openssl x509 -in "$SIGNING_CERT" -pubkey -noout > /tmp/n24_pubkey.pem

if openssl dgst -sha384 -verify /tmp/n24_pubkey.pem \
    -signature "$SIG_FILE" "$CHECKSUM_FILE" 2>/dev/null; then
    echo "  Result  : Signature VERIFIED OK"
else
    echo "  Result  : ERROR — signature verification failed"
    echo "  The .sig file may be corrupt. Do not publish this release."
    rm -f "$SIG_FILE"
    exit 1
fi

# ── Write verification instructions ──────────────────────────────────────────
cat > "$VERIFY_FILE" << EOF
N24 Data Relay — Release Verification Instructions
===================================================
Version   : $VERSION
Package   : ${DEB_BASE}.deb
Publisher : $PUBLISHER
Signed    : $(date -u '+%Y-%m-%d %H:%M:%S UTC')

Certificate
-----------
Issued by : SSL.com Code Signing (ECC P-384)
SHA2 Fingerprint:
  $CERT_SHA2_FPR

To verify this release you need: openssl, sha256sum

Step 1 — Verify the package checksum
--------------------------------------
sha256sum -c ${DEB_BASE}.sha256

Expected output: ${DEB_BASE}.deb: OK

Step 2 — Verify the signature
-------------------------------
# Get the signing certificate from the N24 Data Relay repository:
#   https://raw.githubusercontent.com/Network24Labs/n24-data-relay/main/n24-data-relay-signing.pem
#   SHA2 fingerprint above must match

# Extract the public key from the cert:
openssl x509 -in n24-data-relay-signing.pem -pubkey -noout > pubkey.pem

# Verify the signature:
openssl dgst -sha384 -verify pubkey.pem \\
    -signature ${DEB_BASE}.sig \\
    ${DEB_BASE}.sha256

Expected output: Verified OK

EOF

echo ""
echo "── Release artifacts ───────────────────────────────────────────────────"
echo "  $(ls -lh "$DEB_FILE" | awk '{print $5, $9}')"
echo "  $(ls -lh "$CHECKSUM_FILE" | awk '{print $5, $9}')"
echo "  $(ls -lh "$SIG_FILE" | awk '{print $5, $9}')"
echo "  $(ls -lh "$VERIFY_FILE" | awk '{print $5, $9}')"
echo ""
echo "── Done ────────────────────────────────────────────────────────────────"
echo "  Publish all four files together with your release."
echo "  Also publish your signing cert PEM so users can verify."
echo ""

# Cleanup temp files
rm -f /tmp/n24_pubkey.pem "$SIGNING_CERT"