---
name: omniserv-packaging
description: >-
  Procedures and checks for building, inspecting, and installing OmniServ Debian packages (.deb) on Linux.
  Use when building binaries, releasing updates, updating system packages, or troubleshooting packaging scripts.
---

# OmniServ Linux Packaging & Release Workflow

## 1. Version Pre-check
Before building, synchronize the version across:
1. `linux/app/omniserv/__init__.py`: `__version__ = "X.Y.Z"`
2. Recent git tags: `git tag -l`
3. Installed package state: `dpkg -s omniserv`

## 2. Unprivileged Package Build
`linux/build.sh` uses `dpkg-deb --root-owner-group` and must run unprivileged:
```bash
./linux/build.sh [version]
```
Outputs `.deb` artifact to: `linux/dist/omniserv_<version>_all.deb`.

## 3. Package Verification
Inspect the built package structure without installing:
```bash
dpkg-deb -c linux/dist/omniserv_<version>_all.deb
```
Verify permissions (`root/root`), file sizes, and included assets.

## 4. System Installation Protocol
- Installing to `/usr/lib/omniserv` modifies system files and requires root privileges.
- In automated/headless subshells where `sudo -n true` fails, **never run interactive `sudo` or `pkexec`** (it will hang on password prompt).
- Always instruct the user to run the installation in their authenticated terminal:
  ```bash
  sudo dpkg -i linux/dist/omniserv_<version>_all.deb
  ```
