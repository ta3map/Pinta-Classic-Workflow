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
GTK_DIR="${OPT_DIR}/lib/gtk"
OUTPUT_FILE="${ROOT_DIR}/${PACKAGE}_${VERSION}_amd64.deb"

rm -rf "${WORK_DIR}" "${OUTPUT_FILE}"
mkdir -p "${OPT_DIR}/share" "${PKG_DIR}/usr/bin" "${PKG_DIR}/usr/share/applications" "${PKG_DIR}/usr/share/metainfo" "${PKG_DIR}/usr/share/mime/packages" "${PKG_DIR}/usr/share/icons/hicolor/scalable/apps" "${PKG_DIR}/DEBIAN"

dotnet publish "${ROOT_DIR}/Pinta/Pinta.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:BuildTranslations=true \
  -p:PublishDir="${PUBLISH_DIR}"

rm -f "${PUBLISH_DIR}"/*.pdb
mv "${PUBLISH_DIR}/icons" "${PUBLISH_DIR}/locale" "${OPT_DIR}/share/"
cp -a "${ROOT_DIR}/gtk-bundle/lib" "${GTK_DIR}"
cp -a "${ROOT_DIR}/gtk-bundle/share/glib-2.0" "${OPT_DIR}/share/"

cat > "${PKG_DIR}/usr/bin/${PACKAGE}" <<EOF
#!/bin/sh
export LD_LIBRARY_PATH="/opt/${PACKAGE}/lib/gtk\${LD_LIBRARY_PATH:+:\$LD_LIBRARY_PATH}"
export GSETTINGS_SCHEMA_DIR="/opt/${PACKAGE}/share/glib-2.0/schemas"
exec "/opt/${PACKAGE}/lib/pinta/Pinta" "\$@"
EOF
chmod +x "${PKG_DIR}/usr/bin/${PACKAGE}"

mkdir -p "${WORK_DIR}/debian"
printf 'Source: %s\n\nPackage: %s\nArchitecture: amd64\n' "${PACKAGE}" "${PACKAGE}" > "${WORK_DIR}/debian/control"
DEPENDS="$(cd "${WORK_DIR}" && dpkg-shlibdeps -O --ignore-missing-info -l"${GTK_DIR}" "${PUBLISH_DIR}/Pinta" $(find "${OPT_DIR}/lib" -name '*.so*' -type f) | sed 's/^shlibs:Depends=//')"

sed -e 's/^_//' -e "s/^Exec=.*/Exec=${PACKAGE} %F/" -e "s/^TryExec=.*/TryExec=${PACKAGE}/" \
  "${ROOT_DIR}/xdg/${APP_ID}.desktop.in" > "${PKG_DIR}/usr/share/applications/${APP_ID}.desktop"
sed -e 's/<_/</g' -e 's/<\/_/<\//g' \
  "${ROOT_DIR}/xdg/${APP_ID}.metainfo.xml.in" > "${PKG_DIR}/usr/share/metainfo/${APP_ID}.metainfo.xml"
cp "${ROOT_DIR}/xdg/${APP_ID}.xml" "${PKG_DIR}/usr/share/mime/packages/"
cp "${ROOT_DIR}/Pinta.Resources/icons/hicolor/scalable/apps/${APP_ID}.svg" "${PKG_DIR}/usr/share/icons/hicolor/scalable/apps/"

cat > "${PKG_DIR}/DEBIAN/control" <<EOF
Package: ${PACKAGE}
Version: ${VERSION}
Architecture: amd64
Maintainer: ta3map <https://github.com/ta3map/Pinta-Classic-Workflow>
Depends: ${DEPENDS}, libicu76 | libicu74 | libicu72
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
