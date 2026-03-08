# N24 Data Relay

**Network24 Data Relay** — Secure, automated file transfer for DMZ to SCADA (or similar) networks. Linux-first web app and watcher service.

**Source:** [github.com/Network24Labs/n24-data-relay](https://github.com/Network24Labs/n24-data-relay)

## Overview

N24 Data Relay is the Linux-focused evolution of the file relay concept: a web upload portal and a background watcher service that transfer files via SSH/SCP or SMB. Configuration and setup are done via the web app and config file; no Windows dependency.

See [docs/](docs/) for architecture, setup, configuration, and migration from ZL File Relay.

**Status:** Ready for release — `.deb` packaging and YubiKey-signed builds in place.

## Quick Start (Development)

```bash
# Start the fake-OT SSH test target (Docker required)
./start-fake-ot.sh start

# Run the app (uses appsettings.Development.json pointing at fake-OT on :2222)
cd src/N24DataRelay
dotnet run
# Open http://localhost:5000 — register the first account (auto-approved, Admin role)
```

## Feature Summary

| Feature | Status |
|---|---|
| SSH/SCP file transfer (SSH.NET) | Done |
| SMB transfer (mount-point copy) | Done |
| Web upload portal (Razor Pages + Bootstrap) | Done |
| Real-time transfer status (SignalR) | Done |
| Local account auth (ASP.NET Core Identity) | Done |
| Password expiry + forced reset + email reset | Done |
| Entra ID (Azure AD) OIDC auth | Done |
| Managed Identity (Azure Arc) option | Done |
| User approval workflow + Admin UI | Done |
| Transfer record persistence (SQLite) | Done |
| Transfer metrics: throughput, retries, verify | Done |
| Audit log (SQLite) — logins, config changes, etc. | Done |
| Admin Dashboard (stats, audit feed, service health) | Done |
| Monitoring API (`/api/v1/transfers`, `/api/v1/audit`, `/api/v1/health`) | Done |
| Transfer failure email notifications | Done |
| Config hot-reload (IOptionsMonitor) | Done |
| SMTP email (MailKit) with test-send UI | Done |
| systemd sd-notify integration | Done |
| Fake-OT Docker test target | Done |
| `.deb` packaging (signed releases) | Done |

## Monitoring API

The `/api/v1` endpoints allow external tools (LogScale, Grafana, custom scripts) to poll for data without parsing log files.

| Endpoint | Auth | Description |
|---|---|---|
| `GET /api/v1/health` | None | Service heartbeat, queue depth, last transfer time |
| `GET /api/v1/transfers?since=<ISO8601>&limit=500` | Bearer API key | Transfer records with metrics |
| `GET /api/v1/audit?since=<ISO8601>&limit=500` | Bearer API key | Audit events |

Configure the API key in **Admin → Settings → Portal → Monitoring API Key** or via the `N24DataRelay__WebPortal__Authentication__ApiKey` environment variable.

## Releases and verification

Production installs use the signed `.deb` package. See [deploy/packaging/packaging-README.md](deploy/packaging/packaging-README.md) for build and install instructions.

The **public signing certificate** used to verify release signatures is in the repository root:

- **[n24-data-relay-signing.pem](https://github.com/Network24Labs/n24-data-relay/blob/main/n24-data-relay-signing.pem)** — SSL.com Code Signing (ECC P-384). Use this cert to verify the `.sig` file for any release (checksum signed with the corresponding private key on a YubiKey). Raw: `https://raw.githubusercontent.com/Network24Labs/n24-data-relay/main/n24-data-relay-signing.pem`

Each release is published with a `.deb`, `.sha256`, `.sig`, and a `VERIFY.txt` that gives step-by-step verification commands. You need this public cert (or the public key derived from it) to verify the signature.
