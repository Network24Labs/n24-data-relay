# Architecture

High-level architecture of N24 Data Relay on Ubuntu Linux.

## Single process (Phase 1)

The application runs as **one process** and **one systemd unit**:

- **Host** — The `N24DataRelay` project is the single entry point. It builds a `WebApplication` (Kestrel), registers the **Watcher** as an `IHostedService`, and configures the **WebApp** (routes, auth, upload) via extension methods from the WebApp and Watcher libraries.
- **WebApp** (library) — Upload UI, authentication (Entra ID + local accounts), and admin/settings. Exposes `AddWebAppServices()` and `UseWebApp()`. Replaces the Windows ConfigTool.
- **Watcher** (library) — File watch and transfer (SSH/SCP or SMB). Exposes `AddWatcherServices()`. Runs in the same process as the web app as a background hosted service.
- **Config** — Single shared `appsettings.json` (or equivalent) under Linux FHS/XDG paths. The host loads it once; both WebApp and Watcher use the same `IConfiguration`.

One executable, one systemd unit (`n24-data-relay.service`); no separate watcher or web process.

## Linux paths (FHS / XDG)

- **Config:** `/etc/n24-data-relay/` or `$XDG_CONFIG_HOME/n24-data-relay/` (to be defined in implementation).
- **Data / state:** `/var/lib/n24-data-relay/` (uploads, transfer dirs, SQLite, etc.).
- **Logs:** `/var/log/n24-data-relay/` or under data dir.

Exact defaults will be defined in Core and documented in [CONFIGURATION.md](CONFIGURATION.md).

## Service model

- **systemd** — One unit file, e.g. `n24-data-relay.service`, runs the host executable. Lifecycle: `systemctl start n24-data-relay`, `systemctl stop n24-data-relay`, etc.

## Data flow

1. Users upload files via the WebApp (optionally into a “transfer” directory).
2. The Watcher (same process) monitors the configured watch directory; when files appear, it queues and transfers them via SSH/SCP or SMB to the target (e.g. SCADA file server).
3. Config is read once by the host; admin changes (via WebApp or manual edit) apply after restart or when reload is implemented in later phases.
