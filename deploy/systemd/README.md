# systemd units

Single service (Phase 1):

- **n24-data-relay.service** — One process running both the web app (Kestrel) and the file watcher/transfer logic. Copy to `/etc/systemd/system/`, set `WorkingDirectory` and `ExecStart` to your install path, then `systemctl daemon-reload && systemctl enable --now n24-data-relay`.
