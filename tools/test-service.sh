#!/bin/bash
# Checks install-service.sh and Run.sh under the real systemd with a fake bot, without touching sanakan.service.
#
#   sudo tools/test-service.sh             the fake bot targets the framework of src/Sanakan.csproj
#   sudo tools/test-service.sh net10.0     or the given one, to try a new .NET before the bot moves to it
#   tools/test-service.sh --user sanakan   as root: the account the fake bot runs as
#
# Run it with sudo from the account the bot runs as, like install-service.sh. It builds a throwaway repository in
# /tmp with a tiny .NET program in place of the bot (it exits with the code written to src/fake-exit-code), registers
# it as sanakan-test.service and goes through: start, restart (75), update (100), going back after a broken update,
# bad config (78), stop (0) and file permissions. At the end it removes the service and the folder; after a failure
# the folder stays for a look and the service is removed anyway.

set -u
if [ "$(id -u)" -ne 0 ]; then echo "run it with sudo" >&2; exit 1; fi
TFM_OPT=""
RUN_AS=${SUDO_USER:-}
while [ $# -gt 0 ]; do
    case "$1" in
        --user) RUN_AS=${2:?--user needs a value}; shift ;;
        net*) TFM_OPT=$1 ;;
        *) echo "unknown argument: $1 (use: netX.Y, --user USER)" >&2; exit 2 ;;
    esac
    shift
done
if [ -z "$RUN_AS" ] || [ "$RUN_AS" = root ]; then echo "name the bot's account: sudo from it, or --user USER (not root)" >&2; exit 1; fi
if ! id "$RUN_AS" > /dev/null 2>&1; then echo "no such user: $RUN_AS" >&2; exit 1; fi

SOURCE=$(readlink -f "$(dirname "$(readlink -f "$0")")/..")
NAME=sanakan-test
T=/tmp/sanakan-service-test
REPO=$T/sanakan
FAILED=0
STARTED_AT=$(date '+%Y-%m-%d %H:%M:%S')

as_user() { sudo -u "$RUN_AS" bash -lc "$*"; }
pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*"; FAILED=$((FAILED + 1)); }
journal() { journalctl -u "$NAME" --since "$STARTED_AT" -o cat --no-pager 2>/dev/null; }
count() { journal | grep -c -- "$1"; }

# waits until the journal has the line at least $2 times
wait_for() {
    local text=$1 times=$2 seconds=${3:-120}
    for _ in $(seq "$seconds"); do
        if [ "$(count "$text")" -ge "$times" ]; then return 0; fi
        sleep 1
    done
    return 1
}

fake_program() {
    local version=$1 crash=$2
    cat <<EOF
using System;
using System.IO;
using System.Threading;

Console.WriteLine("fake bot $version started");
if ($crash) { Console.WriteLine("fake bot $version crashes on start"); return 134; }
while (true)
{
    if (File.Exists("fake-exit-code"))
    {
        var code = int.Parse(File.ReadAllText("fake-exit-code").Trim());
        File.Delete("fake-exit-code");
        Console.WriteLine("fake bot $version exits with " + code);
        return code;
    }
    Thread.Sleep(500);
}
EOF
}

push_version() {
    local version=$1 crash=$2
    fake_program "$version" "$crash" > "$T/dev/src/Program.cs"
    chown "$RUN_AS" "$T/dev/src/Program.cs"
    as_user "cd '$T/dev' && git add -A && git -c user.name=test -c user.email=test@localhost commit -qm '$version' && git push -q origin HEAD"
}

exit_bot() { as_user "echo $1 > '$REPO/src/fake-exit-code'"; }

cleanup() {
    "$REPO/tools/install-service.sh" --remove --name "$NAME" > /dev/null 2>&1 || true
    if [ "$FAILED" -eq 0 ]; then rm -rf "$T"; else echo "test files left in $T"; fi
}
trap cleanup EXIT

echo "== preparing $T"
systemctl stop "$NAME" > /dev/null 2>&1 || true
rm -rf "$T"
mkdir -p "$REPO/src" "$REPO/tools"
cp "$SOURCE/src/Run.sh" "$REPO/src/"
cp "$SOURCE/tools/install-service.sh" "$REPO/tools/"
TFM=${TFM_OPT:-$(sed -n 's:.*<TargetFramework>\(.*\)</TargetFramework>.*:\1:p' "$SOURCE/src/Sanakan.csproj" | tr -d '\r')}
DOTNET=${SANAKAN_DOTNET:-dotnet}
echo "fake bot framework: $TFM, dotnet $(as_user "'$DOTNET' --version" 2>/dev/null || echo 'not found') ($DOTNET)"
cat > "$REPO/src/Sanakan.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$TFM</TargetFramework>
    <AssemblyName>Sanakan</AssemblyName>
  </PropertyGroup>
