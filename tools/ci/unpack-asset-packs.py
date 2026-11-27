#!/usr/bin/env python3
"""Lays the Asset Store packs from unity/AssetPacks.json into the project, with no Unity (CI, release.yml).

A .unitypackage is a gzipped tar of one folder per asset, named by its GUID, holding `pathname` (its path in the
project), `asset` (the file; none for a folder) and `asset.meta`. This writes each asset and its .meta where
`pathname` says, as Unity's own import would, and Unity imports them when it next opens the project. It's here
because Unity's ImportPackage only queues an import, which `-quit` cuts off (AssetPackRestore.FromCommandLine).

Packs are read from --cache, laid out like the Editor's Asset Store cache: each at its manifest `cache` path (the
private 5e-asset-packs repo is). Every entry in a pack is written: a pack with an `include` is kept there already
cut to it (UnityPackageSubset, docs/handoffs/briefs/asset-packs-repo.md). A pack whose folder already holds files
is skipped, as AssetPackRestore skips it. A pack's `Packages/manifest.json` entry (Starter Assets has one) lists the
Unity packages it needs: Unity merges it into the project's manifest, so here each must already be there.

    python3 tools/ci/unpack-asset-packs.py --cache _packs --project unity
"""
import argparse
import json
import os
import shutil
import sys
import tarfile


def has_files(folder):
    return os.path.isdir(folder) and any(files for _, _, files in os.walk(folder))


def needs_packages(package, wanted, project):
    with open(os.path.join(project, "Packages", "manifest.json"), encoding="utf-8") as f:
        have = json.load(f).get("dependencies", {})
    lacking = sorted(name for name in wanted.get("dependencies", {}) if name not in have)
    if lacking:
        raise SystemExit(f"ASSET PACKS: FAILED {package} needs {', '.join(lacking)} in Packages/manifest.json")


def unpack(package, project):
    paths = {}
    with tarfile.open(package, "r:gz") as tar:
        for member in tar:
            guid, _, name = member.name.lstrip("./").partition("/")
            if member.isfile() and name == "pathname":
                paths[guid] = tar.extractfile(member).read().decode("utf-8").splitlines()[0].strip()
    root = os.path.realpath(project)
    targets = {}
    for guid, path in sorted(paths.items()):
        if path == "Packages/manifest.json":
            continue
        target = os.path.realpath(os.path.join(root, path))
        if not path.startswith("Assets/") or not target.startswith(root + os.sep):
            raise SystemExit(f"{package}: {guid} has a path outside Assets/: {path!r}")
        targets[guid] = target
    written = 0
    with tarfile.open(package, "r:gz") as tar:
        for member in tar:
            guid, _, name = member.name.lstrip("./").partition("/")
            if not member.isfile() or name not in ("asset", "asset.meta"):
                continue
            if paths.get(guid) == "Packages/manifest.json":
                if name == "asset":
                    needs_packages(package, json.loads(tar.extractfile(member).read()), project)
                continue
            if guid not in targets:
                continue
            target = targets[guid] + (".meta" if name == "asset.meta" else "")
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with tar.extractfile(member) as src, open(target, "wb") as dst:
                shutil.copyfileobj(src, dst, 1 << 20)
            written += name == "asset"
    for guid, target in targets.items():
        if not os.path.exists(target):
            os.makedirs(target, exist_ok=True)
    return written


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--cache", required=True, help="the packs, laid out like the Asset Store cache")
    parser.add_argument("--project", default="unity", help="the Unity project (holds AssetPacks.json)")
    args = parser.parse_args()
    with open(os.path.join(args.project, "AssetPacks.json"), encoding="utf-8") as f:
        packs = json.load(f)["packs"]
    missing = [p for p in packs if not os.path.isfile(os.path.join(args.cache, p["cache"]))]
    if missing:
        names = ", ".join(f"{p['name']} ({p['cache']})" for p in missing)
        print(f"ASSET PACKS: FAILED not in {args.cache}: {names}")
        return 1
    for p in packs:
        if has_files(os.path.join(args.project, p["folder"])):
            print(f"{p['name']}: already in {p['folder']}")
            continue
        n = unpack(os.path.join(args.cache, p["cache"]), args.project)
        print(f"{p['name']}: {n} files")
        if not has_files(os.path.join(args.project, p["folder"])):
            print(f"ASSET PACKS: FAILED {p['name']} put nothing in {p['folder']}")
            return 1
    print("ASSET PACKS: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
