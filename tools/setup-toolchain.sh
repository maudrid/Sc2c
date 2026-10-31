#!/usr/bin/env bash
# Attack on Crateris: install the build toolchain on Linux x86_64.
#
# Copyright (C) 2026 Attack on Crateris contributors.
# This program is free software; you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation; either version 2 of the License, or
# (at your option) any later version.
#
# Installs the .NET 8 SDK and the Godot .NET build into /opt and links
# `dotnet` and `godot` into /usr/local/bin. Safe to run more than once.
# Used as the setup script of the cloud code session; works on a desktop
# Linux machine too (run with sudo).
set -euo pipefail

GODOT_VERSION=4.7.2
DOTNET_CHANNEL=8.0
GODOT_ZIP="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip"
GODOT_URL="https://github.com/godotengine/godot-builds/releases/download/${GODOT_VERSION}-stable"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

if ! /opt/dotnet/dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL%.*}\."; then
    curl -sSL -o "$tmp/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
    bash "$tmp/dotnet-install.sh" --channel "$DOTNET_CHANNEL" --install-dir /opt/dotnet
fi
ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet

godot_bin="/opt/godot/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64"
if [ ! -x "$godot_bin" ]; then
    curl -sSL -o "$tmp/$GODOT_ZIP" "$GODOT_URL/$GODOT_ZIP"
    curl -sSL -o "$tmp/SHA512-SUMS.txt" "$GODOT_URL/SHA512-SUMS.txt"
    (cd "$tmp" && grep " ${GODOT_ZIP}\$" SHA512-SUMS.txt | sha512sum -c -)
    mkdir -p /opt/godot
    unzip -q -o "$tmp/$GODOT_ZIP" -d /opt/godot
fi
ln -sf "$godot_bin" /usr/local/bin/godot

dotnet --version
godot --headless --version