</Project>
EOF
fake_program v1 false > "$REPO/src/Program.cs"
printf 'deploy/\nbin/\nobj/\nConfig.json\nfake-exit-code\n' > "$REPO/src/.gitignore"
echo '{}' > "$REPO/src/Config.json"
chmod 644 "$REPO/src/Config.json"
chown -R "$RUN_AS" "$T"
as_user "cd '$REPO' && git init -q -b master && git add -A && git -c user.name=test -c user.email=test@localhost commit -qm v1 \
    && git clone -q --bare '$REPO' '$T/origin.git' && git remote add origin '$T/origin.git' && git fetch -q origin \
    && git branch -q -u origin/master && git clone -q '$T/origin.git' '$T/dev'"
# a GOut with pictures that --data moves out of the repository; the root-owned file shows whether the install walked into it
mkdir -p "$REPO/GOut"
touch "$REPO/GOut/picture.webp"

echo "== installing $NAME.service"
"$REPO/tools/install-service.sh" --permissions --user "$RUN_AS" --data "$REPO/inside" > /dev/null 2>&1 \
    && fail "--data inside the repository was accepted" || pass "--data inside the repository refused"
if "$REPO/tools/install-service.sh" --name "$NAME" --user "$RUN_AS" --data "$T/data/GOut"; then pass "install script"; else fail "install script"; exit 1; fi

[ "$(stat -c %U "$REPO/src/Run.sh")" = "$RUN_AS" ] && pass "files belong to $RUN_AS" || fail "files do not belong to $RUN_AS"
[ "$(stat -c %a "$REPO/src/Config.json")" = 600 ] && pass "config readable only by its owner" || fail "config permissions $(stat -c %a "$REPO/src/Config.json")"
[ -L "$REPO/GOut" ] && [ "$(readlink -f "$REPO/GOut")" = "$T/data/GOut" ] && pass "GOut moved out and linked" || fail "GOut not linked to $T/data/GOut"
[ -f "$T/data/GOut/picture.webp" ] && pass "pictures moved with it" || fail "pictures lost"
[ "$(stat -c %U "$T/data/GOut")" = "$RUN_AS" ] && pass "GOut folder belongs to $RUN_AS" || fail "GOut folder belongs to $(stat -c %U "$T/data/GOut")"
[ "$(stat -c %U "$T/data/GOut/picture.webp")" = root ] && pass "files in GOut were not walked" || fail "the install walked into GOut"
grep -q "RestartPreventExitStatus=78" "/etc/systemd/system/$NAME.service" && pass "unit keeps bad config down" || fail "unit has no RestartPreventExitStatus"

echo "== start (builds first, about a minute)"
systemctl start "$NAME"
wait_for "fake bot v1 started" 1 240 && pass "built and started v1" || fail "v1 did not start"

echo "== restart after 75"
exit_bot 75
wait_for "fake bot v1 started" 2 && pass "restarted after 75" || fail "no restart after 75"

echo "== update after 100"
push_version v2 false
exit_bot 100
wait_for "fake bot v2 started" 1 240 && pass "pulled, built and started v2" || fail "v2 did not start"

echo "== broken update goes back"
push_version v3 true
exit_bot 100
wait_for "going back" 1 240 && pass "v3 crashed on start and Run.sh went back" || fail "no going back after v3"
wait_for "fake bot v2 started" 2 && pass "v2 started again" || fail "v2 did not start again"

echo "== bad config (78) stays down"
exit_bot 78
sleep 15
state=$(systemctl is-active "$NAME")
[ "$state" != active ] && [ "$(count "fake bot v2 started")" -eq 2 ] && pass "stays down after 78 ($state)" || fail "restarted after 78 ($state)"

echo "== stop (0) stays down"
systemctl start "$NAME"
wait_for "fake bot v2 started" 3 && pass "starts again by hand" || fail "did not start by hand"
exit_bot 0
sleep 10
[ "$(systemctl is-active "$NAME")" != active ] && [ "$(count "fake bot v2 started")" -eq 3 ] && pass "stays down after 0" || fail "restarted after 0"

echo "== journal of the run"
journal | grep -E "Run:|fake bot" | tail -40

echo
if [ "$FAILED" -eq 0 ]; then echo "everything passed"; else echo "$FAILED checks failed"; fi
exit "$FAILED"
