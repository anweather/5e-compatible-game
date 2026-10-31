#!/usr/bin/env bash
# linux.sh: the host's side of a Linux (Ubuntu) worker, over SSH with the host's key, as
# signins.cmd and wip.cmd are for Windows workers over PowerShell Direct. start.cmd and
# stop.cmd call it for VMs that aren't Windows; it runs in Git Bash.
#   linux.sh push <worker> <ip>   sign-ins slot -> worker (only what the slot has newer), then pull
#   linux.sh pull <worker> <ip>   worker -> slot, whichever side is newer
#   linux.sh hook <worker> <ip>   install the commit cost hook (commit-cost.mjs) and the attention hook
#   linux.sh attention <worker> <ip>   print the attention hook's last report (~/.worker/attention.json)
#   linux.sh runner <worker> <ip>      copy in and start the mailbox runner (runner.mjs)
#   linux.sh brief <worker> <ip> <f>   put a brief into the worker's inbox
#   linux.sh outbox <worker> <ip>      print finished reports ("=== <file>" each) and remove them
#   linux.sh wip  <worker> <ip>   push uncommitted or unpushed work as wip/<worker>-<branch>
# Slots are the Windows workers' (<Root>\signins\<worker>, this account only): claude
# (its refresh token rotates, so one sign-in lives in one worker) and github (gh's
# hosts.yml, which doesn't rotate, and .gitconfig). Nothing here prints a token.
set -euo pipefail
cmd="${1:-}"; w="${2:-}"; ip="${3:-}"
[[ -n "$cmd" && -n "$w" && -n "$ip" ]] || { sed -n 2,15p "$0"; exit 2; }
here="$(cd "$(dirname "$0")" && pwd)"
bank="${WORKERS_ROOT:-/c/HyperV/Workers}/signins/$w"
user="${WORKER_USER:-anweather}"
# Clones share the template's host key and get a new IP each boot, on a private NAT switch.
ssh_() { ssh -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR -o ConnectTimeout=5 "$user@$ip" "$@"; }

# What a slot holds: paths under the guest's home. Slot files are named after them, / as ~.
declare -A items=([claude]=".claude/.credentials.json" [github]=".config/gh/hosts.yml .gitconfig")
# How new a copy is, by its first file: Claude's token expiry (0 when signed out); otherwise its time.
guest_stamp() {
  local first="${items[$1]%% *}"
  if [[ "$1" == claude ]]; then ssh_ "f=~/$first; [ -f \$f ] && grep -q refreshToken \$f && grep -o '\"expiresAt\":[0-9]*' \$f | cut -d: -f2 || echo 0"
  else ssh_ "stat -c %Y ~/$first 2>/dev/null || echo 0"; fi
}
slot_stamp() {
  local f; f="$bank/$1/$(echo "${items[$1]%% *}" | tr / '~')"
  [[ -f "$f" ]] || { echo 0; return; }
  if [[ "$1" == claude ]]; then grep -q refreshToken "$f" && grep -o '"expiresAt":[0-9]*' "$f" | cut -d: -f2 || echo 0
  else stat -c %Y "$f"; fi
}

