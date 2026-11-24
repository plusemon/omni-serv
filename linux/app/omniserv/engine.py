"""EngineClient — drives the bash engine (engine/omniserv) the same way the macOS
SwiftUI app and the Windows WinUI app drive their cores: spawn the CLI, parse the
`api` JSON, run verbs. Long operations run on a worker thread and report back on the
GTK main loop via GLib.idle_add.
"""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import threading
from typing import Callable

# Strip terminal colour/escape sequences from engine output before it's shown in toasts /
# the activity log / parsed (the bash engine emits ANSI for its ✓/✗/headers).
_ANSI = re.compile(r"\x1b\[[0-9;]*[A-Za-z]")

import gi
from gi.repository import GLib

# Verbs that touch root-owned state (apt, systemd, :80/:443, /etc/hosts, mkcert CA). The GUI
# runs these via pkexec (a single polkit prompt); the engine then runs root-aware and chowns
# anything it creates back to the user. Everything else (api/status/logs/config/db/php) runs
# unprivileged as the user.
# loginitem IS privileged since 1.0.50: it writes a SYSTEM unit (/etc/systemd/system) so the
# services actually start at boot as root (a `systemctl --user` unit could never start them —
# they need :80/:443 + systemctl). No enable/is-enabled mismatch: both the (root) enable and
# the api's unprivileged `systemctl is-enabled omniserv.service` read the same SYSTEM state.
_ALWAYS_PRIVILEGED = {"install", "update", "uninstall", "helper",
                      "pma", "adminer", "loginitem"}


def _needs_root(args: tuple, force_root: bool = False) -> bool:
    if not args:
        return False
    if force_root:
        return True
    v = args[0]
    if v in _ALWAYS_PRIVILEGED:
        return True

    # If the sudoers helper is active, services, sites, SSL, and DNS all run passwordless
    helper_active = EngineClient.is_helper_active()

    if v in ("start", "stop", "restart"):
        target = args[1] if len(args) > 1 else "all"
        # Pure user-space services never need root elevation
        if target.startswith("php") or target in ("mailpit", "httpd", "apache", "python", "fnm", "composer"):
            return False
        if helper_active:
            return False
        return True

    if v in ("secure", "unsecure", "resecure", "dns"):
        return not helper_active

    if v in ("site", "pysite", "nodesite") and len(args) > 1 and args[1] in ("add", "rm", "remove"):
        return not helper_active

    if v == "site" and len(args) > 1 and args[1] == "server":
        # Switching to OLS touches /usr/local/lsws
        if len(args) > 3 and args[3] in ("ols", "openlitespeed"):
            return True
        return not helper_active

    return False


