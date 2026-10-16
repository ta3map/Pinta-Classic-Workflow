#!/bin/sh
set -e

APP_NAME="${APP_NAME:-PintaClassicWorkflow}"
APP_DISPLAY_NAME="${APP_DISPLAY_NAME:-Pinta Classic Workflow}"
APP_ID="${APP_ID:-io.github.ta3map.PintaClassicWorkflow}"
RUNTIME_ID="${RUNTIME_ID:-linux-x64}"
BUILD_CONFIGURATION="${BUILD_CONFIGURATION:-Release}"
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(sed -n 's/^AC_INIT(\[pinta\], \[\(.*\)\])$/\1/p' "${ROOT_DIR}/configure.ac")"
WORK_DIR="${ROOT_DIR}/release-appimage"
APPDIR="${WORK_DIR}/${APP_NAME}.AppDir"
PUBLISH_DIR="${WORK_DIR}/publish"
OUTPUT_FILE="${ROOT_DIR}/${APP_NAME}-${VERSION}-${RUNTIME_ID}.AppImage"
DESKTOP_TEMPLATE="${ROOT_DIR}/xdg/${APP_ID}.desktop.in"

rm -rf "${WORK_DIR}" "${OUTPUT_FILE}"
mkdir -p "${APPDIR}/usr/bin" "${APPDIR}/usr/share/applications" "${APPDIR}/usr/share/icons/hicolor/scalable/apps"

dotnet publish "${ROOT_DIR}/Pinta/Pinta.csproj" \
  -c "${BUILD_CONFIGURATION}" \
  -r "${RUNTIME_ID}" \
  --self-contained true \
  -p:BuildTranslations=true \
  -p:PublishDir="${PUBLISH_DIR}"

cp -R "${PUBLISH_DIR}/." "${APPDIR}/usr/bin/"
cp -a "${ROOT_DIR}/gtk-bundle/lib" "${APPDIR}/usr/lib"
cp -a "${ROOT_DIR}/gtk-bundle/share/glib-2.0" "${APPDIR}/usr/share/"

sed -e 's/^_//' -e "s/^Exec=.*/Exec=Pinta %F/" -e "s/^TryExec=.*/TryExec=Pinta/" \
  "${DESKTOP_TEMPLATE}" > "${APPDIR}/usr/share/applications/${APP_ID}.desktop"
cp "${APPDIR}/usr/share/applications/${APP_ID}.desktop" "${APPDIR}/${APP_ID}.desktop"

cp "${ROOT_DIR}/Pinta.Resources/icons/hicolor/scalable/apps/${APP_ID}.svg" "${APPDIR}/${APP_ID}.svg"
cp "${ROOT_DIR}/Pinta.Resources/icons/hicolor/scalable/apps/${APP_ID}.svg" "${APPDIR}/usr/share/icons/hicolor/scalable/apps/${APP_ID}.svg"
ln -sf "${APP_ID}.svg" "${APPDIR}/.DirIcon"

cat > "${APPDIR}/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="${HERE}/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export GSETTINGS_SCHEMA_DIR="${HERE}/usr/share/glib-2.0/schemas"
exec "${HERE}/usr/bin/Pinta" "$@"
EOF
chmod +x "${APPDIR}/AppRun"

APPIMAGETOOL="${WORK_DIR}/appimagetool.AppImage"
curl -fsSL "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage" -o "${APPIMAGETOOL}"
chmod +x "${APPIMAGETOOL}"

ARCH=x86_64 "${APPIMAGETOOL}" --appimage-extract-and-run "${APPDIR}" "${OUTPUT_FILE}"

echo "Built ${OUTPUT_FILE}"
