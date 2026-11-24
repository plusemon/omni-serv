#!/bin/bash
# OmniServ for Linux — build the .rpm package for Fedora, RHEL, and derivatives.
# Run on Fedora / RHEL (or Ubuntu with rpm installed): ./linux/build-rpm.sh [version]
# Produces: linux/dist/omniserv-<version>-1.noarch.rpm
set -euo pipefail

VERSION="${1:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ -z "$VERSION" ]; then
  VERSION="$(grep -oE '__version__ *= *"[^"]+"' "$ROOT/linux/app/omniserv/__init__.py" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)"
  VERSION="${VERSION:-1.0.0}"
fi
echo "▶ Building OmniServ for Linux (RPM) $VERSION"

if ! command -v rpmbuild >/dev/null 2>&1; then
  echo "Error: 'rpmbuild' is required to build RPM packages." >&2
  echo "Please install it with:" >&2
  echo "  Fedora / RHEL:   sudo dnf install -y rpm-build" >&2
  echo "  Ubuntu / Debian: sudo apt-get install -y rpm" >&2
  exit 1
fi

DIST="$ROOT/linux/dist"; mkdir -p "$DIST"
STAGE="$(mktemp -d)"; trap 'rm -rf "$STAGE"' EXIT
TOPDIR="$STAGE/rpmbuild"
mkdir -p "$TOPDIR"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}
PKG="$STAGE/pkg"

mkdir -p "$PKG/usr/lib/omniserv/engine" \
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

# ── icons: install the committed hicolor sizes ──
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

# ── RPM spec file ──
SPEC="$TOPDIR/SPECS/omniserv.spec"
cat > "$SPEC" <<EOF
Name:           omniserv
Version:        $VERSION
Release:        1
Summary:        Free local web server (nginx/PHP/MariaDB) with a GTK control panel
License:        MIT
URL:            https://emon.bd
BuildArch:      noarch

AutoReqProv:    no
%define _build_id_links none
%define debug_package %{nil}
%define _source_date_epoch_from_changelog 0

Requires:       bash, python3, python3-gobject, gtk4, libadwaita, curl, unzip, glib2
Recommends:     nss-tools, polkit

%description
OmniServ is a self-controlled local development server for Linux — a clean
alternative to XAMPP. It runs nginx/Apache, multiple PHP versions side by side,
MariaDB / MySQL / PostgreSQL, Redis, and managed Node + Python apps, with trusted
*.test HTTPS (mkcert) and one-click WordPress / PHP sites.

%install
cp -a "$PKG"/* %{buildroot}/

%files
%defattr(-,root,root,-)
/usr/lib/omniserv
/usr/bin/omniserv
/usr/bin/omniserv-gui
/usr/bin/omniserv-tray
/usr/share/applications/com.emon.omniserv.desktop
/usr/share/icons/hicolor/*/apps/com.emon.omniserv.png

%post
/usr/bin/update-desktop-database &>/dev/null || :
/usr/bin/gtk-update-icon-cache -q -f /usr/share/icons/hicolor &>/dev/null || :

# Clean up obsolete bhserve paths
if [ -d "/usr/local/lib/bhserve" ] && [ ! -d "/usr/local/lib/omniserv" ]; then
  mv "/usr/local/lib/bhserve" "/usr/local/lib/omniserv" 2>/dev/null || :
fi

# Clean up stale symlinks
for l in /usr/sbin/php-fpm*; do
  if [ -L "\$l" ]; then
    t="\$(readlink "\$l" 2>/dev/null || true)"
    case "\$t" in
      */bhserve/php/*)
        nt="\${t/bhserve/omniserv}"
        if [ -x "\$nt" ]; then ln -sf "\$nt" "\$l" 2>/dev/null || true
        elif [ ! -e "\$l" ]; then rm -f "\$l" 2>/dev/null || true; fi ;;
      *)
        if [ ! -e "\$l" ]; then rm -f "\$l" 2>/dev/null || true; fi ;;
    esac
  fi
done

%postun
/usr/bin/update-desktop-database &>/dev/null || :
/usr/bin/gtk-update-icon-cache -q -f /usr/share/icons/hicolor &>/dev/null || :

%changelog
* $(date +"%a %b %d %Y") Emon Khan <contact@emon.bd> - $VERSION-1
- Release package for Linux
EOF

# ── build ──
rpmbuild --define "_topdir $TOPDIR" \
         --define "_binaries_in_noarch_packages_terminate_build 0" \
         -bb "$SPEC" >/dev/null

BUILT_RPM="$(find "$TOPDIR/RPMS" -type f -name "omniserv*.rpm" | head -1)"
if [ -z "$BUILT_RPM" ]; then
  echo "Error: rpmbuild failed to produce an RPM package." >&2
  exit 1
fi

OUT="$DIST/omniserv-${VERSION}-1.noarch.rpm"
cp "$BUILT_RPM" "$OUT"

echo "✓ $OUT"
ls -la "$OUT"
echo
echo "Install / upgrade:"
echo "  sudo dnf install $OUT"
