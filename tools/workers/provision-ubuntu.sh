#!/usr/bin/env bash
# Runs INSIDE the Ubuntu 24.04 template VM, as the normal user (it uses sudo):
#
#   curl -fsSL https://raw.githubusercontent.com/anweather/5e-compatible-game/dev/tools/workers/provision-ubuntu.sh | bash
#
# Installs git and git-lfs, the .NET 8 SDK, Node 22, Unity Hub and Editor
# 6000.3.27f1 (+ Windows and Mac Build Support, Mono), Claude Code, and
# clones the repo (branch dev) to ~/5e-compatible-game. Official sources only:
# the Ubuntu archive, nodejs.org, Unity's apt repo, claude.ai.
# Safe to re-run: each step skips what is already there.
set -euo pipefail
UNITY_VERSION=6000.3.27f1
UNITY_CHANGESET=672d562a42a7
REPO_DIR=$HOME/5e-compatible-game

step() { printf '\n== %s\n' "$*"; }

step "apt packages: git, git-lfs, .NET 8 SDK, xvfb (Unity Hub runs headless under it), Hyper-V KVP daemon (host sees the IP)"
sudo apt-get update
sudo apt-get install -y git git-lfs curl ca-certificates gnupg xz-utils xvfb dotnet-sdk-8.0 linux-cloud-tools-virtual
git lfs install

step "Node 22 from nodejs.org"
if ! command -v node >/dev/null || [[ $(node -v) != v22.* ]]; then
  base=https://nodejs.org/dist/latest-v22.x
  tmp=$(mktemp -d)
  curl -fsSL "$base/SHASUMS256.txt" -o "$tmp/SHASUMS256.txt"
  tarball=$(grep -o 'node-v22[^ ]*-linux-x64\.tar\.xz' "$tmp/SHASUMS256.txt" | head -1)
  curl -fsSL "$base/$tarball" -o "$tmp/$tarball"
  (cd "$tmp" && grep " $tarball\$" SHASUMS256.txt | sha256sum -c -)
  sudo tar -xJf "$tmp/$tarball" -C /usr/local --strip-components=1
  rm -rf "$tmp"
fi
node -v

step "Unity Hub (Unity's apt repo)"
if ! command -v unityhub >/dev/null; then
  curl -fsSL https://hub.unity3d.com/linux/keys/public | sudo gpg --dearmor --yes -o /usr/share/keyrings/Unity_Technologies_ApS.gpg
  echo "deb [signed-by=/usr/share/keyrings/Unity_Technologies_ApS.gpg] https://hub.unity3d.com/linux/repos/deb stable main" \
    | sudo tee /etc/apt/sources.list.d/unityhub.list >/dev/null
  sudo apt-get update
  sudo apt-get install -y unityhub
fi

step "Unity Editor $UNITY_VERSION with Windows Build Support (Mono)"
hub() { xvfb-run -a unityhub -- --headless "$@"; }
hub install --version "$UNITY_VERSION" --changeset "$UNITY_CHANGESET" --module windows-mono || echo "!! Editor install reported an error; check: unityhub -- --headless editors -i"
step "Mac Build Support (Mono), if it installs cleanly"
hub install-modules --version "$UNITY_VERSION" --module mac-mono || echo "!! mac-mono did not install; Mac builds stay on the Mac"

step "Claude Code (official installer; not signed in)"
command -v claude >/dev/null || curl -fsSL https://claude.ai/install.sh | bash

step "The repo, branch dev"
[ -d "$REPO_DIR/.git" ] || git clone -b dev https://github.com/anweather/5e-compatible-game.git "$REPO_DIR"

step "Check: core builds and tests, sandbox tests"
(cd "$REPO_DIR" && dotnet build core -c Release -warnaserror -v q && dotnet test core -v q) || echo "!! core build or tests failed"
(cd "$REPO_DIR/sandbox" && npm ci --no-audit --no-fund && npm test) || echo "!! sandbox tests failed"

step "Done. Andrew's steps next: README U3 (Unity licence, GitHub, Claude sign-in), then U4 (seal)."
