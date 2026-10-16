#!/bin/sh
set -e

APP_ID="io.github.ta3map.PintaClassicWorkflow"
PACKAGE="pinta-classic-workflow"
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(sed -n 's/^AC_INIT(\[pinta\], \[\(.*\)\])$/\1/p' "${ROOT_DIR}/configure.ac")"
WORK_DIR="${ROOT_DIR}/release-deb"
PKG_DIR="${WORK_DIR}/${PACKAGE}"
OPT_DIR="${PKG_DIR}/opt/${PACKAGE}"
PUBLISH_DIR="${OPT_DIR}/lib/pinta"
OUTPUT_FILE="${ROOT_DIR}/${PACKAGE}_${VERSION}_amd64.deb"

rm -rf "${WORK_DIR}" "${OUTPUT_FILE}"
mkdir -p "${OPT_DIR}/share" "${PKG_DIR}/usr/bin" "${PKG_DIR}/usr/share/applications" "${PKG_DIR}/usr/share/metainfo" "${PKG_DIR}/usr/share/icons/hicolor/scalable/apps" "${PKG_DIR}/DEBIAN"

dotnet publish "${ROOT_DIR}/Pinta/Pinta.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:BuildTranslations=true \
  -p:PublishDir="${PUBLISH_DIR}"

rm -f "${PUBLISH_DIR}"/*.pdb
mv "${PUBLISH_DIR}/icons" "${PUBLISH_DIR}/locale" "${OPT_DIR}/share/"
ln -s "/opt/${PACKAGE}/lib/pinta/Pinta" "${PKG_DIR}/usr/bin/${PACKAGE}"

sed -e 's/^_//' -e "s/^Exec=.*/Exec=${PACKAGE} %F/" -e "s/^TryExec=.*/TryExec=${PACKAGE}/" \
  "${ROOT_DIR}/xdg/${APP_ID}.desktop.in" > "${PKG_DIR}/usr/share/applications/${APP_ID}.desktop"
sed -e 's/<_/</g' -e 's/<\/_/<\//g' \
  "${ROOT_DIR}/xdg/${APP_ID}.metainfo.xml.in" > "${PKG_DIR}/usr/share/metainfo/${APP_ID}.metainfo.xml"
cp "${ROOT_DIR}/Pinta.Resources/icons/hicolor/scalable/apps/${APP_ID}.svg" "${PKG_DIR}/usr/share/icons/hicolor/scalable/apps/"

cat > "${PKG_DIR}/DEBIAN/control" <<EOF
Package: ${PACKAGE}
Version: ${VERSION}
Architecture: amd64
Maintainer: ta3map <https://github.com/ta3map/Pinta-Classic-Workflow>
Depends: libgtk-4-1 (>= 4.18), libadwaita-1-0 (>= 1.7), libicu76 | libicu74 | libicu72
Recommends: webp-pixbuf-loader
Section: graphics
Priority: optional
Homepage: https://github.com/ta3map/Pinta-Classic-Workflow
Description: Image editor that looks and behaves like Paint.NET
 Pinta Classic Workflow is a fork of Pinta that brings its look and
 behavior as close as possible to Paint.NET.
EOF

dpkg-deb --root-owner-group --build "${PKG_DIR}" "${OUTPUT_FILE}"

echo "Built ${OUTPUT_FILE}"
