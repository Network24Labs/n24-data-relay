# Setup Guide

How to install and run N24 Data Relay on Ubuntu Linux.

## Prerequisites

- Ubuntu 22.04 LTS or later (or any systemd-based Linux distro)
- [.NET 8 Runtime](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu) (or use the self-contained publish)
- Docker (optional — only needed for the fake-OT test target)
- An SSH server on the target SCADA/OT system reachable from this host

### Install .NET 8 Runtime (Ubuntu)

```bash
sudo apt-get update
sudo apt-get install -y dotnet-runtime-8.0
```

## Installation

### Option A: From published binaries

```bash
# Publish self-contained (no .NET runtime required on server)
dotnet publish src/N24DataRelay/N24DataRelay.csproj \
  -c Release -r linux-x64 --self-contained \
  -o /opt/n24-data-relay

# Or framework-dependent (requires .NET 8 runtime installed)
dotnet publish src/N24DataRelay/N24DataRelay.csproj \
  -c Release -r linux-x64 --no-self-contained \
  -o /opt/n24-data-relay
```

### Option B: Run from source (development)

```bash
cd src/N24DataRelay
dotnet run
```

The app auto-creates `data/` subdirectories under the project root in Development mode.

## Directory Layout (Production)

```
/opt/n24-data-relay/          ← application binaries
/etc/n24-data-relay/          ← configuration (appsettings.json)
/var/lib/n24-data-relay/
  uploads/                    ← user upload staging
  uploads/transfer/           ← watch directory → queued for transfer
  archive/                    ← files after successful transfer
  temp/                       ← temporary files
  n24datarelay.db             ← SQLite database (users + transfer history)
/var/log/n24-data-relay/      ← application logs
```

### Create directories and service user

```bash
# Create the service account (no login shell, no home dir)
sudo useradd --system --no-create-home --shell /usr/sbin/nologin n24-data-relay

# Create required directories and set ownership
sudo mkdir -p /etc/n24-data-relay \
              /etc/n24-data-relay/keys \
              /var/lib/n24-data-relay/uploads/transfer \
              /var/lib/n24-data-relay/archive \
              /var/lib/n24-data-relay/temp \
              /var/log/n24-data-relay

sudo chown -R n24-data-relay:n24-data-relay \
              /var/lib/n24-data-relay \
              /var/log/n24-data-relay

sudo chown root:n24-data-relay /etc/n24-data-relay
sudo chmod 750 /etc/n24-data-relay

# Data-protection key ring (ASP.NET uses this to encrypt auth cookies/tokens —
# the app fails at first request without a writable directory here)
sudo chown n24-data-relay:n24-data-relay /etc/n24-data-relay/keys
sudo chmod 700 /etc/n24-data-relay/keys
```

## Configuration

Copy the production config template and edit it:

```bash
sudo cp deploy/config/appsettings.Linux.json /etc/n24-data-relay/appsettings.json
sudo chmod 640 /etc/n24-data-relay/appsettings.json
sudo chown root:n24-data-relay /etc/n24-data-relay/appsettings.json
sudo nano /etc/n24-data-relay/appsettings.json
```

At minimum, fill in the SSH transfer settings (`Transfer.Ssh.Host`, `Username`, auth method, and `DestinationPath`). See [CONFIGURATION.md](CONFIGURATION.md) for all options.

## SSH Key Setup (PublicKey auth method)

```bash
# Generate an ED25519 key pair for the service account
sudo -u n24-data-relay ssh-keygen -t ed25519 \
  -f /etc/n24-data-relay/ssh/id_ed25519 -N ""

# Copy the public key to the remote OT server
cat /etc/n24-data-relay/ssh/id_ed25519.pub
# → add this to ~/.ssh/authorized_keys on the remote server's target user

# Set strict permissions
sudo chmod 700 /etc/n24-data-relay/ssh
sudo chmod 600 /etc/n24-data-relay/ssh/id_ed25519
```

Update `appsettings.json`:
```json
"Ssh": {
  "AuthMethod": "PublicKey",
  "PrivateKeyPath": "/etc/n24-data-relay/ssh/id_ed25519"
}
```

If the key has a passphrase, set it as an environment variable (not in the config file):
```bash
sudo systemctl edit n24-data-relay
# Add under [Service]:
# Environment=N24_SSH_KEY_PASSPHRASE=your-passphrase
```

## systemd Service

