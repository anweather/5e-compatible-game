Needs: core
Area: tools/workers/*.test.mjs
Branch: test-isolation-219

# Landing-gate tests must not reach a live VM (#219)

`tools/workers/bridge-watch.test.mjs:95` ("a removal that fails is logged once, not every pass") is flaky: its dry
run calls `linux.sh outbox-done` against the real u2 VM, which can exit 124 (cut off), and the expected log line
never arrives. It failed the landing gate once on 2026-11-10 and passed on retry.

Do:
1. Stub `linux.sh` in that test (and any other test in `tools/workers/*.test.mjs` that shells to a VM, `ssh`, or
   `tools/workers/*.cmd`) so no test touches a live host. Grep the test files for `linux.sh`, `ssh`, `.cmd`, `172.`.
2. Run the whole `node --test tools/workers/*.test.mjs` 10 times in a row; all must pass.
3. Do not change bridge-watch.sh behaviour (Andrew stopped the watcher on the PC; it is not running).

Done when: the tests pass 10/10 with u2 stopped or unreachable. Land with `land.sh auto test-isolation-219`.
Report: the commit and the count of tests that touched a VM.
