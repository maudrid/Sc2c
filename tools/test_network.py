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

Then (stage 2) a host starts in orbit round Earth, guest C joins and
undocks in Earth's inner system, the host leaves orbit with the action
key, and both are in the Sol system, also in the save.
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


def start(out, name, *args):
    log = open(os.path.join(out, name + ".log"), "w")
    return subprocess.Popen([GODOT, "--headless", "--path", GAME, "--", *args,
                             "--report", os.path.join(out, name + ".json")],
                            stdout=log, stderr=subprocess.STDOUT)


def report(out, name):
    with open(os.path.join(out, name + ".json")) as f:
        return json.load(f)


def ship(snapshot, pid):
    return next((s for s in snapshot["Ships"] if s["Id"] == pid), None)


def main():
    out = tempfile.mkdtemp(prefix="crateris-net-")
    failures = []

    def check(cond, what):
        print(("ok   " if cond else "FAIL ") + what)
        if not cond:
            failures.append(what)

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

    # Stage 2: the host starts in orbit round Earth; guest C joins and
    # undocks in Earth's inner system; the host leaves orbit (action key)
    # and both fly in the Sol system.
    INNER, ORBIT = 2, 3    # Activity values in the reports
    save3 = os.path.join(out, "save3.json")
    host3 = start(out, "host3", "--host", "--no-upnp", "--port", PORT,
                  "--name", "Host", "--id", "host", "--save", save3,
                  "--start-orbit", "82:2",
                  "--script", "none:72,action:2,none:500", "--quit-after", "168")
    time.sleep(1)
    c = start(out, "c", "--join", f"127.0.0.1:{PORT}", "--name", "C",
              "--id", "guest-c", "--script", "none:500", "--quit-after", "96")
    for p in (c, host3):
        p.wait(timeout=60)
    h3, rc = report(out, "host3"), report(out, "c")
    check(any("Starting in orbit" in line for line in h3["log"]),
          "host 3 started in orbit round Earth")
    check(rc["welcome"] == "Joined", "C is Joined while the host is in a system")
    check(any("Undocking" in line for line in rc["log"]), "C saw the undocking scene")
    c_ship, c_host = ship(rc["snapshot"], "guest-c"), ship(rc["snapshot"], "host")
    check(c_ship is not None and c_ship["Activity"] == INNER
          and c_ship["SystemStar"] == 82 and c_ship["InnerPlanet"] == 2,
          "C is in Earth's inner system (it undocked next to the host's world)")
    check(c_host is not None and c_host["SystemStar"] == 82
          and c_host["Activity"] == INNER,
          "C sees the host in the same system, out of orbit after the action key")
    h3_host = ship(h3["snapshot"], "host")
    check(h3_host is not None and h3_host["Activity"] == INNER,
          "host left orbit and flies in Earth's inner system")
    with open(save3) as f:
        saved3 = json.load(f)
    check(saved3["version"] >= 4 and all(p.get("system_star") == 82
                                         for p in saved3["players"]),
          "the save keeps both ships in the Sol system")

    print(f"\nlogs and reports in {out}")
    if failures:
        print(f"{len(failures)} check(s) failed")
        sys.exit(1)
    shutil.rmtree(out, ignore_errors=True)
    print("all network checks passed")


if __name__ == "__main__":
    main()
