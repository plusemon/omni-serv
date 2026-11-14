# OmniServ Workspace Development Guidelines

## Privileged Commands & Subshell Execution
- Do not run `sudo` or `pkexec` commands in background tasks or subshells unless `sudo -n true` succeeds.
- When system binary installation is requested, build the `.deb` artifact unprivileged via `./linux/build.sh` and present the `sudo dpkg -i ...` command to the user.

## GTK4 / Libadwaita CSS Guidelines
- Never use `!important` in GTK CSS files (unsupported syntax in GTK4).
- Use `Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION + 100` and explicit selector chaining to override host desktop themes.
- Always pair background and foreground color overrides to ensure readability across light and dark system themes.
