---
name: omniserv-packaging
description: >-
  Procedures and checks for building, inspecting, and installing OmniServ packages (.deb on Debian/Ubuntu, .rpm on Fedora/RHEL) on Linux.
  Use when building binaries, releasing updates, updating system packages, or troubleshooting packaging scripts.
---

# OmniServ Linux Packaging & Release Workflow

## 1. Version Pre-check
Before building, synchronize the version across:
1. `linux/app/omniserv/__init__.py`: `__version__ = "X.Y.Z"`
2. Recent git tags: `git tag -l`
3. Installed package state: `dpkg -s omniserv` (Debian/Ubuntu) or `rpm -q omniserv` (Fedora/RHEL)

## 2. Unprivileged Package Build
`linux/build.sh` automatically detects the host distribution (or accepts `--deb`, `--rpm`, `--all` flags):
```bash
./linux/build.sh [version] [--deb | --rpm | --all]
```
- Debian/Ubuntu: outputs `.deb` artifact to `linux/dist/omniserv_<version>_all.deb`.
- Fedora/RHEL: outputs `.rpm` artifact to `linux/dist/omniserv-<version>-1.noarch.rpm` via `linux/build-rpm.sh`.

Direct RPM build:
```bash
./linux/build-rpm.sh [version]
```

## 3. Package Verification
Inspect the built package structure without installing:
- **Debian (.deb)**:
  ```bash
  dpkg-deb -c linux/dist/omniserv_<version>_all.deb
  dpkg-deb --info linux/dist/omniserv_<version>_all.deb
  ```
- **Fedora/RHEL (.rpm)**:
  ```bash
  rpm -qip linux/dist/omniserv-<version>-1.noarch.rpm
  rpm -qlp linux/dist/omniserv-<version>-1.noarch.rpm
  rpm -qp --scripts linux/dist/omniserv-<version>-1.noarch.rpm
  ```
Verify permissions (`root/root`), file sizes, dependencies, and included assets.

## 4. System Installation Protocol
- Installing to `/usr/lib/omniserv` modifies system files and requires root privileges.
- In automated/headless subshells where `sudo -n true` fails, **never run interactive `sudo` or `pkexec`** (it will hang on password prompt).
- Always instruct the user to run the installation in their authenticated terminal:
  - **Debian/Ubuntu**:
    ```bash
    sudo dpkg -i linux/dist/omniserv_<version>_all.deb
    sudo apt-get -f install -y
    ```
  - **Fedora/RHEL**:
    ```bash
    sudo dnf install linux/dist/omniserv-<version>-1.noarch.rpm
    ```