```bash
# Copy the unit file
sudo cp deploy/systemd/n24-data-relay.service /etc/systemd/system/

# Edit WorkingDirectory and ExecStart if your install path differs from /opt/n24-data-relay
sudo nano /etc/systemd/system/n24-data-relay.service

# Enable and start
sudo systemctl daemon-reload
sudo systemctl enable --now n24-data-relay

# Check status
sudo systemctl status n24-data-relay
sudo journalctl -u n24-data-relay -f
```

## First Run

1. Open `http://<server-ip>:8080` in a browser.
2. Click **Register** and create the first account. It is automatically approved and assigned the **Admin** role.
3. Sign in and navigate to **Admin → Users** to manage subsequent registrations.
4. Go to **Upload** to upload a file and verify transfer to the target system.

## HTTPS (optional)

Set `EnableHttps: true` in the `Kestrel` section and provide a certificate:

```json
"Kestrel": {
  "EnableHttps": true,
  "HttpsPort": 8443,
  "CertificatePath": "/etc/n24-data-relay/certs/server.pfx",
  "CertificatePassword": "your-pfx-password"
}
```

Or use a reverse proxy (nginx, Caddy) in front of the app on port 8080.

## Multi-instance (DMZ ↔ OT) and deployment checklist

When linking two relay instances (e.g. DMZ and OT) so that files can move both ways over SSH:

1. **Install the app on both sides** (DMZ host and OT host). Each instance has its own config, database, and auth (no shared login).
2. **SSH access**: Each host must run an SSH server (sshd). The transfer directory (e.g. `/var/lib/n24-data-relay/uploads/transfer`) must be writable by the SSH user that the *other* instance uses to connect. So the DMZ instance connects to the OT host and writes into the OT instance’s incoming path, and vice versa.
3. **Configure each instance**: In **Admin → Settings → Instances**, set an instance name (e.g. "DMZ", "OT") and add the other instance as a linked peer (host, port, incoming path). Then in **SSH Target**, set the transfer target to the peer’s host and destination path to the peer’s incoming path. Use **Test connection** (on the SSH tab and per peer on the Instances tab) to verify the path is writable before transferring files.
4. **No extra firewall rules**: Same SSH as single-instance; only the app hosts need to accept SSH from each other.

**Deployment checklist (all relay hosts):**

- Synchronise time (e.g. NTP or chrony) so that audit log timestamps can be correlated across instances. Clock skew between DMZ and OT makes it harder to match “send” and “receive” events in the two audit logs.
- SSH keys and known-host fingerprints configured for the peer.
- Incoming path exists and is writable by the SSH user.

## 3-hop and multi-route (OT → DMZ → CORP)

For **single-direction** 3-hop (e.g. OT → DMZ → CORP): configure each instance with one SSH target pointing to the next hop (OT → DMZ, DMZ → CORP). No code change needed.

For **bidirectional** 3-hop (OT ↔ DMZ ↔ CORP) through the **same** DMZ instance, or for a CORP instance with both "Send to DMZ" and "Send to SCADA" (OT), use **multiple outbound routes**:

1. **Config**: Add `Transfer.Routes` (see [CONFIGURATION.md](CONFIGURATION.md#transferroutes-multi-target--3-hop)) with one entry per direction. Each route has a distinct `SourcePath` (watch directory) and its own SSH (or SMB) target. Example: DMZ has two routes — "to CORP" (e.g. `incoming-from-ot`) and "to OT" (e.g. `incoming-from-corp`). Create those directories; the watcher will use them automatically.
2. **Upload**: When multiple routes exist, the Upload page shows "Send to transfer" with one option per route (e.g. "Send to CORP", "Send to SCADA"), using the route names from config and branding (DmzSideName, ScadaSideName, CorpSideName).
3. **Verification**: In **Admin → Settings → Routes**, review effective routes and use **Test** per route to verify each destination path (write probe + verify).

Zone labels (DMZ, SCADA, CORP) are configurable under **Admin → Settings → Branding**.

## Development Test Target (fake-OT)

```bash
# Requires Docker
./start-fake-ot.sh start    # builds image, starts container on :2222
./start-fake-ot.sh status   # show container status
./start-fake-ot.sh logs     # tail logs
./start-fake-ot.sh stop     # stop and remove container
```

Create `src/N24DataRelay/appsettings.Development.json` to point at the fake-OT container:
```json
{
  "N24DataRelay": {
    "Service": { "WatchDirectory": "data/uploads/transfer" },
    "Transfer": {
      "Ssh": {
        "Host": "127.0.0.1",
        "Port": 2222,
        "Username": "otuser",
        "AuthMethod": "Password",
        "DestinationPath": "incoming"
      }
    }
  }
}
```
