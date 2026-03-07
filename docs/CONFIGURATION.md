# Configuration (stub)

Config file shape for N24 Data Relay, Linux path defaults, and environment variables.

*(To be filled in during Core and implementation phases.)*

## Planned content

- **Config section:** Root key `N24DataRelay` in `appsettings.json` (replacing `ZLFileRelay`).
- **Linux path defaults:** Default values for config dir, data dir, log dir, upload/transfer/archive paths using `/etc/n24-data-relay`, `/var/lib/n24-data-relay`, `/var/log/n24-data-relay` (or XDG where appropriate).
- **Environment variables:** Any env vars used to override config or paths (e.g. config file path, data dir).
