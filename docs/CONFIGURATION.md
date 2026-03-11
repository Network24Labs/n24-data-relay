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
  "DmzSideName": "DMZ",
  "ScadaSideName": "SCADA",
  "CorpSideName": "CORP",
  "Theme": {
    "PrimaryColor": "#0066CC",
    "SecondaryColor": "#003366",
    "AccentColor":   "#FF6600"
  }
}
```

| Key | Default | Description |
|---|---|---|
| `DmzSideName` | `"DMZ"` | Label for the DMZ zone (upload/transfer UI). |
| `ScadaSideName` | `"SCADA"` | Label for the SCADA/OT zone (transfer destination). |
| `CorpSideName` | `"CORP"` | Label for the corporate/third zone (3-hop, multi-route UI). |

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
| `InstanceName` | `null` | Display name for this instance (e.g. "DMZ", "OT"). Used in UI and audit. |
| `IncomingPath` | `null` | Path on this host that peers use as SCP destination. Empty = use WatchDirectory. |
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

## InstanceLinking (multi-instance)

When running multiple relay instances (e.g. one in DMZ, one in OT) that link to each other as peers:

| Key | Default | Description |
|---|---|---|
| `InstanceId` | `null` | Optional stable GUID for this instance (for linking references). |
| `LinkedInstances` | `[]` | List of linked peer instances (see below). |

Each item in `LinkedInstances` has:

| Key | Description |
|---|---|
| `Name` | Display name (e.g. "OT", "DMZ"). |
| `Host` | Peer hostname or IP. |
| `Port` | SSH port (default 22). |
| `IncomingPath` | Remote path on the peer that this instance uses as SCP destination (for UI/docs). |
| `KnownHostFingerprint` | Optional SHA-256 host key fingerprint for the peer. |

For DMZ↔OT linking, set this instance’s **SSH Target** (Transfer.Ssh) to the peer’s host and `DestinationPath` to the peer’s Incoming path. No new protocols or firewall rules; same SSH as single-instance. Use **Admin → Settings → Instances** to configure instance identity and linked peers, and use **Test connection** (SSH Target and per-peer in Instances tab) to verify the destination path is writable before transferring files.

## Transfer.Routes (multi-target / 3-hop)

When you need **multiple outbound targets** (e.g. bidirectional 3-hop OT ↔ DMZ ↔ CORP, or CORP with “Send to DMZ” and “Send to SCADA”), configure **routes** instead of a single SSH target. Each route has a **source path** (watch directory); files landing in that path are sent using that route’s SSH (or SMB) settings.

If `Transfer.Routes` is missing or empty, the app behaves as before: a single effective route is built from `Service.WatchDirectory` and `Transfer.Ssh` (or SMB). When `Transfer.Routes` is set, the watcher uses each route’s `SourcePath` as a watch directory and routes files by path.

| Key | Description |
|---|---|
| `Transfer.Routes` | List of `TransferRouteSettings` (see below). When non-empty, these define all outbound routes. |

Each **TransferRouteSettings** entry:

| Key | Description |
|---|---|
| `Name` | Display name (e.g. "to CORP", "to OT"). Shown in Admin → Settings → Routes and on Upload when multiple routes exist. |
| `SourcePath` | Watch directory for this route. Files under this path are transferred using this route’s SSH/SMB. Must be absolute. |
| `TransferMethod` | `"ssh"` or `"smb"`. |
| `Ssh` | Same structure as top-level `Transfer.Ssh` (Host, Port, Username, AuthMethod, DestinationPath, etc.). |
| `Smb` | Same structure as top-level `Transfer.Smb`. |

Example (DMZ with two routes — to CORP and to OT):

```json
"Transfer": {
  "Routes": [
    {
      "Name": "to CORP",
      "SourcePath": "/var/lib/n24-data-relay/incoming-from-ot",
      "TransferMethod": "ssh",
      "Ssh": {
        "Host": "corp-host",
        "Port": 22,
        "Username": "relay",
        "DestinationPath": "/var/lib/n24-data-relay/incoming"
      }
    },
    {
      "Name": "to OT",
      "SourcePath": "/var/lib/n24-data-relay/incoming-from-corp",
      "TransferMethod": "ssh",
      "Ssh": {
        "Host": "ot-host",
        "Port": 22,
        "Username": "relay",
        "DestinationPath": "/var/lib/n24-data-relay/incoming"
      }
    }
  ]
}
```

Use **Admin → Settings → Routes** to add, edit, and remove routes and to **Test** each route (writes a probe file and verifies the destination path). You can also configure routes by editing the config file directly.

## Transfer.Smb

SMB transfer is supported via a locally-mounted share. Settings are present in the config model but the transfer method is not yet implemented.

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
