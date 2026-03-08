# N24 Data Relay — Debian Packaging

## Overview

The packaging scripts in this directory produce a self-contained `.deb` for
distribution on Debian/Ubuntu-based Linux systems. The package bundles the
.NET 8 runtime — no `dotnet` installation is required on the target host.

## Prerequisites

Build machine requires:
- .NET 8 SDK (`dotnet`)
- `dpkg-deb` (standard on Debian/Ubuntu)
- `pkcs11-tool`, `ykman`, `openssl` (for signing — see below)

## Build

From the repository root:

```bash
./deploy/packaging/build-deb.sh
```

This reads the version from `src/N24DataRelay/N24DataRelay.csproj` automatically.

To specify a version explicitly:

```bash
./deploy/packaging/build-deb.sh 1.0.0
```

Output: `dist/n24-data-relay_<VERSION>_amd64.deb`

## Sign

After building, sign the release with your YubiKey (insert key before running):

```bash
./sign-release.sh ./dist/n24-data-relay_<VERSION>_amd64.deb <VERSION>
```

This produces four files in `dist/`:
- `n24-data-relay_<VERSION>_amd64.deb` — the package
- `n24-data-relay_<VERSION>_amd64.sha256` — SHA256 checksum
- `n24-data-relay_<VERSION>_amd64.sig` — YubiKey signature over the checksum
- `VERIFY.txt` — verification instructions for sysadmins

Publish all four files together with each release.

The **public signing certificate** for verifying signatures is in the [repository](https://github.com/Network24Labs/n24-data-relay) root: [n24-data-relay-signing.pem](../../n24-data-relay-signing.pem). The `VERIFY.txt` included with each release has the exact commands; use that cert (or the public key extracted from it) to verify the `.sig` file.

## Full release workflow

```bash
./deploy/packaging/build-deb.sh
./sign-release.sh ./dist/n24-data-relay_<VERSION>_amd64.deb <VERSION>
```

## Package layout

| Installed path | Contents |
|---|---|
| `/opt/n24-data-relay/` | Application binary + bundled .NET 8 runtime |
| `/etc/n24-data-relay/appsettings.json` | Configuration (preserved on upgrade) |
| `/var/lib/n24-data-relay/` | Data directories (uploads, archive, temp) |
| `/var/log/n24-data-relay/` | Log files |
| `/lib/systemd/system/n24-data-relay.service` | systemd unit |

## Configuration

On first install, the default `appsettings.json` is copied to
`/etc/n24-data-relay/appsettings.json`. Edit this file before starting
the service — at minimum configure:

- `N24DataRelay.Transfer.Ssh` or `Smb` — destination host and credentials
- `N24DataRelay.WebPortal.Authentication.ApiKey` — set a strong random value
- `N24DataRelay.WebPortal.Kestrel` — ports and HTTPS if required
- `AzureAd` — if using Entra ID authentication

On upgrade, the existing config is **never overwritten**.

## Service management

```bash
sudo systemctl enable n24-data-relay   # enable on boot
sudo systemctl start n24-data-relay    # start now
sudo systemctl status n24-data-relay   # check status
journalctl -u n24-data-relay -f        # follow logs
```

## Uninstall

```bash
sudo apt remove n24-data-relay         # removes package, preserves config and data
sudo apt purge n24-data-relay          # removes everything including config and data
```

## Verifying a release

Before installing a downloaded `.deb`, verify the checksum and signature:

1. **Checksum:** `sha256sum -c n24-data-relay_<VERSION>_amd64.sha256`
2. **Signature:** Use the public cert from the [repo](https://github.com/Network24Labs/n24-data-relay) ([n24-data-relay-signing.pem](https://raw.githubusercontent.com/Network24Labs/n24-data-relay/main/n24-data-relay-signing.pem)) and the steps in the release’s `VERIFY.txt`.

Certificate SHA2 fingerprint (confirm you have the right cert):  
`B6:6E:29:26:C1:6D:DE:53:8D:97:60:68:9E:AF:AB:C1:19:5D:26:27:D0:F6:E0:CA:07:C3:D3:AA:7F:04:5C:30`

## Notes

- The service runs as the `n24-data-relay` system user created on install
- Data directories under `/var/lib/n24-data-relay/` are **not removed** on
  `apt remove` — only on `apt purge`
- The systemd unit sets `ProtectSystem=strict` — the service can only write
  to its designated paths
- Built artifacts (`dist/`) are excluded from version control via `.gitignore`
