#!/bin/bash
# Registers the bot as a systemd service that starts src/Run.sh, and gives the bot's user the files it needs.
#
#   sudo tools/install-service.sh                 sanakan.service, Release build
#   sudo tools/install-service.sh debug           sanakan-debug.service, Debug build
#   sudo tools/install-service.sh --permissions   only the owner and permissions of the files
#   sudo tools/install-service.sh --remove        removes sanakan.service (with debug: sanakan-debug.service)
#   --name NAME                                   another service name (tools/test-service.sh uses sanakan-test)
#   --user USER                                   the account the bot runs as, when called as root without sudo
#   --data FOLDER                                 keeps the generated pictures (GOut) in FOLDER, outside the
#                                                 repository, and leaves a link in GOut's place; an existing GOut
#                                                 is moved there when it is on the same disk
#
# Run it with sudo from the account the bot runs as (or as root with --user). The service runs as that user in src, so the bot finds its
# config, Pictures and ../GOut like before. Exit codes (src/ExitCodes.cs): 0 stop and 78 bad config keep it down,
# an update (100) is done by Run.sh itself, anything else restarts it after 5 s; after 10 starts in 10 minutes
# systemd gives up, so a broken bot does not keep logging in to Discord. An existing unit file is copied aside first.

set -eu
REPO=$(readlink -f "$(dirname "$(readlink -f "$0")")/..")
cd "$REPO/src"

NAME=sanakan
ARGS=""
MODE=install
CUSTOM_NAME=""
USER_OPT=""
DATA=""
while [ $# -gt 0 ]; do
    case "$1" in
        debug) NAME=sanakan-debug; ARGS=" debug" ;;
        --permissions) MODE=permissions ;;
        --remove) MODE=remove ;;
        --name) CUSTOM_NAME=${2:?--name needs a value}; shift ;;
        --user) USER_OPT=${2:?--user needs a value}; shift ;;
        --data) DATA=${2:?--data needs a folder}; shift ;;
        *) echo "unknown argument: $1 (use: debug, --permissions, --remove, --name NAME, --user USER, --data FOLDER)" >&2; exit 2 ;;
    esac
    shift
done
if [ -n "$CUSTOM_NAME" ]; then NAME=$CUSTOM_NAME; fi

if [ "$(id -u)" -ne 0 ]; then echo "run it with sudo" >&2; exit 1; fi

UNIT="/etc/systemd/system/$NAME.service"

if [ "$MODE" = remove ]; then
    systemctl disable --now "$NAME" || true
    rm -f "$UNIT"
    systemctl daemon-reload
    echo "$NAME.service removed"
    exit 0
fi

# the bot is reachable from the internet (API, pictures from any address) and builds what it pulls, so it never runs
# as root: a hole in it then reaches only the bot's own files
RUN_AS=${USER_OPT:-${SUDO_USER:-}}
if [ -z "$RUN_AS" ] || [ "$RUN_AS" = root ]; then echo "name the bot's account: sudo from it, or --user USER (not root)" >&2; exit 1; fi
if ! id "$RUN_AS" > /dev/null 2>&1; then echo "no such user: $RUN_AS" >&2; exit 1; fi

# the bot writes builds, generated pictures (../GOut) and its dumps, git pull needs the repository too;
# the configs hold the bot token and database password, so only its user reads them
OWNER="$RUN_AS:$(id -gn "$RUN_AS")"
GOUT="$REPO/GOut"

