#!/usr/bin/env bash
# Runs INSIDE the Ubuntu template, as its user (asks for the sudo password once).
# Starts Claude Code's Remote Control at boot, with nobody logged in (a systemd
# user service plus linger), named after the worker (u1, u2...) so every clone
# shows up as its own session. Log: journalctl --user -u claude-rc
# Re-running replaces the service. Optional argument: the permission mode for
# spawned sessions (default auto).
set -euo pipefail
MODE=${1:-auto}

# KVP lets the host see the IP; fcopy lets start.cmd copy the worker's name in.
sudo systemctl enable --now hv-kvp-daemon hv-fcopy-daemon || true
sudo loginctl enable-linger "$USER"

mkdir -p ~/.local/bin ~/.config/systemd/user
cat > ~/.local/bin/claude-rc <<EOF
#!/usr/bin/env bash
# start.cmd copies "<name> <unix seconds>" into /var/lib/worker/name.txt after
# each boot; KVP's VirtualMachineName (pool 3) and the hostname are fallbacks.
boot=\$(( \$(date +%s) - \$(cut -d. -f1 /proc/uptime) ))
name=
for _ in \$(seq 36); do
  read -r n at < /var/lib/worker/name.txt 2>/dev/null && (( at > boot )) && { name=\$n; break; }
  sleep 5
done
[[ -n \$name ]] || name=\$(tr -s '\0' '\n' < /var/lib/hyperv/.kvp_pool_3 2>/dev/null | grep -A1 -x VirtualMachineName | tail -1 || true)
cd ~/5e-compatible-game
exec ~/.local/bin/claude remote-control --spawn=worktree --permission-mode $MODE --name "\${name:-\$(hostname)}"
EOF
chmod +x ~/.local/bin/claude-rc

cat > ~/.config/systemd/user/claude-rc.service <<'EOF'
[Unit]
Description=Claude Code Remote Control
After=network-online.target

[Service]
ExecStart=%h/.local/bin/claude-rc
Restart=on-failure
RestartSec=60

[Install]
WantedBy=default.target
EOF
systemctl --user daemon-reload
systemctl --user enable claude-rc
echo "Enabled; Remote Control starts at the next boot (spawned sessions: $MODE)."
