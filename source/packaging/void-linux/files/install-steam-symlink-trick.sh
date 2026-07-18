#!/usr/bin/env bash
# install-steam-symlink-trick.sh
#
# Sets up the "DivaMegaMix.exe" symlink trick so Steam's Play button launches
# DMM instead of the game directly. This mirrors the original Windows workflow
# where the user replaced the game exe with DMM renamed to "DivaMegaMix.exe".
#
# On Linux this is OPTIONAL — DMM runs natively, so you can launch it directly
# from the desktop entry without involving Steam. The symlink trick is only
# useful if you want Steam's Play button to open DMM first.
#
# Usage:
#   sudo ./install-steam-symlink-trick.sh
#   sudo ./install-steam-symlink-trick.sh --restore   # undo
#
# Requires:
#   - The game installed via Steam (Proton prefix at compatdata/1761390)
#   - DMM installed at /usr/lib/divamodmanager or ~/.local/share/divamodmanager

set -euo pipefail

APP_ID=1761390
GAME_EXE_NAME="DivaMegaMix.exe"
GAME_EXE_BACKUP="DivaMegaMix.exe "  # trailing space, matching user's existing layout

# Locate DMM binary
if [ -x /usr/lib/divamodmanager/DivaModManager ]; then
    DMM_EXE="/usr/lib/divamodmanager/DivaModManager"
elif [ -x "$HOME/.local/share/divamodmanager/DivaModManager" ]; then
    DMM_EXE="$HOME/.local/share/divamodmanager/DivaModManager"
else
    echo "Error: DivaModManager binary not found." >&2
    echo "Install with: xbps-install -S divamodmanager" >&2
    exit 1
fi

# Locate the game install via Steam libraryfolders.vdf
find_game_exe() {
    local steam_root
    for steam_root in "$HOME/.steam/steam" "$HOME/.local/share/Steam" "$HOME/.var/app/com.valvesoftware.Steam/data/Steam"; do
        [ -d "$steam_root" ] || continue
        local vdf="$steam_root/steamapps/libraryfolders.vdf"
        local libs=("$steam_root")
        if [ -f "$vdf" ]; then
            while IFS= read -r line; do
                if [[ "$line" =~ \"path\"[[:space:]]+\"(.+)\" ]]; then
                    local p="${BASH_REMATCH[1]}"
                    p="${p//\\\\/}"
                    [ -d "$p" ] && libs+=("$p")
                fi
            done < "$vdf"
        fi
        for lib in "${libs[@]}"; do
            local candidate="$lib/steamapps/common/Hatsune Miku Project DIVA Mega Mix+/$GAME_EXE_NAME"
            if [ -f "$candidate" ]; then
                echo "$candidate"
                return 0
            fi
        done
    done
    return 1
}

GAME_EXE=$(find_game_exe || true)
if [ -z "$GAME_EXE" ]; then
    echo "Error: could not auto-detect DivaMegaMix.exe in your Steam library." >&2
    echo "Pass the full path as the first argument:" >&2
    echo "  $0 /path/to/DivaMegaMix.exe" >&2
    exit 1
fi

GAME_DIR=$(dirname "$GAME_EXE")
BACKUP_PATH="$GAME_DIR/$GAME_EXE_BACKUP"

if [ "${1:-}" = "--restore" ]; then
    echo "Restoring original game exe..."
    if [ ! -f "$BACKUP_PATH" ]; then
        echo "Error: backup file '$BACKUP_PATH' not found. Nothing to restore." >&2
        exit 1
    fi
    if [ -L "$GAME_EXE" ] || [ -f "$GAME_EXE" ]; then
        rm -f "$GAME_EXE"
    fi
    mv "$BACKUP_PATH" "$GAME_EXE"
    echo "Restored: $GAME_EXE"
    echo "Steam will now launch the game directly."
    exit 0
fi

echo "DMM binary:   $DMM_EXE"
echo "Game exe:     $GAME_EXE"
echo "Backup path:  $BACKUP_PATH"
echo ""
read -rp "Proceed? This will rename the game exe and create a symlink to DMM. [y/N] " confirm
if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
    echo "Aborted."
    exit 0
fi

# Back up the real exe if not already backed up
if [ ! -f "$BACKUP_PATH" ]; then
    mv "$GAME_EXE" "$BACKUP_PATH"
    echo "Backed up real exe to '$BACKUP_PATH' (note the trailing space — matches your existing setup)."
fi

# Create symlink
ln -sf "$DMM_EXE" "$GAME_EXE"
echo "Created symlink: $GAME_EXE -> $DMM_EXE"
echo ""
echo "Done. Steam's Play button will now launch DMM."
echo "Inside DMM, click 'Launch' to start the real game (which is now at '$BACKUP_PATH')."
echo ""
echo "To undo later: $0 --restore"
