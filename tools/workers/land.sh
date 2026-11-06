#!/usr/bin/env bash
# land.sh: how a branch lands on dev: a worker's own (here), or the coordinator's in its own worktree (auto).
#   land.sh here [--compiled] [--deletes] [--dry] < message
#                                      a worker lands the branch it's on (D15), from its own checkout on any
#                                      machine: pushes it, then runs auto from a landing worktree beside the
#                                      checkout (<checkout>-land, or $LAND_WORKTREE) with dev's land.sh, so a
#                                      branch can't change its own landing. --compiled only where queue.mjs caps
#                                      says unity: Unity C# from a machine without it stops, for the PC to compile.
#                                      A refusal prints STOP and exits 1, and goes to the PM: into
#                                      ~/.worker/outbox (bridge-watch.sh mails it) or, without one, bridge.sh send pm.
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
#   (run the checks: gates.sh; the sandbox's only if the branch touches it)
#   land.sh push <branch>              push to dev (refused if dev moved: merge again), then
#                                      delete the merged branch on origin (Andrew's rule: always)
# A branch from the queue deletes its claim (docs/handoffs/briefs/queue/taken/<f>, its Branch: line) as it lands:
# that deletion needs no --deletes, and a branch that leaves its claim behind is refused.
# Set CLAUDE_CONFIG_DIR to the coordinator's account (~/.claude-pro) so its cost is read from there.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cmd="${1:-}"; branch="${2:-}"
usage() { sed -n 2,27p "$0"; exit 2; }
[[ -n "$cmd" && ( -n "$branch" || "$cmd" == here ) ]] || usage
git fetch -q origin
taken=docs/handoffs/briefs/queue/taken
case "$cmd" in
  here)
    name="$(cut -d' ' -f1 /var/lib/worker/name.txt C:/ProgramData/worker/name.txt 2>/dev/null | head -1 || true)"
    name="${name:-$(hostname)}"
    branch="$(git branch --show-current)"
    out="$(mktemp)"; trap 'rm -f "$out"' EXIT
    refuse() { # the refusal, printed and sent to the PM: the only landing the PM hears about
      local why="$1" report id
      echo "STOP: $why"
      report="# $name reports: landing ${branch:-?} refused"$'\n\n'"land.sh here refused to land ${branch:-?} from $name ($(git rev-parse --short HEAD 2>/dev/null)):"$'\n\n'"$why"
      if [[ -d "$HOME/.worker/outbox" ]]; then
        id="$(date +%Y%m%d-%H%M%S)-land-${branch//[^A-Za-z0-9-]/-}"
        printf '%s\n' "$report" > "$HOME/.worker/outbox/.$id.tmp" && mv "$HOME/.worker/outbox/.$id.tmp" "$HOME/.worker/outbox/$id.md"
        echo "(Sent to the PM through this worker's outbox.)"
      elif [[ -z "${LAND_NO_MAIL:-}" ]]; then
        printf '%s\n' "$report" | BRIDGE_FROM="$name" bash "$here/bridge.sh" send pm "landing ${branch:-?} refused" || echo "(Couldn't mail the PM: tell it yourself.)"
      fi
      exit 1
    }
    flags=(); compiled=0
    for a in "${@:2}"; do
      case "$a" in
        --dry|--deletes) flags+=("$a") ;; --compiled) compiled=1 ;;
        *) echo "Unknown option: $a"; exit 2 ;;
      esac
    done
    [[ -n "$branch" ]] || refuse "the checkout isn't on a branch."
    [[ "$branch" != dev && "$branch" != main ]] || refuse "the checkout is on $branch: land a feature branch."
    [[ -z "$(git status --porcelain)" ]] || refuse "the checkout has changes: commit them (or stash what isn't the branch's) first."$'\n'"$(git status --short | head -20)"
    if [[ $compiled == 1 ]]; then
      node "$here/queue.mjs" caps | grep -qx unity \
        || refuse "--compiled, but this machine has no Unity (queue.mjs caps): the branch goes to the mailbox for the PC to compile."
      flags+=(--compiled)
    elif git diff --name-only origin/dev...HEAD | grep -qE '^unity/.*\.cs$'; then
      refuse "the branch changes Unity C#, and nobody has compiled it in Unity: it goes to the mailbox for the PC (or a machine with Unity) to compile, then land with --compiled."
    fi
    git push -q origin "HEAD:refs/heads/$branch" 2>"$out" || refuse "pushing $branch failed: $(cat "$out")"
    main="$(dirname "$(git rev-parse --path-format=absolute --git-common-dir)")"
    wt="${LAND_WORKTREE:-$main-land}"
    if [[ ! -e "$wt" ]]; then
      git worktree add -q -B landing "$wt" origin/dev || refuse "couldn't make the landing worktree $wt."
    elif [[ "$(git -C "$wt" rev-parse --path-format=absolute --git-common-dir 2>/dev/null)" != "$(git rev-parse --path-format=absolute --git-common-dir)" ]]; then
      refuse "$wt is there but isn't this repo's landing worktree; move it, or set LAND_WORKTREE."
    fi
    # The landing worktree is land.sh's own: whatever a stopped landing left there goes.
    git -C "$wt" merge --abort 2>/dev/null || true
    git -C "$wt" switch -q -C landing origin/dev && git -C "$wt" clean -qfd || refuse "couldn't reset the landing worktree $wt."
    set +e
    (cd "$wt" && bash tools/workers/land.sh auto "$branch" ${flags[@]+"${flags[@]}"}) 2>&1 | tee "$out"
    code=${PIPESTATUS[0]}
    set -e
    if [[ $code != 0 ]]; then refuse "$(grep -A50 '^STOP' "$out" || tail -20 "$out")"; fi
    grep -q '^Landed ' "$out" || exit 0   # a dry run
    # Landed: this checkout moves to dev, without the merged branch.
    git switch -q dev 2>/dev/null || git switch -q -c dev origin/dev
    git merge -q --ff-only origin/dev 2>/dev/null || echo "(Couldn't fast-forward this checkout's dev; it's on GitHub.)"
    git branch -q -D "$branch" 2>/dev/null || true ;;
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
      gone="$(git diff --name-only --diff-filter=D "$range" | grep -v "^$taken/" || true)"
      [[ -z "$gone" ]] || stop "the branch deletes files; read them, then pass --deletes:"$'\n'"$gone"
      heavy="$(git diff --numstat "$range" | awk -v claims="^$taken/" '$1 != "-" && $2 >= 100 && $2 > 2 * $1 && $3 !~ claims { print $2 " lines out of " $3 }')"
      [[ -z "$heavy" ]] || stop "heavy removals; read them, then pass --deletes:"$'\n'"$heavy"
    fi
    for claim in $(git grep -lE "^Branch:[[:space:]]*[\`]?$branch[\`]?[[:space:]]*$" origin/dev -- "$taken" 2>/dev/null | sed 's/^origin\/dev://'); do
      grep -qxF "$claim" <<< "$own" || stop "the branch leaves its claim $claim on dev: git rm it on the branch."
    done
    if [[ $compiled == 0 ]] && grep -qE '^unity/.*\.cs$' <<< "$own"; then
      stop "the branch changes Unity C#; pass --compiled once a machine with Unity has compiled it."
    fi
    message="$(cat)"
    log="$(git rev-parse --absolute-git-dir)/land-gates.log"
    undo() { git reset -q --hard origin/dev; }
    # The sandbox is frozen with its own rules (J-042): its gates run only when it changed, as sandbox.yml does.
    sandbox=0
    grep -qE '^(sandbox/|\.github/workflows/sandbox\.yml$)' <<< "$own" && sandbox=1
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
      if ! SANDBOX_GATES=$sandbox bash "$here/gates.sh" "$log"; then
        undo
        stop "a gate failed; the full log is $log; the worktree is back on dev."
      fi
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
  *) usage ;;
esac
