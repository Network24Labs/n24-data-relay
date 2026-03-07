# Migration from ZL File Relay

Reference guide for migrating from ZL File Relay (Windows) to N24 Data Relay (Linux).

## Component Mapping

| ZL File Relay (Windows) | N24 Data Relay (Linux) | Notes |
|---|---|---|
| `ZLFileRelay.Service` (Windows Service) | `N24DataRelay.Watcher` (`IHostedService`) | Runs in the same process as the web app |
| `ZLFileRelay.WebPortal` (Windows Service, Kestrel) | `N24DataRelay.WebApp` (library in host process) | Same process, one Kestrel instance |
| `ZLFileRelay.ConfigTool` (WPF desktop app) | Web Admin UI (`/Admin/Users`) + `appsettings.json` | No WPF, no Windows SCM interaction |
| Windows Service (`sc.exe`, SCM) | systemd (`systemctl`) | Unit file at `deploy/systemd/n24-data-relay.service` |
| Windows DPAPI (`ProtectedData`) | `ConfigOrEnvCredentialProvider` + env vars | `N24_SSH_KEY_PASSPHRASE` env var |
| Windows Certificate Store (`X509Store`) | File-based `.pfx` certificate | Set `CertificatePath` in `Kestrel` config |
| `C:\ProgramData\ZLFileRelay\` | `/etc/n24-data-relay/` (config) | See path mapping table below |
| `C:\FileRelay\uploads\` | `/var/lib/n24-data-relay/uploads/` | |
| `C:\FileRelay\logs\` | `/var/log/n24-data-relay/` | |
| `.status/*.status.json` (file-based IPC) | `SqliteTransferTracker` (in-process, SQLite) | Transfer history persists across restarts |
| `scp.exe` / `ssh.exe` (spawned process) | `SSH.NET` library (in-process) | No dependency on system `ssh`/`scp` binaries |
| `mpr.dll` P/Invoke (SMB credentials) | Planned: CIFS mount / `smbclient` (Phase 3) | SMB transfer method stubbed |
| Inno Setup `.exe` installer | Planned: `.deb` package (Phase 3) | |
| PowerShell build scripts | Bash scripts (planned Phase 3) | |
| `ZLFileRelay` config section key | `N24DataRelay` config section key | See config rename table below |

## Config Key Rename

The root config section changed from `ZLFileRelay` to `N24DataRelay`.

Example before (ZL File Relay `appsettings.json`):
```json
{
  "ZLFileRelay": {
    "Service": { "WatchDirectory": "C:\\FileRelay\\uploads\\transfer" },
    "Transfer": {
      "Ssh": { "Host": "scada-server.example.com" }
    }
  }
}
```

Example after (N24 Data Relay `appsettings.json`):
```json
{
  "N24DataRelay": {
    "Service": { "WatchDirectory": "/var/lib/n24-data-relay/uploads/transfer" },
    "Transfer": {
      "Ssh": { "Host": "scada-server.example.com" }
    }
  }
}
```

## Path Conversion Table

| ZL File Relay (Windows) | N24 Data Relay (Linux) |
|---|---|
| `C:\ProgramData\ZLFileRelay\appsettings.json` | `/etc/n24-data-relay/appsettings.json` |
| `C:\ProgramData\ZLFileRelay\zlfilerelay.db` | `/var/lib/n24-data-relay/n24datarelay.db` |
| `C:\ProgramData\ZLFileRelay\ssh\id_ed25519` | `/etc/n24-data-relay/ssh/id_ed25519` |
| `C:\FileRelay\uploads\` | `/var/lib/n24-data-relay/uploads/` |
| `C:\FileRelay\uploads\transfer\` | `/var/lib/n24-data-relay/uploads/transfer/` |
| `C:\FileRelay\archive\` | `/var/lib/n24-data-relay/archive/` |
| `C:\FileRelay\logs\` | `/var/log/n24-data-relay/` |

## Features: Preserved vs. Changed

### Preserved (functionally identical)
- SSH/SCP file transfer with retry, backoff, and archive
- File stability check (configurable `FileStabilitySeconds`)
- Web upload portal with multi-file upload
- Real-time transfer status via SignalR
- Local account auth with email/password
- Entra ID (Azure AD) OIDC authentication
- User approval workflow (`RequireApproval`)
- SQLite user database
- Transfer method selection (`ssh` or `smb`)
- File extension blocklist on upload

### Changed
- **Service model**: Two separate Windows Services → one systemd unit running both web + watcher
- **Credential storage**: DPAPI-encrypted `credentials.dat` → `N24_SSH_KEY_PASSPHRASE` environment variable (or plaintext in config for non-sensitive settings)
- **ConfigTool**: WPF desktop app → web-based Admin UI at `/Admin/Users`
- **Transfer status IPC**: `.status/*.status.json` files on disk → in-process `SqliteTransferTracker` with SQLite persistence
- **SCP implementation**: spawns `scp.exe`/`ssh.exe` system processes → SSH.NET managed library (no system binary dependency)
- **Certificate management**: Windows Certificate Store → file-based `.pfx`
- **Installer**: Inno Setup `.exe` → `.deb` package (planned Phase 3)

### Not yet implemented (Phase 3)
- **SMB transfer with credentials**: `mpr.dll` WNetAddConnection2 → CIFS mount / `smbclient`
- **`.deb` packaging**
- **Config hot-reload** (currently requires service restart)
- **Admin configuration UI** (editing `appsettings.json` via web — currently manual file edit)

## Migrating Existing Users

The Identity database schema changed:
- Config key changed from `ZLFileRelay.WebPortal.Authentication.ConnectionString` to `N24DataRelay.WebPortal.Authentication.ConnectionString`
- The `ApplicationUser` table gains `IsApproved`, `ApprovedDate`, `ApprovedBy`, `RegistrationDate` columns

There is no automated migration tool. For existing installations:
1. Export users from the ZL File Relay SQLite DB if needed (email addresses only — passwords are not portable as they use a different hasher configuration)
2. Start N24 Data Relay fresh — the first registered user becomes Admin
3. Re-invite users to self-register and approve them via the Admin UI

## Windows-Specific Code Not Present in N24

The following Windows-only components have no equivalent in N24 (intentionally omitted):

- `ProtectedData` (DPAPI) — replaced by env vars
- `mpr.dll` P/Invoke — SMB credentials not yet implemented
- `ServiceController` / `sc.exe` — service lifecycle handled by systemd
- `System.DirectoryServices` (Active Directory) — not implemented
- `WindowsIdentity` / `WindowsPrincipal` — replaced by standard ASP.NET Core Identity
- `System.Management.Automation` (PowerShell SDK) — not needed
- Windows Event Log sink (Serilog) — console + file sinks only
- Windows ACL APIs (`GetAccessControl`, `SetAccessControl`) — use `chmod`/`chown`
