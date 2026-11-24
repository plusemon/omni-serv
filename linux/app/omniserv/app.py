"""OmniServApp — the Adw.Application: loads brand CSS, opens the MainWindow."""
from __future__ import annotations

import os
import shutil
import subprocess

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, Gdk, Gio, GLib, Gtk  # noqa: E402

from .engine import EngineClient  # noqa: E402
from .prefs import cfg_str  # noqa: E402
from .window import MainWindow  # noqa: E402


_theme_provider: Gtk.CssProvider | None = None

DARK_THEME_CSS = """
@define-color window_bg_color #121316;
@define-color window_fg_color #e4e7eb;
@define-color view_bg_color #121316;
@define-color view_fg_color #e4e7eb;
@define-color headerbar_bg_color #121316;
@define-color headerbar_fg_color #e4e7eb;
@define-color card_bg_color #1a1d21;
@define-color card_fg_color #e4e7eb;
@define-color card_border_color rgba(255, 255, 255, 0.06);

window.background, .background {
  background-color: #121316;
  color: #e4e7eb;
}
.bh-sidebar {
  background-color: #16181c;
  border-right: 1px solid rgba(255, 255, 255, 0.06);
}
.bh-brand {
  color: #ffffff;
}
.bh-sidebar list.navigation-sidebar row {
  color: #e4e7eb;
}
.bh-sidebar list.navigation-sidebar row:hover:not(:selected) {
  background-color: rgba(255, 255, 255, 0.05);
}
.card, .boxed-list, list.boxed-list, .bh-metric {
  background-color: #1a1d21;
  color: #e4e7eb;
  border-color: rgba(255, 255, 255, 0.06);
}
.card:hover, .bh-metric:hover {
  border-color: rgba(255, 255, 255, 0.12);
  background-color: #20242a;
}
list.boxed-list row {
  color: #e4e7eb;
}
list.boxed-list row .dim-label, list.boxed-list row .subtitle {
  color: #9aa0a6;
}
preferencesgroup > box > box > label.title {
  color: #ffffff;
}
preferencesgroup > box > box > label.subtitle {
  color: #9aa0a6;
}
searchentry, entry {
  background-color: #16181c;
  border-color: rgba(255, 255, 255, 0.08);
  color: #e4e7eb;
}
.bh-settings-banner {
  background: linear-gradient(135deg, rgba(13, 110, 253, 0.12) 0%, rgba(20, 23, 28, 0.8) 100%);
  border-color: rgba(13, 110, 253, 0.22);
}
.bh-settings-chip {
  background-color: rgba(255, 255, 255, 0.05);
  border-color: rgba(255, 255, 255, 0.08);
}
.bh-settings-chip:hover {
  background-color: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.15);
}
.bh-settings-chip-val {
  color: #ffffff;
}
.bh-settings-chip-lbl {
  color: #9aa0a6;
}
.bh-settings-shortcut-btn {
  background-color: rgba(255, 255, 255, 0.04);
  border-color: rgba(255, 255, 255, 0.08);
  color: #e4e7eb;
}
.bh-settings-shortcut-btn:hover {
  background-color: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.16);
}
.bh-settings-about-box {
  background-color: #16181c;
  border-color: rgba(255, 255, 255, 0.06);
}
.bh-metric .bh-metric-val {
  color: #ffffff;
}
.bh-metric .bh-metric-cap {
  color: #9aa0a6;
}
.bh-services-filter-bar {
  background-color: #16181c;
  border-color: rgba(255, 255, 255, 0.06);
}
.bh-filter-chip {
  color: #9aa0a6;
}
.bh-filter-chip:hover {
  background-color: rgba(255, 255, 255, 0.06);
  color: #ffffff;
}
.bh-button-group {
  background-color: #1a1d21;
  border-color: rgba(255, 255, 255, 0.08);
}
.bh-sidebar-badge {
  background-color: rgba(255, 255, 255, 0.08);
  color: #9aa0a6;
}
.bh-sidebar list.navigation-sidebar row:selected .bh-sidebar-badge {
  background-color: rgba(13, 110, 253, 0.2);
  color: #0d6efd;
}
.bh-metric-icon-box {
  background-color: rgba(255, 255, 255, 0.06);
  border-color: rgba(255, 255, 255, 0.08);
  color: #9aa0a6;
}
"""

