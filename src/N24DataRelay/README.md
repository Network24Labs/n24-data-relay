# N24 Data Relay (host)

Single entry point: runs the web app and the file watcher/transfer worker.

**Development:** With `ASPNETCORE_ENVIRONMENT=Development`, upload and transfer data lives under this project’s `data/` directory (e.g. `data/uploads/transfer/<username>/`). The app creates these folders on startup. Use `appsettings.Development.json` for local paths and the fake OT SSH target.
