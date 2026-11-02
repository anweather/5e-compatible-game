#!/usr/bin/env bash
# land.sh: how the coordinator lands a worker's branch on dev, in its own worktree.
#   land.sh auto <branch> [--compiled] [--deletes] [--dry] < message
#                                      the whole landing, printing only what matters: refuses on
#                                      deletions, heavy removals, C# nobody compiled in Unity
#                                      (--compiled says DesktopUnity or the Mac did), a conflict, or
#                                      a merge touching files the branch doesn't own; runs every
#                                      gate; pushes, and merges again if dev moved. --deletes
#                                      accepts deletions and heavy removals you've read. --dry
#                                      stops short of pushing and leaves the worktree on dev.
#   land.sh merge <branch> < message   fetch, reset this worktree's branch to origin/dev, merge
#                                      origin/<branch> (--no-ff) with the message plus the
#                                      coordinator's cost lines (commit-cost.mjs, Coordinator-*)
#   (run the checks: core build, tests and format; sandbox typecheck, tests and build)
#   land.sh push <branch>              push to dev (refused if dev moved: merge again), then
#                                      delete the merged branch on origin (Andrew's rule: always)
# Set CLAUDE_CONFIG_DIR to the coordinator's account (~/.claude-pro) so its cost is read from there.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cmd="${1:-}"; branch="${2:-}"
[[ -n "$cmd" && -n "$branch" ]] || { sed -n 2,17p "$0"; exit 2; }
git fetch -q origin
case "$cmd" in
  merge)
    [[ -z "$(git status --porcelain)" ]] || { echo "This worktree has changes; not merging."; exit 1; }
    git switch -q -C "$(git branch --show-current || echo coordinator)" origin/dev
    git merge -q --no-ff --no-commit "origin/$branch"
    msg="$(git rev-parse --git-dir)/LAND_MSG"
    cat > "$msg"
    COMMIT_COST_ROLE=Coordinator COMMIT_COST_NAME="${COMMIT_COST_NAME:-coordinator}" node "$here/commit-cost.mjs" "$msg"
    git commit -q -F "$msg" && rm "$msg"
    git log -1 --format='%h %s' ;;
  auto)
    dry=0; compiled=0; deletes=0
    for a in "${@:3}"; do
      case "$a" in
        --dry) dry=1 ;; --compiled) compiled=1 ;; --deletes) deletes=1 ;;
        *) echo "Unknown option: $a"; exit 2 ;;
      esac
    done
    stop() { echo "STOP: $*"; exit 1; }
    [[ -z "$(git status --porcelain)" ]] || stop "this worktree has changes."
    git merge-base --is-ancestor HEAD origin/dev || stop "this worktree has commits that aren't on dev: push them first."
    git rev-parse -q --verify "origin/$branch" >/dev/null || stop "no origin/$branch."
    range="origin/dev...origin/$branch"
    own="$(git diff --name-only "$range")"
    echo "$branch:$(git diff --shortstat "$range")"
    if [[ $deletes == 0 ]]; then
      gone="$(git diff --name-only --diff-filter=D "$range")"
      [[ -z "$gone" ]] || stop "the branch deletes files; read them, then pass --deletes:"$'\n'"$gone"
      heavy="$(git diff --numstat "$range" | awk '$1 != "-" && $2 >= 100 && $2 > 2 * $1 { print $2 " lines out of " $3 }')"
      [[ -z "$heavy" ]] || stop "heavy removals; read them, then pass --deletes:"$'\n'"$heavy"
    fi
    if [[ $compiled == 0 ]] && grep -qE '^unity/.*\.cs$' <<< "$own"; then
      stop "the branch changes Unity C#; pass --compiled once a machine with Unity has compiled it."
    fi
    message="$(cat)"
    log="$(git rev-parse --absolute-git-dir)/land-gates.log"
    undo() { git reset -q --hard origin/dev; }
    gate() {
      local name="$1"; shift
      if "$@" >> "$log" 2>&1; then echo "  ok  $name"
      else tail -30 "$log"; undo; stop "$name failed; the full log is $log; the worktree is back on dev."; fi
    }
    for try in 1 2 3; do
      if ! printf '%s\n' "$message" | bash "$0" merge "$branch" > /dev/null 2>&1; then
        clash="$(git status --short | grep -E '^(UU|AA|DD|AU|UA|DU|UD)' || true)"
        git merge --abort 2>/dev/null || true
        undo
        stop "the merge failed; have the branch's owner merge dev in. Conflicts:"$'\n'"$clash"
      fi
      foreign="$(git diff --name-only origin/dev HEAD | grep -vxFf <(printf '%s\n' "$own") || true)"
      if [[ -n "$foreign" ]]; then
        undo
        stop "the merge changes files outside the branch's own diff:"$'\n'"$foreign"
      fi
      : > "$log"
      gate "core build" dotnet build core -c Release -warnaserror
      gate "core tests" dotnet test core
      gate "core format" dotnet format core --verify-no-changes
      gate "duplicates" npx --yes jscpd@4.0.5 unity/Assets/Prototype sandbox/src
      gate "chat report" node --test tools/chat-report.test.mjs
      gate "sandbox types" npm --prefix sandbox run typecheck
      gate "sandbox tests" npm --prefix sandbox test
      gate "sandbox build" npm --prefix sandbox run build
      grep -E 'Passed!|Failed!' "$log" | tail -1 | sed 's/^ */  /'
      if [[ $dry == 1 ]]; then undo; echo "Dry run: $branch would land. Nothing pushed."; exit 0; fi
      git fetch -q origin
      if git merge-base --is-ancestor origin/dev HEAD; then bash "$0" push "$branch"; exit 0; fi
      echo "dev moved during the gates: merging again."
      undo
    done
    stop "dev kept moving; nothing pushed." ;;
  push)
    git merge-base --is-ancestor origin/dev HEAD || { echo "dev moved since the merge: run land.sh merge again."; exit 1; }
    git merge-base --is-ancestor "origin/$branch" HEAD || { echo "HEAD doesn't contain $branch."; exit 1; }
    git push -q origin HEAD:dev
    git push -q origin --delete "$branch"
    git branch -q -D "$branch" 2>/dev/null || true
    echo "Landed $branch on dev at $(git rev-parse --short HEAD) and deleted the branch." ;;
  *) sed -n 2,17p "$0"; exit 2 ;;
esac