LIGHT_THEME_CSS = """
@define-color window_bg_color #f6f8fa;
@define-color window_fg_color #1f2328;
@define-color view_bg_color #ffffff;
@define-color view_fg_color #1f2328;
@define-color headerbar_bg_color #ffffff;
@define-color headerbar_fg_color #1f2328;
@define-color card_bg_color #ffffff;
@define-color card_fg_color #1f2328;
@define-color card_border_color rgba(0, 0, 0, 0.08);

window.background, .background {
  background-color: #f6f8fa;
  color: #1f2328;
}
.bh-sidebar {
  background-color: #ffffff;
  border-right: 1px solid rgba(0, 0, 0, 0.08);
}
.bh-brand {
  color: #1f2328;
}
.bh-sidebar list.navigation-sidebar row {
  color: #57606a;
}
.bh-sidebar list.navigation-sidebar row:hover:not(:selected) {
  background-color: rgba(0, 0, 0, 0.04);
}
.card, .boxed-list, list.boxed-list, .bh-metric {
  background-color: #ffffff;
  color: #1f2328;
  border-color: rgba(0, 0, 0, 0.08);
}
.card:hover, .bh-metric:hover {
  border-color: rgba(13, 110, 253, 0.3);
  background-color: #fcfcfd;
}
list.boxed-list row {
  color: #1f2328;
}
list.boxed-list row .dim-label, list.boxed-list row .subtitle {
  color: #656d76;
}
preferencesgroup > box > box > label.title {
  color: #1f2328;
}
preferencesgroup > box > box > label.subtitle {
  color: #656d76;
}
searchentry, entry {
  background-color: #ffffff;
  border-color: rgba(0, 0, 0, 0.12);
  color: #1f2328;
}
.bh-settings-banner {
  background: linear-gradient(135deg, rgba(13, 110, 253, 0.08) 0%, rgba(240, 246, 255, 0.9) 100%);
  border-color: rgba(13, 110, 253, 0.2);
}
.bh-settings-chip {
  background-color: rgba(255, 255, 255, 0.9);
  border-color: rgba(13, 110, 253, 0.15);
}
.bh-settings-chip:hover {
  background-color: #ffffff;
  border-color: rgba(13, 110, 253, 0.3);
}
.bh-settings-chip-val {
  color: #1f2328;
}
.bh-settings-chip-lbl {
  color: #656d76;
}
.bh-settings-shortcut-btn {
  background-color: #ffffff;
  border-color: rgba(0, 0, 0, 0.1);
  color: #1f2328;
}
.bh-settings-shortcut-btn:hover {
  background-color: rgba(0, 0, 0, 0.04);
  border-color: rgba(0, 0, 0, 0.18);
}
.bh-settings-save-box {
  background-color: rgba(13, 110, 253, 0.08);
  border-color: rgba(13, 110, 253, 0.25);
}
.bh-settings-about-box {
  background-color: #ffffff;
  border-color: rgba(0, 0, 0, 0.08);
}
.bh-metric .bh-metric-val {
  color: #1f2328;
}
.bh-metric .bh-metric-cap {
  color: #656d76;
}
.bh-services-filter-bar {
  background-color: #ffffff;
  border-color: rgba(0, 0, 0, 0.08);
}
.bh-filter-chip {
  color: #656d76;
}
.bh-filter-chip:hover {
  background-color: rgba(0, 0, 0, 0.05);
  color: #1f2328;
}
.bh-button-group {
  background-color: #f0f2f5;
  border-color: rgba(0, 0, 0, 0.08);
}
.bh-button-group button {
  color: #1f2328;
}
headerbar .bh-button-group {
  background-color: rgba(0, 0, 0, 0.05);
  border-color: rgba(0, 0, 0, 0.08);
}
.bh-sidebar-badge {
  background-color: rgba(0, 0, 0, 0.06);
  color: #57606a;
}
.bh-sidebar list.navigation-sidebar row:selected .bh-sidebar-badge {
  background-color: rgba(13, 110, 253, 0.15);
  color: #0d6efd;
}
.bh-metric-icon-box {
  background-color: rgba(0, 0, 0, 0.04);
  border-color: rgba(0, 0, 0, 0.08);
  color: #57606a;
}
.bh-metric-icon-box.web {
  background-color: rgba(13, 110, 253, 0.1);
  border-color: rgba(13, 110, 253, 0.2);
  color: #0969da;
}
.bh-metric-icon-box.php {
  background-color: rgba(139, 92, 246, 0.1);
  border-color: rgba(139, 92, 246, 0.2);
  color: #8250df;
}
.bh-metric-icon-box.db {
  background-color: rgba(245, 158, 11, 0.12);
  border-color: rgba(245, 158, 11, 0.25);
  color: #b05a00;
}
.bh-metric-icon-box.stack {
  background-color: rgba(46, 194, 126, 0.12);
  border-color: rgba(46, 194, 126, 0.25);
  color: #1a7f37;
}
.bh-metric-icon-box.cpu {
  background-color: rgba(13, 110, 253, 0.1);
  border-color: rgba(13, 110, 253, 0.2);
  color: #0969da;
}
.bh-metric-icon-box.net {
  background-color: rgba(46, 194, 126, 0.12);
  border-color: rgba(46, 194, 126, 0.25);
  color: #1a7f37;
}
.bh-pill-on {
  background: rgba(46, 194, 126, 0.14);
  color: #1a7f37;
  border-color: rgba(46, 194, 126, 0.35);
}
.bh-quick-btn {
  color: #57606a;
}
.bh-quick-btn:hover {
  background: rgba(0, 0, 0, 0.06);
  color: #1f2328;
}
"""