pull() {
  for it in "${!items[@]}"; do
    (( $(guest_stamp "$it") > $(slot_stamp "$it") )) || continue
    rm -rf "$bank/$it"; mkdir -p "$bank/$it"
    for p in ${items[$it]}; do
      f="$bank/$it/$(echo "$p" | tr / '~')"
      ssh_ "cat ~/$p 2>/dev/null" > "$f" || { rm -f "$f"; continue; }
      touch -d "@$(ssh_ "stat -c %Y ~/$p")" "$f"   # the copy keeps the worker's time, so the next push skips it
    done
    echo "Saved $w's $it to its slot."
  done
}
push() {
  for it in "${!items[@]}"; do
    (( $(slot_stamp "$it") > 0 )) || { echo "Slot $w has no $it yet: sign in on $w once (ssh $user@$ip)."; continue; }
    (( $(guest_stamp "$it") >= $(slot_stamp "$it") )) && continue   # the worker has it, or newer
    for p in ${items[$it]}; do
      f="$bank/$it/$(echo "$p" | tr / '~')"
      [[ -f "$f" ]] && ssh_ "mkdir -p \"\$(dirname ~/$p)\" && umask 077 && cat > ~/$p && touch -d @$(stat -c %Y "$f") ~/$p" < "$f"
    done
    echo "Signed $w in to $it from its slot."
  done
  pull
}
hook() {
  ssh_ 'mkdir -p ~/.worker && cat > ~/.worker/commit-cost.mjs' < "$here/commit-cost.mjs"
  # The attention hook: a permission prompt in the worker is reported to the host.
  ssh_ 'cat > ~/.worker/attention.mjs' < "$here/attention.mjs"
  ssh_ 'cat > ~/.worker/worker-settings.mjs && node ~/.worker/worker-settings.mjs' < "$here/worker-settings.mjs"
  ssh_ 'cat > ~/5e-compatible-game/.git/hooks/prepare-commit-msg && chmod +x ~/5e-compatible-game/.git/hooks/prepare-commit-msg' <<'EOF'
#!/bin/sh
# Worker cost trailers (tools/workers/commit-cost.mjs); not for merges, squashes or amends.
case "$2" in merge|squash|commit) exit 0;; esac
node "$HOME/.worker/commit-cost.mjs" "$1" || true
EOF
  echo "Commits in $w carry their token and time cost."
}
# wip.cmd's guest steps in bash: each worktree's uncommitted work becomes one commit on HEAD
# (a scratch index, so the worktree, index and branch stay as they were), pushed with
# anything else unpushed as wip/<worker>-<branch>.
wip() {
  ssh_ "bash -s -- '$w'" <<'EOF'
set -uo pipefail
w=$1; repo=~/5e-compatible-game; failed=0
[ -d "$repo/.git" ] || { echo "No repo at $repo."; exit 1; }
for wt in $(git -C "$repo" worktree list --porcelain | sed -n 's/^worktree //p'); do
  [ -d "$wt" ] || continue
  branch=$(git -C "$wt" symbolic-ref --short -q HEAD) ; head=$(git -C "$wt" rev-parse HEAD)
  branch=${branch:-detached-${head:0:8}}; commit=$head
  if [ -n "$(git -C "$wt" status --porcelain)" ]; then
    export GIT_INDEX_FILE=$(mktemp -u)
    git -C "$wt" read-tree HEAD && git -C "$wt" add -A && tree=$(git -C "$wt" write-tree)
    rm -f "$GIT_INDEX_FILE"; unset GIT_INDEX_FILE
    git -C "$wt" config user.email >/dev/null || export GIT_AUTHOR_NAME="$w (WIP)" GIT_COMMITTER_NAME="$w (WIP)" GIT_AUTHOR_EMAIL="$w@workers.invalid" GIT_COMMITTER_EMAIL="$w@workers.invalid"
    commit=$(git -C "$wt" commit-tree "$tree" -p "$head" -m "WIP ($w, $branch): uncommitted work saved before the worker stopped") || { echo "$wt: commit failed"; failed=1; continue; }
  fi
  if [ "$(git -C "$wt" rev-list --count "$commit" --not --remotes)" = 0 ]; then echo "$wt: nothing unpushed ($branch)."; continue; fi
  ref="wip/$w-$(echo "$branch" | tr -c 'A-Za-z0-9._/\n-' '-')"
  if git -C "$wt" push -q --force origin "$commit:refs/heads/$ref"; then echo "$wt: pushed $ref (${commit:0:8})."; else echo "$wt: push failed"; failed=1; fi
done
exit $failed
EOF
}

# The IP shows up before sshd is listening.
for _ in $(seq 24); do ssh_ true 2>/dev/null && break; sleep 5; done
ssh_ true || { echo "Can't reach $w over SSH ($user@$ip, the host's key)."; exit 1; }
case "$cmd" in
  push) push ;; pull) pull ;; hook) hook ;; wip) wip ;;
  attention) ssh_ 'cat ~/.worker/attention.json 2>/dev/null || true' ;;
  # The mailbox runner (runner.mjs): copied in and started, detached from this SSH session.
  runner) ssh_ 'mkdir -p ~/.worker && cat > ~/.worker/runner.mjs' < "$here/runner.mjs"
          ssh_ 'setsid nohup node ~/.worker/runner.mjs >/dev/null 2>&1 < /dev/null & echo "The mailbox runner is running."' ;;
  brief)  f="${4:?linux.sh brief <worker> <ip> <file>}"
          ssh_ "mkdir -p ~/.worker/inbox && cat > ~/.worker/inbox/$(basename "$f")" < "$f" ;;
  # Finished reports, each as "=== <file>" then its text, then removed from the worker.
  outbox) ssh_ 'cd ~/.worker/outbox 2>/dev/null || exit 0; for f in *.md; do [ -f "$f" ] || continue; echo "=== $f"; cat "$f"; rm -f "$f"; done' ;;
  *) sed -n 2,15p "$0"; exit 2 ;;
esac
