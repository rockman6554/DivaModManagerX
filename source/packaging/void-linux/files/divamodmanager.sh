#!/bin/sh
# Wrapper script for DivaModManager (Linux port).
# Installed at /usr/bin/divamodmanager by the Void Linux xbps template.

# Locate dotnet (handles both system install and ~/.dotnet)
if [ -x /usr/bin/dotnet ]; then
    DOTNET=/usr/bin/dotnet
elif [ -x "$HOME/.dotnet/dotnet" ]; then
    DOTNET="$HOME/.dotnet/dotnet"
    export DOTNET_ROOT="$HOME/.dotnet"
else
    echo "Error: dotnet runtime not found. Install with: xbps-install -S dotnet-runtime" >&2
    exit 1
fi

APP_DIR=/usr/lib/divamodmanager
exec "$DOTNET" "$APP_DIR/DivaModManager.dll" "$@"
