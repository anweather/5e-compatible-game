#!/usr/bin/env bash
# Runs INSIDE the Ubuntu template, as its user (asks for the sudo password once).
# Starts Claude Code's Remote Control at boot, with nobody logged in (a systemd
# user service plus linger), named after the Hyper-V VM (u1, u2...) so every
# clone shows up as its own session. Log: journalctl --user -u claude-rc
# Re-running replaces the service.
set -euo pipefail

# The KVP daemon is how the guest learns its VM name (and how the host sees its IP).
sudo systemctl enable --now hv-kvp-daemon
sudo loginctl enable-linger "$USER"

mkdir -p ~/.local/bin ~/.config/systemd/user
cat > ~/.local/bin/claude-rc <<'EOF'
#!/usr/bin/env bash
# Pool 3 holds what the host pushes, including VirtualMachineName.
name=$(tr -s '\0' '\n' < /var/lib/hyperv/.kvp_pool_3 2>/dev/null | grep -A1 -x VirtualMachineName | tail -1 || true)
cd ~/5e-compatible-game
exec ~/.local/bin/claude remote-control --name "${name:-$(hostname)}"
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
systemctl --user enable --now claude-rc
systemctl --user --no-pager status claude-rc | head -5
