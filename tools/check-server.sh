#!/bin/bash
# Checks what the bot needs on this server to start as the service and to update itself, and lists every problem:
# git with an origin that has the checked out branch, the .NET SDK of src/Sanakan.csproj, the config, and no bot of
# the old layout (bin/Release/net6.0 started by the old Run.sh loop) still running from this repository.
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