# the database keeps the pictures as ../GOut/..., so GOut stays where it is and may only point elsewhere; inside the
# repository the files would get in the way of git and could go with a fresh clone
if [ -n "$DATA" ]; then
    case "$DATA" in /*) ;; *) echo "--data needs a full path, like /data/sanakan-GOut" >&2; exit 1 ;; esac
    DATA=$(readlink -m "$DATA")
    case "$DATA/" in "$REPO/"*) echo "--data has to be outside the repository ($REPO)" >&2; exit 1 ;; esac
    if [ -L "$GOUT" ]; then
        if [ "$(readlink -m "$GOUT")" != "$DATA" ]; then
            echo "GOut already links to $(readlink "$GOUT"); to move it, stop the bot, move the files and change the link by hand" >&2
            exit 1
        fi
    elif [ -d "$GOUT" ]; then
        if systemctl is-active --quiet "$NAME"; then echo "stop $NAME first, GOut is going to be moved" >&2; exit 1; fi
        if [ -e "$DATA" ] && [ -n "$(ls -A "$DATA")" ]; then echo "$DATA is not empty, GOut is not moved over it" >&2; exit 1; fi
        mkdir -p "$(dirname "$DATA")"
        if [ "$(stat -c %d "$GOUT")" != "$(stat -c %d "$(dirname "$DATA")")" ]; then
            # another disk means copying every picture, which can take hours, so it is left to be done by hand
            echo "$DATA is on another disk than GOut; copy the pictures by hand, then run this again:" >&2
            echo "  rsync -a $GOUT/ $DATA/ && mv $GOUT $GOUT.old && ln -s $DATA $GOUT" >&2
            exit 1
        fi
        if [ -d "$DATA" ]; then rmdir "$DATA"; fi
        mv "$GOUT" "$DATA"
        ln -s "$DATA" "$GOUT"
        echo "GOut moved to $DATA"
    else
        mkdir -p "$DATA"
        ln -s "$DATA" "$GOUT"
    fi
fi

if [ -L "$GOUT" ] && [ ! -d "$GOUT" ]; then echo "GOut links to a missing folder: $(readlink "$GOUT")" >&2; exit 1; fi
mkdir -p "$GOUT"
# GOut can hold millions of generated pictures (it may be a link to another disk), so only the folder itself is
# given to the bot, the files in it already belong to it
find "$REPO" -mindepth 1 -maxdepth 1 ! -name GOut -exec chown -R "$OWNER" {} +
chown "$OWNER" "$REPO" "$(readlink -f "$GOUT")"
chmod 755 "$REPO/src/Run.sh" "$REPO/tools/install-service.sh"
for config in Config.json ConfigDebug.json; do
    if [ -f "$config" ]; then chmod 600 "$config"; fi
done
echo "files in $REPO belong to $RUN_AS, configs readable only by $RUN_AS"
if [ "$MODE" = permissions ]; then exit 0; fi

# systemd does not read the user's shell profile, so a dotnet installed in the home folder has to be found now;
# SANAKAN_DOTNET picks another one, e.g. a newer .NET installed next to the current
DOTNET=${SANAKAN_DOTNET:-$(sudo -u "$RUN_AS" bash -lc 'command -v dotnet' || true)}
if [ -z "$DOTNET" ]; then echo "dotnet not found for $RUN_AS" >&2; exit 1; fi
DOTNET_DIR=$(dirname "$(readlink -f "$DOTNET")")

if [ -f "$UNIT" ]; then
    BACKUP="$UNIT.bak-$(date '+%Y%m%d-%H%M%S')"
    cp "$UNIT" "$BACKUP"
    echo "previous unit saved as $BACKUP"
fi

cat > "$UNIT" <<EOF
[Unit]
Description=Sanakan Discord bot$ARGS
After=network-online.target
Wants=network-online.target
StartLimitIntervalSec=600
StartLimitBurst=10

[Service]
Type=simple
User=$RUN_AS
WorkingDirectory=$REPO/src
ExecStart=$REPO/src/Run.sh$ARGS
Environment=PATH=$DOTNET_DIR:/usr/local/bin:/usr/bin:/bin
Environment=DOTNET_ROOT=$DOTNET_DIR
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=DOTNET_NOLOGO=1
Restart=on-failure
RestartPreventExitStatus=78
RestartSec=5
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable "$NAME"
echo "$NAME.service installed for $RUN_AS in $REPO/src (dotnet from $DOTNET_DIR)"
echo "start or restart:  sudo systemctl restart $NAME"
echo "status:            systemctl status $NAME"
echo "logs:              journalctl -u $NAME -f"
