#!/bin/bash
# Checks what the bot needs on this server to start as the service and to update itself, and lists every problem:
# git with an origin that has the checked out branch, the .NET SDK of src/Sanakan.csproj, the config, the bot's
# connections (Discord, Shinden, NuGet, the fetch proxy and the addresses from the config; needs curl, and python3 to
# read the config) and no bot of the old layout (bin/Release/net6.0 started by the old Run.sh loop) still running
# from this repository.
#
#   tools/check-server.sh              for the Release service (src/Config.json)
#   tools/check-server.sh debug        for the Debug one (src/ConfigDebug.json)
#   tools/check-server.sh --processes  only the running bots; as root it sees the bots of every account
#
# Run it as the account the bot runs as; it changes nothing. install-service.sh runs it before installing, and the
# process check again as root. Exits 1 when something has to be fixed first.

set -u
REPO=$(readlink -f "$(dirname "$(readlink -f "$0")")/..")
CONFIG=Config.json
ONLY_PROCESSES=0
for arg in "$@"; do
    case "$arg" in
        debug) CONFIG=ConfigDebug.json ;;
        --processes) ONLY_PROCESSES=1 ;;
        *) echo "unknown argument: $arg (use: debug, --processes)" >&2; exit 2 ;;
    esac
done

PROBLEMS=0
ok() { echo "ok       $*"; }
problem() { echo "PROBLEM  $*"; PROBLEMS=$((PROBLEMS + 1)); }
warning() { echo "warning  $*"; }
# the repository may still belong to another account (e.g. root before install-service.sh), git refuses it then
repo_git() { git -c safe.directory="$REPO" -C "$REPO" "$@"; }

if [ "$ONLY_PROCESSES" -eq 1 ]; then
    :
# updates pull the checked out branch from origin, fast-forward only (src/Run.sh)
elif ! command -v git > /dev/null 2>&1; then
    problem "git is not installed"
elif ! repo_git rev-parse --git-dir > /dev/null 2>&1; then
    problem "$REPO is not a git repository"
else
    branch=$(repo_git symbolic-ref --short -q HEAD)
    if [ -n "$branch" ] && [ "$branch" != master ]; then
        warning "updates pull $branch; once it is merged into master, switch the server back: git checkout master"
    fi
    if [ -z "$branch" ]; then
        problem "no branch is checked out; updates pull the current branch, check one out, e.g.: git checkout master"
    elif ! origin=$(repo_git remote get-url origin 2>/dev/null); then
        problem "the repository has no origin, updates pull from it: git remote add origin https://github.com/MZKNEK/sanakan.git"
    else
        remote=$(timeout 30 git -c safe.directory="$REPO" -C "$REPO" ls-remote --exit-code --heads origin "refs/heads/$branch" 2>/dev/null)
        case $? in
            0)
                remote_head=${remote%%[[:space:]]*}
                if [ "$remote_head" = "$(repo_git rev-parse HEAD)" ]; then
                    ok "origin ($origin) has $branch at the checked out commit"
                elif ! repo_git cat-file -e "$remote_head^{commit}" 2>/dev/null; then
                    ok "origin ($origin) has $branch with newer commits, the next update brings them"
                elif repo_git merge-base --is-ancestor HEAD "$remote_head"; then
                    ok "origin ($origin) has $branch with newer commits, the next update brings them"
                else
                    problem "the checkout has commits that $branch on origin ($origin) does not; updates pull fast-forward only and would fail"
                fi
                ;;
            2) problem "origin ($origin) has no branch $branch; push it there first or check out a branch origin has" ;;
            *) warning "origin ($origin) could not be reached, the branch $branch was not checked there" ;;
        esac

        # a pull does not overwrite files changed by hand
        changed=$(repo_git status --porcelain --untracked-files=no | cut -c4- | tr '\n' ' ')
        if [ -n "$changed" ]; then warning "files changed by hand can stop an update: $changed"; fi
    fi
fi

