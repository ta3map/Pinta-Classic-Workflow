#!/usr/bin/env bash
cd "$(dirname "$(readlink -f "$0")")/.."

exec flatpak run --share=network --share=ipc --socket=wayland --socket=fallback-x11 --device=dri \
	--filesystem=home --env=LANG=C.UTF-8 \
	--command=bash org.gnome.Sdk//48 \
	-c "source /usr/lib/sdk/dotnet8/enable.sh && exec dotnet '$PWD/build/bin/Pinta.dll' \"\$@\"" pinta "$@"
