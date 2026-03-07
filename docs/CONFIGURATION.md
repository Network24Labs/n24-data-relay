# Configuration Reference

All settings live under the `N24DataRelay` key in `appsettings.json`.
On Linux the app loads config from (in order, later values override earlier):
1. `appsettings.json` in the binary directory
2. `/etc/n24-data-relay/appsettings.json` (or `$N24_DATA_RELAY_CONFIG_DIR/appsettings.json`)
3. `appsettings.{Environment}.json` in the binary directory (Development only)

## Branding

```json
"Branding": {
  "CompanyName": "Network24",
  "ProductName": "N24 Data Relay",
  "SiteName": "Your Site Name",
  "SupportEmail": "support@example.com",
  "Theme": {
    "PrimaryColor": "#0066CC",
    "SecondaryColor": "#003366",
    "AccentColor":   "#FF6600"
  }
}
```

## Paths

```json
"Paths": {
  "UploadDirectory":   "/var/lib/n24-data-relay/uploads",
  "TransferDirectory": "/var/lib/n24-data-relay/uploads/transfer",
  "LogDirectory":      "/var/log/n24-data-relay",
  "ConfigDirectory":   "/etc/n24-data-relay",
  "TempDirectory":     "/var/lib/n24-data-relay/temp"
}
```

## Service (Watcher)

| Key | Default | Description |
|---|---|---|
| `Enabled` | `true` | Enable/disable the file watcher |
| `WatchDirectory` | `/var/lib/n24-data-relay/uploads/transfer` | Directory watched for new files |
| `TransferMethod` | `"ssh"` | Transfer method: `"ssh"` or `"smb"` |
| `RetryAttempts` | `3` | Number of retry attempts on transfer failure |
| `RetryDelaySeconds` | `30` | Initial delay between retries |
| `RetryBackoffMultiplier` | `2.0` | Exponential backoff multiplier |
| `MaxConcurrentTransfers` | `5` | Max simultaneous transfers |
| `FileFilter` | `"*.*"` | File glob filter |
| `ArchiveAfterTransfer` | `true` | Move files to `ArchiveDirectory` after success |
| `ArchiveDirectory` | `/var/lib/n24-data-relay/archive` | Archive destination |
| `DeleteAfterTransfer` | `false` | Delete files after success (overrides archive) |
| `VerifyTransfer` | `true` | Verify remote file size after transfer |
| `FileStabilitySeconds` | `5` | Seconds a file must be unchanged before transferring |
| `ProcessingIntervalSeconds` | `10` | How often the queue is checked |
| `IncludeSubdirectories` | `true` | Watch subdirectories |

## WebPortal

### Authentication

| Key | Default | Description |
|---|---|---|
| `EnableLocalAccounts` | `true` | Allow email/password sign-in |
| `EnableEntraId` | `false` | Enable Entra ID (Azure AD) OIDC sign-in |
| `RequireApproval` | `true` | New registrations require admin approval |
| `RequireEmailConfirmation` | `false` | Require email confirmation on register |
| `ConnectionString` | `Data Source=/var/lib/n24-data-relay/n24datarelay.db` | SQLite path for Identity + transfer records |

When `EnableEntraId: true`, also add a top-level `AzureAd` section:
```json
"AzureAd": {
  "Instance":     "https://login.microsoftonline.com/",
  "TenantId":     "your-tenant-id",
  "ClientId":     "your-client-id",
  "ClientSecret": "your-client-secret",
  "CallbackPath": "/signin-oidc"
}
```
Register the app in Azure AD with redirect URI `https://<host>:<port>/signin-oidc`.

### Kestrel (HTTP/HTTPS)

| Key | Default | Description |
|---|---|---|
| `HttpPort` | `8080` | HTTP listen port |
| `HttpsPort` | `8443` | HTTPS listen port (only used if `EnableHttps: true`) |
| `EnableHttps` | `false` | Enable TLS on Kestrel |
| `CertificatePath` | `null` | Path to `.pfx` certificate file |
| `CertificatePassword` | `null` | PFX password (prefer env var or secrets) |

### Upload Limits

| Key | Default | Description |
|---|---|---|
| `MaxFileSizeBytes` | `4294967295` (~4 GB) | Max single-file upload size |
| `MaxConcurrentUploads` | `10` | Max simultaneous uploads |
| `BlockedFileExtensions` | `[".exe", ".dll", ...]` | Extensions rejected at upload |
| `EnableUploadToTransfer` | `true` | Show "send to transfer" option on upload page |

## Transfer.Ssh

| Key | Default | Description |
|---|---|---|
| `Host` | `""` | Remote SSH server hostname or IP |
| `Port` | `22` | SSH port |
| `Username` | `""` | SSH username |
| `AuthMethod` | `"PublicKey"` | `"PublicKey"` or `"Password"` |
| `PrivateKeyPath` | `null` | Path to private key file (PublicKey auth) |
| `PrivateKeyPassphrase` | `null` | Key passphrase (prefer `N24_SSH_KEY_PASSPHRASE` env var) |
| `DestinationPath` | `""` | Remote directory to transfer files into |
| `StrictHostKeyChecking` | `true` | Verify remote host key |
| `CreateDestinationDirectory` | `true` | Create remote destination if missing |
| `ConnectionTimeout` | `30` | SSH connection timeout (seconds) |
| `OperationTimeout` | `300` | SCP operation timeout (seconds) |
| `Compression` | `true` | Enable SSH compression |

### SSH passphrase as environment variable

Rather than storing the passphrase in `appsettings.json`, set the `N24_SSH_KEY_PASSPHRASE` environment variable. The app checks it first:

```bash
# In /etc/systemd/system/n24-data-relay.service.d/override.conf:
[Service]
Environment=N24_SSH_KEY_PASSPHRASE=your-passphrase
```

## Transfer.Smb

SMB transfer is planned for Phase 3. Settings are present in the config model but the transfer method is not yet implemented.

| Key | Default | Description |
|---|---|---|
| `Server` | `""` | SMB server hostname |
| `SharePath` | `""` | Share path (e.g. `/share/incoming`) |
| `UseCredentials` | `false` | Use username/password for SMB auth |
| `Username` | `null` | SMB username |
| `Domain` | `null` | SMB domain |

## Logging

```json
"Logging": {
  "RetentionDays":   30,
  "MaxFileSizeMB":   100,
  "EnableConsole":   true,
  "EnableEventLog":  false
}
```

`EnableEventLog` is ignored on Linux (no-op).
