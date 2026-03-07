# Migration from ZL File Relay (stub)

Mapping from ZL File Relay (Windows) to N24 Data Relay (Linux), and config/feature parity notes.

*(To be filled in when implementation and config model are stable.)*

## Planned content

- **Component mapping:** ZL Service → N24 Watcher; ZL WebPortal → N24 WebApp; ZL ConfigTool → WebApp admin + config file + systemd.
- **Config migration:** Renaming config section from `ZLFileRelay` to `N24DataRelay`, and converting Windows paths to Linux paths.
- **Features:** What is preserved (SSH/SCP, SMB, uploads, auth) and what changes (no DPAPI, no Windows Service, no WPF ConfigTool; credential storage and service management are Linux-based).
