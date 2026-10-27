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
#   tools/ship-update.sh 0.1.3 --windows-only # no macOS (an Editor without Mac Build Support)
#
# --test uses the channels osx-test and win-test and the app id
# SpellChemistryValleyTest, which only test installs have, so trying the update
# loop never reaches players or a real install on the same machine. Needs: the Unity
# Editor open on this project (clean tree, the commit to ship checked out),
# vpk (dotnet tool install -g vpk) and gh signed in.
set -euo pipefail

here="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-$(command -v unity || echo "$HOME/.unity/bin/unity")}"
VPK="${VPK:-$(command -v vpk || echo "$HOME/.dotnet/tools/vpk")}"
# Where the .NET runtime is, for vpk: ~/.dotnet when .NET was installed there (the Mac),
# not when it only holds global tools (Windows, where .NET is in Program Files).
{ [ -x "$HOME/.dotnet/dotnet" ] || [ -x "$HOME/.dotnet/dotnet.exe" ]; } && export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export UNITY_NO_BANNER=1 UNITY_NO_PAGER=1 UNITY_NON_INTERACTIVE=1
export UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-$here/unity}"
REPO=https://github.com/anweather/5e-compatible-game
APP_ID=SpellChemistryValley
TITLE="Spell Chemistry Valley"

version="${1:-}"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "usage: tools/ship-update.sh X.Y.Z [--test] [--skip-build] [--windows-only]" >&2
  exit 2
fi
shift
test=0; build=1; mac=1
for arg in "$@"; do
  case "$arg" in
    --test) test=1 ;;
    --skip-build) build=0 ;;
    --windows-only) mac=0 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

if [ $test = 1 ]; then
  # Its own app id too: test installs get their own install folder and update cache,
  # so a test package can never be applied over a real install (0.1.102 > 0.1.2).
  suffix=-test; tag="update-test-$version"; name="Update test $version (not for players)"; draft=
  APP_ID=SpellChemistryValleyTest
else
  suffix=; tag="alpha-$version"; name="alpha $version"; draft=--draft
fi
commit="$(git -C "$here" rev-parse HEAD)"
# GitHub tags the release at this commit, so it has to be pushed.
if [ -z "$(git -C "$here" branch -r --contains "$commit" 2>/dev/null)" ]; then
  echo "commit ${commit:0:7} isn't on GitHub yet: push it first (the release is tagged at it)" >&2
  exit 1
fi
token="${GITHUB_TOKEN:-$(gh auth token)}" # for vpk download
out="$here/unity/Builds/Update"
step() { echo; echo "== $*"; }

if [ $build = 1 ]; then
  step "Building $version for $([ $mac = 1 ] && echo "macOS and ")Windows at ${commit:0:7}"
  # Pick up the checked-out code before building (a build on stale code rewrites assets).
  "$UNITY" command eval 'UnityEditor.AssetDatabase.Refresh(); return "refreshed";' --result-only >/dev/null
  sleep 5
  "$UNITY" status --until-ready >/dev/null
  result=$("$UNITY" command eval "return UpdateBuild.Players(\"$version\", $([ $mac = 1 ] && echo true || echo false));" --result-only --timeout 3600)
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

[ $mac = 0 ] || pack "osx$suffix" pack -p "$out/osx/$TITLE.app" -e "$TITLE"
pack "win$suffix" "[win]" pack -r win-x64 -p "$out/win" -e SpellChemistryValley.exe

# Upload with gh rather than `vpk upload github`: vpk finds a release by its tag,
# which GitHub doesn't do for drafts, and a real update is a draft until it's tested.
step "Uploading to $tag"
if ! gh release view "$tag" -R "$REPO" >/dev/null 2>&1; then
  # shellcheck disable=SC2086 # $draft is one flag or nothing
  gh release create "$tag" -R "$REPO" --target "$commit" --prerelease $draft \
    --title "$name" --notes "In-place update $version for macOS and Windows (Velopack)."
fi
channels=("win$suffix"); [ $mac = 0 ] || channels=("osx$suffix" "win$suffix")
for channel in "${channels[@]}"; do
  dir="$out/releases-$channel"
  # The feed plus what this pack made (assets.<channel>.json lists it: the full and
  # delta packages, the installer, the portable zip). Package names differ by
  # channel, so read them rather than guess; the folder also holds the previous
  # full package, fetched only to make the delta.
  files=("$dir/releases.$channel.json")
  while IFS= read -r f; do files+=("$dir/$f"); done < <(grep -o '"RelativeFileName":"[^"]*"' "$dir/assets.$channel.json" | cut -d'"' -f4)
  echo "$channel: ${files[*]##*/}"
  gh release upload "$tag" -R "$REPO" --clobber "${files[@]}"
done

step "Done"
echo "$REPO/releases/tag/$tag"
[ $test = 1 ] || echo "It's a draft: test it, then publish (docs/RELEASING.md)."
