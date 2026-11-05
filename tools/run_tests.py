#!/usr/bin/env python3
# Attack on Crateris: run the tests by topic.
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
    python3 tools/run_tests.py --changed        the topics a change since HEAD can affect
    python3 tools/run_tests.py --changed --since origin/main
    python3 tools/run_tests.py --list           the topics, and how many files each depends on
    python3 tools/run_tests.py --map            remake tests/topic-map.json (a few minutes)
    python3 tools/run_tests.py --separate -j 2  each topic in a test run of its own, two at a time

The test project is built once. By default the chosen topics then run
together in one test run (dotnet test --no-build --filter "Topic=a|Topic=b"),
where xUnit runs their test classes in parallel on every core, and the
results are reported topic by topic; the timing-sensitive tests (their
own test collection) run by themselves after the rest. That's the
quickest: one process loads the data and the content once. --separate
runs each topic as a test run of its own, several at a time (-j), the
heaviest first, and the "alone" topics after them, one by one.

--changed picks topics with tests/topic-map.json, which --map makes: for
each topic, the source files its tests ran (measured with coverage), the
files declaring the types its tests name and the enums and constants
that code uses, its test files and those whose helpers it uses, and the
recordings it loads. A changed file runs the topics whose map holds it;
a changed source, test or recording file the map doesn't know (new, or
run by no test), or a shared file (tests/topics.json "all"), runs every
topic; documents, the game's scripts and the tools run none. TopicTests
always runs. The map is a snapshot: remake it after changing which code
calls which (it says how many source files changed since it was made).
Every topic is the check before calling work done.