class EngineClient:
    def __init__(self) -> None:
        self.path = self._find_engine()

    # ── locating the engine script ───────────────────────────────────────────
    def _find_engine(self) -> str:
        env = os.environ.get("OMNISERV_ENGINE")
        if env and os.path.exists(env):
            return os.path.abspath(env)
        here = os.path.dirname(os.path.abspath(__file__))
        candidates = [
            # repo layout: linux/app/omniserv/engine.py → ../../../engine/omniserv
            os.path.join(here, "..", "..", "..", "engine", "omniserv"),
            os.path.expanduser("~/eng/omniserv"),          # dev sandbox
            "/usr/lib/omniserv/engine/omniserv",            # .deb install
            "/opt/omniserv/engine/omniserv",                # AppImage / opt install
            shutil.which("omniserv"),
        ]
        for c in candidates:
            if c and os.path.exists(c):
                return os.path.abspath(c)
        return "omniserv"  # last resort: rely on PATH

    # ── command construction (elevate privileged verbs) ──────────────────────
    _sudo_nopw: bool | None = None
    _nginx_helper: bool | None = None

    @classmethod
    def is_helper_active(cls) -> bool:
        """True if the sudoers helper for nginx is installed and active. Cached."""
        if cls._nginx_helper is None:
            try:
                cls._nginx_helper = (
                    shutil.which("sudo") is not None and
                    (subprocess.run(
                        ["sudo", "-n", "/usr/sbin/nginx", "-v"],
                        capture_output=True, timeout=2
                    ).returncode == 0 or
                     "NOPASSWD: /usr/sbin/nginx" in subprocess.run(
                        ["sudo", "-n", "-l"],
                        capture_output=True, text=True, timeout=2
                    ).stdout)
                )
            except Exception:
                cls._nginx_helper = False
        return cls._nginx_helper

    def _can_sudo_nopw(self) -> bool:
        """True if `sudo` runs without a password (passwordless sudoers / WSL). Cached."""
        if EngineClient._sudo_nopw is None:
            try:
                EngineClient._sudo_nopw = (shutil.which("sudo") is not None and
                    subprocess.run(["sudo", "-n", "true"], capture_output=True, timeout=5).returncode == 0)
            except Exception:
                EngineClient._sudo_nopw = False
        return EngineClient._sudo_nopw

    def _build(self, args: tuple, force_root: bool = False) -> list[str]:
        base = ["bash", self.path, *args]
        if not (_needs_root(args, force_root) and os.geteuid() != 0):
            return base
        # 1) passwordless sudo (WSL, or our nginx helper / a configured sudoers) → silent, and
        #    works where there's no polkit auth agent (e.g. WSL2 has no agent to show a prompt).
        if self._can_sudo_nopw():
            return ["sudo", "-E", "bash", self.path, *args]
        # 2) desktop: pkexec shows a polkit password dialog (GNOME/KDE).
        if shutil.which("pkexec"):
            bh_home = os.environ.get("OMNISERV_HOME", os.path.expanduser("~/.omniserv"))
            return ["pkexec", "env", f"OMNISERV_HOME={bh_home}", "OMNISERV_GUI=1",
                    "bash", self.path, *args]
        return base

    # ── synchronous run (use only for fast verbs like api/status) ────────────
    def run(self, *args: str, timeout: int | None = None,
            env: dict | None = None, force_root: bool = False) -> tuple[int, str]:
        if args and args[0] == "helper":
            EngineClient._nginx_helper = None
        try:
            # `env` carries secrets (e.g. OMNISERV_DB_PASSWORD) so they go via the process
            # environment (owner-only /proc/<pid>/environ) instead of argv (world-readable in `ps`).
            run_env = {**os.environ, "OMNISERV_GUI": "1"}
            if env:
                run_env.update(env)
            p = subprocess.run(
                self._build(args, force_root),
                capture_output=True, text=True, timeout=timeout,
                env=run_env,
            )
            # pkexec exit 126 = user dismissed the auth dialog; 127 = not authorized.
            if p.returncode in (126, 127) and _needs_root(args, force_root):
                return p.returncode, "Cancelled — administrator approval is required for this action."
            if args and args[0] == "helper":
                EngineClient._nginx_helper = None
            return p.returncode, _ANSI.sub("", (p.stdout or "") + (p.stderr or ""))
        except subprocess.TimeoutExpired:
            return 1, f"timed out after {timeout}s"
        except Exception as e:  # noqa: BLE001
            return 1, str(e)

    # ── api JSON snapshot the GUI renders from ───────────────────────────────
    def api(self) -> dict:
        rc, out = self.run("api", timeout=20)
        data = _extract_json(out)
        if "helper" in data:
            EngineClient._nginx_helper = bool(data["helper"])
        return data

    # ── async run for anything slow (install/start/secure/…) ─────────────────
    def run_async(self, args: list[str], on_done: Callable[[int, str], None],
                  env: dict | None = None, force_root: bool = False) -> None:
        def worker() -> None:
            rc, out = self.run(*args, env=env, force_root=force_root)
            def _dispatch():
                try:
                    on_done(rc, out)
                except Exception as e:  # noqa: BLE001
                    print(f"Error in async callback for {args}: {e}")
                return False
            GLib.idle_add(_dispatch)
        threading.Thread(target=worker, daemon=True).start()


def _extract_json(text: str) -> dict:
    """The api verb prints pure JSON, but be defensive about any stray header/log
    line by slicing from the first '{' to the matching last '}'."""
    if not text:
        return {}
    try:
        return json.loads(text)
    except Exception:
        pass
    s, e = text.find("{"), text.rfind("}")
    if s >= 0 and e > s:
        try:
            return json.loads(text[s : e + 1])
        except Exception:
            return {}
    return {}
