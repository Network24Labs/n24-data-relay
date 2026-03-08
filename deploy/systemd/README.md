# systemd units

Single service (Phase 1):

- **n24-data-relay.service** — One process running both the web app (Kestrel) and the file watcher/transfer logic. Copy to `/etc/systemd/system/`, set `WorkingDirectory` and `ExecStart` to your install path, then `systemctl daemon-reload && systemctl enable --now n24-data-relay`.

## Post-install security hardening

After installing and before starting the service for the first time, apply the following permissions to protect credentials stored in the config file:

```bash
# Restrict config file to service user only (root-owned, readable by service group)
sudo chown root:n24-data-relay /etc/n24-data-relay/appsettings.json
sudo chmod 640 /etc/n24-data-relay/appsettings.json

# Restrict the entire config directory
sudo chown root:n24-data-relay /etc/n24-data-relay
sudo chmod 750 /etc/n24-data-relay

# Restrict data protection keys directory (created automatically on first start)
sudo chown -R n24-data-relay:n24-data-relay /etc/n24-data-relay/keys
sudo chmod 700 /etc/n24-data-relay/keys
```

The application will log a `WARNING` at startup if `appsettings.json` is world-readable.

## Credential best practices

| Credential | Recommended method |
|---|---|
| SSH password | `N24_SSH_KEY_PASSPHRASE` environment variable (set in the service drop-in) |
| SMTP password | `N24DataRelay__Smtp__Password` environment variable |
| Monitoring API key | `N24DataRelay__WebPortal__Authentication__ApiKey` environment variable |
| Entra ID client secret | `AzureAd__ClientSecret` environment variable, or use Managed Identity |

If credentials are set via the admin UI, they are **encrypted using the application data-protection key** (stored in `/etc/n24-data-relay/keys/`) before being written to `appsettings.json`.
The encrypted values are machine/app-bound — a config file copied to another host will not decrypt without the corresponding keys.