def update_theme_provider(is_dark: bool) -> None:
    global _theme_provider
    display = Gdk.Display.get_default()
    if display is None:
        return
    if _theme_provider is None:
        _theme_provider = Gtk.CssProvider()
        Gtk.StyleContext.add_provider_for_display(
            display, _theme_provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION + 100
        )
    _theme_provider.load_from_string(DARK_THEME_CSS if is_dark else LIGHT_THEME_CSS)


def apply_theme(scheme_name: str) -> None:
    sm = Adw.StyleManager.get_default()
    if scheme_name == "dark":
        sm.set_color_scheme(Adw.ColorScheme.FORCE_DARK)
    elif scheme_name == "light":
        sm.set_color_scheme(Adw.ColorScheme.FORCE_LIGHT)
    else:
        sm.set_color_scheme(Adw.ColorScheme.DEFAULT)
    update_theme_provider(sm.get_dark())


class OmniServApp(Adw.Application):
    def __init__(self) -> None:
        super().__init__(application_id="com.emon.omniserv",
                         flags=Gio.ApplicationFlags.DEFAULT_FLAGS)
        self.engine = EngineClient()
        self._tray_proc = None
        # --background (login autostart): first activation keeps the window hidden so only
        # the tray icon appears. Cleared after the first activate, so a later tray "Open" /
        # second launch presents the window normally.
        self._bg_start = False
        # A hidden (never-presented or closed-to-tray) window does NOT keep the GLib main
        # loop alive — without an explicit hold the app exits as soon as activate returns.
        self._held = False

    def do_startup(self) -> None:
        Adw.Application.do_startup(self)
        GLib.set_application_name("OmniServ")
        self._load_css()
        sm = Adw.StyleManager.get_default()
        sm.connect("notify::dark", lambda s, _p: update_theme_provider(s.get_dark()))
        apply_theme(cfg_str("color_scheme", "system"))
        # "quit" action the tray helper invokes over D-Bus (org.freedesktop.Application).
        act = Gio.SimpleAction.new("quit", None)
        act.connect("activate", lambda *_: self._quit_all())
        self.add_action(act)
        self._start_tray()


    def do_activate(self) -> None:
        wins = self.get_windows()          # includes a hidden (closed-to-tray) window
        if wins:
            win = wins[0]
        else:
            win = MainWindow(self)
            win.connect("close-request", self._on_close)
        if self._bg_start and not wins:
            # Login autostart: stay hidden — the tray icon is the only UI. If the tray can't
            # appear (no AppIndicator extension), show the window instead of an invisible app.
            self._bg_start = False
            win.set_visible(False)
            self._hold()
            GLib.timeout_add_seconds(6, self._bg_fallback, win)
            return
        win.set_visible(True)
        win.present()
        self._unhold()

    def _bg_fallback(self, win) -> bool:
        if not self._tray_running() and not win.get_visible():
            win.set_visible(True)
            win.present()
            self._unhold()
        return False

    # ── keep-alive while no window is visible (background start / closed-to-tray) ──
    def _hold(self) -> None:
        if not self._held:
            self._held = True
            self.hold()

    def _unhold(self) -> None:
        if self._held:
            self._held = False
            self.release()

    # ── close-to-tray ──
    def _on_close(self, win) -> bool:
        # With a live tray icon, closing the window just hides it (OmniServ stays reachable);
        # otherwise fall through to the default (destroy → quit). Hold the app while hidden
        # so the main loop can't decide it has nothing left to do.
        if self._tray_running():
            win.set_visible(False)
            self._hold()
            return True
        return False

    def _tray_path(self):
        here = os.path.dirname(os.path.abspath(__file__))  # …/omniserv
        for c in (os.path.join(here, "..", "bin", "omniserv-tray"),
                  "/usr/lib/omniserv/app/bin/omniserv-tray"):
            if os.path.exists(c):
                return os.path.abspath(c)
        return shutil.which("omniserv-tray")

    def _start_tray(self) -> None:
        path = self._tray_path()
        if not path:
            return
        try:
            self._tray_proc = subprocess.Popen(["python3", path, str(os.getpid())])
        except Exception:  # noqa: BLE001
            self._tray_proc = None

    def _tray_running(self) -> bool:
        return self._tray_proc is not None and self._tray_proc.poll() is None

    def _quit_all(self) -> None:
        if self._tray_proc and self._tray_proc.poll() is None:
            try:
                self._tray_proc.terminate()
            except Exception:  # noqa: BLE001
                pass
        self.quit()

    def _load_css(self) -> None:
        css = os.path.join(os.path.dirname(os.path.abspath(__file__)), "style.css")
        if not os.path.exists(css):
            return
        provider = Gtk.CssProvider()
        provider.load_from_path(css)
        Gtk.StyleContext.add_provider_for_display(
            Gdk.Display.get_default(), provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION + 50
        )


def main() -> int:
    import sys
    app = OmniServApp()
    # --background/--tray: launched by the login autostart entry — start hidden-to-tray.
    app._bg_start = ("--background" in sys.argv) or ("--tray" in sys.argv)
    # Ensure the data dir exists BEFORE any GTK/window/api machinery — a fresh install is otherwise
    # "not initialized" (blank Services, nothing installable). Plain Python, idempotent, no root.
    try:
        app.engine.run("init", timeout=20)
    except Exception:  # noqa: BLE001
        pass
    return app.run([sys.argv[0]])   # strip our flags — GTK would reject unknown options
