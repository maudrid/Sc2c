#!/usr/bin/env python3
# Attack on Crateris: run the tests by topic, several topics at a time.
#
# Copyright (C) 2026 Attack on Crateris contributors.
#
# This program is free software; you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation; either version 2 of the License, or
# (at your option) any later version.
"""Run the engine's tests by topic (tests/topics.json).

    python3 tools/run_tests.py                  every topic
    python3 tools/run_tests.py lander drawing   just these topics
    python3 tools/run_tests.py --changed        the topics touched by what changed since HEAD
    python3 tools/run_tests.py --changed --since origin/main
    python3 tools/run_tests.py --list           the topics and what each covers
    python3 tools/run_tests.py --separate -j 2  each topic in a test run of its own, two at a time

The test project is built once. By default the chosen topics then run
together in one test run (dotnet test --no-build --filter "Topic=a|Topic=b"),
where xUnit runs their test classes in parallel on every core, and the
results are reported topic by topic; the timing-sensitive tests (their
own test collection) run by themselves after the rest. That's the
quickest: one process loads the data and the content once. --separate
runs each topic as a test run of its own, several at a time (-j), the
heaviest first, and the "alone" topics after them, one by one: slower on
a few cores, but each topic is isolated. Tests that need the UQM content
read it from CRATERIS_CONTENT, as with dotnet test. Exits non-zero if any
topic fails, printing what failed.
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(REPO, "tests", "Crateris.Core.Tests")
TOPICS_FILE = os.path.join(REPO, "tests", "topics.json")
TRAIT = re.compile(r'\[Trait\("Topic",\s*"([^"]+)"\)\]')
CLASS_TOPIC = re.compile(r'\[Trait\("Topic",\s*"([^"]+)"\)\]\s*(?:\[[^\]]*\]\s*)*public (?:sealed )?class (\w+)')
TRX = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def class_topics():
    """Each test class's topic, from the test files' Topic traits."""
    found = {}
    for name in os.listdir(PROJECT):
        if name.endswith(".cs"):
            with open(os.path.join(PROJECT, name)) as f:
                for topic, cls in CLASS_TOPIC.findall(f.read()):
                    found[cls] = topic
    return found


def run_together(names):
    """The topics in one test run; per topic: (passed, failed, skipped, seconds of test time), and the failures."""
    with tempfile.TemporaryDirectory() as tmp:
        trx = os.path.join(tmp, "results.trx")
        proc = subprocess.run(["dotnet", "test", PROJECT, "--no-build",
                               "--filter", "|".join(f"Topic={n}" for n in names),
                               "--logger", f"trx;LogFileName={trx}"],
                              cwd=REPO, capture_output=True, text=True)
        if not os.path.exists(trx):
            return proc.returncode, {}, proc.stdout + proc.stderr
        root = ET.parse(trx).getroot()
    topics = class_topics()
    tests = {t.get("id"): t.find(f"{TRX}TestMethod").get("className").rsplit(".", 1)[-1]
             for t in root.iter(f"{TRX}UnitTest")}
    per = {n: [0, 0, 0, 0.0] for n in names}
    failures = []
    for r in root.iter(f"{TRX}UnitTestResult"):
        topic = topics.get(tests.get(r.get("testId"), ""), "?")
        row = per.setdefault(topic, [0, 0, 0, 0.0])
        outcome = r.get("outcome")
        row[0 if outcome == "Passed" else 1 if outcome == "Failed" else 2] += 1
        h, m, sec = r.get("duration", "0:0:0").split(":")
        row[3] += int(h) * 3600 + int(m) * 60 + float(sec)
        if outcome == "Failed":
            message = r.find(f".//{TRX}Message")
            failures.append(f"{r.get('testName')}\n{message.text if message is not None else ''}")
    return proc.returncode, per, "\n\n".join(failures) or (proc.stdout + proc.stderr if proc.returncode else "")


def glob_regex(glob):
    """A glob as TopicTests.Glob reads it: ** any folders, * anything in one name."""
    pattern = re.escape(glob).replace(r"\*\*/", "(.*/)?").replace(r"\*\*", ".*") \
        .replace(r"\*", "[^/]*").replace(r"\?", "[^/]")
    return re.compile("^" + pattern + "$")


def load_topics():
    with open(TOPICS_FILE) as f:
        data = json.load(f)
    return data["topics"], [glob_regex(g) for g in data["all"]]


def changed_files(since):
    """Files changed since a commit, staged or not, and new files not yet added."""
    out = subprocess.run(["git", "diff", "--name-only", since], cwd=REPO, capture_output=True,
                         text=True, check=True).stdout.split()
    out += subprocess.run(["git", "ls-files", "--others", "--exclude-standard"], cwd=REPO,
                          capture_output=True, text=True, check=True).stdout.split()
    return sorted(set(out))


def topics_for(files, topics, everything):
    """The topics a set of changed files touch: a shared file runs them all,
    a test file its own classes' topics, any other file the topics whose
    paths match it. Files no topic covers (docs, the game's scripts) run none."""
    chosen = set()
    for f in files:
        if any(r.match(f) for r in everything):
            return set(topics)
        if f.startswith("tests/Crateris.Core.Tests/") and f.endswith(".cs"):
            path = os.path.join(REPO, f)
            if os.path.exists(path):
                with open(path) as fh:
                    chosen.update(TRAIT.findall(fh.read()))
            else:
                return set(topics)   # a test file went: run everything
            continue
        for name, topic in topics.items():
            if any(glob_regex(g).match(f) for g in topic["paths"]):
                chosen.add(name)
    return chosen


def run_topic(name):
    start = time.time()
    proc = subprocess.run(["dotnet", "test", PROJECT, "--no-build", "--filter", f"Topic={name}"],
                          cwd=REPO, capture_output=True, text=True)
    out = proc.stdout + proc.stderr
    summary = re.findall(r"(Passed|Failed)!\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+), Total:\s+(\d+)", out)
    return name, proc.returncode, time.time() - start, summary[-1] if summary else None, out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("topics", nargs="*", help="topics to run (default: all)")
    ap.add_argument("--changed", action="store_true", help="only the topics touched by changed files")
    ap.add_argument("--since", default="HEAD", help="with --changed: compare with this commit (default HEAD)")
    ap.add_argument("--list", action="store_true", help="list the topics and exit")
    ap.add_argument("--separate", action="store_true",
                    help="each topic in a test run of its own, several at a time")
    ap.add_argument("-j", "--jobs", type=int, default=max(2, (os.cpu_count() or 2) // 2),
                    help="with --separate: topics at a time (default: half the cores, at least 2)")
    ap.add_argument("--no-build", action="store_true", help="don't build the test project first")
    args = ap.parse_args()

    topics, everything = load_topics()
    if args.list:
        for name, t in sorted(topics.items(), key=lambda kv: -kv[1].get("weight", 1)):
            print(f"{name}{' (alone)' if t.get('alone') else ''}: " + ", ".join(t["paths"]))
        print("every topic: " + ", ".join(json.load(open(TOPICS_FILE))["all"]))
        return 0

    unknown = [t for t in args.topics if t not in topics]
    if unknown:
        sys.exit(f"unknown topics: {', '.join(unknown)} (see --list)")
    chosen = set(args.topics) if args.topics else set(topics)
    if args.changed:
        files = changed_files(args.since)
        touched = topics_for(files, topics, everything)
        chosen &= touched
        print(f"{len(files)} files changed since {args.since}: topics {', '.join(sorted(chosen)) or 'none'}")
    if not chosen:
        print("no topics to run")
        return 0

    if not args.no_build:
        build = subprocess.run(["dotnet", "build", PROJECT, "-v", "q", "-nologo"], cwd=REPO,
                               capture_output=True, text=True)
        if build.returncode:
            print(build.stdout + build.stderr)
            sys.exit("the test project didn't build")

    order = sorted(chosen, key=lambda n: -topics[n].get("weight", 1))
    if not args.separate:
        start = time.time()
        code, per, failures = run_together(order)
        for name in order:
            passed, failed, skipped, secs = per.get(name, [0, 0, 0, 0.0])
            ok = failed == 0 and passed + skipped > 0
            print(f"{'ok  ' if ok else 'FAIL'} {name:15} {passed:>4} passed, {failed} failed, {skipped} skipped  "
                  f"{secs:6.1f} s of tests")
        totals = [sum(p[i] for p in per.values()) for i in range(3)]
        print(f"\n{len(order)} topics together, {totals[0]} passed, {totals[1]} failed, {totals[2]} skipped "
              f"in {time.time() - start:.1f} s")
        if code != 0 or totals[1] or any(per.get(n, [0, 0, 0])[0] + per.get(n, [0, 0, 0])[2] == 0 for n in order):
            print("\n" + failures)
            return 1
        return 0
    together = [n for n in order if not topics[n].get("alone")]
    alone = [n for n in order if topics[n].get("alone")]
    start = time.time()
    results = []

    def report(r):
        name, code, secs, summary, _ = r
        if summary:
            word, failed, passed, skipped, total = summary
            print(f"{'ok  ' if code == 0 else 'FAIL'} {name:15} {passed:>4} passed, {failed} failed, "
                  f"{skipped} skipped  {secs:5.1f} s", flush=True)
        else:
            print(f"FAIL {name:15} no test summary ({secs:.1f} s)", flush=True)
        results.append(r)

    with ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        for r in pool.map(run_topic, together):
            report(r)
    for name in alone:
        report(run_topic(name))

    failed = [r for r in results if r[1] != 0 or r[3] is None]
    totals = [sum(int(r[3][i]) for r in results if r[3]) for i in (2, 1, 3)]
    print(f"\n{len(results)} topics, {totals[0]} passed, {totals[1]} failed, {totals[2]} skipped "
          f"in {time.time() - start:.1f} s")
    for name, _, _, _, out in failed:
        print(f"\n===== {name} =====\n{out}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
