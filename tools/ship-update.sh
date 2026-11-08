#!/usr/bin/env bash
# Builds the game for macOS and Windows at a version, packs both with Velopack
# (a full package, a delta from the last published release, installers) and
# uploads them to one GitHub release. Installed copies find it on their next
# launch, download the delta and restart into it (SelfUpdate.cs).
# docs/RELEASING.md, "Updates in place", has the whole story.
#
#   tools/ship-update.sh 0.1.3              # the alpha-0.1.3 release, left as a draft
#   tools/ship-update.sh 0.1.3 --test       # update-test-0.1.3 on the test channels, published
#   tools/ship-update.sh 0.1.3 --skip-build # pack and upload what Builds/Update already holds
#   tools/ship-update.sh 0.1.3 --windows-only # no macOS (the PC's half of a release)
#   tools/ship-update.sh 0.1.3 --mac-only     # no Windows (the Mac's half of a release)
#   tools/ship-update.sh 0.1.3 --linux-only   # the Linux zip for the same release (J-051)
#   tools/ship-update.sh 0.1.3 --dry-run      # say what each delta would be made from; change nothing
#
# A release can be built in two halves on two machines from the same commit
# (J-025): each uploads only its own platform's files into the one draft. The
# first to upload creates the draft; the other refuses if that draft targets a
# different commit, or if a race left two releases with the same tag.
#
# Each delta is made from the full package of the newest *published* release
# before this version that has that channel's feed, never from a draft (this
# version's own included). Rebuilding a version the draft already holds replaces
# all of that channel's files: what the new pack didn't make is deleted, so the
# draft never mixes two builds.
#
# --test uses the channels osx-test and win-test and the app id
# SpellChemistryValleyTest, which only test installs have, so trying the update
# loop never reaches players or a real install on the same machine. Needs: the Unity
# Editor open on this project (clean tree, the commit to ship checked out),
# vpk (dotnet tool install -g vpk) and gh signed in.
#
# --linux-only is the other way round: it builds in batch mode, so no Editor may
# be open on this project, and needs neither vpk nor a Velopack channel. Linux
# players download each release: SpellChemistryValley-linux-x64.zip goes into the
# same draft and there are no in-place updates. It takes about 50 minutes.
set -euo pipefail

here="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-$(command -v unity || echo "$HOME/.unity/bin/unity")}"
VPK="${VPK:-$(command -v vpk || echo "$HOME/.dotnet/tools/vpk")}"
# Where the .NET runtime is, for vpk: ~/.dotnet when .NET was installed there (the Mac),
# not when it only holds global tools (Windows, where .NET is in Program Files).
{ [ -x "$HOME/.dotnet/dotnet" ] || [ -x "$HOME/.dotnet/dotnet.exe" ]; } && export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export UNITY_NO_BANNER=1 UNITY_NO_PAGER=1 UNITY_NON_INTERACTIVE=1
export UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-$here/unity}"
# The Editor itself, for batch mode (--linux-only): the wrapper above talks to an open one.
UNITY_BIN="${UNITY_BIN:-/Applications/Unity/Hub/Editor/$(sed -n 's/^m_EditorVersion: //p' "$UNITY_PROJECT_PATH/ProjectSettings/ProjectVersion.txt")/Unity.app/Contents/MacOS/Unity}"
REPO=https://github.com/anweather/5e-compatible-game
APP_ID=SpellChemistryValley
TITLE="Spell Chemistry Valley"

version="${1:-}"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "usage: tools/ship-update.sh X.Y.Z [--test] [--skip-build] [--dry-run] [--windows-only | --mac-only | --linux-only]" >&2
  exit 2
fi
shift
test=0; build=1; mac=1; win=1; linux=0; dry=0
for arg in "$@"; do
  case "$arg" in
    --test) test=1 ;;
    --skip-build) build=0 ;;
    --dry-run) dry=1 ;;
    --windows-only) mac=0 ;;
    --mac-only) win=0 ;;
    --linux-only) mac=0; win=0; linux=1 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done
if [ $mac = 0 ] && [ $win = 0 ] && [ $linux = 0 ]; then echo "--windows-only and --mac-only together leave nothing to ship" >&2; exit 2; fi
if [ $test = 1 ] && [ $linux = 1 ]; then echo "--linux-only has no test channel" >&2; exit 2; fi
case "$mac$win" in 11) platforms="macOS and Windows" ;; 10) platforms=macOS ;; 01) platforms=Windows ;; *) platforms=Linux ;; esac

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
out="$here/unity/Builds/Update"
step() { echo; echo "== $*"; }
channels=(); [ $mac = 0 ] || channels+=("osx$suffix"); [ $win = 0 ] || channels+=("win$suffix")

