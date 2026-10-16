#!/bin/sh
set -e

APP_NAME="${APP_NAME:-PintaClassicWorkflow}"
APP_EXECUTABLE="${APP_EXECUTABLE:-Pinta}"
APP_BUNDLE_ID="${APP_BUNDLE_ID:-io.github.ta3map.PintaClassicWorkflow}"
APP_VERSION="${APP_VERSION:-3.2}"
APP_ICON_FILE="${APP_ICON_FILE:-pinta.icns}"
DMG_BASENAME="${DMG_BASENAME:-PintaClassicWorkflow}"
MAC_CODESIGN_IDENTITY="${MAC_CODESIGN_IDENTITY:-Developer ID Application: Cameron White (D5G6C56TBH)}"
MAC_NOTARY_APPLE_ID="${MAC_NOTARY_APPLE_ID:-cameronwhite91@gmail.com}"
MAC_NOTARY_PASSWORD="${MAC_NOTARY_PASSWORD:-${MAC_DEV_PASSWORD}}"
MAC_NOTARY_TEAM_ID="${MAC_NOTARY_TEAM_ID:-D5G6C56TBH}"

# Parse command line arguments
skip_signing=false
runtimeid=""

for arg in "$@"; do
    case $arg in
        --skip-signing)
            skip_signing=true
            shift
            ;;
        *)
            runtimeid=$arg
            shift
            ;;
    esac
done

if [ "$runtimeid" != "osx-x64" ] && [ "$runtimeid" != "osx-arm64" ]; then
    echo "Invalid runtime identifier (should be osx-x64 or osx-arm64)"
    echo "Usage: ./build_installer.sh [--skip-signing] runtimeid"
    exit 1
fi

MAC_APP_DIR="$PWD/package/${APP_NAME}.app"
MAC_APP_BIN_DIR="${MAC_APP_DIR}/Contents/MacOS/"
MAC_APP_RESOURCE_DIR="${MAC_APP_DIR}/Contents/Resources/"
MAC_APP_SHARE_DIR="${MAC_APP_RESOURCE_DIR}/share"
DMG_PATH="${DMG_BASENAME}-${runtimeid}.dmg"

GTK_UPDATE_ICON_CACHE="$(brew --prefix gtk4)/bin/gtk4-update-icon-cache -f"
PLIST_BUDDY="/usr/libexec/PlistBuddy"

run_codesign()
{
    file=$1
    echo ${file}
    codesign --deep --force --timestamp --options runtime --sign "${MAC_CODESIGN_IDENTITY}" --entitlements entitlements.plist ${file}
}

run_adhoc_codesign()
{
    file=$1
    echo ${file}
    codesign --force --sign - ${file}
}

mkdir -p ${MAC_APP_BIN_DIR} ${MAC_APP_RESOURCE_DIR} ${MAC_APP_SHARE_DIR}

dotnet publish ../../Pinta/Pinta.csproj -p:PublishDir=${MAC_APP_BIN_DIR} -p:BuildTranslations=true -c Release -r $runtimeid --self-contained true

# Remove stuff we don't need.
rm ${MAC_APP_BIN_DIR}/*.pdb

# Move resources files out of the MacOS folder (needed for code signing).
# TODO - this could be done in the .csproj publish rule instead?
mv ${MAC_APP_BIN_DIR}/locale ${MAC_APP_SHARE_DIR}/locale
mv ${MAC_APP_BIN_DIR}/icons ${MAC_APP_SHARE_DIR}/icons
cp hicolor.index.theme ${MAC_APP_SHARE_DIR}/icons/hicolor/index.theme

cp Info.plist ${MAC_APP_DIR}/Contents
${PLIST_BUDDY} -c "Set :CFBundleIdentifier ${APP_BUNDLE_ID}" ${MAC_APP_DIR}/Contents/Info.plist
${PLIST_BUDDY} -c "Set :CFBundleName ${APP_NAME}" ${MAC_APP_DIR}/Contents/Info.plist
${PLIST_BUDDY} -c "Set :CFBundleShortVersionString ${APP_VERSION}" ${MAC_APP_DIR}/Contents/Info.plist
${PLIST_BUDDY} -c "Set :CFBundleVersion ${APP_VERSION}" ${MAC_APP_DIR}/Contents/Info.plist
${PLIST_BUDDY} -c "Set :CFBundleExecutable ${APP_EXECUTABLE}" ${MAC_APP_DIR}/Contents/Info.plist

if [ -f "${APP_ICON_FILE}" ]; then
    cp "${APP_ICON_FILE}" "${MAC_APP_RESOURCE_DIR}/"
else
    echo "Warning: ${APP_ICON_FILE} not found; continuing with generic app icon."
fi

# Install the GTK dependencies.
echo "Bundling GTK..."
./bundle_gtk.py --runtime $runtimeid --resource_dir ${MAC_APP_RESOURCE_DIR}
# Add the GTK lib dir to the library search path (for dlopen()), as an alternative to $DYLD_LIBRARY_PATH.
install_name_tool -add_rpath "@executable_path/../Resources/lib" ${MAC_APP_BIN_DIR}/${APP_EXECUTABLE}

# Generate the icon theme cache.
${GTK_UPDATE_ICON_CACHE} ${MAC_APP_SHARE_DIR}/icons/hicolor
${GTK_UPDATE_ICON_CACHE} ${MAC_APP_SHARE_DIR}/icons/Adwaita

touch ${MAC_APP_DIR}

if [ "$skip_signing" = "false" ]; then
    # Sign the GTK binaries.
    echo "Signing..."
    for lib in `find ${MAC_APP_RESOURCE_DIR} -name \*.dylib -or -name \*.so`
    do
        run_codesign ${lib}
    done

    # Sign the main executable and .NET stuff.
    run_codesign ${MAC_APP_DIR}
else
    # Even for unsigned distribution builds, we need valid ad-hoc signatures
    # for all native code loaded by dyld / dlopen at runtime.
    echo "Ad-hoc signing (unsigned build mode)..."
    for lib in `find ${MAC_APP_RESOURCE_DIR} -name \*.dylib -or -name \*.so`
    do
        run_adhoc_codesign ${lib}
    done
    for lib in `find ${MAC_APP_BIN_DIR} -name \*.dylib`
    do
        run_adhoc_codesign ${lib}
    done
    codesign --deep --force --sign - ${MAC_APP_DIR}
fi

# Create the .dmg image, and include a link to drag the app into /Applications
echo "Creating dmg..."
ln -s /Applications package/Applications
hdiutil create -quiet -srcFolder package -volname "${APP_NAME} Installer" -o ${DMG_PATH}

if [ "$skip_signing" = "false" ]; then
    # Sign the .dmg image
    run_codesign ${DMG_PATH}

    # Notarize
    echo "Notarizing..."
    if [ -z "${MAC_NOTARY_PASSWORD}" ]; then
        echo "Missing notarization password. Set MAC_NOTARY_PASSWORD (or MAC_DEV_PASSWORD)."
        exit 1
    fi
    xcrun notarytool submit --wait --apple-id=${MAC_NOTARY_APPLE_ID} --password ${MAC_NOTARY_PASSWORD} --team-id ${MAC_NOTARY_TEAM_ID} ${DMG_PATH}

    # Staple the result to the dmg
    echo "Stapling..."
    xcrun stapler staple ${DMG_PATH}
fi
