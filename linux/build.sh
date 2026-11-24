#!/bin/bash
# OmniServ for Linux — build the package (.deb on Debian/Ubuntu, .rpm on Fedora/RHEL).
# Run: ./linux/build.sh [version] [--deb | --rpm | --all]
# Produces: linux/dist/omniserv_<version>_all.deb and/or linux/dist/omniserv-<version>-1.noarch.rpm
set -euo pipefail

VERSION=""
TARGET=""
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

for arg in "$@"; do
  case "$arg" in
    --deb) TARGET="deb" ;;
    --rpm) TARGET="rpm" ;;
    --all) TARGET="all" ;;
    -*) echo "Unknown option: $arg" >&2; exit 1 ;;
    *) [ -z "$VERSION" ] && VERSION="$arg" ;;
  esac
done

if [ -z "$VERSION" ]; then
  VERSION="$(grep -oE '__version__ *= *"[^"]+"' "$ROOT/linux/app/omniserv/__init__.py" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)"
  VERSION="${VERSION:-1.0.0}"
fi

# Auto-detect target format if not explicitly requested
if [ -z "$TARGET" ]; then
  if grep -qiE 'fedora|rhel|centos|rocky|almalinux' /etc/os-release 2>/dev/null; then
    TARGET="rpm"
  elif command -v dpkg-deb >/dev/null 2>&1; then
    TARGET="deb"
  elif command -v rpmbuild >/dev/null 2>&1; then
    TARGET="rpm"
  else
    TARGET="deb"
  fi
fi

if [ "$TARGET" = "rpm" ]; then
  exec "$ROOT/linux/build-rpm.sh" "$VERSION"
fi

if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "Error: 'dpkg-deb' is required to build Debian packages." >&2
  echo "Please install it with:" >&2
  echo "  Ubuntu / Debian: sudo apt-get install -y dpkg-dev" >&2
  echo "  Fedora / RHEL:   sudo dnf install -y dpkg" >&2
  exit 1
fi

echo "▶ Building OmniServ for Linux (Debian) $VERSION"

DIST="$ROOT/linux/dist"; mkdir -p "$DIST"
STAGE="$(mktemp -d)"; trap 'rm -rf "$STAGE"' EXIT
PKG="$STAGE/omniserv"

mkdir -p "$PKG/DEBIAN" \
         "$PKG/usr/lib/omniserv/engine" \
         "$PKG/usr/lib/omniserv/app" \
         "$PKG/usr/bin" \
         "$PKG/usr/share/applications" \
         "$PKG/usr/share/icons/hicolor/256x256/apps"

# ── engine (strip CR in case it was checked out on Windows) ──
for f in omniserv platform-linux.sh; do
  tr -d '\r' < "$ROOT/engine/$f" > "$PKG/usr/lib/omniserv/engine/$f"
done
chmod 0755 "$PKG/usr/lib/omniserv/engine/omniserv"

# ── GTK app (python package + launcher), CR-stripped ──
mkdir -p "$PKG/usr/lib/omniserv/app/omniserv" "$PKG/usr/lib/omniserv/app/bin"
(
  cd "$ROOT/linux/app/omniserv"
  find . -type f \( -name "*.py" -o -name "*.css" \) ! -path "*/__pycache__/*" | while read -r f; do
    dest="$PKG/usr/lib/omniserv/app/omniserv/$f"
    mkdir -p "$(dirname "$dest")"
    tr -d '\r' < "$f" > "$dest"
  done
)
for b in omniserv-gui omniserv-tray; do
  tr -d '\r' < "$ROOT/linux/app/bin/$b" > "$PKG/usr/lib/omniserv/app/bin/$b"
  chmod 0755 "$PKG/usr/lib/omniserv/app/bin/$b"
done

# ── CLI + GUI on PATH (symlinks; engine resolves its real dir via readlink -f) ──
ln -sf /usr/lib/omniserv/engine/omniserv   "$PKG/usr/bin/omniserv"
ln -sf /usr/lib/omniserv/app/bin/omniserv-gui "$PKG/usr/bin/omniserv-gui"
ln -sf /usr/lib/omniserv/app/bin/omniserv-tray "$PKG/usr/bin/omniserv-tray"

# ── icons: install the committed hicolor sizes (the same brand icon as Windows/macOS,
#    extracted from macos/icon/AppIcon.ico), named after the app-id so GNOME shell / the
#    taskbar / alt-tab / the in-app About all show it. ──
APPID="com.emon.omniserv"
ICONSRC="$ROOT/linux/packaging/icons"
icon_done=0
for s in 16 32 48 64 128 256 512; do
  if [ -f "$ICONSRC/$s.png" ]; then
    d="$PKG/usr/share/icons/hicolor/${s}x${s}/apps"; mkdir -p "$d"
    cp "$ICONSRC/$s.png" "$d/$APPID.png"; icon_done=1
  fi
done
if [ "$icon_done" = 0 ] && command -v convert >/dev/null && [ -f "$ROOT/macos/icon/AppIcon.ico" ]; then
  d="$PKG/usr/share/icons/hicolor/256x256/apps"; mkdir -p "$d"
  convert "$ROOT/macos/icon/AppIcon.ico" -resize 256x256 "$d/$APPID.png" 2>/dev/null && icon_done=1 || true
fi
[ "$icon_done" = 1 ] || echo "  ! no icon — add sizes to linux/packaging/icons/ or install imagemagick"

# ── desktop entry (named after the app-id so the icon binds to the window) ──
cat > "$PKG/usr/share/applications/$APPID.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=OmniServ
GenericName=Local Web Server
Comment=Free local web server: nginx/PHP/MariaDB, multi-PHP, *.test HTTPS, WordPress
Exec=omniserv-gui
Icon=com.emon.omniserv
Terminal=false
StartupWMClass=com.emon.omniserv
Categories=Development;WebDevelopment;
Keywords=php;nginx;apache;mariadb;mysql;postgresql;wordpress;localhost;server;laravel;
StartupNotify=true
DESKTOP

# ── control + maintainer scripts ──
cat > "$PKG/DEBIAN/control" <<EOF
Package: omniserv
Version: $VERSION
Section: web
Priority: optional
Architecture: all
Depends: bash, python3, python3-gi, python3-gi-cairo, gir1.2-gtk-4.0, gir1.2-adw-1, curl, unzip, libglib2.0-bin
Recommends: libnss3-tools, policykit-1, software-properties-common, gir1.2-gtk-3.0, gir1.2-ayatanaappindicator3-0.1
Maintainer: Emon Khan <bdemon00@gmail.com>
Homepage: https://emon.bd
Description: Free local web server (nginx/PHP/MariaDB) with a GTK control panel
 OmniServ is a self-controlled local development server for Ubuntu/Debian — a clean
 alternative to XAMPP. It runs nginx/Apache, multiple PHP versions side by side,
 MariaDB / MySQL / PostgreSQL, Redis, and managed Node + Python apps, with trusted
 *.test HTTPS (mkcert) and one-click WordPress / PHP sites.
 .
 The servers themselves are installed on demand via apt (Ondrej PHP repo for PHP);
 this package provides the engine and the GTK4/libadwaita control panel.
EOF

cat > "$PKG/DEBIAN/postinst" <<'POST'
#!/bin/bash
set -e
update-desktop-database -q 2>/dev/null || true
gtk-update-icon-cache -q -f /usr/share/icons/hicolor 2>/dev/null || true
# Self-heal: OmniServ ≤1.0.47 ran the "Start at login" toggle with root privileges, which
# could leave root-owned files under the user's ~/.config/systemd. The toggle now runs
# unprivileged (systemctl --user must run as the desktop user) and can't overwrite them
# → "Permission denied". Chown that tree back to the installing user — it is always meant
# to be user-owned.
if [ -n "$SUDO_USER" ] && [ "$SUDO_USER" != root ]; then
  u_home="$(getent passwd "$SUDO_USER" | cut -d: -f6)"
  if [ -n "$u_home" ] && [ -d "$u_home/.config/systemd" ]; then
    chown -R "$SUDO_USER":"$SUDO_USER" "$u_home/.config/systemd" 2>/dev/null || true
  fi
  # Retire the ≤1.0.49 per-user start-at-login unit — replaced in 1.0.50 by a SYSTEM unit
  # (the user unit could never start the services: they need root, so it just prompted at
  # login and did nothing). Bus-free removal always works; user-bus disable is best-effort.
  if [ -n "$u_home" ] && [ -f "$u_home/.config/systemd/user/omniserv.service" ]; then
    XDG_RUNTIME_DIR="/run/user/$(id -u "$SUDO_USER")" runuser -u "$SUDO_USER" -- \
      systemctl --user disable omniserv.service 2>/dev/null || true
    rm -f "$u_home/.config/systemd/user/omniserv.service" \
          "$u_home/.config/systemd/user/default.target.wants/omniserv.service" 2>/dev/null || true
  fi
fi

# Clean up obsolete bhserve paths (renamed to omniserv) and repair/remove stale symlinks
if [ -d "/usr/local/lib/bhserve" ] && [ ! -d "/usr/local/lib/omniserv" ]; then
  mv "/usr/local/lib/bhserve" "/usr/local/lib/omniserv" 2>/dev/null || true
fi
for l in /usr/sbin/php-fpm*; do
  if [ -L "$l" ]; then
    t="$(readlink "$l" 2>/dev/null || true)"
    case "$t" in
      */bhserve/php/*)
        nt="${t/bhserve/omniserv}"
        if [ -x "$nt" ]; then ln -sf "$nt" "$l" 2>/dev/null || true
        elif [ ! -e "$l" ]; then rm -f "$l" 2>/dev/null || true; fi ;;
      *)
        if [ ! -e "$l" ]; then rm -f "$l" 2>/dev/null || true; fi ;;
    esac
  fi
done
exit 0
POST
chmod 0755 "$PKG/DEBIAN/postinst"

# ── build ──
OUT="$DIST/omniserv_${VERSION}_all.deb"
dpkg-deb --root-owner-group --build "$PKG" "$OUT" >/dev/null
echo "✓ $OUT"
ls -la "$OUT"
echo
echo "Install / upgrade (use dpkg — 'apt install ./file.deb' fails on apt 2.9+):"
echo "  sudo dpkg -i $OUT"
echo "  sudo apt-get -f install -y      # first install only: pulls in deps"

if [ "$TARGET" = "all" ]; then
  echo
  "$ROOT/linux/build-rpm.sh" "$VERSION"
fi