# The version in a tag (alpha-0.1.11, update-test-0.1.107), or nothing.
tag_version() { local v="${1##*-}"; [[ "$v" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] && echo "$v"; }
# Whether version $1 comes before version $2.
older() {
  local a b i
  IFS=. read -r -a a <<<"$1"; IFS=. read -r -a b <<<"$2"
  for i in 0 1 2; do
    [ "${a[i]}" -lt "${b[i]}" ] && return 0
    [ "${a[i]}" -gt "${b[i]}" ] && return 1
  done
  return 1
}
# The release a channel's delta is made from: the newest published one before this
# version with that channel's feed. Not `vpk download --pre`: signed in, that sees
# drafts too, and took this version's own draft as the base (0.1.12, no delta made).
base_tag() { # channel
  local best="" bestv="" tags t v
  # (A failure here must stop the script, not ship a full package only.)
  tags="$(gh api "repos/${REPO#https://github.com/}/releases" --paginate \
    --jq ".[] | select(.draft | not) | select(any(.assets[]; .name == \"releases.$1.json\")) | .tag_name")" || return 1
  while IFS= read -r t; do
    v="$(tag_version "$t")" || continue
    older "$v" "$version" || continue
    if [ -z "$bestv" ] || older "$bestv" "$v"; then best="$t"; bestv="$v"; fi
  done <<<"$tags"
  echo "$best"
}
# The files a feed (on stdin) lists for a version. vpk writes it on one line.
feed_files() { # version
  grep -o '{[^{}]*}' | grep "\"Version\":\"$1\"" | sed -n 's/.*"FileName":"\([^"]*\)".*/\1/p' || true
}
# What this version's release already holds for a channel (from an earlier build).
draft_files() { # channel
  { gh release download "$tag" -R "$REPO" -p "releases.$1.json" -O - 2>/dev/null || true; } | feed_files "$version"
}

if [ $dry = 1 ]; then
  for channel in ${channels[@]+"${channels[@]}"}; do
    base="$(base_tag "$channel")"
    echo "$channel: $version's delta from ${base:-nothing (no published release before $version has releases.$channel.json: full package only)}"
    held="$(draft_files "$channel")"
    [ -z "$held" ] || echo "$channel: $tag already holds $(echo "$held" | tr '\n' ' ')for $version: all replaced"
  done
  exit 0
fi

# The release the halves upload into. Made (or checked) before the build, so a second
# machine started a little later finds it, and checked again before uploading.
# How many releases (drafts included) carry this tag: two means both halves created one at once.
same_tag() { gh release list -R "$REPO" --limit 100 --json tagName --jq "[.[] | select(.tagName == \"$tag\")] | length"; }
check_draft() {
  if gh release view "$tag" -R "$REPO" >/dev/null 2>&1; then
    # The other half got there first: it has to be the same commit, or the two halves don't match.
    local target
    target="$(gh release view "$tag" -R "$REPO" --json targetCommitish --jq .targetCommitish)"
    if [ "$target" != "$commit" ]; then
      echo "$tag already targets ${target:0:7}, not ${commit:0:7}: build both halves from the same commit" >&2
      exit 1
    fi
  else
    # shellcheck disable=SC2086 # $draft is one flag or nothing
    gh release create "$tag" -R "$REPO" --target "$commit" --prerelease $draft \
      --title "$name" --notes "In-place update $version for macOS and Windows (Velopack)."
    echo "Release $tag is ready at ${commit:0:7}: the other half can start now."
  fi
  local count
  count="$(same_tag)"
  if [ "$count" -gt 1 ]; then
    echo "$count releases are tagged $tag (both halves created one): delete the extra draft on GitHub, then run again with --skip-build" >&2
    exit 1
  elif [ "$count" -lt 1 ]; then
    echo "no release is tagged $tag on GitHub after creating it" >&2
    exit 1
  fi
}

step "Checking the release $tag"
check_draft

if [ $build = 1 ] && [ $linux = 0 ]; then
  step "Building $version for $platforms at ${commit:0:7}"
  # Pick up the checked-out code before building (a build on stale code rewrites assets).
  "$UNITY" command eval 'UnityEditor.AssetDatabase.Refresh(); return "refreshed";' --result-only >/dev/null
  sleep 5
  "$UNITY" status --until-ready >/dev/null
  result=$("$UNITY" command eval "return UpdateBuild.Players(\"$version\", $([ $mac = 1 ] && echo true || echo false), $([ $win = 1 ] && echo true || echo false));" --result-only --timeout 3600)
  echo "$result" | grep -o '"result": *"[^"]*"' || echo "$result"
  echo "$result" | grep -q 'Update build: OK' || { echo "build failed" >&2; exit 1; }
fi

linux_dir="$UNITY_PROJECT_PATH/Builds/Linux"
linux_zip="$out/SpellChemistryValley-linux-x64.zip"
if [ $build = 1 ] && [ $linux = 1 ]; then
  step "Building $version for Linux at ${commit:0:7} (batch mode, about 50 minutes)"
  mkdir -p "$out"
  rm -rf "$linux_dir"
  log="$out/linux-build.log"
  "$UNITY_BIN" -batchmode -nographics -quit -projectPath "$UNITY_PROJECT_PATH" -buildTarget Linux64 \
    -executeMethod LinuxBuild.FromCommandLine -rebuild-yard -build-version "$version" \
    -expect-commit "${commit:0:7}" -logFile "$log" \
    || { echo "Unity stopped with an error (is an Editor open on this project?): $log" >&2; exit 1; }
  grep -q 'HEADLESS BUILD: OK' "$log" || { echo "build failed: $log" >&2; exit 1; }
fi

if [ $linux = 1 ]; then
  step "Zipping $version for Linux"
  # The host turns away a player whose stamp differs, so a stale folder (--skip-build) mustn't ship.
  stamp="$linux_dir/SpellChemistryValley_Data/StreamingAssets/build.json"
  [ -f "$stamp" ] || { echo "no Linux build at $linux_dir" >&2; exit 1; }
  grep -q "\"version\": *\"$version\"" "$stamp" && grep -q "\"commit\": *\"${commit:0:7}\"" "$stamp" \
    || { echo "the Linux build isn't $version at ${commit:0:7}: $(cat "$stamp")" >&2; exit 1; }
  chmod +x "$linux_dir/SpellChemistryValley.x86_64"
  cp "$here/LICENSE.md" "$here/NOTICE.md" "$linux_dir/"
  sed "s/{VERSION}/$version/g" "$here/tools/README-LINUX.txt" > "$linux_dir/README-LINUX.txt"
  rm -f "$linux_zip"
  (cd "$linux_dir" && zip -qrX "$linux_zip" . -x '*_DoNotShip/*' '*.DS_Store')
fi

pack() { # channel, extra vpk args...
  local channel="$1"; shift
  local dir="$out/releases-$channel"
  rm -rf "$dir"
  mkdir -p "$dir"
  step "Fetching the last published $channel release (for the delta)"
  local base bv entry file
  # (pack runs after ||, where set -e doesn't stop it: hence each || exit.)
  base="$(base_tag "$channel")" || exit 1
  if [ -z "$base" ]; then
    echo "(no published release before $version has releases.$channel.json: this one ships as a full package only)"
  else
    # Lay it out as vpk download would: a feed of the base's full package, and the package.
    bv="$(tag_version "$base")"
    entry="$(gh release download "$base" -R "$REPO" -p "releases.$channel.json" -O - \
      | grep -o '{[^{}]*}' | grep '"Type":"Full"' | grep "\"Version\":\"$bv\"" | head -n 1 || true)"
    file="$(echo "$entry" | sed -n 's/.*"FileName":"\([^"]*\)".*/\1/p')"
    [ -n "$file" ] || { echo "$base's releases.$channel.json lists no full package for $bv" >&2; exit 1; }
    echo "$base: $file"
    gh release download "$base" -R "$REPO" -p "$file" -D "$dir" || exit 1
    echo "{\"Assets\":[$entry]}" > "$dir/releases.$channel.json"
    echo "$base" > "$dir/base"
  fi
  step "Packing $version for $channel"
  "$VPK" "$@" -u "$APP_ID" -v "$version" --packTitle "$TITLE" --packAuthors anweather -c "$channel" -o "$dir" --yes
}

[ $mac = 0 ] || pack "osx$suffix" pack -p "$out/osx/$TITLE.app" -e "$TITLE"
[ $win = 0 ] || pack "win$suffix" "[win]" pack -r win-x64 -p "$out/win" -e SpellChemistryValley.exe

# Upload with gh rather than `vpk upload github`: vpk finds a release by its tag,
# which GitHub doesn't do for drafts, and a real update is a draft until it's tested.
step "Uploading to $tag"
check_draft
# (The odd expansion: bash 3.2 on macOS calls an empty array unbound under set -u.)
for channel in ${channels[@]+"${channels[@]}"}; do
  dir="$out/releases-$channel"
  # The feed plus what this pack made (assets.<channel>.json lists it: the full and
  # delta packages, the installer, the portable zip). Package names differ by
  # channel, so read them rather than guess; the folder also holds the previous
  # full package, fetched only to make the delta.
  files=()
  while IFS= read -r f; do files+=("$dir/$f"); done < <(grep -o '"RelativeFileName":"[^"]*"' "$dir/assets.$channel.json" | cut -d'"' -f4)
  if [ -f "$dir/base" ] && ! printf '%s\n' "${files[@]}" | grep -q -- '-delta\.nupkg$'; then
    echo "$channel: no delta was made from $(cat "$dir/base"): nothing uploaded" >&2
    exit 1
  fi
  files+=("$dir/releases.$channel.json") # the feed last, once what it lists is up
  held="$(draft_files "$channel")"
  echo "$channel: ${files[*]##*/}"
  gh release upload "$tag" -R "$REPO" --clobber "${files[@]}"
  # An earlier build's files this pack didn't make (a delta from another base, say) go.
  # shellcheck disable=SC2086 # one name a line, none with spaces
  for f in $held; do
    case " ${files[*]##*/} " in
      *" $f "*) ;;
      *) echo "$channel: deleting $f (an earlier build of $version)"; gh release delete-asset "$tag" "$f" -R "$REPO" --yes ;;
    esac
  done
done

if [ $linux = 1 ]; then
  echo "linux: ${linux_zip##*/}"
  gh release upload "$tag" -R "$REPO" --clobber "$linux_zip"
fi

step "Done"
echo "$REPO/releases/tag/$tag"
[ $test = 1 ] || echo "It's a draft: test it, then publish (docs/RELEASING.md)."