Tests that need the UQM content read it from CRATERIS_CONTENT, as with
dotnet test. Exits non-zero if any test fails, printing what failed.
"""

import argparse
import glob
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
MAP_FILE = os.path.join(REPO, "tests", "topic-map.json")
TRAIT = re.compile(r'\[Trait\("Topic",\s*"([^"]+)"\)\]')
CLASS_TOPIC = re.compile(r'\[Trait\("Topic",\s*"([^"]+)"\)\]\s*(?:\[[^\]]*\]\s*)*public (?:sealed )?class (\w+)')
DECLARES = re.compile(r'\b(?:class|record(?:\s+struct)?|struct|enum|interface)\s+(\w+)')
ENUM = re.compile(r'\benum\s+(\w+)')
TYPE_START = re.compile(r'\b(?:class|record(?:\s+struct)?|struct)\s+(\w+)')
NAME = re.compile(r'\b[A-Z]\w*\b')
GOLDEN = re.compile(r'Golden\.Load\("([^"]+)"\)')
CHECK_CLASS = "TopicTests"   # always run with --changed
TRX = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
SOURCE_ROOTS = ("src/", "tests/Crateris.Core.Tests/", "tests/golden/")
SUMMARY = re.compile(r"(Passed|Failed)!\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+), Total:\s+(\d+)")


def rel(path):
    return os.path.relpath(path, REPO).replace(os.sep, "/")


def glob_regex(pattern):
    """A glob as TopicTests.Glob reads it: ** any folders, * anything in one name."""
    p = re.escape(pattern).replace(r"\*\*/", "(.*/)?").replace(r"\*\*", ".*") \
        .replace(r"\*", "[^/]*").replace(r"\?", "[^/]")
    return re.compile("^" + p + "$")


def load_topics():
    with open(TOPICS_FILE) as f:
        data = json.load(f)
    return data["topics"], [glob_regex(g) for g in data["all"]], data["all"]


def git(*args):
    return subprocess.run(["git", *args], cwd=REPO, capture_output=True, text=True, check=True).stdout


def changed_files(since):
    """Files changed since a commit, staged or not, and new files not yet added."""
    out = git("diff", "--name-only", "-z", since).split("\0")
    out += git("ls-files", "--others", "--exclude-standard", "-z").split("\0")
    return sorted(f for f in set(out) if f)


def test_files():
    return sorted(glob.glob(os.path.join(PROJECT, "*.cs")))


def class_topics():
    """Each test class's topic, from the test files' Topic traits."""
    found = {}
    for path in test_files():
        with open(path) as f:
            for topic, cls in CLASS_TOPIC.findall(f.read()):
                found[cls] = topic
    return found


# ---------------------------------------------------------------- the map

def source_index():
    """The source and test files: which types each declares, which names
    each uses, and each file's uses of enum values and constants
    (Type.Member, compiled into the code that uses them, so coverage
    doesn't see them), as the files declaring them."""
    files = [p for p in glob.glob(os.path.join(REPO, "src", "**", "*.cs"), recursive=True)
             if "/bin/" not in p and "/obj/" not in p] + test_files()
    texts = {}
    declares, names, enums, consts = {}, {}, set(), {}
    for path in files:
        with open(path) as f:
            text = f.read()
        r = rel(path)
        texts[r] = text
        names[r] = set(NAME.findall(text))
        for n in DECLARES.findall(text):
            declares.setdefault(n, set()).add(r)
        enums.update(ENUM.findall(text))
        # Each type's constants, up to the next type.
        starts = list(TYPE_START.finditer(text))
        for i, m in enumerate(starts):
            body = text[m.end():starts[i + 1].start() if i + 1 < len(starts) else len(text)]
            consts.setdefault(m.group(1), set()).update(re.findall(r"\bconst\s+\w+\s+(\w+)", body))
            consts[m.group(1)].update(re.findall(r",\s*(\w+)\s*=", " ".join(
                re.findall(r"\bconst\s+\w+\s+[^;]*;", body))))
    inlined = {}
    for r, text in texts.items():
        used = set()
        for t, member in re.findall(r"\b([A-Z]\w*)\.([A-Z]\w*)\b", text):
            if t in enums or member in consts.get(t, ()):
                used.update(declares.get(t, ()))
        inlined[r] = used
    return declares, names, inlined


def coverage_files(topic, out_dir):
    """The source files a topic's tests run, measured with coverage (in mapping mode)."""
    env = dict(os.environ, CRATERIS_TOPIC_MAP="1")
    subprocess.run(["dotnet", "test", PROJECT, "--no-build", "--filter", f"Topic={topic}",
                    "--collect", "XPlat Code Coverage", "--results-directory", out_dir],
                   cwd=REPO, capture_output=True, text=True, env=env)
    reports = glob.glob(os.path.join(out_dir, "*", "coverage.cobertura.xml"))
    if not reports:
        sys.exit(f"no coverage for topic {topic}")
    root = ET.parse(reports[0]).getroot()
    sources = [s.text for s in root.iter("source") if s.text]
    files = set()
    for c in root.iter("class"):
        if any(int(line.get("hits", "0")) > 0 for line in c.iter("line")):
            name = c.get("filename")
            full = name if os.path.isabs(name) else next(
                (os.path.join(s, name) for s in sources if os.path.exists(os.path.join(s, name))), name)
            files.add(rel(full))
    return files


def make_map(topics):
    declares, names, inlined = source_index()
    by_topic = {}
    for path in test_files():
        with open(path) as f:
            text = f.read()
        for t in set(TRAIT.findall(text)):
            by_topic.setdefault(t, []).append((rel(path), text))
    result = {}
    with tempfile.TemporaryDirectory() as tmp:
        for topic in sorted(topics):
            start = time.time()
            files = coverage_files(topic, os.path.join(tmp, topic))
            for path, text in by_topic.get(topic, []):
                files.add(path)
                # The types its tests name (and the test files whose helpers they use).
                for n in names[path]:
                    files.update(declares.get(n, ()))
                for g in GOLDEN.findall(text):
                    files.add(f"tests/golden/{g}.json")
            # The enum values and constants the code it runs uses.
            for path in list(files):
                files.update(inlined.get(path, ()))
            result[topic] = sorted(f for f in files if f.startswith(SOURCE_ROOTS))
            print(f"{topic:15} {len(result[topic]):3} files  ({time.time() - start:.0f} s)", flush=True)
    data = {
        "_notes": ["Made by tools/run_tests.py --map: for each topic, the files a change to which can "
                   "affect its tests (the source files they ran, measured with coverage; the types they "
                   "name; the enums and constants that code uses; the test files and recordings they use). "
                   "--changed reads it. Remake it after changing which code calls which."],
        "commit": git("rev-parse", "HEAD").strip(),
        "topics": result,
    }
    with open(MAP_FILE, "w") as f:
        json.dump(data, f, indent=1)
        f.write("\n")
    print(f"wrote {rel(MAP_FILE)}")


def load_map():
    if not os.path.exists(MAP_FILE):
        return None
    with open(MAP_FILE) as f:
        return json.load(f)


def topics_for(files, topics, everything, topic_map):
    """The topics a set of changed files can affect (see the module's doc), and why all if so."""
    if topic_map is None:
        return set(topics), "no topic map (run --map): every topic"
    known = {}
    for t, fs in topic_map["topics"].items():
        for f in fs:
            known.setdefault(f, set()).add(t)
    chosen = set()
    for f in files:
        if any(r.match(f) for r in everything):
            return set(topics), f"{f} is shared: every topic"
        if f in known:
            chosen |= known[f]
        elif f.startswith(SOURCE_ROOTS) and (f.endswith(".cs") or f.endswith(".json")):
            return set(topics), f"{f} isn't in the topic map (new, or run by no test): every topic"
    return chosen, ""


# ---------------------------------------------------------------- running

def run_together(names, extra_filter=""):
    """The topics in one test run: per topic [passed, failed, skipped, seconds of test time], and the failures."""
    with tempfile.TemporaryDirectory() as tmp:
        trx = os.path.join(tmp, "results.trx")
        filt = "|".join([f"Topic={n}" for n in names] + ([extra_filter] if extra_filter else []))
        proc = subprocess.run(["dotnet", "test", PROJECT, "--no-build", "--filter", filt,
                               "--logger", f"trx;LogFileName={trx}"],
                              cwd=REPO, capture_output=True, text=True)
        if not os.path.exists(trx):
            return proc.returncode, {}, proc.stdout + proc.stderr
        root = ET.parse(trx).getroot()
    topics = class_topics()
    tests = {t.get("id"): t.find(f"{TRX}TestMethod").get("className").rsplit(".", 1)[-1].split("+")[0]
             for t in root.iter(f"{TRX}UnitTest")}
    per = {n: [0, 0, 0, 0.0] for n in names}
    failures = []
    for r in root.iter(f"{TRX}UnitTestResult"):
        cls = tests.get(r.get("testId"), "")
        topic = "topic checks" if cls == CHECK_CLASS and extra_filter else topics.get(cls, "?")
        row = per.setdefault(topic, [0, 0, 0, 0.0])
        outcome = r.get("outcome")
        row[0 if outcome == "Passed" else 2 if outcome == "NotExecuted" else 1] += 1
        h, m, sec = r.get("duration", "0:0:0").split(":")
        row[3] += int(h) * 3600 + int(m) * 60 + float(sec)
        if outcome not in ("Passed", "NotExecuted"):
            message = r.find(f".//{TRX}Message")
            failures.append(f"{r.get('testName')} ({outcome})\n{message.text if message is not None else ''}")
    return proc.returncode, per, "\n\n".join(failures) or (proc.stdout + proc.stderr if proc.returncode else "")


def run_filter(name, filt):
    start = time.time()
    proc = subprocess.run(["dotnet", "test", PROJECT, "--no-build", "--filter", filt],
                          cwd=REPO, capture_output=True, text=True)
    out = proc.stdout + proc.stderr
    summary = SUMMARY.findall(out)
    return name, proc.returncode, time.time() - start, summary[-1] if summary else None, out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("topics", nargs="*", help="topics to run (default: all)")
    ap.add_argument("--changed", action="store_true", help="only the topics a change can affect")
    ap.add_argument("--since", default="HEAD", help="with --changed: compare with this commit (default HEAD)")
    ap.add_argument("--list", action="store_true", help="list the topics and exit")
    ap.add_argument("--map", action="store_true", help="remake tests/topic-map.json and exit")
    ap.add_argument("--separate", action="store_true", help="each topic in a test run of its own, several at a time")
    ap.add_argument("-j", "--jobs", type=int, default=max(2, (os.cpu_count() or 2) // 2),
                    help="with --separate: topics at a time (default: half the cores, at least 2)")
    ap.add_argument("--no-build", action="store_true", help="don't build the test project first")
    args = ap.parse_args()

    topics, everything, everything_globs = load_topics()
    topic_map = load_map()
    if args.list:
        for name, t in sorted(topics.items(), key=lambda kv: -kv[1].get("weight", 1)):
            n = len(topic_map["topics"].get(name, [])) if topic_map else "?"
            print(f"{name:15} {n:>3} files{'  (alone with --separate)' if t.get('alone') else ''}")
        print("every topic on a change to: " + ", ".join(everything_globs))
        return 0

    if not args.no_build:
        build = subprocess.run(["dotnet", "build", PROJECT, "-v", "q", "-nologo"], cwd=REPO,
                               capture_output=True, text=True)
        if build.returncode:
            print(build.stdout + build.stderr)
            sys.exit("the test project didn't build")
    if args.map:
        make_map(topics)
        return 0

    unknown = [t for t in args.topics if t not in topics]
    if unknown:
        sys.exit(f"unknown topics: {', '.join(unknown)} (see --list)")
    chosen = set(args.topics) if args.topics else set(topics)
    extra = ""
    if args.changed:
        files = changed_files(args.since)
        touched, why = topics_for(files, topics, everything, topic_map)
        chosen &= touched
        extra = f"FullyQualifiedName~{CHECK_CLASS}"
        print(f"{len(files)} files changed since {args.since}: topics {', '.join(sorted(chosen)) or 'none'}"
              + (f" ({why})" if why else ""))
        if topic_map:
            moved = [f for f in git("diff", "--name-only", topic_map["commit"], "--", "src").split() if f.endswith(".cs")]
            if moved:
                print(f"note: {len(moved)} source files changed since the topic map was made; "
                      "remake it (--map) if what calls what changed")
    order = sorted(chosen, key=lambda n: -topics[n].get("weight", 1))

    if not args.separate:
        start = time.time()
        code, per, failures = run_together(order, extra)
        for name in order:
            passed, failed, skipped, secs = per.get(name, [0, 0, 0, 0.0])
            ok = failed == 0 and passed + skipped > 0
            print(f"{'ok  ' if ok else 'FAIL'} {name:15} {passed:>4} passed, {failed} failed, {skipped} skipped  "
                  f"{secs:6.1f} s of tests")
        for name in sorted(set(per) - set(order)):
            passed, failed, skipped, _ = per[name]
            print(f"{'ok  ' if failed == 0 else 'FAIL'} {name:15} {passed:>4} passed, {failed} failed, {skipped} skipped")
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
            _, failed, passed, skipped, _ = summary
            print(f"{'ok  ' if code == 0 else 'FAIL'} {name:15} {passed:>4} passed, {failed} failed, "
                  f"{skipped} skipped  {secs:5.1f} s", flush=True)
        else:
            print(f"FAIL {name:15} no test summary ({secs:.1f} s)", flush=True)
        results.append(r)

    with ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        for r in pool.map(lambda n: run_filter(n, f"Topic={n}"), together):
            report(r)
    for name in alone:
        report(run_filter(name, f"Topic={name}"))
    if extra:
        report(run_filter("topic checks", extra))
    failed = [r for r in results if r[1] != 0 or r[3] is None]
    totals = [sum(int(r[3][i]) for r in results if r[3]) for i in (2, 1, 3)]
    print(f"\n{len(results)} runs, {totals[0]} passed, {totals[1]} failed, {totals[2]} skipped "
          f"in {time.time() - start:.1f} s")
    for name, _, _, _, out in failed:
        print(f"\n===== {name} =====\n{out}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
