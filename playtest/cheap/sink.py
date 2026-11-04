#!/usr/bin/env python3
"""Sends a session's findings somewhere a person will see them.

    sink.py <run folder> <sink> [--unreproduced]     send what hasn't been sent yet
    sink.py --check <sink>                           can this sink be used? (before spending a run)

A sink is none, folder:<dir> (a new F-NNNN/finding.md each) or github:<owner/repo> (an issue each, labelled
playtest). Only notes the model reproduced are sent unless --unreproduced is given. What's sent goes in the run
folder's sunk.jsonl, and a title the sink already has is skipped, so running it twice sends nothing twice.
"""
import json
import pathlib
import re
import subprocess
import sys

LABEL = "playtest"


def gh(*args):
    return subprocess.run(["gh", *args], capture_output=True, text=True)


def parse(sink):
    kind, _, where = sink.partition(":")
    if (kind == "none" and not where) or (kind in ("folder", "github") and where):
        return kind, where
    sys.exit(f"unknown sink {sink!r}: use none, folder:<dir> or github:<owner/repo>")


def check(sink):
    kind, where = parse(sink)
    if kind == "github":
        seen = gh("repo", "view", where, "--json", "viewerPermission")
        if seen.returncode != 0:
            sys.exit(f"can't reach {where} with gh: {seen.stderr.strip()}")


def read_lines(path):
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text().splitlines() if line.strip()]


def fenced(text):
    return "```\n" + str(text).replace("```", "'''") + "\n```"


def provenance(run):
    return (f"Text playtester, {run.get('model', '?')} at core {run.get('commit', '?')}, seed {run.get('seed', '?')}. "
            "A model's note, not yet checked by a person.")


def issue_body(note, run):
    return "\n\n".join([
        f"*{provenance(run)}*",
        f"**Reproduced by the model:** {'yes' if note.get('reproduced') else 'no'}",
        "**Steps**\n" + fenced(note.get("steps", "")),
        "**What happened**\n" + fenced(note.get("happened", "")),
        "**What should have**\n" + fenced(note.get("expected", "")),
        "**Evidence**\n" + fenced(note.get("evidence", "")),
        f"Replay: `playtest/cheap/README.md`, command {note.get('atCommand', '?')}, state `{note.get('state', '?')}`.",
    ])


def finding_text(number, note, run):
    return "\n".join([
        f"# F-{number:04d}: {note['title']}",
        "",
        "- **Category:** Not judged yet",
        f"- **Build:** {provenance(run)}",
        f"- **Reproduced:** {'Yes' if note.get('reproduced') else 'No'}, by the model.",
        "",
        f"**Steps:** {note.get('steps', '')}",
        "",
        f"**What happens:** {note.get('happened', '')}",
        "",
        f"**What should:** {note.get('expected', '')}",
        "",
        f"**Evidence:** {note.get('evidence', '')}",
        "",
    ])


def folder_sink(where):
    root = pathlib.Path(where)
    root.mkdir(parents=True, exist_ok=True)
    folders = [p for p in root.glob("F-[0-9]*") if p.is_dir()]
    titles = set()
    for folder in folders:
        first = (folder / "finding.md").read_text().split("\n", 1)[0] if (folder / "finding.md").exists() else ""
        titles.add(first.partition(": ")[2])
    numbers = [int(re.sub(r"\D", "", p.name)) for p in folders]

    def send(note, run):
        number = max(numbers, default=0) + 1
        numbers.append(number)
        folder = root / f"F-{number:04d}"
        folder.mkdir()
        (folder / "finding.md").write_text(finding_text(number, note, run))
        return str(folder)

    return titles, send


def github_sink(where):
    seen = gh("issue", "list", "--repo", where, "--state", "all", "--limit", "1000", "--json", "title")
    if seen.returncode != 0:
        sys.exit(f"can't list issues of {where}: {seen.stderr.strip()}")
    titles = {issue["title"] for issue in json.loads(seen.stdout)}

    def send(note, run):
        made = gh("issue", "create", "--repo", where, "--title", note["title"], "--body", issue_body(note, run),
                  "--label", LABEL)
        if made.returncode != 0:
            raise RuntimeError(made.stderr.strip())
        return made.stdout.strip()

    return titles, send


def deliver(folder, sink, unreproduced):
    kind, where = parse(sink)
    notes = read_lines(folder / "findings.jsonl")
    wanted = [n for n in notes if n.get("reproduced") or unreproduced]
    held = len(notes) - len(wanted)
    if kind == "none":
        print(f"{len(notes)} notes in {folder / 'findings.jsonl'}; no sink")
        return 0
    run = json.loads((folder / "run.json").read_text()) if (folder / "run.json").exists() else {}
    sunk_path = folder / "sunk.jsonl"
    sent = {line["title"] for line in read_lines(sunk_path) if line["sink"] == sink}
    titles, send = folder_sink(where) if kind == "folder" else github_sink(where)
    made = 0
    for note in wanted:
        if note["title"] in sent or note["title"] in titles:
            continue
        try:
            place = send(note, run)
        except RuntimeError as failed:
            print(f"not sent, run sink.py again: {note['title']}: {failed}", file=sys.stderr)
            return 1
        titles.add(note["title"])
        made += 1
        with sunk_path.open("a") as sunk:
            sunk.write(json.dumps({"title": note["title"], "sink": sink, "place": place}) + "\n")
        print(f"sent: {place}")
    print(f"{made} sent to {sink}, {len(wanted) - made} already there, {held} unreproduced held back")
    return 0


def main(args):
    if len(args) == 2 and args[0] == "--check":
        check(args[1])
        return 0
    unreproduced = "--unreproduced" in args
    args = [a for a in args if a != "--unreproduced"]
    if len(args) != 2:
        sys.exit(__doc__)
    return deliver(pathlib.Path(args[0]), args[1], unreproduced)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
