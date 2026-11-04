#!/usr/bin/env python3
"""python3 playtest/cheap/sink_test.py: the sinks, on a stand-in gh, so nothing is filed."""
import json
import os
import pathlib
import stat
import subprocess
import sys
import tempfile
import unittest

SINK = pathlib.Path(__file__).with_name("sink.py")
FAKE_GH = """#!/bin/sh
echo "$@" >> "$GH_LOG"
case "$1 $2" in
  "issue list") cat "$GH_ISSUES" ;;
  "issue create") [ -n "$GH_FAIL" ] && { echo refused >&2; exit 1; }; echo https://example.test/issues/7 ;;
esac
"""


def note(title, reproduced=True):
    return {"title": title, "steps": "go 1,2", "happened": "x", "expected": "y", "evidence": "z",
            "reproduced": reproduced, "atCommand": 3, "state": "abc"}


class Sinks(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.root = pathlib.Path(self.dir.name)
        self.run_dir = self.root / "run"
        self.run_dir.mkdir()
        (self.run_dir / "run.json").write_text(json.dumps({"model": "haiku", "seed": "1", "commit": "abc"}))
        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        gh = bin_dir / "gh"
        gh.write_text(FAKE_GH)
        gh.chmod(gh.stat().st_mode | stat.S_IEXEC)
        self.log = self.root / "gh.log"
        self.issues = self.root / "issues.json"
        self.issues.write_text("[]")
        self.env = {**os.environ, "PATH": f"{bin_dir}:{os.environ['PATH']}", "GH_LOG": str(self.log),
                    "GH_ISSUES": str(self.issues)}

    def notes(self, *notes):
        (self.run_dir / "findings.jsonl").write_text("".join(json.dumps(n) + "\n" for n in notes))

    def sink(self, *args, **env):
        return subprocess.run([sys.executable, "-I", str(SINK), str(self.run_dir), *args], capture_output=True,
                              text=True, env={**self.env, **env})

    def creates(self):
        return [l for l in self.log.read_text().splitlines() if l.startswith("issue create")] if self.log.exists() else []

    def test_github_files_only_what_was_reproduced(self):
        self.notes(note("real"), note("maybe", reproduced=False))
        self.assertEqual(self.sink("github:o/r").returncode, 0)
        self.assertEqual(len(self.creates()), 1)
        self.assertIn("--title real", self.creates()[0])
        self.assertIn("--label playtest", self.log.read_text())

    def test_unreproduced_goes_when_asked(self):
        self.notes(note("maybe", reproduced=False))
        self.sink("github:o/r", "--unreproduced")
        self.assertEqual(len(self.creates()), 1)

    def test_running_twice_files_once(self):
        self.notes(note("real"))
        self.sink("github:o/r")
        self.sink("github:o/r")
        self.assertEqual(len(self.creates()), 1)

    def test_a_title_github_has_is_not_filed(self):
        self.issues.write_text(json.dumps([{"title": "real"}]))
        self.notes(note("real"))
        self.sink("github:o/r")
        self.assertEqual(self.creates(), [])

    def test_a_refused_issue_can_be_sent_again(self):
        self.notes(note("real"))
        self.assertEqual(self.sink("github:o/r", GH_FAIL="1").returncode, 1)
        self.assertFalse((self.run_dir / "sunk.jsonl").exists())
        self.assertEqual(self.sink("github:o/r").returncode, 0)
        self.assertTrue((self.run_dir / "sunk.jsonl").exists())

    def test_folder_takes_the_next_number(self):
        found = self.root / "found"
        (found / "F-0009").mkdir(parents=True)
        (found / "F-0009" / "finding.md").write_text("# F-0009: old\n")
        self.notes(note("old"), note("new"))
        self.assertEqual(self.sink(f"folder:{found}").returncode, 0)
        self.assertTrue((found / "F-0010" / "finding.md").read_text().startswith("# F-0010: new\n"))
        self.assertFalse((found / "F-0011").exists())

    def test_none_sends_nothing(self):
        self.notes(note("real"))
        self.assertEqual(self.sink("none").returncode, 0)
        self.assertEqual(self.creates(), [])

    def test_an_unknown_sink_is_refused(self):
        self.notes(note("real"))
        self.assertNotEqual(self.sink("slack:x").returncode, 0)


if __name__ == "__main__":
    unittest.main()
