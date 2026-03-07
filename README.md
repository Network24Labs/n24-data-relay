# N24 Data Relay

**Network24 Data Relay** — Secure, automated file transfer for DMZ to SCADA (or similar) networks. Linux-first web app and watcher service.

## Overview

N24 Data Relay is the Linux-focused evolution of the file relay concept: a web upload portal and a background watcher service that transfer files via SSH/SCP or SMB. Configuration and setup are done via the web app and config file; no Windows dependency.

See [docs/](docs/) for architecture, setup, configuration, and migration from ZL File Relay.

**Status:** Phase 2 — production-ready feature set. Core transfer pipeline, web portal, admin UI, transfer status persistence, systemd integration, and Entra ID authentication are fully implemented.

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
| Web upload portal (Razor Pages + Bootstrap) | Done |
| Real-time transfer status (SignalR) | Done |
| Local account auth (ASP.NET Core Identity) | Done |
| Entra ID (Azure AD) OIDC auth | Done |
| User approval workflow + Admin UI | Done |
| Transfer record persistence (SQLite) | Done |
| systemd sd-notify integration | Done |
| Fake-OT Docker test target | Done |
| SMB transfer | Planned (Phase 3) |
| `.deb` packaging | Planned (Phase 3) |
| Config hot-reload | Planned (Phase 3) |
