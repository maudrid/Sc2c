#!/usr/bin/env python3
# Attack on Crateris: end-to-end network test with real Godot processes.
#
# Copyright (C) 2026 Attack on Crateris contributors.
#
# This program is free software; you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation; either version 2 of the License, or
# (at your option) any later version.
"""Run a host and guests headless on this machine and check what happened.

    dotnet build game/Crateris.Game.csproj
    godot --headless --import --path game
    python3 tools/test_network.py

Needs the UQM 0.8.0 content: CRATERIS_CONTENT, or the game's own
downloaded copy. Every process is given it with --content; one more
checks a host without it stops and says so.

Timeline (24 frames a second):
  0 s   host starts, thrusting north
  1 s   guest A joins (first time), turns and thrusts, quits at ~4 s
  2 s   guest B joins and coasts
  6 s   guest A rejoins with the same id and does nothing
  12 s  host quits and saves
  13 s  host restarts from the save; A rejoins once more

Checks: A is Joined then Rejoined and comes back where it left; B is
Joined; both saw the undocking scene; everyone sees everyone; the save
holds all three players.

Then a host quits while a guest is still undocking: the guest says the
host closed the game and does nothing more.

Then (stage 2) a host starts in orbit round Earth's Moon, guest C joins
and undocks in Earth's inner system, the host does an auto scan from the
orbit menu (finding the Moon's nodes) and leaves orbit with the menu's
Navigate, and both are in the Sol system, also in the save.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = os.path.join(REPO, "game")
PORT = "24711"
GODOT = os.environ.get("GODOT", "godot")
PACKAGE = "uqm-0.8.0-content.uqm"


def find_content():
    """The UQM content package: CRATERIS_CONTENT (a file or its folder), or
    the game's own downloaded copy. Hosting and joining need it."""
    given = os.environ.get("CRATERIS_CONTENT")
    if given:
        path = os.path.join(given, PACKAGE) if os.path.isdir(given) else given
        if not os.path.isfile(path):
            sys.exit(f"CRATERIS_CONTENT names {path}, which doesn't exist")
        return path
    data = os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share")
    stored = os.path.join(data, "godot", "app_userdata", "Attack on Crateris",
                          "content", PACKAGE)
    if os.path.isfile(stored):
        return stored
    sys.exit("The network test needs the UQM 0.8.0 content: set CRATERIS_CONTENT to "
             f"{PACKAGE}, or start the game once with a window to download it.")


CONTENT = None


def start(out, name, *args, content=True, env=None):
    log = open(os.path.join(out, name + ".log"), "w")
    extra = ["--content", CONTENT] if content else []
    return subprocess.Popen([GODOT, "--headless", "--path", GAME, "--", *args, *extra,
                             "--report", os.path.join(out, name + ".json")],
                            stdout=log, stderr=subprocess.STDOUT, env=env)


def report(out, name):
    with open(os.path.join(out, name + ".json")) as f:
        return json.load(f)


def ship(snapshot, pid):
    return next((s for s in snapshot["Ships"] if s["Id"] == pid), None)