# the server builds the bot itself, so it needs the SDK of the project's framework, not only the runtime
tfm=$(sed -n 's:.*<TargetFramework>\(.*\)</TargetFramework>.*:\1:p' "$REPO/src/Sanakan.csproj" | tr -d '\r')
major=${tfm#net}
major=${major%%.*}
DOTNET=${SANAKAN_DOTNET:-$(command -v dotnet || true)}
if [ "$ONLY_PROCESSES" -eq 1 ]; then
    :
elif [ -z "$DOTNET" ]; then
    problem "dotnet is not installed for $(id -un), the bot ($tfm) needs the .NET $major SDK"
else
    sdks=$("$DOTNET" --list-sdks 2>/dev/null | cut -d' ' -f1)
    sdk=$(echo "$sdks" | grep "^$major\." | tail -n 1)
    if [ -n "$sdk" ]; then
        ok ".NET SDK $sdk for $tfm ($DOTNET)"
    else
        problem "no .NET $major SDK for $tfm in $DOTNET (found: $(echo "$sdks" | tr '\n' ' ')); install it next to the others"
    fi
fi

if [ "$ONLY_PROCESSES" -eq 1 ]; then
    :
elif [ ! -f "$REPO/src/$CONFIG" ]; then
    problem "src/$CONFIG is missing, see Configuration in README.md"
elif [ ! -r "$REPO/src/$CONFIG" ]; then
    problem "src/$CONFIG is not readable by $(id -un): sudo tools/install-service.sh --permissions"
else
    ok "src/$CONFIG is there"
    CONFIG_READABLE=1
fi

# the bot's connections; a server that lets out only listed domains shows here what is missing from the list
http_code() { curl -s -o /dev/null -w '%{http_code}' --connect-timeout 5 --max-time 15 "$@" 2>/dev/null || true; }
# $1 what, $2 address, $3 the expected code (empty: any answer will do), $4 problem or warning
check_url() {
    local code
    code=$(http_code "$2")
    if [ "$code" = 000 ]; then
        "$4" "$1 ($2) cannot be reached; behind a domain allowlist ask for its domain"
    elif [ -n "$3" ] && [ "$code" != "$3" ]; then
        "$4" "$1 ($2) answered HTTP $code instead of $3"
    else
        ok "$1 answers ($2)"
    fi
}
# Newtonsoft reads the config with any letter case, so the keys are matched the same way
config_value() {
    python3 - "$REPO/src/$CONFIG" "$1" 2>/dev/null <<'EOF'
import json, sys
value = json.load(open(sys.argv[1], encoding="utf-8-sig"))
for key in sys.argv[2].split("."):
    value = next((v for k, v in value.items() if k.lower() == key.lower()), None) if isinstance(value, dict) else None
print(value if isinstance(value, str) else "")
EOF
}
PROXY_TEST_IMAGE=https://cdn.discordapp.com/embed/avatars/0.png

if [ "$ONLY_PROCESSES" -eq 1 ]; then
    :
elif ! command -v curl > /dev/null 2>&1; then
    warning "curl is not installed, the bot's connections were not checked"
else
    CONFIGURED=0
    if [ "${CONFIG_READABLE:-0}" -eq 1 ]; then
        if ! command -v python3 > /dev/null 2>&1; then
            warning "python3 is not installed, the addresses from src/$CONFIG were not checked"
        elif ! python3 -c 'import json, sys; json.load(open(sys.argv[1], encoding="utf-8-sig"))' "$REPO/src/$CONFIG" 2>/dev/null; then
            warning "src/$CONFIG is not plain JSON for python3, the addresses from it were not checked"
        else
            CONFIGURED=1
        fi
    fi
    value() { if [ "$CONFIGURED" -eq 1 ]; then config_value "$1"; fi; }

    api=$(value DiscordApiUrl)
    if [ -n "$api" ]; then check_url "Discord API" "$api" "" problem; else check_url "Discord API" https://discord.com/api/v10/gateway 200 problem; fi
    gateway=$(value DiscordGatewayUrl)
    if [ -z "$gateway" ]; then check_url "Discord gateway" https://gateway.discord.gg/ "" problem; fi
    check_url "Discord CDN (avatars, attachments)" "$PROXY_TEST_IMAGE" 200 problem
    check_url "Discord media proxy" https://media.discordapp.net/ "" problem
    check_url "Shinden pictures for cards" http://cdn.shinden.eu/cdn1/other/placeholders/title/225x350.jpg 200 problem
    check_url "NuGet (updates build the bot here)" https://api.nuget.org/v3/index.json 200 problem

    shinden=$(value Shinden.BaseUri)
    check_url "Shinden API" "${shinden:-https://shinden.pl/}" "" problem
    for name in Heartbeat Ingest; do
        url=$(value "$name.Url")
        if [ -n "$url" ]; then check_url "$name" "$url" "" warning; fi
    done

    proxy=$(value FetchProxy.Url)
    if [ -n "$proxy" ]; then
        key=$(value FetchProxy.Key)
        key=${key//\\/\\\\}
        key=${key//\"/\\\"}
        # the key goes through curl's stdin config, not the command line other accounts can see
        answer=$(printf 'header = "X-Internal-Key: %s"\n' "$key" | curl -s --config - -w '\n%{http_code}' --connect-timeout 5 --max-time 20 \
            -H 'Content-Type: application/json' \
            --data "{\"url\":\"$PROXY_TEST_IMAGE\",\"method\":\"GET\",\"kind\":\"image\",\"max_bytes\":5242880,\"timeout_ms\":8000,\"purpose\":\"sanakan\",\"any\":true}" \
            "$proxy" 2>/dev/null)
        code=${answer##*$'\n'}
        body=${answer%$'\n'*}
        if [ "$code" = 200 ] && echo "$body" | grep -Eq '"status" *: *200'; then
            ok "fetch proxy downloads players' links ($proxy)"
        elif [ "$code" = 401 ]; then
            problem "the fetch proxy ($proxy) refuses FetchProxy.Key"
        elif [ -z "$code" ] || [ "$code" = 000 ]; then
            problem "the fetch proxy ($proxy) cannot be reached"
        else
            problem "the fetch proxy ($proxy) did not download a test picture: HTTP $code $(echo "$body" | head -c 200)"
        fi
    elif [ "$CONFIGURED" -eq 1 ]; then
        ok "no fetch proxy: players' links are downloaded directly from this server"
    fi
fi

# the old layout runs bin/<config>/<framework>/Sanakan.dll in src and restarts it from a Run.sh loop; next to the
# service it would be a second bot on the same token
if [ -d /proc ] && command -v pgrep > /dev/null 2>&1; then
    old=0
    for pid in $(pgrep -f 'Sanakan\.dll'); do
        cmd=$(tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null)
        case "$cmd" in *"/deploy/"*|"") continue ;; esac
        cwd=$(readlink "/proc/$pid/cwd" 2>/dev/null)
        if [ "$cwd" = "$REPO/src" ]; then
            problem "the bot runs in the old layout from this repository (pid $pid: $cmd); stop it and the Run.sh loop that restarts it"
            old=1
        elif [ -z "$cwd" ]; then
            warning "a bot of another account runs (pid $pid: $cmd); if it is the old one from this repository, stop it and its Run.sh loop"
            old=1
        fi
    done
    if [ "$old" -eq 0 ]; then ok "no bot of the old layout runs from this repository"; fi
fi

if [ "$PROBLEMS" -eq 0 ]; then
    echo "everything the service needs is in place"
else
    echo "$PROBLEMS problem(s) to fix first"
    exit 1
fi
