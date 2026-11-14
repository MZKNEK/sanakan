#!/bin/bash
# Runs the bot and updates it when it asks for that. The systemd service (tools/install-service.sh) starts it;
# by hand use --loop, which also restarts the bot.
#
#   ./Run.sh                 Release build (what the service starts)
#   ./Run.sh debug           Debug build
#   ./Run.sh --loop          restarts the bot by itself, for running without systemd
#   ./Run.sh --update        only pulls and builds, the next start uses the new build
#
# Bot exit codes (src/ExitCodes.cs): 0 stop, 75 restart, 78 bad config (stays down), 100 update; anything else is
# a crash and is restarted like 75. 200 is the update code of older versions.
# An update pulls the current branch from origin (fast-forward only) and publishes it into deploy/<config>/<time>;
# the bot switches to it only when the build succeeded. A new build that fails within ROLLBACK_SECONDS of its first
# start is replaced by the previous one.

set -u
cd "$(dirname "$(readlink -f "$0")")" || exit 1

CONFIG=Release
MODE=once
for arg in "$@"; do
    case "$arg" in
        debug) CONFIG=Debug ;;
        --loop) MODE=loop ;;
        --update) MODE=update ;;
        *) echo "unknown argument: $arg (use: debug, --loop, --update)" >&2; exit 2 ;;
    esac
done

DEPLOY="deploy/${CONFIG,,}"
KEEP_BUILDS=3
ROLLBACK_SECONDS=60
STOP=0
BAD_CONFIG=78

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] Run: $*"; }

current() { cat "$DEPLOY/current" 2>/dev/null; }

is_update() { [ "$1" -eq 100 ] || [ "$1" -eq 200 ]; }

build() {
    local stamp
    stamp=$(date '+%Y%m%d-%H%M%S')
    log "building $stamp ($CONFIG, commit $(git rev-parse --short HEAD 2>/dev/null))"
    if ! dotnet publish Sanakan.csproj -c "$CONFIG" -o "$DEPLOY/$stamp" --nologo -v quiet; then
        rm -rf "${DEPLOY:?}/$stamp"
        log "build failed, staying on ${1:-$(current)}"
        return 1
    fi
    if [ -n "$(current)" ]; then current > "$DEPLOY/previous"; fi
    echo "$stamp" > "$DEPLOY/current"
    echo "$stamp" > "$DEPLOY/pending"
    # the newest builds stay, the current and the previous one among them
    find "$DEPLOY" -mindepth 1 -maxdepth 1 -type d | sort | head -n -"$KEEP_BUILDS" | xargs -r rm -rf
    log "switched to $stamp"
}

update() {
    local branch
    branch=$(git rev-parse --abbrev-ref HEAD)
    if [ "$branch" = HEAD ]; then
        log "update: no branch is checked out (tools/check-server.sh), staying on $(current)"
        return 1
    fi
    if ! git remote get-url origin > /dev/null 2>&1; then
        log "update: the repository has no origin (tools/check-server.sh), staying on $(current)"
        return 1
    fi
    log "update: pulling $branch from origin"
    if ! git pull --ff-only origin "$branch"; then
        log "update: pull failed, staying on $(current)"
        return 1
    fi
    build
}

# runs the bot until it exits with something other than an update request, returns that exit code
run() {
    local stamp started code ran previous
    while true; do
        if [ -z "$(current)" ] || [ ! -f "$DEPLOY/$(current)/Sanakan.dll" ]; then build "nothing" || return 1; fi

        stamp=$(current)
        started=$(date +%s)
        log "starting $stamp"
        dotnet "$DEPLOY/$stamp/Sanakan.dll"
        code=$?
        ran=$(( $(date +%s) - started ))
        log "bot exited with code $code after ${ran}s"

        if [ -f "$DEPLOY/pending" ]; then
            # the first build has nothing to go back to
            previous=$(cat "$DEPLOY/previous" 2>/dev/null)
            if [ "$code" -ne "$STOP" ] && ! is_update "$code" && [ "$ran" -lt "$ROLLBACK_SECONDS" ] \
                && [ -n "$previous" ] && [ -d "$DEPLOY/$previous" ]; then
                log "new build $stamp failed on start, going back to $previous"
                echo "$previous" > "$DEPLOY/current"
            fi
            rm -f "$DEPLOY/pending"
        fi

        if ! is_update "$code"; then return "$code"; fi
        update
    done
}

case "$MODE" in
    update)
        update
        exit $?
        ;;
    once)
        run
        exit $?
        ;;
    loop)
        delay=5
        while true; do
            started=$(date +%s)
            run
            code=$?
            if [ "$code" -eq "$STOP" ]; then log "stopped"; exit 0; fi
            if [ "$code" -eq "$BAD_CONFIG" ]; then log "the configuration is invalid, fix it and start again"; exit "$code"; fi
            if [ $(( $(date +%s) - started )) -ge 600 ]; then delay=5; fi
            log "restarting in ${delay}s"
            sleep "$delay"
            delay=$(( delay * 2 > 300 ? 300 : delay * 2 ))
        done
        ;;
esac