def main():
    global CONTENT
    CONTENT = find_content()
    out = tempfile.mkdtemp(prefix="crateris-net-")
    failures = []

    def check(cond, what):
        print(("ok   " if cond else "FAIL ") + what)
        if not cond:
            failures.append(what)

    # Without the content, a headless host stops at once and says how to
    # give it one (content stage, decision 4).
    bare = dict(os.environ, XDG_DATA_HOME=os.path.join(out, "no-content"))
    bare.pop("CRATERIS_CONTENT", None)
    nocontent = start(out, "nocontent", "--host", "--no-upnp", "--port", PORT,
                      "--quit-after", "48", content=False, env=bare)
    nocontent.wait(timeout=60)
    with open(os.path.join(out, "nocontent.log")) as f:
        said = f.read()
    check(nocontent.returncode == 3 and "--content PATH" in said,
          "a host without the content stops and says how to give it")

    save = os.path.join(out, "save.json")
    host = start(out, "host", "--host", "--no-upnp", "--port", PORT,
                 "--name", "Host", "--id", "host", "--save", save,
                 "--script", "thrust:200,none:500", "--quit-after", "288")
    time.sleep(1)
    a1 = start(out, "a1", "--join", f"127.0.0.1:{PORT}", "--name", "A",
               "--id", "guest-a", "--script", "right:12,thrust:40,none:100",
               "--quit-after", "72")
    time.sleep(1)
    b = start(out, "b", "--join", f"127.0.0.1:{PORT}", "--name", "B",
              "--id", "guest-b", "--script", "none:500", "--quit-after", "150")
    a1.wait(timeout=60)
    time.sleep(6 - 2 - 72 / 24 + 1)
    a2 = start(out, "a2", "--join", f"127.0.0.1:{PORT}", "--name", "A",
               "--id", "guest-a", "--script", "none:500", "--quit-after", "72")
    for p in (b, a2, host):
        p.wait(timeout=60)

    h, ra1, ra2, rb = (report(out, n) for n in ("host", "a1", "a2", "b"))
    check(host.returncode == 0, "host exited cleanly")
    check(ra1["welcome"] == "Joined", "A's first join is Joined")
    check(ra2["welcome"] == "Rejoined", "A's second join is Rejoined")
    check(rb["welcome"] == "Joined", "B is Joined")
    check(any("Undocking" in line for line in ra1["log"]), "A saw the undocking scene")
    check(any("Undocking" in line for line in rb["log"]), "B saw the undocking scene")
    check(not any("Undocking" in line for line in ra2["log"]), "A doesn't undock again on rejoin")
    # Once undocked, nothing still says the escort is undocking.
    for r, n in ((ra1, "A"), (rb, "B")):
        check(any("] Undocked." in line for line in r["log"])
              and "undocking" not in (r["status"] + r["notice"]).lower(),
              f"{n} was told it undocked, and the undocking message is gone")

    for r, n in ((ra1, "A"), (rb, "B")):
        ids = {s["Id"] for s in r["snapshot"]["Ships"]}
        check({"host", "guest-a", "guest-b"} <= ids or {"host", "guest-b"} <= ids,
              f"{n} sees the other players")
        check(ship(r["snapshot"], "host") is not None, f"{n} sees the host's flagship")

    a_first = ship(ra1["snapshot"], "guest-a")
    a_back = ship(ra2["snapshot"], "guest-a")
    check(a_first is not None and a_first["Facing"] != 0, "A turned during the first visit")
    check(a_back is not None and abs(a_back["UniverseX"] - a_first["UniverseX"]) <= 2
          and abs(a_back["UniverseY"] - a_first["UniverseY"]) <= 2,
          "A came back where it left")

    host_ship = ship(h["snapshot"], "host")
    check(host_ship["UniverseY"] > 1450 + 50, "host flew north")
    with open(save) as f:
        saved = json.load(f)
    ids = {p["id"] for p in saved["players"]}
    check(ids == {"host", "guest-a", "guest-b"}, "save holds all three players")
    check(all(not p["undock_pending"] for p in saved["players"]),
          "undocking recorded as shown")

    # The host restarts from the save; A rejoins the restored game.
    save2 = os.path.join(out, "save2.json")
    host2 = start(out, "host2", "--host", "--no-upnp", "--port", PORT,
                  "--name", "Host", "--load", save, "--save", save2,
                  "--script", "none:500", "--quit-after", "96")
    time.sleep(1)
    a3 = start(out, "a3", "--join", f"127.0.0.1:{PORT}", "--name", "A",
               "--id", "guest-a", "--script", "none:500", "--quit-after", "36")
    for p in (a3, host2):
        p.wait(timeout=60)
    h2, ra3 = report(out, "host2"), report(out, "a3")
    saved_a = next(p for p in saved["players"] if p["id"] == "guest-a")
    a_restored = ship(ra3["snapshot"], "guest-a")
    check(ra3["welcome"] == "Rejoined", "A rejoins a game the host loaded from its save")
    check(a_restored is not None
          and (a_restored["LogX"], a_restored["LogY"]) ==
          (saved_a["flight"]["log_x"], saved_a["flight"]["log_y"]),
          "A is exactly where the save left it")
    check(ship(h2["snapshot"], "host")["LogY"] == next(
        p for p in saved["players"] if p["id"] == "host")["flight"]["log_y"],
        "host's flagship is where the save left it")

    # Stage 2: the host starts in orbit round Earth's Moon; guest C joins
    # and undocks in Earth's inner system; the host scans from the orbit
    # menu (Scan, Auto scan; 5.1 s for the three scans), closes the scan
    # menu, chooses Navigate to leave orbit, and both fly in the Sol system.
    INNER, ORBIT = 2, 3    # Activity values in the reports
    save3 = os.path.join(out, "save3.json")
    host3 = start(out, "host3", "--host", "--no-upnp", "--port", PORT,
                  "--name", "Host", "--id", "host", "--save", save3,
                  "--start-orbit", "82:2:1",
                  "--script", "none:40,select:2,select:2,none:150,cancel:2,up:2,select:2,none:500",
                  "--quit-after", "420")
    time.sleep(1)
    c = start(out, "c", "--join", f"127.0.0.1:{PORT}", "--name", "C",
              "--id", "guest-c", "--script", "none:500", "--quit-after", "330")
    for p in (c, host3):
        p.wait(timeout=60)
    h3, rc = report(out, "host3"), report(out, "c")
    check(any("Starting in orbit" in line for line in h3["log"]),
          "host 3 started in orbit round Earth's Moon")
    check(any("Scan started: AutoScan" in line for line in h3["log"]),
          "the host started an auto scan from the orbit menu")
    # The Moon's nodes, as UQM generates them: 9 mineral deposits, 1
    # energy source and the 10 Brainbox Bulldozers.
    check(any("Scan done: 9 mineral, 1 energy, 10 life shown" in line for line in h3["log"]),
          "the auto scan showed the Moon's 9 mineral, 1 energy and 10 life nodes")
    check(any("Leaving orbit" in line for line in h3["log"]),
          "the host chose Navigate to leave orbit")
    check(rc["welcome"] == "Joined", "C is Joined while the host is in a system")
    check(any("Undocking" in line for line in rc["log"]), "C saw the undocking scene")
    c_ship, c_host = ship(rc["snapshot"], "guest-c"), ship(rc["snapshot"], "host")
    check(c_ship is not None and c_ship["Activity"] == INNER
          and c_ship["SystemStar"] == 82 and c_ship["InnerPlanet"] == 2,
          "C is in Earth's inner system (it undocked next to the host's world)")
    # Order of what C saw, from its log, not from when the processes ran.
    seen = [line.split("] ", 1)[1] for line in rc["log"] if "] Saw host " in line]
    check(len(seen) >= 2 and seen[0].startswith("Saw host Orbit")
          and seen[1].startswith("Saw host InnerSystem"),
          "C saw the host in orbit, then flying after it left orbit")
    check(c_host is not None and c_host["SystemStar"] == 82,
          "C sees the host in the same system")
    h3_host = ship(h3["snapshot"], "host")
    check(h3_host is not None and h3_host["Activity"] == INNER,
          "host left orbit and flies in Earth's inner system")
    with open(save3) as f:
        saved3 = json.load(f)
    check(saved3["version"] >= 4 and all(p.get("system_star") == 82
                                         for p in saved3["players"]),
          "the save keeps both ships in the Sol system")

    # The host quits while guest D is still undocking: D says the host
    # closed the game, and doesn't go on to undock or send to the host.
    save4 = os.path.join(out, "save4.json")
    host4 = start(out, "host4", "--host", "--no-upnp", "--port", PORT,
                  "--name", "Host", "--id", "host", "--save", save4,
                  "--quit-after-join", "24", "--quit-after", "600")
    time.sleep(1)
    d = start(out, "d", "--join", f"127.0.0.1:{PORT}", "--name", "D",
              "--id", "guest-d", "--script", "none:500", "--quit-after", "600")
    for p in (d, host4):
        p.wait(timeout=60)
    rd = report(out, "d")
    lines = [line.split("] ", 1)[1] for line in rd["log"]]
    closed = lines.index("The host closed the game.") if "The host closed the game." in lines else -1
    check(rd["welcome"] == "Joined" and "Undocking" in lines, "D joined and started undocking")
    check(closed >= 0 and not any(l.startswith("Undocked") for l in lines),
          "D was told the host closed the game before it finished undocking")
    check(closed >= 0 and rd["status"] == "The host closed the game." and lines[-1] == lines[closed],
          "nothing happened on D after the host closed the game")

    print(f"\nlogs and reports in {out}")
    if failures:
        print(f"{len(failures)} check(s) failed")
        sys.exit(1)
    shutil.rmtree(out, ignore_errors=True)
    print("all network checks passed")


if __name__ == "__main__":
    main()
