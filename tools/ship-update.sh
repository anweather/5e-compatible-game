#!/usr/bin/env bash
# Builds the game for macOS and Windows at a version, packs both with Velopack
# (a full package, a delta from the last published update, installers) and
# uploads them to one GitHub release. Installed copies find it on their next
# launch, download the delta and restart into it (SelfUpdate.cs).
# docs/RELEASING.md, "Updates in place", has the whole story.
#
#   tools/ship-update.sh 0.1.3              # the alpha-0.1.3 release, left as a draft
#   tools/ship-update.sh 0.1.3 --test       # update-test-0.1.3 on the test channels, published
#   tools/ship-update.sh 0.1.3 --skip-build # pack and upload what Builds/Update already holds
#
# --test uses the channels osx-test and win-test, which only test installs
# follow, so trying the update loop never reaches players. Needs: the Unity
# Editor open on this project (clean tree, the commit to ship checked out),
# vpk (dotnet tool install -g vpk) and gh signed in.
set -euo pipefail

here="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-$(command -v unity || echo "$HOME/.unity/bin/unity")}"
VPK="${VPK:-$(command -v vpk || echo "$HOME/.dotnet/tools/vpk")}"
[ -d "$HOME/.dotnet" ] && export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export UNITY_NO_BANNER=1 UNITY_NO_PAGER=1 UNITY_NON_INTERACTIVE=1
export UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-$here/unity}"
REPO=https://github.com/anweather/5e-compatible-game
APP_ID=SpellChemistryValley
TITLE="Spell Chemistry Valley"

version="${1:-}"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "usage: tools/ship-update.sh X.Y.Z [--test] [--skip-build]" >&2
  exit 2
fi
shift
test=0; build=1
for arg in "$@"; do
  case "$arg" in
    --test) test=1 ;;
    --skip-build) build=0 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

if [ $test = 1 ]; then
  suffix=-test; tag="update-test-$version"; name="Update test $version (not for players)"; publish=--publish
else
  suffix=; tag="alpha-$version"; name="alpha $version"; publish=
fi
commit="$(git -C "$here" rev-parse HEAD)"
token="${GITHUB_TOKEN:-$(gh auth token)}"
out="$here/unity/Builds/Update"
step() { echo; echo "== $*"; }

if [ $build = 1 ]; then
  step "Building $version for macOS and Windows at ${commit:0:7}"
  # Pick up the checked-out code before building (a build on stale code rewrites assets).
  "$UNITY" command eval 'UnityEditor.AssetDatabase.Refresh(); return "refreshed";' --result-only >/dev/null
  sleep 5
  "$UNITY" status --until-ready >/dev/null
  result=$("$UNITY" command eval "return UpdateBuild.Players(\"$version\");" --result-only --timeout 3600)
  echo "$result" | grep -o '"result": *"[^"]*"' || echo "$result"
  echo "$result" | grep -q 'Update build: OK' || { echo "build failed" >&2; exit 1; }
fi

pack() { # channel, extra vpk args...
  local channel="$1"; shift
  local dir="$out/releases-$channel"
  rm -rf "$dir"
  step "Fetching the last $channel update (for the delta)"
  "$VPK" download github --repoUrl "$REPO" --token "$token" --pre -c "$channel" -o "$dir" \
    || echo "(none yet: this one ships as a full package only)"
  step "Packing $version for $channel"
  "$VPK" "$@" -u "$APP_ID" -v "$version" --packTitle "$TITLE" --packAuthors anweather -c "$channel" -o "$dir" --yes
}

pack "osx$suffix" pack -p "$out/osx/$TITLE.app" -e "$TITLE"
pack "win$suffix" "[win]" pack -r win-x64 -p "$out/win" -e SpellChemistryValley.exe

for channel in "osx$suffix" "win$suffix"; do
  step "Uploading $channel to $tag"
  # shellcheck disable=SC2086 # $publish is one flag or nothing
  "$VPK" upload github -o "$out/releases-$channel" -c "$channel" --repoUrl "$REPO" --token "$token" \
    --pre $publish --merge --tag "$tag" --releaseName "$name" --targetCommitish "$commit"
done

step "Done"
echo "$REPO/releases/tag/$tag"
[ $test = 1 ] || echo "It's a draft: test it, then publish (docs/RELEASING.md)."
