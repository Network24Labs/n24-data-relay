# Fake OT environment (SSH/SCP)

Full OpenSSH server in a container so N24 Data Relay can test **SCP** transfers over SSH (required for your security model). Not SFTP-only.

- **Image:** Alpine + OpenSSH; supports SCP and SFTP.
- **User:** `otuser` / password `supersecret123` (change in Dockerfile if needed).
- **Incoming dir:** `/home/otuser/incoming` (bind-mounted to `~/n24-test-uploads` by `start-fake-ot.sh`).

Use the repo-root script: `./start-fake-ot.sh start` (builds this image on first run).
