#!/usr/bin/env bash
set -e
cd "$(dirname "$(readlink -f "$0")")/.."

flatpak run --share=network --filesystem=home --env=LANG=C.UTF-8 \
	--command=bash org.gnome.Sdk//48 \
	-c "source /usr/lib/sdk/dotnet8/enable.sh && cd '$PWD' && dotnet build Pinta/Pinta.csproj"

read -rp "Готово. Enter для закрытия..."
