#!/usr/bin/env bash
# land.sh: how the coordinator lands a worker's branch on dev, in its own worktree.
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
[[ -n "$cmd" && -n "$branch" ]] || { sed -n 2,9p "$0"; exit 2; }
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
  push)
    git merge-base --is-ancestor origin/dev HEAD || { echo "dev moved since the merge: run land.sh merge again."; exit 1; }
    git merge-base --is-ancestor "origin/$branch" HEAD || { echo "HEAD doesn't contain $branch."; exit 1; }
    git push -q origin HEAD:dev
    git push -q origin --delete "$branch"
    git branch -q -D "$branch" 2>/dev/null || true
    echo "Landed $branch on dev at $(git rev-parse --short HEAD) and deleted the branch." ;;
  *) sed -n 2,9p "$0"; exit 2 ;;
esac
