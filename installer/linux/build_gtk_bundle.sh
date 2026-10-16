#!/bin/sh
set -e

GTK_VERSION="4.18.6"
PANGO_VERSION="1.56.4"
ADW_VERSION="1.7.12"
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
WORK_DIR="${ROOT_DIR}/gtk-build"
PREFIX="${WORK_DIR}/prefix"
BUNDLE_DIR="${ROOT_DIR}/gtk-bundle"

rm -rf "${WORK_DIR}" "${BUNDLE_DIR}"
mkdir -p "${WORK_DIR}" "${BUNDLE_DIR}/lib" "${BUNDLE_DIR}/share/glib-2.0/schemas"
cd "${WORK_DIR}"

export LD_LIBRARY_PATH="${PREFIX}/lib"
export PKG_CONFIG_PATH="${PREFIX}/lib/pkgconfig:${PREFIX}/share/pkgconfig"

git clone --depth 1 -b "${GTK_VERSION}" https://gitlab.gnome.org/GNOME/gtk.git
git clone --depth 1 -b "${PANGO_VERSION}" https://gitlab.gnome.org/GNOME/pango.git gtk/subprojects/pango
git clone --depth 1 -b "${ADW_VERSION}" https://gitlab.gnome.org/GNOME/libadwaita.git

meson setup gtk/_build gtk --prefix="${PREFIX}" --libdir=lib --buildtype=release \
  -Dmedia-gstreamer=disabled -Dprint-cpdb=disabled -Dprint-cups=disabled \
  -Dvulkan=disabled -Dcloudproviders=disabled -Dsysprof=disabled -Dtracker=disabled \
  -Dcolord=disabled -Daccesskit=disabled -Dintrospection=disabled \
  -Ddocumentation=false -Dman-pages=false -Dbuild-demos=false \
  -Dbuild-testsuite=false -Dbuild-examples=false -Dbuild-tests=false
meson install -C gtk/_build

meson setup libadwaita/_build libadwaita --prefix="${PREFIX}" --libdir=lib --buildtype=release \
  -Dintrospection=disabled -Dvapi=false -Dtests=false -Dexamples=false -Dgtk_doc=false
meson install -C libadwaita/_build

cp -a "${PREFIX}"/lib/*.so* "${BUNDLE_DIR}/lib/"
strip --strip-unneeded "${BUNDLE_DIR}"/lib/*.so.*.*
cp "${PREFIX}"/share/glib-2.0/schemas/*.xml "${BUNDLE_DIR}/share/glib-2.0/schemas/"
glib-compile-schemas "${BUNDLE_DIR}/share/glib-2.0/schemas"

echo "Built ${BUNDLE_DIR}"
