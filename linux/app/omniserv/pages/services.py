"""Services pane: professional flat dashboard-style service management with overview cards,
inline category filters, and clean categorized service rows.
"""
from __future__ import annotations

import os

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, GLib, Gtk, Pango  # noqa: E402

from ..widgets import pill, status_dot
from ._shared import CardGrid, _open_text_editor, clean_version

SERVICE_GROUPS_DEF = [
    ("PHP Versions", "php"),
    ("Web Servers", "web"),
    ("Databases", "db"),
    ("Cache & In-Memory", "cache"),
    ("DNS / TLS / Mail", "dns tls mail"),
    ("Runtimes", "node python"),
]

FILTER_CATEGORIES = [
    ("all", "All"),
    ("php", "PHP"),
    ("web", "Web"),
    ("db", "Databases"),
    ("cache", "Cache"),
    ("tools", "Tools"),
]


class ServicesPage(Gtk.Box):
    def __init__(self, win) -> None:
        super().__init__(orientation=Gtk.Orientation.VERTICAL)
        self.win = win
        self._current_status = "installed"  # "installed", "running", "all"
        self._current_category = "all"
        self._search_query = ""
        self._last_services: list[dict] = []
        self._last_config: dict = {}
        self._sig = None

        self.scroller = Gtk.ScrolledWindow(vexpand=True, hexpand=True)
        self.scroller.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        self.clamp = Adw.Clamp(maximum_size=1150, tightening_threshold=800)
        self.body = Gtk.Box(
            orientation=Gtk.Orientation.VERTICAL,
            spacing=18,
            margin_top=18,
            margin_bottom=24,
            margin_start=18,
            margin_end=18,
        )
        self.clamp.set_child(self.body)
        self.scroller.set_child(self.clamp)
        self.append(self.scroller)

        # ── 1. In-Page Header: Title & Subtitle ──
        head = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
        tb = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, hexpand=True)
        tb.append(Gtk.Label(label="Services", xalign=0, css_classes=["title-1"]))
        self.subtitle = Gtk.Label(
            label="Manage web servers, runtimes, databases and background services",
            xalign=0,
            css_classes=["dim-label"],
        )
        tb.append(self.subtitle)
        head.append(tb)
        self.body.append(head)

        # ── 2. Top Summary Metric Cards (Enhanced with icons & hierarchy) ──
        cards = CardGrid()
        self.c_web = self._metric_card("Web Server", "web-browser-symbolic", "web")
        self.c_php = self._metric_card("PHP Engine", "application-x-addon-symbolic", "php")
        self.c_db = self._metric_card("Database", "drive-harddisk-symbolic", "db")
        self.c_stack = self._metric_card("Stack Status", "emblem-system-symbolic", "stack")
        for c in (
            self.c_web["card"],
            self.c_php["card"],
            self.c_db["card"],
            self.c_stack["card"],
        ):
            cards.add_card(c)
        self.body.append(cards)

        # ── 3. Unified Search & Two-Tier Filter Toolbar ──
        filter_container = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)

        # Top row: Search input + Status segmented switcher (Installed / Running / All)
        toolbar_top = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12, valign=Gtk.Align.CENTER)

        self.search_entry = Gtk.SearchEntry(
            placeholder_text="Search services (e.g. php, nginx, mariadb)…",
        )
        self.search_entry.set_size_request(280, -1)
        self.search_entry.set_hexpand(True)
        self.search_entry.connect("search-changed", self._on_search_changed)
        toolbar_top.append(self.search_entry)

        # Status Switcher
        status_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, css_classes=["bh-button-group", "linked"])
        self.status_installed_btn = Gtk.ToggleButton(label="Installed", active=True)
        self.status_running_btn = Gtk.ToggleButton(label="Running", active=False)
        self.status_all_btn = Gtk.ToggleButton(label="All", active=False)

        self.status_installed_btn.connect("toggled", lambda b: self._on_status_toggled("installed", b))
        self.status_running_btn.connect("toggled", lambda b: self._on_status_toggled("running", b))
        self.status_all_btn.connect("toggled", lambda b: self._on_status_toggled("all", b))

        for b in (self.status_installed_btn, self.status_running_btn, self.status_all_btn):
            status_box.append(b)
        toolbar_top.append(status_box)
        filter_container.append(toolbar_top)

        # Bottom row: Scrollable Category Filter Pills
        chips_scroll = Gtk.ScrolledWindow()
        chips_scroll.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.NEVER)
        chips_scroll.set_propagate_natural_width(True)

        self.chips_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
        self._cat_buttons: dict[str, Gtk.ToggleButton] = {}
        for c_key, c_label in FILTER_CATEGORIES:
            btn = Gtk.ToggleButton(
                label=c_label,
                active=(c_key == "all"),
                css_classes=["bh-filter-chip"],
            )
            btn.connect("toggled", lambda b, k=c_key: self._on_cat_toggled(k, b))
            self._cat_buttons[c_key] = btn
            self.chips_row.append(btn)
        chips_scroll.set_child(self.chips_row)
        filter_container.append(chips_scroll)

        self.body.append(filter_container)

        # ── 4. Services Sections Container ──
        self.list_container = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=18)
        self.body.append(self.list_container)

        # Empty status page
        self.empty_page = Adw.StatusPage(
            icon_name="system-search-symbolic",
            title="No matching services",
            description="Try switching to 'All' or clearing your search query.",
        )
        self.empty_page.set_visible(False)
        self.body.append(self.empty_page)

    # ── Metric Card Builder (With category icon and clear typography) ──
    def _metric_card(self, title: str, icon_name: str, icon_class: str = "") -> dict:
        card = Gtk.Box(
            orientation=Gtk.Orientation.VERTICAL,
            spacing=8,
            css_classes=["card", "bh-metric"],
        )
        top = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8, valign=Gtk.Align.CENTER)

        icon_box = Gtk.Box(
            css_classes=["bh-metric-icon-box", icon_class] if icon_class else ["bh-metric-icon-box"],
            valign=Gtk.Align.CENTER,
        )
        icon_img = Gtk.Image.new_from_icon_name(icon_name)
        icon_img.set_pixel_size(18)
        icon_box.append(icon_img)
        top.append(icon_box)

        top.append(
            Gtk.Label(
                label=title,
                xalign=0,
                hexpand=True,
                css_classes=["bh-metric-cap", "dim-label"],
            )
        )
        badge = pill("● Stopped", "bh-pill-off")
        top.append(badge)
        card.append(top)

        val = Gtk.Label(
            label="—",
            xalign=0,
            css_classes=["bh-metric-val"],
            wrap=True,
            wrap_mode=Pango.WrapMode.WORD_CHAR,
            max_width_chars=18,
        )
        sub = Gtk.Label(label="", xalign=0, css_classes=["dim-label", "caption"], wrap=True)
        card.append(val)
        card.append(sub)
        return {"card": card, "val": val, "sub": sub, "badge": badge}

    def _set_card_badge(self, badge: Gtk.Label, text: str, css: str) -> None:
        for c in ("bh-pill-on", "bh-pill-off", "bh-pill-warn", "bh-pill-blue"):
            badge.remove_css_class(c)
        badge.add_css_class(css)
        badge.set_label(text)

    # ── Filters & Search Handlers ──
    def _on_search_changed(self, entry: Gtk.SearchEntry) -> None:
        self._search_query = entry.get_text().strip().lower()
        self._render_services()

    def _on_status_toggled(self, key: str, button: Gtk.ToggleButton) -> None:
        if not button.get_active():
            if self._current_status == key:
                button.set_active(True)
            return

        self._current_status = key
        for k, b in (
            ("installed", self.status_installed_btn),
            ("running", self.status_running_btn),
            ("all", self.status_all_btn),
        ):
            if k != key and b.get_active():
                b.set_active(False)
        self._render_services()

    def _on_cat_toggled(self, key: str, button: Gtk.ToggleButton) -> None:
        if not button.get_active():
            if self._current_category == key:
                button.set_active(True)
            return

        self._current_category = key
        for k, b in self._cat_buttons.items():
            if k != key and b.get_active():
                b.set_active(False)
        self._render_services()

    def _matches_filter(self, s: dict) -> bool:
        if self._search_query:
            q = self._search_query
            key = s.get("key", "").lower()
            role = s.get("role", "").lower()
            formula = s.get("formula", "").lower()
            version = s.get("version", "").lower()
            if not (q in key or q in role or q in formula or q in version):
                return False

        # Status filter
        st = self._current_status
        if st == "installed" and not s.get("installed"):
            return False
        elif st == "running" and not s.get("running"):
            return False

        # Category filter
        cat = self._current_category
        if cat == "all":
            return True
        elif cat == "php":
            return s.get("role") == "php"
        elif cat == "web":
            return s.get("role") == "web"
        elif cat == "db":
            return s.get("role") == "db"
        elif cat == "cache":
            return s.get("role") == "cache"
        elif cat == "tools":
            return s.get("role") in ("dns", "tls", "mail", "node", "python")
        return True

    # ── Refresh & Rendering ──
    def refresh(self, data: dict) -> None:
        services = data.get("services", [])
        cfg = data.get("config", {})
        self._last_services = services
        self._last_config = cfg

        sig = (
            tuple(
                (
                    s["key"],
                    s["installed"],
                    s.get("running"),
                    s.get("enabled"),
                    s.get("version", ""),
                )
                for s in services
            ),
            cfg.get("default_php"),
            cfg.get("default_web"),
        )

        self._update_metrics(services, cfg)
        self._update_filter_counts(services)

        if sig == self._sig:
            return
        self._sig = sig
        self._render_services()

    def _update_metrics(self, services: list[dict], cfg: dict) -> None:
        installed_count = sum(1 for s in services if s.get("installed"))
        running_count = sum(1 for s in services if s.get("running"))
        total_count = len(services)

        def_php = cfg.get("default_php", "php@8.5")
        def_web = cfg.get("default_web", "nginx")
        self.subtitle.set_label(
            f"{running_count} of {installed_count} installed services active · Defaults: {def_php}, {def_web}"
        )

        # 1. Web Server Card
        web_running = [s for s in services if s.get("role") == "web" and s.get("running")]
        if web_running:
            w_svc = web_running[0]
            v_clean = clean_version(w_svc.get("version", ""))
            self.c_web["val"].set_label(w_svc["key"].title() + (f" {v_clean}" if v_clean else ""))
            self.c_web["sub"].set_label("Port 80, 443 · HTTP/HTTPS")
            self._set_card_badge(self.c_web["badge"], "● Running", "bh-pill-on")
        else:
            self.c_web["val"].set_label("Inactive")
            self.c_web["sub"].set_label("Default: " + def_web)
            self._set_card_badge(self.c_web["badge"], "● Stopped", "bh-pill-off")

        # 2. PHP Runtime Card
        php_running = [s for s in services if s.get("role") == "php" and s.get("running")]
        php_installed = [s for s in services if s.get("role") == "php" and s.get("installed")]
        if php_running:
            p_svc = php_running[0]
            v_clean = clean_version(p_svc.get("version", ""))
            self.c_php["val"].set_label(p_svc["key"] + (f" ({v_clean})" if v_clean else ""))
            self.c_php["sub"].set_label(f"{len(php_running)} active · {len(php_installed)} installed")
            self._set_card_badge(self.c_php["badge"], "● Active", "bh-pill-on")
        else:
            self.c_php["val"].set_label(def_php)
            self.c_php["sub"].set_label("No active FPM instance")
            self._set_card_badge(self.c_php["badge"], "● Stopped", "bh-pill-off")

        # 3. Database Server Card
        db_running = [s for s in services if s.get("role") == "db" and s.get("running")]
        if db_running:
            d_svc = db_running[0]
            v_clean = clean_version(d_svc.get("version", ""))
            self.c_db["val"].set_label(d_svc["key"].upper() + (f" {v_clean}" if v_clean else ""))
            self.c_db["sub"].set_label("Port 3306 · Default database")
            self._set_card_badge(self.c_db["badge"], "● Running", "bh-pill-on")
        else:
            self.c_db["val"].set_label("None active")
            self.c_db["sub"].set_label("MariaDB / MySQL / PostgreSQL")
            self._set_card_badge(self.c_db["badge"], "● Stopped", "bh-pill-off")

        # 4. Overall Stack Status Card
        if running_count > 0:
            self.c_stack["val"].set_label(f"{running_count} Active")
            self.c_stack["sub"].set_label(f"{installed_count} of {total_count} services installed")
            self._set_card_badge(self.c_stack["badge"], "● Live", "bh-pill-on")
        else:
            self.c_stack["val"].set_label("All Stopped")
            self.c_stack["sub"].set_label(f"{installed_count} installed available")
            self._set_card_badge(self.c_stack["badge"], "● Offline", "bh-pill-off")

    def _update_filter_counts(self, services: list[dict]) -> None:
        inst_cnt = sum(1 for s in services if s.get("installed"))
        run_cnt = sum(1 for s in services if s.get("running"))
        tot_cnt = len(services)

        self.status_installed_btn.set_label(f"Installed ({inst_cnt})")
        self.status_running_btn.set_label(f"Running ({run_cnt})")
        self.status_all_btn.set_label(f"All ({tot_cnt})")

        cat_counts = {
            "all": tot_cnt,
            "php": sum(1 for s in services if s.get("role") == "php"),
            "web": sum(1 for s in services if s.get("role") == "web"),
            "db": sum(1 for s in services if s.get("role") == "db"),
            "cache": sum(1 for s in services if s.get("role") == "cache"),
            "tools": sum(1 for s in services if s.get("role") in ("dns", "tls", "mail", "node", "python")),
        }
        for k, btn in self._cat_buttons.items():
            base_label = next(lbl for key, lbl in FILTER_CATEGORIES if key == k)
            count = cat_counts.get(k, 0)
            btn.set_label(f"{base_label} ({count})")

    def _render_services(self) -> None:
        adj = self.scroller.get_vadjustment()
        pos = adj.get_value() if adj else 0.0

        child = self.list_container.get_first_child()
        while child:
            nxt = child.get_next_sibling()
            self.list_container.remove(child)
            child = nxt

        total_visible_rows = 0
        cfg = self._last_config
        def_php = cfg.get("default_php", "")
        def_web = cfg.get("default_web", "")

        for title, roles in SERVICE_GROUPS_DEF:
            group_svcs = [
                s
                for s in self._last_services
                if s.get("role") in roles.split() and self._matches_filter(s)
            ]
            if not group_svcs:
                continue

            # Sort: installed services first, then uninstalled
            group_svcs.sort(key=lambda x: (not x.get("installed", False), not x.get("running", False), x.get("key", "")))
            total_visible_rows += len(group_svcs)

            # Section Header
            grp_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            hdr_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)

            lbl = Gtk.Label(label=title, xalign=0, css_classes=["title-4"])
            hdr_box.append(lbl)

            running_in_grp = sum(1 for s in group_svcs if s.get("running"))
            if running_in_grp > 0:
                hdr_box.append(pill(f"{running_in_grp} active", "bh-pill-on"))

            hdr_box.append(Gtk.Box(hexpand=True))
            hdr_box.append(
                Gtk.Label(
                    label=f"{len(group_svcs)} items",
                    css_classes=["dim-label", "caption"],
                )
            )
            grp_box.append(hdr_box)

            # Boxed List
            listbox = Gtk.ListBox(
                selection_mode=Gtk.SelectionMode.NONE,
                css_classes=["boxed-list"],
            )
            for s in group_svcs:
                listbox.append(self._build_service_row(s, def_php, def_web))
            grp_box.append(listbox)

            self.list_container.append(grp_box)

        has_visible = total_visible_rows > 0
        self.list_container.set_visible(has_visible)
        self.empty_page.set_visible(not has_visible)

        if adj and pos:
            GLib.idle_add(lambda a=adj, p=pos: (a.set_value(p), False)[1])

    # ── Flat, Clean Service Row with Grouped Action Menu ──
    def _build_service_row(self, s: dict, def_php: str, def_web: str) -> Adw.ActionRow:
        key = s["key"]
        role = s.get("role", "")
        installed = s.get("installed", False)
        running = s.get("running", False)
        enabled = s.get("enabled", False)
        version_raw = s.get("version", "")
        ver_clean = clean_version(version_raw)
        formula = s.get("formula", "")

        row = Adw.ActionRow(title=key)
        row.add_css_class("bh-service-row")

        sub_parts = []
        if ver_clean:
            sub_parts.append(f"v{ver_clean}")
        if formula and formula != key:
            sub_parts.append(formula)
        if role == "web":
            sub_parts.append("Port 80, 443")
        elif key in ("mysql", "mariadb"):
            sub_parts.append("Port 3306")
        elif key.startswith("postgresql"):
            sub_parts.append("Port 5432")
        elif key == "redis":
            sub_parts.append("Port 6379")
        elif key == "mailpit":
            sub_parts.append("Port 8025 / 1025")

        row.set_subtitle(" · ".join(sub_parts) if sub_parts else (formula or role.upper()))

        # Status Dot
        row.add_prefix(status_dot(running if installed else False))

        # Suffix: Badges + Clean Action Group
        suffix = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6, valign=Gtk.Align.CENTER)

        # Default badges
        if key == def_php and role == "php":
            suffix.append(pill("Default PHP", "bh-pill-blue"))
        elif key == def_web and role == "web":
            suffix.append(pill("Default Web", "bh-pill-blue"))

        # Status pill
        if installed:
            suffix.append(pill("● Running", "bh-pill-on") if running else pill("● Stopped", "bh-pill-off"))
        else:
            suffix.append(pill("Not installed", "bh-pill-off"))

        if not installed:
            install_btn = Gtk.Button(
                css_classes=["suggested-action"],
                valign=Gtk.Align.CENTER,
            )
            install_btn.set_child(Adw.ButtonContent(icon_name="list-add-symbolic", label="Install"))
            install_btn.connect(
                "clicked",
                lambda *_, k=key: self.win.run_progress(
                    ["install", k],
                    f"Installing {k}",
                    f"Downloading and setting up {k}…",
                    f"{k} installed.",
                ),
            )
            suffix.append(install_btn)
        else:
            actions_group = Gtk.Box(
                orientation=Gtk.Orientation.HORIZONTAL, spacing=4, margin_start=4, valign=Gtk.Align.CENTER
            )

            # 1. Start / Stop Button
            if role in ("php", "web", "db", "cache", "mail"):
                if running:
                    stop_b = Gtk.Button(
                        icon_name="media-playback-stop-symbolic",
                        tooltip_text=f"Stop {key}",
                        css_classes=["bh-quick-btn", "flat"],
                    )
                    stop_b.connect(
                        "clicked",
                        lambda *_, k=key: self.win.run_verb(["stop", k], f"Stopping {key}…"),
                    )
                    actions_group.append(stop_b)
                else:
                    start_b = Gtk.Button(
                        icon_name="media-playback-start-symbolic",
                        tooltip_text=f"Start {key}",
                        css_classes=["bh-quick-btn", "flat"],
                    )
                    start_b.connect(
                        "clicked",
                        lambda *_, k=key: self.win.run_verb(["start", k], f"Starting {key}…"),
                    )
                    actions_group.append(start_b)

                restart_b = Gtk.Button(
                    icon_name="view-refresh-symbolic",
                    tooltip_text=f"Restart {key}",
                    css_classes=["bh-quick-btn", "flat"],
                )
                restart_b.connect(
                    "clicked",
                    lambda *_, k=key: self.win.run_verb(["restart", k], f"Restarting {key}…"),
                )
                actions_group.append(restart_b)

            # 2. Auto-start Star Toggle
            star = Gtk.ToggleButton(
                icon_name="starred-symbolic",
                tooltip_text="Auto-start on boot",
                active=enabled,
                css_classes=["bh-star", "bh-quick-btn", "flat"],
            )
            star.connect(
                "toggled",
                lambda btn, k=key: self.win.run_verb(
                    ["enable" if btn.get_active() else "disable", k], None, refresh=True
                ),
            )
            actions_group.append(star)

            # 3. Contextual More Actions Menu Button (...)
            more_btn = self._build_more_menu(s)
            actions_group.append(more_btn)

            suffix.append(actions_group)

        row.add_suffix(suffix)
        return row

    def _build_more_menu(self, s: dict) -> Gtk.MenuButton:
        key = s["key"]
        role = s.get("role", "")
        running = s.get("running", False)

        btn = Gtk.MenuButton(
            icon_name="view-more-symbolic",
            tooltip_text="More actions",
            css_classes=["bh-quick-btn", "flat"],
        )
        popover = Gtk.Popover()
        menu_box = Gtk.Box(
            orientation=Gtk.Orientation.VERTICAL,
            spacing=4,
            margin_top=6,
            margin_bottom=6,
            margin_start=6,
            margin_end=6,
        )

        if role == "php":
            b_ini = Gtk.Button(css_classes=["flat"])
            b_ini.set_child(Adw.ButtonContent(icon_name="document-edit-symbolic", label="Edit php.ini"))
            b_ini.connect("clicked", lambda *_, k=key: (popover.popdown(), self._edit_ini(k)))
            menu_box.append(b_ini)
        elif role == "web":
            b_conf = Gtk.Button(css_classes=["flat"])
            b_conf.set_child(Adw.ButtonContent(icon_name="document-edit-symbolic", label="Edit Server Config"))
            b_conf.connect("clicked", lambda *_, k=key: (popover.popdown(), self._open_web_config(k)))
            menu_box.append(b_conf)
        elif role == "db" and key in ("mariadb", "mysql") and running:
            b_pwd = Gtk.Button(css_classes=["flat"])
            b_pwd.set_child(Adw.ButtonContent(icon_name="dialog-password-symbolic", label="Set Root Password"))
            b_pwd.connect("clicked", lambda *_: (popover.popdown(), self.win.db_root_dialog()))
            menu_box.append(b_pwd)

        # Update Action
        b_upd = Gtk.Button(css_classes=["flat"])
        b_upd.set_child(Adw.ButtonContent(icon_name="software-update-available-symbolic", label=f"Update {key}"))
        b_upd.connect(
            "clicked",
            lambda *_, k=key: (
                popover.popdown(),
                self.win.run_verb(["update", k], f"Updating {key}…"),
            ),
        )
        menu_box.append(b_upd)

        # Separator before destructive action
        menu_box.append(Gtk.Separator(orientation=Gtk.Orientation.HORIZONTAL, margin_top=2, margin_bottom=2))

        # Uninstall Action
        b_rm = Gtk.Button(css_classes=["flat", "destructive-action"])
        b_rm.set_child(Adw.ButtonContent(icon_name="user-trash-symbolic", label="Uninstall Service"))
        b_rm.connect(
            "clicked",
            lambda *_, k=key: (
                popover.popdown(),
                self.win.confirm(
                    f"Uninstall {key}?",
                    "The service binary is removed; your data and configs stay.",
                    lambda: self.win.run_verb(["uninstall", k], f"Uninstalling {key}…"),
                ),
            ),
        )
        menu_box.append(b_rm)

        popover.set_child(menu_box)
        btn.set_popover(popover)
        return btn

    # ── Config Editors ──
    def _edit_ini(self, key: str) -> None:
        rc, out = self.win.engine.run("php", "ini", "path", key.replace("php@", ""))
        path = out.strip().splitlines()[-1].strip() if out.strip() else ""
        if path and os.path.exists(path):
            _open_text_editor(path)
        else:
            self.win.toast(f"Couldn't resolve php.ini path for {key}")

    def _open_web_config(self, key: str) -> None:
        conf_candidates = [
            os.path.expanduser(f"~/.omniserv/{key}/{key}.conf"),
            os.path.expanduser(
                f"~/.omniserv/{'apache' if key == 'httpd' else key}/{'apache2' if key == 'httpd' else 'nginx'}.conf"
            ),
            f"/etc/{'apache2' if key == 'httpd' else key}/{'apache2' if key == 'httpd' else 'nginx'}.conf",
        ]
        for c in conf_candidates:
            if os.path.isfile(c):
                _open_text_editor(c)
                return
        self.win.toast(f"Configuration file for {key} not found")
