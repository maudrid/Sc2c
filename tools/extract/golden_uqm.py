#!/usr/bin/env python3
# Attack on Crateris: record reference behaviour from UQM 0.8.0's own code.
#
# Copyright (C) 2026 Attack on Crateris contributors.
# The recorded values are produced by The Ur-Quan Masters source code,
# Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
# the UQM contributors.
#
# This program is free software; you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation; either version 2 of the License, or
# (at your option) any later version.
"""Record reference ("golden") behaviour from UQM's own C code.

Builds the programs in tools/extract/golden/ against the UQM 0.8.0
source, linking UQM's real flight and math code, and writes their output
to tests/golden/. The C# port is tested against these files, so it has
to match UQM frame by frame.

    python3 tools/extract/golden_uqm.py --uqm ../uqm-0.8.0/sc2

Needs gcc. See docs/uqm-data.md.
"""

import argparse
import datetime
import json
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import extract_uqm  # noqa: E402

LEFT, RIGHT, THRUST = 1, 2, 4

# name, thrusters, jets, fuel tanks, fuel (1/100 units), start universe
# x, y, steps. Each step is (frames, input flags). Fuel capacity is 10 units
# of reserve plus 50 per tank.
FLIGHT_SCENARIOS = [
    ("new_game_thrust_turn_coast", 2, 2, 1, 1000, 1752, 1450,
     [(40, THRUST), (20, THRUST | LEFT), (60, 0)]),
    ("full_drive_turn_at_max_speed", 11, 8, 1, 5000, 5000, 5000,
     [(50, THRUST), (40, THRUST | RIGHT), (20, THRUST), (80, 0)]),
    ("spin_in_place", 2, 2, 1, 1000, 3000, 3000,
     [(40, RIGHT), (10, THRUST), (30, LEFT), (40, 0)]),
    ("runs_out_of_fuel", 2, 2, 0, 3, 2000, 7000,
     [(150, THRUST), (40, 0)]),
    ("no_fuel_at_start", 4, 2, 0, 0, 2000, 2000,
     [(30, THRUST), (10, RIGHT)]),
    ("long_diagonal_fuel_rollover", 5, 3, 1, 5000, 1000, 1000,
     [(3, RIGHT), (700, THRUST), (40, 0)]),
    ("tapping_thrust", 3, 4, 1, 2000, 6000, 3000,
     [(1, THRUST), (1, 0)] * 20 + [(5, LEFT | THRUST), (5, RIGHT)]),
    ("both_turn_keys", 2, 2, 1, 1000, 4000, 4000,
     [(10, LEFT | RIGHT | THRUST), (30, 0)]),
    ("fuel_above_capacity_is_capped", 2, 2, 0, 1500, 5000, 2000,
     [(40, THRUST)]),
]

FLIGHT_LINK = ["uqm/ship.c", "uqm/ships/sis_ship/sis_ship.c",
               "uqm/velocity.c", "uqm/trans.c", "libs/math/sqrt.c",
               "uqm/globdata.c"]
MATH_LINK = ["uqm/trans.c", "libs/math/sqrt.c", "uqm/velocity.c"]
LANDER_LINK = ["uqm/velocity.c", "uqm/trans.c", "uqm/displist.c",
               "uqm/globdata.c", "libs/math/sqrt.c", "libs/graphics/intersec.c",
               "libs/graphics/boxint.c", "uqm/plandata.c"]   # random.c is included
LANDER_STUBS = os.path.join(HERE, "golden", "lander_stubs.c")


def lander_script(seed, frames):
    """A long scripted drive: steps of 1 to 40 frames of any mix of left,
    right, thrust and fire (never take-off), from a seeded generator."""
    import random
    rng = random.Random(seed)
    steps, total = [], 0
    while total < frames:
        n = rng.randint(1, 40)
        steps.append((n, rng.choice([0, 4, 4, 4, 5, 6, 12, 12, 13, 14, 8, 1, 2, 9, 10])))
        total += n
    return steps


# Landings: [name, TFB_Random seed, upgrades (1 speed, 2 shot), tectonics,
# weather, temperature, site x, y (planetside map units), ship's crew,
# steps]. No hazards yet (item 8): every chance is 0, but BuildObjectList
# still draws its random number each frame.
LANDER_SCENARIOS = [
    ["still", 1, 0, 0, 0, 0, 484, 148, 50, [(10, 0)]],
    ["take-off-at-once", 2, 0, 0, 0, 0, 484, 148, 50, [(1, 16)]],
    ["five-crew", 3, 0, 0, 0, 0, 300, 100, 5, [(20, 4), (5, 16)]],
    ["turn-and-drive", 7, 0, 0, 0, 0, 10, 2, 50,
     [(30, 4), (10, 2), (30, 12), (20, 8), (12, 1), (40, 4), (6, 13)]],
    ["north-pole", 9, 0, 0, 0, 0, 100, 0, 50,
     [(3, 1), (60, 4), (6, 2), (40, 4), (20, 0)]],
    ["south-pole", 10, 0, 0, 0, 0, 900, 299, 50,
     [(80, 4), (5, 1), (80, 4)]],
    ["east-edge", 11, 0, 0, 0, 0, 967, 150, 50, [(200, 12)]],
    ["west-edge", 12, 0, 0, 0, 0, 0, 150, 50, [(200, 12)]],
    ["long-drive", 101, 0, 0, 0, 0, 484, 148, 50, lander_script(101, 1500)],
    ["long-drive-speed", 102, 1, 0, 0, 0, 200, 50, 50, lander_script(102, 1500)],
    ["long-drive-shot", 103, 2, 0, 0, 0, 700, 250, 50, lander_script(103, 1500)],
    ["long-drive-both", 104, 3, 0, 0, 0, 484, 148, 30, lander_script(104, 1500)],
]
STRINGS_LINK = ["libs/strings/getstr.c", "libs/strings/strings.c",
                "libs/memory/w_memlib.c", "libs/resource/propfile.c",
                "libs/resource/loadres.c", "libs/strings/stringhashtable.c"]

# UQM's file calls as the C library's, for the programs that read content
# with UQM's loaders.
UIO_FILES = os.path.join(HERE, "golden", "uio_files.c")

# The resource types UQM 0.8.0 registers (every InstallResTypeVectors
# call); any other type is UNKNOWNRES, as newResourceDesc in resinit.c has
# it (and ResourceIndex in ContentPackage.cs).
FONTS_LINK = ["libs/strings/unicode.c", "libs/graphics/boxint.c",
              "libs/graphics/sdl/png2sdl.c"]
IMAGES_LINK = ["libs/graphics/sdl/png2sdl.c"]
# The SDL 1.2 stand-in UQM's PNG loader is built against.
SDL_STANDIN = os.path.join(HERE, "golden", "sdl.c")
SDL_INCLUDE = os.path.join(HERE, "golden", "sdl")
RESOURCE_TYPES = {"3DOVID", "BINTAB", "BOOLEAN", "COLOR", "CONVERSATION",
                  "FONTRES", "GFXRES", "INT32", "MUSICRES", "SHIP", "SNDRES",
                  "STRING", "STRTAB", "UNKNOWNRES"}


def read_resource_index(exe, path):
    """A resource index as UQM reads it: UQM's own PropFile_from_string
    (run by the strings harness) gives the key/value pairs, a later key
    replaces an earlier one (process_resource_desc), and the type is what
    comes before the first ':' if UQM knows it (newResourceDesc)."""
    out = subprocess.run([exe, "index", path], capture_output=True,
                         text=True, check=True).stdout
    entries = {}
    for key, value in json.loads(out):
        kind, sep, rest = value.partition(":")
        entries[key] = (kind, rest) if sep and kind in RESOURCE_TYPES \
            else ("UNKNOWNRES", value)
    return entries

# The official content package's SHA-256 (as in ContentPackage.cs).
CONTENT_SHA256 = "77d75ac25e6fb755a33c4ba3b38a7b7bc41fcbc02896891b0cc9ac9214b72eef"


def systems_link(uqm):
    gen = os.path.join(uqm.uqm, "planets", "generate")
    return (["uqm/planets/orbits.c", "uqm/planets/calc.c",
             "uqm/planets/surface.c", "uqm/planets/gentopo.c",
             "uqm/planets/pl_stuff.c", "uqm/gendef.c",
             "uqm/plandata.c", "uqm/starmap.c", "uqm/globdata.c",
             "libs/math/random2.c", "uqm/trans.c", "libs/math/sqrt.c",
             "uqm/velocity.c", "libs/graphics/intersec.c",
             "libs/graphics/boxint.c"] +
            sorted(f"uqm/planets/generate/{n}" for n in os.listdir(gen)
                   if n.endswith(".c")))


# Our own stand-ins for the parts of UQM the systems program doesn't need.
SYSTEMS_STUBS = os.path.join(HERE, "golden", "stubs.c")


def build(uqm, main_c, link, out, extra=(), libs=(), includes=(), defines=()):
    cflags = ["-w", "-no-pie", *(f"-I{d}" for d in includes), *(f"-D{d}" for d in defines),
              "-I", uqm.build, "-I", uqm.src, "-I", uqm.uqm,
              "-I", os.path.join(uqm.uqm, "ships", "sis_ship"),
              "-I", os.path.join(uqm.uqm, "planets")]
    objs = []
    for i, src in enumerate([main_c, *extra] + [os.path.join(uqm.src, f)
                                                for f in link]):
        obj = os.path.join(uqm.build, f"{os.path.basename(out)}_{i}.o")
        res = subprocess.run(["gcc", *cflags, "-c", src, "-o", obj],
                             capture_output=True, text=True)
        if res.returncode:
            raise RuntimeError(f"gcc failed for {src}:\n{res.stderr}")
        objs.append(obj)
    subprocess.run(["gcc", "-no-pie", *objs, "-o", out,
                    "-Wl,--unresolved-symbols=ignore-all", *libs], check=True,
                   capture_output=True, text=True)


def header(uqm, files, notes):
    return {
        "_notice": extract_uqm.NOTICE.replace(
            "Extracted for Attack on Crateris by tools/extract/"
            "extract_uqm.py", "Recorded for Attack on Crateris by "
            "tools/extract/golden_uqm.py"),
        "_source": {"repository": extract_uqm.UQM_REPO, "branch": "v0.8.0",
                    "commit": uqm.commit(), "files": files},
        "_notes": notes,
    }


def record_flight(uqm, exe):
    lines = []
    for name, thr, jets, tanks, fuel, sx, sy, steps in FLIGHT_SCENARIOS:
        flat = " ".join(f"{f} {fl}" for f, fl in steps)
        lines.append(f"{name} {thr} {jets} {tanks} {fuel} {sx} {sy} "
                     f"{len(steps)} {flat}")
    out = subprocess.run([exe], input="\n".join(lines) + "\n",
                         capture_output=True, text=True, check=True).stdout
    scenarios = [json.loads(line) for line in out.splitlines()]
    if len(scenarios) != len(FLIGHT_SCENARIOS):
        raise RuntimeError("flight harness lost a scenario")
    data = header(uqm, ["tools/extract/golden/flight.c"] +
                  [f"src/{f}" for f in FLIGHT_LINK] + ["src/uqm/hyper.c"], [
        "Hyperspace flight of the flagship, recorded by running UQM's own "
        "ship_preprocess, sis_hyper_preprocess, inertial_thrust, init_sis "
        "and MoveSIS (see tools/extract/golden/flight.c).",
        "input: [frames, flags] steps; flags 1 left, 2 right, 4 thrust.",
        "ship: characteristics init_sis derives from the drive and jet "
        "slots, in hyperspace.",
        "frames: one entry per frame after it is processed: [facing, "
        "velocity dx, velocity dy (velocity units, 1/32 world unit), log_x, "
        "log_y, fuel (1/100 units), status flags (1 left, 2 right, 4 "
        "thrust, 128 at max speed, 256 beyond max speed), turn_wait, "
        "thrust_wait].",
        "Screen is UQM's default 320x240; the hyperspace view and its "
        "per-frame scroll limit depend on it.",
    ])
    data["scenarios"] = scenarios
    return data


def run_lander(exe, scenarios, masks=None, draw=False):
    """Runs the lander program on landings [name, seed, upgrades,
    tectonics, weather, temperature, x, y, crew, storage, nodes, steps];
    returns its landings with their input."""
    lines = []
    for name, seed, upgrades, tect, weather, temp, x, y, crew, storage, nodes, steps in scenarios:
        flat_nodes = " ".join(" ".join(map(str, n)) for n in nodes)
        flat = " ".join(f"{n} {f}" for n, f in steps)
        lines.append(f"{name} {seed} {upgrades} {tect} {weather} {temp} {x} {y} "
                     f"{crew} {storage} {len(nodes)} {flat_nodes} {len(steps)} {flat}")
    out = subprocess.run([exe] + ([masks] if masks else []) + (["draw"] if draw else []), input="\n".join(lines) + "\n",
                         capture_output=True, text=True, check=True).stdout
    landings = json.loads("[" + out.replace("}\n{", "},\n{") + "]")
    if len(landings) != len(scenarios):
        raise RuntimeError("the lander program lost a landing")
    for landing, scenario in zip(landings, scenarios):
        landing["input"] = {"tectonics": scenario[3], "weather": scenario[4],
                            "temperature": scenario[5], "site": scenario[6:8],
                            "crew": scenario[8], "storage": scenario[9],
                            "nodes": scenario[10], "steps": scenario[11]}
        if not any(f[0] == "G" for f in landing["frames"]) and scenario[11][0][1] != 16:
            raise RuntimeError(f"{landing['name']}: never on the ground")
    return landings


def record_lander(uqm, exe):
    """UQM's own lander on the surface, frame by frame (lander.c)."""
    landings = run_lander(exe, [[*s[:9], 500, [], s[9]] for s in LANDER_SCENARIOS])
    data = header(uqm, ["tools/extract/golden/lander.c",
                        "tools/extract/golden/lander_stubs.c",
                        "src/uqm/planets/lander.c", "src/uqm/velocity.c",
                        "src/libs/math/random.c"], [
        "UQM 0.8.0's own lander, recorded by running lander.c's PlanetSide "
        "(the warm-up, InitPlanetSide's jitter, the landing, DoPlanetSide "
        "under DoInput, the take-off) with drawing and sound stubbed, a "
        "clock that each sleep moves on (so every frame takes no time), "
        "TFB_Random seeded and the input scripted (tools/extract/golden/"
        "lander.c). Code only, no content.",
        "input.steps: [frames, flags], each held for that many of "
        "DoPlanetSide's frames (its first call only sets the lander going): "
        "1 left, 2 right, 4 thrust, 8 fire, 16 take off. After the steps "
        "the lander takes off. upgrades: 1 IMPROVED_LANDER_SPEED, 2 "
        "IMPROVED_LANDER_SHOT. site: planetLoc, in planetside map units.",
        "frames: every ScrollPlanetSide, [phase (L landing, G on the "
        "ground, T taking off), x, y (curLanderLoc), facing (0 to 15), "
        "offset (landingOffset: how far above its place the lander is "
        "drawn, 0 on the ground, null if not drawn), crew aboard the "
        "lander, TFB_Random's seed after the frame, the element queue's "
        "[x, y, life_span, playerNr] in order (the lander's bolts), the "
        "nodes left (0), the lander's hold, the mineral text and the nodes "
        "taken (all empty here; see lander-pickups.json)]. crew_left, ship_crew and "
        "landers: after PlanetSide returns.",
    ])
    data["landings"] = landings
    return data


def pickup_worlds():
    """Worlds with mineral nodes only (no energy, no life), most nodes first:
    [star, planet, moon or -1, nodes]."""
    with open(os.path.join(REPO, "tests", "golden", "world-surfaces.json")) as f:
        systems = json.load(f)["systems"]
    worlds = []
    for s, star in enumerate(systems):
        for p, planet in enumerate(star):
            for m, w in enumerate(planet):
                if len(w) < 5 or not w[1] or w[4] is None:
                    continue
                minerals, energy, life = w[4]
                if minerals and not energy and not life and w[3][8] != 0xFFFF:
                    worlds.append((-len(minerals), s, p, m - 1, minerals))
    worlds.sort()
    return [[s, p, m, [[0, t, d, x, y] for x, y, t, d in minerals]]
            for _, s, p, m, minerals in worlds]


def pickup_nodes(seed, count, cx, cy):
    """Mineral nodes packed round a site on the scan map: every size class
    and many amounts and elements, from a seeded generator."""
    import random
    rng = random.Random(seed)
    nodes = []
    for _ in range(count):
        x = (cx + rng.randint(-25, 25)) % 242
        y = max(0, min(74, cy + rng.randint(-12, 12)))
        nodes.append([0, rng.randrange(133), (rng.randint(1, 70) << 8) | rng.randint(0, 2), x, y])
    return nodes


def tfb_random(seed):
    """TFB_Random's next seed (libs/math/random.c, unsigned 32-bit)."""
    seed = (16807 * (seed % 127773) - 2836 * (seed // 127773)) & 0xFFFFFFFF
    if seed > 2147483647:
        seed -= 2147483647
    elif seed == 0:
        seed = 1
    return seed


def landing_place(seed, x, y):
    """Where PlanetSide puts the lander for a site: the facing, then
    InitPlanetSide's jitter, wrapping east and west and kept off the poles."""
    r = tfb_random(seed)            # the facing
    r = tfb_random(r)
    x = (x - (64 - r % 128)) % 968
    r = tfb_random(r)
    y = max(0, min(299, y - (64 - r % 128)))
    return x, y


def seam_scenarios():
    """Landings within a few units of the east-west edge, with nodes just
    across it, so the lander touches them the nearer way round."""
    out = []
    for name, want, node_x, base in [("seam-east", range(0, 3), 241, 500),
                                     ("seam-west", range(965, 968), 0, 600)]:
        seed = next(s for s in range(base, base + 100000)
                    if landing_place(s, 0, 148)[0] in want)
        x, y = landing_place(seed, 0, 148)
        nodes = [[0, k * 7 % 133, (20 << 8) | 1, node_x, (y >> 2) + dy]
                 for k, dy in enumerate((-1, 0, 1))]
        out.append([name, seed, 0, 0, 0, 0, 0, 148, 50, 500, nodes,
                    [(5, 0)] + lander_script(seed, 300)])
    return out


def pickup_scenarios():
    """Landings among mineral nodes: [name, seed, upgrades (1 speed, 2
    shot, 4 cargo), tectonics, weather, temperature, site x, y, crew,
    storage room, nodes, steps]."""
    worlds = pickup_worlds()
    out = []
    for k, (s, p, m, nodes) in enumerate(worlds[:3]):
        _, t, d, x, y = nodes[0]
        out.append([f"world-{s}-{p}-{m}", 200 + k, [0, 4, 3][k], 0, 0, 0, x * 4, y * 4,
                    50, 500, nodes, lander_script(200 + k, 1500)])
    for k, (upgrades, storage, fire) in enumerate([(0, 500, False), (4, 500, False),
                                                  (0, 30, False), (4, 70, False),
                                                  (2, 500, True), (6, 500, True)]):
        nodes = pickup_nodes(300 + k, 30, 121, 37)
        steps = lander_script(300 + k, 1200)
        if not fire:
            steps = [(n, f & ~8) for n, f in steps]
        out.append([f"packed-{k}", 300 + k, upgrades, 0, 0, 0, 484, 148, 50, storage,
                    nodes, steps])
    # Among nodes at each pole (the text moving as the lander stops there)
    # and across the east-west edge (contact the nearer way round).
    for k, (name, cx, cy, sx, sy) in enumerate([("north-pole", 60, 2, 240, 0),
                                                ("south-pole", 180, 72, 720, 299),
                                                ("east-west", 0, 37, 4, 148),
                                                ("west-east", 241, 37, 964, 148)]):
        out.append([f"packed-{name}", 400 + k, [0, 4, 2, 6][k], 0, 0, 0, sx, sy, 50, 500,
                    pickup_nodes(400 + k, 30, cx, cy), lander_script(400 + k, 900)])
    return out + seam_scenarios()


def creature_worlds():
    """Worlds with creatures (of the 23 ordinary kinds) and no energy
    nodes, chosen so that every kind is on one of them, most kinds first:
    [star, planet, moon or -1, nodes ([scan, type, density, x, y], life
    then minerals)]."""
    with open(os.path.join(REPO, "tests", "golden", "world-surfaces.json")) as f:
        systems = json.load(f)["systems"]
    worlds = []
    for s, star in enumerate(systems):
        for p, planet in enumerate(star):
            for m, w in enumerate(planet):
                if len(w) < 5 or not w[1] or w[4] is None:
                    continue
                minerals, energy, life = w[4]
                if not life or energy or any(t >= 23 for _, _, t, _ in life):
                    continue
                nodes = ([[2, t, d, x, y] for x, y, t, d in life]
                         + [[0, t, d, x, y] for x, y, t, d in minerals])
                worlds.append((s, p, m - 1, {t for _, _, t, _ in life}, nodes))
    chosen, left = [], set(range(23))
    while left:
        best = max(worlds, key=lambda w: (len(w[3] & left), len(w[4])))
        if not best[3] & left:
            break
        chosen.append(best)
        left -= best[3]
    return [[s, p, m, nodes] for s, p, m, _, nodes in chosen], left


def creature_scenarios():
    """Landings among creatures: [name, seed, upgrades (1 speed, 2 shot, 4
    cargo, 8 biological shield), tectonics, weather, temperature, site x,
    y, crew, storage room, nodes, steps]."""
    worlds, missing = creature_worlds()
    if missing:
        raise RuntimeError(f"no world has creatures of kinds {sorted(missing)}")
    out = []
    for k, (s, p, m, nodes) in enumerate(worlds):
        _, _, _, x, y = nodes[0]
        out.append([f"life-{s}-{p}-{m}", 500 + k, [0, 2, 4, 3][k % 4], 0, 0, 0,
                    x * 4, y * 4, 50, 500, nodes, lander_script(500 + k, 600)])
    # The deadliest kinds (they move, so generateBioNode puts them at random
    # places), a lander with few crew; then the same with the biological
    # shield.
    # Three kinds at most, as UQM has sheets for (MAX_LIFE_VARIATION).
    deadly = [[2, t, 0, 121 + dx, 37 + dy] for t in (9, 14, 13)
              for dx, dy in ((-6, 0), (6, 0), (0, 5), (0, -5))]
    for k, upgrades in enumerate((0, 8)):
        out.append([f"deadly-{'shielded' if upgrades else 'bare'}", 600 + k, upgrades,
                    0, 0, 0, 484, 148, 4, 500, deadly, lander_script(600 + k, 800)])
    # Harmless still creatures round the site, to stun, can and pick up.
    import math
    ring = [[2, (0, 1, 4)[i % 3], 0, 121 + round(r * math.cos(i * 0.7)), 37 + round(r * math.sin(i * 0.7) / 2)]
            for i, r in enumerate([5, 7, 9, 11, 13, 15] * 4)]
    for k in range(2):
        out.append([f"harvest-{k}", 700 + k, [0, 2][k], 0, 0, 0, 484, 148, 50, 500, ring,
                    lander_script(700 + k, 900)])
    return out


def record_lander_creatures(uqm, lander_exe, strings_exe, images_exe, package):
    """UQM's own lander among creatures, with the package's masks."""
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, LANDER_SHEETS)
        landings = run_lander(lander_exe, creature_scenarios(), masks)
    canned = sum(1 for l in landings for f in l["frames"] for e in f[7]
                 if len(e) > 5 and e[4] == 0)
    if canned < 10:
        raise RuntimeError(f"hardly any creature canned ({canned} frames)")
    if not any(l["landers"] == 0 for l in landings):
        raise RuntimeError("no lander lost in the creature landings")
    data = header(uqm, ["tools/extract/golden/lander.c",
                        "tools/extract/golden/lander_stubs.c",
                        "src/uqm/planets/lander.c", "src/uqm/planets/scan.c",
                        "src/libs/graphics/intersec.c"], [
        "UQM 0.8.0's own lander among creatures, recorded as lander.json "
        "is, with the life nodes put on the surface as GeneratePlanetSide "
        "and generateBioNode put them (TFB_Random's draws for the moving "
        "ones' places and every creature's first frame come first) and "
        "contact found by UQM's own DrawablesIntersect with the masks of "
        "graphics.lander, graphics.miscdata, graphics.lifecan and "
        "graphics.life.0 to 25 from the official content package (SHA-256 "
        f"{CONTENT_SHA256}). Positions and events come from code and the "
        "masks' shapes; no content.",
        "Landings on worlds with creatures and no energy nodes, chosen so "
        "every ordinary kind (0 to 22) is on one, with their mineral nodes "
        "too, landing on the first creature's node and firing, with "
        "upgrades by turn; and among the most dangerous kinds with 4 crew, "
        "without and with the biological shield (8).",
        "input as in lander-pickups.json, nodes [scan (0 mineral, 2 life), "
        "type, density, x, y]. frames as in lander.json; a creature's "
        "element is [x, y, scan_node, playerNr, hit_points, mass_points "
        "(its kind, | 128 when aware; its value once canned), sheet (9 "
        "canister, 10 + n graphics.life.n), frame, thrust_wait, flashing].",
    ])
    data["masks"] = {k: [m[:4] + [m[4]] for m in v] for k, v in hashes.items()}
    data["landings"] = landings
    return data


def world_nodes(star, planet, moon):
    """A world's report and nodes from world-surfaces.json: (tectonics,
    weather, temperature, nodes [scan, type, density, x, y], life then
    minerals)."""
    with open(os.path.join(REPO, "tests", "golden", "world-surfaces.json")) as f:
        w = json.load(f)["systems"][star][planet][moon + 1]
    report, (minerals, energy, life) = w[3], w[4]
    if energy:
        raise RuntimeError(f"world {star}-{planet}-{moon} has energy nodes")
    return (report[1], report[2], report[6],
            [[2, t, d, x, y] for x, y, t, d in life] + [[0, t, d, x, y] for x, y, t, d in minerals])


def hazard_scenarios():
    """Landings among hazards: [name, seed, upgrades (1 speed, 2 shot, 4
    cargo; shields 8 biological, 16 quake, 32 lightning, 64 lava),
    tectonics, weather, temperature, site x, y, crew, storage room, nodes,
    steps], on real worlds with their nodes."""
    worlds = [("shaky", 293, 1, -1), ("stormy", 212, 0, -1), ("stormy-hot", 484, 0, -1),
              ("hot-life", 317, 4, -1), ("all-three", 240, 1, -1)]
    out = []
    for k, (name, s, p, m) in enumerate(worlds):
        tect, weather, temp, nodes = world_nodes(s, p, m)
        out.append([f"{name}-{s}-{p}-{m}", 800 + k, [0, 2, 4, 3, 1][k], tect, weather, temp,
                    484, 148, 50, 500, nodes, lander_script(800 + k, 700)])
    tect, weather, temp, nodes = world_nodes(240, 1, -1)
    # Few crew among every hazard: bare, with every shield, with each one.
    for k, (label, shields) in enumerate([("bare", 0), ("shielded", 120), ("quake-shield", 16),
                                          ("lightning-shield", 32), ("lava-shield", 64)]):
        out.append([f"few-crew-{label}", 900 + k, shields, tect, weather, temp,
                    300 + 100 * k, 150, 6, 500, nodes, lander_script(900 + k, 900)])
    # Lava and quakes round the poles (their places wrap north and south,
    # spreading lava stops at the poles) and across the east-west edge.
    _, _, hot, hot_nodes = world_nodes(393, 1, -1)
    for k, (label, x, y) in enumerate([("north", 200, 0), ("south", 700, 299),
                                       ("west-edge", 2, 150), ("east-edge", 965, 150)]):
        out.append([f"hot-{label}", 950 + k, 0, 7, 0, hot, x, y, 50, 500, hot_nodes,
                    [(20, 0)] + lander_script(950 + k, 500)])
    # Milder worlds, where a lander can come back.
    for k, (name, s, p, m, upgrades) in enumerate([("mild", 394, 0, 1, 0), ("mild", 285, 0, -1, 2),
                                                   ("mild-shielded", 394, 0, 1, 120)]):
        t, w, h, mild = world_nodes(s, p, m)
        out.append([f"{name}-{s}-{p}-{m}", 970 + k, upgrades, t, w, h, 484, 148, 50, 500,
                    mild, lander_script(970 + k, 500)])
    # Hazards while the lander comes down and takes off at once.
    out.append(["all-three-take-off", 960, 0, tect, weather, temp, 484, 148, 50, 500, nodes, [(1, 16)]])
    # Long landings with every shield among so many hazards that UQM's
    # display queue fills (MAX_DISPLAY_ELEMENTS, nodes and all): what can't
    # start doesn't. The worlds' deposits are copied far off round the map
    # (the lander drives a few hundred units at most) to fill it sooner.
    def crowd(nodes, n):
        minerals = [m for m in nodes if m[0] == 0]
        far = [[0, t, d, (x * 7 + 3 * i) % 30 + 200, (y + 5 * i) % 75]
               for i in range(n // len(minerals) + 1) for _, t, d, x, y in minerals]
        return nodes + far[:n]
    out.append(["crowded-all-three", 980, 120 | 2, tect, weather, temp, 484, 148, 50, 500,
                crowd(nodes, 90), lander_script(980, 3000)])
    for k in range(2):
        out.append([f"crowded-hot-{k}", 981 + k, 120 | 2, 7, 0, hot, 484, 148, 50, 500,
                    crowd(hot_nodes, 70), lander_script(981 + k, 3000)])
    return out


def drawing_scenarios():
    """Landings whose drawing is recorded: among deposits, creatures to
    can, the deadliest with the shield, every hazard with few crew (the
    lander lost) and with every shield, cut to a few hundred frames."""
    by_name = {sc[0]: sc for sc in pickup_scenarios() + creature_scenarios() + hazard_scenarios()}
    out = []
    for name, frames in (("packed-4", 400), ("packed-west-east", 350), ("harvest-0", 500), ("deadly-shielded", 400),
                         ("few-crew-bare", 400), ("mild-shielded-394-0-1", 400)):
        key = name if name in by_name else next(k for k in by_name if k.startswith(name))
        sc = list(by_name[key])
        steps, total = [], 0
        for n, flags in sc[11]:
            if total >= frames:
                break
            steps.append((min(n, frames - total), flags))
            total += steps[-1][0]
        sc[11] = steps
        out.append(sc)
    return out


def cargo_cases():
    """Holds for the cargo display: [amounts by category (8), bio data,
    the bays' capacity (GetStorageBayCapacity), the category selected]."""
    import random
    rng = random.Random(5)
    cases = [[0] * 8 + [0, 500, 0], [1, 2, 3, 4, 5, 6, 7, 8, 9, 500, 7],
             [500, 0, 0, 0, 0, 0, 0, 0, 0, 500, 3], [10, 0, 999, 0, 0, 0, 0, 0, 50, 1500, 2]]
    for _ in range(8):
        a = [rng.choice([0, rng.randint(1, 9), rng.randint(10, 99), rng.randint(100, 999)]) for _ in range(8)]
        bays = (sum(a) + 499) // 500 + rng.randint(0, 3)
        cases.append(a + [rng.randint(0, 150), max(bays, 1) * 500, rng.randint(0, 7)])
    return cases


def record_cargo(lander_exe, masks):
    """UQM's cargo display (cargo.c's DrawCargoStrings) for each case."""
    with open(os.path.join(REPO, "data", "elements.json")) as f:
        worth = [c["worth"] for c in json.load(f)["categories"]]
    lines = [" ".join(map(str, c[:9] + worth + c[9:])) for c in cargo_cases()]
    out = subprocess.run([lander_exe, masks, "cargo"], input="\n".join(lines) + "\n",
                         capture_output=True, text=True, check=True).stdout
    draws = json.loads(out)
    if len(draws) != len(lines):
        raise RuntimeError("the lander program lost a cargo display")
    return [{"input": c, "draws": d} for c, d in zip(cargo_cases(), draws)]


def record_lander_drawing(uqm, lander_exe, strings_exe, images_exe, package):
    """What UQM's lander draws on the surface view and in its gauges."""
    with tempfile.TemporaryDirectory() as tmp:
        masks, _ = write_masks(strings_exe, images_exe, package, tmp, HAZARD_SHEETS)
        landings = run_lander(lander_exe, drawing_scenarios(), masks, draw=True)
        cargo = record_cargo(lander_exe, masks)
    data = header(uqm, ["tools/extract/golden/lander.c",
                        "tools/extract/golden/lander_stubs.c",
                        "src/uqm/planets/lander.c", "src/libs/graphics/frame.c"], [
        "What UQM 0.8.0's own lander draws, frame by frame, recorded as "
        "lander-hazards.json is (and with its masks) with the drawing "
        "recorded: each frame adds two lists after the explosion, what was "
        "drawn on the surface view (PlanetContext: ScrollPlanetSide's map, "
        "DrawBatch's display list from its head, the lander, its shadow and "
        "shield, the mineral text) and in the lander's gauges (RadarContext: "
        "InitLander before the landing, as PickPlanetSide calls it, then "
        "AnimateLanderWarmup, DeltaLanderCrew and FillLanderHold), in order. "
        "Each is [\"m\", x, y] the planet's map, [\"s\", sheet, frame, x, "
        "y] a stamp, [\"f\", sheet, frame, x, y, r, g, b] a filled stamp, "
        "[\"r\", x, y, width, height, r, g, b] a filled rectangle, or "
        "[\"t\", x, y, align, text, characters (-1 all), r, g, b] text "
        "(the game's strings are empty here; the amount is code). Sheets "
        "as in lander.json. Code and the masks' shapes; no content.",
        "Landings from lander-pickups.json, lander-creatures.json and "
        "lander-hazards.json, cut to their first few hundred frames.",
        "cargo: the Manifest's cargo display as cargo.c's DrawCargoStrings "
        "first draws it (in the status area, StatusContext), for holds "
        "[amounts by element category (8), bio data, the bays' capacity, "
        "the category selected], with each category's worth from "
        "elements.json: the same kinds of drawing call, text with a font "
        "after its colour (1 StarConFont, 2 TinyFont).",
    ])
    data["landings"] = landings
    data["cargo"] = cargo
    return data


def hazard_chances(exe):
    """PlanetSide's hazard chances for every world orbited in
    world-surfaces.json, from its report as generateOrbital leaves it:
    [star, planet, moon, [tectonics, weather, fire]] out of 256."""
    with open(os.path.join(REPO, "tests", "golden", "world-surfaces.json")) as f:
        systems = json.load(f)["systems"]
    worlds = [(s, p, m - 1, w[3]) for s, star in enumerate(systems) for p, planet in enumerate(star)
              for m, w in enumerate(planet) if len(w) > 3 and w[1]]
    out = subprocess.run([exe, "chances"], input="".join(f"{r[1]} {r[2]} {r[6]}\n" for *_, r in worlds),
                         capture_output=True, text=True, check=True).stdout
    chances = json.loads(out)
    if len(chances) != len(worlds):
        raise RuntimeError("the lander program lost a world's chances")
    return [[s, p, m, c] for (s, p, m, _), c in zip(worlds, chances)]


def record_lander_hazards(uqm, lander_exe, strings_exe, images_exe, package):
    """UQM's own lander among quakes, lightning and lava, with the
    package's masks; and every world's hazard chances."""
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, HAZARD_SHEETS)
        landings = run_lander(lander_exe, hazard_scenarios(), masks)
    hazards = [e for l in landings for f in l["frames"] for e in f[7] if len(e) == 9]
    for sheet, what in ((1, "quake"), (2, "lightning"), (3, "lava")):
        if not any(e[5] == sheet for e in hazards):
            raise RuntimeError(f"no {what} in the hazard landings")
    if not any(l["landers"] == 0 for l in landings):
        raise RuntimeError("no lander lost in the hazard landings")
    full = sum(1 for l in landings for f in l["frames"] if len(f[7]) + f[8] >= 150)
    if full < 100:
        raise RuntimeError(f"the display queue was full in only {full} frames")
    data = header(uqm, ["tools/extract/golden/lander.c",
                        "tools/extract/golden/lander_stubs.c",
                        "src/uqm/planets/lander.c", "src/uqm/planets/scan.c",
                        "src/libs/graphics/intersec.c"], [
        "UQM 0.8.0's own lander among hazards, recorded as lander-creatures."
        "json is, with PlanetSide's chances of quakes, lightning and lava "
        "from the world's tectonics, weather and temperature "
        "(GetHazardChance, GetThermalHazardRating), and contact found by "
        "UQM's own DrawablesIntersect with the masks of graphics.lander, "
        "graphics.quake, graphics.lightning, graphics.lavaspot, "
        "graphics.miscdata, graphics.lifecan and graphics.life.0 to 25 from "
        f"the official content package (SHA-256 {CONTENT_SHA256}). Positions "
        "and events come from code and the masks' shapes; no content.",
        "Landings on real worlds with their nodes (world-STAR-PLANET-MOON's "
        "report and nodes in world-surfaces.json): the shakiest, the "
        "stormiest, stormy and hot, hot with life, and one with all three; "
        "on that one with 6 crew, bare, with every shield (8 biological, 16 "
        "quake, 32 lightning, 64 lava) and with each hazard's shield alone; "
        "on a hot world, with quakes too, at each pole and either side of "
        "the east-west edge; on two milder worlds, without and with every "
        "shield; taking off at once; and three long landings with every "
        "shield (crowded-*) among so many hazards that UQM's display queue "
        "fills (MAX_DISPLAY_ELEMENTS, 150 nodes and all).",
        "input and frames as in lander-creatures.json; a hazard's element "
        "is [x, y, life_span, playerNr, mass_points (1 a quake, 2 "
        "lightning, 0 harmless lightning, 3 lava), sheet (1 graphics.quake, "
        "2 graphics.lightning, 3 graphics.lavaspot), frame, facing, "
        "turn_wait].",
        "chances: every orbited world's [star, planet, moon (-1 the planet), "
        "[TectonicsChance, WeatherChance, FireChance]], out of 256 a frame, "
        "as PlanetSide sets them from the world's report.",
    ])
    data["masks"] = {k: [m[:4] + [m[4]] for m in v] for k, v in hashes.items()}
    data["landings"] = landings
    data["chances"] = hazard_chances(lander_exe)
    return data


def record_lander_pickups(uqm, lander_exe, strings_exe, images_exe, package):
    """UQM's own lander picking up mineral nodes, with the package's masks
    of the lander and the nodes (the shapes; positions and pickups are
    code)."""
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, LANDER_SHEETS)
        landings = run_lander(lander_exe, pickup_scenarios(), masks)
    taken = sum(len(f[11]) for l in landings for f in l["frames"])
    if taken < 30:
        raise RuntimeError(f"only {taken} nodes taken in the pickup landings")
    data = header(uqm, ["tools/extract/golden/lander.c",
                        "tools/extract/golden/lander_stubs.c",
                        "src/uqm/planets/lander.c", "src/uqm/planets/scan.c",
                        "src/libs/graphics/intersec.c"], [
        "UQM 0.8.0's own lander picking up mineral nodes, recorded as "
        "lander.json is, with the nodes put on the surface as "
        "GeneratePlanetSide puts them and CheckObjectCollision's contacts "
        "found by UQM's own DrawablesIntersect with the masks of "
        "graphics.lander and graphics.miscdata from the official content "
        f"package (SHA-256 {CONTENT_SHA256}). Positions and pickups come "
        "from code and the masks' shapes; no content.",
        "Landings on the three worlds with the most mineral nodes and no "
        "energy or life (world-surfaces.json's nodes; named world-STAR-"
        "PLANET-MOON), landing on the first node; on nodes packed round the "
        "site, with and without the cargo upgrade (4) and firing, and with "
        "little storage room left; among nodes at each pole and either side "
        "of the east-west edge; and (seam-east, seam-west) with the lander "
        "coming down within a few units of the edge and nodes just across "
        "it, touched the nearer way round.",
        "input as in lander.json, with storage (the room left in the "
        "storage bays) and nodes ([scan (0), element, density, x, y] on the "
        "scan map, by node number). frames as in lander.json.",
    ])
    data["masks"] = {k: [m[:4] + [m[4]] for m in v] for k, v in hashes.items()}
    data["landings"] = landings
    return data


def record_math(uqm, exe):
    out = subprocess.run([exe], capture_output=True, text=True,
                         check=True).stdout
    data = header(uqm, ["tools/extract/golden/math.c"] +
                  [f"src/{f}" for f in MATH_LINK] + ["src/uqm/units.h"], [
        "UQM's fixed-point math, recorded from its own code: sinetab, "
        "ARCTAN [dx, dy, angle], square_root [value, root], universe/log "
        "conversions [value, x, y] and VELOCITY_DESC operations.",
        "velocity 'after' is [TravelAngle, vector.w, vector.h, fract.w, "
        "fract.h, error.w, error.h, incr.w, incr.h]; each step is "
        "[num_frames, dx, dy, error.w, error.h] from "
        "GetNextVelocityComponents.",
    ])
    data.update(json.loads(out))
    return data


def record_systems(uqm, exe):
    out = subprocess.run([exe], capture_output=True, text=True,
                         check=True).stdout
    link = systems_link(uqm)
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "tools/extract/golden/stubs.c",
                        "src/uqm/planets/solarsys.c"] +
                  [f"src/{f}" for f in link], [
        "Star systems as UQM 0.8.0 generates them, recorded by running its "
        "own LoadSolarSys, GenerateMoons, FillOrbits, DoPlanetaryAnalysis "
        "and every generator in planets/generate/ (see "
        "tools/extract/golden/systems.c). Game state is all zeros, as in a "
        "new game.",
        "rng: RandomContext (libs/math/random2.c). new_context_first is the "
        "first number from a new context. Each sequence is [seed given to "
        "SeedRandom, seed after it, then 50 numbers].",
        "systems: one per star in starmap order (NUM_SOLAR_SYSTEMS). index "
        "is the star's generator (STAR_DESC.Index), sun_seed the sun's "
        "rand_seed, sun_type its data_index (star size).",
        "planets: PlanetDesc in order, as LoadSolarSys leaves them. moons: "
        "for each planet, MoonDesc as GenerateMoons leaves them on entering "
        "that planet's inner system.",
        "Each world: [data_index, radius, location.x, location.y, "
        "rand_seed, NumPlanets] and, for worlds with planet data, then "
        "[orbit-colour temperature (no greenhouse bonus), temp_color index, "
        "AxialTilt, Tectonics, Weather, PlanetDensity, PlanetRadius, "
        "SurfaceGravity, SurfaceTemperature, RotationPeriod, AtmoDensity, "
        "LifeChance, PlanetToSunDist, RNG seed after the analysis]. The "
        "planet data is DoPlanetaryAnalysis into the solar system's own "
        "SYSTEM_INFO, as generateOrbital does on entering orbit. The "
        "starbase (255) and the Sa-Matra (254) have no planet data.",
    ])
    data.update(json.loads(out))
    return data


def record_surfaces(uqm, exe):
    out = subprocess.run([exe, "orbits"], capture_output=True, text=True,
                         check=True).stdout
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "tools/extract/golden/stubs.c",
                        "src/uqm/planets/solarsys.c",
                        "src/uqm/planets/surface.c",
                        "src/uqm/planets/scan.c"] +
                  [f"src/{f}" for f in systems_link(uqm)
                   if "generate/" in f], [
        "Entering orbit around every world, recorded by running each "
        "star's own generateOrbital (planets/generate/), then its "
        "generateMinerals, generateEnergy and generateLife for every node, "
        "as GeneratePlanetSurface in scan.c does. See "
        "tools/extract/golden/systems.c.",
        "Every world is entered from a new game's state (all game state "
        "zero). No alien ships are present (StartSphereTracking is "
        "false), so homeworld conversations don't start and the world "
        "is orbited as when its owners are gone. Nothing has been picked "
        "up.",
        "systems: per star in starmap order, per planet a list: the planet, "
        "then its moons in order. Each world: [data_index, entered] and, "
        "if orbit was entered: [scan seeds: mineral, energy, biological], "
        "[report: AxialTilt, Tectonics, Weather, PlanetDensity, "
        "PlanetRadius, SurfaceGravity, SurfaceTemperature, RotationPeriod, "
        "AtmoDensity, LifeChance, PlanetToSunDist] as generateOrbital "
        "leaves PLANET_INFO, and nodes: [minerals, energy, life], each a "
        "list of [x, y, type, density] (NODE_INFO), or null for a "
        "shielded world, which can't be scanned; then the orbit screen's "
        "scan text, as UQM's own PrintCoarseScanPC (scan.c) prints it: "
        "orbit, atmosphere, temperature, weather, tectonics, mass, radius, "
        "gravity, day, tilt. Labels and unit words come from the content's "
        "strings, which are empty here, so a value that is only a word "
        "(Vacuum, Super Thick, None) is empty, and unit words are missing "
        "(\"0.39\" is 0.39 a.u., \" 3\" is Class 3).",
        "Mineral type is the element (data/elements.json id); density is "
        "MAKE_WORD (gross size 0..2, quantity). Life type is the creature "
        "(data/lifeforms.json; 23 is an Evil One, 24 a Brainbox Bulldozer, "
        "25 Zex's Beauty).",
        "map_width and map_height: the surface map node positions are on "
        "(MAP_WIDTH, MAP_HEIGHT at 320x240).",
    ])
    data.update(json.loads(out))
    return data


def record_ip(uqm, exe):
    out = subprocess.run([exe, "ip"], capture_output=True, text=True,
                         check=True).stdout
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "src/uqm/planets/solarsys.c", "src/uqm/units.h",
                        "src/uqm/planets/planets.h"], [
        "Interplanetary screen geometry from UQM's own solarsys.c: the "
        "screen and zoom constants, where InitSolarSys puts a ship arriving "
        "from hyperspace (display origin and system location, at the "
        "widest zoom), and samples of displayToLocation and "
        "locationToDisplay as [radius, in x, in y, out x, out y].",
        "Screen is UQM's default 320x240.",
    ])
    data.update(json.loads(out))
    return data


def ip_flight_data(uqm, out):
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "tools/extract/golden/stubs.c",
                        "src/uqm/planets/solarsys.c", "src/uqm/ipdisp.c",
                        "src/uqm/velocity.c"], [
        "Interplanetary flight, frame by frame, recorded by running UQM's "
        "own ProcessShipControls, flagship_inertial_thrust, "
        "CheckShipLocation, CheckIntersect, FindRadius, enterInnerSystem, "
        "leaveInnerSystem, ValidateOrbits and flag_ship_preprocess, in the "
        "order IP_frame runs them (see tools/extract/golden/systems.c), "
        "flag_ship_preprocess on the frame the ship leaves the system too "
        "(IP_frame's RedrawQueue still runs) but not on its first frame after "
        "ResetSolarSys (spawn_flag_ship's element is APPEARING, and "
        "PreProcess doesn't preprocess it). "
        "After entering orbit, the ship returns to flight as "
        "EnterPlanetOrbit does (FreeSolarSys, LoadSolarSys, CheckZoomLevel, "
        "ResetSolarSys); the orbit itself is left out.",
        "Contact is UQM's own DrawablesIntersect (intersec.c, boxint.c) "
        "with the sprites' masks from the official content package "
        "(sprite-masks.json), so recording needs --content. The positions "
        "and events are code and shapes, not content.",
        "A test pilot chooses the input each frame (turn towards the goal, "
        "thrust when facing it); the engine's tests replay the recorded "
        "input. Input flags: 1 left, 2 right, 4 thrust.",
        "start: [facing, ip_location x, y, ShipStamp x, y, zoom radius] "
        "on arrival from hyperspace.",
        "frames: [input, facing, velocity dx, dy (GetCurrentVelocityComponents), "
        "ip_location x, y, ShipStamp x, y, zoom radius (SunDesc[0].radius), "
        "inner system planet or -1, WaitIntersect, event] after each frame. "
        "Events: 0 none, 1 zoom, 2 entered an inner system, 3 left it, "
        "4 entered orbit (and came back), 5 left the system.",
    ])
    data.update(json.loads(out))
    return data


def record_layout(uqm, exe):
    out = subprocess.run([exe, "layout"], capture_output=True, text=True,
                         check=True).stdout
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "src/uqm/planets/solarsys.c"], [
        "What UQM's DrawSystem draws in a system's views, for every star, "
        "from its own GetPlanetOrbitRect, ValidateOrbits, enterInnerSystem "
        "and ValidateInnerOrbits.",
        "outer: [zoom radius, planet, orbit rect x, y, width, height, "
        "image origin x, y, image size] for each zoom (8192 to 1024) and "
        "planet. Image size is the orbital image's size index, or -1 for "
        "the starbase and the Sa-Matra.",
        "inner: for each planet, its inner view: arc (the planet's own "
        "orbit, drawn through the middle) as [x, y, width, height], the "
        "planet's image size, and moons as [orbit rect x, y, width, "
        "height, image origin x, y, image size].",
    ])
    data.update(json.loads(out))
    return data


def record_sprites(uqm, exe):
    out = subprocess.run([exe, "sprites"], capture_output=True, text=True,
                         check=True).stdout
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "tools/extract/golden/stubs.c",
                        "src/uqm/planets/solarsys.c"], [
        "The sprites UQM's own DrawSystem stamps in a system's views, for "
        "every star, in the order it draws them: each stamp is [sheet, "
        "frame, x, y, colour table entry]. Sheets: 2 the worlds' images "
        "(graphics.planets), 3 the orbit background (graphics."
        "orbitbackground: 16 the starbase, 17 a shielded planet in its "
        "inner view, 19 the Sa-Matra), 5 the sun (graphics.truespacesun). "
        "The entry is the colour table entry set for the stamp "
        "(colortable.orbplan's for a world, colortable.truespace's for the "
        "sun), -1 for the orbit background.",
        "outer: the outer view at each zoom (8192 to 1024), after "
        "ValidateOrbits. inner: each planet's inner view, after "
        "enterInnerSystem and ValidateInnerOrbits.",
        "background: the SHA-256 of CreateStarBackGround's stamps (all from "
        "the orbit background), each as little-endian 16-bit frame, x, y.",
        "Code only: frames are numbers; nothing is read from the content.",
    ])
    data.update(json.loads(out))
    return data


def record_string_numbering(uqm, exe):
    out = subprocess.run([exe, "bases"], capture_output=True, text=True,
                         check=True).stdout
    data = header(uqm, ["tools/extract/golden/strings.c", "src/uqm/gamestr.h",
                        "src/uqm/menustat.h"], [
        "The game strings' numbering from UQM's gamestr.h: each range's "
        "base and count, as [name, base, count], and GAMESTR_COUNT.",
        "menu: menustat.h's numbers for the orbit screen's PC menus, whose "
        "entries are GAME_STRING (MAINMENU_STRING_BASE + n).",
    ])
    data.update(json.loads(out))
    return data


# String table inputs of our own (not UQM content), to record exactly what
# UQM's loader does with each quirk of the format.
STRING_TABLE_CASES = [
    ("simple", b"#(One)\nFirst\n\n#(Two)\nSecond\n"),
    ("text_before_first_header_is_ignored", b"stray\nlines\n#(A)\nkept\n"),
    ("lines_join_and_end_is_trimmed", b"#(A)\nline one\nline two\n\n\n#(B)\nb\n"),
    ("crlf", b"#(A)\r\nwindows\r\nlines\r\n\r\n#(B)\r\nb\r\n"),
    ("empty_strings", b"#(A)\n#(B)\n\n#(C)\nc"),
    ("hash_lines_without_a_name", b"#(A)\na\n#\n#()\nafter\n#(B)b\nb\n"),
    ("name_with_text_after", b"#(Vega) -- STAR_STRING_BASE\nV\n"),
    ("no_newline_at_end", b"#(A)\nlast line"),
    ("long_line_is_read_in_pieces", b"#(A)\n" + b"x" * 1500 + b"\n#(B)\n" + b"y" * 1022 + b"#(C)\nz\n"),
    ("nul_inside_a_line", b"#(A)\nbefore\x00after\nnext\n"),
    ("utf8", "#(A)\nCaf\u00e9 \u00b0 \u2014\n".encode("utf-8")),
    ("only_blank", b"#(A)\n\n\n"),
    ("too_many_strings", b"".join(b"#(s)\n%d\n" % i for i in range(2100))),
]


# Resource index inputs of our own, for UQM's property file rules.
RESOURCE_INDEX_CASES = [
    ("simple", b"a = BINTAB:base/a.ct\nb = SHIP:7\n"),
    ("comments_and_blank_lines", b"# comment\n\n   \nkey = STRTAB:x.txt # trailing\n#k = v\n"),
    ("spaces_and_crlf", b"  key   =   GFXRES:base/a.ani   \r\nk2=STRING:key Up\r\n"),
    ("no_value_and_bare_key", b"novalue\nhash#before = x\nlast.key"),
    ("duplicates", b"a = BINTAB:first\nb = SHIP:1\na = BINTAB:second\n"),
    ("unknown_types", b"a = BINTA:base/a.ct\nb = http://example.org/x\nc = no colon here\nd =\n"),
    ("empty_key", b"= orphan value\n"),
]


def record_resource_index_cases(uqm, exe):
    cases = []
    with tempfile.TemporaryDirectory() as tmp:
        for name, text in RESOURCE_INDEX_CASES:
            path = os.path.join(tmp, name + ".rmp")
            with open(path, "wb") as f:
                f.write(text)
            entries = read_resource_index(exe, path)
            cases.append({"name": name, "input": text.decode("utf-8"),
                          "entries": [[k, t, v] for k, (t, v) in entries.items()]})
    data = header(uqm, ["tools/extract/golden/strings.c",
                        "src/libs/resource/propfile.c",
                        "src/libs/resource/resinit.c"], [
        "Resource index inputs written for this test (RESOURCE_INDEX_CASES "
        "in tools/extract/golden_uqm.py), read by UQM's own "
        "PropFile_from_string; entries are [id, type, value] after "
        "process_resource_desc (a later id replaces an earlier one) and "
        "newResourceDesc's type rule.",
    ])
    data["cases"] = cases
    return data


def record_string_table_cases(uqm, exe):
    with tempfile.TemporaryDirectory() as tmp:
        files = []
        for i, (_, text) in enumerate(STRING_TABLE_CASES):
            local = os.path.join(tmp, f"{i}.txt")
            with open(local, "wb") as f:
                f.write(text)
            files.append(local)
        out = subprocess.run([exe, *files], capture_output=True, text=True,
                             check=True).stdout
    data = header(uqm, ["tools/extract/golden/strings.c",
                        "src/libs/strings/getstr.c"], [
        "String table inputs written for this test (STRING_TABLE_CASES in "
        "tools/extract/golden_uqm.py, not UQM content), as UQM's own "
        "_GetStringData reads them. input is the file in hex; strings are "
        "[length, bytes in hex], length counting UQM's terminating NUL.",
    ])
    data["cases"] = [{"name": name, "input": text.hex(), "strings": strings}
                     for (name, text), strings in zip(STRING_TABLE_CASES, json.loads(out))]
    return data


# Conversation inputs of our own, for each quirk of _GetConversationData.
CONVERSATION_CASES = [
    ("simple", b"#(GLAD) commander-000.ogg\nHurry back.\n\n#(next)\nline one\nline two\n"),
    ("tab_before_the_clip", b"#(A)\tpkunk-059.ogg\nsaid\n"),
    ("no_clip", b"#(A)\na\n#(B)  \nb\n"),
    ("text_before_first_header_is_ignored", b"stray\n#(A)\nkept\n"),
    ("empty_entries", b"#(A)\n#(B)\n\n#(C)\nc"),
    ("hash_lines_without_a_name", b"#(A)\na\n#\n#()\n#)(\nafter\n"),
    ("name_without_parentheses", b"#NAME\ntext\n#NAME2 clip.ogg\nmore\n"),
    ("clip_ends_at_a_parenthesis", b"#(A)x.ogg)rest\nt\n#((B)) y.ogg z.ogg\nu\n"),
    ("crlf", b"#(A) a.ogg\r\nwindows\r\nlines\r\n\r\n#(B)\r\nb\r\n"),
    ("long_line_is_read_in_pieces", b"#(A)\n" + b"x" * 1500 + b"\n#(B)\n" + b"y" * 1022 + b"#(C)\nz\n"),
    # A '#' that starts fgets' second 1,023-byte piece starts an entry.
    ("hash_at_a_piece_start", b"#(A)\n" + b"y" * 1023 + b"#(C)\nz\n"),
    ("long_header", b"#(" + b"N" * 1100 + b") clip.ogg\ntext\n"),
    ("nul_in_a_long_line", b"#(A)\n" + b"a" * 500 + b"\x00" + b"b" * 700 + b"\n#(B)\nb\n"),
    ("nul_inside_lines", b"#(A) a\x00b.ogg\nbefore\x00after\nnext\n"),
    ("utf8", "#(A)\nCaf\u00e9 \u2014 \u00b0\n".encode("utf-8")),
    ("duplicate_names", b"#(A)\nfirst\n#(A)\nsecond\n"),
    ("too_many_entries", b"".join(b"#(s%d)\n%d\n" % (i, i) for i in range(2100))),
]


def read_conversations(exe, files):
    out = subprocess.run([exe, "conversation", *files], capture_output=True,
                         text=True, check=True).stdout
    return json.loads(out)


def record_conversation_cases(uqm, exe):
    with tempfile.TemporaryDirectory() as tmp:
        files = []
        for i, (_, text) in enumerate(CONVERSATION_CASES):
            local = os.path.join(tmp, f"{i}.txt")
            with open(local, "wb") as f:
                f.write(text)
            files.append(local)
        tables = read_conversations(exe, files)
    data = header(uqm, ["tools/extract/golden/strings.c",
                        "src/libs/strings/getstr.c"], [
        "Conversation inputs written for this test (CONVERSATION_CASES in "
        "tools/extract/golden_uqm.py, not UQM content), as UQM's own "
        "_GetConversationData reads them. input is the file in hex; each "
        "entry is [name in hex, text length, text in hex, voice clip in hex "
        "or null], the text's length counting UQM's terminating NUL.",
    ])
    data["cases"] = [{"name": name, "input": text.hex(), "entries": entries}
                     for (name, text), entries in zip(CONVERSATION_CASES, tables)]
    return data


def record_conversations(uqm, exe, package):
    """Every CONVERSATION resource of the official content package, as
    UQM's own _GetConversationData reads it: names and clips in full
    (they're ids), texts as hashes (they're content)."""
    import hashlib
    import zipfile
    with open(package, "rb") as f:
        if hashlib.sha256(f.read()).hexdigest() != CONTENT_SHA256:
            sys.exit(f"{package} isn't the official UQM 0.8.0 content package")
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        entries = [(key, path.split(":")[0]) for key, (kind, path)
                   in read_resource_index(exe, index_file).items()
                   if kind == "CONVERSATION"]
        files = []
        for i, (key, path) in enumerate(entries):
            local = os.path.join(tmp, f"{i}.txt")
            with open(local, "wb") as f:
                f.write(z.read(path))
            files.append(local)
        tables = read_conversations(exe, files)
    out = {}
    for (key, path), table in zip(entries, tables):
        out[key] = {"path": path, "entries": [
            [bytes.fromhex(n).decode("utf-8"), length,
             hashlib.sha256(bytes.fromhex(t)).hexdigest(),
             None if c is None else bytes.fromhex(c).decode("utf-8")]
            for n, length, t, c in table]}
    data = header(uqm, ["tools/extract/golden/strings.c",
                        "src/libs/strings/getstr.c"], [
        "Every conversation (CONVERSATION resource) of the official UQM "
        f"0.8.0 content package (SHA-256 {CONTENT_SHA256}), as UQM's own "
        "_GetConversationData reads it. Each entry is [name, text length "
        "(counting UQM's terminating NUL; 0 for an empty entry), SHA-256 of "
        "the text's bytes, voice clip or null]. The texts are content (CC "
        "BY-NC-SA 2.5), so only their hashes are here; the names are the "
        "scripts' phrase ids.",
    ])
    data["conversations"] = out
    return data


def read_string_tables(exe, package, keep=lambda key: True):
    """The STRTAB resources of the official content package whose ids
    pass keep, as UQM's own _GetStringData reads them: {id: (path,
    [[length, hex], ...])}, as the strings program prints them."""
    import hashlib
    import zipfile
    with open(package, "rb") as f:
        digest = hashlib.sha256(f.read()).hexdigest()
    if digest != CONTENT_SHA256:
        sys.exit(f"{package} isn't the official UQM 0.8.0 content package")
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        entries = [(key, path) for key, (kind, path)
                   in read_resource_index(exe, index_file).items()
                   if kind == "STRTAB" and keep(key)]
        files = []
        for i, (key, path) in enumerate(entries):
            local = os.path.join(tmp, f"{i}.txt")
            with open(local, "wb") as f:
                f.write(z.read(path))
            files.append(local)
        out = subprocess.run([exe, *files], capture_output=True, text=True,
                             check=True).stdout
    return {key: (path, strings)
            for (key, path), strings in zip(entries, json.loads(out))}


def record_string_tables(uqm, exe, package):
    """Every STRTAB resource in the official content package, as UQM's own
    _GetStringData reads it. The strings are content, so only their
    lengths and SHA-256 hashes are kept."""
    import hashlib
    tables = {}
    for key, (path, strings) in read_string_tables(exe, package).items():
        tables[key] = {
            "path": path,
            "strings": [[length, hashlib.sha256(bytes.fromhex(data)).hexdigest()]
                        for length, data in strings],
        }
    data = header(uqm, ["tools/extract/golden/strings.c",
                        "src/libs/strings/getstr.c"], [
        "Every string table (STRTAB resource) of the official UQM 0.8.0 "
        f"content package (SHA-256 {CONTENT_SHA256}), as UQM's own "
        "_GetStringData reads it. The strings are content (CC BY-NC-SA "
        "2.5), so they aren't here: each string is [length, SHA-256], "
        "where length counts UQM's terminating NUL (0 for an empty "
        "string) and the hash is of those length bytes.",
    ])
    data["tables"] = tables
    return data


def record_names(uqm, systems_exe, strings_exe, package):
    """Every name UQM's own code gives to stars, planets and moons, with
    the game strings of the official content package. Names are content,
    so only their SHA-256 hashes are kept."""
    import hashlib
    _, strings = read_string_tables(
        strings_exe, package, lambda key: key == "text.starcon")["text.starcon"]
    with tempfile.TemporaryDirectory() as tmp:
        table = os.path.join(tmp, "gamestrings.hex")
        with open(table, "w") as f:
            for length, data in strings:
                f.write(data + "\n")
        out = subprocess.run([systems_exe, "names", table],
                             capture_output=True, text=True,
                             check=True).stdout

    def h(name):
        return hashlib.sha256(bytes.fromhex(name)).hexdigest()

    stars = []
    for star in json.loads(out):
        planets = []
        for p in star[1:]:
            planets.append({"name": h(p[0]), "title": h(p[1]),
                            "planet_or_moon": h(p[2]),
                            "moons": [[h(t), h(n)] for t, n in p[3:]]})
        stars.append({"cluster": h(star[0]), "planets": planets})
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "tools/extract/golden/strings.c",
                        "src/libs/strings/getstr.c",
                        "src/uqm/starmap.c", "src/uqm/planets/solarsys.c",
                        "src/uqm/planets/scan.c",
                        "src/uqm/planets/generate/gendefault.c",
                        "src/uqm/planets/generate/gensol.c"], [
        "Every name UQM 0.8.0's own code gives, with the game strings "
        "(text.starcon) of the official content package (SHA-256 "
        f"{CONTENT_SHA256}), read by UQM's _GetStringData (the strings "
        "program, tools/extract/golden/strings.c). Names are content "
        "(CC BY-NC-SA 2.5), so each is the SHA-256 of its UTF-8 bytes.",
        "stars: one per star in starmap order. cluster: GetClusterName. "
        "planets: in PlanetDesc order, each as seen from its own inner "
        "system (after GenerateMoons, which calls generateName): name is "
        "GLOBAL_SIS (PlanetName), title GetPlanetTitle (the orbit "
        "screen's title), planet_or_moon GetPlanetOrMoonName into "
        "SIS_NAME_SIZE bytes (as the save summary does). moons: in "
        "MoonDesc order, [GetPlanetTitle, GetPlanetOrMoonName] for each.",
    ])
    data["stars"] = stars
    return data


# Texts of our own laid out in the fonts the orbit screen uses, to record
# each quirk of TextRect and _text_blt: alignment, the last character's
# spacing, characters the font hasn't got, bad UTF-8 (which ends the text),
# and font_DrawText's limit of MAX_DELTAS (100) characters.
FONT_TEXT_FONTS = ["font.starcon", "font.tiny", "font.micro"]
FONT_TEXTS = [
    b"", b" ", b"A", b"Scan", b"Orbit: 1.00 a.u.", b"Temp: -18\xc2\xb0 c",
    b"Hello, world!", b"0123456789", b"MiXeD cAsE wItH sPaCeS",
    b"a\xe4\xf6\xfc", b"\xe4\xb8\x80 not in the font", b"ok\x80then",
    b"ok\xc3", b"ok\xc0\xafthen", b"ok\xe2\x82then",
    b"x" * 99, b"y" * 100, b"z" * 101, b"w" * 150,
]


def record_fonts(uqm, fonts_exe, strings_exe, package):
    """Every font of the official content package as UQM's _GetFontData
    loads it, and texts laid out by its TextRect and _text_blt: our own
    texts, and the game strings the orbit screen draws. Fonts and what is
    drawn with them are content, so only hashes are kept."""
    import hashlib
    import zipfile

    def h(value):
        return hashlib.sha256(json.dumps(value, separators=(",", ":"))
                              .encode()).hexdigest()

    _, strings = read_string_tables(
        strings_exe, package, lambda key: key == "text.starcon")["text.starcon"]
    game = [bytes.fromhex(data)[:-1] if length else None
            for length, data in strings]
    bases = json.loads(subprocess.run([strings_exe, "bases"],
                                      capture_output=True, text=True,
                                      check=True).stdout)
    base = {name: (b, n) for name, b, n in bases["ranges"]}
    game_cases = []
    for name in ("MAINMENU_STRING_BASE", "ORBITSCAN_STRING_BASE",
                 "SCAN_STRING_BASE"):
        b, n = base[name]
        game_cases += range(b, b + n)

    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        fonts = {key: path for key, (kind, path)
                 in read_resource_index(strings_exe, index_file).items()
                 if kind == "FONTRES"}
        for path in set(fonts.values()):
            for n in z.namelist():
                if n.startswith(path + "/") and not n.endswith("/"):
                    local = os.path.join(tmp, n)
                    os.makedirs(os.path.dirname(local), exist_ok=True)
                    with open(local, "wb") as f:
                        f.write(z.read(n))
        cases = []
        lines = []
        for path in sorted(set(fonts.values())):
            lines.append(f"font {path} {os.path.join(tmp, path)}")
        for key in FONT_TEXT_FONTS:
            for align in (0, 1, 2):
                for text in FONT_TEXTS:
                    cases.append({"font": key, "align": align,
                                  "text": text.hex()})
                    lines.append(f"text {fonts[key]} {align} "
                                 f"{text.hex() or '-'}")
                for n in game_cases:
                    if game[n] is None:
                        continue
                    cases.append({"font": key, "align": align,
                                  "game_string": n})
                    lines.append(f"text {fonts[key]} {align} "
                                 f"{game[n].hex() or '-'}")
        case_file = os.path.join(tmp, "cases.txt")
        with open(case_file, "w") as f:
            f.write("\n".join(lines) + "\n")
        out = subprocess.run([fonts_exe, case_file], capture_output=True,
                             text=True, check=True).stdout.splitlines()

    loaded = {}
    results = []
    for line in out:
        v = json.loads(line)
        if isinstance(v, dict):
            loaded[v["font"]] = {
                "metrics": h([v["leading"], v["leading_width"]]),
                "glyphs": {str(g[0]): h(g) for g in v["glyphs"]},
            }
        else:
            results.append(v)
    assert len(results) == len(cases)
    for case, result in zip(cases, results):
        case["result"] = h(result)
    data = header(uqm, ["tools/extract/golden/fonts.c",
                        "src/libs/graphics/gfxload.c",
                        "src/libs/graphics/font.c",
                        "src/libs/graphics/boxint.c",
                        "src/libs/strings/unicode.c"], [
        "Every font (FONTRES resource) of the official UQM 0.8.0 content "
        f"package (SHA-256 {CONTENT_SHA256}) as UQM's own _GetFontData "
        "loads it, with images read by libpng as SDL_image reads them, and "
        "texts laid out by UQM's TextRect and _text_blt (see "
        "tools/extract/golden/fonts.c). Fonts are content (CC BY-NC-SA "
        "2.5), so everything here is a SHA-256 of compact JSON.",
        "fonts: by folder. metrics: [Leading, LeadingWidth]. glyphs: by "
        "character number, [char, width, height, hot_x, hot_y, disp_width, "
        "disp_height, alpha as lowercase hex].",
        "resources: the FONTRES ids and the folder each names.",
        "texts: a text (our own, in hex, or a game string by number) laid "
        "out in a font with an alignment (0 left, 1 center, 2 right) at "
        "baseline 0, 0. result: [[TextRect's result (1 or 0), corner x, "
        "corner y, width, height, CharCount], then each character drawn, "
        "[char, pen x, pen y]].",
    ])
    data["resources"] = dict(sorted(fonts.items()))
    data["fonts"] = loaded
    data["texts"] = cases
    return data


def make_png(width, height, depth, color_type, pixels, palette=None,
             trns=None, interlace=False):
    """A PNG of our own: pixels is a list of rows of sample tuples."""
    import struct
    import zlib
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[color_type]

    def row_bytes(samples):
        if depth == 16:
            return b"".join(struct.pack(">H", v) for v in samples)
        if depth == 8:
            return bytes(samples)
        out, acc, bits = bytearray(), 0, 0
        for v in samples:
            acc = (acc << depth) | v
            bits += depth
            if bits == 8:
                out.append(acc)
                acc, bits = 0, 0
        if bits:
            out.append(acc << (8 - bits))
        return bytes(out)

    def image(rows):
        return b"".join(b"\0" + row_bytes([v for px in r for v in px])
                        for r in rows if r)

    if interlace:
        passes = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
                  (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]
        raw = b""
        for x0, y0, dx, dy in passes:
            rows = [[pixels[y][x] for x in range(x0, width, dx)]
                    for y in range(y0, height, dy)]
            if rows and rows[0]:
                raw += image(rows)
    else:
        raw = image(pixels)

    def chunk(kind, body):
        return (struct.pack(">I", len(body)) + kind + body
                + struct.pack(">I", zlib.crc32(kind + body) & 0xffffffff))

    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(
        ">IIBBBBB", width, height, depth, color_type, 0, 0,
        1 if interlace else 0))
    if palette is not None:
        data += chunk(b"PLTE", bytes(v for c in palette for v in c))
    if trns is not None:
        data += chunk(b"tRNS", trns)
    data += chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")
    assert channels
    return data


def image_cases():
    """Images of our own for each path through UQM's TFB_png_to_sdl."""
    import struct

    def grid(w, h, f):
        return [[f(x, y) for x in range(w)] for y in range(h)]

    pal4 = [(10, 20, 30), (40, 50, 60), (70, 80, 90), (200, 100, 0)]
    pal16 = [(i * 16, 255 - i * 16, i * 7) for i in range(16)]
    cases = []
    for depth in (1, 2, 4, 8, 16):
        top = (1 << depth) - 1
        cases.append((f"gray{depth}", make_png(
            5, 3, depth, 0, grid(5, 3, lambda x, y: ((x + y * 5) % (top + 1),)))))
    cases += [
        ("gray8_alpha", make_png(4, 2, 8, 4, grid(4, 2, lambda x, y: (x * 60, y * 200)))),
        ("gray16_alpha", make_png(3, 2, 16, 4, grid(3, 2, lambda x, y: (x * 9000 + 7, 65535 - y * 300)))),
        ("rgb8", make_png(3, 2, 8, 2, grid(3, 2, lambda x, y: (x * 80, y * 90, 7)))),
        ("rgb16", make_png(3, 2, 16, 2, grid(3, 2, lambda x, y: (x * 20000 + 255, y * 30000, 1234)))),
        ("rgb8_key", make_png(3, 2, 8, 2, grid(3, 2, lambda x, y: (x * 80, y * 90, 7)),
                              trns=struct.pack(">HHH", 80, 0, 7))),
        ("rgb16_key_truncated", make_png(2, 2, 16, 2, grid(2, 2, lambda x, y: (x * 300, y, 2)),
                                         trns=struct.pack(">HHH", 300, 0, 2))),
        ("rgba8", make_png(3, 2, 8, 6, grid(3, 2, lambda x, y: (x * 80, y * 90, 7, x * 100)))),
        ("rgba16", make_png(2, 2, 16, 6, grid(2, 2, lambda x, y: (x * 40000, y * 2000, 5, 60000)))),
        ("palette1", make_png(9, 2, 1, 3, grid(9, 2, lambda x, y: ((x + y) % 2,)), palette=pal4[:2])),
        ("palette2", make_png(5, 2, 2, 3, grid(5, 2, lambda x, y: ((x + y) % 4,)), palette=pal4)),
        ("palette4", make_png(3, 3, 4, 3, grid(3, 3, lambda x, y: ((x * 3 + y) % 16,)), palette=pal16)),
        ("palette8", make_png(4, 2, 8, 3, grid(4, 2, lambda x, y: ((x + y) % 4,)), palette=pal4)),
        ("palette_one_key", make_png(4, 2, 8, 3, grid(4, 2, lambda x, y: ((x + y) % 4,)),
                                     palette=pal4, trns=bytes([255, 255, 0]))),
        ("palette_all_opaque_trns", make_png(4, 2, 8, 3, grid(4, 2, lambda x, y: ((x + y) % 4,)),
                                             palette=pal4, trns=bytes([255, 255]))),
        ("palette_two_keys", make_png(4, 2, 8, 3, grid(4, 2, lambda x, y: ((x + y) % 4,)),
                                      palette=pal4, trns=bytes([0, 255, 0]))),
        ("palette_translucent", make_png(4, 2, 8, 3, grid(4, 2, lambda x, y: ((x + y) % 4,)),
                                         palette=pal4, trns=bytes([255, 128]))),
        ("palette4_translucent", make_png(3, 3, 4, 3, grid(3, 3, lambda x, y: ((x * 3 + y) % 16,)),
                                          palette=pal16, trns=bytes([0, 17, 255]))),
        ("gray8_key", make_png(3, 2, 8, 0, grid(3, 2, lambda x, y: (x * 40,)),
                               trns=struct.pack(">H", 40))),
        ("rgb8_interlaced", make_png(9, 9, 8, 2, grid(9, 9, lambda x, y: (x * 20, y * 20, x ^ y)),
                                     interlace=True)),
        ("palette2_interlaced", make_png(9, 9, 2, 3, grid(9, 9, lambda x, y: ((x + 2 * y) % 4,)),
                                         palette=pal4, interlace=True)),
        ("rgba8_interlaced_one_column", make_png(1, 9, 8, 6, grid(1, 9, lambda x, y: (y, y * 2, y * 3, 255 - y)),
                                                 interlace=True)),
        ("not_a_png", b"/* XPM */\nstatic char *x[] = {};\n"),
        ("cut_short", make_png(3, 2, 8, 2, grid(3, 2, lambda x, y: (x, y, 1)))[:40]),
    ]
    return cases


def record_images(uqm, exe, package):
    """Images as UQM's own TFB_png_to_sdl loads them: images of our own for
    every path through it (in full), and a sample of each kind of PNG in the
    official content package (as hashes, since they are content)."""
    import collections
    import hashlib
    import struct
    import zipfile

    def kind(data):
        if data[:8] != b"\x89PNG\r\n\x1a\n":
            return None
        pos, trns = 8, False
        while pos + 8 <= len(data):
            length, tag = struct.unpack(">I4s", data[pos:pos + 8])
            trns |= tag == b"tRNS"
            pos += 12 + length
        return f"type{data[25]}-depth{data[24]}-interlace{data[28]}" + \
            ("-trns" if trns else "")

    own = image_cases()
    sample = collections.defaultdict(list)
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        files = []
        for i, (name, data) in enumerate(own):
            path = os.path.join(tmp, f"own{i}.png")
            with open(path, "wb") as f:
                f.write(data)
            files.append(path)
        for n in sorted(z.namelist()):
            if not n.lower().endswith(".png"):
                continue
            data = z.read(n)
            k = kind(data)
            if k and len(sample[k]) < 40:
                path = os.path.join(tmp, f"c{len(files)}.png")
                with open(path, "wb") as f:
                    f.write(data)
                sample[k].append((n, len(files)))
                files.append(path)
        out = subprocess.run([exe, *files], capture_output=True, text=True,
                             check=True).stdout.splitlines()
    results = [json.loads(line) for line in out]
    assert len(results) == len(files)
    data = header(uqm, ["tools/extract/golden/images.c",
                        "tools/extract/golden/sdl.c",
                        "tools/extract/golden/sdl/SDL.h",
                        "src/libs/graphics/sdl/png2sdl.c"], [
        "Images as UQM 0.8.0's own PNG loader (TFB_png_to_sdl, over libpng "
        "and a stand-in for SDL 1.2) loads them. Each result is [0] if it "
        "doesn't load, or [1, bits per pixel, has alpha, width, height, "
        "colour key or -1, palette (256 RGB entries in hex, entries the "
        "PNG doesn't set are 0; \"\" without a palette), pixels (rows "
        "without padding, in hex)].",
        "own: images of our own for each path through the loader, given "
        "in full (png in hex) with their results.",
        "content: up to 40 of each kind of PNG (colour type, bit depth, "
        "interlace, transparency) in the official content package "
        f"(SHA-256 {CONTENT_SHA256}), by path, with the SHA-256 of the "
        "result as compact JSON, since images are content.",
    ])
    data["own"] = [{"name": name, "png": png.hex(), "result": results[i]}
                   for i, (name, png) in enumerate(own)]
    data["content"] = {
        k: [{"path": n, "result": hashlib.sha256(json.dumps(
            results[i], separators=(",", ":")).encode()).hexdigest()}
            for n, i in v]
        for k, v in sorted(sample.items())}
    return data


def record_comm_graphics(uqm, strings_exe, images_exe, package):
    """Every conversation's frames (the comm.*.graphics resources) as UQM
    loads them (_GetCelData: each .ani line names an image, its
    transparent colour, its colour map and hot spot; an image that doesn't
    load is dropped), each image read by UQM's own PNG loader and kept as
    the hash of its result, since images are content."""
    import hashlib
    import zipfile
    with open(package, "rb") as f:
        if hashlib.sha256(f.read()).hexdigest() != CONTENT_SHA256:
            sys.exit(f"{package} isn't the official UQM 0.8.0 content package")
    sheets = {}
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        keys = sorted(k for k, (kind, _) in index.items()
                      if kind == "GFXRES" and k.startswith("comm.") and k.endswith(".graphics"))
        for key in keys:
            ani_path = index[key][1]
            folder = ani_path.rsplit("/", 1)[0] + "/" if "/" in ani_path else ""
            lines, files = [], []
            for line in z.read(ani_path).decode().splitlines():
                fields = line.split()
                if not fields:
                    continue
                name, transparent, cmap, hx, hy = fields[0], *map(int, fields[1:5])
                try:
                    data = z.read(folder + name)
                except KeyError:
                    continue   # dropped, as _GetCelData drops it
                path = os.path.join(tmp, f"{len(files)}.png")
                with open(path, "wb") as f:
                    f.write(data)
                lines.append([folder + name, transparent, cmap, hx, hy])
                files.append(path)
            out = subprocess.run([images_exe, *files], capture_output=True,
                                 text=True, check=True).stdout.splitlines()
            frames = []
            for entry, result in zip(lines, out):
                result = json.loads(result)
                if result[0] == 0:
                    continue   # doesn't load: dropped
                frames.append(entry + [hashlib.sha256(json.dumps(
                    result, separators=(",", ":")).encode()).hexdigest()])
            sheets[key] = {"path": ani_path, "frames": frames}
    data = header(uqm, ["tools/extract/golden/images.c",
                        "src/libs/graphics/gfxload.c",
                        "src/libs/graphics/sdl/png2sdl.c"], [
        "Every conversation's frames (comm.*.graphics) in the official "
        f"content package (SHA-256 {CONTENT_SHA256}), as _GetCelData loads "
        "them, in order: [image path, transparent colour index, colour map "
        "index, hot spot x, hot spot y, SHA-256 of the image as UQM's own "
        "PNG loader gives it, as images.json's results are hashed]. Frames "
        "whose image doesn't load are left out, as UQM drops them.",
    ])
    data["sheets"] = sheets
    return data


def surface_lines(exe, tables_dir=None, full=()):
    """The systems program's surfaces mode, one parsed line at a time; the
    worlds in full also have their whole height map."""
    args = [exe, "surfaces"]
    for star, planet, moon in full:
        args += ["-f", f"{star}:{planet}:{moon}"]
    proc = subprocess.Popen(args + ([tables_dir] if tables_dir else []),
                            stdout=subprocess.PIPE, text=True)
    for line in proc.stdout:
        yield json.loads(line)
    if proc.wait():
        raise RuntimeError("the surfaces mode failed")


# Worlds whose whole height map is kept, one of each map algorithm: a
# cratered world, a topographic one and a gas giant (Sol's Mercury, Mars
# and Jupiter).
FULL_HEIGHT_MAPS = [(82, 0, -1), (82, 3, -1), (82, 4, -1)]

# Earth's surface definition (EARTH_MASK_ANIM), the one world whose surface
# is its own images.
EARTH_SURFACE = "graphics.earthmask"


def write_earth_frames(images_exe, z, ani_path, tables):
    """Earth's surface frames as UQM loads them (_GetCelData: each line of
    the .ani names an image, and one that doesn't load is dropped), each
    through UQM's own PNG loader, into tables as graphics.earthmask.N.pnm:
    RGB (P6) for a true colour image, palette indexes (P5) for a palette
    one."""
    folder = ani_path.rsplit("/", 1)[0] + "/" if "/" in ani_path else ""
    names = [line.split()[0] for line in z.read(ani_path).decode().splitlines()
             if line.split()]
    files = []
    for i, name in enumerate(names):
        path = os.path.join(tables, f"earth{i}.png")
        try:
            data = z.read(folder + name)
        except KeyError:
            continue
        with open(path, "wb") as f:
            f.write(data)
        files.append(path)
    out = subprocess.run([images_exe, *files], capture_output=True,
                         text=True, check=True).stdout.splitlines()
    n = 0
    for line in out:
        result = json.loads(line)
        if not result[0]:
            continue
        _, bpp, _, w, h, _, _, pixels = result
        pixels = bytes.fromhex(pixels)
        if bpp == 8:
            kind = b"P5"
        elif bpp == 24:
            kind = b"P6"
        else:
            sys.exit(f"{EARTH_SURFACE} frame {n} has {bpp} bits a pixel")
        with open(os.path.join(tables, f"{EARTH_SURFACE}.{n}.pnm"), "wb") as f:
            f.write(kind + f"\n{w} {h}\n255\n".encode() + pixels)
        n += 1


def planet_tables(strings_exe, images_exe, package, tmp):
    """The package's planet colour maps and translate tables (BINTAB), as
    files named by resource id in a new folder under tmp, with Earth's
    surface frames (write_earth_frames)."""
    import hashlib
    import zipfile
    with open(package, "rb") as f:
        if hashlib.sha256(f.read()).hexdigest() != CONTENT_SHA256:
            sys.exit(f"{package} isn't the official UQM 0.8.0 content package")
    tables = os.path.join(tmp, "tables")
    os.makedirs(tables)
    with zipfile.ZipFile(package) as z:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        for key, (kind, path) in index.items():
            if kind == "BINTAB" and key.startswith("planet."):
                with open(os.path.join(tables, key), "wb") as f:
                    f.write(z.read(path))
        write_earth_frames(images_exe, z, index[EARTH_SURFACE][1], tables)
    return tables


# The sprite sheets UQM tests for contact, by the systems program's sheet
# numbers (tools/extract/golden/systems.c).
CONTACT_SHEETS = [
    (1, "graphics.flagship"),                    # the flagship in a system
    (2, "graphics.planets"),                     # the worlds
    (3, "graphics.orbitbackground"),             # the starbase and the Sa-Matra
    (4, "graphics.stars"),                       # stars_in_space in a system
    (6, "ship.flagship.graphics.hyperspace"),    # the flagship in hyperspace
    (7, "graphics.hyperstars"),                  # the hyperspace stars
    (8, "graphics.ambient"),                     # stars_in_space in hyperspace
]


# The sheets the lander's collisions test (CheckObjectCollision): the
# lander and its shots (LanderFrame[0]) and the nodes (MiscDataFrame), as
# the lander program numbers them.
LANDER_SHEETS = [
    (0, "graphics.lander"),
    (8, "graphics.miscdata"),
    (9, "graphics.lifecan"),        # a canned creature (PlanetSideFrame[0])
] + [(10 + n, f"graphics.life.{n}") for n in range(26)]   # load_life_form

# And the hazards' sheets (LanderFrame[1] to [3]).
HAZARD_SHEETS = LANDER_SHEETS + [
    (1, "graphics.quake"),
    (2, "graphics.lightning"),
    (3, "graphics.lavaspot"),
]


# The ships ported so far (data/ships.json ids): stage 4a's.
BATTLE_SHIPS = ["sis_ship", "ilwrath"]
# Tanaka's (and Katana's) damaged scout: init_shofixti's sheets for an
# encounter's Shofixti without SHOFIXTI_RECRUITED (shofixti/resinst.h's
# OLDSHOF_*): its hull at three sizes and its captain's window, no special.
OLD_SCOUT = ["ship.shofixti.graphics.oldscout.large", "ship.shofixti.graphics.oldscout.medium",
             "ship.shofixti.graphics.oldscout.small"]
OLD_CAPTAIN = "ship.shofixti.graphics.oldcaptain"


def battle_sheets():
    """The sheets a battle tests for contact, the big size only (farray[0],
    InitIntersectFrame in collide.h): every ship's (the flagship's, the
    Ilwrath's and stage 4b's race ships') ship and weapon (and special, for
    a ship that has one) as their RACE_DESCs name them (data/ships.json),
    and its status panel's icons, whose frame 1 ModifySilhouette tests the
    VUX's limpets against (weapon.c), the arena's asteroids, blasts
    and explosions (InitSpace, init.c), stars_in_space, whose last frame
    is floating crew, lasers and ion trails (graphics.stars), and every
    planet the battle can have (load_gravity_well, cons_res.c: each
    planet type's and a shielded world's, data/planet-types.json); and
    Tanaka's damaged scout's hull (OLD_SCOUT). The sheet numbers only name
    the images program's files."""
    with open(os.path.join(REPO, "data", "ships.json")) as f:
        ships = {s["id"]: s for s in json.load(f)["ships"]}
    with open(os.path.join(REPO, "data", "planet-types.json")) as f:
        planets = json.load(f)
    ids = []
    for ship in BATTLE_SHIPS + RACE_SHIPS:
        g = ships[ship]["graphics"]
        ids += [g[k][0] for k in ("ship", "weapon", "special") if g[k][0]]
        ids.append(g["icons"])
    ids.append(OLD_SCOUT[0])
    ids += ["graphics.asteroid.large", "graphics.blast.large",
            "graphics.boom.large", "graphics.stars"]
    ids += [t["battle_sprites"][0] for t in planets["planet_types"]]
    ids.append(planets["shielded_battle_sprites"][0])
    return list(enumerate(ids))


def battle_resources():
    """Every graphics resource a battle draws (BattleResources.All): each
    fighting ship's sheets at its three sizes, captain's window and status
    icons as data/ships.json names them, InitSpace's stars, explosions,
    blasts and asteroids, the status panel's frames and every planet's
    battle sprites (data/planet-types.json); and Tanaka's damaged scout's
    hull and captain's window (OLD_SCOUT, OLD_CAPTAIN)."""
    ships, _ = battle_ships()
    with open(os.path.join(REPO, "data", "planet-types.json")) as f:
        planets = json.load(f)
    ids = []
    for ship in BATTLE_SHIPS + RACE_SHIPS:
        g = ships[ship]["graphics"]
        for k in ("ship", "weapon", "special"):
            ids += [r for r in g[k] if r]
        ids += [g["captain"], g["icons"]]
    ids += OLD_SCOUT + [OLD_CAPTAIN]
    ids += ["graphics.stars", "graphics.status"]
    for base in ["graphics.boom", "graphics.blast", "graphics.asteroid"]:
        ids += [f"{base}.large", f"{base}.medium", f"{base}.small"]
    for sprites in [t["battle_sprites"] for t in planets["planet_types"]] + [planets["shielded_battle_sprites"]]:
        ids += sprites
    return list(dict.fromkeys(ids))


def record_battle_masks(uqm, strings_exe, images_exe, package):
    """The collision masks of the battle's sheets (battle_sheets), and
    every battle resource's frame count (battle_resources)."""
    with tempfile.TemporaryDirectory() as tmp:
        _, hashes = write_masks(strings_exe, images_exe, package, tmp,
                                battle_sheets())
    with tempfile.TemporaryDirectory() as tmp:
        _, all_frames = write_masks(strings_exe, images_exe, package, tmp,
                                    list(enumerate(battle_resources())))
    data = header(uqm, ["tools/extract/golden/images.c",
                        "src/libs/graphics/gfxload.c",
                        "src/libs/graphics/sdl/canvas.c",
                        "src/libs/graphics/tfb_draw.c",
                        "src/libs/graphics/sdl/sdl_common.c",
                        "src/libs/graphics/sdl/sdl1_common.c",
                        "src/libs/graphics/sdl/sdl2_common.c",
                        "src/libs/graphics/sdl/pure.c",
                        "src/libs/graphics/sdl/sdl2_pure.c",
                        "src/libs/graphics/sdl/opengl.c",
                        "src/uqm/collide.h", "src/uqm/init.c",
                        "src/uqm/cons_res.c"], [
        "The collision masks of the sprite sheets a battle tests for "
        "contact, from the official content package (SHA-256 "
        f"{CONTENT_SHA256}), as sprite-masks.json records the others: "
        "each frame as _GetCelData loads it, read by UQM's own PNG loader, "
        "with its colour key set as process_image sets it, and a pixel "
        "able to touch as TFB_DrawCanvas_Intersect decides it; the "
        "planets, true colour without alpha, converted to the screen's 32 "
        "bits first, as TFB_DrawImage_New converts them (TFB_DisplayFormatAlpha "
        "to the 32-bit screens without alpha): the images program models that "
        "step, as its SDL stand-in has no conversion.",
        "Collisions test the big size only (InitIntersectFrame takes the "
        "frame from farray[0]), so only it is here: "
        f"{', '.join(BATTLE_SHIPS + RACE_SHIPS)}'s ship, weapon and special sheets as "
        "data/ships.json names them, and their status panel's icons (frame 1 "
        "is the ship's silhouette, which ModifySilhouette in weapon.c tests "
        "the VUX's limpets against: every frame is here), the asteroids, blasts and explosions "
        "(InitSpace), graphics.stars (floating crew, lasers and ion "
        "trails are its last frame) and every planet's battle sprite "
        "(load_gravity_well).",
        "By resource id, each frame is [width, height, hot spot x, hot "
        "spot y, SHA-256 of the mask: a byte a pixel, row by row, 1 where "
        "it can touch]. Content, so hashes only.",
        "frames: every graphics resource a battle draws (every fighting "
        "ship's ship, weapon and special sheets at their three sizes, its "
        "captain's window and status icons; the stars, the status panel, the "
        "explosions, blasts and asteroids at three sizes, every planet's "
        "battle sprites), its frame count as _GetCelData loads it (an image "
        "that doesn't load is dropped).",
    ])
    data["sheets"] = hashes
    data["frames"] = {key: len(frames) for key, frames in all_frames.items()}
    return data


def write_masks(strings_exe, images_exe, package, tmp, sheets=None):
    """Every frame of the contact sheets as UQM loads it (_GetCelData:
    each .ani line names an image, its transparent colour and hot spot,
    and an image that doesn't load is dropped), with its collision mask
    from the images program's mask mode, written into a new folder under
    tmp as SHEET.INDEX.mask for the systems program. Returns the folder
    and, by resource id, each frame's [width, height, hot spot x, y,
    SHA-256 of the mask (a byte a pixel, 1 where it can touch)]."""
    import hashlib
    import zipfile
    with open(package, "rb") as f:
        if hashlib.sha256(f.read()).hexdigest() != CONTENT_SHA256:
            sys.exit(f"{package} isn't the official UQM 0.8.0 content package")
    masks = os.path.join(tmp, "masks")
    os.makedirs(masks)
    hashes = {}
    with zipfile.ZipFile(package) as z:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        for sheet, key in sheets or CONTACT_SHEETS:
            ani_path = index[key][1]
            folder = ani_path.rsplit("/", 1)[0] + "/" if "/" in ani_path else ""
            frames, args = [], []
            for n, line in enumerate(z.read(ani_path).decode().splitlines()):
                fields = line.split()
                if not fields:
                    continue
                name, transparent, _, hx, hy = fields[0], *map(int, fields[1:5])
                try:
                    data = z.read(folder + name)
                except KeyError:
                    continue
                path = os.path.join(tmp, f"{sheet}_{n}.png")
                with open(path, "wb") as f:
                    f.write(data)
                frames.append((hx, hy))
                args += [path, str(transparent)]
            out = subprocess.run([images_exe, "mask", *args], capture_output=True,
                                 text=True, check=True).stdout.splitlines()
            entries = []
            for (hx, hy), line in zip(frames, out):
                result = json.loads(line)
                if result[0] == 0:
                    continue   # dropped, as _GetCelData drops it
                _, w, h, bits = result
                bits = bytes.fromhex(bits)
                digest = hashlib.sha256(bits).hexdigest()
                with open(os.path.join(masks, f"{sheet}.{len(entries)}.mask"), "w") as f:
                    f.write(f"{w} {h} {hx} {hy}\n")
                    for y in range(h):
                        f.write("".join(str(b) for b in bits[y * w:(y + 1) * w]) + "\n")
                entries.append([w, h, hx, hy, digest])
            hashes[key] = entries
    return masks, hashes



# ---- The battle engine (stage 4a item 2) ----

# UQM's battle code the battle program runs (tools/extract/golden/battle.c
# includes battle.c, galaxy.c and random.c).
BATTLE_LINK = ["uqm/process.c", "uqm/collide.c", "uqm/gravity.c",
               "uqm/misc.c", "uqm/weapon.c", "uqm/ship.c", "uqm/tactrans.c",
               "uqm/init.c", "uqm/status.c", "uqm/shipstat.c",
               "uqm/displist.c", "uqm/velocity.c", "uqm/trans.c",
               "uqm/globdata.c", "libs/math/sqrt.c",
               "libs/graphics/pixmap.c", "libs/graphics/intersec.c",
               "libs/graphics/boxint.c",
               "uqm/ships/sis_ship/sis_ship.c", "uqm/ships/ilwrath/ilwrath.c",
               "uqm/intel.c", "uqm/cyborg.c"]
# The race ships (stage 4b): every ship but the flagship, the Ilwrath, the
# Sa-Matra (lastbat, the endgame's) and the Ur-Quan drone (probe, which
# never fights), by data/ships.json's id, in the order of the plan's three
# parts (docs/plans/stage-4b.md, items 2, 3 and 4), each part recorded to
# its own file.
RACE_SHIP_PARTS = [
    ["arilou", "spathi", "supox", "druuge", "yehat", "zoqfot", "thradd", "umgah"],
    ["utwig", "mycon", "androsyn", "mmrnmhrm", "chenjesu", "slylandr", "urquan", "blackurq"],
    ["human", "chmmr", "shofixti", "syreen", "orz", "melnorme", "vux", "pkunk"],
]
RACE_SHIPS = [ship for part in RACE_SHIP_PARTS for ship in part]
BATTLE_LINK += [f"uqm/ships/{ship}/{ship}.c" for ship in sorted(RACE_SHIPS)]
BATTLE_STUBS = os.path.join(HERE, "golden", "battle_stubs.c")

# The planets the battles have (load_gravity_well's battle sprites).
BATTLE_PLANETS = ["planet.water", "planet.chondrite"]

# How many battles random_battle makes.
RANDOM_BATTLES = 8

# BATTLE_INPUT_STATE.
B_LEFT, B_RIGHT, B_THRUST, B_WEAPON, B_SPECIAL, B_ESCAPE = 1, 2, 4, 8, 16, 32
# The test ships' weapons (battle.c): a missile, a homing missile, a laser,
# six missiles round the ship.
W_MISSILE, W_HOMING, W_LASER, W_SPRAY = 1, 2, 4, 8

# The AI's ratings (intel.h), as the battle program numbers them from 1.
AI_RATINGS = [None, "standard", "good", "awesome"]

# An element as the battle program writes it.
# A frame as it writes it: its ships, elements and draws (and each side's
# input, which run() takes out into the battle's "inputs").
F_ZOOM, F_SHIPS, F_ELEMENTS, F_DRAWS, F_INPUTS = 3, 7, 8, 9, 10
E_FLAGS, E_LIFE, E_CREW, E_MASS = 0, 1, 2, 3
E_CX, E_CY, E_SHEET, E_FRAME, E_PLAYER, E_PRIM = 15, 16, 19, 20, 21, 22
E_BLACK, E_SLOT, E_TARGET, E_COLOR = 23, 24, 25, 26
PLAYER_SHIP_FLAG, FINITE_LIFE_FLAG, CREW_OBJECT_FLAG = 1 << 2, 1 << 10, 1 << 14
# A STARSHIP's cur_status_flags' LOW_ON_ENERGY (races.h).
LOW_ON_ENERGY_FLAG = 1 << 5


def battle_ships():
    with open(os.path.join(REPO, "data", "ships.json")) as f:
        data = json.load(f)
    return {s["id"]: s for s in data["ships"]}, data["flag_values"]


def battle_program_sheets():
    """The sheets the battle program loads, numbered: the flagship's and
    the Ilwrath's ship and weapon sheets (data/ships.json), the arena's
    asteroids, blasts and explosions (InitSpace) and the battles' planets,
    each at its three sizes, and the stars (one size)."""
    ships, _ = battle_ships()
    ids = []
    for ship in BATTLE_SHIPS:
        g = ships[ship]["graphics"]
        ids += g["ship"] + g["weapon"]
    for base in ["graphics.asteroid", "graphics.blast", "graphics.boom"] + BATTLE_PLANETS:
        ids += [f"{base}.large", f"{base}.medium", f"{base}.small"]
    ids.append("graphics.stars")
    return ids


def test_ship(ship_id, crew=None, weapons=W_MISSILE, **changes):
    """A test ship flying ship_id's sheets with its RACE_DESC's numbers
    (data/ships.json) and plain weapons: the flagship's blaster's numbers
    and laser range, or the Ilwrath's hellfire's. changes overrides any
    field (a characteristic by its name)."""
    ships, flag_values = battle_ships()
    s = ships[ship_id]
    d = s["defines"]
    if ship_id == "sis_ship":
        weapon = dict(life=d["BLASTER_LIFE"], speed=d["BLASTER_SPEED"], hits=d["BLASTER_HITS"],
                      damage=d["BLASTER_DAMAGE"], offset=d["BLASTER_OFFSET"], laser_range=d["LASER_RANGE"])
    else:
        weapon = dict(life=d["MISSILE_LIFE"], speed=d["MISSILE_SPEED"], hits=d["MISSILE_HITS"],
                      damage=d["MISSILE_DAMAGE"], offset=d["ILWRATH_OFFSET"], laser_range=0)
    ship = dict(ship=ship_id, crew=s["crew"] if crew is None else crew, max_crew=s["max_crew"],
                energy=s["energy"], max_energy=s["max_energy"],
                flags=sum(flag_values[f] for f in s["flags"]),
                characteristics=dict(s["characteristics"]), weapons=weapons, track_wait=2, **weapon)
    for key, value in changes.items():
        if key in ship["characteristics"]:
            ship["characteristics"][key] = value
        else:
            ship[key] = value
    return ship


CHARACTERISTICS = ["max_thrust", "thrust_increment", "energy_regeneration", "weapon_energy_cost",
                   "special_energy_cost", "energy_wait", "turn_wait", "thrust_wait", "weapon_wait",
                   "special_wait", "ship_mass"]


def battle_line(scenario, numbers):
    ships, _ = battle_ships()
    parts = [scenario["name"], scenario["seed"], 1 if scenario.get("zoom", "smooth") == "smooth" else 0,
             1 if scenario.get("runaway") else 0]
    parts += [numbers[f"{scenario['planet']}.{s}"] for s in ("large", "medium", "small")]
    for ship in scenario["ships"]:
        kind = ship.get("kind", "test")
        if kind == "sis":
            parts += [1, ship["crew"], ship.get("bomb", 0)] + ship["drives"] + ship["jets"] + ship["modules"]
            continue
        if kind == "ilwrath":
            parts += [2, ship["crew"]]
            continue
        if kind == "race":
            parts += [3, ship["ship"], ship["crew"]]
            continue
        parts.append(0)
        g = ships[ship["ship"]]["graphics"]
        parts += [numbers[r] for r in g["ship"] + g["weapon"]]
        parts += [ship["crew"], ship["max_crew"], ship["energy"], ship["max_energy"], ship["flags"]]
        parts += [ship["characteristics"][c] for c in CHARACTERISTICS]
        parts += [ship[k] for k in ("weapons", "life", "speed", "hits", "damage", "offset",
                                    "track_wait", "laser_range")]
        # Its cyborg_control, for the AI to fly it (ship_intelligence).
        parts += [ship.get("weapon_range", 0), ship.get("maneuverability", 0)]
    parts += scenario.get("fleet", [1, 1])
    parts.append(len(scenario["steps"]))
    for step in scenario["steps"]:
        parts += list(step)
    parts.append(len(scenario.get("events", [])))
    for event in scenario.get("events", []):
        parts += list(event)
    # Who flies each side (the keys, or the AI at a rating), and the Earth
    # fight's PROBE_ILWRATH_ENCOUNTER.
    parts += [AI_RATINGS.index(c) if c else 0 for c in scenario.get("ai", [None, None])]
    parts.append(1 if scenario.get("earth") else 0)
    # SHOFIXTI_RECRUITED (stage 4b's Shofixti: without it, Tanaka's scout).
    parts.append(1 if scenario.get("shofixti_recruited") else 0)
    # Keys taken from each side's input, [mask, unless] (battle.c's
    # side_input; only the race battles' checks set it).
    for mask, unless in scenario.get("mask", [[0, 0], [0, 0]]):
        parts += [mask, unless]
    return " ".join(str(p) for p in parts)


class BattleProgram:
    """The battle program with its sheets and masks, run on scenarios."""

    def __init__(self, exe, sheets_file, masks, ids=None, sounds=None):
        self.exe, self.sheets_file, self.masks = exe, sheets_file, masks
        self.ids = ids or battle_program_sheets()
        # The Pkunk's sounds' count, for pkunk_postprocess (battle.c).
        self.args = [str(sounds)] if sounds else []
        self.numbers = {r: n for n, r in enumerate(self.ids)}
        out = subprocess.run(["nm", "-n", exe], capture_output=True, text=True, check=True).stdout
        self.symbols = [(int(a, 16), name) for a, kind, name in
                        (line.split() for line in out.splitlines() if len(line.split()) == 3)
                        if kind in "tT"]

    def place(self, address):
        """The function an address is in (the program has its symbols)."""
        import bisect
        i = bisect.bisect_right([a for a, _ in self.symbols], address) - 1
        return self.symbols[i][1]

    def run(self, scenarios, colours=False):
        """The battles, their elements without their colour (item 2's
        traces don't keep it) unless colours."""
        lines = [battle_line(s, self.numbers) for s in scenarios]
        res = subprocess.run([self.exe, self.sheets_file, self.masks, *self.args], input="\n".join(lines) + "\n",
                             capture_output=True, text=True)
        if res.returncode < 0:
            # UQM's own code crashing. The cause seen was AbandonShip events
            # towards a ship without crew, which the program now refuses.
            raise RuntimeError(" ".join(filter(None, [
                f"the battle program crashed (signal {-res.returncode}); possibly an AbandonShip event "
                "UQM never makes.", res.stderr.strip()])))
        if res.returncode:
            raise RuntimeError(f"the battle program refused a battle: {res.stderr.strip()}")
        out = res.stdout
        battles = json.loads("[" + out.replace("}\n{", "},\n{") + "]")
        if len(battles) != len(scenarios):
            raise RuntimeError("the battle program lost a battle")
        places = {}
        for battle in battles:
            battle["inputs"] = [frame.pop(F_INPUTS) for frame in battle["frames"]]
            for frame in battle["frames"]:
                frame[9] = [places.setdefault(a, self.place(a)) for a in frame[9]]
                if not colours:
                    for e in frame[F_ELEMENTS]:
                        del e[E_COLOR]
        return battles


def facing_towards(dx, dy):
    """UQM's facing (0 up, clockwise, 16 a turn) nearest the way (dx, dy)."""
    import math
    return round(math.atan2(dx, -dy) / (2 * math.pi / 16)) % 16


def wrap_delta(d, size):
    return (d + size // 2) % size - size // 2


def fighting(frame, side):
    """The side's ship in the battle (its element still there), as the
    battle program writes it, or None."""
    return next((s for s in frame[F_SHIPS][side] if s and s[7]), None)


def ship_of(frame, side):
    return next((e for e in frame[F_ELEMENTS] if e[E_FLAGS] & PLAYER_SHIP_FLAG and e[E_PLAYER] == side), None)


def steer(frame, side, goal, fire=False, keep_off=0):
    """Keys turning side's ship towards goal(frame) (a place, or None to
    drift), thrusting when it faces it (and firing, if asked) unless
    within keep_off world units."""
    ship = ship_of(frame, side)
    target = goal(frame) if ship else None
    if ship is None or target is None or fighting(frame, side) is None:
        return 0
    dx = wrap_delta(target[0] - ship[E_CX], 8192)
    dy = wrap_delta(target[1] - ship[E_CY], 7680)
    diff = (facing_towards(dx, dy) - fighting(frame, side)[2]) % 16
    keys = 0 if diff == 0 else B_RIGHT if diff < 8 else B_LEFT
    if diff in (0, 1, 15):
        if abs(dx) + abs(dy) > keep_off:
            keys |= B_THRUST
        if fire:
            keys |= B_WEAPON
    return keys


def the_planet(frame):
    p = next((e for e in frame[F_ELEMENTS] if e[E_MASS] == 200), None)
    return (p[E_CX], p[E_CY]) if p else None


def the_ship(side):
    def goal(frame):
        s = ship_of(frame, side)
        return (s[E_CX], s[E_CY]) if s else None
    return goal


def nearest_asteroid(side):
    def goal(frame):
        ship = ship_of(frame, side)
        rocks = [e for e in frame[F_ELEMENTS] if e[E_MASS] == 3 and not e[E_FLAGS] & FINITE_LIFE_FLAG
                 and e[E_PRIM] == 1]
        if not ship or not rocks:
            return None
        rock = min(rocks, key=lambda e: abs(wrap_delta(e[E_CX] - ship[E_CX], 8192))
                   + abs(wrap_delta(e[E_CY] - ship[E_CY], 7680)))
        return (rock[E_CX], rock[E_CY])
    return goal


def nearest_crew(side):
    def goal(frame):
        ship = ship_of(frame, side)
        crew = [e for e in frame[F_ELEMENTS] if e[E_FLAGS] & CREW_OBJECT_FLAG]
        if not ship or not crew:
            return None
        c = min(crew, key=lambda e: abs(wrap_delta(e[E_CX] - ship[E_CX], 8192))
                + abs(wrap_delta(e[E_CY] - ship[E_CY], 7680)))
        return (c[E_CX], c[E_CY])
    return goal


def piloted(program, scenario, frames, pilots, chunk=4):
    """The scenario's steps made by flying: every chunk frames, each side's
    pilot (frame, side, frames run so far -> keys; None for no keys) picks
    its keys from the battle as it is (the battle run again from the start
    each time)."""
    scenario = dict(scenario, steps=list(scenario.get("steps", [])))
    while sum(s[0] for s in scenario["steps"]) < frames:
        battle = program.run([scenario])[0]
        if battle["ended"]:
            break
        last = battle["frames"][-1]
        t = len(battle["frames"]) - 1
        keys = [pilot(last, side, t) if pilot else 0 for side, pilot in enumerate(pilots)]
        n = min(chunk, frames - sum(s[0] for s in scenario["steps"]))
        steps = scenario["steps"]
        if steps and list(steps[-1][1:]) == keys:
            steps[-1] = (steps[-1][0] + n, *keys)
        else:
            steps.append((n, *keys))
    return scenario


def random_steps(seed, frames, sides=2, choices=None, longest=30):
    """Seeded random keys for each side: steps of 1 to longest frames."""
    import random
    rng = random.Random(seed)
    steps, total = [], 0
    choices = choices or [0, B_THRUST, B_THRUST, B_LEFT, B_RIGHT, B_THRUST | B_LEFT, B_THRUST | B_RIGHT,
                          B_WEAPON, B_WEAPON | B_THRUST, B_WEAPON | B_LEFT, B_WEAPON | B_RIGHT | B_THRUST,
                          B_LEFT | B_RIGHT, B_SPECIAL | B_THRUST]
    while total < frames:
        n = min(rng.randint(1, longest), frames - total)
        steps.append((n, *[rng.choice(choices) for _ in range(sides)]))
        total += n
    return steps


def random_battle(name, rng):
    """A battle with everything at random, from rng: each side's ship
    (either sheet, any weapons, sometimes the Skiff's thrust (its
    increment its most), a light or nimble hull, free shots, short or long
    lives, crew above its most, crew-immune), fleets of one to three,
    either zoom, retreat allowed or not, keys at random (Escape among
    them) and AbandonShip at random frames."""
    ships = []
    for side in range(2):
        t = test_ship(rng.choice(["sis_ship", "ilwrath"]), crew=rng.choice([None, 3, 5, 12, 50]),
                      weapons=rng.choice([0, W_MISSILE, W_HOMING, W_MISSILE | W_HOMING, W_LASER,
                                          W_MISSILE | W_LASER, W_HOMING | W_LASER, W_SPRAY]))
        c = t["characteristics"]
        if rng.random() < 0.3:
            c["thrust_increment"] = c["max_thrust"]   # the Skiff's inertialess thrust
        if rng.random() < 0.3:
            c["ship_mass"] = rng.choice([1, 2, 6, 10])
        if rng.random() < 0.3:
            c["turn_wait"] = 0
        if rng.random() < 0.3:
            c["weapon_energy_cost"] = 0
        if rng.random() < 0.2:
            t["flags"] |= 64   # CREW_IMMUNE
        if rng.random() < 0.3:
            t["life"] = rng.choice([2, 40, 90])
        ships.append(t)
    frames = rng.choice([1200, 2000])
    keys = [0, B_THRUST, B_THRUST, B_LEFT, B_RIGHT, B_THRUST | B_LEFT, B_THRUST | B_RIGHT, B_WEAPON,
            B_WEAPON | B_THRUST, B_WEAPON | B_LEFT, B_WEAPON | B_RIGHT | B_THRUST, B_LEFT | B_RIGHT,
            B_SPECIAL | B_THRUST, B_ESCAPE, B_ESCAPE | B_THRUST]
    steps = random_steps(rng.randrange(1 << 30), frames, choices=keys, longest=rng.choice([5, 15, 30]))
    events = sorted((rng.randrange(60, frames), rng.randrange(2), rng.randrange(2), rng.randrange(1, 12))
                    for _ in range(rng.choice([1, 3, 5])))
    return dict(name=name, seed=rng.randrange(1 << 31), planet=rng.choice(BATTLE_PLANETS),
                zoom=rng.choice(["smooth", "smooth", "step"]), runaway=rng.random() < 0.4,
                fleet=[rng.choice([1, 2, 3]), rng.choice([1, 2, 3])],
                ships=ships, steps=steps, events=events, full=60)


def clamp_events(program, scenario):
    """The scenario's AbandonShip events holding the ship to one crew at
    least, as the Syreen's song (UQM's only caller) does: each event's crew
    cut to the ship's crew then, less one, and dropped if none can go, or
    if either ship's element has no crew (dead, or warped out: the
    battle program refuses those; towards one, the crew after the first
    belong to that ship, whose RACE_DESC new_ship frees, and UQM's
    crew_preprocess can crash reading it)."""
    events = []
    for frame, side, target, crew in scenario["events"]:
        steps, left = [], frame
        for step in scenario["steps"]:
            if left <= 0:
                break
            steps.append((min(step[0], left), *step[1:]))
            left -= step[0]
        battle = program.run([dict(scenario, steps=steps, events=events)])[0]
        if len(battle["frames"]) <= frame:
            continue   # the battle was over by then
        ship = fighting(battle["frames"][frame], side)
        # Events accepted at this frame haven't run in the battle above.
        left = ship[0] - sum(e[3] for e in events if e[0] == frame and e[1] == side) if ship else 0
        alive = [e and e[E_CREW] for e in (ship_of(battle["frames"][frame], side),
                                           ship_of(battle["frames"][frame], target))]
        if ship and fighting(battle["frames"][frame], target) and left > 1 and all(alive):
            events.append((frame, side, target, min(crew, left - 1)))
    return dict(scenario, events=events)


def battle_scenarios(program):
    """The traces: each a battle UQM runs from a seed with scripted keys
    (some flown by a pilot steering at a goal; the keys are recorded)."""
    flagship, ilwrath = "sis_ship", "ilwrath"
    out = []
    # Warping in and drifting: the arena, the warp-in and the camera.
    # (Seed 522 puts the planet on an asteroid first: TimeSpaceMatterConflict.)
    out.append(dict(name="warp-in", seed=522, planet="planet.water",
                    ships=[test_ship(flagship), test_ship(ilwrath)], steps=[(80, 0, 0)], full=80))
    # Flying into the planet until the ship dies of it.
    s = dict(name="planet-crash", seed=23, planet="planet.chondrite",
             ships=[test_ship(flagship, crew=12), test_ship(ilwrath)], steps=[(20, 0, 0)], full=200)
    out.append(piloted(program, s, 700, [lambda f, side, t: steer(f, side, the_planet), None]))
    # Ramming the other ship again and again.
    s = dict(name="ramming", seed=37, planet="planet.water",
             ships=[test_ship(ilwrath), test_ship(flagship)], steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 500, [lambda f, side, t: steer(f, side, the_ship(1)),
                                         lambda f, side, t: B_LEFT if (t >> 3) % 3 == 0 else 0]))
    # Shooting each other until one dies: missiles, the blaster's own blast,
    # the laser; the explosion, and the battle's end.
    s = dict(name="shooting", seed=41, planet="planet.water",
             ships=[test_ship(flagship, weapons=W_MISSILE | W_LASER), test_ship(ilwrath, crew=10)],
             steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 900, [lambda f, side, t: steer(f, side, the_ship(1), fire=True, keep_off=900),
                                         lambda f, side, t: steer(f, side, the_ship(0), fire=True, keep_off=600)]))
    # Homing missiles after a ship flying at random.
    s = dict(name="homing", seed=53, planet="planet.chondrite",
             ships=[test_ship(flagship, weapons=W_HOMING, weapon_wait=10), test_ship(ilwrath, crew=22)],
             steps=[(20, 0, 0)], full=120)
    rnd = random_steps(530, 600, sides=1)
    flat = [k for n, k in rnd for _ in range(n)]
    out.append(piloted(program, s, 600, [lambda f, side, t: steer(f, side, the_ship(1), fire=True, keep_off=2000),
                                         lambda f, side, t: flat[min(len(flat) - 1, t)] & ~B_WEAPON]))
    # Shooting asteroids: they break up and come back on the seam.
    s = dict(name="asteroids", seed=67, planet="planet.water",
             ships=[test_ship(ilwrath, weapon_energy_cost=0), test_ship(flagship)], steps=[(20, 0, 0)], full=120)
    out.append(piloted(program, s, 900, [lambda f, side, t: steer(f, side, nearest_asteroid(0), fire=True, keep_off=500),
                                         None]))
    # The list full: six long-lived missiles a frame from each ship.
    out.append(dict(name="list-full", seed=71, planet="planet.chondrite",
                    ships=[test_ship(ilwrath, weapons=W_SPRAY, life=60, weapon_energy_cost=0),
                           test_ship(ilwrath, weapons=W_SPRAY, life=90, weapon_energy_cost=0,
                                     crew=22)],
                    steps=[(20, 0, 0), (120, B_WEAPON, B_WEAPON | B_LEFT), (60, B_THRUST, 0),
                           (100, B_WEAPON | B_THRUST, B_WEAPON | B_RIGHT | B_THRUST)], full=40))
    # Crew floating: AbandonShip called directly, the ships near each
    # other, moving and still; the crew drift to their ship (or, once
    # theirs has none, the nearest) and are picked up.
    s = dict(name="crew-floating", seed=83, planet="planet.water",
             ships=[test_ship(ilwrath, crew=12), test_ship(flagship, crew=30)],
             steps=[(20, 0, 0)], events=[(200, 1, 0, 8), (260, 0, 1, 3), (300, 1, 0, 30)], full=120)
    out.append(piloted(program, s, 560, [
        lambda f, side, t: steer(f, side, the_ship(1), keep_off=300) if t < 200
        else steer(f, side, lambda fr: nearest_crew(0)(fr) or the_ship(1)(fr)),
        lambda f, side, t: B_LEFT if t > 400 else 0]))
    # Crew floating towards a ship that dies: they turn to their own ship
    # or the nearest (crew_preprocess). The dying ship is crew-immune: its
    # AbandonShip does nothing. The first
    # crew leave a ship still since it warped in (its velocity zeroed:
    # TravelAngle 0, so they go as from a ship moving straight up).
    s = dict(name="crew-orphans", seed=131, planet="planet.chondrite",
             ships=[test_ship(ilwrath, crew=4, flags=64), test_ship(flagship, crew=40, weapons=W_MISSILE | W_HOMING)],
             steps=[(20, 0, 0)], events=[(20, 1, 0, 6), (24, 0, 1, 2), (90, 1, 0, 5)], full=200)
    out.append(piloted(program, s, 400, [lambda f, side, t: steer(f, side, the_ship(1), keep_off=500),
                                         lambda f, side, t: steer(f, side, the_ship(0), fire=True, keep_off=800)]))
    # Scraping: ships turning while they touch each other or the planet,
    # so a turn alone makes them overlap (ProcessCollisions' time 1).
    def scrape(goal):
        def pilot(f, side, t):
            ship, target = ship_of(f, side), goal(f)
            if ship and target and abs(wrap_delta(target[0] - ship[E_CX], 8192)) \
                    + abs(wrap_delta(target[1] - ship[E_CY], 7680)) < 700:
                return (B_LEFT if (t >> 2) % 2 else B_RIGHT) | B_THRUST
            return steer(f, side, goal)
        return pilot
    s = dict(name="scrape", seed=149, planet="planet.water",
             ships=[test_ship(ilwrath), test_ship(flagship)], steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 700, [lambda f, side, t: scrape(the_ship(1) if t < 400 else the_planet)(f, side, t),
                                         lambda f, side, t: B_LEFT if t % 40 < 20 else B_RIGHT]))
    # Retreating: the ship flashes and warps out; the battle ends.
    out.append(dict(name="retreat", seed=97, planet="planet.water", runaway=True,
                    ships=[test_ship(flagship), test_ship(ilwrath)],
                    steps=[(10, B_ESCAPE, 0), (30, B_THRUST, B_THRUST), (2, B_ESCAPE, 0), (400, 0, B_WEAPON)],
                    full=100))
    # Fleets, three ships against two, shooting: two ships die together
    # (the second while the first explodes; the winner picks last), and
    # each side's next ship warps in in the dead one's element
    # (GetNextStarShip), until a side is out of ships.
    s = dict(name="fleets", seed=301, planet="planet.water", fleet=[3, 2],
             ships=[test_ship(ilwrath, crew=3, weapons=W_MISSILE | W_HOMING),
                    test_ship(ilwrath, crew=3, weapons=W_MISSILE | W_HOMING)],
             steps=[(20, 0, 0)], full=480)
    out.append(piloted(program, s, 900, [lambda f, side, t: steer(f, side, the_ship(1), fire=True, keep_off=300),
                                         lambda f, side, t: steer(f, side, the_ship(0), fire=True, keep_off=300)],
                       chunk=6))
    # Jostling: two quick-turning ships at random, without weapons, which
    # turn into each other and the asteroids (ProcessCollisions at time 1:
    # the turn taken back); seed 13 spawns two asteroids on each other,
    # which both break up at once.
    out.append(dict(name="jostle", seed=13, planet="planet.water",
                    ships=[test_ship(ilwrath, weapons=0), test_ship(ilwrath, weapons=0)],
                    steps=random_steps(1300, 2500, choices=list(range(8)), longest=7), full=60))
    # The winner dying after the loser's explosion, while its element waits
    # to pick the next ship: no other ship's element is left
    # (FindAliveStarShip finds none). Side 1 shoots side 0, then flies
    # into the planet with its one crew.
    def shoot_then_crash(f, side, t):
        other = fighting(f, 0)
        if other and other[0] > 0:
            return steer(f, side, the_ship(0), fire=True, keep_off=600)
        return steer(f, side, the_planet)
    s = dict(name="lonely-death", seed=500, planet="planet.water",
             ships=[test_ship(ilwrath, crew=1), test_ship(ilwrath, crew=1)], steps=[(20, 0, 0)], full=360)
    out.append(piloted(program, s, 600, [None, shoot_then_crash]))
    # Battles with everything at random (random_battle): among others they
    # reach the Skiff's thrust, crew held to its most, the camera's scroll
    # clamped both ways and AbandonShip from a ship moving downwards
    # (check_battles makes sure).
    rng = __import__("random").Random(4242)
    out += [clamp_events(program, random_battle(f"random-{i}", rng)) for i in range(RANDOM_BATTLES)]
    # The step zoom (TFB_SCALE_STEP) and long battles at random.
    out.append(dict(name="step-zoom", seed=101, planet="planet.chondrite", zoom="step",
                    ships=[test_ship(flagship, weapons=W_MISSILE | W_HOMING), test_ship(ilwrath)],
                    steps=random_steps(1010, 1200), full=60))
    out.append(dict(name="long-1", seed=1234567, planet="planet.water",
                    ships=[test_ship(flagship, weapons=W_MISSILE | W_LASER), test_ship(ilwrath)],
                    steps=random_steps(1111, 2000), full=60))
    out.append(dict(name="long-2", seed=2222222, planet="planet.chondrite",
                    ships=[test_ship(ilwrath), test_ship(flagship, weapons=W_HOMING | W_LASER)],
                    steps=random_steps(2222, 2000), full=60))
    return out


def battle_frame_hash(frame):
    """A frame's ships, elements and draws as the reference keeps a long
    battle's: the SHA-256 of their compact JSON, its first 16 hex digits."""
    import hashlib
    text = json.dumps(frame[F_SHIPS:F_DRAWS + 1], separators=(",", ":"))
    return hashlib.sha256(text.encode()).hexdigest()[:16]


def record_battle(uqm, battle_exe, strings_exe, images_exe, package):
    """UQM's own battle engine, frame by frame (battle.c), with the
    package's masks of the battle's sheets."""
    ids = battle_program_sheets()
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, list(enumerate(ids)))
        sheets_file = os.path.join(tmp, "sheets.txt")
        with open(sheets_file, "w") as f:
            for n, key in enumerate(ids):
                f.write(f"{n} {key} {len(hashes[key])}\n")
        program = BattleProgram(battle_exe, sheets_file, masks)
        scenarios = battle_scenarios(program)
        battles = program.run(scenarios)
    check_battles(scenarios, battles)
    out = []
    for scenario, battle in zip(scenarios, battles):
        full = scenario["full"]
        frames = [f if i <= full else f[:F_SHIPS] + [len(f[F_ELEMENTS]), battle_frame_hash(f)]
                  for i, f in enumerate(battle["frames"])]
        inputs = {k: v for k, v in scenario.items() if k not in ("name", "full")}
        out.append({"name": scenario["name"], "input": inputs, "full": full,
                    "ended": battle["ended"], "stars": battle["stars"], "frames": frames,
                    "crew_floating": battle["crew_floating"], "freed": battle["freed"]})
    data = header(uqm, ["tools/extract/golden/battle.c", "tools/extract/golden/battle_stubs.c"]
                  + [f"src/{f}" for f in BATTLE_LINK] + ["src/uqm/battle.c", "src/uqm/galaxy.c",
                                                         "src/libs/math/random.c"], [
        "UQM 0.8.0's own battle engine, recorded by running battle.c's Battle "
        "as an encounter runs it (see tools/extract/golden/battle.c): "
        "TFB_SeedRandom from the seed, InitShips (the stars, five asteroids, "
        "the planet), each side's ship warping in (the enemy's first), then "
        "DoBattle's frames: ProcessInput, and RedrawQueue's PreProcessQueue "
        "(each element's step, moving, ProcessCollisions over UQM's own "
        "DrawablesIntersect and collide()), CalcView and PostProcessQueue, "
        "with UQM's process.c, collide.c, gravity.c, misc.c, weapon.c, ship.c, "
        "tactrans.c, init.c, status.c and shipstat.c; drawing, sound and "
        "music stubbed. Contact is found with the masks of the sheets "
        "(sheets, numbered; the big size's, as battle-masks.json has them) "
        f"from the official content package (SHA-256 {CONTENT_SHA256}); the "
        "medium and small sizes only count their frames (SetAbsFrameIndex "
        "takes an index round them). Everything else is code.",
        "The ships are test ships (load_ship in battle.c): UQM's stock "
        "ship_preprocess and ship_postprocess with no callbacks of their "
        "own, flying the flagship's or the Ilwrath's sheets with its "
        "RACE_DESC's numbers (data/ships.json, changed where input says) and "
        "plain weapons made by weapon.c: weapons 1 a missile ahead "
        "(initialize_missile from the ship's weapon sheet at its facing), 2 "
        "a homing missile ahead (turning a facing towards TrackShip's "
        "target every track_wait + 1 frames), 4 a laser ahead "
        "(initialize_laser, laser_range pixels), 8 six missiles round the "
        "ship (facing + i * 8 / 3) instead; life, speed, hits, damage and "
        "offset are the missiles' (offset the laser's too): the flagship's "
        "blaster's numbers or the Ilwrath's hellfire's.",
        "input: seed (TFB_SeedRandom's), zoom (smooth, UQM's default "
        "TFB_SCALE_TRILINEAR, or step), runaway (STARBASE_AVAILABLE set: side "
        "0 can retreat), the planet's sheets, each side's ship, fleet (how many "
        "of its ship each side has, 1 each without), steps "
        "[frames, side 0's keys, side 1's] in BATTLE_INPUT_STATE (1 left, 2 "
        "right, 4 thrust, 8 weapon, 16 special, 32 escape), events [frame, "
        "side, target, crew]: AbandonShip called on side's ship towards "
        "target's before that frame. Some battles' keys were made by a pilot "
        "steering at the planet, the other ship or an asteroid; they're "
        "recorded as keys.",
        "stars: InitGalaxy's stars (log_star_array: 30 big, 60 medium, 90 "
        "small, each block sorted by y), [x, y] each, before MoveGalaxy moves "
        "them.",
        "frames: the first after the ships are spawned (before any frame), "
        "then one after each DoBattle: [TFB_Random's seed, the camera's "
        "scroll this frame (CalcView's dx, dy, added to every element), "
        "zoom_out, the view (0 stable, 1 scrolled, 2 zoomed; -1 before the "
        "first frame), SpaceOrg x and y (the view's corner), ships, elements, "
        "draws]. ships: each side's ships in its queue, each [crew_level "
        "and energy_level (SHIP_INFO's: the status panel's), ShipFacing, "
        "energy_counter, weapon_counter, special_counter, cur_status_flags, 1 "
        "if it has an element], or null without a RACE_DESC (not yet picked, or freed). "
        "elements: the display list from its head, each [state_flags, "
        "life_span, crew_level (hit_points), mass_points, turn_wait, "
        "thrust_wait, the velocity's TravelAngle, vector, fract, error and "
        "incr (width then height), current x, y, next x, y] as Super Melee's "
        "netplay checksum (checksum.c) takes them, then [its sheet's number, "
        "frame index, playerNr, the primitive's type (7 none), 1 if a filled "
        "stamp in black (cloaked), its slot in the list's table (its handle, "
        "1 to 150) and its hTarget's (0 none)]. draws: each TFB_Random this "
        "frame, by the UQM function that drew it.",
        "After the first full frames (full, counting the first), a frame is "
        "[seed, scroll x, y, zoom_out, view, SpaceOrg x, y, the number of elements, the "
        "first 16 hex digits of the SHA-256 of [ships, elements, draws] as "
        "compact JSON (no spaces)]. ended: whether the battle ended (a side "
        "out of ships) before the steps did.",
        "crew_floating: the crew floating in space as the battle stopped "
        "(CountCrewElements); freed: each side's STARSHIPs' crew_level, in "
        "its queue's order, after Battle's UninitShips gave that crew to the "
        "ships left with crew and recorded each ship's crew (a ship never "
        "picked keeps the crew it came with). Added later, at the end.",
    ])
    data["sheets"] = ids
    data["battles"] = out
    return data


def check_battles(scenarios, battles):
    """Each battle shows what it's for."""
    by_name = {b["name"]: b for b in battles}
    scenario = {sc["name"]: sc for sc in scenarios}

    def frames(name):
        return by_name[name]["frames"]

    def draws(name):
        return {d for f in frames(name)[1:] for d in f[F_DRAWS]}

    def crew(name, side):
        return [fighting(f, side)[0] if fighting(f, side) else None for f in frames(name)]

    def lost(name, side):
        c = crew(name, side)
        return any(a is not None and b is not None and b < a for a, b in zip(c, c[1:]))

    def keys(name, side):
        """Each frame's keys for the side (frame n's at n, n from 1)."""
        return [None] + [step[1 + side] for step in scenario[name]["steps"] for _ in range(step[0])]

    def fail(name, what):
        raise RuntimeError(f"{name}: {what}")

    def near(f, a, b, distance):
        return a and b and abs(wrap_delta(a[E_CX] - b[E_CX], 8192)) + abs(wrap_delta(a[E_CY] - b[E_CY], 7680)) < distance

    def turns_taken_back(name, side, turn_wait):
        """Frames where the ship turned (its turn wait set again from 0)
        but ends facing as it was: ProcessCollisions took the turn back,
        its frame touching something at the frame's start (time 1)."""
        fr, k = frames(name), keys(name, side)
        n = 0
        for i in range(1, len(fr) - 1):
            a, b = ship_of(fr[i], side), ship_of(fr[i + 1], side)
            if a and b and k[i + 1] & (B_LEFT | B_RIGHT) and a[4] == 0 and b[4] == turn_wait \
                    and fighting(fr[i], side)[2] == fighting(fr[i + 1], side)[2]:
                n += 1
        return n

    if frames("warp-in")[0][F_DRAWS].count("spawn_planet") != 12:
        fail("warp-in", "the planet wasn't put on an asteroid first (TimeSpaceMatterConflict)")
    if not lost("planet-crash", 0) or not by_name["planet-crash"]["ended"]:
        fail("planet-crash", "the ship didn't die of the planet")
    # A ship that never thrusts only waits to thrust after a bump
    # (COLLISION_THRUST_WAIT); here with the other ship next to it.
    if not any(near(f, ship_of(f, 0), ship_of(f, 1), 400) and ship_of(f, 1)[5]
               for f in frames("ramming")):
        fail("ramming", "the ships never bumped")
    if not by_name["shooting"]["ended"] or "explosion_preprocess" not in draws("shooting"):
        fail("shooting", "nobody died")
    # The homing missiles (side 0's, the flagship's blaster) turn in flight:
    # the same missile (its slot, its life a frame less) heading another way.
    fr = frames("homing")
    turns = 0
    for a, b in zip(fr[1:], fr[2:]):
        shots = {e[24]: e for e in a[F_ELEMENTS] if e[E_PLAYER] == 0 and e[E_MASS] == 2
                 and e[E_FLAGS] & FINITE_LIFE_FLAG}
        for e in b[F_ELEMENTS]:
            was = shots.get(e[24])
            if was and e[E_MASS] == 2 and e[E_LIFE] == was[E_LIFE] - 1 and e[6] != was[6] \
                    and e[E_FRAME] != was[E_FRAME]:   # its facing too: not gravity's pull
                turns += 1
    if not turns or not lost("homing", 1):
        fail("homing", "the homing missiles never turned and hit")
    if "spawn_asteroid" not in draws("asteroids"):
        fail("asteroids", "no asteroid came back")
    if max(len(f[F_ELEMENTS]) for f in frames("list-full")) < 150:
        fail("list-full", "the list never filled")
    if "AbandonShip" not in draws("crew-floating"):
        fail("crew-floating", "no crew left a ship")
    c = crew("crew-floating", 0)
    if not any(a is not None and b is not None and b > a for a, b in zip(c, c[1:])):
        fail("crew-floating", "no crew picked up")
    # Crew heading for side 0's ship (slot of its element) head elsewhere once it dies.
    fr = frames("crew-orphans")
    dead_slot = ship_of(fr[1], 0)[24]
    if not any(e[E_FLAGS] & CREW_OBJECT_FLAG and e[25] not in (0, dead_slot) for f in fr for e in f[F_ELEMENTS]) \
            or not by_name["crew-orphans"]["ended"]:
        fail("crew-orphans", "no crew turned to another ship after theirs died")
    # The crew-immune ship's AbandonShip (event at 24, so in frame 25) does nothing.
    if "AbandonShip" in fr[25][F_DRAWS] or crew("crew-orphans", 0)[24] != crew("crew-orphans", 0)[25]:
        fail("crew-orphans", "the crew-immune ship lost crew")
    if turns_taken_back("scrape", 0, scenario["scrape"]["ships"][0]["characteristics"]["turn_wait"]) == 0:
        fail("scrape", "no turn taken back")
    # Seed 13's two asteroids on each other break up on the first frame.
    first = frames("jostle")[1][F_ELEMENTS]
    if sum(1 for e in first if e[E_FLAGS] & FINITE_LIFE_FLAG and e[E_FRAME] == 16 and e[E_PRIM] == 1) < 2:
        fail("jostle", "the asteroids spawned on each other didn't break up")
    # The retreat: flashing (a filled stamp), then gone with its crew.
    fr = frames("retreat")
    last = [f[F_SHIPS][0][0] for f in fr if f[F_SHIPS][0][0]][-1]
    if not by_name["retreat"]["ended"] or not any(ship_of(f, 0) and ship_of(f, 0)[E_PRIM] == 2 for f in fr) \
            or not last[0]:
        fail("retreat", "the ship didn't warp out with its crew")
    zooms = {f[F_ZOOM] for f in frames("step-zoom")}
    if not zooms <= {0, 1, 2, 3} or len(zooms) < 3:
        fail("step-zoom", f"zooms {sorted(zooms)}")
    # A ship dying with no other ship's element in the list.
    lonely = 0
    before = frames("lonely-death")[0]
    for f in frames("lonely-death")[1:]:
        for e in f[F_ELEMENTS]:
            if e[E_FLAGS] & PLAYER_SHIP_FLAG and e[E_FLAGS] & FINITE_LIFE_FLAG and e[E_LIFE] == 35 and e[E_CREW] == 0 \
                    and not any(x[E_FLAGS] & PLAYER_SHIP_FLAG and x[24] != e[24]
                                for g in (before, f) for x in g[F_ELEMENTS]):
                lonely += 1
        before = f
    if not lonely or not by_name["lonely-death"]["ended"]:
        fail("lonely-death", "no ship died alone")
    # The random battles keep what they were added for.
    randoms = [n for n in by_name if n.startswith("random-")]
    def ships_input(name, side):
        return scenario[name]["ships"][side]
    if not any(ships_input(n, side)["crew"] > ships_input(n, side)["max_crew"]
               and fighting(frames(n)[0], side)[0] == ships_input(n, side)["max_crew"]
               for n in randoms for side in (0, 1)):
        fail("random-*", "no ship's crew held to its most")
    skiffs = [(n, side) for n in randoms for side in (0, 1)
              if ships_input(n, side)["characteristics"]["thrust_increment"]
              == ships_input(n, side)["characteristics"]["max_thrust"]]
    # inertial_thrust ran: the key held, at the top speed, the thrust wait just set.
    if not any(fighting(f, side) and fighting(f, side)[6] & B_THRUST and fighting(f, side)[6] & (1 << 7)
               and ship_of(f, side)[5] == ships_input(n, side)["characteristics"]["thrust_wait"]
               for n, side in skiffs for f in frames(n)):
        fail("random-*", "no Skiff thrusting")
    org_jump_x = (8192 // 75) & ~3
    if not any(f[1] == org_jump_x for n in randoms for f in frames(n)) \
            or not any(f[1] == -org_jump_x for n in randoms for f in frames(n)):
        fail("random-*", "the camera's scroll never clamped both ways")
    down = 0
    for n in randoms:
        for frame, side, target, crew in scenario[n]["events"]:
            ship = ship_of(frames(n)[frame], side)
            # Its TravelAngle moving downwards, and not crew-immune (AbandonShip returns at once).
            if ship and 17 <= ship[6] <= 47 and not ships_input(n, side)["flags"] & 64 \
                    and frame < len(frames(n)) - 1:
                down += 1
    if not down:
        fail("random-*", "no AbandonShip from a ship moving downwards")
    # Fleets: two die at once (both with an element and no crew), and on
    # each side a later ship flies in the first one's element (slot).
    fr = frames("fleets")
    both = any(all(s and s[0] == 0 and s[7] for s in (fighting(f, 0), fighting(f, 1))) for f in fr)
    taken_over = all(any(any(s and s[7] for s in f[F_SHIPS][side][1:]) and ship_of(f, side)
                         and ship_of(f, side)[24] == ship_of(fr[0], side)[24] for f in fr)
                     for side in (0, 1))
    if not both or not taken_over or not by_name["fleets"]["ended"]:
        fail("fleets", "no ships dying together, or no next ship")


# ---- The flagship and the Ilwrath (stage 4a item 3) ----

# sis.h's module numbers and each kind of slot's empty mark.
M_THRUSTER, M_JETS, M_CREW_POD, M_STORAGE_BAY, M_FUEL_TANK = 1, 2, 3, 4, 5
M_DYNAMO, M_FURNACE, M_GUN, M_BLASTER, M_CANNON, M_TRACKING, M_DEFENSE = 7, 8, 9, 10, 11, 12, 13
M_BOMB_0 = 14
EMPTY_DRIVE, EMPTY_JET, EMPTY_MODULE = 20, 21, 22
# A new game's flagship (data/new-game.json, ShipModules.FromLoadout): a
# fuel tank in module slot 1, a crew pod in 2, a storage bay in 8, the gun
# in 15; thrusters in drive slots 5 and 6, jets in 0 and 6.
START_MODULES = {1: M_FUEL_TANK, 2: M_CREW_POD, 8: M_STORAGE_BAY, 15: M_GUN}
# How many battles random_ship_battle makes.
RANDOM_SHIP_BATTLES = 6
# The flagship's shots' life by damage when first recorded (sis_ship.c:
# BLASTER_LIFE + 3 a step, less the frame they're fired in).
SIS_SHOT_LIFE = {2: 11, 4: 14, 6: 17}
LINE_PRIM, STAMPFILL_PRIM = 3, 2


def sis(crew, modules=None, thrusters=(5, 6), jets=(0, 6), bomb=0):
    """The flagship (sis_ship.c) with crew (GLOBAL_SIS's CrewEnlisted) and
    these slots: modules {slot: module}, the drive and jet slots filled."""
    modules = START_MODULES if modules is None else modules
    return dict(ship="sis_ship", kind="sis", crew=crew, bomb=bomb,
                drives=[M_THRUSTER if i in thrusters else EMPTY_DRIVE for i in range(11)],
                jets=[M_JETS if i in jets else EMPTY_JET for i in range(8)],
                modules=[modules.get(i, EMPTY_MODULE) for i in range(16)])


def ilwrath(crew=22):
    """The Ilwrath (ilwrath.c), its crew held to crew as an encounter's ship's is."""
    return dict(ship="ilwrath", kind="ilwrath", crew=crew)


def distance(a, b):
    return abs(wrap_delta(a[E_CX] - b[E_CX], 8192)) + abs(wrap_delta(a[E_CY] - b[E_CY], 7680))


def cloaker(period=150, close=700, every=1):
    """An Ilwrath's pilot: it flies at the other ship, cloaks every period
    frames, and fires (uncloaking) once within close of it."""
    def pilot(f, side, t):
        keys = steer(f, side, the_ship(1 - side))
        ship, them = ship_of(f, side), ship_of(f, 1 - side)
        if ship and them and distance(ship, them) < close and (t // 4) % every == 0:
            keys |= B_WEAPON
        elif t % period < 4:
            keys |= B_SPECIAL
        return keys
    return pilot


def random_ship_battle(name, rng):
    """The flagship with slots at random (at most 7 point defences: with 8
    or more, spawn_point_defense's colour is read past its table's end)
    and crew at random, against the Ilwrath with crew at random, keys at
    random for both, Special among them."""
    pieces = [EMPTY_MODULE] * 5 + [M_CREW_POD, M_STORAGE_BAY, M_FUEL_TANK, M_DYNAMO, M_FURNACE, M_GUN,
                                   M_BLASTER, M_CANNON, M_TRACKING, M_DEFENSE]
    modules = {i: rng.choice(pieces) for i in range(16)}
    modules[15] = rng.choice([M_GUN, M_BLASTER, M_CANNON, EMPTY_MODULE])   # mostly armed
    defences = [i for i, m in modules.items() if m == M_DEFENSE]
    for i in defences[7:]:
        modules[i] = EMPTY_MODULE
    ship = sis(rng.choice([0, 3, 40, 150, 2000]), modules,
               thrusters=rng.sample(range(11), rng.randint(0, 11)), jets=rng.sample(range(8), rng.randint(0, 8)))
    frames = rng.choice([1200, 1600])
    keys = [0, B_THRUST, B_LEFT, B_RIGHT, B_THRUST | B_LEFT, B_THRUST | B_RIGHT, B_WEAPON, B_WEAPON | B_THRUST,
            B_WEAPON | B_LEFT, B_SPECIAL, B_SPECIAL | B_THRUST, B_SPECIAL | B_WEAPON, B_SPECIAL | B_LEFT]
    return dict(name=name, seed=rng.randrange(1 << 31), planet=rng.choice(BATTLE_PLANETS),
                zoom=rng.choice(["smooth", "smooth", "step"]),
                ships=[ship, ilwrath(rng.choice([1, 7, 22]))],
                steps=random_steps(rng.randrange(1 << 30), frames, choices=keys, longest=rng.choice([4, 12, 30])),
                full=60)


def ship_battle_scenarios(program):
    """The flagship's layouts against the Ilwrath, and the Ilwrath's
    hellfire and cloak (outside the Earth fight: keys, not its AI)."""
    out = []
    shoot = lambda keep_off: lambda f, side, t: steer(f, side, the_ship(1 - side), fire=True, keep_off=keep_off)
    # A new game's flagship, its gun in the nose, against the Ilwrath
    # coming at it, firing and cloaking.
    s = dict(name="sis-start", seed=7001, planet="planet.water",
             ships=[sis(50), ilwrath()], steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 900, [shoot(700), cloaker()]))
    # Guns all round: a blaster in the nose (15), a spread pair of guns
    # (14), cannons both sides (13), a gun in the tail (0); dynamos and a
    # furnace; six thrusters and three jets. The flagship spins firing.
    s = dict(name="sis-all-round", seed=7002, planet="planet.chondrite",
             ships=[sis(80, {0: M_GUN, 1: M_CREW_POD, 2: M_CREW_POD, 3: M_DYNAMO, 4: M_DYNAMO, 5: M_FURNACE,
                              13: M_CANNON, 14: M_GUN, 15: M_BLASTER},
                        thrusters=(0, 2, 4, 6, 8, 10), jets=(1, 3, 5)), ilwrath()],
             steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 900, [lambda f, side, t: B_WEAPON | (B_LEFT if (t // 40) % 2 else B_RIGHT | B_THRUST),
                                         shoot(250)]))
    # Tracking: four tracking systems (three count), cannons nose and tail,
    # a spread pair of blasters; the Ilwrath flying at random.
    s = dict(name="sis-tracking", seed=7003, planet="planet.water",
             ships=[sis(50, {0: M_CANNON, 2: M_CREW_POD, 4: M_TRACKING, 5: M_TRACKING, 6: M_TRACKING,
                              7: M_TRACKING, 14: M_BLASTER, 15: M_CANNON}), ilwrath(22)],
             steps=[(20, 0, 0)], full=150)
    rnd = random_steps(7031, 900, sides=1, choices=[0, B_THRUST, B_LEFT | B_THRUST, B_RIGHT | B_THRUST, B_LEFT])
    flat = [k for n, k in rnd for _ in range(n)]
    out.append(piloted(program, s, 900, [shoot(1500), lambda f, side, t: flat[min(len(flat) - 1, t)]]))
    # One tracking system (its shots turn every fourth frame) and two.
    for n, slots in (("1", {12: M_TRACKING}), ("2", {11: M_TRACKING, 12: M_TRACKING})):
        s = dict(name=f"sis-tracking-{n}", seed=7040 + int(n), planet="planet.chondrite",
                 ships=[sis(20, {**START_MODULES, **slots}), ilwrath(22)], steps=[(20, 0, 0)], full=80)
        out.append(piloted(program, s, 500, [shoot(1800), lambda f, side, t: flat[min(len(flat) - 1, t)]]))
    # Point defence: three modules, the key held; the Ilwrath's hellfire
    # close by, and the asteroids.
    s = dict(name="sis-point-defence", seed=7004, planet="planet.water",
             ships=[sis(50, {**START_MODULES, 9: M_DEFENSE, 10: M_DEFENSE, 11: M_DEFENSE}), ilwrath()],
             steps=[(20, 0, 0)], full=200)
    out.append(piloted(program, s, 800, [lambda f, side, t: B_SPECIAL | steer(f, side, the_ship(1), fire=t % 60 < 8,
                                                                                keep_off=350),
                                         shoot(150)]))
    # No weapon and no point defence: the keys do nothing
    # (sis_battle_preprocess); the captain alone, killed by the Ilwrath:
    # uninit_sis writes the game over, ~0.
    s = dict(name="sis-unarmed", seed=7005, planet="planet.chondrite",
             ships=[sis(0, {2: M_CREW_POD, 4: M_FURNACE}), ilwrath()], steps=[(20, 0, 0)], full=120)
    out.append(piloted(program, s, 600, [lambda f, side, t: B_WEAPON | B_SPECIAL | steer(f, side, the_ship(1)),
                                         shoot(100)]))
    # The most: every drive and jet slot, cannons in the four weapon
    # slots, three tracking, two point defences, three dynamos, two
    # furnaces, two crew pods.
    s = dict(name="sis-full", seed=7006, planet="planet.water",
             ships=[sis(100, {0: M_CANNON, 1: M_CREW_POD, 2: M_CREW_POD, 3: M_FURNACE, 4: M_FURNACE, 5: M_DYNAMO,
                              6: M_DYNAMO, 7: M_DYNAMO, 8: M_DEFENSE, 9: M_DEFENSE, 10: M_TRACKING,
                              11: M_TRACKING, 12: M_TRACKING, 13: M_CANNON, 14: M_CANNON, 15: M_CANNON},
                        thrusters=range(11), jets=range(8)), ilwrath()],
             steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 900, [lambda f, side, t: B_SPECIAL * ((t // 8) % 2) | steer(f, side, the_ship(1), fire=True,
                                                                                                keep_off=500),
                                         cloaker(every=2)]))
    # The bomb aboard (CHMMR_BOMB_STATE 3): init_sis sets BOMB_CARRIER, and
    # Escape doesn't retreat though the starbase is available.
    out.append(dict(name="sis-bomb", seed=7007, planet="planet.water", runaway=True,
                    ships=[sis(50, {**START_MODULES, **{i: M_BOMB_0 + i % 6 for i in range(3, 13) if i != 8}},
                               bomb=3), ilwrath()],
                    steps=[(20, 0, 0), (30, B_ESCAPE, 0), (100, B_ESCAPE | B_THRUST, B_THRUST)], full=150))
    # Retreating: the flagship, shot at, warps out (flee_preprocess) and
    # keeps its crew; freed by new_ship, uninit_sis writes it back. Once
    # from afar, once with the Ilwrath close and firing, so it's hit while
    # it flashes.
    def retreat_after(t0, keep_off):
        return lambda f, side, t: B_ESCAPE if t >= t0 else steer(f, side, the_ship(1), fire=True, keep_off=keep_off)
    s = dict(name="sis-retreat", seed=7012, planet="planet.water", runaway=True,
             ships=[sis(40, {**START_MODULES, 9: M_DEFENSE}), ilwrath()], steps=[(20, 0, 0)], full=60)
    out.append(piloted(program, s, 700, [retreat_after(320, 900), shoot(400)]))
    def flee_close(f, side, t):
        ship, them = ship_of(f, 0), ship_of(f, 1)
        if ship and them and distance(ship, them) < 450:
            return B_ESCAPE
        return 0
    def shoot_while_seen(stop_after):
        """The Ilwrath's pilot: shooting until the flagship has flashed
        stop_after frames."""
        flashing = []
        def pilot(f, side, t):
            ship = ship_of(f, 0)
            if ship and ship[E_PRIM] == STAMPFILL_PRIM:
                flashing.append(t)
            return steer(f, side, the_ship(0), fire=len(flashing) < stop_after, keep_off=100)
        return pilot
    s = dict(name="sis-retreat-hit", seed=7013, planet="planet.chondrite", runaway=True,
             ships=[sis(30), ilwrath()], steps=[(20, 0, 0)], full=60)
    out.append(piloted(program, s, 700, [flee_close, shoot_while_seen(3)], chunk=2))
    # Shot down while it flees: destroyed, ~0.
    s = dict(name="sis-retreat-shot-down", seed=7013, planet="planet.chondrite", runaway=True,
             ships=[sis(30), ilwrath()], steps=[(20, 0, 0)], full=60)
    out.append(piloted(program, s, 700, [flee_close, shoot(100)], chunk=2))
    # The Ilwrath's cloak, scripted: on, thrusting and turning cloaked (no
    # ion trail), Special again (uncloaking), on again, firing (uncloaking,
    # turned towards the flagship ahead of it), fading in and out.
    out.append(dict(name="ilwrath-cloak", seed=7008, planet="planet.chondrite",
                    ships=[sis(50), ilwrath()],
                    steps=[(20, 0, 0), (1, 0, B_SPECIAL), (30, B_THRUST, B_THRUST), (20, B_LEFT, B_THRUST | B_LEFT),
                           (14, 0, 0), (1, 0, B_SPECIAL), (20, 0, 0), (1, 0, B_SPECIAL), (5, 0, B_SPECIAL),
                           (30, B_RIGHT, B_THRUST), (6, 0, B_WEAPON), (10, 0, 0), (1, 0, B_SPECIAL),
                           (40, B_THRUST, B_THRUST | B_RIGHT), (3, 0, B_WEAPON | B_SPECIAL), (40, 0, B_SPECIAL),
                           (10, 0, B_WEAPON), (60, B_LEFT, B_SPECIAL | B_THRUST)],
                    full=320))
    # Hunting with the cloak: the Ilwrath closes in cloaked and fires.
    s = dict(name="ilwrath-hunt", seed=7009, planet="planet.water",
             ships=[sis(30, {**START_MODULES, 9: M_DEFENSE}), ilwrath()], steps=[(20, 0, 0)], full=200)
    out.append(piloted(program, s, 900, [lambda f, side, t: B_SPECIAL | (B_THRUST if t % 100 < 30 else 0)
                                         | (B_LEFT if t % 200 < 15 else 0), cloaker(period=90, close=450)]))
    # Point defence passing over a cloaked ship: the flagship sits holding
    # Special while the Ilwrath, cloaked, comes within its lasers' range
    # without firing.
    def creeper(f, side, t):
        ship, them = ship_of(f, side), ship_of(f, 0)
        if not ship or not them:
            return 0
        if ship[E_PRIM] == 1:
            return B_SPECIAL   # seen: cloak (Special while cloaked would uncloak)
        far = distance(ship, them) > 1200
        return steer(f, side, the_ship(0)) & ~(0 if far or t % 24 < 2 else B_THRUST)
    s = dict(name="sis-defence-cloaked", seed=7011, planet="planet.chondrite",
             ships=[sis(50, {**START_MODULES, 9: M_DEFENSE, 10: M_DEFENSE}), ilwrath()], steps=[(20, 0, 0)])
    s = piloted(program, s, 500, [lambda f, side, t: B_SPECIAL, creeper])
    out.append(dict(s, full=sum(n for n, *_ in s["steps"])))
    # Two Ilwraths, both cloaking and firing, cloaked at once at times (the
    # Earth fight's 7 crew on one side).
    out.append(dict(name="ilwrath-duel", seed=7010, planet="planet.chondrite",
                    ships=[ilwrath(7), ilwrath(22)],
                    steps=random_steps(7100, 1500, choices=[0, B_THRUST, B_LEFT, B_RIGHT, B_THRUST | B_LEFT,
                                                            B_WEAPON, B_WEAPON | B_THRUST, B_SPECIAL,
                                                            B_SPECIAL | B_THRUST, B_SPECIAL | B_WEAPON],
                                       longest=12), full=80))
    rng = __import__("random").Random(4343)
    out += [random_ship_battle(f"ships-random-{i}", rng) for i in range(RANDOM_SHIP_BATTLES)]
    return out


def check_ship_battles(scenarios, battles):
    """Each battle of the flagship and the Ilwrath shows what it's for."""
    by_name = {b["name"]: b for b in battles}
    scenario = {sc["name"]: sc for sc in scenarios}

    def frames(name):
        return by_name[name]["frames"]

    def fail(name, what):
        raise RuntimeError(f"{name}: {what}")

    def shots(f, side):
        return [e for e in f[F_ELEMENTS] if e[E_PLAYER] == side and e[E_FLAGS] & FINITE_LIFE_FLAG
                and not e[E_FLAGS] & PLAYER_SHIP_FLAG and e[E_PRIM] == 1]

    def new_shots(f):
        """The flagship's shots fired this frame (their life whole)."""
        return [e for e in shots(f, 0) if SIS_SHOT_LIFE.get(e[E_MASS]) == e[E_LIFE]]

    def crew(name, side):
        return [fighting(f, side)[0] if fighting(f, side) else None for f in frames(name)]

    def lost(name, side):
        c = crew(name, side)
        return any(a is not None and b is not None and b < a for a, b in zip(c, c[1:]))

    def cloaked(f, side):
        s = ship_of(f, side)
        return s and s[E_PRIM] == STAMPFILL_PRIM and s[E_BLACK]

    if not any(new_shots(f) for f in frames("sis-start")) or not lost("sis-start", 1) or not lost("sis-start", 0):
        fail("sis-start", "the flagship and the Ilwrath didn't hit each other")
    # nose 1, spread 2, sides 2, tail 1, in one frame.
    if max(len(new_shots(f)) for f in frames("sis-all-round")) != 6:
        fail("sis-all-round", "no six shots at once")
    if {e[E_MASS] for f in frames("sis-all-round") for e in new_shots(f)} != {2, 4, 6}:
        fail("sis-all-round", "not every kind of gun fired")
    # Tracking: a shot turned (the same slot, a frame on, along another
    # facing at the blaster's speed: SetVelocityVector's angle is a facing's).
    for name in ("sis-tracking", "sis-tracking-1", "sis-tracking-2"):
        turns = 0
        fr = frames(name)
        for a, b in zip(fr, fr[1:]):
            was = {e[E_SLOT]: e for e in shots(a, 0)}
            for e in shots(b, 0):
                w = was.get(e[E_SLOT])
                if w and w[E_MASS] == e[E_MASS] and e[E_LIFE] == w[E_LIFE] - 1 and e[6] != w[6] and e[6] % 4 == 0:
                    turns += 1
        if not turns:
            fail(name, "no shot turned")
    # Point defence: its lasers (lines, their damage the modules' count).
    fr = frames("sis-point-defence")
    lasers = [e for f in fr for e in f[F_ELEMENTS] if e[E_PRIM] == LINE_PRIM and e[E_PLAYER] == 0 and e[E_MASS] == 3]
    if not lasers:
        fail("sis-point-defence", "the point defence never fired")
    if by_name["sis-unarmed"]["crew_enlisted"] != 0xFFFF or any(new_shots(f) for f in frames("sis-unarmed")):
        fail("sis-unarmed", "the unarmed flagship fired, or didn't die")
    if any(f[F_SHIPS][0][0] and f[F_SHIPS][0][0][4] not in (0, 1, 2) for f in frames("sis-unarmed")[2:]):
        fail("sis-unarmed", "the weapon's counter isn't held at 2")
    # The retreats: the flagship flashed (a filled stamp) and is gone with
    # its crew (not ~0), the Ilwrath still fighting; the battle over.
    for name in ("sis-retreat", "sis-retreat-hit"):
        c = [x for x in crew(name, 0) if x is not None]
        last = frames(name)[-1]
        if not by_name[name]["ended"] or by_name[name]["crew_enlisted"] in (0xFFFF, scenario[name]["ships"][0]["crew"]) \
                or by_name[name]["crew_enlisted"] != c[-1] - 1 \
                or ship_of(last, 0) or not (fighting(last, 1) and fighting(last, 1)[0]) \
                or not any(ship_of(f, 0) and ship_of(f, 0)[E_PRIM] == STAMPFILL_PRIM for f in frames(name)):
            fail(name, "the flagship didn't retreat with its crew")
    fr = frames("sis-retreat-hit")
    if not any(ship_of(a, 0) and ship_of(b, 0) and ship_of(a, 0)[E_PRIM] == STAMPFILL_PRIM
               and fighting(a, 0) and fighting(b, 0) and fighting(b, 0)[0] < fighting(a, 0)[0]
               for a, b in zip(fr, fr[1:])):
        fail("sis-retreat-hit", "the flagship wasn't hit while it fled")
    fr = frames("sis-retreat-shot-down")
    if by_name["sis-retreat-shot-down"]["crew_enlisted"] != 0xFFFF \
            or not any(ship_of(f, 0) and ship_of(f, 0)[E_PRIM] == STAMPFILL_PRIM for f in fr):
        fail("sis-retreat-shot-down", "the flagship wasn't destroyed while it fled")
    if not by_name["sis-bomb"]["bomb_carrier"] or by_name["sis-bomb"]["ended"]:
        fail("sis-bomb", "the bomb's carrier retreated, or wasn't one")
    if max(len(new_shots(f)) for f in frames("sis-full")) != 6 or \
            not any(e[E_PRIM] == LINE_PRIM and e[E_PLAYER] == 0 for f in frames("sis-full") for e in f[F_ELEMENTS]):
        fail("sis-full", "no six cannons at once, or no point defence")
    # The cloak: on (a black filled stamp), off again after firing, and
    # fading in (filled in colours) on the way.
    for name in ("ilwrath-cloak", "ilwrath-hunt"):
        fr = frames(name)
        on = [i for i, f in enumerate(fr) if cloaked(f, 1)]
        if not on or not any(ship_of(f, 1) and ship_of(f, 1)[E_PRIM] == 1 for f in fr[on[0]:]):
            fail(name, "the Ilwrath never cloaked and uncloaked")
        colours = {ship_of(f, 1)[E_COLOR] for f in fr if ship_of(f, 1) and ship_of(f, 1)[E_PRIM] == STAMPFILL_PRIM}
        if len(colours) < 6:
            fail(name, f"the cloak's colours: {sorted(colours)}")
    # Point defence held, ready and paid for, with the cloaked Ilwrath in
    # its range (and nothing else), fires nothing.
    def in_laser_range(e, ship):
        """spawn_point_defense's range: the next places' deltas as SIZEs,
        not across the seam, in display units, within LASER_RANGE."""
        d = []
        for k in (17, 18):
            v = (e[k] - ship[k] + 32768) % 65536 - 32768
            v = -v if v < 0 and v != -32768 else v
            d.append((v >> 2) & 0xFFFF)
        return d[0] <= 100 and d[1] <= 100 and d[0] * d[0] + d[1] * d[1] <= 100 * 100
    defended = scenario["sis-defence-cloaked"]
    cost = min(defended["ships"][0]["modules"].count(M_DEFENSE), 8) << 2
    fr, keys = frames("sis-defence-cloaked"), [None] + [k[1] for k in defended["steps"] for _ in range(k[0])]
    passed = 0
    for i in range(1, len(fr)):
        before, now = fighting(fr[i - 1], 0), fighting(fr[i], 0)
        ship, them = ship_of(fr[i], 0), ship_of(fr[i], 1)
        if not (before and now and ship and them and cloaked(fr[i], 1) and keys[i] & B_SPECIAL):
            continue
        near = [e for e in fr[i][F_ELEMENTS] if e is not ship and not e[E_FLAGS] & ((1 << 6) | (1 << 4))
                and in_laser_range(e, ship)]
        if near == [them] and before[5] == 0 and before[1] >= cost and now[5] == 0:
            passed += 1
    if not passed:
        fail("sis-defence-cloaked", "the point defence never passed over the cloaked Ilwrath")
    if not lost("ilwrath-hunt", 0):
        fail("ilwrath-hunt", "the hellfire never hit")
    fr = frames("ilwrath-duel")
    if not any(cloaked(f, 0) and cloaked(f, 1) for f in fr):
        fail("ilwrath-duel", "the two were never cloaked at once")
    randoms = [n for n in by_name if n.startswith("ships-random-")]
    if not any(by_name[n]["ended"] for n in randoms):
        fail("ships-random-*", "no battle ended")


def record_battle_ships(uqm, battle_exe, strings_exe, images_exe, package):
    """The flagship (sis_ship.c) built from several layouts, and the
    Ilwrath (ilwrath.c), in UQM's own battle engine (battle.c)."""
    ids = battle_program_sheets()
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, list(enumerate(ids)))
        sheets_file = os.path.join(tmp, "sheets.txt")
        with open(sheets_file, "w") as f:
            for n, key in enumerate(ids):
                f.write(f"{n} {key} {len(hashes[key])}\n")
        program = BattleProgram(battle_exe, sheets_file, masks)
        scenarios = ship_battle_scenarios(program)
        battles = program.run(scenarios, colours=True)
    check_ship_battles(scenarios, battles)
    out = []
    for scenario, battle in zip(scenarios, battles):
        full = scenario["full"]
        frames = [f if i <= full else f[:F_SHIPS] + [len(f[F_ELEMENTS]), battle_frame_hash(f)]
                  for i, f in enumerate(battle["frames"])]
        inputs = {k: v for k, v in scenario.items() if k not in ("name", "full")}
        out.append({"name": scenario["name"], "input": inputs, "full": full, "ended": battle["ended"],
                    "crew_enlisted": battle["crew_enlisted"], "bomb_carrier": battle["bomb_carrier"],
                    "stars": battle["stars"], "frames": frames,
                    "crew_floating": battle["crew_floating"], "freed": battle["freed"]})
    data = header(uqm, ["tools/extract/golden/battle.c", "tools/extract/golden/battle_stubs.c"]
                  + [f"src/{f}" for f in BATTLE_LINK] + ["src/uqm/battle.c", "src/uqm/galaxy.c",
                                                         "src/libs/math/random.c"], [
        "UQM 0.8.0's own flagship (sis_ship.c) and Ilwrath (ilwrath.c) in its "
        "own battle engine, recorded by the program battle.json is "
        "(tools/extract/golden/battle.c; see battle.json's notes for the "
        "engine, the sheets, the masks and the frames), as an encounter runs "
        "a battle. The flagship is init_sis's, built from GLOBAL_SIS's slots "
        "and crew (CrewEnlisted, the captain counted on top) as in an "
        "encounter; the Ilwrath init_ilwrath's, its crew held to its "
        "STARSHIP's (the Earth fight's is 7). Both are flown by keys (some "
        "steered by a pilot; the AI is item 4's), so the Ilwrath cloaks as "
        "it does outside the Earth fight.",
        "input: as battle.json's, but each ship is the flagship, kind sis: "
        "crew (CrewEnlisted), bomb (CHMMR_BOMB_STATE), drives (11), jets (8) "
        "and modules (16) as sis.h numbers them (1 FUSION_THRUSTER, 2 "
        "TURNING_JETS, 3 CREW_POD, 4 STORAGE_BAY, 5 FUEL_TANK, 7 DYNAMO_UNIT, "
        "8 SHIVA_FURNACE, 9 GUN_WEAPON, 10 BLASTER_WEAPON, 11 CANNON_WEAPON, "
        "12 TRACKING_SYSTEM, 13 ANTIMISSILE_DEFENSE, 14 to 19 BOMB_MODULE_0 "
        "to 5; 20, 21 and 22 empty drive, jet and module slots); or the "
        "Ilwrath, kind ilwrath: crew.",
        "frames: as battle.json's, each element with one more number at its "
        "end: its primitive's colour as MAKE_RGB15 makes it (red, green and "
        "blue, five bits each, red highest), -1 for one never set (UQM "
        "keeps a primitive per slot: an element starts with its slot's last "
        "colour). The Ilwrath's cloak fades through its colours. "
        "crew_enlisted: GLOBAL_SIS (CrewEnlisted) after the battle: "
        "uninit_sis writes crew_level - 1 there as free_ship frees the "
        "flagship's description, new_ship if it died, UninitShips at the "
        "battle's end if not (its crew held to its most when it warped in, "
        "spawn_ship's clamp), 65535 for none left, UQM's game over; bomb_carrier: "
        "BOMB_CARRIER after it (init_sis sets it when CHMMR_BOMB_STATE is 3).",
        "crew_floating: the crew floating in space as the battle stopped "
        "(CountCrewElements); freed: each side's STARSHIPs' crew_level, in "
        "its queue's order, after Battle's UninitShips gave that crew to the "
        "ships left with crew and recorded each ship's crew (a ship never "
        "picked keeps the crew it came with). Added later, at the end.",
    ])
    data["sheets"] = ids
    data["battles"] = out
    return data


# ---- The AI (stage 4a item 4) ----

# How many battles random_ai_battle makes.
RANDOM_AI_BATTLES = 6


def away_from(side):
    """A pilot's goal: the point of the arena opposite side's ship."""
    def goal(frame):
        s = ship_of(frame, side)
        return ((s[E_CX] + 4096) % 8192, (s[E_CY] + 3840) % 7680) if s else None
    return goal


def random_ai_battle(name, rng):
    """The flagship with slots and crew at random, flown by keys at
    random, against the Ilwrath flown by UQM's AI at a rating at random,
    at Earth (no cloak) or elsewhere."""
    pieces = [EMPTY_MODULE] * 5 + [M_CREW_POD, M_STORAGE_BAY, M_FUEL_TANK, M_DYNAMO, M_FURNACE, M_GUN,
                                   M_BLASTER, M_CANNON, M_TRACKING, M_DEFENSE]
    modules = {i: rng.choice(pieces) for i in range(16)}
    modules[15] = rng.choice([M_GUN, M_BLASTER, M_CANNON, EMPTY_MODULE])
    for i in [i for i, m in modules.items() if m == M_DEFENSE][7:]:
        modules[i] = EMPTY_MODULE
    ship = sis(rng.choice([3, 40, 150, 2000]), modules,
               thrusters=rng.sample(range(11), rng.randint(0, 11)), jets=rng.sample(range(8), rng.randint(0, 8)))
    keys = [0, B_THRUST, B_LEFT, B_RIGHT, B_THRUST | B_LEFT, B_THRUST | B_RIGHT, B_WEAPON, B_WEAPON | B_THRUST,
            B_WEAPON | B_LEFT, B_SPECIAL, B_SPECIAL | B_THRUST, B_SPECIAL | B_WEAPON]
    return dict(name=name, seed=rng.randrange(1 << 31), planet=rng.choice(BATTLE_PLANETS),
                zoom=rng.choice(["smooth", "smooth", "step"]), earth=rng.random() < 0.4,
                ships=[ship, ilwrath(rng.choice([7, 15, 22]))], ai=[None, rng.choice(["standard", "good", "awesome",
                                                                                     "awesome"])],
                steps=random_steps(rng.randrange(1 << 30), rng.choice([1200, 1800]), choices=keys,
                                   longest=rng.choice([4, 12, 30])),
                full=60)


def ai_battle_scenarios(program):
    """The Ilwrath flown by UQM's own AI (intel.c, cyborg.c,
    ilwrath_intelligence) against a scripted flagship, at Earth
    (PROBE_ILWRATH_ENCOUNTER: no cloak) and elsewhere; and the flagship
    flown by the AI (sis_intelligence, the Cyborg option's)."""
    out = []
    shoot = lambda keep_off: lambda f, side, t: steer(f, side, the_ship(1 - side), fire=True, keep_off=keep_off)
    flee = lambda f, side, t: steer(f, side, away_from(1 - side))
    # The Earth fight: a new game's flagship against the Ilwrath of 7
    # crew, the story's AI (AWESOME_RATING), not cloaking.
    pods = {**START_MODULES, 3: M_CREW_POD, 4: M_CREW_POD}
    s = dict(name="ai-earth", seed=8001, planet="planet.water", ships=[sis(120, pods), ilwrath(7)],
             ai=[None, "awesome"], earth=True, steps=[(20, 0, 0)], full=200)
    out.append(piloted(program, s, 1200, [lambda f, side, t: steer(f, side, the_ship(1), fire=t % 60 < 10,
                                                                    keep_off=700), None]))
    # The same, the flagship keeping away (the AI hunting it far off,
    # where elsewhere it would cloak), then turning to fight.
    s = dict(name="ai-earth-chase", seed=8002, planet="planet.chondrite", ships=[sis(30), ilwrath(7)],
             ai=[None, "awesome"], earth=True, steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 1500, [lambda f, side, t: flee(f, side, t) if t < 700 else shoot(500)(f, side, t),
                                          None]))
    # Elsewhere: the Ilwrath of 22 crew cloaks, hunting the flagship.
    s = dict(name="ai-cloak", seed=8003, planet="planet.water", ships=[sis(120, pods), ilwrath(22)],
             ai=[None, "awesome"], steps=[(20, 0, 0)], full=200)
    out.append(piloted(program, s, 1500, [shoot(700), None]))
    s = dict(name="ai-cloak-chase", seed=8004, planet="planet.chondrite", ships=[sis(80), ilwrath(22)],
             ai=[None, "awesome"], steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 1500, [lambda f, side, t: flee(f, side, t) if (t // 300) % 2 == 0
                                          else shoot(400)(f, side, t), None]))
    # The ratings: GOOD_RATING aims with a margin of error of 20 pixels;
    # STANDARD_RATING 40, and never uses the special (tactical_intelligence
    # adds to its counter each frame), so never cloaks.
    for rating, seed in (("good", 8005), ("standard", 8006)):
        s = dict(name=f"ai-{rating}", seed=seed, planet="planet.water", ships=[sis(50), ilwrath(22)],
                 ai=[None, rating], steps=[(20, 0, 0)], full=150)
        out.append(piloted(program, s, 1200, [shoot(600), None]))
    # Layouts: point defence held against the hellfire, guns all round
    # spinning, tracking shots.
    s = dict(name="ai-point-defence", seed=8007, planet="planet.chondrite",
             ships=[sis(120, {**pods, 9: M_DEFENSE, 10: M_DEFENSE, 11: M_DEFENSE}), ilwrath(22)],
             ai=[None, "awesome"], steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 1200, [lambda f, side, t: B_SPECIAL | steer(f, side, the_ship(1), fire=t % 50 < 10,
                                                                                keep_off=400), None]))
    s = dict(name="ai-all-round", seed=8008, planet="planet.water",
             ships=[sis(80, {0: M_GUN, 1: M_CREW_POD, 2: M_CREW_POD, 3: M_DYNAMO, 4: M_DYNAMO, 5: M_FURNACE,
                             13: M_CANNON, 14: M_GUN, 15: M_BLASTER}, thrusters=(0, 2, 4, 6, 8, 10), jets=(1, 3, 5)),
                    ilwrath(22)],
             ai=[None, "awesome"], earth=True, steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 1200, [lambda f, side, t: B_WEAPON | (B_LEFT if (t // 40) % 2 else B_RIGHT | B_THRUST),
                                          None]))
    s = dict(name="ai-tracking", seed=8009, planet="planet.chondrite",
             ships=[sis(50, {0: M_CANNON, 2: M_CREW_POD, 4: M_TRACKING, 5: M_TRACKING, 14: M_BLASTER, 15: M_CANNON}),
                    ilwrath(15)],
             ai=[None, "awesome"], steps=[(20, 0, 0)], full=150)
    rnd = random_steps(8090, 1500, sides=1, choices=[0, B_THRUST, B_LEFT | B_THRUST, B_RIGHT | B_THRUST, B_WEAPON,
                                                     B_WEAPON | B_LEFT])
    flat = [k for n, k in rnd for _ in range(n)]
    out.append(piloted(program, s, 1500, [lambda f, side, t: flat[min(len(flat) - 1, t)], None]))
    # Retreating with the AI after it: the flagship flees (DoRunAway, in
    # ProcessInput before the AI's turn, so the AI sees it that frame:
    # the ship's mass made 100) and warps out.
    s = dict(name="ai-retreat", seed=8010, planet="planet.water", runaway=True,
             ships=[sis(40), ilwrath(22)], ai=[None, "awesome"], steps=[(20, 0, 0)], full=60)
    def retreat_when_close(f, side, t):
        ship, them = ship_of(f, 0), ship_of(f, 1)
        if t > 150 and ship and them and distance(ship, them) < 900:
            return B_ESCAPE
        return flee(f, side, t)
    s = piloted(program, s, 600, [retreat_when_close, None], chunk=2)
    out.append(dict(s, full=sum(n for n, *_ in s["steps"])))
    # Crew floating (AbandonShip, as the Syreen's song): the Ilwrath's crew
    # after it and the flagship's after the Ilwrath, the ships far apart;
    # the AI goes for crew (CREW_OBJECT_INDEX) when its enemy is far.
    s = dict(name="ai-crew", seed=8011, planet="planet.chondrite", ships=[sis(60), ilwrath(22)], ai=[None, "awesome"],
             earth=True, steps=[(20, 0, 0)], events=[(120, 1, 0, 6), (130, 0, 1, 8), (400, 1, 0, 5)], full=150)
    out.append(clamp_events(program, piloted(program, s, 1200, [flee, None])))
    # The AI flying the flagship (sis_intelligence; UQM's Cyborg option),
    # against the Ilwrath's AI: point defence, tracking and guns; and
    # against an Ilwrath cloaking under keys (the AI's margin for a
    # cloaked ship).
    s = dict(name="ai-sis-vs-ai", seed=8012, planet="planet.water",
             ships=[sis(80, {**START_MODULES, 3: M_TRACKING, 4: M_TRACKING, 9: M_DEFENSE, 10: M_DEFENSE, 13: M_CANNON,
                             14: M_BLASTER, 6: M_DYNAMO}, thrusters=range(8), jets=range(6)), ilwrath(22)],
             ai=["awesome", "awesome"], steps=[(1500, 0, 0)], full=200)
    out.append(s)
    s = dict(name="ai-sis-vs-keys", seed=8013, planet="planet.chondrite",
             ships=[sis(60, {**START_MODULES, 0: M_GUN, 9: M_DEFENSE}), ilwrath(22)], ai=["good", None],
             steps=[(20, 0, 0)], full=150)
    out.append(piloted(program, s, 1200, [None, cloaker(period=120, close=500)]))
    s = dict(name="ai-sis-standard", seed=8014, planet="planet.water",
             ships=[sis(30, {**START_MODULES, 12: M_TRACKING}, thrusters=(1, 2, 3)), ilwrath(7)],
             ai=["standard", "good"], earth=True, steps=[(1200, 0, 0)], full=100)
    out.append(s)
    # Two Ilwraths flown by the AI, both cloaking (elsewhere).
    out.append(dict(name="ai-duel", seed=8015, planet="planet.chondrite", ships=[ilwrath(22), ilwrath(22)],
                    ai=["awesome", "awesome"], steps=[(1500, 0, 0)], full=100))
    # The AI's paths only other ships take (4b's), with test ships flown by
    # it (their cyborg_control set, ship_intelligence their AI): an enemy
    # whose homing missiles are seeking weapons (dodged by enticing, and
    # fired at: PlotIntercept between two short-lived elements), against an
    # ultra-manoeuvrable ship (thrust as its top speed) and against the
    # flagship without point defence (sis_intelligence keeping after a heavy
    # missile two frames off, its enemy far off); an enemy with an
    # immediate weapon (a laser: ship_weapons from the enemy), a fleet of
    # two against the Ilwrath; and a ship with no turn or thrust wait
    # (InitCyborg's other branch).
    _, flag_values = battle_ships()
    seeking, immediate, fore = (flag_values[f] for f in ("SEEKING_WEAPON", "IMMEDIATE_WEAPON", "FIRES_FORE"))

    def homing_enemy():
        t = test_ship("ilwrath", crew=30, weapons=W_HOMING | W_MISSILE, weapon_wait=3, weapon_energy_cost=0)
        # Heavy (damage 4: sis_intelligence keeps after one that heavy) and
        # long-ranged (LONG_RANGE_WEAPON: an enemy the AI pursues).
        t.update(flags=seeking | fore, hits=4, damage=4, life=60, speed=30, weapon_range=4000)
        return t
    ultra = test_ship("ilwrath", crew=30, max_thrust=40, weapons=W_MISSILE)
    ultra["characteristics"]["thrust_increment"] = 40
    ultra.update(crew=60, max_crew=60, weapon_range=2000, maneuverability=100)
    def volleys(keep_off):
        return lambda f, side, t: steer(f, side, the_ship(1 - side), fire=t % 40 < 4, keep_off=keep_off)
    # (The stock ship_intelligence never fires a seeking weapon, which the
    # ships' own AIs fire: the missiles' ship flies by keys.)
    s = dict(name="ai-seeking-ultra", seed=8016, planet="planet.water", ships=[ultra, homing_enemy()],
             ai=["awesome", None], steps=[(20, 0, 0)], full=100)
    out.append(piloted(program, s, 1200, [None, volleys(900)]))
    s = dict(name="ai-seeking-sis", seed=8017, planet="planet.chondrite",
             ships=[sis(150, {**START_MODULES, 3: M_CREW_POD, 4: M_CREW_POD}, thrusters=(), jets=()), homing_enemy()],
             ai=["good", None], steps=[(20, 0, 0)], full=100)
    # (kept more than 16 frames off, which sis_intelligence's test needs)
    out.append(piloted(program, s, 1200, [None, volleys(1600)]))
    laser = test_ship("sis_ship", crew=30, weapons=W_LASER, weapon_wait=4)
    laser.update(flags=immediate | fore, weapon_range=400)
    out.append(dict(name="ai-immediate", seed=8018, planet="planet.water", ships=[laser, ilwrath(22)],
                    fleet=[2, 1], ai=["good", "awesome"], steps=[(1500, 0, 0)], full=100))
    still = test_ship("ilwrath", crew=20, turn_wait=0, thrust_wait=0, weapons=W_MISSILE)
    still.update(weapon_range=800)
    out.append(dict(name="ai-zero-wait", seed=8019, planet="planet.chondrite", ships=[sis(50), still],
                    ai=[None, "standard"], steps=random_steps(8190, 1000), full=100))
    rng = __import__("random").Random(4444)
    out += [random_ai_battle(f"ai-random-{i}", rng) for i in range(RANDOM_AI_BATTLES)]
    return out


def check_ai_battles(scenarios, battles):
    """Each battle flown by the AI shows what it's for."""
    by_name = {b["name"]: b for b in battles}
    scenario_by_name = {sc["name"]: sc for sc in scenarios}

    def frames(name):
        return by_name[name]["frames"]

    def fail(name, what):
        raise RuntimeError(f"{name}: {what}")

    def cloaked(f, side):
        s = ship_of(f, side)
        return s and s[E_PRIM] == STAMPFILL_PRIM and s[E_BLACK]

    def lost(name, side):
        c = [fighting(f, side)[0] if fighting(f, side) else None for f in frames(name)]
        return any(a is not None and b is not None and b < a for a, b in zip(c, c[1:]))

    def flames(name, side):
        return any(e[E_PLAYER] == side and e[E_FLAGS] & FINITE_LIFE_FLAG and not e[E_FLAGS] & PLAYER_SHIP_FLAG
                   and e[E_PRIM] == 1 for f in frames(name) for e in f[F_ELEMENTS])

    for sc in scenarios:
        name = sc["name"]
        inputs = by_name[name]["inputs"]
        for side, rating in enumerate(sc.get("ai", [None, None])):
            used = {i[side] for i in inputs[1:] if i[side] >= 0}
            if rating and not used - {0}:
                fail(name, f"the AI flying side {side} did nothing")
            if rating and len(frames(name)) < 150:
                fail(name, "the battle is too short for the AI's choices to matter")
        if sc.get("earth") and "ilwrath" in [s["ship"] for s in sc["ships"][1:]] and sc["ai"][1]:
            if any(cloaked(f, 1) for f in frames(name)) or any(i[1] >= 0 and i[1] & B_SPECIAL for i in inputs):
                fail(name, "the Ilwrath cloaked at Earth")
    for name in ("ai-earth", "ai-cloak", "ai-earth-chase", "ai-cloak-chase"):
        if not flames(name, 1):
            fail(name, "the AI never fired")
    for name in ("ai-cloak", "ai-cloak-chase", "ai-good", "ai-duel"):
        if not any(cloaked(f, 1) for f in frames(name)):
            fail(name, "the AI never cloaked")
    if not any(cloaked(f, 0) for f in frames("ai-duel")):
        fail("ai-duel", "the AI on side 0 never cloaked")
    if any(cloaked(f, 1) for f in frames("ai-standard")):
        fail("ai-standard", "the standard AI cloaked")
    if not lost("ai-earth", 0) or not lost("ai-cloak", 0):
        fail("ai-earth", "the AI's hellfire never hit")
    fr = frames("ai-retreat")
    if not by_name["ai-retreat"]["ended"] or ship_of(fr[-1], 0) \
            or not any(ship_of(f, 0) and ship_of(f, 0)[E_PRIM] == STAMPFILL_PRIM for f in fr):
        fail("ai-retreat", "the flagship didn't retreat")
    if not any(e[E_FLAGS] & CREW_OBJECT_FLAG for f in frames("ai-crew") for e in f[F_ELEMENTS]):
        fail("ai-crew", "no crew floated")
    # sis_intelligence: point defence (lasers) and shots.
    fr = frames("ai-sis-vs-ai")
    if not any(e[E_PRIM] == LINE_PRIM and e[E_PLAYER] == 0 for f in fr for e in f[F_ELEMENTS]) \
            or not flames("ai-sis-vs-ai", 0):
        fail("ai-sis-vs-ai", "the flagship's AI never used its point defence and guns")
    # The paths only other ships take: seeking missiles fired at the AI's
    # ship, lasers (an immediate weapon) at the Ilwrath by both ships of
    # side 0's fleet in turn, and a ship with no waits flown.
    for name in ("ai-seeking-ultra", "ai-seeking-sis"):
        if not flames(name, 1):
            fail(name, "the seeking missiles were never fired")
    fr = frames("ai-immediate")
    if not any(f[F_SHIPS][0][1] and f[F_SHIPS][0][1][7]
               and any(e[E_PRIM] == LINE_PRIM and e[E_PLAYER] == 0 for e in f[F_ELEMENTS]) for f in fr):
        fail("ai-immediate", "side 0's second ship never fired its laser")
    # No waits: the ship's facing changing frame after frame (a turn wait
    # holds it for a frame at least).
    # (and no thrust wait either, nor a manoeuvrability given, which
    # InitCyborg's other branch needs)
    facings = [fighting(f, 1)[2] if fighting(f, 1) else None for f in frames("ai-zero-wait")]
    still = scenario_by_name["ai-zero-wait"]["ships"][1]["characteristics"]
    if still["turn_wait"] or still["thrust_wait"] or scenario_by_name["ai-zero-wait"]["ships"][1].get("maneuverability") \
            or not any(a is not None and b is not None and c is not None and a != b != c
                       for a, b, c in zip(facings, facings[1:], facings[2:])):
        fail("ai-zero-wait", "the ship has a wait, or never turned two frames running")
    draws = {d for b in battles for f in b["frames"] for d in f[F_DRAWS]}
    for place in ("TurnShip", "Entice"):
        if place not in draws:
            fail("ai-*", f"the AI never drew a random number in {place}")


# ---- Every race's ship (stage 4b item 1) ----

# How long each race ship's duels run: against the flagship by keys, and
# the AI's against the flagship's AI; the frames kept whole (the rest are
# hashed, as the other traces' later frames).
RACE_KEYS_FRAMES, RACE_AI_FRAMES, RACE_FULL = 720, 900, 60
# How many seeds each battle may try (race_battle_scenarios).
RACE_SEEDS = 40
# The keys' phases (frames each): the weapon pressed in pulses, then the
# special, then both at once (the Orz's marines take both).
RACE_PHASE = 40


def race(ship_id, crew=None):
    """A race ship (its own ships/RACE/RACE.c, init_RACE), its STARSHIP's
    crew_level crew (spawn_ship holds the RACE_DESC's to it), its
    description's starting crew without."""
    ships, _ = battle_ships()
    return dict(ship=ship_id, kind="race", crew=ships[ship_id]["crew"] if crew is None else crew)


def race_program_sheets(part):
    """The sheets the battle program loads for a part's race ships,
    numbered: the flagship's ship and weapon sheets and its status icons
    (the VUX's limpets test them), each race ship's ship, weapon and
    special sheets as load_ship loads them (a size without its own is the
    one before's), its icons and captain's window (the Shofixti's
    self-destruct counts its frames), the arena's asteroids, blasts and
    explosions and the battles' planets at three sizes, and the stars."""
    ships, _ = battle_ships()
    g = ships["sis_ship"]["graphics"]
    ids = g["ship"] + g["weapon"] + [g["icons"]]
    for ship in part:
        g = ships[ship]["graphics"]
        for k in ("ship", "weapon", "special"):
            ids += [r for r in g[k] if r]
        ids += [g["icons"], g["captain"]]
        if ship == "shofixti":
            ids += OLD_SCOUT + [OLD_CAPTAIN]   # Tanaka's damaged scout (shofixti-old-*)
    for base in ["graphics.asteroid", "graphics.blast", "graphics.boom"] + BATTLE_PLANETS:
        ids += [f"{base}.large", f"{base}.medium", f"{base}.small"]
    ids.append("graphics.stars")
    return list(dict.fromkeys(ids))


def race_pilot(keep_off=400, goal=None, special_goal=None, specials_from=0, weapon_until=None):
    """A race ship's keys: it flies at the flagship (or goal), keeping off
    keep_off, and in turn presses its weapon in pulses for RACE_PHASE
    frames, its special (flying at special_goal meanwhile, if given), then
    both at once; the special not before specials_from frames, the weapon
    not after weapon_until."""
    def pilot(f, side, t):
        phase = (t // RACE_PHASE) % 3
        target = special_goal if phase == 1 and special_goal else goal or the_ship(1 - side)
        keys = steer(f, side, target, keep_off=keep_off)
        pulse = (t // 4) % 2 == 0
        if pulse and phase in (0, 2) and (weapon_until is None or t < weapon_until):
            keys |= B_WEAPON
        if pulse and phase in (1, 2) and t >= specials_from:
            keys |= B_SPECIAL
        return keys
    return pilot


def flagship_pilot(keep_off=600):
    """The flagship's keys against a race ship: at it, firing in bursts."""
    return lambda f, side, t: steer(f, side, the_ship(1 - side), fire=t % 40 < 6, keep_off=keep_off)


# What each race ship's keys need beyond race_pilot's defaults: how close
# it keeps (the Syreen's song and the Zoq-Fot-Pik's tongue reach only so
# far), and the rest.
RACE_PILOTS = {
    "syreen": dict(keep_off=250),
    "zoqfot": dict(keep_off=150),
    "androsyn": dict(keep_off=200),
    "chmmr": dict(keep_off=500),
    # The Slylandro's special harvests asteroids near it.
    "slylandr": dict(keep_off=300, special_goal=nearest_asteroid(1)),
    # The Shofixti's self-destruct ends it: after the weapon's phase, and a
    # second Shofixti comes in (fleet).
    "shofixti": dict(keep_off=300),
    # The Mycon regrows crew only with all its energy and crew lost: its
    # weapon only for the first round, then it takes hits and regrows.
    "mycon": dict(weapon_until=3 * RACE_PHASE),
}
# A fleet of more than one for a ship whose special ends it.
RACE_FLEETS = {"shofixti": [1, 2]}


def race_flagship(keys=False):
    """The flagship each race ship fights: a new game's with more crew
    pods, and for the AI a blaster and a tracking system too (by keys, its
    gun alone, so the race ship lives to use its special)."""
    if keys:
        return sis(100, {**START_MODULES, 3: M_CREW_POD, 4: M_CREW_POD})
    return sis(100, {**START_MODULES, 3: M_CREW_POD, 4: M_CREW_POD, 12: M_TRACKING, 14: M_BLASTER})


def race_battle_scenarios(program, part, seed):
    """Each race ship of the part: against the flagship by keys, using its
    weapon and special (race_pilot), and flown by UQM's AI (awesome, the
    story's enemy's) against the flagship flown by the AI too (sis_intelligence)."""
    out = []
    # Tanaka's damaged scout (the Shofixti without SHOFIXTI_RECRUITED) after
    # the recruited Shofixti, as a ship of its own (shofixti-old).
    flown = [(n, ship, ship) for n, ship in enumerate(part)]
    if "shofixti" in part:
        at = part.index("shofixti")
        flown.insert(at + 1, (at, "shofixti", "shofixti-old"))
    for n, ship, label in flown:
        opts = RACE_PILOTS.get(ship, {})
        planet = BATTLE_PLANETS[n % 2]
        extra = dict(shofixti_recruited=True) if label == "shofixti" else {}
        if ship in RACE_FLEETS:
            extra["fleet"] = RACE_FLEETS[ship]
        # The keys' battle: the first seed (from seed + 2n, a thousand
        # apart) in which the weapon and the special each have an effect
        # (key_effects; a small ship may die before its special's turn).
        for k in range(RACE_SEEDS):
            s = dict(name=f"{label}-keys", seed=seed + 2 * n + 1000 * k + (500 if label != ship else 0), planet=planet,
                     ships=[race_flagship(keys=True), race(ship)], steps=[(20, 0, 0)], full=RACE_FULL, **extra)
            s = piloted(program, s, RACE_KEYS_FRAMES, [flagship_pilot(), race_pilot(**opts)], chunk=6)
            if all(key_effects(program, s, program.run([s], colours=True)[0]).values()):
                break
        out.append(s)
        # The AI's battle: the first seed (from seed + 2n + 1, a thousand
        # apart) in which the race ship's AI uses both its weapon and its
        # special to some effect (some, as the Arilou's teleport, only when
        # a shot is about to hit).
        for k in range(RACE_SEEDS):
            s = dict(name=f"{label}-ai", seed=seed + 2 * n + 1 + 1000 * k + (500 if label != ship else 0),
                     planet=BATTLE_PLANETS[(n + 1) % 2],
                     ships=[race_flagship(), race(ship)], ai=["awesome", "awesome"],
                     steps=[(RACE_AI_FRAMES, 0, 0)], full=RACE_FULL, **extra)
            used = key_effects(program, s, program.run([s], colours=True)[0])
            if all(t is not None for what, t in used.items() if what in race_ai_uses(s["name"])):
                break
        out.append(s)
        if ship == "pkunk":
            # The Pkunk reborn (pkunk_preprocess's even chance, new_pkunk):
            # one of little crew flying at the flagship, the first seed in
            # which it dies and comes back.
            for k in range(RACE_SEEDS):
                s = dict(name="pkunk-rebirth", seed=seed + 50 + 1000 * k, planet=planet,
                         ships=[race_flagship(), race(ship, crew=2)], steps=[(20, 0, 0)], full=RACE_FULL)
                s = piloted(program, s, RACE_KEYS_FRAMES,
                            [flagship_pilot(keep_off=300), race_pilot(keep_off=100)], chunk=6)
                if reborn(program.run([s])[0]):
                    break
            out.append(s)
    return out


def reborn(battle):
    """Whether a Pkunk was reborn in the battle (new_pkunk drew its place)."""
    return any("new_pkunk" in f[F_DRAWS] for f in battle["frames"])


def without_status_keys(frame, side):
    """The frame with side's ships' status flags' WEAPON and SPECIAL bits
    cleared (they are the presses themselves, which ship_preprocess copies
    from the input, effect or not), and LOW_ON_ENERGY (ship_postprocess
    sets it for a press without the energy, to play a sound), and each
    element's handle and its target's (E_SLOT, E_TARGET) given as places
    in the list instead: a press that only allocates a helper element that
    dies within the frame (the Earthling's spawn_point_defense without the
    energy to fire) shifts the handles later elements get, which is no
    effect. As JSON for comparing."""
    ships = [list(map(list, (s if s else [] for s in side_ships))) for side_ships in frame[F_SHIPS]]
    for s in ships[side]:
        if s:
            s[6] &= ~(B_WEAPON | B_SPECIAL | LOW_ON_ENERGY_FLAG)
    place = {e[E_SLOT]: i + 1 for i, e in enumerate(frame[F_ELEMENTS])}
    elements = []
    for e in frame[F_ELEMENTS]:
        e = list(e)
        e[E_SLOT] = place[e[E_SLOT]]
        e[E_TARGET] = place.get(e[E_TARGET], -1) if e[E_TARGET] else 0
        elements.append(e)
    return json.dumps(frame[:F_SHIPS] + [ships, elements] + frame[F_ELEMENTS + 1:])


def key_effect(program, scenario, battle, key):
    """The first frame in which side 1's key has an effect, the weapon
    pressed without the special (the Orz's both are its marines, the
    special's; the special counts with the weapon or without, as the
    Druuge's AI burns crew only as it fires): the
    same battle with those presses taken away (battle.c's mask, the keys'
    or the AI's) first differs there, with the same input up to and in that
    frame (a battle whose AI decides otherwise first, having seen its own
    last input differ, shows nothing) and the status flags' presses aside
    (without_status_keys). None if it never has one."""
    other = B_SPECIAL if key == B_WEAPON else 0
    without = program.run([dict(scenario, mask=[[0, 0], [key, other]])], colours=True)[0]

    def masked(i):
        return [i[0], i[1] & ~key if i[1] > 0 and not i[1] & other else i[1]]
    a, b = battle["frames"], without["frames"]
    for t in range(max(len(a), len(b))):
        if t >= len(a) or t >= len(b):
            return t   # one ended sooner, its inputs the same so far
        same_input = masked(battle["inputs"][t]) == without["inputs"][t]
        if without_status_keys(a[t], 1) != without_status_keys(b[t], 1):
            return t if same_input else None
        if not same_input:
            return None
    return None


def key_effects(program, scenario, battle):
    """{"weapon": frame, "special": frame}: the first frame each has an
    effect (key_effect), None for never; never before frame 2 (frame 1's
    input comes as the ship warps in)."""
    out = {}
    for key, what in ((B_WEAPON, "weapon"), (B_SPECIAL, "special")):
        t = key_effect(program, scenario, battle, key)
        out[what] = t if t is not None and t >= 2 else None
    return out


def race_ai_uses(name):
    """What a race ship's AI battle must use to some effect: its weapon and
    its special, but Tanaka's damaged scout's AI never presses its special
    (shofixti_intelligence presses it only with a special sheet, which the
    damaged scout hasn't: init_shofixti's NULL_RESOURCE)."""
    return ("weapon",) if name == "shofixti-old-ai" else ("weapon", "special")


def check_race_battles(program, scenarios, battles):
    """Each race ship's duels show what they're for: by keys and by the
    AI, its weapon and its special each have an effect (key_effects); the
    Pkunk's rebirth battle, the rebirth. Returns each battle's key_effects
    (None for the rebirth's)."""
    def fail(name, why):
        raise RuntimeError(f"race battle {name}: {why}")
    effects = []
    for scenario, battle in zip(scenarios, battles):
        name = scenario["name"]
        if name == "pkunk-rebirth":
            if not reborn(battle):
                fail(name, "the Pkunk wasn't reborn")
            effects.append(None)
            continue
        used = key_effects(program, scenario, battle)
        for what, t in used.items():
            if t is None and (not name.endswith("-ai") or what in race_ai_uses(name)):
                fail(name, f"its {what} never had an effect")
        effects.append(used)
    return effects


def run_race_battles(program, scenarios):
    """The battles, each in a program run of its own."""
    return [program.run([s], colours=True)[0] for s in scenarios]


def pkunk_sound_count(strings_exe, package):
    """The Pkunk's sounds (ship.pkunk.sounds, a sound bank: each line of its
    .snd file a sound), which GetSoundCount counts for pkunk_postprocess."""
    import zipfile
    with tempfile.TemporaryDirectory() as tmp, zipfile.ZipFile(package) as z:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        path = index["ship.pkunk.sounds"][1]
        folder = path.rsplit("/", 1)[0] + "/"
        names = [line.strip() for line in z.read(path).decode().splitlines() if line.strip()]
        for name in names:
            z.getinfo(folder + name)   # each sound is there
    return len(names)


def record_battle_races(uqm, battle_exe, strings_exe, images_exe, package):
    """Every race ship (stage 4b) in UQM's own battle engine (battle.c),
    with its own code and AI: one file for each part of the plan."""
    sounds = pkunk_sound_count(strings_exe, package)
    files = {}
    for n, part in enumerate(RACE_SHIP_PARTS, 1):
        ids = race_program_sheets(part)
        with tempfile.TemporaryDirectory() as tmp:
            masks, hashes = write_masks(strings_exe, images_exe, package, tmp, list(enumerate(ids)))
            sheets_file = os.path.join(tmp, "sheets.txt")
            with open(sheets_file, "w") as f:
                for i, key in enumerate(ids):
                    f.write(f"{i} {key} {len(hashes[key])}\n")
            program = BattleProgram(battle_exe, sheets_file, masks, ids=ids, sounds=sounds)
            scenarios = race_battle_scenarios(program, part, 9000 + 100 * n)
            battles = run_race_battles(program, scenarios)
            if run_race_battles(program, scenarios) != battles:
                raise RuntimeError(f"race battles part {n}: recorded twice, they differ")
            effects = check_race_battles(program, scenarios, battles)
        out = []
        for scenario, battle, uses in zip(scenarios, battles, effects):
            full = scenario["full"]
            frames = [f if i <= full else f[:F_SHIPS] + [len(f[F_ELEMENTS]), battle_frame_hash(f)]
                      for i, f in enumerate(battle["frames"])]
            inputs = {k: v for k, v in scenario.items() if k not in ("name", "full")}
            out.append({"name": scenario["name"], "input": inputs, "full": full, "ended": battle["ended"],
                        "crew_enlisted": battle["crew_enlisted"], "bomb_carrier": battle["bomb_carrier"],
                        "stars": battle["stars"], "inputs": battle["inputs"], "frames": frames,
                        "crew_floating": battle["crew_floating"], "freed": battle["freed"]})
            if uses:
                out[-1]["uses"] = uses
        data = header(uqm, ["tools/extract/golden/battle.c", "tools/extract/golden/battle_stubs.c"]
                      + [f"src/{f}" for f in BATTLE_LINK] + ["src/uqm/battle.c", "src/uqm/galaxy.c",
                                                             "src/libs/math/random.c"], [
            f"Part {n} of stage 4b's race ships (docs/plans/stage-4b.md, item {n + 1}): "
            f"{', '.join(part)}, each with its own code (src/uqm/ships/RACE/RACE.c: "
            "init_RACE's RACE_DESC, its preprocess, postprocess, weapon and "
            "intelligence functions), in UQM 0.8.0's own battle engine, recorded by "
            "the program battle.json, battle-ships.json and battle-ai.json are "
            "(tools/extract/golden/battle.c; see their notes for the engine, the "
            "sheets, the masks, the flagship, the AI and the frames), as an "
            "encounter runs a battle. Each battle is a program run of its own, so "
            "nothing a ship's code keeps between battles carries over (pkunk.c's "
            "last insult needn't: init_pkunk resets it whenever a Pkunk is made).",
            "Each race ship has two battles: RACE-keys, the race ship (side 1) "
            "flown by keys against the flagship (side 0, keys; a new game's "
            "with two more crew pods, firing its gun in bursts): it flies at the "
            "flagship and in turn presses its weapon in pulses, its special, then "
            "both at once, each for "
            f"{RACE_PHASE} frames (the Slylandro flies at an asteroid while it "
            "presses its special, which harvests them); and RACE-ai, the race "
            "ship flown by UQM's AI (awesome, the story's enemy's: its own "
            "intelligence_func on cyborg.c) against the flagship flown by the AI "
            "(sis_intelligence, UQM's Cyborg option; a blaster and a tracking "
            "system too). Each takes the first seed (a thousand apart) in which "
            "its ship's weapon and special each have an effect. The weapon "
            "counts only when pressed without the special, since the Orz's "
            "two pressed together launch its marines, which are its special; "
            "the special counts with or without the weapon. A key has an "
            "effect when the battle with those presses taken away (the keys' "
            "or the AI's, battle.c's mask) differs, with the same input until "
            "it does. The comparison leaves out the ship's status flags' weapon "
            "and special bits (the presses themselves) and LOW_ON_ENERGY (a "
            "press without the energy), and takes each element's handle and "
            "its target's as places in the list (a helper allocated and gone "
            "within a frame only shifts later handles). Never before frame 2 (frame 1's input comes as the "
            "ship warps in). uses: the first frame each has one. The Shofixti (recruited) "
            "has a second ship (fleet), as its self-destruct ends the first; "
            "pkunk-rebirth is a Pkunk of 2 crew by keys, the first seed in which "
            "it dies and is reborn (new_pkunk). Checked when recorded: recorded "
            "twice, they're the same, and each shows what it's for.",
            "input: as battle-ai.json's, and a race ship is kind race: ship "
            "(data/ships.json's id) and crew (its STARSHIP's crew_level, which "
            "spawn_ship holds its RACE_DESC's crew to, at most its max_crew); "
            "shofixti_recruited: SHOFIXTI_RECRUITED set (without it an "
            "encounter's Shofixti is Tanaka's damaged scout, init_shofixti). "
            "The race ship's sheets are its RACE_DESC's as load_ship loads them "
            "(load_animation: a size without its own is the size before's), "
            "and its status icons and captain's window: the Shofixti's "
            "self-destruct counts the captain window's frames "
            "(captain_control.special, set by DrawCaptainsWindow), and the "
            "VUX's limpets test the hit ship's icons' frame 1 "
            "(ModifySilhouette, which draws random numbers to place them). "
            f"GetSoundCount of the Pkunk's sounds is the content's ({sounds}): "
            "pkunk_postprocess draws insults until one differs from the last.",
            "uses: {weapon, special}, the frame in which each first has an effect "
            "(not in pkunk-rebirth). "
            "inputs, frames, crew_enlisted, bomb_carrier, crew_floating and "
            "freed: as battle-ai.json's. The frames' draws name UQM's "
            "functions, the ships' own static ones among them.",
        ])
        data["sheets"] = ids
        data["battles"] = out
        files[f"battle-races-{n}"] = data
    return files


# ---- The battle's drawing (stage 4a item 12) ----

def battle_draw_sheets():
    """The draw program's sheets: the battle program's, then each ported
    ship's captain's window and status icons, and the status panel's own
    frames (graphics.status, setup.c's StatusFrame)."""
    ships, _ = battle_ships()
    ids = battle_program_sheets()
    for ship in BATTLE_SHIPS:
        g = ships[ship]["graphics"]
        ids += [g["captain"], g["icons"]]
    return ids + ["graphics.status"]


class BattleDrawProgram(BattleProgram):
    """The battle program built to record its drawing (-DRECORD_DRAW)."""

    def __init__(self, exe, sheets_file, masks):
        super().__init__(exe, sheets_file, masks)
        self.ids = battle_draw_sheets()
        self.numbers = {r: n for n, r in enumerate(self.ids)}


def battle_draw_scenarios(program):
    """Fights to draw: the flagship (a new game's, with point defence)
    against the Ilwrath coming at it, firing, cloaking, until one is
    destroyed (its captain's window flashing as it explodes), at the smooth
    zoom (UQM's default); and a short one at the step zoom."""
    shoot = lambda keep_off: lambda f, side, t: steer(f, side, the_ship(1 - side), fire=True, keep_off=keep_off)
    defend = lambda f, side, t: shoot(500)(f, side, t) | (B_SPECIAL if t % 30 < 2 else 0)
    out = []
    s = dict(name="draw-fight", seed=7101, planet="planet.water",
             ships=[sis(20, {1: M_FUEL_TANK, 2: M_CREW_POD, 4: M_DEFENSE, 8: M_STORAGE_BAY, 15: M_GUN}),
                    ilwrath(12)], steps=[(20, 0, 0)], full=0)
    out.append(piloted(program, s, 700, [defend, cloaker(period=120, close=500)]))
    s = dict(name="draw-step", seed=7102, planet="planet.chondrite", zoom="step",
             ships=[sis(30), ilwrath(22)], steps=[(20, 0, 0)], full=0)
    out.append(piloted(program, s, 160, [shoot(600), cloaker(period=60)]))
    return out


def record_battle_draw(uqm, battle_exe, draw_exe, strings_exe, images_exe, package):
    """What UQM draws in a battle (battle.c built with RECORD_DRAW): each
    frame's status panel calls and the space's primitives."""
    ids = battle_draw_sheets()
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, list(enumerate(ids)))
        sheets_file = os.path.join(tmp, "sheets.txt")
        with open(sheets_file, "w") as f:
            for n, key in enumerate(ids):
                f.write(f"{n} {key} {len(hashes[key])}\n")
        pilot = BattleProgram(battle_exe, sheets_file, masks)
        scenarios = battle_draw_scenarios(pilot)
        battles = BattleDrawProgram(draw_exe, sheets_file, masks).run(scenarios, colours=True)
    out = []
    for scenario, battle in zip(scenarios, battles):
        inputs = {k: v for k, v in scenario.items() if k not in ("name", "full")}
        if len(battle["drawn"]) != len(battle["frames"]):
            raise RuntimeError(f"{scenario['name']}: {len(battle['drawn'])} frames drawn, {len(battle['frames'])} run")
        out.append({"name": scenario["name"], "input": inputs, "ended": battle["ended"], "stars": battle["stars"],
                    "views": [f[:F_SHIPS] for f in battle["frames"]], "drawn": battle["drawn"]})
    data = header(uqm, ["tools/extract/golden/battle.c", "tools/extract/golden/battle_stubs.c"]
                  + [f"src/{f}" for f in BATTLE_LINK] + ["src/uqm/battle.c", "src/uqm/galaxy.c",
                                                         "src/libs/graphics/frame.c"], [
        "What UQM 0.8.0 draws in a battle, recorded by the battle program "
        "(tools/extract/golden/battle.c; battle.json's notes say how it runs "
        "a battle) built with RECORD_DRAW: its drawing calls are recorded "
        "rather than stubbed. The battles are the flagship (sis_ship.c) "
        "against the Ilwrath (ilwrath.c), flown by pilots (their keys "
        "recorded in input, as battle-ships.json's), with the captain's "
        "windows and status icons loaded (load_ship's battle data).",
        "sheets: the battle program's (battle.json), then the captains' "
        "windows and icons and graphics.status (StatusFrame), numbered.",
        "views: each frame's [seed, scroll x, y, zoom_out, view (0 stable, 1 "
        "scrolled, 2 zoomed, -1 the first), SpaceOrg x, y], as battle.json's "
        "frames begin.",
        "drawn: each frame's [status, space]. status: the status panel's "
        "calls in StatusContext as status.c and shipstat.c make them, in its "
        "own coordinates, in order: [\"s\", sheet, frame, x, y] DrawStamp, "
        "[\"f\", sheet, frame, x, y, r, g, b] DrawFilledStamp, [\"r\", x, y, w, "
        "h, r, g, b] DrawFilledRectangle, [\"o\", ...] DrawRectangle, "
        "[\"t\", font, x, y, align, text, r, g, b] font_DrawText (fonts named; "
        "a ship's race strings read \"string N\", N the table's index; the "
        "flagship's names Vindicator and Zelnick; optWhichMenu OPT_PC, UQM's "
        "default); a sheet of -1 is no frame. "
        "space: DrawBatch's primitives as it draws the display list, in "
        "order, each [type (0 point, 1 stamp, 2 filled stamp, 3 line), x, y, "
        "a line's second x, y (else 0), sheet, frame (else -1), colour "
        "(RGB15, -1 never set)], at the display coordinates PostProcessQueue "
        "and MoveGalaxy gave them; then the graphic scale it's drawn at "
        "(SetGraphicScale: 0 none).",
    ])
    data["sheets"] = ids
    data["battles"] = out
    return data


# Every race ship's views (stage 4b item 10): its keys battle drawn.
RACE_DRAW_SPACE_EVERY = 60


def record_battle_races_draw(uqm, draw_exe, strings_exe, images_exe, package):
    """What UQM draws in each race ship's battle by keys (battle-races-N's
    RACE-keys, and pkunk-rebirth), replayed from its recorded input in the
    draw build: each frame's status panel, and the space at every
    RACE_DRAW_SPACE_EVERY-th frame."""
    ships, _ = battle_ships()
    out = []
    for n, part in enumerate(RACE_SHIP_PARTS, 1):
        with open(os.path.join(REPO, "tests", "golden", f"battle-races-{n}.json")) as f:
            recorded = json.load(f)
        ids = list(dict.fromkeys(race_program_sheets(part) + [ships["sis_ship"]["graphics"]["captain"],
                                                             "graphics.status"]))
        scenarios = [dict(name=b["name"], full=0, **b["input"]) for b in recorded["battles"]
                     if b["name"].endswith("-keys") or b["name"] == "pkunk-rebirth"]
        with tempfile.TemporaryDirectory() as tmp:
            masks, hashes = write_masks(strings_exe, images_exe, package, tmp, list(enumerate(ids)))
            sheets_file = os.path.join(tmp, "sheets.txt")
            with open(sheets_file, "w") as f:
                for i, key in enumerate(ids):
                    f.write(f"{i} {key} {len(hashes[key])}\n")
            program = BattleProgram(draw_exe, sheets_file, masks, ids=ids, sounds=pkunk_sound_count(strings_exe, package))
            battles = [program.run([s], colours=True)[0] for s in scenarios]
        traced = {b["name"]: b for b in recorded["battles"]}
        for scenario, battle in zip(scenarios, battles):
            name = scenario["name"]
            if len(battle["drawn"]) != len(battle["frames"]):
                raise RuntimeError(f"{name}: {len(battle['drawn'])} frames drawn, {len(battle['frames'])} run")
            # The same battle as battle-races-N's: its inputs, and every frame as that file keeps it (whole up to its
            # full, then the frame's ships with its elements' count and hash).
            full = traced[name]["full"]
            frames = [f if i <= full else f[:F_SHIPS] + [len(f[F_ELEMENTS]), battle_frame_hash(f)]
                      for i, f in enumerate(battle["frames"])]
            if battle["inputs"] != traced[name]["inputs"] or frames != traced[name]["frames"]:
                raise RuntimeError(f"{name}: the draw build's battle isn't battle-races-{n}'s")
            drawn = [[status, space if t % RACE_DRAW_SPACE_EVERY == 0 else None]
                     for t, (status, space) in enumerate(battle["drawn"])]
            out.append({"name": name, "part": n, "sheets": ids, "drawn": drawn})
    data = header(uqm, ["tools/extract/golden/battle.c", "tools/extract/golden/battle_stubs.c"]
                  + [f"src/{f}" for f in BATTLE_LINK] + ["src/uqm/battle.c", "src/uqm/galaxy.c",
                                                         "src/libs/graphics/frame.c"], [
        "What UQM 0.8.0 draws in each race ship's battle by keys (stage 4b "
        "item 10): battle-races-1.json to -3.json's RACE-keys battles and "
        "pkunk-rebirth, each run again from its recorded input in the battle "
        "program built with RECORD_DRAW (battle-draw.json's notes), checked "
        "to run the same battle (the same inputs, and every frame as that "
        "file keeps it).",
        "part: the battle-races file the battle is from. sheets: that part's "
        "battle program's sheets, then the flagship's captain's window and "
        "graphics.status, numbered.",
        "drawn: each frame's [status, space] "
        "as battle-draw.json's, the space only at every "
        f"{RACE_DRAW_SPACE_EVERY}th frame (null otherwise). A status call "
        "drawn clipped (InitShipStatus with a rectangle: ModifySilhouette's "
        "MODIFY_SWAP, the Orz's marines leaving a place) ends with the clip "
        "rectangle, [x, y, w, h] in the panel's coordinates.",
    ])
    data["battles"] = out
    return data


def record_battle_ai(uqm, battle_exe, strings_exe, images_exe, package):
    """The Ilwrath (and the flagship) flown by UQM's own AI (intel.c,
    cyborg.c, ilwrath_intelligence, sis_intelligence) in UQM's own battle
    engine (battle.c)."""
    ids = battle_program_sheets()
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, list(enumerate(ids)))
        sheets_file = os.path.join(tmp, "sheets.txt")
        with open(sheets_file, "w") as f:
            for n, key in enumerate(ids):
                f.write(f"{n} {key} {len(hashes[key])}\n")
        program = BattleProgram(battle_exe, sheets_file, masks)
        scenarios = ai_battle_scenarios(program)
        battles = program.run(scenarios, colours=True)
    check_ai_battles(scenarios, battles)
    out = []
    for scenario, battle in zip(scenarios, battles):
        full = scenario["full"]
        frames = [f if i <= full else f[:F_SHIPS] + [len(f[F_ELEMENTS]), battle_frame_hash(f)]
                  for i, f in enumerate(battle["frames"])]
        inputs = {k: v for k, v in scenario.items() if k not in ("name", "full")}
        out.append({"name": scenario["name"], "input": inputs, "full": full, "ended": battle["ended"],
                    "crew_enlisted": battle["crew_enlisted"], "bomb_carrier": battle["bomb_carrier"],
                    "stars": battle["stars"], "inputs": battle["inputs"], "frames": frames,
                    "crew_floating": battle["crew_floating"], "freed": battle["freed"]})
    data = header(uqm, ["tools/extract/golden/battle.c", "tools/extract/golden/battle_stubs.c"]
                  + [f"src/{f}" for f in BATTLE_LINK] + ["src/uqm/battle.c", "src/uqm/galaxy.c",
                                                         "src/libs/math/random.c"], [
        "UQM 0.8.0's own AI (intel.c's computer_intelligence, cyborg.c's "
        "tactical_intelligence, ship_intelligence and the rest, and the "
        "ships' own intelligence_func: ilwrath.c's ilwrath_intelligence, "
        "sis_ship.c's sis_intelligence) flying ships in its own battle "
        "engine, recorded by the program battle.json and battle-ships.json "
        "are (tools/extract/golden/battle.c; see their notes for the engine, "
        "the sheets, the masks, the ships and the frames), as an encounter "
        "runs a battle: battle.c's ProcessInput calls each side's frameInput "
        "in turn, side 0's ships first, and a side the AI flies has intel.c's "
        "computer_intelligence for it, as battlecontrols.c's computer "
        "handlers have.",
        "input: as battle-ships.json's, and ai: who flies each side, null the "
        "keys (the steps' keys for a side the AI flies are ignored), or the "
        "AI at a rating (PlayerControl: standard STANDARD_RATING, good "
        "GOOD_RATING, awesome AWESOME_RATING, the story's enemy's, restart.c; "
        "side 1 COMPUTER_CONTROL, side 0 CYBORG_CONTROL as UQM's Cyborg option "
        "makes it); earth: PROBE_ILWRATH_ENCOUNTER set, with IN_ENCOUNTER the "
        "Earth fight's rule, in which ilwrath_intelligence doesn't cloak. "
        "A test ship (battle.json's) has its cyborg_control too, weapon_range "
        "and maneuverability (WeaponRange, ManeuverabilityIndex; absent, 0), "
        "and UQM's stock ship_intelligence as its intelligence_func, so the "
        "AI can fly it.",
        "inputs: each frame's BATTLE_INPUT_STATE for each side as its "
        "frameInput returned it (1 left, 2 right, 4 thrust, 8 weapon, 16 "
        "special), -1 where it wasn't called (no ship with an element; the "
        "first frame, before DoBattle). frames: as battle-ships.json's; the "
        "AI's random draws (TurnShip turning a half circle, Entice dodging) "
        "are among each frame's draws, named by UQM's function. "
        "crew_enlisted and bomb_carrier: as battle-ships.json's.",
        "crew_floating: the crew floating in space as the battle stopped "
        "(CountCrewElements); freed: each side's STARSHIPs' crew_level, in "
        "its queue's order, after Battle's UninitShips gave that crew to the "
        "ships left with crew and recorded each ship's crew (a ship never "
        "picked keeps the crew it came with). Added later, at the end.",
    ])
    data["sheets"] = ids
    data["battles"] = out
    return data


def record_contact(uqm, systems_exe, strings_exe, images_exe, package):
    """Contact by sprite shape, with the package's masks: the masks
    (content, so hashes), UQM's own DrawablesIntersect on many cases, and
    the interplanetary flights. Returns {"sprite-masks", "intersections",
    "ip-flight"}."""
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp)
        cases = subprocess.run([systems_exe, "intersect", "-k", masks],
                               capture_output=True, text=True, check=True).stdout
        flight = subprocess.run([systems_exe, "flight", "-k", masks],
                                capture_output=True, text=True, check=True).stdout
    out = {}
    data = header(uqm, ["tools/extract/golden/images.c",
                        "src/libs/graphics/gfxload.c",
                        "src/libs/graphics/sdl/canvas.c",
                        "src/libs/graphics/tfb_draw.c",
                        "src/libs/graphics/sdl/sdl_common.c",
                        "src/libs/graphics/sdl/sdl1_common.c",
                        "src/libs/graphics/sdl/sdl2_common.c",
                        "src/libs/graphics/sdl/pure.c",
                        "src/libs/graphics/sdl/sdl2_pure.c",
                        "src/libs/graphics/sdl/opengl.c"], [
        "The collision masks of the sprite sheets UQM tests for contact, "
        f"from the official content package (SHA-256 {CONTENT_SHA256}): "
        "each frame as _GetCelData loads it, read by UQM's own PNG loader, "
        "with its colour key set as process_image sets it, and a pixel able "
        "to touch as TFB_DrawCanvas_Intersect decides it. By resource id, "
        "each frame is [width, height, hot spot x, hot spot y, SHA-256 of "
        "the mask: a byte a pixel, row by row, 1 where it can touch]. A "
        "true colour frame without alpha (only the orbit background's last, "
        "never tested for contact) is converted to the screen's 32 bits "
        "first, as TFB_DrawImage_New converts it, which the images program "
        "models (its SDL stand-in has no conversion). Content, so hashes "
        "only.",
    ])
    data["sheets"] = hashes
    out["sprite-masks"] = data
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "src/libs/graphics/intersec.c",
                        "src/libs/graphics/boxint.c"], [
        "UQM's own DrawablesIntersect (intersec.c, with BoxIntersect from "
        "boxint.c) on cases chosen by the systems program's intersect mode, "
        "with the masks of sprite-masks.json: pairs of frames UQM tests for "
        "contact (and a few others) placed near each other, still or "
        "moving, with a time limit.",
        "Sheets: 1 graphics.flagship, 2 graphics.planets, 3 "
        "graphics.orbitbackground, 4 graphics.stars, 6 "
        "ship.flagship.graphics.hyperspace, 7 graphics.hyperstars, 8 "
        "graphics.ambient.",
        "cases: [sheet0, frame0, origin x0, y0, end point x0, y0, sheet1, "
        "frame1, origin x1, y1, end point x1, y1, max time, result, then "
        "after the call control 0's end point x, y and last_time_val, and "
        "control 1's].",
        "Positions and times from code and the masks' shapes; no content.",
    ])
    data.update(json.loads(cases))
    out["intersections"] = data
    out["ip-flight"] = ip_flight_data(uqm, flight)
    return out


def record_planet_surfaces(uqm, systems_exe, strings_exe, images_exe=None,
                           package=None):
    """Every world's surface as UQM's own GeneratePlanetSurface makes it:
    the height maps (code only), and with the package the coloured maps
    and rotating planets (content, so hashes only). One run of the
    surfaces mode gives all of them, hashed by the program itself.
    Returns {"surface-heights": ..., "surface-colors": ... and
    "planet-spheres": ... (with the package)}."""
    heights, full, colors, spheres = [], [], [], []
    with tempfile.TemporaryDirectory() as tmp:
        tables = planet_tables(strings_exe, images_exe, package, tmp) if package else None
        for (star, planet, moon, entered, defined, topo, colored, light,
             frames, shield, whole) in surface_lines(systems_exe, tables, FULL_HEIGHT_MAPS):
            # Earth's maps come from its images, content: not in the code-only file.
            heights.append([star, planet, moon, entered, defined,
                            None if defined else topo or None,
                            None if defined else light or None])
            if frames:
                spheres.append([star, planet, moon,
                                [[step, rgba] for step, rgba in frames], shield or None])
            if whole:
                full.append({"world": [star, planet, moon], "heights": whole})
            colors.append([star, planet, moon, colored or None])
    if len(full) != len(FULL_HEIGHT_MAPS):
        raise RuntimeError(f"only {len(full)} whole height maps")
    out = {}
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "tools/extract/golden/stubs.c",
                        "tools/extract/golden/sha256.h",
                        "src/uqm/planets/plangen.c",
                        "src/uqm/planets/gentopo.c"], [
        "Every world's height map as UQM 0.8.0's own GeneratePlanetSurface "
        "(plangen.c, gentopo.c) makes it on entering orbit from a new "
        "game, taken just before it is coloured. Code only, no content.",
        "worlds: [star, planet, moon or -1, orbit entered, defined (Earth, "
        "whose surface is an image), SHA-256 of the MAP_WIDTH x MAP_HEIGHT "
        "signed bytes, row by row, or null, SHA-256 of the light map "
        "GenerateLightMap makes of it (all zeros for a gas giant) or null]. "
        "Earth's maps are content (its images), so null here; its sphere is "
        "in planet-spheres.json.",
        "full: the whole height map of a few worlds, in hex.",
    ])
    data["worlds"] = heights
    data["full"] = full
    out["surface-heights"] = data
    if package:
        data = header(uqm, ["tools/extract/golden/systems.c",
                            "tools/extract/golden/sha256.h",
                            "src/uqm/planets/plangen.c"], [
            "Every world's coloured surface map (the scan screen's map) as UQM "
            "0.8.0's own RenderTopography draws it, with the colour maps and "
            "translate tables (BINTAB) of the official content package "
            f"(SHA-256 {CONTENT_SHA256}), and Earth's its own image "
            f"({EARTH_SURFACE} frame 0, through UQM's own PNG loader). "
            "Content, so each map is the SHA-256 of its MAP_WIDTH x "
            "MAP_HEIGHT pixels as R, G, B bytes, row by row.",
            "worlds: [star, planet, moon or -1, SHA-256 or null (not entered)].",
        ])
        data["worlds"] = colors
        out["surface-colors"] = data
        data = header(uqm, ["tools/extract/golden/systems.c",
                            "tools/extract/golden/sha256.h",
                            "tools/extract/golden/images.c",
                            "src/uqm/planets/plangen.c",
                            "src/uqm/planets/pl_stuff.c",
                            "src/libs/graphics/sdl/png2sdl.c"], [
            "The rotating planet as UQM 0.8.0's own RenderPlanetSphere draws "
            "it, for Earth (its surface and elevations from its own images, "
            f"{EARTH_SURFACE}, through UQM's own PNG loader) and the first "
            "other world of each planet type (with and without a shield), "
            f"with the package's colour tables (SHA-256 {CONTENT_SHA256}), "
            "and UQM's default (PC) shield. Content, so hashes only.",
            "worlds: [star, planet, moon or -1, [[rotation step, SHA-256 of "
            "the DIAMETER x DIAMETER RGBA frame], ...], SHA-256 of the "
            "SHIELD_DIAM x SHIELD_DIAM RGBA halo (CreateShieldMask) or null]. "
            "Step 1 is InitSphereRotation's frame, each later step one more "
            "PrepareNextRotationFrame.",
        ])
        data["worlds"] = spheres
        out["planet-spheres"] = data
    return out


def planetside_lines(exe, tables_dir=None):
    """The systems program's planetside mode, its lines parsed. The program
    is run to the end first, so a failure (which leaves a line half
    printed) is reported as one, not as bad JSON."""
    proc = subprocess.run([exe, "planetside"] + ([tables_dir] if tables_dir else []),
                          stdout=subprocess.PIPE, text=True)
    if proc.returncode:
        raise RuntimeError(f"the planetside mode failed (exit {proc.returncode})")
    return [json.loads(line) for line in proc.stdout.splitlines()]


def record_planetside(uqm, systems_exe, strings_exe, images_exe=None, package=None):
    """Every landable world's planetside map (TopoScale4x), with frandom
    started afresh for each world (docs/plans/stage-2b.md, decision 1):
    the 4x height maps (code only) and, with the package, the coloured 4x
    maps (content, so hashes). Returns {"planetside-heights": ... and
    "planetside-colors": ... (with the package)}."""
    sources = ["tools/extract/golden/systems.c", "tools/extract/golden/stubs.c",
               "tools/extract/golden/sha256.h", "src/uqm/planets/plangen.c",
               "src/uqm/planets/gentopo.c"]
    heights = [[star, planet, moon, landable, topo or None]
               for star, planet, moon, landable, topo, _ in planetside_lines(systems_exe)]
    if sum(1 for w in heights if w[3]) == 0:
        raise RuntimeError("no landable worlds")
    wrong = [w[0:3] for w in heights if bool(w[3]) != bool(w[4])]
    if wrong:
        raise RuntimeError(f"worlds with a height map if and only if not landable: {wrong[:5]}")
    data = header(uqm, sources, [
        "Every world's planetside map, the 4x map UQM 0.8.0's own "
        "GeneratePlanetSurface makes with TopoScale4x (plangen.c) on entering "
        "orbit from a new game, for worlds that are neither shielded nor gas "
        "giants. TopoScale4x draws on frandom, whose seed UQM starts at "
        "0x12345678 when the program starts and never resets; here each world "
        "is run in a process of its own, so frandom starts afresh for each, "
        "as the engine does (docs/plans/stage-2b.md, decision 1). Code only, "
        "no content.",
        "worlds: [star, planet, moon or -1, landable, SHA-256 of the "
        "(MAP_WIDTH * 4) x (MAP_HEIGHT * 4) signed bytes, row by row, or null].",
    ])
    data["worlds"] = heights
    out = {"planetside-heights": data}
    if package:
        with tempfile.TemporaryDirectory() as tmp:
            tables = planet_tables(strings_exe, images_exe, package, tmp)
            lines = planetside_lines(systems_exe, tables)
        if [line[0:4] for line in lines] != [h[0:4] for h in heights]:
            raise RuntimeError("the planetside runs visited different worlds, "
                               "or found different ones landable")
        wrong = [line[0:3] for line in lines if bool(line[3]) != bool(line[5])]
        if wrong:
            raise RuntimeError("worlds with a coloured map if and only if "
                               f"not landable: {wrong[:5]}")
        colors = [[star, planet, moon, colored or None]
                  for star, planet, moon, _, _, colored in lines]
        data = header(uqm, sources, [
            "Every landable world's coloured planetside map as UQM 0.8.0's own "
            "RenderTopography draws the 4x map, with the colour maps and "
            "translate tables (BINTAB) of the official content package "
            f"(SHA-256 {CONTENT_SHA256}), frandom started afresh for each world "
            "as in planetside-heights.json. Content, so each map is the SHA-256 "
            "of its pixels as R, G, B bytes, row by row.",
            "worlds: [star, planet, moon or -1, SHA-256 or null (not landable)].",
        ])
        data["worlds"] = colors
        out["planetside-colors"] = data
    return out


def record_landing(uqm, systems_exe):
    """For every world, DoScan's checks before dispatching the lander and
    getLandingFuelNeeded's fuel (scan.c). Code only."""
    out = subprocess.run([systems_exe, "landing"], capture_output=True,
                         text=True, check=True).stdout
    worlds = [json.loads(line) for line in out.splitlines()]
    if not any(w[4] for w in worlds):
        raise RuntimeError("no world the lander can go down to")
    wrong = [w[0:3] for w in worlds if (w[3] == 0) != (w[5] < 0) or (w[4] and not w[3])]
    if wrong:
        raise RuntimeError(f"landing mode: inconsistent worlds {wrong[:5]}")
    data = header(uqm, ["tools/extract/golden/systems.c",
                        "src/uqm/planets/scan.c"], [
        "For every world, entered from a new game as orbits mode does, what "
        "UQM 0.8.0's own DoScan (scan.c) checks of the world before it "
        "dispatches the lander, and the fuel getLandingFuelNeeded says the "
        "landing costs. Code only, no content.",
        "worlds: [star, planet, moon or -1, entered (1 or 0), dispatchable "
        "(1 if neither shielded nor a gas giant), fuel in 1/100 units "
        "(FUEL_TANK_SCALE), -1 if not entered].",
    ])
    data["worlds"] = worlds
    return data


# ------------------------------------------------------------ conversations

# The conversation recording program (golden/comm.c) is built once per
# script, with the script's own file included and these of UQM's linked.
COMM_LINK = STRINGS_LINK + ["uqm/commglue.c", "uqm/sis.c", "libs/math/random.c", "libs/math/random2.c",
                            "libs/math/sqrt.c"]
COMM_PROGRAM = os.path.join(HERE, "golden", "comm.c")

# sis.h's module numbers, for the ship's module slots (EMPTY_SLOT + 2 is
# an empty module slot).
MODULES = {"PLANET_LANDER": 0, "FUSION_THRUSTER": 1, "TURNING_JETS": 2,
           "CREW_POD": 3, "STORAGE_BAY": 4, "FUEL_TANK": 5,
           "HIGHEFF_FUELSYS": 6, "DYNAMO_UNIT": 7, "SHIVA_FURNACE": 8,
           "GUN_WEAPON": 9, "BLASTER_WEAPON": 10, "CANNON_WEAPON": 11,
           "TRACKING_SYSTEM": 12, "ANTIMISSILE_DEFENSE": 13,
           "BOMB_MODULE_0": 14, "EMPTY": 22}
# The modules with a price (NUM_PURCHASE_MODULES), in sis.h's order, and
# a new game's prices (data/new-game.json; 0 for those not for sale).
PURCHASE_MODULES = [m for m, n in sorted(MODULES.items(), key=lambda x: x[1]) if n < 14]
with open(os.path.join(REPO, "data", "new-game.json")) as _f:
    _prices = json.load(_f)["prices"]
    NEW_GAME_COSTS = _prices["module_cost"]
    # A new game's price of a crew member (GLOBAL (CrewCost)).
    NEW_GAME_CREW_COST = _prices["crew"]
# Empty drive and jet slots (EMPTY_SLOT + 0 and + 1).
EMPTY_DRIVE, EMPTY_JET = 20, 21
# A new game's slots (InitGameStructures, data/new-game.json).
NEW_GAME_MODULES = ["EMPTY", "FUEL_TANK", "CREW_POD"] + ["EMPTY"] * 5 + \
    ["STORAGE_BAY"] + ["EMPTY"] * 6 + ["GUN_WEAPON"]
NEW_GAME_DRIVES = [5, 6]    # the slots with a fusion thruster
NEW_GAME_JETS = [0, 6]      # and with turning jets
# races.h's order (data/races.json), and a new game's standings (all
# BAD_GUY but the humans, GOOD_GUY).
RACES = ["ARILOU_SHIP", "CHMMR_SHIP", "HUMAN_SHIP", "ORZ_SHIP", "PKUNK_SHIP",
         "SHOFIXTI_SHIP", "SPATHI_SHIP", "SUPOX_SHIP", "THRADDASH_SHIP",
         "UTWIG_SHIP", "VUX_SHIP", "YEHAT_SHIP", "MELNORME_SHIP", "DRUUGE_SHIP",
         "ILWRATH_SHIP", "MYCON_SHIP", "SLYLANDRO_SHIP", "UMGAH_SHIP",
         "URQUAN_SHIP", "ZOQFOTPIK_SHIP", "SYREEN_SHIP", "BLACK_URQUAN_SHIP",
         "YEHAT_REBEL_SHIP", "URQUAN_DRONE_SHIP"]
ALLIED = {"dead": 0, "good": 1, "bad": 2}
RADIOACTIVE = 6   # elemdata.h's category
# races.h's INFINITE_RADIUS ((COUNT) ~0): a race met anywhere.
INFINITE_RADIUS = 0xFFFF


def new_game_strengths():
    """Each race's sphere's strength in a new game (InitGameStructures:
    its ship's from the master ship list, data/ships.json; the Yehat
    rebels' none, the drone's the probe's), in races.h's order, for
    StartSphereTracking: 0 for none, INFINITE_RADIUS for a race met
    anywhere."""
    with open(os.path.join(REPO, "data", "ships.json")) as f:
        by_species = {s["species"]: s["fleet"]["strength"] for s in json.load(f)["ships"]}
    species = {"HUMAN_SHIP": "EARTHLING_ID", "BLACK_URQUAN_SHIP": "KOHR_AH_ID", "URQUAN_SHIP": "UR_QUAN_ID",
               "URQUAN_DRONE_SHIP": "UR_QUAN_PROBE_ID"}
    return [0 if r == "YEHAT_REBEL_SHIP" else by_species[species.get(r, r[:-len("_SHIP")] + "_ID")] for r in RACES]


def talk_ship(crew=50, fuel=1000, landers=1, ru=0, elements=None,
              modules=None, drives=None, jets=None, captain="Zelnick", ship="Vindicator"):
    """A flagship for a talk: a new game's unless told otherwise. drives
    and jets are the slots that have a thruster or turning jets."""
    return {"crew": crew, "fuel": fuel, "landers": landers, "ru": ru,
            "elements": elements or [0] * 8,
            "modules": modules or NEW_GAME_MODULES,
            "drives": NEW_GAME_DRIVES if drives is None else drives,
            "jets": NEW_GAME_JETS if jets is None else jets,
            "captain": captain, "ship": ship}


def commander_starts():
    """Starting states for the commander's talk (comandr.c), chosen to
    reach every condition it reads: which visit it is (Intro: the Ilwrath
    come, a visit before, the radioactives brought, the base destroyed),
    radioactives in the hold, no lander (with the Chmmr's bomb not yet
    started, and one lost before), fuel under 2 units (given before or
    not), the base promised or asked about before, or taken before the
    radioactives are brought, a full crew or not (the new lander's 4 crew
    capped by the crew pod), a ship without fuel tanks, and the names
    spliced into the replies. (The fuel gift can't reach the tanks' limit:
    it's offered only under 2 units.)"""
    starts = []
    needs = [("lander", {}, {}),
             ("no-lander", {}, {"landers": 0}),
             ("no-lander-lost-before", {"LANDERS_LOST": 1}, {"landers": 0}),
             ("no-lander-bomb", {"CHMMR_BOMB_STATE": 2}, {"landers": 0}),
             ("no-lander-crew-short", {}, {"landers": 0, "crew": 10})]
    fuels = [("fuel", {}, {}),
             ("low-fuel", {}, {"fuel": 150}),
             ("low-fuel-given-before", {"GIVEN_FUEL_BEFORE": 1}, {"fuel": 150}),
             ("low-fuel-no-tanks", {}, {"fuel": 150, "modules": ["CREW_POD"] + ["EMPTY"] * 15})]

    def add(name, state, ship):
        starts.append({"name": name, "state": state, "ship": talk_ship(**ship)})

    for visit, vstate in (("first", {}), ("again", {"STARBASE_VISITED": 1})):
        for radios in (0, 5):
            for nname, nstate, nship in needs:
                for fname, fstate, fship in fuels:
                    elements = [0] * 8
                    elements[RADIOACTIVE] = radios
                    add(f"{visit}-{'radios' if radios else 'no-radios'}-{nname}-{fname}",
                        {**vstate, **nstate, **fstate},
                        {**nship, **fship, "elements": elements})
    # The Lunar base taken before the radioactives are brought (nothing in
    # gensol.c waits for them): RevealSelf then offers BaseDestroyed.
    radios = [0] * 8
    radios[RADIOACTIVE] = 5
    for visit, vstate in (("first", {}), ("again", {"STARBASE_VISITED": 1})):
        add(f"{visit}-radios-base-destroyed", {**vstate, "MOONBASE_DESTROYED": 1, "MOONBASE_ON_SHIP": 1},
            {"elements": radios})
        add(f"{visit}-no-radios-base-destroyed", {**vstate, "MOONBASE_DESTROYED": 1}, {})
    base = {"STARBASE_VISITED": 1, "RADIOACTIVES_PROVIDED": 1}
    for promised in (0, 1):
        for asked in (0, 1):
            for nname, nstate, nship in needs:
                for fname, fstate, fship in fuels:
                    add(f"moon-base{'-promised' if promised else ''}{'-asked' if asked else ''}-{nname}-{fname}",
                        {**base, "WILL_DESTROY_BASE": promised, "STARBASE_YACK_STACK1": asked, **nstate, **fstate},
                        {**nship, **fship})
    add("base-destroyed", {**base, "MOONBASE_DESTROYED": 1, "MOONBASE_ON_SHIP": 1}, {})
    add("base-destroyed-no-lander-low-fuel", {**base, "MOONBASE_DESTROYED": 1}, {"landers": 0, "fuel": 150})
    for cname, captain, ship in (("", "Zelnick", "Vindicator"),
                                 ("-long-names", "Captain Fifteen", "Fifteen bytes!!"),
                                 ("-accented-names", "Zoë", "Ñandú"),
                                 ("-empty-names", "", "")):
        add(f"ilwrath-came{cname}", {**base, "MOONBASE_DESTROYED": 1, "PROBE_ILWRATH_ENCOUNTER": 1},
            {"captain": captain, "ship": ship})
        add(f"first-names{cname}", {}, {"captain": captain, "ship": ship})
    return starts


def ilwrath_starts():
    """The Ilwrath's talk (ilwrathc.c), from states chosen to reach every
    condition it reads: the ship at Earth (PROBE_ILWRATH_ENCOUNTER, the
    opening's; with the news asked before, which only the branches met in
    space read), the endgame's won battle, met in space (each visit, with
    the visits past those the switch names, and each piece of news asked,
    the last past ILWRATH_INFO's two bits; deceived into fighting the
    Thraddash, and fighting them; in hyperspace and a system), at their
    homeworld (GLOBAL_FLAGS_AND_DATA bit 7, genilw.c's orbit: each visit),
    the Chmmr's (bit 6: each visit), and by HyperWave Broadcaster to a ship
    (bit 5) and to the homeworld (bit 4: the gods spoken each time, the
    worship each time, deceived, fighting)."""
    starts = [{"name": "at-earth", "state": {"PROBE_ILWRATH_ENCOUNTER": 1}, "ship": talk_ship()},
              {"name": "at-earth-info-asked", "state": {"PROBE_ILWRATH_ENCOUNTER": 1, "ILWRATH_INFO": 3},
               "ship": talk_ship()},
              space_start("won-last-battle", {}, "WON_LAST_BATTLE"),
              space_start("in-a-system", {}, "IN_INTERPLANETARY"),
              space_start("deceived", {"ILWRATH_DECEIVED": 1}),
              space_start("fighting-thraddash", {"ILWRATH_DECEIVED": 1, "ILWRATH_FIGHT_THRADDASH": 1})]
    for visits in range(8):
        starts.append(space_start(f"visit-{visits}", {"ILWRATH_VISITS": visits}))
    for info in range(1, 4):
        starts.append(space_start(f"info-{info}", {"ILWRATH_VISITS": 1, "ILWRATH_INFO": info}))
    # The homeworld's whole talk from its first visit; the later visits'
    # hellos only (follow none: their replies are the first's).
    for visits in range(3):
        starts.append(space_start(f"homeworld-visit-{visits}",
                                  {"GLOBAL_FLAGS_AND_DATA": 1 << 7, "ILWRATH_HOME_VISITS": visits},
                                  "IN_INTERPLANETARY", **({"follow": []} if visits else {})))
    for visits in range(2):
        starts.append(space_start(f"chmmr-visit-{visits}",
                                  {"GLOBAL_FLAGS_AND_DATA": 1 << 6, "ILWRATH_CHMMR_VISITS": visits}))
    for name, state in (("", {}), ("-deceived", {"ILWRATH_DECEIVED": 1}),
                        ("-fighting-thraddash", {"ILWRATH_DECEIVED": 1, "ILWRATH_FIGHT_THRADDASH": 1})):
        starts.append(space_start(f"caster-ship{name}", {"GLOBAL_FLAGS_AND_DATA": 1 << 5, **state}))
        starts.append(space_start(f"caster-home{name}", {"GLOBAL_FLAGS_AND_DATA": 1 << 4, **state},
                                  "IN_INTERPLANETARY"))
    # The gods spoken before change the hello only; the worship before,
    # the worship reply's answer only (the rest is caster-home's).
    for spoken in range(1, 5):
        starts.append(space_start(f"caster-home-spoken-{spoken}",
                                  {"GLOBAL_FLAGS_AND_DATA": 1 << 4, "ILWRATH_GODS_SPOKEN": spoken},
                                  "IN_INTERPLANETARY", follow=[]))
    for worship in range(1, 4):
        starts.append(space_start(f"caster-home-worship-{worship}",
                                  {"GLOBAL_FLAGS_AND_DATA": 1 << 4, "ILWRATH_WORSHIP": worship},
                                  "IN_INTERPLANETARY", follow=["GodsSpeak", "CombatIsInevitable"]))
    return starts


def spathi_starts():
    """The Spathi's talk away from Spathiwa (spathic.c), from states chosen
    to reach every condition it reads: Fwiffo on Pluto (FOUND_PLUTO_SPATHI
    1: the escorts with room for him or not, the password known from the
    Melnorme before Spathiwa or not), and met in space after it (2); the
    endgame's won battle; each SPATHI_MANNER (0 neutral, 1 angry: each
    visit, 2 hating, 3 at peace) with the visits before, the alliance, the
    quest, and the neutral talk's stacks; the visits past those the
    switches name; a fight's post-encounter with the password known or
    not; in hyperspace and a system."""
    # Fwiffo's whole talk with room for him; without, from where joining
    # is offered (the replies to it; the rest is the same), and with the
    # password known, his hello only. Away from Pluto, the starts that
    # differ from the first visit's neutral talk only in the hello (follow
    # none) or in a fight's post-encounter (the fights followed).
    allied = {"alliances": {"SPATHI_SHIP": "good"}}
    fights = ["SpathiNeutral", "SpathiAntagonize", "ExitConversation"]
    starts = [space_start("pluto", {"FOUND_PLUTO_SPATHI": 1}, "IN_INTERPLANETARY", escorts=[0, 12]),
              space_start("pluto-no-room", {"FOUND_PLUTO_SPATHI": 1}, "IN_INTERPLANETARY", escorts=[300, 0],
                          prefix=[0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1], follow=["ExitConversation"]),
              space_start("pluto-password", {"FOUND_PLUTO_SPATHI": 1, "SPATHI_HOME_VISITS": 7,
                                             "KNOW_SPATHI_PASSWORD": 1}, "IN_INTERPLANETARY", escorts=[0, 12],
                          follow=[]),
              space_start("after-pluto", {"FOUND_PLUTO_SPATHI": 2}, follow=[]),
              space_start("won-last-battle", {}, "WON_LAST_BATTLE", follow=fights),
              space_start("in-a-system", {}, "IN_INTERPLANETARY", follow=[]),
              space_start("hating", {"SPATHI_MANNER": 2}),
              space_start("at-peace", {"SPATHI_MANNER": 3}, follow=fights),
              space_start("at-peace-allied", {"SPATHI_MANNER": 3}, **allied),
              space_start("password-known", {"SPATHI_HOME_VISITS": 7}, follow=fights),
              space_start("home-visited", {"SPATHI_HOME_VISITS": 2}, follow=fights)]
    for visits in range(8):
        starts.append(space_start(f"neutral-visit-{visits}", {"SPATHI_VISITS": visits},
                                  **({"follow": []} if visits else {})))
        starts.append(space_start(f"angry-visit-{visits}", {"SPATHI_MANNER": 1, "SPATHI_VISITS": visits},
                                  **({"follow": []} if visits > 1 else {})))
    for visits in range(3):
        starts.append(space_start(f"allied-visit-{visits}", {"SPATHI_VISITS": visits}, **allied))
        starts.append(space_start(f"quest-visit-{visits}", {"SPATHI_QUEST": 1, "SPATHI_VISITS": visits}))
    for stack0 in range(3):
        for stack1 in range(2):
            for stack2 in range(2):
                starts.append(space_start(f"stacks-{stack0}{stack1}{stack2}",
                                          {"SPATHI_VISITS": 1, "SPATHI_STACK0": stack0,
                                           "SPATHI_STACK1": stack1, "SPATHI_STACK2": stack2}))
    return starts


def field32(name, value):
    """A 32-bit story value as UQM keeps it: four 8-bit fields, low byte first."""
    return {f"{name}{i}": value >> (8 * i) & 0xFF for i in range(4)}


def starbase_starts():
    """The allied starbase commander's talk (starbas.c), from states chosen
    to reach every condition it reads.

    - The starbase ready (MOONBASE_ON_SHIP), as the opening reaches it, on
      a few dates and seeds, with and without minerals aboard, and with the
      Chmmr's bomb just installed.
    - Every reply, walked in full from one visit (the information the
      commander gives: the starbase, the history, the mission).
    - His greetings: the first of a visit on several seeds, and once
      greeted before (RETURN_HELLO).
    - Each bulletin's condition (CheckBulletins): the races allied, the
      story fields, the dates CheckTiming compares (a month's end, a
      year's), the crew sold and bought, the Arilou's gift with room in
      the fleet or not; several at once, already given, and repeated.
    - Each device (DiscussDevices), alone, discussed before, and several
      at once with the Vux beast's report.
    - Selling minerals: light, medium and heavy loads (by their worth, at
      the edges), each with every count of sales before.
    - The mission's advice (AnalyzeCondition): ships short of the minimum
      in each way, the minimum and the maximum, the module kinds' counts,
      resource units, allies, the escort fleet's worth and the Chmmr's
      bomb's state and the Samatra.

    A start with follow walks only the replies calling those functions,
    and one with prefix starts its walk after those replies: the rest of
    the talk is walked in full from the information starts."""
    starts = []
    allied = {"STARBASE_AVAILABLE": 1, "MOONBASE_DESTROYED": 1, "RADIOACTIVES_PROVIDED": 1,
              "GLOBAL_FLAGS_AND_DATA": 255, "STARBASE_MONTH": 3, "STARBASE_DAY": 3}
    every = field32("STARBASE_BULLETS", 0xFFFFFFFF)
    after = (2155, 3, 4)

    def add(name, state, ship=None, follow=None, date=after, **extra):
        start = {"name": name, "state": {**allied, **state}, "ship": ship or talk_ship(),
                 "date": list(date), **extra}
        if follow is not None:
            start["follow"] = follow
        starts.append(start)

    bye = ["NormalStarbase", "ByeBye"]

    # The starbase ready.
    for seed, date in ((1, (2155, 2, 17)), (2, (2155, 3, 31)), (3, (2156, 12, 1)),
                       (40, (2155, 3, 3)), (500, (2155, 3, 4)), (6000, (2155, 3, 5)),
                       (70000, (2155, 3, 6)), (800000, (2155, 3, 7))):
        for minerals in (0, 12):
            elements = [0] * 8
            elements[RADIOACTIVE] = minerals
            add(f"ready-{date[0]}-{date[1]}-{date[2]}{'-minerals' if minerals else ''}",
                {"MOONBASE_ON_SHIP": 1, "STARBASE_MONTH": 0, "STARBASE_DAY": 0},
                talk_ship(elements=elements), ["ByeBye", "SellMinerals"], date, seed=seed)
    add("ready-bomb", {"MOONBASE_ON_SHIP": 1, "CHMMR_BOMB_STATE": 2}, follow=["ByeBye"])
    add("bomb-installed", {"CHMMR_BOMB_STATE": 2, "STARBASE_VISITED": 1, **every}, follow=["ByeBye"])
    add("bomb-later", {"CHMMR_BOMB_STATE": 3, "STARBASE_VISITED": 1, **every}, follow=["ByeBye"])

    # Every reply: the race lists (each a reply a race, taken away once
    # asked, so 2^7 orders) are walked from their own starts, the walk
    # going straight to them (prefix: need_info, history, alien_races, and
    # the alliance or the Hierarchy).
    add("information", {"STARBASE_VISITED": 1, **every},
        follow=["NormalStarbase", "ByeBye", "NeedInfo", "TellStarBase", "TellHistory", "AlienRaces",
                "WarInfo", "AncientHistory", "TellMission", "DefeatUrquan", "SellMinerals"])
    add("information-alliance", {"STARBASE_VISITED": 1, **every}, follow=["AllianceInfo", "AlienRaces"],
        prefix=[0, 1, 0, 0])
    add("information-hierarchy", {"STARBASE_VISITED": 1, **every}, follow=["HierarchyInfo", "AlienRaces"],
        prefix=[0, 1, 0, 1])

    # The greetings.
    for seed in range(1, 17):
        add(f"hello-{seed}", every, follow=["ByeBye"], seed=seed)
    add("hello-names", every, talk_ship(captain="Captain Fifteen", ship="Fifteen bytes!!"),
        ["ByeBye", "NeedInfo", "TellStarBase"], seed=3)
    add("hello-empty-names", every, talk_ship(captain="", ship=""),
        ["ByeBye", "NeedInfo", "TellStarBase"], seed=4)
    add("return-hello", {"STARBASE_VISITED": 1, **every}, follow=bye)

    # The bulletins, each alone (the others already given).
    def bulletin(n, name, state=None, date=after, **extra):
        add(f"bulletin-{n}-{name}", {"STARBASE_VISITED": 1, **field32("STARBASE_BULLETS", 0xFFFFFFFF & ~(1 << n)),
                                     **(state or {})}, follow=bye, date=date, **extra)

    for n, race in ((0, "SPATHI_SHIP"), (1, "ZOQFOTPIK_SHIP"), (2, "SUPOX_SHIP"), (3, "UTWIG_SHIP"),
                    (4, "ORZ_SHIP"), (12, "CHMMR_SHIP"), (13, "SHOFIXTI_SHIP")):
        bulletin(n, race.lower(), alliances={race: "good"})
        bulletin(n, race.lower() + "-not", alliances={race: "dead"})
    bulletin(5, "arilou-gift", {"PORTAL_SPAWNER": 1}, escorts=[0, 12])
    bulletin(5, "arilou-gift-room-for-2", {"PORTAL_SPAWNER": 1}, escorts=[0, 2])
    bulletin(5, "arilou-gift-no-room", {"PORTAL_SPAWNER": 1}, escorts=[0, 0])
    bulletin(5, "arilou-gift-manner", {"PORTAL_SPAWNER": 1, "ARILOU_MANNER": 2}, escorts=[0, 12])
    bulletin(5, "no-spawner", {}, escorts=[0, 12])
    bulletin(6, "zoqfot-distress", {"ZOQFOT_DISTRESS": 1})
    bulletin(21, "zoqfot-dead", {"ZOQFOT_DISTRESS": 2})
    bulletin(10, "spathi-shielded", {"SPATHI_SHIELDED_SELVES": 1})
    bulletin(14, "pkunk-mission", {"PKUNK_MISSION": 1})
    bulletin(17, "yehat-absorbed-pkunk", {"YEHAT_ABSORBED_PKUNK": 1})
    bulletin(18, "bomb", {"CHMMR_BOMB_STATE": 2})
    for n, months, days in ((7, 3, 0), (8, 6, 0), (9, 0, 7), (11, 0, 42), (15, 7, 0)):
        # STARBASE 3/3: due after this date.
        d = datetime.date(2155, 3, 3)
        due = d.replace(month=d.month + months) + datetime.timedelta(days=days) if months \
            else d + datetime.timedelta(days=days)
        for when, date in (("on", due), ("after", due + datetime.timedelta(days=1))):
            bulletin(n, f"timing-{when}", date=(date.year, date.month, date.day))
    late = (2160, 1, 1)
    bulletin(7, "timing-met-melnorme", {"MET_MELNORME": 1}, late)
    bulletin(8, "timing-met-melnorme", {"MET_MELNORME": 1}, late)
    bulletin(9, "timing-found-spathi", {"FOUND_PLUTO_SPATHI": 1}, late)
    bulletin(11, "timing-zoqfot-visited", {"ZOQFOT_HOME_VISITS": 1}, late)
    bulletin(11, "timing-zoqfot-groups", field32("ZOQFOT_GRPOFFS", 0x100), late)
    bulletin(15, "timing-destruct-code", {"DESTRUCT_CODE_ON_SHIP": 1}, late)
    # CheckTiming across a year's end, and from a month's last day.
    for n in (7, 11, 15):
        for date in ((2155, 12, 31), (2156, 1, 7), (2156, 1, 8), (2156, 6, 26), (2156, 6, 27)):
            bulletin(n, f"timing-from-11-25-{date[0]}-{date[1]}-{date[2]}",
                     {"STARBASE_MONTH": 11, "STARBASE_DAY": 25}, date)
    for date in ((2155, 4, 30), (2155, 5, 1), (2155, 5, 2)):
        bulletin(7, f"timing-from-1-31-{date[1]}-{date[2]}", {"STARBASE_MONTH": 1, "STARBASE_DAY": 31}, date)
    for sold in (1, 100, 101, 250, 251, 1000):
        crew = {"CREW_SOLD_TO_DRUUGE0": sold & 0xFF, "CREW_SOLD_TO_DRUUGE1": sold >> 8}
        for n in (26, 27, 29):
            bulletin(n, f"crew-sold-{sold}", crew)
    for bought in (999, 1000):
        bulletin(28, f"crew-bought-{bought}", {"CREW_PURCHASED0": bought & 0xFF, "CREW_PURCHASED1": bought >> 8})
    # Several at once, on several seeds (BEFORE_WE_GO_ON's lines).
    many = {"ZOQFOT_DISTRESS": 1, "SPATHI_SHIELDED_SELVES": 1, "PKUNK_MISSION": 1,
            "CHMMR_BOMB_STATE": 2, "CREW_SOLD_TO_DRUUGE0": 150, "PORTAL_SPAWNER": 1}
    friends = {r: "good" for r in ("SPATHI_SHIP", "ZOQFOTPIK_SHIP", "SUPOX_SHIP", "UTWIG_SHIP",
                                   "ORZ_SHIP", "CHMMR_SHIP", "SHOFIXTI_SHIP")}
    for seed in range(1, 15):
        add(f"bulletins-many-{seed}", {"STARBASE_VISITED": 1, **many}, follow=bye, date=late,
            alliances=friends, escorts=[0, 12], seed=seed)
    add("bulletins-hello", {**many}, follow=bye, date=late, alliances=friends, escorts=[0, 12], seed=5)
    add("bulletins-given", {"STARBASE_VISITED": 1, **many, **field32("STARBASE_BULLETS", 0x0000FFFF)},
        follow=bye, date=late, alliances=friends, escorts=[0, 12])
    add("bulletins-none-due", {"STARBASE_VISITED": 1, "MET_MELNORME": 1, "FOUND_PLUTO_SPATHI": 1,
                               "ZOQFOT_HOME_VISITS": 1, "DESTRUCT_CODE_ON_SHIP": 1}, follow=bye, date=late)

    # The devices, each alone, then discussed before, then several at once.
    devices = [("ROSY_SPHERE_ON_SHIP", "DISCUSSED_ROSY_SPHERE"), ("ARTIFACT_2_ON_SHIP", "DISCUSSED_ARTIFACT_2"),
               ("ARTIFACT_3_ON_SHIP", "DISCUSSED_ARTIFACT_3"), ("SUN_DEVICE_ON_SHIP", "DISCUSSED_SUN_EFFICIENCY"),
               ("UTWIG_BOMB_ON_SHIP", "DISCUSSED_UTWIG_BOMB"), ("MAIDENS_ON_SHIP", "DISCUSSED_MAIDENS"),
               ("TALKING_PET_ON_SHIP", "DISCUSSED_TALKING_PET"), ("AQUA_HELIX_ON_SHIP", "DISCUSSED_AQUA_HELIX"),
               ("CLEAR_SPINDLE_ON_SHIP", "DISCUSSED_CLEAR_SPINDLE"),
               ("UMGAH_BROADCASTERS_ON_SHIP", "DISCUSSED_UMGAH_HYPERWAVE"),
               ("TAALO_PROTECTOR_ON_SHIP", "DISCUSSED_TAALO_PROTECTOR"), ("EGG_CASE0_ON_SHIP", "DISCUSSED_EGG_CASING0"),
               ("EGG_CASE1_ON_SHIP", "DISCUSSED_EGG_CASING1"), ("EGG_CASE2_ON_SHIP", "DISCUSSED_EGG_CASING2"),
               ("SYREEN_SHUTTLE_ON_SHIP", "DISCUSSED_SYREEN_SHUTTLE"), ("VUX_BEAST_ON_SHIP", "DISCUSSED_VUX_BEAST"),
               ("DESTRUCT_CODE_ON_SHIP", "DISCUSSED_DESTRUCT_CODE"),
               ("PORTAL_SPAWNER_ON_SHIP", "DISCUSSED_PORTAL_SPAWNER"), ("PORTAL_KEY_ON_SHIP", "DISCUSSED_URQUAN_WARP"),
               ("BURV_BROADCASTERS_ON_SHIP", "DISCUSSED_BURVIX_HYPERWAVE")]
    quiet = {"STARBASE_VISITED": 1, **every}
    for on, discussed in devices:
        add(f"device-{on.lower()}", {**quiet, on: 1}, follow=bye)
        add(f"device-{on.lower()}-discussed", {**quiet, on: 1, discussed: 1}, follow=bye)
    for cond in range(1, 5):
        add(f"device-ultron-{cond}", {**quiet, "ULTRON_CONDITION": cond}, follow=bye)
        add(f"device-ultron-{cond}-discussed", {**quiet, "ULTRON_CONDITION": cond, "DISCUSSED_ULTRON": 1},
            follow=bye)
    add("devices-eggs", {**quiet, "EGG_CASE0_ON_SHIP": 1, "EGG_CASE2_ON_SHIP": 1}, follow=bye)
    add("devices-several", {**quiet, "ROSY_SPHERE_ON_SHIP": 1, "SUN_DEVICE_ON_SHIP": 1, "ULTRON_CONDITION": 2,
                            "MAIDENS_ON_SHIP": 1}, follow=bye)
    add("devices-vux-first", {**quiet, "VUX_BEAST_ON_SHIP": 1, "DESTRUCT_CODE_ON_SHIP": 1}, follow=bye)
    add("devices-vux-later", {**quiet, "ROSY_SPHERE_ON_SHIP": 1, "TALKING_PET_ON_SHIP": 1,
                              "VUX_BEAST_ON_SHIP": 1, "BURV_BROADCASTERS_ON_SHIP": 1}, follow=bye)

    # Selling minerals: loads by worth, with every count of sales before.
    sell = ["SellMinerals", "ByeBye"]

    def load(**amounts):
        cats = ["COMMON", "CORROSIVE", "BASE_METAL", "NOBLE", "RARE_EARTH", "PRECIOUS", "RADIOACTIVE", "EXOTIC"]
        return [amounts.get(c.lower(), 0) for c in cats]

    loads = {"LIGHT_MINERAL_LOAD": load(common=40, radioactive=12),
             "MEDIUM_MINERAL_LOAD": load(base_metal=100, exotic=50),
             "HEAVY_MINERAL_LOAD": load(exotic=200)}
    for counter, elements in loads.items():
        for before in range(7):   # 6 at most: the sixth load's lines repeat
            add(f"sell-{counter.lower()}-{before}", {**quiet, counter: before},
                talk_ship(elements=elements, ru=300), sell)
    for total, elements in ((999, load(common=999)), (1000, load(common=1000)),
                            (2499, load(exotic=99, common=24)), (2500, load(exotic=100)),
                            (1, load(common=1)), (5000, load(exotic=200))):
        add(f"sell-worth-{total}", quiet, talk_ship(elements=elements), sell)
    add("sell-every-kind", quiet, talk_ship(elements=[1, 2, 3, 4, 5, 6, 7, 8]), sell)
    add("sell-hello", every, talk_ship(elements=load(noble=20)), sell, seed=9)

    # The mission's advice (what_do_now), by the ship and the story.
    # The walk goes straight to the advice (need_info, our_mission,
    # what_do_now) and stops there: the rest is the information start's.
    # (A device aboard is discussed already, so its reply isn't offered.)
    mission = []

    def ship(modules=(), drives=7, jets=5, crew=50, fuel=5000, landers=1, ru=0):
        mods = list(modules) + ["EMPTY"] * (16 - len(modules))
        return talk_ship(crew=crew, fuel=fuel, landers=landers, ru=ru, modules=mods,
                         drives=list(range(drives)), jets=list(range(jets)))

    minimum = ["STORAGE_BAY", "DYNAMO_UNIT", "GUN_WEAPON", "GUN_WEAPON"]
    maximum = ["SHIVA_FURNACE", "SHIVA_FURNACE", "CANNON_WEAPON", "CANNON_WEAPON", "TRACKING_SYSTEM",
               "ANTIMISSILE_DEFENSE", "ANTIMISSILE_DEFENSE"]
    best = dict(drives=11, jets=8, crew=150, fuel=15000, landers=3)
    allies = {"SPATHI_SHIP": "good", "ORZ_SHIP": "good"}

    def advise(name, ship_, state=None, **extra):
        add(f"mission-{name}", {**quiet, **(state or {})}, ship_, mission, prefix=[0, 2, 2], **extra)

    advise("new-ship", talk_ship())
    advise("new-ship-rich", talk_ship(ru=3000))
    advise("new-ship-2999", talk_ship(ru=2999))
    advise("minimum", ship(minimum))
    for short, s_ in (("thrusters", ship(minimum, drives=6)), ("jets", ship(minimum, jets=4)),
                      ("one-gun", ship(minimum[:3])), ("crew", ship(minimum, crew=49)),
                      ("fuel", ship(minimum, fuel=4999)), ("bays", ship(minimum[1:])),
                      ("landers", ship(minimum, landers=0)), ("dynamos", ship(["STORAGE_BAY"] + minimum[2:])),
                      ("everything", ship([], drives=0, jets=0, crew=0, fuel=0, landers=0))):
        advise(f"short-{short}", s_)
    advise("one-gun-tracking", ship(minimum[:3] + ["TRACKING_SYSTEM"]))
    advise("one-blaster", ship(["STORAGE_BAY", "DYNAMO_UNIT", "BLASTER_WEAPON"]))
    advise("shiva", ship(["STORAGE_BAY", "SHIVA_FURNACE", "GUN_WEAPON", "GUN_WEAPON"]))
    advise("minimum-one-ally", ship(minimum), alliances={"SPATHI_SHIP": "good"})
    advise("minimum-allies", ship(minimum), alliances=allies)
    advise("minimum-allies-rich", ship(minimum, ru=3000), alliances=allies)
    advise("minimum-allies-fleet", ship(minimum), alliances=allies, escorts=[6000, 0])
    advise("minimum-allies-fleet-5999", ship(minimum), alliances=allies, escorts=[5999, 0])
    full = ship(["STORAGE_BAY"] + maximum, **best)
    advise("maximum-allies-fleet", full, alliances=allies, escorts=[6000, 0])
    advise("maximum-samatra", full, {"AWARE_OF_SAMATRA": 1}, alliances=allies, escorts=[6000, 0])
    advise("maximum-utwig-bomb", full, {"AWARE_OF_SAMATRA": 1, "UTWIG_BOMB": 1}, alliances=allies,
           escorts=[6000, 0])
    advise("maximum-utwig-bomb-aboard", full, {"AWARE_OF_SAMATRA": 1, "UTWIG_BOMB": 1, "UTWIG_BOMB_ON_SHIP": 1,
                                                  "DISCUSSED_UTWIG_BOMB": 1},
           alliances=allies, escorts=[6000, 0])
    advise("maximum-no-allies", full)
    for short, s_ in (("thrusters", ship(maximum, **{**best, "drives": 10})),
                      ("jets", ship(maximum, **{**best, "jets": 7})),
                      ("guns", ship(maximum[:2] + ["CANNON_WEAPON", "BLASTER_WEAPON", "TRACKING_SYSTEM"]
                                    + maximum[5:], **best)),
                      ("crew", ship(maximum, **{**best, "crew": 149})),
                      ("fuel", ship(maximum, **{**best, "fuel": 14999})),
                      ("landers", ship(maximum, **{**best, "landers": 2})),
                      ("dynamos", ship(["SHIVA_FURNACE", "DYNAMO_UNIT"] + maximum[2:], **best)),
                      ("defense", ship(maximum[:6], **best))):
        advise(f"nearly-maximum-{short}", s_, alliances=allies, escorts=[6000, 0])
    for bomb in (2, 3):
        advise(f"bomb-{bomb}-new-ship", talk_ship(), {"CHMMR_BOMB_STATE": bomb})
        advise(f"bomb-{bomb}-fleet", full, {"CHMMR_BOMB_STATE": bomb}, escorts=[20000, 0])
        advise(f"bomb-{bomb}-fleet-19999", full, {"CHMMR_BOMB_STATE": bomb}, escorts=[19999, 0])
        advise(f"bomb-{bomb}-fleet-pet", full, {"CHMMR_BOMB_STATE": bomb, "TALKING_PET_ON_SHIP": 1,
                                                       "DISCUSSED_TALKING_PET": 1},
               escorts=[20000, 0])
    return starts


# The special systems' own groups' offsets as a game that has been to
# them has them (any distinct numbers: the scripts only compare them with
# BattleGroupRef), for the Ur-Quan's and the Kohr-Ah's talks.
GROUP_OFFSETS = {"URQUAN_PROBE_GRPOFFS0": 0x1100, "COLONY_GRPOFFS0": 0x2200, "SAMATRA_GRPOFFS0": 0x3300}
# globdata.h's activities (LOBYTE (GLOBAL (CurrentActivity))).
ACTIVITIES = {"SUPER_MELEE": 0, "IN_LAST_BATTLE": 1, "IN_ENCOUNTER": 2, "IN_HYPERSPACE": 3,
              "IN_INTERPLANETARY": 4, "WON_LAST_BATTLE": 5}


def space_start(name, state, activity="IN_HYPERSPACE", group=None, **kw):
    """A start for a race met in space: the special groups' offsets set,
    where the talk is (activity) and the system's own group met (group,
    its offset's field: BattleGroupRef; none, 0)."""
    offsets = {}
    for field, value in GROUP_OFFSETS.items():
        offsets.update(field32(field[:-1], value))
    start = {"name": name, "state": {**offsets, **state}, "ship": talk_ship(), "activity": activity}
    if group:
        start["battle_group"] = group
    start.update(kw)
    return start


def urquan_starts():
    """The Ur-Quan's talk (urquanc.c), met in space and as their drone
    (URQUAN_DRONE_CONVERSATION runs this script), from states chosen to
    reach every condition it reads: the endgame's won battle, the drone
    at Earth (the system's own group, in interplanetary: its message, and
    in hyperspace not so), the captain hypnotised by the Talking Pet (the
    first time, again, the compulsion mentioned before), the Talking Pet
    aboard (sensed or not), the colony's guard, the Sa-Matra's, each visit
    before and after the Kohr-Ah's frenzy (with the visits past those the
    switch names, which UQM's 3-bit field overflows), the key phrase known
    or told, and each piece of news asked."""
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE"),
              space_start("drone", {}, "IN_INTERPLANETARY", "URQUAN_PROBE_GRPOFFS0"),
              space_start("drone-hypnotized", {"PLAYER_HYPNOTIZED": 1}, "IN_INTERPLANETARY",
                          "URQUAN_PROBE_GRPOFFS0"),
              space_start("drone-offset-in-hyperspace", {}, "IN_HYPERSPACE", "URQUAN_PROBE_GRPOFFS0"),
              space_start("hypnotized-first", {"PLAYER_HYPNOTIZED": 1}),
              space_start("hypnotized-again", {"PLAYER_HYPNOTIZED": 1, "URQUAN_HYPNO_VISITS": 1}),
              space_start("hypnotized-again-mentioned", {"PLAYER_HYPNOTIZED": 1, "URQUAN_HYPNO_VISITS": 1,
                                                         "MENTIONED_PET_COMPULSION": 1}),
              space_start("pet", {"TALKING_PET_ON_SHIP": 1}),
              space_start("pet-sensed", {"TALKING_PET_ON_SHIP": 1, "URQUAN_SENSES_EVIL": 1}),
              space_start("colony", {}, "IN_INTERPLANETARY", "COLONY_GRPOFFS0"),
              space_start("colony-pet", {"TALKING_PET_ON_SHIP": 1}, "IN_INTERPLANETARY", "COLONY_GRPOFFS0"),
              space_start("colony-offset-in-hyperspace", {}, "IN_HYPERSPACE", "COLONY_GRPOFFS0"),
              space_start("samatra", {}, "IN_INTERPLANETARY", "SAMATRA_GRPOFFS0"),
              space_start("in-a-system", {}, "IN_INTERPLANETARY")]
    for visits in range(8):
        starts.append(space_start(f"visit-{visits}", {"URQUAN_VISITS": visits}))
        starts.append(space_start(f"frenzy-visit-{visits}", {"URQUAN_VISITS": visits, "KOHR_AH_FRENZY": 1}))
    for know in (1, 2):
        starts.append(space_start(f"know-story-{know}", {"URQUAN_VISITS": 1, "KNOW_URQUAN_STORY": know}))
    for info in range(1, 4):
        starts.append(space_start(f"info-{info}", {"URQUAN_VISITS": 2, "URQUAN_INFO": info}))
    return starts


def blackur_starts():
    """The Kohr-Ah's talk (blackurc.c), from states chosen to reach every
    condition it reads: the endgame's won battle, everyone killed, the
    Talking Pet aboard (sensed or not), the Sa-Matra's guards, each visit
    before and in the frenzy, and the replies' counters (the reasons asked
    for, the pleading, the news; each value of each, and each with the
    others, two ways), the key phrase known or told, and the goodbye said
    before."""
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE"),
              space_start("killed-all", {"KOHR_AH_KILLED_ALL": 1}),
              space_start("pet", {"TALKING_PET_ON_SHIP": 1}),
              space_start("pet-sensed", {"TALKING_PET_ON_SHIP": 1, "KOHR_AH_SENSES_EVIL": 1}),
              space_start("samatra", {}, "IN_INTERPLANETARY", "SAMATRA_GRPOFFS0"),
              space_start("samatra-offset-in-hyperspace", {}, "IN_HYPERSPACE", "SAMATRA_GRPOFFS0"),
              space_start("in-a-system", {}, "IN_INTERPLANETARY")]
    for frenzy in (0, 1):
        f = {"KOHR_AH_FRENZY": frenzy} if frenzy else {}
        kind = "frenzy" if frenzy else "hello"
        for visits in range(1, 4):
            starts.append(space_start(f"{kind}-visit-{visits}", {**f, "KOHR_AH_VISITS": visits}))
        for k in range(4):
            for name, (r, p, i) in ((f"{kind}-counters-{k}", (k, k, k)),
                                    (f"{kind}-counters-mixed-{k}", (k, 3 - k, (k + 1) % 4))):
                starts.append(space_start(name, {**f, "KOHR_AH_REASONS": r, "KOHR_AH_PLEAD": p, "KOHR_AH_INFO": i}))
    for know in (1, 2):
        starts.append(space_start(f"know-story-{know}", {"KNOW_KOHR_AH_STORY": know}))
    starts.append(space_start("said-bye-before", {"KOHR_AH_BYES": 1}))
    return starts


def universe_to_log(ux, uy):
    """UNIVERSE_TO_LOGX and UNIVERSE_TO_LOGY (units.h)."""
    log_units_x, log_units_y = (8192 >> 4) * 195, (7680 >> 4) * 25
    universe_units_x, universe_units_y = ((9999 + 1) >> 4) * 10, (9999 + 1) >> 4
    return ((ux * log_units_x + (universe_units_x >> 1)) // universe_units_x,
            ((9999 - uy) * log_units_y + (universe_units_y >> 1)) // universe_units_y)


# A new game's place on the star map (globdata.c: Sol's).
SOL_LOG = universe_to_log(1752, 1450)


def slyland_starts():
    """The Slylandro probe's talk (slyland.c), from states chosen to reach
    every condition it reads: each visit (the hello; the visits past those
    the switch names), each reply's count of times asked (each value, and
    mixed), the destruct code aboard (DESTRUCT_CODE_ON_SHIP: the code sent,
    an instant victory), and the probe saying where it is (its fourth
    "who are you", SLYLANDRO_PROBE_ID 3) from places on the star map that
    give each sign and each way of speaking a number: zero, digits, teens,
    tens with and without units, hundreds with and without the rest (the
    flagship's log_x and log_y, between universe units too)."""
    starts = [space_start("first", {})]
    for visits in range(1, 8):
        starts.append(space_start(f"visit-{visits}", {"SLYLANDRO_PROBE_VISITS": visits}, follow=[]))
    counters = ["SLYLANDRO_PROBE_THREAT", "SLYLANDRO_PROBE_WRONG", "SLYLANDRO_PROBE_ID",
                "SLYLANDRO_PROBE_INFO", "SLYLANDRO_PROBE_EXIT"]
    for k in range(1, 4):
        starts.append(space_start(f"counters-{k}", {"SLYLANDRO_PROBE_VISITS": 1, **{c: k for c in counters}}))
        starts.append(space_start(f"counters-mixed-{k}", {"SLYLANDRO_PROBE_VISITS": 2,
                                                          **{c: (k + i) % 4 for i, c in enumerate(counters)}}))
    starts.append(space_start("destruct-code", {"DESTRUCT_CODE_ON_SHIP": 1}))
    starts.append(space_start("destruct-code-in-a-system", {"DESTRUCT_CODE_ON_SHIP": 1, "SLYLANDRO_PROBE_ID": 3},
                              "IN_INTERPLANETARY"))
    places = [("b-corvi", universe_to_log(333, 9812)),
              ("plus-plus", universe_to_log(9999, 0)),
              ("minus-minus", universe_to_log(0, 9999)),
              ("teens", universe_to_log(333 + 1234, 9812 - 3156)),
              ("tens", universe_to_log(333 + 200, 9812 - 2207)),
              ("hundreds", universe_to_log(333 + 3000, 9812 - 5003)),
              ("between-units", (universe_to_log(4321, 4567)[0] + 7, universe_to_log(4321, 4567)[1] - 5)),
              ("sol", SOL_LOG)]
    for name, log in places:
        # The whole talk from B Corvi; elsewhere "who are you" alone (the
        # third reply offered, the destruct code not aboard).
        starts.append(space_start(f"coordinates-{name}", {"SLYLANDRO_PROBE_ID": 3}, log=list(log),
                                  **({} if name == "b-corvi" else {"prefix": [2], "follow": []})))
    return starts


def home_start(name, state, flags=1 << 7, **kw):
    """A start for a race's world's talk (a generator's InitCommunication):
    in a system, GLOBAL_FLAGS_AND_DATA as the generator sets it."""
    start = {"name": name, "state": {**({"GLOBAL_FLAGS_AND_DATA": flags} if flags else {}), **state},
             "ship": talk_ship(), "activity": "IN_INTERPLANETARY"}
    start.update(kw)
    return start


def spahome_starts():
    """The Spathi at home (spahome.c, Spathiwa's moon), from states chosen
    to reach every condition it reads: the password asked (each visit, the
    visits past those the switch names, the names spliced in), known from
    the Melnorme before any visit (SPATHI_HOME_VISITS 7) or from Fwiffo
    (FOUND_PLUTO_SPATHI 1 to 3, with him in the escorts, after another
    ship, or not), the council (each SPATHI_MANNER), angry with the password
    told, hating, the quest learnt (the Evil Ones told of, already
    eliminated), on the quest (lied about once or twice, the creatures
    examined, eliminated), the party (each visit) and the allies (each
    visit, each piece of news: SPATHI_INFO); the fights' post-encounter
    from each manner."""
    password = {"KNOW_SPATHI_PASSWORD": 1, "SPATHI_HOME_VISITS": 7}
    allied = {"alliances": {"SPATHI_SHIP": "good"}}
    starts = [home_start("first", {}),
              # The council and on from the Melnorme's password: the whole talk.
              home_start("melnorme-password", password),
              home_start("melnorme-password-fwiffo", {**password, "FOUND_PLUTO_SPATHI": 2}, follow=[]),
              home_start("council", {"KNOW_SPATHI_PASSWORD": 1},
                         follow=["SpathiCouncil", "ExitConversation"]),
              home_start("council-at-peace", {"KNOW_SPATHI_PASSWORD": 1, "SPATHI_MANNER": 3},
                         follow=["SpathiCouncil", "AllianceOffer", "ExitConversation"]),
              home_start("angry", {"KNOW_SPATHI_PASSWORD": 1, "SPATHI_MANNER": 1},
                         follow=["SpathiAngry", "AllianceOffer", "ExitConversation"]),
              home_start("angry-password-from-melnorme", {**password, "SPATHI_MANNER": 1},
                         follow=["SpathiPassword", "SpathiCouncil", "ExitConversation"]),
              home_start("angry-fwiffo", {"FOUND_PLUTO_SPATHI": 1, "SPATHI_MANNER": 1},
                         follow=["SpathiPassword", "SpathiCouncil", "ExitConversation"]),
              home_start("hating", {"SPATHI_MANNER": 2}),
              home_start("hating-password", {"SPATHI_MANNER": 2, "KNOW_SPATHI_PASSWORD": 1}, follow=[])]
    for visits in range(1, 8):
        starts.append(home_start(f"password-visit-{visits}", {"SPATHI_HOME_VISITS": visits}, follow=[]))
    for cname, captain, ship in (("long-names", "Captain Fifteen", "Fifteen bytes!!"),
                                 ("accented-names", "Zoë", "Ñandú"),
                                 ("empty-names", "", "")):
        start = home_start(f"password-{cname}", {}, follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    # Fwiffo: found on Pluto (1), met after (2), told of before (3); in the
    # escorts (with another ship before him) or not.
    for found in (1, 2, 3):
        for fname, fleet in (("", []), ("-escort", ["SPATHI_SHIP"]), ("-escort-second", ["HUMAN_SHIP", "SPATHI_SHIP"]),
                             ("-other-escort", ["HUMAN_SHIP"])):
            if found == 3 and fname:
                continue
            starts.append(home_start(f"fwiffo-{found}{fname}", {"FOUND_PLUTO_SPATHI": found},
                                     escort_fleet=fleet, escorts=[0, 12 - len(fleet)],
                                     follow=["SpathiPassword", "SpathiCouncil", "ExitConversation"]))
    starts.append(home_start("fwiffo-1-told", {"FOUND_PLUTO_SPATHI": 1, "KNOW_SPATHI_PASSWORD": 1},
                             escort_fleet=["SPATHI_SHIP"], escorts=[0, 11],
                             follow=["SpathiCouncil", "ExitConversation"]))
    # The quest learnt and not taken: the Evil Ones told of or not, already eliminated or not.
    for evil in (0, 1):
        for gone in (0, 1):
            starts.append(home_start(f"learn-quest-evil-{evil}-eliminated-{gone}",
                                     {"KNOW_SPATHI_QUEST": 1, "KNOW_SPATHI_EVIL": evil,
                                      "SPATHI_CREATURES_ELIMINATED": gone}))
    # On the quest.
    for lied in range(3):
        for examined in (0, 1):
            for gone in (0, 1):
                starts.append(home_start(f"quest-lied-{lied}-examined-{examined}-eliminated-{gone}",
                                         {"SPATHI_QUEST": 1, "SPATHI_MANNER": 3, "LIED_ABOUT_CREATURES": lied,
                                          "SPATHI_CREATURES_EXAMINED": examined,
                                          "SPATHI_CREATURES_ELIMINATED": gone}))
    for visits in range(4):
        starts.append(home_start(f"party-visit-{visits}", {"SPATHI_PARTY": 1, "SPATHI_MANNER": 3,
                                                           "SPATHI_HOME_VISITS": visits}))
        starts.append(home_start(f"allied-visit-{visits}", {"SPATHI_PARTY": 1, "SPATHI_MANNER": 3,
                                                            "SPATHI_HOME_VISITS": visits},
                                 **allied, **({"follow": []} if visits else {})))
    for info in range(1, 6):
        starts.append(home_start(f"allied-info-{info}", {"SPATHI_MANNER": 3, "SPATHI_HOME_VISITS": 1,
                                                         "SPATHI_INFO": info},
                                 **allied, follow=["SpathiAllies"]))
    return starts


def slyhome_starts():
    """The Slylandro at home (slyhome.c, their gas giant), from states
    chosen to reach every condition it reads: each visit (the hello and
    the goodbye; the visits past those the switch names), the recall
    greeting once the code is known (RECALL_VISITS), the names spliced in,
    and each stack of questions walked on its own (the others asked to
    their ends, so the walks stay small): the visitors (SLYLANDRO_STACK1
    to 3), the humans (STACK4 and what they know), the Slylandro
    themselves, the probes (STACK5 to 8, with the bug shown or not), the
    probe's programming (STACK9 and what the player knows) and the bug
    and its fix, the self-destruct code with each SLYLANDRO_MULTIPLIER
    (its ramp-down run at once: recorded as AddEvent)."""
    done = {"SLYLANDRO_HOME_VISITS": 1, "SLYLANDRO_STACK1": 7, "SLYLANDRO_STACK2": 1, "SLYLANDRO_STACK3": 3}
    probes = {**done, "SLYLANDRO_STACK4": 2, "PLAYER_KNOWS_PROBE": 1}
    function = {**probes, "PROBE_EXHIBITED_BUG": 1, "SLYLANDRO_STACK5": 1, "SLYLANDRO_STACK6": 1,
                "SLYLANDRO_STACK7": 3, "SLYLANDRO_STACK8": 2}
    knows = {**function, "SLYLANDRO_STACK8": 3, "SLYLANDRO_STACK9": 3, "PLAYER_KNOWS_PROGRAM": 1,
             "PLAYER_KNOWS_PRIORITY": 1, "PLAYER_KNOWS_EFFECTS": 1, "SLYLANDRO_MULTIPLIER": 2}
    starts = [home_start("first", {}, 0, follow=["HomeWorld", "ExitConversation"])]
    for visits in range(1, 8):
        starts.append(home_start(f"visit-{visits}", {"SLYLANDRO_HOME_VISITS": visits}, 0,
                                 follow=["ExitConversation"]))
    for cname, captain, ship in (("long-names", "Captain Fifteen", "Fifteen bytes!!"),
                                 ("accented-names", "Zoë", "Ñandú"),
                                 ("empty-names", "", "")):
        start = home_start(f"names-{cname}", {}, 0, follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    starts += [
        home_start("recall", {"SLYLANDRO_KNOW_BROKEN": 1, "SLYLANDRO_HOME_VISITS": 1, "PLAYER_KNOWS_PROBE": 1}, 0,
                   follow=["ExitConversation"]),
        home_start("recall-first-visit", {"SLYLANDRO_KNOW_BROKEN": 1}, 0, follow=["ExitConversation"]),
        home_start("recalled", {"SLYLANDRO_KNOW_BROKEN": 1, "RECALL_VISITS": 1, "SLYLANDRO_HOME_VISITS": 2,
                                "PLAYER_KNOWS_PROBE": 1}, 0, follow=["ExitConversation"]),
        home_start("recalled-3", {"SLYLANDRO_KNOW_BROKEN": 1, "RECALL_VISITS": 3}, 0, follow=[]),
        home_start("humans", done, 0, follow=["HumanInfo", "HomeWorld", "ExitConversation"]),
        home_start("humans-know-more", {**done, "SLYLANDRO_STACK4": 1, "SLYLANDRO_KNOW_EARTH": 1,
                                        "SLYLANDRO_KNOW_EXPLORE": 1, "SLYLANDRO_KNOW_URQUAN": 2,
                                        "SLYLANDRO_KNOW_GATHER": 1}, 0,
                   follow=["HumanInfo", "HomeWorld", "ExitConversation"]),
        home_start("slylandro", {**done, "SLYLANDRO_STACK4": 2}, 0,
                   follow=["SlylandroInfo", "HomeWorld", "ExitConversation"]),
        home_start("probes", probes, 0, follow=["ProbeInfo", "HomeWorld", "ExitConversation"]),
        home_start("probes-bug", {**probes, "PROBE_EXHIBITED_BUG": 1}, 0,
                   follow=["ProbeInfo", "HomeWorld", "ExitConversation"]),
        # The probe's programming: know_more_probe, talk_more_probe_attack.
        home_start("probe-function", function, 0, prefix=[1, 0], follow=["ProbeFunction", "ProbeInfo"]),
        # The bug and its fix: know_more_probe, talk_more_probe_attack, probe_has_bug.
        home_start("probe-bug", knows, 0, prefix=[1, 0, 0], follow=["ProbeBug", "FixBug", "HomeWorld",
                                                                   "ExitConversation"])]
    # The code asked for with each multiplier (the ramp up's 0 to 4, and
    # the 3-bit field's last): think_about_rep_priorities through
    # what_about_rep_priorities, then mega_self_destruct.
    for m in (0, 1, 4, 7):
        starts.append(home_start(f"self-destruct-multiplier-{m}", {**knows, "SLYLANDRO_MULTIPLIER": m}, 0,
                                 prefix=[1, 0, 0, 0, 0, 0, 3], follow=[]))
    return starts


def melnorm_starts():
    """The Melnorme (melnorm.c), from states chosen to reach every
    condition it reads, each walk kept small by its credits and room or by
    follow and prefix: the first meeting (its small talk's stacks walked
    whole, the bluster, the alliance's names in its first reply, the hello
    of each later meeting before business); getting down to business with
    something to trade or nothing; the hello and down to business at each
    YACK_STACK2; the trade menu (the first trade, why purple, out of
    credits, nothing to sell); fuel (by the tanks' room and the credits,
    filling up, no room, past 0x3FFF at once, the first time); each of the
    catalog's 13 items with those before it had (modules by their prices,
    lander upgrades by the ship's fields), short of credits, all gone;
    information (each stack's last, the two pieces that set story fields,
    each way, all gone); selling bio data and rainbow worlds (with and
    without the other, the count past the worlds' bits); the anger (each
    level's hellos and stacks, the apology, the strip to calm them worth it
    or not, the fights); the rescue (each count's line, refused before,
    and the strip from ships and places that give an offer, a full tank,
    charity, and the searches' byte wrapping as the real ship's worth stays
    while the copy has nothing left to take)."""
    met = {"MET_MELNORME": 1}
    business = {**met, "MELNORME_YACK_STACK2": 6, "WHY_MELNORME_PURPLE": 1, "TRADED_WITH_MELNORME": 1}
    full = talk_ship(fuel=6000)       # a new game's ship, its tanks full: no fuel to buy
    empty = talk_ship(fuel=0)
    starts = [
        # The first meeting's small talk, its bluster and back (not business: below).
        {"name": "first", "state": {}, "ship": talk_ship(), "follow": ["DoFirstMeeting", "DoBluster", "ExitConversation"]},
        {"name": "first-business-1", "state": {"MELNORME_BUSINESS_COUNT": 1}, "ship": talk_ship(), "follow": []},
        {"name": "first-business-2", "state": {"MELNORME_BUSINESS_COUNT": 2}, "ship": talk_ship(), "follow": []},
        {"name": "met-before-business", "state": {**met, "MELNORME_YACK_STACK2": 5, "MELNORME_BUSINESS_COUNT": 2,
                                                  "MELNORME_YACK_STACK0": 2, "MELNORME_YACK_STACK1": 2},
         "ship": talk_ship(), "follow": ["DoFirstMeeting", "ExitConversation"]},
        # get_on_with_business (third offered with STACK1 1): to trade, or to the rescue.
        {"name": "get-on-with-business", "state": {**met, "MELNORME_YACK_STACK1": 1}, "ship": talk_ship(),
         "prefix": [2], "follow": []},
        {"name": "get-on-with-business-stranded", "state": {**met, "MELNORME_YACK_STACK1": 1}, "ship": empty,
         "prefix": [2], "follow": ["DoRescue", "ExitConversation"]},
    ]
    for alliance in range(4):
        starts.append({"name": f"first-alliance-name-{alliance}", "state": {"NEW_ALLIANCE_NAME": alliance},
                       "ship": talk_ship(), "follow": []})
    starts.append({"name": "first-alliance-name-3-accented", "state": {"NEW_ALLIANCE_NAME": 3},
                   "ship": talk_ship(captain="Zo\u00eb", ship="\u00d1and\u00fa"), "follow": []})
    # Down to business at each YACK_STACK2 (the hello lines, the last kept).
    for stack in range(6, 16):
        starts.append({"name": f"hello-{stack}", "state": {**business, "MELNORME_YACK_STACK2": stack},
                       "ship": talk_ship(), "follow": []})
    # The trade menu: the first trade, why purple, out of credits, nothing to sell.
    starts.append({"name": "trade-first", "state": {**met, "MELNORME_YACK_STACK2": 6}, "ship": full})
    for name, state, ship in (("fuel-only", {}, full), ("bio-only", {}, talk_ship(fuel=0)),
                              ("credits-only", {"MELNORME_CREDIT0": 1}, empty),
                              ("rainbow-only", {"RAINBOW_WORLD0": 1}, empty)):
        start = {"name": f"trade-{name}", "state": {**business, **state}, "ship": ship, "follow": []}
        if name == "bio-only":
            start["bio"] = 1
        starts.append(start)
    # Fuel: 30 units of room and 30 credits (fill up just affordable), 20 credits (not), no room for 25,
    # a full tank once bought, the first time.
    for name, fuel, credits, extra in (("room-30-credits-30", 3000, 30, {}), ("room-30-credits-20", 3000, 20, {}),
                                       ("room-10", 5000, 40, {}), ("first-time", 5990, 3, {"MELNORME_FUEL_PROCEDURE": 0}),
                                       ("empty-tank", 0, 2, {})):
        starts.append({"name": f"fuel-{name}", "state": {**business, "MELNORME_FUEL_PROCEDURE": 1,
                                                          "MELNORME_INFO_PROCEDURE": 1, "MELNORME_TECH_PROCEDURE": 1,
                                                          "MELNORME_CREDIT0": credits, **extra},
                       "ship": talk_ship(fuel=fuel),
                       "prefix": [0], "follow": ["DoBuy"]})
    # Past 0x3FFF at once: four high-efficiency tanks (410 units), empty, filled up with 500 credits.
    he = ["EMPTY", "HIGHEFF_FUELSYS", "CREW_POD", "HIGHEFF_FUELSYS", "HIGHEFF_FUELSYS", "HIGHEFF_FUELSYS"] + \
        ["EMPTY"] * 2 + ["STORAGE_BAY"] + ["EMPTY"] * 6 + ["GUN_WEAPON"]
    starts.append({"name": "fuel-fill-past-0x3fff", "state": {**business, "MELNORME_FUEL_PROCEDURE": 1,
                                                               "MELNORME_CREDIT0": 500 & 0xFF, "MELNORME_CREDIT1": 500 >> 8},
                   "ship": talk_ship(fuel=0, modules=he), "prefix": [0, 0, 4], "follow": []})
    # The catalog: each item with those before it had, 150 credits (one bought, the next shown).
    techs = [("module", "BLASTER_WEAPON"), ("state", "IMPROVED_LANDER_SPEED"), ("module", "ANTIMISSILE_DEFENSE"),
             ("shield", 0), ("state", "IMPROVED_LANDER_CARGO"), ("module", "HIGHEFF_FUELSYS"),
             ("state", "IMPROVED_LANDER_SHOT"), ("shield", 1), ("module", "TRACKING_SYSTEM"), ("shield", 2),
             ("shield", 3), ("module", "CANNON_WEAPON"), ("module", "SHIVA_FURNACE")]
    for k in range(len(techs) + 1):
        state = {**business, "MELNORME_FUEL_PROCEDURE": 1, "MELNORME_TECH_PROCEDURE": 1 if k else 0,
                 "MELNORME_CREDIT0": 150}
        costs = dict(NEW_GAME_COSTS)
        shields = 0
        for kind, what in techs[:k]:
            if kind == "module":
                costs[what] = 1
            elif kind == "state":
                state[what] = 1
            else:
                shields |= 1 << what
        if shields:
            state["LANDER_SHIELDS"] = shields
        # buy, buy_technology (second with fuel full), then the item bought.
        starts.append({"name": f"tech-{k}", "state": state, "ship": full, "costs": costs,
                       "prefix": [0, 0], "follow": ["DoBuy"] if k == 0 else []})
        if 0 < k < len(techs):
            starts.append({"name": f"tech-{k}-buy", "state": state, "ship": full, "costs": costs,
                           "prefix": [0, 0, 0], "follow": []})
    starts.append({"name": "tech-short-of-credits", "state": {**business, "MELNORME_TECH_PROCEDURE": 1,
                                                               "MELNORME_CREDIT0": 100},
                   "ship": full, "prefix": [0, 0, 0], "follow": []})
    # Information: each stack's last with 150 credits, the two pieces that set story fields, all gone.
    info = {**business, "MELNORME_INFO_PROCEDURE": 1, "MELNORME_TECH_PROCEDURE": 1, "MELNORME_CREDIT0": 150}
    starts.append({"name": "info-first-time", "state": {**info, "MELNORME_INFO_PROCEDURE": 0}, "ship": full,
                   "prefix": [0], "follow": ["DoBuy"]})
    starts.append({"name": "info-last-each", "state": {**info, "MELNORME_EVENTS_INFO_STACK": 7,
                                                        "MELNORME_ALIEN_INFO_STACK": 15,
                                                        "MELNORME_HISTORY_INFO_STACK": 8},
                   "ship": full, "prefix": [0], "follow": ["DoBuy"]})
    for pluto in (0, 1):
        starts.append({"name": f"info-password-pluto-{pluto}",
                       "state": {**info, "MELNORME_ALIEN_INFO_STACK": 13, "FOUND_PLUTO_SPATHI": pluto,
                                 "MELNORME_EVENTS_INFO_STACK": 8, "MELNORME_HISTORY_INFO_STACK": 9},
                       "ship": full, "prefix": [0, 1, 0], "follow": []})
    for shattered in range(4):
        starts.append({"name": f"info-shattered-{shattered}",
                       "state": {**info, "MELNORME_ALIEN_INFO_STACK": 14, "KNOW_ABOUT_SHATTERED": shattered,
                                 "MELNORME_EVENTS_INFO_STACK": 8, "MELNORME_HISTORY_INFO_STACK": 9},
                       "ship": full, "prefix": [0, 1, 0], "follow": []})
    starts.append({"name": "info-all-gone", "state": {**info, "MELNORME_EVENTS_INFO_STACK": 8,
                                                       "MELNORME_ALIEN_INFO_STACK": 16, "MELNORME_HISTORY_INFO_STACK": 9},
                   "ship": full, "prefix": [0], "follow": ["DoBuy"]})
    # Selling: bio data with and without new rainbow worlds, the worlds alone, the count past the bits.
    for name, bio, worlds, count in (("bio", 7, 0, 0), ("bio-and-rainbows", 3, 0b101, 1), ("rainbows", 0, 0b11, 0),
                                     ("rainbows-high-bits", 0, 0x300, 0), ("rainbows-some-sold", 0, 0b1011, 2)):
        state = {**business, "RAINBOW_WORLD0": worlds & 0xFF, "RAINBOW_WORLD1": worlds >> 8,
                 "MELNORME_RAINBOW_COUNT": count}
        starts.append({"name": f"sell-{name}", "state": state, "ship": full, "bio": bio,
                       "follow": ["DoSell", "NatureOfConversation"]})
    # The anger.
    for count in range(3):
        starts.append({"name": f"miffed-{count}", "state": {**met, "MELNORME_ANGER": 1, "MELNORME_MIFFED_COUNT": count},
                       "ship": talk_ship(), "follow": ["DoMelnormeMiffed", "ExitConversation"] if count == 0 else []})
        starts.append({"name": f"pissed-{count}", "state": {**met, "MELNORME_ANGER": 2, "MELNORME_PISSED_COUNT": count},
                       "ship": talk_ship(), "follow": ["DoMelnormePissed", "ExitConversation"] if count == 0 else []})
        starts.append({"name": f"hate-{count}", "state": {**met, "MELNORME_ANGER": 3, "MELNORME_HATE_COUNT": count},
                       "ship": talk_ship()})
    starts.append({"name": "miffed-apology", "state": {**met, "MELNORME_ANGER": 1, "MELNORME_YACK_STACK3": 4},
                   "ship": talk_ship(), "prefix": [0], "follow": []})
    # ok_strip_me (YACK_STACK4 2): worth it (cargo, fuel past the reserve, a spare crew pod), and not.
    rich = talk_ship(crew=60, fuel=4000, landers=3, elements=[5, 0, 3, 0, 0, 0, 1, 0],
                     modules=["EMPTY", "FUEL_TANK", "CREW_POD", "CREW_POD", "CREW_POD"] + ["EMPTY"] * 3 +
                     ["STORAGE_BAY", "DYNAMO_UNIT"] + ["EMPTY"] * 5 + ["GUN_WEAPON"])
    poor = talk_ship(landers=0, drives=[], jets=[5],
                     modules=["EMPTY", "EMPTY", "CREW_POD"] + ["EMPTY"] * 13)
    for name, ship, bio in (("rich", rich, 9), ("poor", poor, 0), ("new-game", talk_ship(), 0)):
        starts.append({"name": f"strip-me-{name}", "state": {**met, "MELNORME_ANGER": 2, "MELNORME_YACK_STACK4": 2},
                       "ship": ship, "bio": bio, "prefix": [0], "follow": []})
    # The rescue: each count's line, refused before.
    rescue = {**met, "MELNORME_YACK_STACK2": 6}
    for count in range(8):
        starts.append({"name": f"rescue-count-{count}", "state": {**rescue, "MELNORME_RESCUE_COUNT": count},
                       "ship": empty, **({"follow": []} if count else {})})
    starts.append({"name": "rescue-refused", "state": {**rescue, "MELNORME_RESCUE_REFUSED": 1}, "ship": empty})
    # The strip offered: from ships and places (the seed's), a full tank (RESCUE_TANKS), charity (a ship
    # worth nothing), the searches wrapping (the copy stripped bare short of the fuel's worth).
    places = [("near-sol", (1800, 1500)), ("far", (9000, 9000)), ("corner", (0, 0)), ("mid", (5000, 4000))]
    for pname, (ux, uy) in places:
        for sname, ship in (("new-game", talk_ship(fuel=0)), ("rich", {**rich, "fuel": 0}), ("poor", {**poor, "fuel": 0})):
            starts.append({"name": f"strip-{sname}-{pname}", "state": rescue, "ship": ship,
                           "log": list(universe_to_log(ux, uy)), "prefix": [0]})
    bare = talk_ship(fuel=0, landers=0, drives=[], jets=[], modules=["EMPTY", "EMPTY", "CREW_POD"] + ["EMPTY"] * 13)
    starts.append({"name": "strip-charity", "state": rescue, "ship": bare, "log": list(universe_to_log(9000, 9000)),
                   "prefix": [0]})
    starts.append({"name": "strip-refused-before", "state": {**rescue, "MELNORME_RESCUE_REFUSED": 1},
                   "ship": talk_ship(fuel=0), "log": list(universe_to_log(6000, 2000)), "prefix": [0]})
    return starts


NAMES = (("long-names", "Captain Fifteen", "Fifteen bytes!!"), ("accented-names", "Zoë", "Ñandú"),
         ("empty-names", "", ""))


def shofixt_starts():
    """Tanaka (or Katana) at the Shofixti's system (shofixt.c), from
    states chosen to reach every condition it reads: the endgame's won
    battle; hostile, each visit's greeting for Tanaka and for Katana
    (SHOFIXTI_KIA; the visits past those the switch names), each
    combination of the three stacks of hostile replies (SHOFIXTI_STACK1 to
    3, with the Maidens aboard or not), each count of "don't want to fight"
    (STACK4), the names spliced into "I am captain" (each alliance name,
    the captain's and ship's names); friendly (STACK2 past 2, its own
    bits' last too), each value of the friendly stacks (STACK1, STACK3) for
    Tanaka and Katana, with the Maidens aboard (the recruitment) or not."""
    sys_start = lambda name, state, **kw: space_start(name, state, "IN_INTERPLANETARY", **kw)
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE")]
    for kia in (0, 1):
        who = "katana" if kia else "tanaka"
        for visits in range(8):
            starts.append(sys_start(f"{who}-visit-{visits}", {"SHOFIXTI_KIA": kia, "SHOFIXTI_VISITS": visits},
                                    **({} if visits == 0 else {"follow": []})))
    for s1 in range(4):
        for s2 in range(3):
            for s3 in range(4):
                for maidens in ((0, 1) if s2 == 2 else (0,)):
                    starts.append(sys_start(f"stacks-{s1}-{s2}-{s3}" + ("-maidens" if maidens else ""),
                                            {"SHOFIXTI_VISITS": 1, "SHOFIXTI_STACK1": s1, "SHOFIXTI_STACK2": s2,
                                             "SHOFIXTI_STACK3": s3, "MAIDENS_ON_SHIP": maidens}))
    starts.append(sys_start("stacks-katana-look", {"SHOFIXTI_KIA": 1, "SHOFIXTI_VISITS": 1, "SHOFIXTI_STACK3": 1}))
    for fights in range(1, 4):
        starts.append(sys_start(f"dont-want-to-fight-{fights}", {"SHOFIXTI_VISITS": 1, "SHOFIXTI_STACK4": fights}))
    # "I am captain ...": donkey_breath (the second reply with STACK1 3 and STACK3 3).
    for alliance in range(4):
        starts.append(sys_start(f"captain-alliance-name-{alliance}",
                                {"SHOFIXTI_STACK1": 3, "SHOFIXTI_STACK2": 2, "SHOFIXTI_STACK3": 3,
                                 "NEW_ALLIANCE_NAME": alliance}, prefix=[0], follow=[]))
    for cname, captain, ship in NAMES:
        start = sys_start(f"captain-{cname}", {"SHOFIXTI_STACK1": 3, "SHOFIXTI_STACK2": 2, "SHOFIXTI_STACK3": 3,
                                               "NEW_ALLIANCE_NAME": 3}, prefix=[0], follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    for kia in (0, 1):
        who = "katana" if kia else "tanaka"
        for maidens in (0, 1):
            starts.append(sys_start(f"friendly-{who}" + ("-maidens" if maidens else ""),
                                    {"SHOFIXTI_KIA": kia, "SHOFIXTI_STACK2": 3, "MAIDENS_ON_SHIP": maidens}))
        for s1 in range(4):
            for s3 in range(4):
                starts.append(sys_start(f"friendly-{who}-stacks-{s1}-{s3}",
                                        {"SHOFIXTI_KIA": kia, "SHOFIXTI_STACK2": 3, "SHOFIXTI_STACK1": s1,
                                         "SHOFIXTI_STACK3": s3}, follow=[]))
    starts.append(sys_start("friendly-stack2-7", {"SHOFIXTI_STACK2": 7}, follow=[]))
    return starts


def zoqfot_starts():
    """The Zoq-Fot-Pik (zoqfotc.c), from states chosen to reach every
    condition it reads: the endgame's won battle; hostile, each visit
    (ZOQFOT_HOME_VISITS, the visits past those the switch names); the first
    meeting at their homeworld (GLOBAL_FLAGS_AND_DATA bit 7) and with their
    scout (no bit, in a system), each walked whole, with each alliance's
    and ship's names in "we are"; the thanks for a rescue from distress
    (ZOQFOT_DISTRESS, each value; the host's fleet with room for the 4
    ships, for some, for none); the neutral homeworld's and the allies'
    greetings (each visit), the neutral one's walked whole (the alliance
    made in it, and the allies' questions after it); each piece of the
    homeworld's news (ZOQFOT_INFO); and the war's news by what they've
    told (ZOQFOT_KNOW_MASK), the Kohr-Ah's frenzy, the Utwig and Supox's
    mission (UTWIG_SUPOX_MISSION) and the date (the years to the Kohr-Ah's
    victory, its last year from March)."""
    home = lambda name, state, **kw: home_start(name, {"MET_ZOQFOT": 1, **state}, **kw)
    allied = {"alliances": {"ZOQFOTPIK_SHIP": "good"}}
    starts = [home_start("won-last-battle", {}, activity="WON_LAST_BATTLE"),
              home_start("first-home", {}),
              space_start("first-scout", {}, "IN_INTERPLANETARY")]
    for alliance in range(4):
        starts.append(home_start(f"first-alliance-name-{alliance}", {"NEW_ALLIANCE_NAME": alliance}, follow=[]))
    for cname, captain, ship in NAMES:
        start = home_start(f"first-{cname}", {"NEW_ALLIANCE_NAME": 3}, follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    for visits in range(8):
        starts.append(home("hostile-visit-%d" % visits, {"ZOQFOT_HOSTILE": 1, "ZOQFOT_HOME_VISITS": visits}))
        starts.append(home(f"neutral-visit-{visits}", {"ZOQFOT_HOME_VISITS": visits},
                           **({} if visits == 0 else {"follow": []})))
        # Allied: the replies walked from the neutral homeworld's alliance made.
        starts.append(home(f"allied-visit-{visits}", {"ZOQFOT_HOME_VISITS": visits}, **allied, follow=[]))
    for distress in (1, 2, 3):
        starts.append(home(f"distress-{distress}", {"ZOQFOT_DISTRESS": distress}, escorts=[0, 12], **allied,
                           follow=[]))
    for room in (2, 0):
        starts.append(home(f"distress-room-{room}", {"ZOQFOT_DISTRESS": 1}, escorts=[0, room], **allied, follow=[]))
    for info in range(4):
        starts.append(home(f"info-{info}", {"ZOQFOT_INFO": info}, prefix=[0], follow=[]))
    # The war's news: any_war_news, the second reply.
    dates = [(2155, 2, 17), (2158, 2, 28), (2158, 3, 1), (2159, 2, 28), (2159, 3, 1), (2160, 6, 1)]
    for mask in (0, 1, 2, 6):
        for mission in (0, 1, 4, 5):
            for date in dates:
                starts.append(home(f"war-news-mask-{mask}-mission-{mission}-{date[0]}-{date[1]}",
                                   {"ZOQFOT_KNOW_MASK": mask, "UTWIG_SUPOX_MISSION": mission},
                                   date=list(date), prefix=[1], follow=[]))
    for mask in (0, 7, 8, 15):
        starts.append(home(f"war-news-frenzy-mask-{mask}", {"ZOQFOT_KNOW_MASK": mask, "KOHR_AH_FRENZY": 1},
                           prefix=[1], follow=[]))
    return starts


def vux_starts():
    """The VUX (vuxc.c), from states chosen to reach every condition it
    reads: the endgame's won battle (in space and at ZEX's world); in
    space and at their homeworld (GLOBAL_FLAGS_AND_DATA bit 7), each
    visit's greeting (VUX_VISITS, VUX_HOME_VISITS, the visits past those
    the switch names), the first walked whole; each pair of the reasons
    asked and the threats (VUX_STACK_1, every value of its 4 bits: the
    apologies stop it at 14, past "the truth", and 15 only a start
    reaches; VUX_STACK_2), the news (VUX_INFO), the
    friendship asked (VUX_STACK_3) and the farewells (VUX_STACK_4);
    Admiral ZEX at the Maidens' world (bit 6): each visit's greeting
    (ZEX_VISITS, before and after he's been threatened: ZEX_STACK_3 2),
    walked whole from his first meeting with and without the beast aboard
    (VUX_BEAST_ON_SHIP) and with his wish known (KNOW_ZEX_WANTS_MONSTER),
    and each combination of his three stacks (ZEX_STACK_1 to 3)."""
    zex = lambda name, state, **kw: home_start(name, state, flags=1 << 6, **kw)
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE"),
              zex("zex-won-last-battle", {}, activity="WON_LAST_BATTLE")]
    for visits in range(8):
        more = {} if visits == 0 else {"follow": []}
        starts.append(space_start(f"space-visit-{visits}", {"VUX_VISITS": visits}, **more))
        starts.append(home_start(f"home-visit-{visits}", {"VUX_HOME_VISITS": visits}, **more))
    for s1 in range(16):
        for s2 in range(4):
            starts.append(space_start(f"stacks-{s1}-{s2}", {"VUX_VISITS": 1, "VUX_STACK_1": s1, "VUX_STACK_2": s2}))
    for info in range(1, 4):
        starts.append(space_start(f"info-{info}", {"VUX_VISITS": 1, "VUX_INFO": info}))
    for s1 in (14, 15):
        for s3 in range(1, 4):
            starts.append(space_start(f"friends-{s1}-{s3}", {"VUX_VISITS": 1, "VUX_STACK_1": s1, "VUX_STACK_3": s3}))
    for s4 in range(1, 4):
        starts.append(space_start(f"farewell-{s4}", {"VUX_VISITS": 1, "VUX_STACK_4": s4}))
    for visits in range(8):
        for threatened in (0, 2):
            starts.append(zex(f"zex-visit-{visits}-stack3-{threatened}", {"ZEX_VISITS": visits, "ZEX_STACK_3": threatened},
                              **({} if visits == 0 else {"follow": []})))
    for beast in (0, 1):
        for know in (0, 1):
            starts.append(zex(f"zex-beast-{beast}-know-{know}", {"VUX_BEAST_ON_SHIP": beast,
                                                                 "KNOW_ZEX_WANTS_MONSTER": know}))
    for z1 in range(4):
        for z2 in range(4):
            for z3 in range(4):
                starts.append(zex(f"zex-stacks-{z1}-{z2}-{z3}", {"ZEX_VISITS": 1, "ZEX_STACK_1": z1, "ZEX_STACK_2": z2,
                                                                 "ZEX_STACK_3": z3}, follow=[]))
    for z1 in (2, 3):
        starts.append(zex(f"zex-creature-known-{z1}", {"ZEX_VISITS": 1, "ZEX_STACK_1": z1, "KNOW_ZEX_WANTS_MONSTER": 1}))
    starts.append(zex("zex-beast-known-stacks", {"ZEX_VISITS": 1, "ZEX_STACK_1": 3, "ZEX_STACK_2": 2, "ZEX_STACK_3": 3,
                                                 "KNOW_ZEX_WANTS_MONSTER": 1, "VUX_BEAST_ON_SHIP": 1}))
    return starts


def yehat_starts():
    """The Yehat (yehatc.c), from states chosen to reach every condition it
    reads: the endgame's won battle; in space and at their homeworld
    (GLOBAL_FLAGS_AND_DATA bit 7), before and after the civil war
    (YEHAT_CIVIL_WAR: the royalists), each visit's greeting (the visits
    past those the switch names), the first of each walked whole; each
    value of the space stacks (YEHAT_REBEL_INFO, NO_YEHAT_HELP_SPACE,
    NO_YEHAT_ALLY_SPACE) and of the homeworld's and the royalists' (their
    news, NO_YEHAT_HELP_HOME, NO_YEHAT_INFO, NO_YEHAT_ALLY_HOME); the Pkunk
    asked about (met in space and at home, or one of them; absorbed or
    not; told already); the Shofixti alive once Tanaka has been met
    (SHOFIXTI_VISITS; YEHAT_REBEL_TOLD_PKUNK; the escort fleet with a
    Shofixti ship, another's, or none; YEHAT_REBEL_VISITS), the revolt
    sent; the names spliced into the alliance's demand (each alliance
    name, the captain's and ship's names), at home and in space; and the
    farewell in space from several seeds (its draw: peace or a fight)."""
    home = lambda name, state, **kw: home_start(name, state, **kw)
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE"),
              home_start("home-won-last-battle", {}, activity="WON_LAST_BATTLE")]
    for war in (0, 1):
        tag = "royalist-" if war else ""
        for visits in range(8):
            more = {} if visits == 0 else {"follow": []}
            starts.append(space_start(f"{tag}space-visit-{visits}", {"YEHAT_CIVIL_WAR": war, "YEHAT_VISITS": visits},
                                      **more))
            starts.append(home(f"{tag}home-visit-{visits}", {"YEHAT_CIVIL_WAR": war, "YEHAT_HOME_VISITS": visits},
                               **more))
    for info in range(8):
        starts.append(space_start(f"space-info-{info}", {"YEHAT_VISITS": 1, "YEHAT_REBEL_INFO": info}, follow=[]))
        if info < 4:
            # The news asked (the first reply while there's news: whats_up_space_N for 0 to 3).
            starts.append(space_start(f"space-info-{info}-asked", {"YEHAT_VISITS": 1, "YEHAT_REBEL_INFO": info},
                                      prefix=[0], follow=[]))
    for help_ in range(4):
        for ally in range(4):
            starts.append(space_start(f"space-help-{help_}-ally-{ally}",
                                      {"YEHAT_VISITS": 1, "NO_YEHAT_HELP_SPACE": help_, "NO_YEHAT_ALLY_SPACE": ally}))
    for info in (0, 1):
        for help_ in (0, 1):
            for noinfo in (0, 1):
                for ally in (0, 1):
                    starts.append(home(f"home-info-{info}-help-{help_}-noinfo-{noinfo}-ally-{ally}",
                                       {"YEHAT_HOME_VISITS": 1, "YEHAT_ROYALIST_INFO": info,
                                        "NO_YEHAT_HELP_HOME": help_, "NO_YEHAT_INFO": noinfo,
                                        "NO_YEHAT_ALLY_HOME": ally}))
        for noinfo in (0, 1):
            starts.append(space_start(f"royalist-info-{info}-noinfo-{noinfo}",
                                      {"YEHAT_CIVIL_WAR": 1, "YEHAT_VISITS": 1, "YEHAT_ROYALIST_INFO": info,
                                       "NO_YEHAT_INFO": noinfo}))
    pkunk_met = [("both", {"PKUNK_VISITS": 1, "PKUNK_HOME_VISITS": 1}), ("space", {"PKUNK_VISITS": 1}),
                 ("home", {"PKUNK_HOME_VISITS": 1})]
    for met, state in pkunk_met:
        for absorbed in (0, 1):
            for told in (0, 1):
                s = {**state, "YEHAT_ABSORBED_PKUNK": absorbed, "YEHAT_ROYALIST_TOLD_PKUNK": told}
                name = f"pkunk-{met}-absorbed-{absorbed}-told-{told}"
                starts.append(space_start(f"space-{name}", {"YEHAT_VISITS": 1, **s}))
                starts.append(home(f"home-{name}", {"YEHAT_HOME_VISITS": 1, **s}))
                starts.append(space_start(f"royalist-{name}", {"YEHAT_CIVIL_WAR": 1, "YEHAT_VISITS": 1, **s}))
    revolt = ["StartRevolt", "ExitConversation"]
    for told in (0, 1):
        for fleet_name, fleet in (("none", []), ("shofixti", ["SHOFIXTI_SHIP"]),
                                  ("spathi", ["SPATHI_SHIP"]), ("mixed", ["SPATHI_SHIP", "SHOFIXTI_SHIP"])):
            for rebel_visits in range(3):
                starts.append(space_start(f"shofixti-told-{told}-fleet-{fleet_name}-visits-{rebel_visits}",
                                          {"YEHAT_VISITS": 1, "SHOFIXTI_VISITS": 1, "YEHAT_REBEL_TOLD_PKUNK": told,
                                           "YEHAT_REBEL_VISITS": rebel_visits},
                                          escort_fleet=fleet, follow=revolt))
    for alliance in range(4):
        starts.append(space_start(f"space-alliance-name-{alliance}", {"YEHAT_VISITS": 1, "NEW_ALLIANCE_NAME": alliance},
                                  follow=[]))
        starts.append(home(f"home-alliance-name-{alliance}", {"YEHAT_HOME_VISITS": 1, "NEW_ALLIANCE_NAME": alliance},
                           follow=[]))
    for cname, captain, ship in NAMES:
        for where, make in (("space", space_start), ("home", home)):
            start = make(f"{where}-{cname}", {"NEW_ALLIANCE_NAME": 3}, follow=[])
            start["ship"] = talk_ship(captain=captain, ship=ship)
            starts.append(start)
    for seed in (1, 2, 3, 4):
        starts.append(space_start(f"farewell-seed-{seed}", {"YEHAT_VISITS": 1}, seed=seed, follow=["ExitConversation"]))
    return starts


def rebel_starts():
    """The Yehat rebels (rebel.c), from states chosen to reach every
    condition it reads: the endgame's last battle (the cavalry, with the
    escort fleet's room for none, some, 8 and more); each visit's greeting
    (YEHAT_REBEL_VISITS, the visits past those the switch names), the
    first walked whole; each value of the revolution's news
    (YEHAT_REBEL_INFO, its 3 bits); the Pkunk asked about (met in space and
    at home, or one of them; absorbed or not; told already); their ships
    asked for, by the next ships' date (none given yet; a year, a month or
    a day before it; on it; after it; the next date in January) and the
    escort fleet's room (none, fewer than 4, 4, more)."""
    sys_start = lambda name, state, **kw: space_start(name, state, "IN_INTERPLANETARY", **kw)
    starts = []
    for room in (0, 1, 2, 3, 8, 9, 12):
        starts.append(space_start(f"cavalry-room-{room}", {}, "IN_LAST_BATTLE", escorts=[0, room]))
    for visits in range(8):
        starts.append(space_start(f"visit-{visits}", {"YEHAT_REBEL_VISITS": visits},
                                  **({"escorts": [0, 12]} if visits == 0 else {"follow": []})))
    for info in range(8):
        starts.append(space_start(f"info-{info}", {"YEHAT_REBEL_VISITS": 1, "YEHAT_REBEL_INFO": info},
                                  follow=["Rebels"]))
    for met, state in (("both", {"PKUNK_VISITS": 1, "PKUNK_HOME_VISITS": 1}), ("space", {"PKUNK_VISITS": 1}),
                       ("home", {"PKUNK_HOME_VISITS": 1})):
        for absorbed in (0, 1):
            for told in (0, 1):
                starts.append(sys_start(f"pkunk-{met}-absorbed-{absorbed}-told-{told}",
                                        {"YEHAT_REBEL_VISITS": 1, **state, "YEHAT_ABSORBED_PKUNK": absorbed,
                                         "YEHAT_REBEL_TOLD_PKUNK": told}, follow=["Rebels"]))
    # any_ships, the second reply (with no Pkunk to ask about), by the next ships' date (2156-03-10).
    ship_date = {"YEHAT_SHIP_MONTH": 3, "YEHAT_SHIP_DAY": 10, "YEHAT_SHIP_YEAR": 1}
    dates = [("year-before", (2155, 6, 1)), ("month-before", (2156, 2, 28)), ("day-before", (2156, 3, 9)),
             ("on-the-day", (2156, 3, 10)), ("month-after", (2156, 4, 1)), ("year-after", (2157, 1, 1)),
             ("december", (2156, 12, 31))]
    for room in (0, 1, 3, 4, 12):
        starts.append(space_start(f"ships-never-given-room-{room}", {"YEHAT_REBEL_VISITS": 1}, escorts=[0, room],
                                  date=[2155, 2, 17], prefix=[1], follow=[]))
        for when, date in dates:
            starts.append(space_start(f"ships-{when}-room-{room}", {"YEHAT_REBEL_VISITS": 1, **ship_date},
                                      escorts=[0, room], date=list(date), prefix=[1], follow=[]))
    starts.append(space_start("ships-far-year", {"YEHAT_REBEL_VISITS": 1, "YEHAT_SHIP_MONTH": 1, "YEHAT_SHIP_YEAR": 31},
                              escorts=[0, 12], prefix=[1], follow=[]))
    return starts


def orz_starts():
    """The Orz (orzc.c), from states chosen to reach every condition it
    reads: the endgame's won battle; the first meeting (MET_ORZ_BEFORE: the
    hello and the computer's report after it) in space, at their homeworld
    (GLOBAL_FLAGS_AND_DATA bit 7) and at the Taalo protector's moon (bit
    6), walked whole, by each standing (ORZ_MANNER, the alliance or not);
    each visit's greeting by standing and place (ORZ_VISITS,
    ORZ_HOME_VISITS, TAALO_VISITS, the visits past those the switch names);
    the neutral talk walked whole from each of its stacks (ORZ_ANDRO_STATE,
    ORZ_STACK0 and 1, REFUSED_ORZ_ALLIANCE), with the names spliced into
    "we are"; the allies' questions (ORZ_GENERAL_INFO, ORZ_PERSONAL_INFO,
    ORZ_ANDRO_STATE) walked whole to the break over the Androsynth, with
    Orz ships in the escort fleet (among others, alone, none) taken out;
    ORZ_MANNER 3 with the alliance not made (CheckAlliance); the angry
    talk's news and each insult (ORZ_PERSONAL_INFO, the eighth's count
    kept); and the Taalo moon's questions by standing, leave to land for
    allies."""
    allied = {"alliances": {"ORZ_SHIP": "good"}}
    taalo = lambda name, state, **kw: home_start(name, state, flags=1 << 6, **kw)
    places = (("space", space_start), ("home", home_start), ("taalo", taalo))
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE"),
              taalo("taalo-won-last-battle", {}, activity="WON_LAST_BATTLE")]
    manners = (("neutral", 0, {}), ("angry", 1, {}), ("hostile", 2, {}), ("allied", 3, allied),
               ("unallied-3", 3, {}))
    for where, make in places:
        for mname, manner, kw in manners:
            starts.append(make(f"first-{where}-{mname}", {"ORZ_MANNER": manner}, escort_fleet=["ORZ_SHIP"],
                               escorts=[0, 12], **kw))
    visit_fields = {"space": "ORZ_VISITS", "home": "ORZ_HOME_VISITS", "taalo": "TAALO_VISITS"}
    for where, make in places:
        for mname, manner, kw in manners:
            for visits in range(8):
                field = "ORZ_VISITS" if manner == 2 else visit_fields[where]
                starts.append(make(f"{where}-{mname}-visit-{visits}",
                                   {"MET_ORZ_BEFORE": 1, "ORZ_MANNER": manner, field: visits}, follow=[], **kw))
    met = {"MET_ORZ_BEFORE": 1, "ORZ_VISITS": 1}
    # The neutral talk, walked whole from each state of its stacks.
    for andro in range(4):
        for s0 in (0, 1):
            for s1 in (0, 1):
                for refused in (0, 1):
                    starts.append(space_start(f"neutral-andro-{andro}-stack0-{s0}-stack1-{s1}-refused-{refused}",
                                              {**met, "ORZ_ANDRO_STATE": andro, "ORZ_STACK0": s0,
                                               "ORZ_STACK1": s1, "REFUSED_ORZ_ALLIANCE": refused}))
    for cname, captain, ship in NAMES:
        start = space_start(f"neutral-{cname}", met, follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    # The allies' questions, walked whole to the break, with Orz ships in the escorts or none.
    fleets = (("orz-among-others", ["SPATHI_SHIP", "ORZ_SHIP", "SHOFIXTI_SHIP", "ORZ_SHIP"]),
              ("orz-alone", ["ORZ_SHIP"]), ("none", []), ("others", ["SPATHI_SHIP"]))
    for fname, fleet in fleets:
        starts.append(space_start(f"allied-fleet-{fname}", {**met, "ORZ_MANNER": 3}, escort_fleet=fleet,
                                  escorts=[0, 8], **allied))
    for andro in range(4):
        for general in range(4):
            for personal in range(8):
                starts.append(home_start(f"allied-andro-{andro}-general-{general}-personal-{personal}",
                                         {"MET_ORZ_BEFORE": 1, "ORZ_HOME_VISITS": 1, "ORZ_MANNER": 3,
                                          "ORZ_ANDRO_STATE": andro, "ORZ_GENERAL_INFO": general,
                                          "ORZ_PERSONAL_INFO": personal},
                                         escort_fleet=["ORZ_SHIP"], escorts=[0, 11], **allied))
    # The angry talk: its news and each insult (the apology makes the alliance again).
    for general in range(4):
        for personal in range(8):
            starts.append(space_start(f"angry-general-{general}-personal-{personal}",
                                      {**met, "ORZ_MANNER": 1, "ORZ_GENERAL_INFO": general,
                                       "ORZ_PERSONAL_INFO": personal}))
    # The Taalo moon's questions by standing.
    for mname, manner, kw in manners:
        if manner != 2:
            starts.append(taalo(f"taalo-walk-{mname}", {"MET_ORZ_BEFORE": 1, "TAALO_VISITS": 1, "ORZ_MANNER": manner},
                                **kw))
    return starts


def pkunk_starts():
    """The Pkunk (pkunkc.c), from states chosen to reach every condition it
    reads: the endgame's won battle; in space and at their homeworld
    (GLOBAL_FLAGS_AND_DATA bit 7), by each standing (PKUNK_MANNER: neutral,
    angry, hating, friends), each visit's greeting (PKUNK_VISITS,
    PKUNK_HOME_VISITS, the visits past those the switch names), the first
    of each walked whole; the Clear Spindle given or not (CLEAR_SPINDLE),
    the Kohr-Ah's victory sensed (the year, PKUNK_SENSE_VICTOR,
    KOHR_AH_FRENZY); their friendship offered at home (we_are_vindicator,
    with each captain's and ship's names) and its 4 ships by the escort
    fleet's room (none, some, all) and the date (the next ships' date's
    day at most the 28th; its year in 5 bits); the yearly gift by the next
    ships' date (none given yet; a year, a month or a day before it; on it;
    after it) and the room; their anger mended at home (the spindle given
    then or already); the fleet on the move (PKUNK_ON_THE_MOVE,
    PKUNK_SWITCH, PKUNK_FLEET, PKUNK_MISSION, PKUNK_RETURN, PKUNK_MIGRATE,
    PKUNK_WORRY, asked after the fleet's whereabouts); the two question
    menus (about the Pkunk, about the Ilwrath), each walked on its own
    with the homeworld it returns to, so the homeworld's other walks leave
    them out (walked together they make tens of thousands of paths); the
    Ilwrath (ILWRATH_DECEIVED); the friendly talk in
    space (PKUNK_INFO, PKUNK_WAR, PKUNK_DONE_WAR, PKUNK_FORTUNE, the ships
    at home); the neutral one (PKUNK_INFO); and their migration's talk
    (PKUNK_MISSION odd, PKUNK_MIGRATE_VISITS, each PKUNK_REASONS)."""
    friends = {"PKUNK_MANNER": 3}
    # The homeworld's talk walked whole would walk every mix of its two
    # question menus (about the Pkunk, about the Ilwrath) with everything
    # else: each menu is walked on its own below (with the homeworld it
    # returns to), and the other walks leave both out.
    home_walk = ["PkunkHome", "DiscussConquer", "OfferAlliance", "PkunkAngry", "ExitConversation"]
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE"),
              home_start("home-won-last-battle", {}, activity="WON_LAST_BATTLE")]
    for manner in range(4):
        for visits in range(8):
            first = visits == 0 and manner != 2
            more = {"escorts": [0, 12]} if first else {"follow": []}
            starts.append(space_start(f"space-manner-{manner}-visit-{visits}",
                                      {"PKUNK_MANNER": manner, "PKUNK_VISITS": visits}, **more))
            field = "PKUNK_VISITS" if manner in (1, 2) else "PKUNK_HOME_VISITS"
            # Friends' later visits before the next ships' date (else the gift instead of the greeting).
            later = {"PKUNK_SHIP_MONTH": 1, "PKUNK_SHIP_YEAR": 31} if manner == 3 and visits else {}
            for spindle in (0, 1):
                starts.append(home_start(f"home-manner-{manner}-visit-{visits}-spindle-{spindle}",
                                         {"PKUNK_MANNER": manner, field: visits, "CLEAR_SPINDLE": spindle, **later},
                                         **(dict(more, follow=home_walk) if first else more)))
    # The Kohr-Ah's victory sensed at home, the spindle given already.
    for year in (2155, 2156):
        for sensed in (0, 1):
            for frenzy in (0, 1):
                starts.append(home_start(f"sense-{year}-sensed-{sensed}-frenzy-{frenzy}",
                                         {"CLEAR_SPINDLE": 1, "PKUNK_SENSE_VICTOR": sensed, "KOHR_AH_FRENZY": frenzy},
                                         date=[year, 2, 17], follow=[]))
    # Their friendship offered at home and its ships, by the room and the date.
    for room in (0, 2, 4, 12):
        for date in ((2155, 2, 17), (2156, 12, 31), (2185, 6, 30), (2187, 1, 29)):
            starts.append(home_start(f"offer-room-{room}-{date[0]}-{date[1]}-{date[2]}",
                                     {"CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": 1}, escorts=[0, room],
                                     date=list(date), follow=["OfferAlliance", "ExitConversation"]))
    for cname, captain, ship in NAMES:
        start = home_start(f"offer-{cname}", {"CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": 1}, follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    # The yearly gift at home, by the next ships' date (2156-03-10) and the room.
    ship_date = {"PKUNK_SHIP_MONTH": 3, "PKUNK_SHIP_DAY": 10, "PKUNK_SHIP_YEAR": 1}
    dates = [("year-before", (2155, 6, 1)), ("month-before", (2156, 2, 28)), ("day-before", (2156, 3, 9)),
             ("on-the-day", (2156, 3, 10)), ("month-after", (2156, 4, 1)), ("year-after", (2157, 1, 1)),
             ("late-day", (2156, 3, 31))]
    for room in (0, 2, 4, 12):
        for visits in (0, 1, 7):
            starts.append(home_start(f"gift-never-room-{room}-visit-{visits}",
                                     {**friends, "CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": visits},
                                     escorts=[0, room], follow=[]))
            for when, date in dates:
                starts.append(home_start(f"gift-{when}-room-{room}-visit-{visits}",
                                         {**friends, **ship_date, "CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": visits},
                                         escorts=[0, room], date=list(date), follow=[]))
    # The two question menus at home, each walked whole with the homeworld it returns to.
    for menu in ("AboutPkunk", "AboutIlwrath"):
        for manner in (0, 3):
            starts.append(home_start(f"menu-{menu}-manner-{manner}",
                                     {"PKUNK_MANNER": manner, "CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": 1},
                                     follow=[menu, "PkunkHome"]))
    # Their anger mended at home, the spindle given then or already.
    for spindle in (0, 1):
        starts.append(home_start(f"angry-home-spindle-{spindle}", {"PKUNK_MANNER": 1, "PKUNK_VISITS": 1,
                                                                   "CLEAR_SPINDLE": spindle}, follow=home_walk))
    # The fleet on the move, asked at home.
    for mission in (0, 1, 2, 3):
        for switch in (0, 1):
            for fleet in range(4):
                starts.append(home_start(f"move-mission-{mission}-switch-{switch}-fleet-{fleet}",
                                         {**friends, "CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": 1,
                                          "PKUNK_ON_THE_MOVE": 1, "PKUNK_MISSION": mission,
                                          "PKUNK_SWITCH": switch, "PKUNK_FLEET": fleet},
                                         follow=["PkunkHome"]))
    for ret in (0, 1):
        for migrate in range(4):
            for mission in (0, 1):
                starts.append(home_start(f"move-return-{ret}-migrate-{migrate}-mission-{mission}",
                                         {**friends, "CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": 1,
                                          "PKUNK_ON_THE_MOVE": 1, "PKUNK_MISSION": mission,
                                          "PKUNK_RETURN": ret, "PKUNK_MIGRATE": migrate},
                                         prefix=[0], follow=[]))
    # The worries, offered once the fleet's whereabouts are asked (where_fleet_1 disabled) while migrating.
    for worry in range(4):
        starts.append(home_start(f"move-worry-{worry}", {**friends, "CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": 1,
                                                         "PKUNK_ON_THE_MOVE": 1, "PKUNK_MISSION": 1,
                                                         "PKUNK_WORRY": worry},
                                     follow=["PkunkHome"]))
    for deceived in (0, 1):
        for manner in (0, 3):
            starts.append(home_start(f"ilwrath-deceived-{deceived}-manner-{manner}",
                                     {"PKUNK_MANNER": manner, "CLEAR_SPINDLE": 1, "PKUNK_HOME_VISITS": 1,
                                      "ILWRATH_DECEIVED": deceived}, follow=["PkunkHome", "AboutIlwrath"]))
    # The friendly talk in space.
    for info in range(8):
        starts.append(space_start(f"friendly-info-{info}", {**friends, "PKUNK_VISITS": 1, "PKUNK_INFO": info,
                                                            "PKUNK_SHIP_MONTH": 1, "PKUNK_SHIP_YEAR": 31},
                                  prefix=[0], follow=[]))
    for war in range(4):
        for done in (0, 1):
            starts.append(space_start(f"friendly-war-{war}-done-{done}",
                                      {**friends, "PKUNK_VISITS": 1, "PKUNK_WAR": war, "PKUNK_DONE_WAR": done}))
    for fortune in range(8):
        starts.append(space_start(f"friendly-fortune-{fortune}", {**friends, "PKUNK_VISITS": 1,
                                                                  "PKUNK_FORTUNE": fortune, "PKUNK_DONE_WAR": 1},
                                  prefix=[1], follow=[]))
    for when, date in dates:
        starts.append(space_start(f"friendly-ships-{when}", {**friends, **ship_date, "PKUNK_VISITS": 1},
                                  date=list(date), prefix=[0], follow=[]))
    # The neutral talk in space.
    for info in range(8):
        starts.append(space_start(f"neutral-info-{info}", {"PKUNK_VISITS": 1, "PKUNK_INFO": info}))
    # Their migration's talk.
    for mission in (1, 3, 5, 7):
        for visits in range(8):
            starts.append(space_start(f"migrate-mission-{mission}-visit-{visits}",
                                      {"PKUNK_MISSION": mission, "PKUNK_MIGRATE_VISITS": visits},
                                      **({} if visits == 0 and mission == 1 else {"follow": []})))
    for reasons in range(16):
        starts.append(space_start(f"migrate-reasons-{reasons}", {"PKUNK_MISSION": 1, "PKUNK_MIGRATE_VISITS": 1,
                                                                 "PKUNK_REASONS": reasons}))
    return starts


def druuge_starts():
    """The Druuge (druugec.c), from states chosen to reach every condition
    it reads: the endgame's won battle; hostile for good (DRUUGE_MANNER) in
    space, at their trade world (GLOBAL_FLAGS_AND_DATA bit 7) and at the
    bomb's moon (bit 6), by each visit; attacked (ATTACKED_DRUUGE) in
    space; the neutral talk in space by its visits and news; the salvage
    fight by the escort fleet (empty or not), the flagship's weapons (none,
    each kind, the first and the last slot) and DRUUGE_SALVAGE; the bomb's
    moon by BOMB_VISITS, walked whole; the trade world's greetings by its
    visits, the disclaimer and each of the goods scanned aboard; the trade
    world walked whole with too few crew to buy anything, by what the
    players know of the slave trade (KNOW_DRUUGE_SLAVERS) and the news
    (DRUUGE_HOME_INFO, KNOW_ABOUT_SHATTERED); the buying walked whole by
    the crew aboard around each price (10 for fuel, 100 for a ship or an
    artifact), the escort fleet's room and the tanks' room; the goodbye
    after each amount of crew sold, by the crew sold before
    (CREW_SOLD_TO_DRUUGE0 and 1 around 100 and 250, and wrapping at 16
    bits) and the crew's price (wrapping at a byte); the selling of each of
    the goods by the escort fleet's room and the fuel the tanks lack
    (around each of the deal's lines, and around DeltaSISGauges' 0x3FFF
    step), for the Rosy Sphere while the players haven't it, and of
    several goods walked whole."""
    bomb = lambda name, state, **kw: home_start(name, state, flags=1 << 6, **kw)
    places = (("space", space_start), ("home", home_start), ("bomb", bomb))
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE"),
              home_start("home-won-last-battle", {}, activity="WON_LAST_BATTLE"),
              space_start("hostile-won-last-battle", {"DRUUGE_MANNER": 1}, "WON_LAST_BATTLE")]
    # Hostile for good, and attacked, by each visit.
    for where, make in places:
        for visits in range(8):
            field = "DRUUGE_HOME_VISITS" if where == "home" else "DRUUGE_VISITS"
            starts.append(make(f"hostile-{where}-visit-{visits}", {"DRUUGE_MANNER": 1, field: visits}))
    for visits in range(8):
        starts.append(space_start(f"attacked-space-visit-{visits}", {"ATTACKED_DRUUGE": 1, "DRUUGE_VISITS": visits}))
    # The neutral talk in space, walked whole, by its visits and its news.
    for visits in range(8):
        starts.append(space_start(f"space-visit-{visits}", {"DRUUGE_VISITS": visits}))
    for info in range(1, 4):
        starts.append(space_start(f"space-info-{info}", {"DRUUGE_VISITS": 1, "DRUUGE_SPACE_INFO": info}))

    # A flagship with crew pods for 350 (DeltaSISGauges keeps the crew within what the pods hold).
    pods = ["CREW_POD"] * 7 + ["FUEL_TANK"] + ["EMPTY"] * 7 + ["GUN_WEAPON"]

    def with_ship(start, **kw):
        if kw.get("crew", 0) > 50 and "modules" not in kw:
            kw["modules"] = pods
        start["ship"] = talk_ship(**kw)
        return start

    # The salvage fight: no escorts and no weapon aboard.
    empty = ["EMPTY"] * 16
    unarmed = ["EMPTY", "FUEL_TANK", "CREW_POD"] + ["EMPTY"] * 5 + ["STORAGE_BAY"] + ["EMPTY"] * 7
    kits = (("unarmed", unarmed), ("empty", empty),
            ("others", ["DYNAMO_UNIT", "SHIVA_FURNACE", "TRACKING_SYSTEM", "ANTIMISSILE_DEFENSE", "BOMB_MODULE_0",
                        "HIGHEFF_FUELSYS"] + ["EMPTY"] * 10),
            ("gun-first", ["GUN_WEAPON"] + ["EMPTY"] * 15), ("gun-last", ["EMPTY"] * 15 + ["GUN_WEAPON"]),
            ("blaster", ["EMPTY"] * 7 + ["BLASTER_WEAPON"] + ["EMPTY"] * 8),
            ("cannon", ["EMPTY"] * 3 + ["CANNON_WEAPON"] + ["EMPTY"] * 12))
    for kname, modules in kits:
        for salvage in (0, 1):
            starts.append(with_ship(space_start(f"salvage-{kname}-{salvage}", {"DRUUGE_SALVAGE": salvage}),
                                    modules=modules))
    for salvage in (0, 1):
        starts.append(with_ship(space_start(f"salvage-unarmed-escorted-{salvage}", {"DRUUGE_SALVAGE": salvage},
                                            escort_fleet=["SPATHI_SHIP"], escorts=[0, 11]), modules=unarmed))
    # No salvage where they're attacked, hostile, at their trade world or at the bomb's moon.
    starts.append(with_ship(space_start("unarmed-attacked", {"ATTACKED_DRUUGE": 1}), modules=unarmed))
    starts.append(with_ship(space_start("unarmed-hostile", {"DRUUGE_MANNER": 1}), modules=unarmed))
    starts.append(with_ship(home_start("unarmed-home", {}, follow=[]), modules=unarmed))
    starts.append(with_ship(bomb("unarmed-bomb", {}, follow=[]), modules=unarmed))
    # The bomb's moon, walked whole.
    for visits in range(8):
        starts.append(bomb(f"bomb-visit-{visits}", {"BOMB_VISITS": visits, "DRUUGE_VISITS": 3}))
    starts.append(bomb("bomb-attacked", {"BOMB_VISITS": 3, "ATTACKED_DRUUGE": 1, "DRUUGE_VISITS": 3}))
    # The trade world's greetings: by its visits, the disclaimer, the goods scanned.
    for visits in range(8):
        starts.append(home_start(f"home-visit-{visits}", {"DRUUGE_HOME_VISITS": visits}, follow=[]))
    for attacked in (0, 1):
        for disclaimer in (0, 1):
            starts.append(home_start(f"home-attacked-{attacked}-disclaimer-{disclaimer}",
                                     {"ATTACKED_DRUUGE": attacked, "DRUUGE_DISCLAIMER": disclaimer}, follow=[]))
    goods = (("maidens", {"MAIDENS_ON_SHIP": 1}, "SCANNED_MAIDENS"),
             ("egg-case-0", {"EGG_CASE0_ON_SHIP": 1}, "SCANNED_FRAGMENTS"),
             ("egg-case-1", {"EGG_CASE1_ON_SHIP": 1}, "SCANNED_FRAGMENTS"),
             ("egg-case-2", {"EGG_CASE2_ON_SHIP": 1}, "SCANNED_FRAGMENTS"),
             ("caster", {"BURV_BROADCASTERS_ON_SHIP": 1}, "SCANNED_CASTER"),
             ("spawner", {"PORTAL_SPAWNER_ON_SHIP": 1}, "SCANNED_SPAWNER"))
    for gname, aboard, scanned in goods:
        for seen in (0, 1):
            starts.append(home_start(f"home-scan-{gname}-scanned-{seen}", {**aboard, scanned: seen}, follow=[]))
    for bought in range(4):
        starts.append(home_start(f"home-scan-fragments-bought-{bought}",
                                 {"EGG_CASE1_ON_SHIP": 1, "FRAGMENTS_BOUGHT": bought}, follow=[]))
    everything = {}
    for _, aboard, _ in goods:
        everything.update(aboard)
    starts.append(home_start("home-scan-everything", everything, follow=[]))
    # The trade world walked whole with too few crew to buy anything.
    poor = dict(crew=5)
    for know in range(4):
        starts.append(with_ship(home_start(f"home-walk-know-{know}", {"KNOW_DRUUGE_SLAVERS": know}), **poor))
    for info in range(1, 4):
        starts.append(with_ship(home_start(f"home-info-{info}", {"DRUUGE_HOME_VISITS": 1, "DRUUGE_HOME_INFO": info,
                                                               "KNOW_DRUUGE_SLAVERS": 3},
                                           follow=["TradeWorld", "ExitConversation"]), **poor))
    for shattered in range(4):
        starts.append(with_ship(home_start(f"home-info-2-shattered-{shattered}",
                                           {"DRUUGE_HOME_VISITS": 3, "DRUUGE_HOME_INFO": 2, "KNOW_DRUUGE_SLAVERS": 3,
                                            "KNOW_ABOUT_SHATTERED": shattered},
                                           follow=["TradeWorld", "ExitConversation"]), **poor))
    # The buying, walked whole from the trade world's menu's "want to buy" (its third reply).
    know = {"DRUUGE_HOME_VISITS": 1, "KNOW_DRUUGE_SLAVERS": 3}
    owned = {"ROSY_SPHERE": 1, "ROSY_SPHERE_ON_SHIP": 1, "ARTIFACT_2_ON_SHIP": 1, "ARTIFACT_3_ON_SHIP": 1}
    for crew in (9, 10, 11, 99, 100, 110):
        starts.append(with_ship(home_start(f"buy-crew-{crew}", know, prefix=[2], follow=["Buy"],
                                           escorts=[0, 1]), crew=crew))
    starts.append(with_ship(home_start("buy-crew-100-no-room", know, prefix=[2], follow=["Buy"]), crew=100))
    starts.append(with_ship(home_start("buy-ships-room-1", {**know, **owned}, prefix=[2], follow=["Buy"],
                                       escorts=[0, 1], escort_fleet=["SPATHI_SHIP"] * 11), crew=205, fuel=6000))
    starts.append(with_ship(home_start("buy-ships-room-12", {**know, **owned}, prefix=[2], follow=["Buy"],
                                       escorts=[0, 12]), crew=300, fuel=6000))
    for fuel in (0, 4500, 5500, 6000):
        starts.append(with_ship(home_start(f"buy-fuel-{fuel}", {**know, **owned}, prefix=[2], follow=["Buy"]),
                                crew=25, fuel=fuel))
    for have in range(8):
        state = {**know, "ROSY_SPHERE": have & 1, "ARTIFACT_2_ON_SHIP": have >> 1 & 1,
                 "ARTIFACT_3_ON_SHIP": have >> 2 & 1}
        starts.append(with_ship(home_start(f"buy-artifacts-{have}", state, prefix=[2], follow=["Buy"]), crew=100))
    # The goodbye after crew sold: the crew sold before, the crew sold in this talk (ships and fuel bought
    # with every artifact owned: the menu is a ship, fuel, done), and the crew's price.
    sold = ((0, 10), (90, 10), (91, 10), (0, 110), (100, 10), (100, 150), (0, 250), (0, 260), (100, 160),
            (101, 10), (101, 150), (240, 10), (241, 10), (250, 10), (251, 10), (300, 100), (255, 10), (256, 10),
            (65500, 100), (65530, 10))
    for before, now in sold:
        for visits in (0, 1):
            if visits and (before, now) not in ((0, 110), (0, 260), (101, 150)):
                continue
            starts.append(with_ship(home_start(
                f"bye-sold-{before}-and-{now}-visit-{visits}",
                {"KNOW_DRUUGE_SLAVERS": 3, **owned, "DRUUGE_HOME_VISITS": visits,
                 "CREW_SOLD_TO_DRUUGE0": before & 0xFF, "CREW_SOLD_TO_DRUUGE1": before >> 8},
                prefix=[2] + [0] * (now // 100) + [1] * (now % 100 // 10) + [2, 3], follow=[], escorts=[0, 12]),
                crew=now + 5))
    for cost, before, now in ((250, 0, 260), (249, 0, 110), (254, 101, 150), (0, 0, 110)):
        starts.append({**with_ship(home_start(
            f"bye-crew-cost-{cost}-sold-{before}-and-{now}",
            {"KNOW_DRUUGE_SLAVERS": 3, **owned, "CREW_SOLD_TO_DRUUGE0": before & 0xFF,
             "CREW_SOLD_TO_DRUUGE1": before >> 8},
            prefix=[2] + [0] * (now // 100) + [1] * (now % 100 // 10) + [2, 3], follow=[], escorts=[0, 12]),
            crew=now + 5), "crew_cost": cost})
    # The selling, walked whole from the trade world's menu's "want to sell" (its second reply): each of the
    # goods alone at the regular deal (the players have the Rosy Sphere), by the escort fleet's room.
    sphere = {"DRUUGE_HOME_VISITS": 1, "ROSY_SPHERE": 1, "ROSY_SPHERE_ON_SHIP": 1}
    sell = dict(prefix=[1], follow=["Sell", "Trade"])
    for room in (0, 3, 6, 12):
        starts.append(home_start(f"sell-maidens-room-{room}", {**sphere, "MAIDENS_ON_SHIP": 1},
                                 escorts=[0, room], **sell))
    cases = (("0", {"EGG_CASE0_ON_SHIP": 1}), ("1", {"EGG_CASE1_ON_SHIP": 1}), ("2", {"EGG_CASE2_ON_SHIP": 1}),
             ("all", {"EGG_CASE0_ON_SHIP": 1, "EGG_CASE1_ON_SHIP": 1, "EGG_CASE2_ON_SHIP": 1}),
             ("1-and-2", {"EGG_CASE1_ON_SHIP": 1, "EGG_CASE2_ON_SHIP": 1}))
    for cname, aboard in cases:
        for bought in range(4):
            for room in (0, 12):
                if room == 0 and (cname, bought) != ("all", 0):
                    continue
                starts.append(home_start(f"sell-egg-case-{cname}-bought-{bought}-room-{room}",
                                         {**sphere, **aboard, "FRAGMENTS_BOUGHT": bought},
                                         escorts=[0, room], **sell))
    # Fuel to the brim: the tanks (two high-efficiency ones and a plain one: 26,000 with the reserve; four
    # high-efficiency ones: 41,000; none: the reserve) and what they lack, around each of the deal's lines
    # and around DeltaSISGauges' 0x3FFF step.
    tanks = ["HIGHEFF_FUELSYS", "CREW_POD", "HIGHEFF_FUELSYS", "FUEL_TANK"] + ["EMPTY"] * 11 + ["GUN_WEAPON"]
    for lack in (0, 49, 50, 949, 950, 951, 4949, 4950, 9949, 9950, 16383, 16384, 16385, 24949, 24950, 26000):
        starts.append(with_ship(home_start(f"sell-caster-lacking-{lack}", {**sphere, "BURV_BROADCASTERS_ON_SHIP": 1},
                                           **sell), fuel=26000 - lack, modules=tanks))
    starts.append(with_ship(home_start("sell-caster-lacking-41000", {**sphere, "BURV_BROADCASTERS_ON_SHIP": 1},
                                       **sell), fuel=0, modules=["HIGHEFF_FUELSYS"] * 4 + ["EMPTY"] * 12))
    starts.append(with_ship(home_start("sell-caster-lacking-32767", {**sphere, "BURV_BROADCASTERS_ON_SHIP": 1},
                                       **sell), fuel=41000 - 32767, modules=["HIGHEFF_FUELSYS"] * 4 + ["EMPTY"] * 12))
    starts.append(with_ship(home_start("sell-caster-no-tanks", {**sphere, "BURV_BROADCASTERS_ON_SHIP": 1},
                                       **sell), fuel=0, modules=empty))
    for room in (0, 2, 3, 12):
        for fuel in (1000, 6000):
            starts.append(with_ship(home_start(f"sell-spawner-room-{room}-fuel-{fuel}",
                                               {**sphere, "PORTAL_SPAWNER_ON_SHIP": 1}, escorts=[0, room], **sell),
                                    fuel=fuel))
    # Each of the goods offered for the Rosy Sphere (the players haven't it), walked whole.
    no_sphere = {"DRUUGE_HOME_VISITS": 1}
    for gname, aboard, _ in goods:
        starts.append(home_start(f"trade-{gname}-for-sphere", {**no_sphere, **aboard}, escorts=[0, 12], **sell))
    starts.append(home_start("trade-maidens-and-caster-for-sphere",
                             {**no_sphere, "MAIDENS_ON_SHIP": 1, "BURV_BROADCASTERS_ON_SHIP": 1},
                             escorts=[0, 12], **sell))
    # The sphere aboard though never had (no play reaches it): the regular deal refused gives it too.
    starts.append(home_start("trade-maidens-sphere-aboard", {**no_sphere, "MAIDENS_ON_SHIP": 1,
                                                             "ROSY_SPHERE_ON_SHIP": 1}, escorts=[0, 12], **sell))
    # Several goods, walked whole: every one with room for all the ships, and three with room for 7 of 10.
    starts.append(home_start("sell-everything", {**sphere, **everything, "EGG_CASE2_ON_SHIP": 0},
                             escorts=[0, 12], **sell))
    starts.append(home_start("sell-for-ships-room-7", {**sphere, "MAIDENS_ON_SHIP": 1, "EGG_CASE0_ON_SHIP": 1,
                                                       "PORTAL_SPAWNER_ON_SHIP": 1}, escorts=[0, 7], **sell))
    # The whole trade world with a little to buy and to sell, walked whole.
    starts.append(with_ship(home_start("home-walk-trading", {**know, **owned, "BURV_BROADCASTERS_ON_SHIP": 1},
                                       escorts=[0, 12]), crew=25))
    return starts


def mycon_first_draw(seed):
    """Whether a Mycon talk's first draw (NormalMycon's (BYTE) TFB_Random ()
    < 256 * 30 / 100) makes its replies fight."""
    return (tfb_random(seed) & 0xFF) < 256 * 30 // 100


def mycon_seeds(fight, count, start=1):
    """The first seeds from start whose first draw makes the replies fight, or not."""
    seeds, seed = [], start
    while len(seeds) < count:
        if mycon_first_draw(seed) == fight:
            seeds.append(seed)
        seed += 1
    return seeds


def mycon_starts():
    """The Mycon (myconc.c), from states chosen to reach every condition it
    reads: the endgame's won battle; the Sun Device taken (SUN_DEVICE, by
    MYCON_VISITS) and the ambush known (MYCON_KNOW_AMBUSH), in space, at
    their homeworld (GLOBAL_FLAGS_AND_DATA bit 7) and at the Sun Device's
    world (bit 6); the Sun Device's world by its visits, and walked whole
    by the ambush's states (MYCON_AMBUSH, MYCON_FELL_FOR_AMBUSH,
    NO_TRICK_AT_SUN); each greeting in space and at home; the talk in space
    and at home walked whole from seeds whose first draw (TFB_Random) makes
    the replies fight and from seeds whose doesn't (the later draws fall
    both ways); each ramble (MYCON_RAMBLE) as a first reply's answer, each
    insult (MYCON_INSULTS) and each question (MYCON_INFO) offered and
    made, answered with a fight and without; the shattered worlds asked
    about (KNOW_ABOUT_SHATTERED); and
    the trick (MYCON_AMBUSH) walked whole through its clues in every
    order."""
    sun = lambda name, state, **kw: home_start(name, state, flags=1 << 6, **kw)
    places = (("space", space_start), ("home", home_start), ("sun", sun))
    fight, talk = mycon_seeds(True, 40), mycon_seeds(False, 40)
    starts = [space_start("won-last-battle", {}, "WON_LAST_BATTLE", seed=talk[0]),
              home_start("home-won-last-battle", {}, activity="WON_LAST_BATTLE", seed=fight[0], follow=[]),
              sun("sun-won-last-battle", {}, activity="WON_LAST_BATTLE")]
    # The Sun Device taken, the ambush known.
    for visits in range(8):
        starts.append(space_start(f"thief-visit-{visits}", {"SUN_DEVICE": 1, "MYCON_VISITS": visits}))
    for where, make in places:
        starts.append(make(f"thief-{where}", {"SUN_DEVICE": 1, "MYCON_KNOW_AMBUSH": 1, "MYCON_HOME_VISITS": 2,
                                              "MYCON_SUN_VISITS": 1}))
        starts.append(make(f"liar-{where}", {"MYCON_KNOW_AMBUSH": 1, "MYCON_FELL_FOR_AMBUSH": 1, "MYCON_AMBUSH": 1}))
    # The Sun Device's world.
    for visits in range(8):
        starts.append(sun(f"sun-visit-{visits}", {"MYCON_SUN_VISITS": visits}, follow=[]))
    for ambush in (0, 1):
        for fell in (0, 1):
            for refused in (0, 1):
                starts.append(sun(f"sun-ambush-{ambush}-fell-{fell}-refused-{refused}",
                                  {"MYCON_SUN_VISITS": 1, "MYCON_AMBUSH": ambush, "MYCON_FELL_FOR_AMBUSH": fell,
                                   "NO_TRICK_AT_SUN": refused}))
    # Each greeting in space and at home.
    for visits in range(8):
        starts.append(space_start(f"space-visit-{visits}", {"MYCON_VISITS": visits}, follow=[], seed=talk[visits]))
        starts.append(home_start(f"home-visit-{visits}", {"MYCON_HOME_VISITS": visits}, follow=[],
                                 seed=fight[visits]))
    # The talk in space, walked whole: by the insult that's next, the first draw a fight or not.
    for insults in range(8):
        for dname, seeds in (("fight", fight), ("talk", talk)):
            starts.append(space_start(f"space-insult-{insults}-first-{dname}",
                                      {"MYCON_VISITS": 1, "MYCON_INSULTS": insults, "MYCON_RAMBLE": 4 * insults},
                                      seed=seeds[8 + insults]))
    # Each ramble as the first reply's answer ("we come in peace", the first reply), a fight or not.
    for ramble in range(32):
        for dname, seeds in (("fight", fight), ("talk", talk)):
            starts.append(space_start(f"space-ramble-{ramble}-{dname}",
                                      {"MYCON_VISITS": 2, "MYCON_RAMBLE": ramble, "KNOW_ABOUT_SHATTERED": ramble % 4},
                                      prefix=[0], follow=[], seed=seeds[ramble]))
    # Each question at home (the menu's first reply): where the first draw is a fight, every first reply;
    # where it isn't, the replies offered and the question asked.
    for info in range(16):
        state = {"MYCON_HOME_VISITS": 3, "MYCON_INFO": info, "MYCON_RAMBLE": 31 - info}
        starts.append(home_start(f"home-question-{info}-fight", state, follow=["CombatIsInevitable"],
                                 seed=fight[16 + info]))
        starts.append(home_start(f"home-question-{info}-talk", state, follow=[], seed=talk[16 + info]))
        starts.append(home_start(f"home-question-{info}-talk-asked", state, prefix=[0], follow=[],
                                 seed=talk[16 + info]))
    # The talk at home, walked whole, the shattered worlds asked about or not.
    for shattered in range(4):
        for dname, seeds in (("fight", fight), ("talk", talk)):
            starts.append(home_start(f"home-walk-shattered-{shattered}-first-{dname}",
                                     {"MYCON_HOME_VISITS": 1, "KNOW_ABOUT_SHATTERED": shattered,
                                      "MYCON_INFO": 5 + shattered, "MYCON_RAMBLE": 22 + shattered},
                                     seed=seeds[32 + shattered]))
    # Once they fell for the ambush: neither the shattered worlds nor the trick.
    starts.append(home_start("home-fell-for-ambush", {"MYCON_HOME_VISITS": 1, "KNOW_ABOUT_SHATTERED": 1,
                                                      "MYCON_AMBUSH": 1, "MYCON_FELL_FOR_AMBUSH": 1},
                             follow=[], seed=talk[36]))
    # The trick, walked whole: offered whatever the draw, its clues in every order.
    for dname, seeds in (("fight", fight), ("talk", talk)):
        starts.append(home_start(f"home-trick-first-{dname}", {"MYCON_HOME_VISITS": 1, "MYCON_AMBUSH": 1,
                                                               "KNOW_ABOUT_SHATTERED": 1},
                                 follow=["TrickMycon"], seed=seeds[37]))
    return starts


def utwig_starts():
    """The Utwig (utwigc.c), from states chosen to reach every condition it
    reads: the endgame's won battle (and the fight before it, IN_LAST_BATTLE:
    the ordinary talk); hostile (UTWIG_HOSTILE) in space, at their
    homeworld (GLOBAL_FLAGS_AND_DATA bit 7) and at the bomb's moon (bit 6),
    by each visit (UTWIG_VISITS, UTWIG_HOME_VISITS, BOMB_VISITS) and the
    Ultron aboard or not, and "wait, we have the Ultron" by each of its
    states (ULTRON_CONDITION; UTWIG_INFO for the trick's two lines,
    KOHR_AH_FRENZY for the mission); the neutral talk in space and at home
    by each visit, walked whole, and from every state of its stacks
    (UTWIG_STACK1 and 2, UTWIG_WAR_NEWS), with each captain's and
    alliance's name spliced into "we are"; the Supox's sphere shown
    (StartSphereTracking: unknown, known already, none, extinct); the
    Ultron shown by each of its states, and given with every counter it
    clears set, the Supox dead or both allied already; the Ultron back
    without the alliance (the other song, the talk at peace from the
    start); the bomb's moon by each visit, walked whole with and without
    the Ultron, from every state of its stacks (BOMB_STACK1 and 2,
    BOMB_INFO), and the Ultron shown there by each of its states
    (REFUSED_ULTRON_AT_BOMB); the allies at home by each visit, their news
    walked whole by the mission's state (UTWIG_SUPOX_MISSION) and the
    news told (UTWIG_WAR_NEWS), the 4 ships by the escort fleet's room
    (none, 2, 4, 12); in space by the mission's state and each visit,
    before and after the Kohr-Ah walked whole (UTWIG_INFO); the
    alliance without the Ultron back, and at the bomb's moon; and the two
    races' standings apart (the Utwig allied with the Supox not or dead,
    the Supox allied with the Utwig not or dead: CheckAlliance
    (UTWIG_SHIP) alone is read)."""
    allied = {"alliances": {"UTWIG_SHIP": "good", "SUPOX_SHIP": "good"}}
    back = {"UTWIG_HAVE_ULTRON": 1, "ULTRON_CONDITION": 5}
    bomb = lambda name, state, **kw: home_start(name, state, flags=1 << 6, **kw)
    places = (("space", space_start), ("home", home_start), ("bomb", bomb))
    visit_fields = {"space": "UTWIG_VISITS", "home": "UTWIG_HOME_VISITS", "bomb": "BOMB_VISITS"}
    starts = []
    for where, make in places:
        starts.append(make(f"{where}-won-last-battle", {}, activity="WON_LAST_BATTLE"))
        starts.append(make(f"{where}-in-last-battle", {}, activity="IN_LAST_BATTLE", follow=[]))
    starts.append(space_start("hostile-won-last-battle", {"UTWIG_HOSTILE": 1}, "WON_LAST_BATTLE"))
    starts.append(home_start("ultron-won-last-battle", back, activity="WON_LAST_BATTLE", **allied))
    # Hostile, by each visit, the Ultron aboard or not.
    for where, make in places:
        for visits in range(8):
            for ultron in (0, 1):
                starts.append(make(f"hostile-{where}-visit-{visits}-ultron-{ultron}",
                                   {"UTWIG_HOSTILE": 1, visit_fields[where]: visits, "ULTRON_CONDITION": ultron}))
    starts.append(space_start("hostile-allied", {"UTWIG_HOSTILE": 1, "ULTRON_CONDITION": 4}, **allied))
    starts.append(space_start("hostile-ultron-back", {"UTWIG_HOSTILE": 1, **back}))
    # The Ultron shown, from a hostile greeting and in the neutral talk (its other replies left out).
    for manner, state in (("hostile", {"UTWIG_HOSTILE": 1}), ("neutral", {"UTWIG_STACK1": 4, "UTWIG_STACK2": 2,
                                                                         "UTWIG_WAR_NEWS": 3})):
        for where, make in places[:2]:
            for ultron in range(1, 8):
                for info in (0, 1):
                    for frenzy in (0, 1):
                        starts.append(make(f"{manner}-{where}-ultron-{ultron}-info-{info}-frenzy-{frenzy}",
                                           {**state, visit_fields[where]: 1, "ULTRON_CONDITION": ultron,
                                            "UTWIG_INFO": info, "KOHR_AH_FRENZY": frenzy},
                                           follow=["ExitConversation"]))
    # The Ultron given: every counter it clears set; the Supox dead; both allied already; a race's sphere known.
    counters = {"UTWIG_VISITS": 5, "SUPOX_VISITS": 6, "UTWIG_HOME_VISITS": 7, "SUPOX_HOME_VISITS": 3, "BOMB_VISITS": 2,
                "SUPOX_INFO": 1, "UTWIG_INFO": 1, "SUPOX_WAR_NEWS": 3, "UTWIG_WAR_NEWS": 3, "SUPOX_HOSTILE": 1,
                "UTWIG_STACK1": 4, "UTWIG_STACK2": 2, "ULTRON_CONDITION": 4}
    starts.append(home_start("give-counters", counters, follow=["ExitConversation"]))
    starts.append(home_start("give-hostile-counters", {**counters, "UTWIG_HOSTILE": 1}))
    starts.append(home_start("give-supox-dead", counters, follow=["ExitConversation"],
                             alliances={"SUPOX_SHIP": "dead"}))
    starts.append(home_start("give-allied-already", counters, follow=["ExitConversation"], **allied))
    # The neutral talk, by each visit; walked whole in space, and at home with the broken Ultron aboard.
    for where, make in places[:2]:
        for visits in range(8):
            starts.append(make(f"neutral-{where}-visit-{visits}", {visit_fields[where]: visits},
                               **({} if visits == 0 and where == "space" else {"follow": []})))
    starts.append(home_start("neutral-home-walk-ultron", {"UTWIG_HOME_VISITS": 1, "ULTRON_CONDITION": 1}))
    for s1 in range(8):
        for news in range(4):
            for s2 in range(4):
                starts.append(space_start(f"neutral-stack1-{s1}-news-{news}-stack2-{s2}",
                                          {"UTWIG_VISITS": 1, "UTWIG_STACK1": s1, "UTWIG_WAR_NEWS": news,
                                           "UTWIG_STACK2": s2}, follow=[]))
    for alliance in range(4):
        starts.append(space_start(f"neutral-alliance-name-{alliance}", {"UTWIG_VISITS": 1,
                                                                        "NEW_ALLIANCE_NAME": alliance}, follow=[]))
        for cname, captain, ship in NAMES:
            start = space_start(f"neutral-{cname}-alliance-name-{alliance}",
                                {"UTWIG_VISITS": 1, "NEW_ALLIANCE_NAME": alliance}, follow=[])
            start["ship"] = talk_ship(captain=captain, ship=ship)
            starts.append(start)
    # The Supox's sphere shown (what_about_you_2): unknown, known already, none, extinct.
    about_us = {"UTWIG_VISITS": 1, "UTWIG_STACK1": 4, "UTWIG_STACK2": 2, "UTWIG_WAR_NEWS": 1}
    starts.append(space_start("sphere-unknown", about_us))
    starts.append(space_start("sphere-known", about_us, fleets={"SUPOX_SHIP": [333, 333]}))
    starts.append(space_start("sphere-none", about_us, fleets={"SUPOX_SHIP": [0, 0]}))
    starts.append(space_start("sphere-extinct", about_us, fleets={"SUPOX_SHIP": [0, 0]},
                              alliances={"SUPOX_SHIP": "dead"}))
    # The Ultron back without the alliance: the talk at peace from the start, the other song.
    starts.append(space_start("ultron-back-unallied", {**back, "UTWIG_VISITS": 1, "UTWIG_STACK1": 2, "UTWIG_STACK2": 2,
                                                       "UTWIG_WAR_NEWS": 3}))
    starts.append(bomb("ultron-back-unallied-bomb", {**back, "BOMB_VISITS": 1}, follow=[]))
    # The bomb's moon: by each visit, walked whole with and without the Ultron, by each state of its stacks.
    for visits in range(8):
        starts.append(bomb(f"bomb-visit-{visits}", {"BOMB_VISITS": visits}, **({} if visits == 0 else {"follow": []})))
    starts.append(bomb("bomb-walk-ultron", {"BOMB_VISITS": 1, "ULTRON_CONDITION": 2}))
    starts.append(bomb("bomb-walk-told", {"BOMB_VISITS": 1, "BOMB_INFO": 1}))
    for s1 in range(4):
        for s2 in range(4):
            for info in (0, 1):
                starts.append(bomb(f"bomb-stack1-{s1}-stack2-{s2}-info-{info}",
                                   {"BOMB_VISITS": 1, "BOMB_STACK1": s1, "BOMB_STACK2": s2, "BOMB_INFO": info},
                                   follow=[]))
    for ultron in range(8):
        for refused in (0, 1):
            starts.append(bomb(f"bomb-ultron-{ultron}-refused-{refused}",
                               {"BOMB_VISITS": 1, "BOMB_STACK1": 3, "BOMB_STACK2": 2, "ULTRON_CONDITION": ultron,
                                "REFUSED_ULTRON_AT_BOMB": refused}, follow=["ExitConversation"]))
    # The allies at home: by each visit; their news walked whole by the mission and the news told; the ships.
    for visits in range(8):
        starts.append(home_start(f"allied-home-visit-{visits}", {**back, "UTWIG_HOME_VISITS": visits}, follow=[],
                                 escorts=[0, 12], **allied))
    for mission in range(8):
        for news in range(4):
            starts.append(home_start(f"allied-home-mission-{mission}-news-{news}",
                                     {**back, "UTWIG_HOME_VISITS": 1, "UTWIG_SUPOX_MISSION": mission,
                                      "UTWIG_WAR_NEWS": news}, escorts=[0, 12],
                                     **({} if news == 0 else {"follow": ["AlliedHome"]}), **allied))
    for room in (0, 2, 4, 12):
        starts.append(home_start(f"allied-home-ships-room-{room}", {**back, "UTWIG_HOME_VISITS": 1},
                                 escorts=[0, room], follow=["ExitConversation"], **allied))
    # The allies in space: by the mission's state and each visit; before and after the Kohr-Ah walked whole.
    for mission in range(8):
        for visits in range(8):
            starts.append(space_start(f"allied-space-mission-{mission}-visit-{visits}",
                                      {**back, "UTWIG_SUPOX_MISSION": mission, "UTWIG_VISITS": visits},
                                      follow=[], **allied))
    for mission in (1, 5):
        for info in (0, 1):
            starts.append(space_start(f"allied-space-mission-{mission}-info-{info}",
                                      {**back, "UTWIG_SUPOX_MISSION": mission, "UTWIG_VISITS": 1, "UTWIG_INFO": info},
                                      **allied))
    # The alliance without the Ultron back (the first song, the talk hostile until hailed), and at the bomb's moon.
    starts.append(home_start("allied-no-ultron", {"UTWIG_HOME_VISITS": 1}, follow=[], escorts=[0, 12], **allied))
    for mission in (0, 1, 2, 5):
        starts.append(bomb(f"allied-bomb-mission-{mission}", {**back, "UTWIG_SUPOX_MISSION": mission}, follow=[],
                           **allied))
    # The two races' standings apart (CheckAlliance (UTWIG_SHIP) alone is read): the Utwig allied with the
    # Supox not, or dead (the Ultron given with the Supox extinct leaves it so); the Supox allied with the
    # Utwig not, or dead: the neutral talk, and the bomb's moon's.
    for sname in ("bad", "dead"):
        alone = {"alliances": {"UTWIG_SHIP": "good", "SUPOX_SHIP": sname}}
        starts.append(home_start(f"utwig-allied-supox-{sname}-home", {**back, "UTWIG_HOME_VISITS": 1}, escorts=[0, 12],
                                 follow=["ExitConversation"], **alone))
        starts.append(space_start(f"utwig-allied-supox-{sname}-space", {**back, "UTWIG_SUPOX_MISSION": 1,
                                                                        "UTWIG_VISITS": 1}, follow=[], **alone))
        other = {"alliances": {"UTWIG_SHIP": sname, "SUPOX_SHIP": "good"}}
        for where, make in places:
            starts.append(make(f"utwig-{sname}-supox-allied-{where}", {visit_fields[where]: 1}, follow=[], **other))
        starts.append(home_start(f"utwig-{sname}-supox-allied-ultron-back", {**back, "UTWIG_HOME_VISITS": 1},
                                 follow=[], **other))
    return starts


def supox_starts():
    """The Supox (supoxc.c), from states chosen to reach every condition it
    reads: the endgame's won battle (and the fight before it,
    IN_LAST_BATTLE: the ordinary talk); hostile (SUPOX_HOSTILE) in space
    and at their homeworld (GLOBAL_FLAGS_AND_DATA bit 7) by each visit
    (SUPOX_VISITS for both); the neutral talk in space and at home by each
    visit (SUPOX_VISITS, SUPOX_HOME_VISITS), walked whole down to the
    Ultron handed over and its two farewells, and from every state of its
    stacks (SUPOX_STACK1 and 2, SUPOX_WAR_NEWS), with each captain's,
    ship's and alliance's name spliced into the replies; the Utwig met
    before or not (UTWIG_VISITS, UTWIG_HOME_VISITS, BOMB_VISITS), and their
    sphere shown (StartSphereTracking: unknown, known already, none,
    extinct); the repairs asked about by each of the Ultron's states
    (ULTRON_CONDITION, SUPOX_ULTRON_HELP); the allies at home by each
    visit, their news walked whole by the mission's state
    (UTWIG_SUPOX_MISSION) and the news told (SUPOX_WAR_NEWS), the 4 ships
    by the escort fleet's room (none, 2, 4, 12); in space by the mission's
    state and each visit, before and after the Kohr-Ah walked whole
    (SUPOX_INFO); and the two races' standings apart (the Supox allied
    with the Utwig not or dead, the Utwig allied with the Supox not or
    dead: CheckAlliance (SUPOX_SHIP) alone is read)."""
    allied = {"alliances": {"UTWIG_SHIP": "good", "SUPOX_SHIP": "good"}}
    back = {"UTWIG_HAVE_ULTRON": 1, "ULTRON_CONDITION": 5}
    places = (("space", space_start), ("home", home_start))
    visit_fields = {"space": "SUPOX_VISITS", "home": "SUPOX_HOME_VISITS"}
    starts = []
    for where, make in places:
        starts.append(make(f"{where}-won-last-battle", {}, activity="WON_LAST_BATTLE"))
        starts.append(make(f"{where}-in-last-battle", {}, activity="IN_LAST_BATTLE", follow=[]))
    starts.append(space_start("hostile-won-last-battle", {"SUPOX_HOSTILE": 1}, "WON_LAST_BATTLE"))
    # Hostile, by each visit: the greeting is the whole talk.
    for where, make in places:
        for visits in range(8):
            starts.append(make(f"hostile-{where}-visit-{visits}", {"SUPOX_HOSTILE": 1, "SUPOX_VISITS": visits}))
    starts.append(home_start("hostile-home-home-visits", {"SUPOX_HOSTILE": 1, "SUPOX_HOME_VISITS": 1}))
    starts.append(space_start("hostile-allied", {"SUPOX_HOSTILE": 1, **back}, **allied))
    starts.append(space_start("hostile-ultron", {"SUPOX_HOSTILE": 1, "ULTRON_CONDITION": 4}))
    # The neutral talk, by each visit; walked whole in space.
    for where, make in places:
        for visits in range(8):
            starts.append(make(f"neutral-{where}-visit-{visits}", {visit_fields[where]: visits},
                               **({} if visits == 0 and where == "space" else {"follow": []})))
    for s1 in range(8):
        for s2 in range(4):
            for news in range(4):
                starts.append(space_start(f"neutral-stack1-{s1}-stack2-{s2}-news-{news}",
                                          {"SUPOX_VISITS": 1, "SUPOX_STACK1": s1, "SUPOX_STACK2": s2,
                                           "SUPOX_WAR_NEWS": news}, follow=[]))
    for s1 in range(3):
        for cname, captain, ship in NAMES:
            start = space_start(f"neutral-{cname}-stack1-{s1}", {"SUPOX_VISITS": 1, "SUPOX_STACK1": s1}, follow=[])
            start["ship"] = talk_ship(captain=captain, ship=ship)
            starts.append(start)
    for alliance in range(4):
        starts.append(space_start(f"neutral-alliance-name-{alliance}",
                                  {"SUPOX_VISITS": 1, "SUPOX_STACK1": 2, "NEW_ALLIANCE_NAME": alliance}, follow=[]))
    start = space_start("neutral-accented-names-alliance-name-3", {"SUPOX_VISITS": 1, "SUPOX_STACK1": 2,
                                                                   "NEW_ALLIANCE_NAME": 3}, follow=[])
    start["ship"] = talk_ship(captain=NAMES[1][1], ship=NAMES[1][2])
    starts.append(start)
    # The Utwig asked about: met before (in space, at home, at the bomb's moon) or not; the Ultron handed over.
    asked = {"SUPOX_VISITS": 1, "SUPOX_STACK1": 5, "SUPOX_STACK2": 2}
    for field in ("UTWIG_VISITS", "UTWIG_HOME_VISITS", "BOMB_VISITS"):
        starts.append(space_start(f"met-utwig-{field}", {**asked, field: 1}))
    for where, make in places:
        starts.append(make(f"{where}-ultron-handed-over", {**asked, visit_fields[where]: 5, "SUPOX_VISITS": 5,
                                                           "SUPOX_WAR_NEWS": 2}))
    # The Utwig's sphere shown (anyone_around_here): unknown, known already, none, extinct.
    starts.append(space_start("sphere-unknown", asked, follow=["NeutralSupox"]))
    starts.append(space_start("sphere-known", asked, fleets={"UTWIG_SHIP": [666, 666]}, follow=["NeutralSupox"]))
    starts.append(space_start("sphere-none", asked, fleets={"UTWIG_SHIP": [0, 0]}, follow=["NeutralSupox"]))
    starts.append(space_start("sphere-extinct", asked, fleets={"UTWIG_SHIP": [0, 0]},
                              alliances={"UTWIG_SHIP": "dead"}, follow=["NeutralSupox"]))
    # The repairs asked about, by each of the Ultron's states.
    for ultron in range(8):
        for helped in (0, 1):
            for news in (0, 2):
                starts.append(space_start(f"ultron-{ultron}-helped-{helped}-news-{news}",
                                          {**asked, "ULTRON_CONDITION": ultron, "SUPOX_ULTRON_HELP": helped,
                                           "SUPOX_WAR_NEWS": news}, follow=["NeutralSupox"]))
    # The allies at home: by each visit; their news walked whole by the mission and the news told; the ships.
    for visits in range(8):
        starts.append(home_start(f"allied-home-visit-{visits}", {**back, "SUPOX_HOME_VISITS": visits}, follow=[],
                                 escorts=[0, 12], **allied))
    for mission in range(8):
        for news in range(4):
            starts.append(home_start(f"allied-home-mission-{mission}-news-{news}",
                                     {**back, "SUPOX_HOME_VISITS": 1, "UTWIG_SUPOX_MISSION": mission,
                                      "SUPOX_WAR_NEWS": news}, escorts=[0, 12],
                                     **({} if news == 0 else {"follow": ["AlliedHome"]}), **allied))
    for room in (0, 2, 4, 12):
        starts.append(home_start(f"allied-home-ships-room-{room}", {**back, "SUPOX_HOME_VISITS": 1},
                                 escorts=[0, room], follow=["ExitConversation"], **allied))
    # The allies in space: by the mission's state and each visit; before and after the Kohr-Ah walked whole.
    for mission in range(8):
        for visits in range(8):
            starts.append(space_start(f"allied-space-mission-{mission}-visit-{visits}",
                                      {**back, "UTWIG_SUPOX_MISSION": mission, "SUPOX_VISITS": visits},
                                      follow=[], **allied))
    for mission in (1, 5):
        for info in (0, 1):
            starts.append(space_start(f"allied-space-mission-{mission}-info-{info}",
                                      {**back, "UTWIG_SUPOX_MISSION": mission, "SUPOX_VISITS": 1, "SUPOX_INFO": info},
                                      **allied))
    # The Supox allied alone (the alliance is read, not the Ultron's flags).
    starts.append(home_start("allied-alone", {"SUPOX_HOME_VISITS": 1}, follow=[],
                             alliances={"SUPOX_SHIP": "good"}))
    # The two races' standings apart (CheckAlliance (SUPOX_SHIP) alone is read): the Supox allied with the
    # Utwig not, or dead; the Utwig allied with the Supox not, or dead: the neutral talk.
    for sname in ("bad", "dead"):
        alone = {"alliances": {"SUPOX_SHIP": "good", "UTWIG_SHIP": sname}}
        starts.append(home_start(f"supox-allied-utwig-{sname}-home", {**back, "SUPOX_HOME_VISITS": 1}, escorts=[0, 12],
                                 follow=["ExitConversation"], **alone))
        starts.append(space_start(f"supox-allied-utwig-{sname}-space", {**back, "UTWIG_SUPOX_MISSION": 1,
                                                                        "SUPOX_VISITS": 1}, follow=[], **alone))
        other = {"alliances": {"SUPOX_SHIP": sname, "UTWIG_SHIP": "good"}}
        for where, make in places:
            starts.append(make(f"supox-{sname}-utwig-allied-{where}", {**back, visit_fields[where]: 1}, follow=[],
                               **other))
    return starts


def thradd_starts():
    """The Thraddash (thraddc.c), from states chosen to reach every
    condition it reads: the endgame's won battle (and the fight before it,
    IN_LAST_BATTLE: the ordinary talk) in space, at their homeworld
    (GLOBAL_FLAGS_AND_DATA bit 7) and at the Aqua Helix's world (bit 6);
    the Helix taken (AQUA_HELIX) by each visit (HELIX_VISITS), whatever
    else; the Ilwrath's war (ILWRATH_FIGHT_THRADDASH) by THRADD_MANNER and
    each visit; the hostile greetings at home by each visit
    (THRADD_HOME_VISITS) and in space by each state of their mission
    (THRADD_MISSION, THRADD_MISSION_VISITS) and each visit (THRADD_VISITS),
    with the praise as the body count passes 8 and 16
    (THRADDASH_BODY_COUNT around each, THRADD_BODY_LEVEL); the hostile
    talk walked whole in space, and from every state of its stacks
    (THRADD_INFO, THRADD_STACK_1 with KOHR_AH_FRENZY, THRADD_HOSTILE_STACK_2
    to 5) and of the mission's return (THRADD_MISSION 4); the Helix's
    world's hostile talk by each visit, walked whole, and from every state
    of the fields it keeps its questions in (THRADD_CULTURE, THRADD_INTRO,
    THRADD_DEMEANOR), the body count ignored there; the worship with 25
    destroyed (the count around 25 with each THRADD_MANNER and place),
    walked whole (every choice of greeting, name and manner), with each
    captain's name spliced into "the slave empire of"; the allies greeted
    by each manner, visit and place, the culture's name spliced in by
    place, their talk walked whole at each place and from every state of
    THRADD_INFO, THRADD_DEMEANOR and THRADD_STACK_1 (their goodbyes); and
    the standing itself, which the script never reads, apart from
    THRADD_MANNER. States UQM can't reach, recorded to pin which field is
    read: THRADD_MANNER set with a body count below 25, the alliance
    without THRADD_MANNER. Not recorded: THRADD_MANNER with no culture
    named (THRADD_CULTURE 0), which UQM's script asserts against."""
    allied = {"alliances": {"THRADDASH_SHIP": "good"}}
    worshipped = {"THRADD_MANNER": 1, "THRADD_CULTURE": 1, "THRADDASH_BODY_COUNT": 25}
    helix = lambda name, state, **kw: home_start(name, state, flags=1 << 6, **kw)
    places = (("space", space_start), ("home", home_start), ("helix", helix))
    starts = []
    for where, make in places:
        starts.append(make(f"{where}-won-last-battle", {}, activity="WON_LAST_BATTLE"))
        starts.append(make(f"{where}-in-last-battle", {}, activity="IN_LAST_BATTLE", follow=[]))
    starts.append(space_start("allied-won-last-battle", worshipped, "WON_LAST_BATTLE", **allied))
    starts.append(space_start("thief-won-last-battle", {"AQUA_HELIX": 1}, "WON_LAST_BATTLE"))
    # The Helix taken: a fight wherever they're met, whatever else.
    for where, make in places:
        for visits in range(8):
            for manner in (0, 1):
                starts.append(make(f"thief-{where}-visit-{visits}-manner-{manner}",
                                   {"AQUA_HELIX": 1, "HELIX_VISITS": visits, "HELIX_UNPROTECTED": 1,
                                    **(worshipped if manner else {})},
                                   **(allied if manner else {})))
    starts.append(space_start("thief-ilwrath-war", {"AQUA_HELIX": 1, "ILWRATH_FIGHT_THRADDASH": 1}))
    # The Ilwrath's war: a greeting alone, by the manner and each visit.
    for where, make in places:
        for visits in range(8):
            for manner in (0, 1):
                starts.append(make(f"ilwrath-war-{where}-visit-{visits}-manner-{manner}",
                                   {"ILWRATH_FIGHT_THRADDASH": 1, "THRADD_VISITS": visits,
                                    **({"THRADD_MANNER": 1, "THRADD_CULTURE": 1} if manner else {})}))
    starts.append(space_start("ilwrath-war-body-count", {"ILWRATH_FIGHT_THRADDASH": 1, "THRADDASH_BODY_COUNT": 25}))
    # The hostile greetings: at home by each visit (the mission not read there), in space by the mission.
    for visits in range(8):
        starts.append(home_start(f"hostile-home-visit-{visits}", {"THRADD_HOME_VISITS": visits}, follow=[]))
    for mission in range(1, 8):
        starts.append(home_start(f"hostile-home-mission-{mission}", {"THRADD_MISSION": mission}, follow=[]))
    for mission in range(8):
        for visits in range(8):
            for told in (0, 1):
                starts.append(space_start(f"hostile-space-mission-{mission}-visit-{visits}-told-{told}",
                                          {"THRADD_MISSION": mission, "THRADD_VISITS": visits,
                                           "THRADD_MISSION_VISITS": told},
                                          **({} if (mission, visits, told) == (0, 0, 0) else {"follow": []})))
    # The praise as the body count grows, below the worship's 25.
    for where, make in places[:2]:
        for count in (0, 7, 8, 15, 16, 24):
            for level in range(4):
                starts.append(make(f"hostile-{where}-count-{count}-level-{level}",
                                   {"THRADDASH_BODY_COUNT": count, "THRADD_BODY_LEVEL": level}, follow=[]))
    # The hostile talk from every state of its stacks.
    quiet = {"THRADD_VISITS": 1, "THRADD_STACK_1": 5, "THRADD_HOSTILE_STACK_3": 1}
    for info in range(4):
        starts.append(space_start(f"hostile-info-{info}", {**quiet, "THRADD_INFO": info}, follow=["ThraddHostile"]))
    for stack in range(8):
        for frenzy in (0, 1):
            starts.append(space_start(f"hostile-stack-{stack}-frenzy-{frenzy}",
                                      {"THRADD_VISITS": 1, "THRADD_STACK_1": stack, "KOHR_AH_FRENZY": frenzy},
                                      follow=["ExitConversation"] if stack == 4 else []))
    for bits in range(16):
        s2, s3, s4, s5 = (bits >> i & 1 for i in range(4))
        starts.append(space_start(f"hostile-stacks-{s2}{s3}{s4}{s5}",
                                  {**quiet, "THRADD_MISSION": 4, "THRADD_HOSTILE_STACK_2": s2,
                                   "THRADD_HOSTILE_STACK_3": s3, "THRADD_HOSTILE_STACK_4": s4,
                                   "THRADD_HOSTILE_STACK_5": s5}, follow=["ExitConversation"]))
    starts.append(space_start("hostile-mission-back", {**quiet, "THRADD_MISSION": 4}, follow=["ThraddHostile"]))
    starts.append(home_start("hostile-home-exits", {"THRADD_HOME_VISITS": 1}, follow=["ExitConversation"]))
    # The Helix's world, hostile: by each visit, walked whole, and from every state of its fields.
    for visits in range(8):
        starts.append(helix(f"helix-visit-{visits}", {"HELIX_VISITS": visits}, **({} if visits == 0 else {"follow": []})))
    for culture in range(4):
        for intro in range(4):
            for demeanor in (0, 1, 4):
                starts.append(helix(f"helix-culture-{culture}-intro-{intro}-demeanor-{demeanor}",
                                    {"HELIX_VISITS": 1, "THRADD_CULTURE": culture, "THRADD_INTRO": intro,
                                     "THRADD_DEMEANOR": demeanor}, follow=[]))
    for count in (8, 16, 25, 31):
        starts.append(helix(f"helix-count-{count}", {"HELIX_VISITS": 1, "THRADDASH_BODY_COUNT": count}, follow=[]))
    # The worship: the body count around 25 with each THRADD_MANNER, in space and at home.
    for where, make in places[:2]:
        for count in (24, 25, 26, 31):
            for manner in (0, 1):
                state = {"THRADDASH_BODY_COUNT": count, "THRADD_VISITS": 3, "THRADD_STACK_1": 5,
                         **({"THRADD_MANNER": 1, "THRADD_CULTURE": 2} if manner else {})}
                starts.append(make(f"worship-{where}-count-{count}-manner-{manner}", state, follow=[]))
    for level in range(4):
        starts.append(space_start(f"worship-level-{level}", {"THRADDASH_BODY_COUNT": 25, "THRADD_BODY_LEVEL": level},
                                  follow=[]))
    starts.append(space_start("worship-walk", {"THRADDASH_BODY_COUNT": 25, "THRADD_VISITS": 3, "THRADD_STACK_1": 5,
                                               "THRADD_CULTURE": 1, "THRADD_INTRO": 2, "THRADD_DEMEANOR": 1}))
    starts.append(home_start("worship-home-allied-already", {"THRADDASH_BODY_COUNT": 25}, follow=["ThraddCulture"],
                             **allied))
    for cname, captain, ship in NAMES:
        start = space_start(f"worship-{cname}", {"THRADDASH_BODY_COUNT": 25}, follow=["ThraddCulture"])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    # The allies: greeted by each manner, visit and place; the culture's name by place.
    for where, make in places:
        for intro in range(4):
            for visits in range(8):
                starts.append(make(f"allied-{where}-intro-{intro}-visit-{visits}",
                                   {**worshipped, "THRADD_INTRO": intro, "THRADD_VISITS": visits}, follow=[], **allied))
        for culture in (2, 3):
            starts.append(make(f"allied-{where}-culture-{culture}", {**worshipped, "THRADD_CULTURE": culture},
                               follow=[], **allied))
        starts.append(make(f"allied-{where}-walk", {**worshipped, "THRADD_VISITS": 1}, **allied))
    for info in range(4):
        starts.append(space_start(f"allied-info-{info}", {**worshipped, "THRADD_INFO": info},
                                  follow=["ThraddAllies"], **allied))
    for demeanor in range(8):
        starts.append(space_start(f"allied-demeanor-{demeanor}", {**worshipped, "THRADD_DEMEANOR": demeanor},
                                  follow=["ThraddAllies"], **allied))
    for stack in range(8):
        starts.append(helix(f"allied-goodbye-{stack}", {**worshipped, "THRADD_STACK_1": stack},
                            follow=["ExitConversation"], **allied))
    # The standing apart from THRADD_MANNER (the script reads the manner alone): the manner with the
    # alliance ended (the Ilwrath's war ends it) or the Thraddash dead, and below 25; the alliance without it.
    for sname in ("bad", "dead"):
        starts.append(space_start(f"manner-alliance-{sname}", worshipped, follow=[],
                                  alliances={"THRADDASH_SHIP": sname}))
    for count in (0, 24):
        starts.append(space_start(f"manner-count-{count}", {**worshipped, "THRADDASH_BODY_COUNT": count}, follow=[],
                                  **allied))
    for where, make in places:
        starts.append(make(f"allied-without-manner-{where}", {"THRADD_VISITS": 1}, follow=[], **allied))
    return starts


def umgah_starts():
    """The Umgah (umgahc.c), from states chosen to reach every condition
    it reads: the endgame's won battle (no line of its own: the ordinary
    talk, at peace from init) and the fight before it; hostile for good
    (UMGAH_HOSTILE) by each visit and place, whatever else; the Talking
    Pet's zombies (UMGAH_ZOMBIE_BLOBBIES) by each visit and place: the
    Pet met (TALKING_PET_VISITS), the secret known (KNOW_UMGAH_ZOMBIES),
    or neither, and then their questions walked whole, the Umgah met
    before they were zombies or not (MET_NORMAL_UMGAH), each captain's
    and ship's name spliced into "we are"; before the Pet in space and at
    their homeworld (GLOBAL_FLAGS_AND_DATA bit 7) by each visit
    (UMGAH_VISITS, UMGAH_HOME_VISITS), walked whole, and from every state
    of UMGAH_EVIL_BLOBBIES and UMGAH_MENTIONED_TRICKS; with the Pet taken
    (TALKING_PET): in space by each visit, at their homeworld the reward
    walked whole (met before or not), by UMGAH_MENTIONED_TRICKS, by the
    bio data aboard (none, some, and 65,035: the 500 given reach the top
    of UQM's 16-bit count; past it UQM's wraps and the port's doesn't,
    docs/differences.md row 22, so that isn't recorded) and by the escort
    fleet's room for the 4 ships (none, 2, 4, 12). Zombies with the Pet
    taken, and with TALKING_PET_VISITS (counted only with the Pet
    aboard), are states UQM reaches, if seldom: taking the Pet frees the
    zombies, but arilouc.c's post_arilou_enc makes them zombies again
    after a talk at the Arilou's homeworld while ARILOU_HOME_VISITS is
    at most 1 (it stays 1 through the visits that ask for the wreck's
    part), so with the Pet taken and used once, and their reward not
    collected (UMGAH_HOSTILE, which its goodbye sets, is read first), the
    Umgah say their DESTROY_INTERFERER greetings. They're recorded by set-up, as every
    zombie state is."""
    places = (("space", space_start), ("home", home_start))
    starts = []
    for where, make in places:
        starts.append(make(f"{where}-won-last-battle", {}, activity="WON_LAST_BATTLE", follow=[]))
        starts.append(make(f"{where}-in-last-battle", {}, activity="IN_LAST_BATTLE", follow=[]))
        starts.append(make(f"{where}-pet-won-last-battle", {"TALKING_PET": 1}, activity="WON_LAST_BATTLE", follow=[]))
        starts.append(make(f"{where}-hostile-won-last-battle", {"UMGAH_HOSTILE": 1}, activity="WON_LAST_BATTLE"))
    # Hostile for good: by each visit and place, whatever else.
    for where, make in places:
        for visits in range(8):
            starts.append(make(f"hostile-{where}-visit-{visits}", {"UMGAH_HOSTILE": 1, "TALKING_PET": 1,
                                                                 "UMGAH_VISITS": visits}))
        starts.append(make(f"hostile-{where}-home-visits", {"UMGAH_HOSTILE": 1, "TALKING_PET": 1,
                                                             "UMGAH_HOME_VISITS": 2}))
        starts.append(make(f"hostile-{where}-no-pet", {"UMGAH_HOSTILE": 1}))
        starts.append(make(f"hostile-{where}-zombies", {"UMGAH_HOSTILE": 1, "UMGAH_ZOMBIE_BLOBBIES": 1,
                                                         "KNOW_UMGAH_ZOMBIES": 1}))
    # The zombies: the Pet met, the secret known, or neither, by each visit and place.
    for where, make in places:
        for met_pet in (0, 1):
            for know in (0, 1):
                for visits in range(8):
                    starts.append(make(f"zombies-{where}-pet-{met_pet}-know-{know}-visit-{visits}",
                                       {"UMGAH_ZOMBIE_BLOBBIES": 1, "TALKING_PET_VISITS": met_pet,
                                        "KNOW_UMGAH_ZOMBIES": know, "UMGAH_VISITS": visits}, follow=[]))
        starts.append(make(f"zombies-{where}-home-visits", {"UMGAH_ZOMBIE_BLOBBIES": 1, "UMGAH_HOME_VISITS": 2},
                           follow=[]))
        starts.append(make(f"zombies-{where}-pet-taken", {"UMGAH_ZOMBIE_BLOBBIES": 1, "TALKING_PET": 1}, follow=[]))
    for met in (0, 1):
        starts.append(space_start(f"zombies-walk-met-{met}", {"UMGAH_ZOMBIE_BLOBBIES": 1, "MET_NORMAL_UMGAH": met,
                                                              "UMGAH_VISITS": 2}))
        starts.append(home_start(f"zombies-home-met-{met}", {"UMGAH_ZOMBIE_BLOBBIES": 1, "MET_NORMAL_UMGAH": met},
                                 follow=["CombatIsInevitable"]))
    for cname, captain, ship in NAMES:
        start = space_start(f"zombies-{cname}", {"UMGAH_ZOMBIE_BLOBBIES": 1}, follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    # Before the Pet: by each visit and place, walked whole, and by its two flags.
    for where, make in places:
        field = "UMGAH_VISITS" if where == "space" else "UMGAH_HOME_VISITS"
        other = "UMGAH_HOME_VISITS" if where == "space" else "UMGAH_VISITS"
        for visits in range(8):
            starts.append(make(f"normal-{where}-visit-{visits}", {field: visits},
                               **({} if visits == 0 else {"follow": []})))
        starts.append(make(f"normal-{where}-other-visits", {other: 2}, follow=[]))
        starts.append(make(f"normal-{where}-met", {field: 1, "MET_NORMAL_UMGAH": 1, "KNOW_UMGAH_ZOMBIES": 1},
                           follow=["CombatIsInevitable"]))
    for evil in (0, 1):
        for tricks in range(4):
            starts.append(space_start(f"normal-evil-{evil}-tricks-{tricks}",
                                      {"UMGAH_VISITS": 1, "UMGAH_EVIL_BLOBBIES": evil,
                                       "UMGAH_MENTIONED_TRICKS": tricks}, follow=["CombatIsInevitable"]))
    # With the Pet taken: in space by each visit; at their homeworld the reward.
    for visits in range(8):
        starts.append(space_start(f"pet-space-visit-{visits}", {"TALKING_PET": 1, "UMGAH_VISITS": visits}))
    starts.append(space_start("pet-space-home-visits", {"TALKING_PET": 1, "UMGAH_HOME_VISITS": 2}))
    for met in (0, 1):
        starts.append(home_start(f"reward-walk-met-{met}", {"TALKING_PET": 1, "MET_NORMAL_UMGAH": met},
                                 escorts=[0, 12]))
    for tricks in range(4):
        starts.append(home_start(f"reward-tricks-{tricks}", {"TALKING_PET": 1, "MET_NORMAL_UMGAH": 1,
                                                             "UMGAH_MENTIONED_TRICKS": tricks},
                                 escorts=[0, 12], follow=["UmgahReward"]))
    for visits in (1, 7):
        starts.append(home_start(f"reward-home-visits-{visits}", {"TALKING_PET": 1, "UMGAH_HOME_VISITS": visits,
                                                                  "UMGAH_VISITS": visits}, follow=[]))
    for bio in (7, 65035):
        starts.append(home_start(f"reward-bio-{bio}", {"TALKING_PET": 1, "MET_NORMAL_UMGAH": 1,
                                                       "UMGAH_MENTIONED_TRICKS": 2}, bio=bio, escorts=[0, 12]))
    for room in (0, 2, 4, 12):
        starts.append(home_start(f"reward-ships-room-{room}", {"TALKING_PET": 1, "MET_NORMAL_UMGAH": 1},
                                 escorts=[0, room], follow=["CombatIsInevitable"]))
    return starts


def talkpet_starts():
    """The Talking Pet (talkpet.c), from states chosen to reach every
    condition it reads: the endgame's won battle; the pep talk before the
    last battle by UQM's Cyborg option (cyborg: GLOBAL (glob_flags)'s
    CYBORG_ENABLED); before the Sa-Matra (READY_TO_CONFUSE_URQUAN) by the
    bomb's state (CHMMR_BOMB_STATE, UTWIG_BOMB_ON_SHIP) and the Ur-Quan
    compelled or not (URQUAN_MESSED_UP), its questions there walked
    whole; aboard (TALKING_PET_ON_SHIP) by each visit (TALKING_PET_VISITS)
    with and without the Taalo protector (TAALO_PROTECTOR_ON_SHIP), its
    talk walked whole (the mind-control strobe turned on twice: the
    ambient animations' flags change the first time only), from every
    state of its news and suggestions (TALKING_PET_INFO,
    TALKING_PET_SUGGESTIONS), with a ship to compel (SHIP_TO_COMPEL) and
    its lie known (DNYARRI_LIED, LEARNED_TALKING_PET); at the Umgah's
    world with the enemy's ships there (enemy_ships: GetHeadLink (&GLOBAL
    (npc_built_ship_q))) by each visit (TALKING_PET_HOME_VISITS) and the
    protector, the first visit's questions walked whole with the zombies
    known or not (KNOW_UMGAH_ZOMBIES) and each ship's name spliced into
    "we are"; TALKING_PET_ON_SHIP apart from TALKING_PET (the first alone
    is read; each set without the other, with enemy ships and none, and
    before the Sa-Matra); with no enemy ships its deal, the Umgah alive (their sphere
    unknown, known already or none) or extinct (how_trust kept only for
    one who met it before with the protector), walked whole with
    LEARNED_TALKING_PET or not, and the counters its coming aboard
    clears. States UQM can't reach, recorded to pin the order the fields
    are read in or a branch nothing sets the flag for:
    READY_TO_CONFUSE_URQUAN without the Pet aboard; TALKING_PET_ON_SHIP
    without TALKING_PET, or the other way (talkpet.c sets the two
    together and nothing in UQM's play clears either); LEARNED_TALKING_PET
    (no UQM code sets it, so "bullshit" and "you lied" are dead in UQM)."""
    at_umgah = lambda name, state, ships=10, **kw: home_start(name, state, enemy_ships=ships, **kw)
    aboard = lambda name, state, **kw: space_start(name, {"TALKING_PET": 1, "TALKING_PET_ON_SHIP": 1, **state}, **kw)
    deal = lambda name, state, **kw: home_start(name, state, **kw)
    gone = {"alliances": {"UMGAH_SHIP": "dead"}, "fleets": {"UMGAH_SHIP": [0, 0]}}
    starts = []
    starts.append(space_start("won-last-battle", {}, "WON_LAST_BATTLE"))
    starts.append(aboard("aboard-won-last-battle", {"READY_TO_CONFUSE_URQUAN": 1}, activity="WON_LAST_BATTLE"))
    for cyborg in (0, 1):
        for compel in (0, 1):
            starts.append(aboard(f"last-battle-cyborg-{cyborg}-compel-{compel}", {"SHIP_TO_COMPEL": compel},
                                 activity="IN_LAST_BATTLE", cyborg=cyborg, enemy_ships=1))
    starts.append(space_start("last-battle-not-aboard", {"READY_TO_CONFUSE_URQUAN": 1}, "IN_LAST_BATTLE", enemy_ships=1))
    # Before the Sa-Matra: by the bomb's state, the bomb aboard, the Ur-Quan compelled.
    for bomb in range(4):
        for aboard_bomb in (0, 1):
            for messed in (0, 1):
                starts.append(aboard(f"samatra-bomb-{bomb}-aboard-{aboard_bomb}-compelled-{messed}",
                                     {"READY_TO_CONFUSE_URQUAN": 1, "CHMMR_BOMB_STATE": bomb,
                                      "UTWIG_BOMB_ON_SHIP": aboard_bomb, "URQUAN_MESSED_UP": messed,
                                      "SHIP_TO_COMPEL": 1}, enemy_ships=1))
    starts.append(space_start("samatra-not-aboard", {"READY_TO_CONFUSE_URQUAN": 1, "CHMMR_BOMB_STATE": 3},
                              enemy_ships=1, follow=[]))
    # Aboard: by each visit and the protector; its talk walked whole; its counted lines; a ship to compel; its lie.
    for visits in range(8):
        for taalo in (0, 1):
            starts.append(aboard(f"aboard-visit-{visits}-taalo-{taalo}",
                                 {"TALKING_PET_VISITS": visits, "TAALO_PROTECTOR_ON_SHIP": taalo},
                                 **({} if (visits, taalo) == (0, 1) else {"follow": []})))
    starts.append(aboard("aboard-enemy-ships", {"TALKING_PET_HOME_VISITS": 1}, enemy_ships=3, follow=[]))
    # TALKING_PET_ON_SHIP apart from TALKING_PET (the script reads the first alone; the port's start options
    # set each without the other: --start-devices TALKING_PET_DEVICE, --start-state TALKING_PET=1): aboard
    # by the first alone, with enemy ships or none; with the second alone its world's talks, by the enemy's ships.
    for ships in (0, 10):
        starts.append(space_start(f"on-ship-alone-ships-{ships}", {"TALKING_PET_ON_SHIP": 1, "TALKING_PET_VISITS": 1},
                                  enemy_ships=ships, follow=[]))
        starts.append(home_start(f"taken-alone-ships-{ships}", {"TALKING_PET": 1}, enemy_ships=ships, follow=[]))
    starts.append(space_start("on-ship-alone-ready", {"TALKING_PET_ON_SHIP": 1, "READY_TO_CONFUSE_URQUAN": 1,
                                                      "CHMMR_BOMB_STATE": 3}, enemy_ships=1, follow=[]))
    starts.append(space_start("taken-alone-ready", {"TALKING_PET": 1, "READY_TO_CONFUSE_URQUAN": 1,
                                                    "CHMMR_BOMB_STATE": 3}, enemy_ships=1, follow=[]))
    for n in range(8):
        starts.append(aboard(f"aboard-info-{n}", {"TALKING_PET_VISITS": 1, "TALKING_PET_INFO": n},
                             follow=["PetDevice"]))
        starts.append(aboard(f"aboard-suggestions-{n}", {"TALKING_PET_VISITS": 1, "TALKING_PET_SUGGESTIONS": n},
                             follow=["PetDevice"]))
    for compel in (0, 1):
        for lied in (0, 1):
            for learned in (0, 1):
                starts.append(aboard(f"aboard-compel-{compel}-lied-{lied}-learned-{learned}",
                                     {"TALKING_PET_VISITS": 1, "TALKING_PET_INFO": 7, "TALKING_PET_SUGGESTIONS": 7,
                                      "SHIP_TO_COMPEL": compel, "DNYARRI_LIED": lied,
                                      "LEARNED_TALKING_PET": learned}, follow=["PetDevice", "ExitConversation"]))
    # At the Umgah's world with its zombies' ships: by each visit and the protector.
    for visits in range(8):
        for taalo in (0, 1):
            starts.append(at_umgah(f"zombies-visit-{visits}-taalo-{taalo}",
                                   {"UMGAH_ZOMBIE_BLOBBIES": 1, "TALKING_PET_HOME_VISITS": visits,
                                    "TAALO_PROTECTOR_ON_SHIP": taalo, "UMGAH_VISITS": 3},
                                   **({} if visits != 0 else {"follow": []})))
    for know in (0, 1):
        for taalo in (0, 1):
            starts.append(at_umgah(f"zombies-walk-know-{know}-taalo-{taalo}",
                                   {"UMGAH_ZOMBIE_BLOBBIES": 1, "KNOW_UMGAH_ZOMBIES": know,
                                    "TAALO_PROTECTOR_ON_SHIP": taalo}))
    starts.append(at_umgah("zombies-one-ship", {"UMGAH_ZOMBIE_BLOBBIES": 1}, ships=1, follow=[]))
    for cname, captain, ship in NAMES:
        start = at_umgah(f"zombies-{cname}", {"UMGAH_ZOMBIE_BLOBBIES": 1}, follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    # No enemy ships: its deal. The Umgah alive (their sphere unknown, known, none) or extinct.
    starts.append(deal("deal-walk", {"UMGAH_ZOMBIE_BLOBBIES": 1, "TALKING_PET_HOME_VISITS": 1}))
    starts.append(deal("deal-walk-learned", {"UMGAH_ZOMBIE_BLOBBIES": 1, "TALKING_PET_HOME_VISITS": 1,
                                             "LEARNED_TALKING_PET": 1}))
    starts.append(deal("deal-sphere-known", {"UMGAH_ZOMBIE_BLOBBIES": 1}, fleets={"UMGAH_SHIP": [333, 333]},
                       follow=[]))
    starts.append(deal("deal-sphere-none", {"UMGAH_ZOMBIE_BLOBBIES": 1}, fleets={"UMGAH_SHIP": [0, 0]}, follow=[]))
    for visits in (0, 1):
        for taalo in (0, 1):
            starts.append(deal(f"deal-umgah-gone-visits-{visits}-taalo-{taalo}",
                               {"TALKING_PET_HOME_VISITS": visits, "TAALO_PROTECTOR_ON_SHIP": taalo},
                               prefix=[0], follow=[], **gone))
            starts.append(deal(f"deal-umgah-alive-visits-{visits}-taalo-{taalo}",
                               {"UMGAH_ZOMBIE_BLOBBIES": 1, "TALKING_PET_HOME_VISITS": visits,
                                "TAALO_PROTECTOR_ON_SHIP": taalo}, prefix=[0], follow=[]))
    starts.append(deal("deal-umgah-gone-walk", {"TALKING_PET_HOME_VISITS": 2, "TAALO_PROTECTOR_ON_SHIP": 1},
                       follow=["PetDeal"], **gone))
    starts.append(deal("deal-umgah-gone-greeting", {}, follow=[], **gone))
    # Its coming aboard: the Umgah freed, every counter it clears set.
    starts.append(deal("deal-counters", {"UMGAH_ZOMBIE_BLOBBIES": 1, "UMGAH_VISITS": 5, "UMGAH_HOME_VISITS": 3,
                                         "ARILOU_STACK_2": 1, "SHIP_TO_COMPEL": 1, "KNOW_UMGAH_ZOMBIES": 1},
                       prefix=[0], follow=["ExitConversation"]))
    return starts


def arilou_starts():
    """The Arilou (arilouc.c), from states chosen to reach every condition
    it reads: the endgame's won battle, in hyperspace and in QuasiSpace
    (ARILOU_SPACE_SIDE: 0 or 1 in hyperspace, 2 or more in QuasiSpace,
    each ship's own); the first meeting (MET_ARILOU) at each side, walked
    whole, whatever their manner; their enemies for good (ARILOU_MANNER 2)
    by each visit on each side; angry (ARILOU_MANNER 1) in hyperspace by
    each visit and walked whole, and at their homeworld walked whole (its
    friendship's reply alone, by a prefix); friendly (0) and friends for
    good (3, at peace from init) in hyperspace by each visit, their small
    talk walked from every state of their news (ARILOU_INFO), their hints
    (ARILOU_HINTS, the shattered world known or not) and why they're here
    (ARILOU_STACK_5); at their homeworld the greetings by visit, the wreck
    asked for and the portal spawner not given (NO_PORTAL_VISITS) or given,
    every menu open with each asked from first (the last asked is offered
    first), and each of its menus walked on its own, the others closed: the war
    (ARILOU_STACK_1), Earth (ARILOU_STACK_3), the portal and the Talking
    Pet (KNOW_ARILOU_WANT_WRECK, ARILOU_STACK_4, PORTAL_KEY_ON_SHIP,
    PORTAL_SPAWNER, the second visit's "clever human"), the Pet met
    (TALKING_PET_VISITS, ARILOU_STACK_2) or taken (TALKING_PET), the Umgah
    (KNOW_UMGAH_ZOMBIES and each ARILOU_CHECKED_UMGAH: the check's event
    queued), and the insult's reply by manner; then the goodbye by
    ARILOU_HOME_VISITS and manner, for post_arilou_enc's zombies and
    friendship. A hostile ending (the encounter screen's attack, which
    the recording doesn't make) is the world's tests'."""
    quasi = lambda name, state, **kw: space_start(name, {"ARILOU_SPACE_SIDE": 2, **state}, **kw)
    sides = (("space", space_start), ("quasi", quasi))
    starts = []
    for where, make in sides:
        starts.append(make(f"{where}-won-last-battle", {}, activity="WON_LAST_BATTLE"))
        starts.append(make(f"{where}-in-last-battle", {"MET_ARILOU": 1, "ARILOU_MANNER": 2},
                           activity="IN_LAST_BATTLE", follow=[]))
    # The first meeting, walked whole at each side, whatever their manner.
    starts.append(space_start("first-space", {}))
    starts.append(quasi("first-quasi", {}, follow=["ArilouHome", "ExitConversation"]))
    for side in (1, 3):
        starts.append(space_start(f"first-side-{side}", {"ARILOU_SPACE_SIDE": side}, follow=[]))
    for manner in (1, 2, 3):
        for where, make in sides:
            starts.append(make(f"first-{where}-manner-{manner}", {"ARILOU_MANNER": manner}, follow=[]))
    met = {"MET_ARILOU": 1}
    # Their enemies for good, by each visit at each side.
    for where, make in sides:
        for visits in range(8):
            starts.append(make(f"hostile-{where}-visit-{visits}", {**met, "ARILOU_MANNER": 2,
                                                                  "ARILOU_VISITS": visits}))
    # Angry: in hyperspace by each visit and walked whole; at their homeworld walked whole.
    for visits in range(8):
        starts.append(space_start(f"angry-space-visit-{visits}", {**met, "ARILOU_MANNER": 1, "ARILOU_VISITS": visits},
                                  **({} if visits == 1 else {"follow": []})))
    for home in (0, 1, 2):
        starts.append(quasi(f"angry-home-visits-{home}", {**met, "ARILOU_MANNER": 1, "ARILOU_HOME_VISITS": home,
                                                          "ARILOU_STACK_1": 2, "ARILOU_STACK_3": 3,
                                                          "KNOW_ARILOU_WANT_WRECK": 1, "ARILOU_STACK_4": 1},
                            follow=["AngryHomeArilou", "ExitConversation"]))
    starts.append(quasi("angry-home-friends", {**met, "ARILOU_MANNER": 1, "ARILOU_STACK_1": 2, "ARILOU_STACK_3": 3,
                                                "KNOW_ARILOU_WANT_WRECK": 1, "ARILOU_STACK_4": 1},
                        prefix=[2], follow=["ExitConversation"]))
    # Friendly and friends for good, in hyperspace: by each visit, and the small talk walked.
    for manner in (0, 3):
        for visits in range(8):
            starts.append(space_start(f"friendly-{manner}-space-visit-{visits}",
                                      {**met, "ARILOU_MANNER": manner, "ARILOU_VISITS": visits}, follow=[]))
    for info in range(4):
        starts.append(space_start(f"friendly-info-{info}", {**met, "ARILOU_VISITS": 1, "ARILOU_INFO": info,
                                                            "ARILOU_HINTS": 3, "ARILOU_STACK_5": 3}))
    for hints in range(4):
        for shattered in ((0, 1, 2, 3) if hints == 1 else (0,)):
            starts.append(space_start(f"friendly-hints-{hints}-shattered-{shattered}",
                                      {**met, "ARILOU_VISITS": 1, "ARILOU_INFO": 3, "ARILOU_HINTS": hints,
                                       "KNOW_ABOUT_SHATTERED": shattered, "ARILOU_STACK_5": 3}))
    for stack in range(4):
        starts.append(space_start(f"friendly-stack5-{stack}", {**met, "ARILOU_VISITS": 1, "ARILOU_STACK_5": stack,
                                                               "ARILOU_MANNER": 3 if stack == 2 else 0}))
    # At their homeworld: the greetings by visit, the wreck asked for and the spawner not given, or given.
    closed = {"ARILOU_STACK_1": 2, "ARILOU_STACK_3": 3, "KNOW_ARILOU_WANT_WRECK": 1, "ARILOU_STACK_4": 1,
              "PORTAL_SPAWNER": 1}
    for visits in range(8):
        starts.append(quasi(f"home-visit-{visits}", {**met, "ARILOU_HOME_VISITS": visits, **closed}, follow=[]))
    for visits in (0, 1):
        for home in (0, 1, 2):
            starts.append(quasi(f"home-no-portal-visit-{visits}-home-{home}",
                                {**met, **closed, "PORTAL_SPAWNER": 0, "NO_PORTAL_VISITS": visits,
                                 "ARILOU_HOME_VISITS": home}, follow=["ExitConversation"]))
    starts.append(quasi("home-wreck-not-asked", {**met, "ARILOU_HOME_VISITS": 1, "ARILOU_STACK_1": 2,
                                                  "ARILOU_STACK_3": 3}, follow=[]))
    # Each menu on its own, the others closed.
    home = {**met, "ARILOU_HOME_VISITS": 2}
    starts.append(quasi("home-war", {**home, **closed, "ARILOU_STACK_1": 0}))
    starts.append(quasi("home-earth", {**home, **closed, "ARILOU_STACK_3": 0}))
    for spawner in (0, 1):
        starts.append(quasi(f"home-portal-spawner-{spawner}",
                            {**home, **closed, "KNOW_ARILOU_WANT_WRECK": 0, "ARILOU_STACK_4": 0,
                             "PORTAL_SPAWNER": spawner}))
    for visits in (1, 2):
        for spawner in (0, 1):
            starts.append(quasi(f"home-key-visits-{visits}-spawner-{spawner}",
                                {**met, **closed, "ARILOU_HOME_VISITS": visits - 1, "PORTAL_SPAWNER": spawner,
                                 "PORTAL_KEY_ON_SHIP": 1}))
    for pet_visits in (1, 5):
        starts.append(quasi(f"home-pet-met-{pet_visits}", {**home, **closed, "TALKING_PET_VISITS": pet_visits}))
    starts.append(quasi("home-pet-met-stack-1", {**home, **closed, "TALKING_PET_VISITS": 1, "ARILOU_STACK_2": 1},
                        follow=[]))
    starts.append(quasi("home-pet-taken", {**home, **closed, "TALKING_PET": 1, "TALKING_PET_VISITS": 1,
                                           "KNOW_UMGAH_ZOMBIES": 1}, follow=[]))
    for checked in range(4):
        starts.append(quasi(f"home-umgah-checked-{checked}",
                            {**home, **closed, "KNOW_UMGAH_ZOMBIES": 1, "ARILOU_CHECKED_UMGAH": checked}))
    starts.append(quasi("home-umgah-not-known", {**home, **closed, "ARILOU_CHECKED_UMGAH": 2}, follow=[]))
    # Every menu open: the one last asked from offered first (LastStack), each in turn.
    open_ = {**home, "PORTAL_SPAWNER": 1}
    for first in range(4):
        starts.append(quasi(f"home-order-pet-{first}", {**open_, "TALKING_PET_VISITS": 1}, prefix=[first], follow=[]))
        starts.append(quasi(f"home-order-umgah-{first}", {**open_, "KNOW_UMGAH_ZOMBIES": 1, "ARILOU_CHECKED_UMGAH": 2},
                            prefix=[first], follow=[]))
    # The insult's reply by manner, and the goodbye by visit and manner (post_arilou_enc).
    for manner in (0, 1, 2, 3):
        for visits in (0, 1, 2):
            starts.append(quasi(f"home-bye-manner-{manner}-visits-{visits}",
                                {**met, **closed, "ARILOU_MANNER": manner, "ARILOU_HOME_VISITS": visits,
                                 "UMGAH_VISITS": 3, "UMGAH_HOME_VISITS": 2},
                                follow=["ExitConversation", "AngryHomeArilou"]))
    return starts


def syreen_starts():
    """The Syreen at their starbase (syreenc.c; gensyr.c sets no
    GLOBAL_FLAGS_AND_DATA), from states chosen to reach every condition
    it reads: the endgame's won battle; before they know of the Mycon
    (SYREEN_KNOW_ABOUT_MYCON) their greetings by each visit
    (SYREEN_HOME_VISITS); the first two visits' introductions walked (the
    four replies, the two "I need"s) with each captain's and ship's name
    spliced into "we are" and the welcome, and the first two questions'
    answers by a prefix; their talk's menus each walked on its own, the
    others closed (SYREEN_STACK0 to 2), two of them together, and all of
    them open with each asked from first, for the order the last asked
    puts first; the shattered world
    (KNOW_ABOUT_SHATTERED, KNOW_SYREEN_WORLD_SHATTERED, SYREEN_WANT_PROOF)
    and each egg case aboard, to the horrible truth and the shuttle
    (KNOW_SYREEN_VAULT or not); the vault looked for by each visit and
    its hint; the vault found (SHIP_VAULT_UNLOCKED): the ambush planned
    and the reward walked whole; the ambush ready by each visit; and
    after it (MYCON_KNOW_AMBUSH) the greetings by each visit (the
    alliance at the first, the Syreen living or extinct), their news
    (SYREEN_INFO) and the reward had or not (PLAYER_HAD_SEX)."""
    base = lambda name, state, **kw: home_start(name, state, flags=0, **kw)
    starts = [base("won-last-battle", {}, activity="WON_LAST_BATTLE")]
    for visits in range(8):
        starts.append(base(f"before-visit-{visits}", {"SYREEN_HOME_VISITS": visits}, follow=[]))
    starts.append(base("initial", {}, follow=["InitialSyreen"]))
    starts.append(base("initial-second", {"SYREEN_HOME_VISITS": 1}, follow=[]))
    for first in range(4):
        for second in (0, 1):
            starts.append(base(f"initial-{first}-normal-{second}", {}, prefix=[first, second], follow=[]))
    for cname, captain, ship in NAMES:
        start = base(f"initial-{cname}", {}, prefix=[2], follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    normal = {"SYREEN_HOME_VISITS": 2}
    closed = {"SYREEN_STACK0": 3, "SYREEN_STACK1": 3, "SYREEN_STACK2": 3}
    for stack in range(3):
        for value in range(4):
            starts.append(base(f"normal-stack{stack}-{value}", {**normal, **closed, f"SYREEN_STACK{stack}": value},
                               **({} if value == 0 else {"follow": []})))
    starts.append(base("normal-stacks-0-1", {**normal, **closed, "SYREEN_STACK0": 0, "SYREEN_STACK1": 0}))
    # Every menu open: the one last asked from offered first (LastStack), each in turn.
    for first in range(4):
        starts.append(base(f"normal-order-{first}", {**normal, "KNOW_ABOUT_SHATTERED": 2,
                                                     "KNOW_SYREEN_WORLD_SHATTERED": 1}, prefix=[first], follow=[]))
    # The shattered world, and the egg cases: the proof, the truth and the shuttle.
    for shattered in range(4):
        for world in (0, 1):
            for proof in (0, 1):
                starts.append(base(f"shattered-{shattered}-world-{world}-proof-{proof}",
                                   {**normal, **closed, "KNOW_ABOUT_SHATTERED": shattered,
                                    "KNOW_SYREEN_WORLD_SHATTERED": world, "SYREEN_WANT_PROOF": proof},
                                   **({} if (shattered, world, proof) == (2, 1, 0) else {"follow": []})))
    for case in range(3):
        starts.append(base(f"egg-case-{case}", {**normal, **closed, "KNOW_ABOUT_SHATTERED": 3,
                                                f"EGG_CASE{case}_ON_SHIP": 1}, follow=["NormalSyreen"]))
    for vault in (0, 1):
        starts.append(base(f"egg-case-shuttle-vault-{vault}",
                           {**normal, **closed, "KNOW_ABOUT_SHATTERED": 3, "EGG_CASE1_ON_SHIP": 1,
                            "KNOW_SYREEN_VAULT": vault, "SYREEN_WANT_PROOF": 1}))
    know = {"SYREEN_KNOW_ABOUT_MYCON": 1}
    # The vault looked for, by each visit; found: the ambush planned and the reward.
    for visits in range(8):
        starts.append(base(f"vault-visit-{visits}", {**know, "SYREEN_HOME_VISITS": visits},
                           **({} if visits == 0 else {"follow": []})))
    for visits in (0, 5):
        starts.append(base(f"vault-found-visit-{visits}", {**know, "SHIP_VAULT_UNLOCKED": 1,
                                                           "SYREEN_HOME_VISITS": visits},
                           **({} if visits == 0 else {"follow": []})))
    # The ambush ready, by each visit; after it, by each visit, their news and the reward.
    for visits in range(8):
        starts.append(base(f"ambush-ready-visit-{visits}", {**know, "MYCON_AMBUSH": 1, "SHIP_VAULT_UNLOCKED": 1,
                                                            "SYREEN_HOME_VISITS": visits},
                           **({} if visits == 0 else {"follow": []})))
    after = {**know, "MYCON_AMBUSH": 1, "MYCON_KNOW_AMBUSH": 1}
    for visits in range(8):
        starts.append(base(f"after-ambush-visit-{visits}", {**after, "SYREEN_HOME_VISITS": visits}, follow=[]))
    starts.append(base("after-ambush-extinct", after, alliances={"SYREEN_SHIP": "dead"}, follow=[]))
    starts.append(base("after-ambush-allied", after, alliances={"SYREEN_SHIP": "good"}, follow=[]))
    for info in range(4):
        for had in (0, 1):
            starts.append(base(f"after-ambush-info-{info}-had-{had}",
                               {**after, "SYREEN_HOME_VISITS": 2, "SYREEN_INFO": info, "PLAYER_HAD_SEX": had},
                               follow=["AfterAmbush", "FriendlyExit"]))
    starts.append(base("after-ambush-reward", {**after, "SYREEN_HOME_VISITS": 2}, prefix=[0, 0],
                       follow=["Foreplay", "Sex", "FriendlyExit"]))
    return starts


# The ships the Chmmr's bomb rebuilds (chmmrc.c's proceed): a new game's,
# one with no gaps, one with modules at the back only and one with none.
CHMMR_SHIPS = (("new-game", {}),
               ("full", {"modules": ["CREW_POD", "FUEL_TANK", "STORAGE_BAY", "GUN_WEAPON"] * 4, "crew": 120,
                         "fuel": 3000, "landers": 3, "ru": 4321, "elements": [5, 0, 7, 0, 0, 2, 9, 1],
                         "drives": [0, 3], "jets": []}),
               ("back", {"modules": ["EMPTY"] * 12 + ["CREW_POD", "EMPTY", "FUEL_TANK", "STORAGE_BAY"]}),
               ("empty", {"modules": ["EMPTY"] * 16, "drives": [], "jets": list(range(8))}))


def chmmr_starts():
    """The Chmmr (chmmrc.c), from states chosen to reach every condition
    it reads: shielded (CHMMR_EMERGING not set) by each visit
    (CHMMR_HOME_VISITS), their stack (CHMMR_STACK) walked, and their
    advice walked with everything it asks about aboard and known
    (TALKING_PET_ON_SHIP, UTWIG_BOMB_ON_SHIP, SUN_DEVICE_ON_SHIP,
    AWARE_OF_SAMATRA), with none, and with each alone; emerging at the
    first visit (the captain's introductions with each name spliced in)
    and later, walked whole by the Pet and the bomb aboard and the
    Sa-Matra known; unleashed (CHMMR_UNLEASHED) not ready, by what's
    missing and their standing (allied, not, or extinct: CheckAlliance
    asks for GOOD_GUY), and ready, walked to "proceed"
    with and without the starbase (STARBASE_AVAILABLE: the modules
    cleared past the bomb's, or moved to the back) on ships of each
    layout (CHMMR_SHIPS); and after the bomb (CHMMR_BOMB_STATE 2 and 3)
    by each visit, walked by CHMMR_STACK. Met in space as a group
    --start-encounter brings, and at their world (GLOBAL_FLAGS_AND_DATA
    bit 7 as genchmmr.c sets it), which the script doesn't read."""
    starts = [space_start("won-last-battle", {}, activity="WON_LAST_BATTLE", follow=[]),
              home_start("home-shielded", {}, follow=[])]
    for visits in range(8):
        starts.append(space_start(f"shielded-visit-{visits}", {"CHMMR_HOME_VISITS": visits}, follow=[]))
    starts.append(space_start("shielded-stack", {"CHMMR_HOME_VISITS": 1},
                              follow=["ChmmrShielded", "ExitConversation"]))
    everything = {"TALKING_PET_ON_SHIP": 1, "UTWIG_BOMB_ON_SHIP": 1, "SUN_DEVICE_ON_SHIP": 1, "AWARE_OF_SAMATRA": 1}
    starts.append(space_start("advice-everything", {"CHMMR_HOME_VISITS": 1, "CHMMR_STACK": 3, **everything},
                              follow=["ChmmrAdvice", "ChmmrShielded"]))
    starts.append(space_start("advice-nothing", {"CHMMR_HOME_VISITS": 1, "CHMMR_STACK": 3},
                              follow=["ChmmrAdvice", "ChmmrShielded"]))
    for flag in everything:
        starts.append(space_start(f"advice-{flag.lower()}", {"CHMMR_HOME_VISITS": 1, "CHMMR_STACK": 3, flag: 1},
                                  follow=["ChmmrAdvice"]))
    # Emerging: the first visit and later, by what's aboard and known.
    for pet in (0, 1):
        for bomb in (0, 1):
            for samatra in (0, 1):
                starts.append(space_start(f"emerging-pet-{pet}-bomb-{bomb}-samatra-{samatra}",
                                          {"CHMMR_EMERGING": 1, "TALKING_PET_ON_SHIP": pet, "UTWIG_BOMB_ON_SHIP": bomb,
                                           "AWARE_OF_SAMATRA": samatra},
                                          follow=["ChmmrFree", "NotReady", "ImproveBomb"]))
    for visits in (1, 5):
        starts.append(space_start(f"emerging-visit-{visits}", {"CHMMR_EMERGING": 1, "CHMMR_HOME_VISITS": visits},
                                  follow=["ChmmrFree"]))
    for cname, captain, ship in NAMES:
        start = space_start(f"emerging-{cname}", {"CHMMR_EMERGING": 1}, follow=[])
        start["ship"] = talk_ship(captain=captain, ship=ship)
        starts.append(start)
    # Unleashed: not ready, by what's missing and the alliance; ready, to "proceed".
    unleashed = {"CHMMR_UNLEASHED": 1}
    for pet, bomb in ((0, 0), (1, 0), (0, 1)):
        for allied in ("bad", "good", "dead"):
            starts.append(space_start(f"unleashed-pet-{pet}-bomb-{bomb}-{allied}",
                                      {**unleashed, "TALKING_PET_ON_SHIP": pet, "UTWIG_BOMB_ON_SHIP": bomb},
                                      alliances={"CHMMR_SHIP": allied}))
    ready = {**unleashed, "TALKING_PET_ON_SHIP": 1, "UTWIG_BOMB_ON_SHIP": 1}
    for allied in ("bad", "good", "dead"):
        starts.append(space_start(f"ready-{allied}", {**ready, "STARBASE_AVAILABLE": 1},
                                  alliances={"CHMMR_SHIP": allied}))
    for sname, ship in CHMMR_SHIPS:
        for starbase in (0, 1):
            start = space_start(f"ready-proceed-{sname}-starbase-{starbase}",
                                {**ready, "STARBASE_AVAILABLE": starbase, "CHMMR_HOME_VISITS": 3, "CHMMR_STACK": 2},
                                prefix=[2], follow=[])
            start["ship"] = talk_ship(**ship)
            starts.append(start)
    starts.append(space_start("ready-proceed-costs", {**ready, "STARBASE_AVAILABLE": 1}, prefix=[2], follow=[],
                              costs={**NEW_GAME_COSTS, "PLANET_LANDER": 0}))
    # After the bomb, by each visit and state, walked by the stack.
    for state in (2, 3):
        for visits in range(8):
            starts.append(space_start(f"after-bomb-{state}-visit-{visits}",
                                      {"CHMMR_BOMB_STATE": state, "CHMMR_HOME_VISITS": visits}, follow=[]))
    for stack in (0, 1, 2):
        starts.append(space_start(f"after-bomb-stack-{stack}", {"CHMMR_BOMB_STATE": 2, "CHMMR_HOME_VISITS": 1,
                                                                "CHMMR_STACK": stack, "CHMMR_UNLEASHED": 1}))
    return starts


COMM_SCRIPTS = {"comandr": commander_starts, "ilwrath": ilwrath_starts, "starbas": starbase_starts,
                "urquan": urquan_starts, "blackur": blackur_starts, "spathi": spathi_starts,
                "slyland": slyland_starts, "spahome": spahome_starts, "slyhome": slyhome_starts,
                "melnorm": melnorm_starts, "shofixt": shofixt_starts, "zoqfot": zoqfot_starts,
                "vux": vux_starts, "yehat": yehat_starts, "rebel": rebel_starts,
                "orz": orz_starts, "pkunk": pkunk_starts,
                "druuge": druuge_starts, "mycon": mycon_starts,
                "utwig": utwig_starts, "supox": supox_starts,
                "thradd": thradd_starts, "umgah": umgah_starts, "talkpet": talkpet_starts,
                "arilou": arilou_starts, "syreen": syreen_starts, "chmmr": chmmr_starts}
# Files and notes a script's recording has besides every script's.
COMM_EXTRA_FILES = {"melnorm": ["src/libs/math/random2.c", "src/libs/math/sqrt.c"]}
COMM_EXTRA_NOTES = {"melnorm": [
    "The Melnorme's starts also give the modules' prices where not a new "
    "game's (costs: ModuleCost by module, the catalog's modules bought by "
    "a price set), and their steps have two more events: the flagship's "
    "slots as the strip changes them (slots: the 11 drive, 8 jet and 16 "
    "module slots, sis.h's numbers) and the prices as the catalog changes "
    "them (costs: PLANET_LANDER to ANTIMISSILE_DEFENSE). Selling bio data "
    "runs as if a button were pressed at once, as selling minerals does: "
    "it's sold in one go. MELNORME_CREDIT0 and 1 are the credits."],
    "druuge": [
    "The Druuge's starts also give what a crew member costs at the "
    "starbase where not a new game's 3 (crew_cost: GLOBAL (CrewCost), a "
    "BYTE), and their steps have one more event: that price as the talk "
    "changes it (crew_cost: crew sold to the Druuge raise it as the talk "
    "says goodbye)."],
    "talkpet": [
    "The Talking Pet's starts also give whether the enemy has ships there "
    "(enemy_ships: how many npc_built_ship_q has, which the script's "
    "GetHeadLink sees as none or some; none where not given) and UQM's "
    "Cyborg option (cyborg: 1 for GLOBAL (glob_flags)'s CYBORG_ENABLED; "
    "off where not given). Its mind-control strobe (MindControlStrobe) "
    "clears the one-shot ambient animation's ANIM_DISABLED each time: no "
    "animation runs here to set it again, so only the first shows as a "
    "change of the ambient flags."]}
# A script's file where it isn't named after its folder.
COMM_FILES = {"ilwrath": "ilwrathc.c", "urquan": "urquanc.c", "blackur": "blackurc.c", "spathi": "spathic.c",
              "zoqfot": "zoqfotc.c", "vux": "vuxc.c", "yehat": "yehatc.c", "orz": "orzc.c", "pkunk": "pkunkc.c",
              "druuge": "druugec.c", "mycon": "myconc.c", "utwig": "utwigc.c", "supox": "supoxc.c",
              "thradd": "thraddc.c", "umgah": "umgahc.c", "arilou": "arilouc.c", "syreen": "syreenc.c",
              "chmmr": "chmmrc.c"}
# The date where a start doesn't give one: a new game's.
TALK_DATE = (2155, 2, 17)
# The scripts' random seed where a start doesn't give one.
TALK_SEED = 12345


def build_comm(uqm, script, init, out):
    path = os.path.join(uqm.uqm, "comm", script, COMM_FILES.get(script, script + ".c"))
    build(uqm, COMM_PROGRAM, COMM_LINK, out, extra=[UIO_FILES],
          libs=["-lm", "-Wl,--wrap=DeltaSISGauges", "-Wl,--wrap=DrawLanders",
                "-Wl,--wrap=TFB_Random", "-Wl,--wrap=ClearSISRect"],
          includes=[os.path.join(HERE, "golden")],
          defines=[f'SCRIPT_FILE="{path}"', f"SCRIPT_INIT={init}", f"SCRIPT_{script}"])


def battle_group_ref(start):
    """BattleGroupRef: the offset of the special group a start names (its
    four 8-bit fields in the start's state), else 0."""
    group = start.get("battle_group")
    if not group:
        return 0
    return sum(start["state"].get(f"{group[:-1]}{i}", 0) << (8 * i) for i in range(4))


def walk_talk(exe, files, music, start, fields, state_bytes, functions, element_worth, strengths):
    """Every path through a talk from one start: runs the script with each
    reply offered in turn, depth first, and stops a path at a talk state
    already walked (the story state, the ship, the phrases used up, the
    screen and the replies offered all the same), which also ends loops
    (asking the commander about the base again and again)."""
    state = bytearray(state_bytes)
    for name, value in start["state"].items():
        f = fields[name]
        bit = f["start_bit"]
        for i in range(f["bits"]):
            if value >> i & 1:
                state[(bit + i) >> 3] |= 1 << ((bit + i) & 7)
    ship = start["ship"]
    alliances = {r: "bad" for r in RACES}
    alliances["HUMAN_SHIP"] = "good"
    alliances.update(start.get("alliances", {}))
    escorts = start.get("escorts", [0, 0])
    # Each race's sphere as StartSphereTracking reads it: its strength and
    # its known strength (a new game's: unknown) where the start doesn't
    # say ("fleets": {race: [actual, known]}).
    fleets = [start.get("fleets", {}).get(r, [strengths[i], 0]) for i, r in enumerate(RACES)]
    head = "\n".join([
        *files, state.hex(),
        " ".join(map(str, [ship["crew"], ship["fuel"], ship["landers"], ship["ru"],
                           *ship["elements"], *(MODULES[m] for m in ship["modules"]),
                           *(MODULES["FUSION_THRUSTER"] if i in ship["drives"] else EMPTY_DRIVE
                             for i in range(11)),
                           *(MODULES["TURNING_JETS"] if i in ship["jets"] else EMPTY_JET
                             for i in range(8))])),
        ship["captain"].encode().hex(), ship["ship"].encode().hex(),
        " ".join(map(str, start.get("date", TALK_DATE))),
        str(start.get("seed", TALK_SEED)),
        " ".join(map(str, element_worth)),
        " ".join(str(ALLIED[alliances[r]]) for r in RACES),
        " ".join(f"{a} {k}" for a, k in fleets),
        f"{escorts[0]} {escorts[1]}",
        " ".join(str(RACES.index(r)) for r in start.get("escort_fleet", [])),
        str(start.get("bio", 0)),
        " ".join(music),
        f"{ACTIVITIES[start.get('activity', 'SUPER_MELEE')]} {battle_group_ref(start)}",
        " ".join(map(str, start.get("log", SOL_LOG))),
        " ".join(str(start.get("costs", NEW_GAME_COSTS).get(m, 0)) for m in PURCHASE_MODULES),
        str(start.get("crew_cost", NEW_GAME_CREW_COST)),
        f"{start.get('enemy_ships', 0)} {start.get('cyborg', 0)}"])
    by_bit = {f["start_bit"]: name for name, f in fields.items()}

    def run(replies):
        out = subprocess.run([exe], input=head + f"\n{len(replies)} {' '.join(map(str, replies))}\n",
                             capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"{start['name']} {replies}: {out.stdout[-500:]}{out.stderr}")
        events = [json.loads(line) for line in out.stdout.splitlines()]
        for e in events:
            if "error" in e:
                raise RuntimeError(f"{start['name']} {replies}: {e['error']}")
            if "set" in e:
                e["set"] = by_bit[e["set"]]
            if "offer" in e:
                e["offer"] = [[ref, functions[int(f, 16)], text] for ref, f, text in e["offer"]]
            # NPCPhrase_cb's callbacks, by name: a line's, and each run.
            for key in ("callback", "run"):
                if key in e:
                    e[key] = functions[int(e[key], 16)]
        return events

    prefix = start.get("prefix", [])
    steps, seen, todo = [], set(), [prefix]
    while todo:
        replies = todo.pop()
        events = run(replies)
        # The events since the last reply (all of them for the first step).
        if replies:
            events = events[max(i for i, e in enumerate(events) if "reply" in e):]
        last = events[-1]
        if "offer" in last:
            key = last.pop("key")
            follow = start.get("follow")
            if key in seen:
                last["seen"] = True
            else:
                seen.add(key)
                todo.extend(replies + [i] for i in reversed(range(len(last["offer"])))
                            if follow is None or last["offer"][i][1] in follow)
        steps.append({"replies": replies, "events": events})
    return steps


def record_talks(uqm, strings_exe, package):
    """Every path through the ported conversation scripts, as UQM's own
    scripts run them (golden/comm.c), from starting states chosen to reach
    every condition each reads: one file per script (talks-NAME.json),
    so no one file grows with the scripts."""
    import hashlib
    import zipfile
    with open(package, "rb") as f:
        if hashlib.sha256(f.read()).hexdigest() != CONTENT_SHA256:
            sys.exit(f"{package} isn't the official UQM 0.8.0 content package")
    with open(os.path.join(REPO, "data", "game-state.json")) as f:
        game_state = json.load(f)
    fields = {s["name"]: s for s in game_state["states"]}
    state_bytes = (game_state["total_bits"] + 7) >> 3
    with open(os.path.join(REPO, "data", "conversations.json")) as f:
        scripts = {s["script"]: s for s in json.load(f)["scripts"]}
    with open(os.path.join(REPO, "data", "new-game.json")) as f:
        worth = json.load(f)["prices"]["element_worth"]
    element_worth = [worth[c] for c in ("COMMON", "CORROSIVE", "BASE_METAL", "NOBLE",
                                        "RARE_EARTH", "PRECIOUS", "RADIOACTIVE", "EXOTIC")]
    with open(os.path.join(REPO, "data", "races.json")) as f:
        if [r["race"] for r in json.load(f)["races"]] != RACES:
            raise RuntimeError("data/races.json's races aren't races.h's")
    strengths = new_game_strengths()
    files_out = {}
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        names = set(z.namelist())
        # The music that loads: in the index, with its file in the package.
        music = sorted(k for k, (kind, path) in index.items()
                       if kind == "MUSICRES" and path in names)
        # GOLDEN_TALKS (comma-separated scripts) records only those, while working on one.
        wanted = os.environ.get("GOLDEN_TALKS")
        for script, starts in COMM_SCRIPTS.items():
            if wanted and script not in wanted.split(","):
                continue
            desc = scripts[script]["description"]
            files = []
            for key in (desc["phrases"], desc["color_map"]):
                files.append(os.path.join(tmp, key))
                with open(files[-1], "wb") as f:
                    f.write(z.read(index[key][1]))
            exe = os.path.join(uqm.build, "comm_" + script)
            build_comm(uqm, script, scripts[script]["init"], exe)
            symbols = subprocess.run(["nm", exe], capture_output=True, text=True,
                                     check=True).stdout.split("\n")
            functions = {int(a, 16): n for a, kind, n in
                         (line.split() for line in symbols if len(line.split()) == 3)
                         if kind in "tT"}
            data = header(uqm, ["tools/extract/golden/comm.c", "src/uqm/commglue.c", "src/uqm/sis.c",
                                "src/libs/math/random.c", "src/libs/strings/getstr.c",
                                f"src/uqm/comm/{script}/{COMM_FILES.get(script, script + '.c')}"]
                          + COMM_EXTRA_FILES.get(script, []), TALK_NOTES + COMM_EXTRA_NOTES.get(script, []))
            data["script"] = script
            data["starts"] = [{**start, "steps": walk_talk(exe, files, music, start, fields, state_bytes,
                                                           functions, element_worth, strengths)}
                              for start in starts()]
            files_out[f"talks-{script}"] = data
    return files_out


TALK_NOTES = [
    "Every path through one ported conversation script (one file per "
    "script: talks-NAME.json, NAME the script's folder in src/uqm/comm), "
    "recorded by running UQM's own script with UQM's commglue.c, sis.c and "
    "string tables, and the rest of the talk engine stubbed "
    "(tools/extract/golden/comm.c says what each event is). Each start "
    "is a story state (fields not given are 0), a flagship (crew, fuel "
    "in 1/100 units, landers, resource units, element amounts, module "
    "slots, the drive and jet slots with a thruster or jets, captain's "
    "and ship's names), the date (a new game's where not given), the "
    "seed of the scripts' random numbers (TFB_SeedRandom; 12345 where "
    "not given), the races' standings where not a new game's "
    "(alliances: good, bad or dead), the races' spheres where not a new "
    "game's (fleets: each race's actual and known strength, which "
    "StartSphereTracking reads and sets; a new game's are its ships' "
    "strengths, unknown), the escort fleet's worth and room for more "
    "ships (escorts; none where not given), its ships' races in "
    "built_ship_q's order (escort_fleet, which CountEscortShips, "
    "RemoveEscortShips and GetHeadLink read; none where not given), the "
    "bio data aboard (bio: TotalBioMass; 0 where not given), where the "
    "talk is (activity: LOBYTE (GLOBAL (CurrentActivity)), SUPER_MELEE "
    "where not given) and the system's own group met (battle_group: the "
    "field of the offset BattleGroupRef holds; 0 where not given; the "
    "Ur-Quan's and the Kohr-Ah's starts have URQUAN_PROBE_GRPOFFS0, "
    "COLONY_GRPOFFS0 and SAMATRA_GRPOFFS0 set to 0x1100, 0x2200 and "
    "0x3300), and its steps: the replies "
    "chosen from the start, each the place in the replies offered, and "
    "the events after the last of them up to the next replies offered "
    "or the talk's end. An offer marked seen reaches a talk state "
    "already walked from this start, so the walk goes no further; a "
    "start with follow walks only the replies calling those functions, "
    "and one with prefix starts its walk after those replies (the rest "
    "is walked from other starts). Surrendering to the Ur-Quan "
    "(setSegue (Segue_defeat)) sets the crew to (COUNT)~0, 65535, as UQM "
    "marks the game lost. Voices are off (usingSpeech false); "
    "selling minerals runs as if a button were pressed at once.",
    "Texts are content (CC BY-NC-SA 2.5), so only the first 16 hex "
    "digits of each line's SHA-256 are here; set names the field "
    "written (data/game-state.json); offer gives each reply's phrase "
    "number, the script's function it calls and its text's hash; a "
    "line's callback (NPCPhrase_cb) and a callback's run name the "
    "script's function.",
]


# ------------------------------------------------------------ outfitting

# The outfitting recording program (golden/outfit.c): UQM's outfit.c,
# shipyard.c and planets/roster.c included, sis.c, menu.c, build.c,
# displist.c and random.c linked, the rest stubbed.
OUTFIT_PROGRAM = os.path.join(HERE, "golden", "outfit.c")
OUTFIT_LINK = ["uqm/sis.c", "uqm/menu.c", "uqm/build.c", "uqm/displist.c", "libs/math/random.c"]
OUTFIT_STUBS = os.path.join(HERE, "golden", "outfit_stubs.c")
# A new game's prices (data/new-game.json), by module number.
MODULE_NAMES = ["PLANET_LANDER", "FUSION_THRUSTER", "TURNING_JETS", "CREW_POD", "STORAGE_BAY", "FUEL_TANK",
                "HIGHEFF_FUELSYS", "DYNAMO_UNIT", "SHIVA_FURNACE", "GUN_WEAPON", "BLASTER_WEAPON",
                "CANNON_WEAPON", "TRACKING_SYSTEM", "ANTIMISSILE_DEFENSE", "BOMB_MODULE_0", "BOMB_MODULE_1",
                "BOMB_MODULE_2", "BOMB_MODULE_3", "BOMB_MODULE_4", "BOMB_MODULE_5"]
# Every module for sale, as after the Melnorme's and the Chmmr's tech.
ALL_FOR_SALE = {"HIGHEFF_FUELSYS": 50, "SHIVA_FURNACE": 80, "BLASTER_WEAPON": 60, "CANNON_WEAPON": 90,
                "TRACKING_SYSTEM": 30, "ANTIMISSILE_DEFENSE": 45}


def outfit_scenarios():
    """Starts and key sequences for outfitting and the shipyard's crew:
    each rule DoInstallModule, ChangeFuelQuantity and DMS_ModifyCrew have,
    on purpose, then long seeded random walks over the menus."""
    import random
    base = talk_ship(ru=20000)
    out = []

    def add(name, steps, ship=None, state=None, costs=None, fuel_cost=20, crew_cost=3, shofixti=False):
        out.append({"name": name, "ship": ship or base, "state": state or {}, "costs": costs or {},
                    "fuel_cost": fuel_cost, "crew_cost": crew_cost, "shofixti": shofixti,
                    "steps": steps.split()})

    def ship(**kw):
        return talk_ship(**{"ru": 20000, **kw})

    # The menu and the fuel.
    add("menu", "outfit d d d d d u u u s d d s")
    add("menu-cancel", "outfit c")
    add("fuel", "outfit s u u u pu pu d pd pd pd s u c c", ship(fuel=1050))
    add("fuel-rounding-down", "outfit s u c c", ship(fuel=1049))
    add("fuel-drain", "outfit s " + "pd " * 3 + "d " * 4 + "pu u s c", ship(fuel=650))
    add("fuel-full", "outfit s u pu pd u c c", ship(fuel=5950))
    add("fuel-poor", "outfit s u u u pu d pu c c", ship(ru=50, fuel=1000))
    add("fuel-dear", "outfit s pu pu c c", ship(ru=1000, fuel=1000), fuel_cost=37)
    add("fuel-other-keys", "outfit s l r s c", ship(fuel=1000))
    add("fuel-two-tanks", "outfit s " + "pu " * 12 + "c c",
        ship(modules=["FUEL_TANK", "FUEL_TANK", "CREW_POD"] + ["EMPTY"] * 5 + ["STORAGE_BAY"] + ["EMPTY"] * 6
             + ["GUN_WEAPON"]))
    # The modules: buying each, choosing among them, the weapons' slots.
    add("modules-cycle", "outfit d s s " + "r " * 16 + "l " * 16 + "c c c", costs=ALL_FOR_SALE)
    add("modules-cycle-front", "outfit d s l s " + "r " * 16 + "c c c", costs=ALL_FOR_SALE)
    add("modules-buy", "outfit d s s s r s r r s r r r s r r r r s c c", costs=ALL_FOR_SALE)
    for i, m in enumerate(MODULE_NAMES[3:14]):
        k = MODULE_NAMES.index(m) - 3
        add(f"buy-{m.lower()}", f"outfit d s l s {'r ' * k}s c c", costs=ALL_FOR_SALE)
        add(f"buy-{m.lower()}-middle", f"outfit d s r r r s {'r ' * k}s c c", costs=ALL_FOR_SALE)
    # The weapons' slots' edges: the modules offered on each slot of an
    # empty ship.
    for slot in range(16):
        add(f"choose-slot-{slot}", f"outfit d s {'r ' * slot}s {'r ' * 14}{'l ' * 14}c c c",
            ship(modules=["EMPTY"] * 16), costs=ALL_FOR_SALE)
    add("buy-too-dear", "outfit d s s s r s c c c", ship(ru=1999))
    add("buy-exactly", "outfit d s s s c c", ship(ru=2000))
    add("sell-gun", "outfit d s l s c c")
    add("sell-and-buy-back", "outfit d s l s s l l s c c", ship(ru=0))
    # Selling what's needed.
    add("sell-crew-pod-needed", "outfit d s r r s c c")
    add("sell-crew-pod-room", "outfit d s r r s c c", ship(crew=50, modules=["CREW_POD", "FUEL_TANK", "CREW_POD"]
                                                           + ["EMPTY"] * 13))
    add("sell-crew-pod-51", "outfit d s r r s c c", ship(crew=51, modules=["CREW_POD", "FUEL_TANK", "CREW_POD"]
                                                         + ["EMPTY"] * 13))
    add("sell-crew-pod-empty", "outfit d s r r s c c", ship(crew=0))
    add("sell-fuel-tank", "outfit d s r s c c", ship(fuel=1000))
    add("sell-fuel-tank-needed", "outfit d s r s c c", ship(fuel=1100))
    add("sell-fuel-tank-second", "outfit d s r s c c", ship(fuel=6000, modules=["FUEL_TANK", "FUEL_TANK"]
                                                            + ["EMPTY"] * 14))
    add("sell-fuel-tank-second-needed", "outfit d s r s c c", ship(fuel=6100, modules=["FUEL_TANK", "FUEL_TANK"]
                                                                   + ["EMPTY"] * 14))
    add("sell-heff-tank", "outfit d s r s c c", ship(fuel=6000, modules=["FUEL_TANK", "HIGHEFF_FUELSYS"]
                                                     + ["EMPTY"] * 14), costs=ALL_FOR_SALE)
    add("sell-heff-tank-needed", "outfit d s r s c c", ship(fuel=6100, modules=["FUEL_TANK", "HIGHEFF_FUELSYS"]
                                                            + ["EMPTY"] * 14), costs=ALL_FOR_SALE)
    elements = [0] * 8
    elements[0] = 500
    add("sell-storage-bay", "outfit d s " + "r " * 8 + "s c c", ship(elements=elements))
    add("sell-storage-bay-empty", "outfit d s " + "r " * 8 + "s c c")
    add("sell-storage-bay-second", "outfit d s " + "r " * 8 + "s c c",
        ship(elements=elements, modules=["EMPTY"] * 8 + ["STORAGE_BAY", "STORAGE_BAY"] + ["EMPTY"] * 6))
    elements = [0] * 8
    elements[7] = 501
    add("sell-storage-bay-second-needed", "outfit d s " + "r " * 8 + "s c c",
        ship(elements=elements, modules=["EMPTY"] * 8 + ["STORAGE_BAY", "STORAGE_BAY"] + ["EMPTY"] * 6))
    # The drives, the jets and the landers.
    add("drives", "outfit d s u u s s r s r s l l l s s c c c")
    add("drives-all", "outfit d s u u " + "s s r " * 11 + "c c", ship(ru=100000))
    add("jets", "outfit d s u s s s r s c c")
    add("jets-wrap", "outfit d s u " + "l " * 9 + "r " * 9 + "c c")
    add("rows", "outfit d s u u u u u d d d d d d c c")
    add("landers", "outfit d s d s s s r s r s c c c")
    add("landers-buy-all", "outfit d s d " + "r s s " * 10 + "c c", ship(ru=100000))
    add("landers-poor", "outfit d s d r s s c c c", ship(ru=499))
    add("landers-none", "outfit d s d s s s c c", ship(landers=0))
    add("landers-ten", "outfit d s d " + "l " * 3 + "s s c c", ship(landers=10))
    # The Chmmr's bomb in: its slots and no landers.
    bomb = ["BOMB_MODULE_0", "BOMB_MODULE_1", "BOMB_MODULE_2", "BOMB_MODULE_3", "BOMB_MODULE_4",
            "BOMB_MODULE_5", "BOMB_MODULE_0", "BOMB_MODULE_1", "BOMB_MODULE_2", "BOMB_MODULE_3",
            "EMPTY", "CREW_POD", "FUEL_TANK", "STORAGE_BAY", "EMPTY", "GUN_WEAPON"]
    add("bomb", "outfit d s l l l l l l l r r r r r r r d d d u u u u s s s c c c", ship(modules=bomb),
        {"CHMMR_BOMB_STATE": 3})
    add("bomb-not-yet", "outfit d s d d u u c c", ship(modules=bomb), {"CHMMR_BOMB_STATE": 2})
    # The shipyard's crew: room, the wallet, the price's threshold, the Shofixti.
    add("crew-hire", "hire " * 6 + "dismiss", ship(crew=45))
    add("crew-poor", "hire hire hire", ship(crew=10, ru=5))
    add("crew-dismiss", "dismiss " * 4 + "hire", ship(crew=2, ru=0))
    add("crew-threshold", "hire hire hire dismiss dismiss dismiss dismiss", ship(crew=10),
        {"CREW_PURCHASED0": 998 & 0xFF, "CREW_PURCHASED1": 998 >> 8})
    add("crew-threshold-shofixti", "hire hire hire dismiss dismiss", ship(crew=10),
        {"CREW_PURCHASED0": 998 & 0xFF, "CREW_PURCHASED1": 998 >> 8}, shofixti=True)
    add("crew-no-pods", "hire dismiss", ship(crew=0, modules=["EMPTY"] * 16))
    add("crew-bought-none", "dismiss dismiss", ship(crew=5))
    add("crew-then-outfit", "hire hire outfit d s r r s c c dismiss", ship(crew=48))

    # Seeded random walks over the menus, on ships of every kind; a walk
    # that leaves outfitting opens it again (the recorder puts "outfit" in).
    keys = ["u", "d", "l", "r", "s", "c", "pu", "pd", "s", "r", "d"]
    ships = [ship(), ship(ru=3000, fuel=1234), ship(ru=500, crew=0, landers=0),
             ship(ru=100000, modules=["CREW_POD"] * 4 + ["STORAGE_BAY"] * 4 + ["FUEL_TANK"] * 4 + ["EMPTY"] * 4),
             ship(ru=100000, elements=[100] * 8, drives=list(range(11)), jets=list(range(8)))]
    for seed in range(40):
        rnd = random.Random(seed)
        steps = ["outfit"] + [rnd.choice(keys) for _ in range(250)]
        if seed % 4 == 3:
            steps[100:100] = ["hire", "hire", "dismiss"]
        add(f"walk-{seed}", " ".join(steps), ships[seed % len(ships)],
            {"CHMMR_BOMB_STATE": 3} if seed % 10 == 9 else {},
            ALL_FOR_SALE if seed % 2 else {}, fuel_cost=20 + seed % 3, crew_cost=3 + seed % 2)
    return out


def race_ships():
    """Each race of avail_race_q (races.h's order): its ship in
    data/ships.json, by InitGameStructures' species (the rebels the
    Yehat's, the drone the probe's)."""
    with open(os.path.join(REPO, "data", "ships.json")) as f:
        ships = json.load(f)
    by_species = {s["species"]: s for s in ships["ships"]}
    species = sorted(((v, k) for k, v in ships["species_ids"].items()))
    return [by_species["YEHAT_ID" if race == "YEHAT_REBEL_SHIP" else "UR_QUAN_PROBE_ID" if race == "URQUAN_DRONE_SHIP"
                       else species[ships["species_ids"]["ARILOU_ID"] + i][1]] for i, race in enumerate(RACES)]


def race_crews():
    """Each race of avail_race_q (races.h's order): its ship's starting
    and most crew, by InitGameStructures' species (the rebels the Yehat's,
    the drone the probe's), from data/ships.json."""
    with open(os.path.join(REPO, "data", "ships.json")) as f:
        ships = json.load(f)
    by_species = {s["species"]: s for s in ships["ships"]}
    species = sorted(((v, k) for k, v in ships["species_ids"].items()))
    out = []
    for i, race in enumerate(RACES):
        sid = ("YEHAT_ID" if race == "YEHAT_REBEL_SHIP" else "UR_QUAN_PROBE_ID" if race == "URQUAN_DRONE_SHIP"
               else species[ships["species_ids"]["ARILOU_ID"] + i][1])
        out.append((by_species[sid]["crew"], by_species[sid]["max_crew"]))
    return out


def shipyard_scenarios():
    """Starts and keys for the host's fleet at the shipyard
    (DoModifyShips: building, crewing and scrapping fleet ships, the
    cursor) and in the roster (DoModifyRoster): each rule on purpose, then
    seeded random walks. A fleet ship is [race, crew, captain, slot]."""
    import random
    out = []

    def add(name, steps, ship=None, allied=("HUMAN_SHIP",), fleet=(), state=None, crew_cost=3, seed=12345,
            reopen="yard"):
        out.append({"name": name, "ship": ship or talk_ship(ru=20000), "state": state or {}, "costs": {},
                    "fuel_cost": 20, "crew_cost": crew_cost, "shofixti": "SHOFIXTI_SHIP" in allied,
                    "seed": seed, "allied": list(allied), "fleet": [list(f) for f in fleet],
                    "reopen": reopen, "steps": steps.split()})

    def ship(**kw):
        return talk_ship(**{"ru": 20000, **kw})

    many = ("ARILOU_SHIP", "HUMAN_SHIP", "SPATHI_SHIP", "SYREEN_SHIP", "SHOFIXTI_SHIP", "ZOQFOTPIK_SHIP", "URQUAN_SHIP")
    # The cursor: round the hangar, the flagship's row, its crew.
    add("cursor", "yard " + "r " * 7 + "d " + "l " * 8 + "d d u u u " + "r " * 3 + "d s u u d s c c")
    add("cursor-flagship-crew", "yard s u u d d d c d d s c", ship(crew=48))
    # Building: into slots by the cursor, each allied race in order, the list wrapping round.
    add("build-slot-0", "yard d s s c", allied=many)
    add("build-slots", "yard d r r r s r s c r r s d d s c u l s l l s c d s s c", allied=many)
    add("build-list-wraps", "yard u s l l r r r r r r r r d d u c s " + "d " * 9 + "s c", allied=many)
    add("build-each", " ".join(["yard"] + [f"{'r ' * (i % 6)}{'u ' if i >= 6 else 'd '}s {'r ' * i}s c c yard"
                                         for i in range(7)]), ship(ru=100000), allied=many)
    add("build-among-ships", "yard d s s c r r s s c l s c r s c",
        fleet=[["SPATHI_SHIP", 30, 7, 1], ["ARILOU_SHIP", 6, 9, 3]], allied=many)
    add("build-too-poor", "yard d s s c s r s c", ship(ru=1099), allied=("HUMAN_SHIP", "SHOFIXTI_SHIP"))
    add("build-exactly", "yard d s s u c", ship(ru=1100))
    add("build-none-allied-but-humans", "yard u s r l u d s c")
    # The hangar full: every slot a ship, select crews it.
    full = [["SPATHI_SHIP" if i % 2 else "ARILOU_SHIP", 3 + i % 4, 5 + i, i] for i in range(12)]
    add("hangar-full", "yard d s u u d c r s d c u s c " + "r " * 5 + "s u c c", fleet=full, allied=many)
    # Crew: up to the most (a Syreen to its starting 12, not its 42), the wallet, down to none and the refund.
    add("hire-to-most", "yard d s s " + "u " * 20 + "c", ship(ru=100000))
    add("hire-syreen", "yard d s " + "r " * 3 + "s " + "u " * 14 + "d c", ship(ru=100000), allied=many)
    add("hire-poor", "yard d s s u u u u u c", ship(ru=1105))
    add("hire-crew-of-max", "yard d s u d c", fleet=[["SYREEN_SHIP", 42, 5, 0]], allied=many)
    add("dismiss-to-none", "yard d s s u u d d d d s", ship(ru=5000))
    add("dismiss-and-cancel", "yard d s s u d d c u s", ship(ru=5000))
    add("buy-back", "yard d s s u u d d d u u u d d d d c", ship(ru=5000))
    add("buy-back-short", "yard d s u u d c", ship(ru=10), fleet=[["HUMAN_SHIP", 0, 6, 0]])
    add("dismiss-fleet-ship", "yard r d s " + "d " * 7 + "u c",
        fleet=[["SPATHI_SHIP", 5, 7, 1]], allied=many)
    add("scrap-on-select", "yard d s d d s d s s c",
        fleet=[["SPATHI_SHIP", 2, 7, 0], ["ARILOU_SHIP", 6, 9, 1]], allied=many)
    add("scrap-then-build", "yard d s d d c s s c", fleet=[["SPATHI_SHIP", 2, 7, 0]], allied=many)
    # The crew price's threshold and the Shofixti, on fleet ships.
    t = {"CREW_PURCHASED0": 998 & 0xFF, "CREW_PURCHASED1": 998 >> 8}
    add("threshold", "yard d s s u u u d d d d d d u u c", ship(ru=10000), state=t)
    add("threshold-buy-back", "yard d s s d u u d d c", ship(ru=10000),
        state={"CREW_PURCHASED0": 1000 & 0xFF, "CREW_PURCHASED1": 1000 >> 8})
    add("threshold-shofixti", "yard d s s u u u d d d d d c", ship(ru=10000), state=t,
        allied=("HUMAN_SHIP", "SHOFIXTI_SHIP"))
    add("crew-bought-none", "yard d s d c", fleet=[["SPATHI_SHIP", 5, 7, 0]], allied=many)
    # The race lit kept past a cancel and a crew change (delta_item's low byte).
    add("item-kept", "yard d s r r c s r r s c s d c r s s c", allied=many,
        fleet=[["SPATHI_SHIP", 5, 7, 0]])
    # The roster: no fleet, one ship, several; the cursor round the points; crew both ways and its limits.
    add("roster-none", "roster", reopen="roster")
    add("roster-one", "roster u d l r s u u d d d c c", ship(crew=40), fleet=[["SPATHI_SHIP", 28, 7, 4]],
        allied=many, reopen="roster")
    crews = race_crews()
    for n in (2, 3, 4, 5, 7, 12):
        fl = [[RACES[(3 * i) % 22], min(1 + i, crews[(3 * i) % 22][0]), 5 + i, i] for i in range(n)]
        add(f"roster-{n}", "roster " + "d " * 13 + "u " * 13 + "r l " * 7 + "d r u l r d r " * 3 + "c",
            fleet=fl, allied=many, reopen="roster")
    add("roster-crew", "roster s u u u d d d d d d s r s u d c c", ship(crew=40),
        fleet=[["HUMAN_SHIP", 16, 7, 0], ["SPATHI_SHIP", 2, 7, 5]], allied=many, reopen="roster")
    add("roster-flagship-empty", "roster s u d u c c", ship(crew=0), fleet=[["HUMAN_SHIP", 10, 7, 0]],
        reopen="roster")
    add("roster-flagship-full", "roster s d u d c c", ship(crew=50), fleet=[["HUMAN_SHIP", 10, 7, 0]],
        reopen="roster")
    add("roster-syreen-over", "roster s d u d d c", ship(crew=20), fleet=[["SYREEN_SHIP", 20, 7, 0]],
        allied=many, reopen="roster")
    add("roster-at-start", "roster s u d u c", ship(crew=20), fleet=[["ARILOU_SHIP", 6, 7, 0]], reopen="roster")
    add("roster-sparse", "roster r d s u c d d r s d c c", ship(crew=30),
        fleet=[["ARILOU_SHIP", 3, 7, 2], ["SPATHI_SHIP", 10, 7, 7], ["HUMAN_SHIP", 5, 8, 11]], allied=many,
        reopen="roster")

    # Seeded random walks: fleets, allies and wallets of every kind; a walk
    # that leaves the screen opens it again (the recorder puts it in).
    keys = ["u", "d", "l", "r", "s", "c", "u", "d", "s"]
    for seed in range(40):
        rnd = random.Random(1000 + seed)
        allied = sorted({"HUMAN_SHIP"} | set(rnd.sample(RACES[:22], rnd.randint(0, 8))), key=RACES.index)
        slots = sorted(rnd.sample(range(12), rnd.randint(0, 12 if seed % 5 == 4 else 6)))
        crews = race_crews()
        fleet = []
        for k, slot in enumerate(slots):
            race = rnd.choice(RACES[:22])
            fleet.append([race, rnd.randint(1, crews[RACES.index(race)][0]), 5 + k, slot])
        roster = seed % 3 == 2
        ru = rnd.choice([0, 500, 1300, 3000, 20000, 100000])
        steps = [("roster" if roster else "yard")] + [rnd.choice(keys) for _ in range(250)]
        state = {}
        if seed % 4 == 1:
            state = {"CREW_PURCHASED0": 995 & 0xFF, "CREW_PURCHASED1": 995 >> 8}
        add(f"walk-{seed}", " ".join(steps), ship(ru=ru, crew=rnd.randint(0, 50)), allied=tuple(allied),
            fleet=fleet, state=state, crew_cost=3 + seed % 2, seed=seed + 1, reopen="roster" if roster else "yard")
    return out


def outfit_head(scenario, fields, state_bytes, prices):
    """golden/outfit.c's and golden/screens.c's first lines: the game
    state, the prices, the ship."""
    state = bytearray(state_bytes)
    for name, value in scenario["state"].items():
        f = fields[name]
        for i in range(f["bits"]):
            if value >> i & 1:
                state[(f["start_bit"] + i) >> 3] |= 1 << ((f["start_bit"] + i) & 7)
    costs = {**prices["module_cost"], **scenario["costs"]}
    ship = scenario["ship"]
    return [
        state.hex(),
        " ".join(str(costs.get(m, 0)) for m in MODULE_NAMES) + f" {scenario['fuel_cost']} {scenario['crew_cost']}",
        " ".join(map(str, [ship["crew"], ship["fuel"], ship["landers"], ship["ru"], *ship["elements"],
                           *(MODULES[m] if m in MODULES else 14 + int(m[-1]) for m in ship["modules"]),
                           *(MODULES["FUSION_THRUSTER"] if i in ship["drives"] else EMPTY_DRIVE
                             for i in range(11)),
                           *(MODULES["TURNING_JETS"] if i in ship["jets"] else EMPTY_JET for i in range(8))])),
    ]


def run_outfit(exe, scenario, fields, state_bytes, prices):
    """Runs one scenario; a key after outfitting was left gets
    "outfit" put before it. Returns the scenario's steps as run and their
    lines."""
    head = "\n".join(outfit_head(scenario, fields, state_bytes, prices) + ["1" if scenario["shofixti"] else "2"])
    steps = list(scenario["steps"])
    while True:
        out = subprocess.run([exe], input=head + "\n" + " ".join(steps) + "\n", capture_output=True, text=True)
        lines = [json.loads(line) for line in out.stdout.splitlines()]
        open_ = False
        for x in lines[:-1]:
            open_ = (open_ or x["step"] == "outfit") and not x["left"]
        if out.returncode == 1 and lines and "error" in lines[-1] and not open_:
            steps.insert(len(lines) - 1, "outfit")
            continue
        if out.returncode or any("error" in x for x in lines):
            raise RuntimeError(f"outfit {scenario['name']}: {out.stdout[-300:]}{out.stderr}")
        return steps, lines


def run_yard(exe, scenario, fields, state_bytes, prices):
    """Runs one shipyard or roster scenario (golden/outfit.c with the
    fleet's lines); a key after its screen was left gets the screen put
    before it again (scenario's reopen). Returns the steps as run and
    their lines."""
    crews = race_crews()
    standing = {r: ALLIED["good"] if r in scenario["allied"] else ALLIED["bad"] for r in RACES}
    head = "\n".join(outfit_head(scenario, fields, state_bytes, prices) + ["1" if scenario["shofixti"] else "2"])
    tail = "\n".join([
        str(scenario["seed"]),
        " ".join(f"{standing[r]} {crews[i][0]} {crews[i][1]}" for i, r in enumerate(RACES)),
        " ".join(f"{RACES.index(r)} {c} {cap} {slot}" for r, c, cap, slot in sorted(scenario["fleet"], key=lambda f: f[3])),
    ])
    steps = list(scenario["steps"])
    while True:
        out = subprocess.run([exe], input=head + "\n" + " ".join(steps) + "\n" + tail + "\n",
                             capture_output=True, text=True)
        lines = [json.loads(line) for line in out.stdout.splitlines()]
        if out.returncode == 1 and lines and "error" in lines[-1] and len(lines) > 1 and lines[-2]["screen"] == 0:
            steps.insert(len(lines) - 1, scenario["reopen"])
            continue
        if out.returncode or any("error" in x for x in lines):
            raise RuntimeError(f"shipyard {scenario['name']}: {out.stdout[-300:]}{out.stderr}")
        return steps, lines


def record_outfit(uqm, exe):
    """Outfitting and the shipyard's crew, as UQM's own outfit.c and
    shipyard.c run them (golden/outfit.c), key by key."""
    with open(os.path.join(REPO, "data", "game-state.json")) as f:
        game_state = json.load(f)
    fields = {s["name"]: s for s in game_state["states"]}
    state_bytes = (game_state["total_bits"] + 7) >> 3
    with open(os.path.join(REPO, "data", "new-game.json")) as f:
        prices = json.load(f)["prices"]
    scenarios = []
    for scenario in outfit_scenarios():
        steps, lines = run_outfit(exe, scenario, fields, state_bytes, prices)
        # The slots only where they change (and on the first step).
        last = {}
        for x in lines:
            for k in ("modules", "drives", "jets"):
                if last.get(k) == x[k]:
                    del x[k]
                else:
                    last[k] = x[k]
            del x["step"]
        scenarios.append({**scenario, "steps": steps, "results": lines})
    shipyard = []
    for scenario in shipyard_scenarios():
        steps, lines = run_yard(exe, scenario, fields, state_bytes, prices)
        # The fleet only where it changes (and on the first step); the slots never change here.
        last = None
        for x in lines:
            for k in ("modules", "drives", "jets", "step"):
                del x[k]
            if x["fleet"] == last:
                del x["fleet"]
            else:
                last = x["fleet"]
        shipyard.append({**scenario, "steps": steps, "results": lines})
    data = header(uqm, ["tools/extract/golden/outfit.c", "tools/extract/golden/outfit_stubs.c", "src/uqm/outfit.c",
                        "src/uqm/shipyard.c", "src/uqm/planets/roster.c", "src/uqm/build.c", "src/uqm/displist.c",
                        "src/uqm/sis.c", "src/uqm/menu.c", "src/libs/math/random.c"], [
        "Outfitting and the shipyard's crew, recorded by running UQM 0.8.0's "
        "own DoOutfit, DoInstallModule and ChangeFuelQuantity (outfit.c) and "
        "DMS_ModifyCrew and CrewTransaction (shipyard.c) with sis.c and "
        "menu.c, the drawing stubbed (tools/extract/golden/outfit.c). Code "
        "only, no content.",
        "Each scenario: a story state (fields not given are 0), the module "
        "costs where not a new game's (before MODULE_COST_SCALE), FuelCost, "
        "CrewCost, whether the Shofixti are allied, a flagship as in "
        "the talks' files (talks-comandr.json and the rest), and its steps: outfit opens outfitting; u, d, l, r, s, "
        "c, pu and pd are keys (up, down, left, right, select, cancel, page "
        "up and down); hire and dismiss the shipyard's up and down on the "
        "flagship's crew. results: after each step, whether it was refused "
        "(MENU_SOUND_FAILURE), whether outfitting was left, the cursor "
        "(CurState and delta_item: UQM's numbers), the ship (crew, fuel in "
        "1/100 units, landers, resource units), its module, drive and jet "
        "slots (sis.h's numbers; each only where it changed, and after the "
        "first step), and the crew price and crew bought.",
        "shipyard: the host's fleet at the shipyard and in the roster "
        "(stage 4b item 7), recorded by running UQM's own DoModifyShips "
        "and its DMS_ functions (shipyard.c), DoModifyRoster (planets/"
        "roster.c), and build.c's CloneShipFragment and NameCaptain on "
        "real queues (displist.c) with TFB_Random (random.c). Each "
        "scenario: as above, and the seed of TFB_Random (the captains' "
        "names drawn), the races allied (GOOD_GUY; the rest BAD_GUY; each "
        "race's ship's starting and most crew from data/ships.json), and "
        "the fleet (built_ship_q: race, crew, captain's name index, hangar "
        "slot). Steps: yard opens DoModifyShips freshly (DoShipyard's "
        "Crew), roster opens the roster (RosterMenu, left at once with no "
        "fleet), and u, d, l, r, s and c are keys to the screen open (a "
        "key after it was left has it opened again, reopen). results: "
        "after each step, as above but the slots, and the fleet (each "
        "ship's race as races.h's number, crew, captain and slot, in the "
        "queue's order; only where it changed, and after the first step), "
        "the screen open (0 none, 2 the shipyard, 3 the roster), and for "
        "the roster its state (CurState), item (modifyingCrew) and point "
        "(the lit ship's curShipPt).",
    ])
    data["scenarios"] = scenarios
    data["shipyard"] = shipyard
    return data


# ------------------------------------------------------------ devices

DEVICES_PROGRAM = os.path.join(HERE, "golden", "devices.c")
DEVICES_LINK = ["uqm/util.c", "libs/strings/unicode.c"]


def devices_scenarios():
    """Story states for the Manifest's Devices list, and keys for its
    cursor: each device alone (the Ultron at each repair), those never
    listed, none, several and all of them, with walks over the pages."""
    with open(os.path.join(REPO, "data", "devices.json")) as f:
        devices = json.load(f)["devices"]
    on = sorted({d["field"] for d in devices if "field" in d and "value" not in d})
    out = [{"name": "none", "state": {}, "keys": ""}]
    for field in on:
        out.append({"name": field.lower(), "state": {field: 1}, "keys": "d u c"})
    for v in range(1, 6):
        out.append({"name": f"ultron-{v}", "state": {"ULTRON_CONDITION": v}, "keys": "d c"})
    out.append({"name": "never-listed", "state": {"DESTRUCT_CODE_ON_SHIP": 1}, "keys": ""})
    # Lists of whole pages: 10 and 15 devices.
    for n in (10, 15):
        out.append({"name": f"pages-{n}", "state": {f: 1 for f in on[:n]},
                    "keys": "pd pd pd d u pu pu d d d d d d c"})
    several = {"ROSY_SPHERE_ON_SHIP": 1, "TALKING_PET_ON_SHIP": 1, "SUN_DEVICE_ON_SHIP": 1, "MOONBASE_ON_SHIP": 1,
               "VUX_BEAST_ON_SHIP": 1, "DESTRUCT_CODE_ON_SHIP": 1, "EGG_CASE1_ON_SHIP": 1, "ULTRON_CONDITION": 3}
    out.append({"name": "several", "state": several, "keys": "d d d d d u pd pu r l pd pd pu pu u u c"})
    every = {f: 1 for f in on}
    every["ULTRON_CONDITION"] = 2
    out.append({"name": "every", "state": every, "keys": "pd " * 6 + "pu " * 6 + "d " * 30 + "u " * 30 + "c"})
    import random
    for seed in range(12):
        rnd = random.Random(seed)
        state = {f: 1 for f in on if rnd.random() < 0.4}
        if rnd.random() < 0.5:
            state["ULTRON_CONDITION"] = rnd.randint(1, 4)
        keys = " ".join(rnd.choice(["u", "d", "l", "r", "pu", "pd"]) for _ in range(60)) + " c"
        out.append({"name": f"walk-{seed}", "state": state, "keys": keys})
    return out


def record_devices(uqm, exe):
    """The Manifest's Devices list as UQM's own planets/devices.c makes
    it (InventoryDevices) and moves its cursor (DoManipulateDevices),
    golden/devices.c. Code only."""
    with open(os.path.join(REPO, "data", "game-state.json")) as f:
        game_state = json.load(f)
    fields = {s["name"]: s for s in game_state["states"]}
    scenarios = []
    for sc in devices_scenarios():
        state = bytearray((game_state["total_bits"] + 7) >> 3)
        for name, value in sc["state"].items():
            f = fields[name]
            for i in range(f["bits"]):
                if value >> i & 1:
                    state[(f["start_bit"] + i) >> 3] |= 1 << ((f["start_bit"] + i) & 7)
        out = subprocess.run([exe], input=state.hex() + "\n" + sc["keys"] + "\n", capture_output=True,
                             text=True)
        if out.returncode:
            raise RuntimeError(f"devices {sc['name']}: {out.stdout[-300:]}{out.stderr}")
        lines = [json.loads(x) for x in out.stdout.splitlines()]
        scenarios.append({**sc, "list": lines[0]["list"], "steps": lines[1:]})
    data = header(uqm, ["tools/extract/golden/devices.c", "src/uqm/planets/devices.c", "src/uqm/util.c",
                        "src/libs/strings/unicode.c"], [
        "The Manifest's Devices list, recorded by running UQM 0.8.0's own "
        "InventoryDevices, DoManipulateDevices and DrawDevices "
        "(planets/devices.c, with util.c's DrawStarConBox) with the drawing "
        "primitives recording each call (tools/extract/golden/devices.c). "
        "Code only.",
        "Each scenario: the story fields set (the rest 0), the keys (u, d, "
        "l, r, pu, pd, c), the list (device numbers, globdata.h's), and the "
        "steps: open (DevicesMenu's first drawing; none when the list is "
        "empty), then each key: the cursor (CurState), the top row shown "
        "(topIndex), whether the list was left, and the calls drawn "
        "meanwhile, in the screen's coordinates (the status area's clip "
        "rectangle's corner added).",
        "A call: [\"s\", resource, frame, x, y] DrawStamp (the frame as "
        "SetAbsFrameIndex is asked for it), [\"r\", x, y, width, height, r, "
        "g, b] DrawFilledRectangle, [\"t\", font, x, y, align, count, text, "
        "r, g, b] font_DrawText (count -1: all), each followed by the "
        "context's clip rectangle (x, y, width, height).",
        "The game's strings are the recording program's, not the content's: "
        "GAME_STRING (N) is \"{gs:N}\" followed, by N % 4, by nothing, "
        "\" b\", \" b c\" or \"\u00e9 b\", so each way a name splits into "
        "its two lines (utf8StringPos, skipUTF8Chars) is recorded.",
    ])
    data["scenarios"] = scenarios
    return data


# ------------------------------------------------------------ the alien's animations

COMMANIM_PROGRAM = os.path.join(HERE, "golden", "commanim.c")
COMMANIM_LINK = ["libs/math/random.c"]


def commanim_steps(seed, script):
    """A talk's timeline for one race: ONE_SECOND/40 frames (COMM_ANIM_RATE)
    and some of other lengths, the alien talking and falling silent, the
    script pausing the talking, and an ambient animation switched off and
    on again, as scripts do."""
    import random
    rnd = random.Random(seed)
    steps = []
    talking = False
    for i in range(1400):
        ticks = 21 if rnd.random() < 0.85 else rnd.randint(1, 90)
        event = ""
        if i % 150 == 40:
            event = "talk"
            talking = True
        elif i % 150 == 120 and talking:
            event = "stop"
            talking = False
        elif i == 700:
            event = "pause"
        elif i == 760:
            event = "unpause"
        steps.append(f"{ticks}:{event}" if event else str(ticks))
    n = len(script["ambient"])
    if n:
        a = rnd.randrange(n)
        flags = script["ambient"][a][2]
        steps.insert(900, f"21:flags={a}={flags | 128}")
        steps.insert(1000, f"21:flags={a}={flags}")
    return steps


def record_commanim(uqm, exe):
    """Every race's alien animations as UQM's own commanim.c runs them
    (golden/commanim.c), on a clock of the program's own. Code and the
    descriptions (data/conversations.json); no content."""
    with open(os.path.join(REPO, "data", "conversations.json")) as f:
        scripts = json.load(f)["scripts"]
    races = []
    for i, sc in enumerate(scripts):
        d = sc["description"]
        seed = 1000 + i
        steps = commanim_steps(seed, d)
        head = "\n".join([str(seed), str(len(d["ambient"]))] + [" ".join(map(str, a)) for a in d["ambient"]]
                         + [" ".join(map(str, d["talk"])), " ".join(map(str, d["transition"]))])
        out = subprocess.run([exe], input=head + "\n" + " ".join(steps) + "\n", capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"commanim {sc['script']}: {out.stderr}")
        lines = [json.loads(x) for x in out.stdout.splitlines()]
        if len(lines) != len(steps):
            raise RuntimeError(f"commanim {sc['script']}: {len(lines)} of {len(steps)} steps")
        # Only the steps where something happened, by their place.
        happened, last = [], None
        for k, x in enumerate(lines):
            flags = (x["talk"], x["transit"])
            if x["draw"] or x["xform"] or flags != last:
                happened.append([k, x["draw"], x["xform"], x["talk"], x["transit"]])
            last = flags
        races.append({"script": sc["script"], "seed": seed, "steps": steps, "happened": happened})
    data = header(uqm, ["tools/extract/golden/commanim.c", "src/uqm/commanim.c", "src/libs/math/random.c"], [
        "Every conversation's alien animations, recorded by running UQM 0.8.0's "
        "own commanim.c (ProcessCommAnimations, with its own TFB_Random seeded) "
        "on a clock of the recording program's own "
        "(tools/extract/golden/commanim.c), from each script's description in "
        "data/conversations.json. Code only, no content.",
        "Each race: its seed and steps (each \"ticks\" or \"ticks:event\": talk, "
        "stop, pause, unpause, flags=I=F), and the steps where something "
        "happened: [step, frames stamped (-1 the main frame, else the frame's "
        "index in the alien's graphics), colour fades started ([map, time]), "
        "the talking and the transition descriptors' flags after it].",
    ])
    data["races"] = races
    return data


# ---- Hyperspace encounters (stage 3b-1 item 3) ----

HYPERSPACE_PROGRAM = os.path.join(HERE, "golden", "hyperspace.c")
HYPERSPACE_STUBS = os.path.join(HERE, "golden", "hyperspace_stubs.c")
# UQM's code the hyperspace program runs: the battle program's (battle.c
# runs hyperspace as a battle) and the new game's, the clock's and the
# star map's (hyper.c and galaxy.c are included).
HYPERSPACE_LINK = list(dict.fromkeys(BATTLE_LINK + [
    "uqm/clock.c", "uqm/gameev.c", "uqm/build.c", "uqm/master.c", "uqm/starmap.c",
    "uqm/plandata.c", "uqm/ships/probe/probe.c"]))
# The sheets hyperspace loads (LoadHyperData, init_sis in hyperspace).
HYPERSPACE_SHEETS = ["graphics.ambient", "graphics.hyperstars", "graphics.quasispace",
                     "ship.flagship.graphics.hyperspace"]

# races.h's numbers of the races the scenarios meet.
R_SPATHI, R_MELNORME, R_ILWRATH, R_SLYLANDRO, R_URQUAN, R_KOHR_AH = 6, 12, 14, 16, 18, 21
# ENCOUNTER.flags.
ONE_SHOT_ENCOUNTER, ENCOUNTER_REFORMING = 1 << 7, 1 << 6


def hyperspace_scenario(name, seed, at, steps, facing=0, fuel=5000, day_ticks=24, tick_count=24,
                        starbase=1, multiplier=0, anger=0, thrusters=2, fleets=(), encounters=(), after=None):
    """A hyperspace scenario for golden/hyperspace.c: the flagship at
    `at` (universe), the clock's day day_ticks frames long, tick_count
    frames from its next, STARBASE_AVAILABLE, SLYLANDRO_MULTIPLIER and
    MELNORME_ANGER; fleets [race, strength, x, y] changed; encounters
    kept from before, each [race, flags, ships, ox, oy, radius, lx, ly,
    transition_state]; steps [frames, keys]. after: a meeting the run
    ends in followed back into hyperspace (stage 3b-1 item 5), a dict of
    status (0 a talk without a battle, 1 a battle), seed (the second
    Battle's), crews (the ships left, each one's crew) and steps."""
    sc = dict(name=name, seed=seed, x=at[0], y=at[1], facing=facing, fuel=fuel, day_ticks=day_ticks,
              tick_count=tick_count, starbase=starbase, multiplier=multiplier, anger=anger,
              thrusters=thrusters, fleets=[list(f) for f in fleets], encounters=[list(e) for e in encounters],
              steps=[list(s) for s in steps])
    if after is not None:
        sc["after"] = dict(status=after["status"], seed=after["seed"], crews=list(after["crews"]),
                           steps=[list(s) for s in after["steps"]])
    return sc


def hyperspace_line(sc, n):
    parts = [f"scenario{n}", sc["seed"], sc["x"], sc["y"], sc["facing"], sc["fuel"], sc["day_ticks"],
             sc["tick_count"], sc["starbase"], sc["multiplier"], sc["anger"], sc["thrusters"],
             len(sc["fleets"])]
    for f in sc["fleets"]:
        parts += f
    parts.append(len(sc["encounters"]))
    for e in sc["encounters"]:
        parts += e
    parts.append(len(sc["steps"]))
    for st in sc["steps"]:
        parts += st
    after = sc.get("after")
    parts.append(1 if after else 0)
    if after:
        parts += [after["status"], after["seed"], len(after["crews"])] + after["crews"]
        parts.append(len(after["steps"]))
        for st in after["steps"]:
            parts += st
    return " ".join(str(p) for p in parts)


# Keys (BATTLE_INPUT_STATE).
H_LEFT, H_RIGHT, H_THRUST = 1, 2, 4


def hyperspace_scenarios():
    """UQM's draws and frames for one flagship in hyperspace, in the spheres
    of 3b-1's races (decision 2: the Ur-Quan, the Kohr-Ah, the Spathi, the
    Ilwrath, the Slylandro)."""
    return [
        # In the Ur-Quan's and the Kohr-Ah's spheres (70% a day each): groups
        # come in through their vortices, fly at the flagship and meet it.
        hyperspace_scenario("waiting in the Ur-Quan's sphere", 11, (5800, 6100),
                            [[40, H_LEFT], [700, 0]], tick_count=5),
        hyperspace_scenario("turning in the Kohr-Ah's sphere", 2024, (6300, 6600),
                            [[30, 0], [80, H_RIGHT | H_THRUST], [60, H_THRUST], [600, H_LEFT]], tick_count=20),
        # A meeting followed back into hyperspace (stage 3b-1 item 5): the
        # group met talked away (re-forming, one-shot: gone as its element
        # is made again), the others' elements made afresh round the
        # flagship spinning back in (hyper_transition) and flying on.
        hyperspace_scenario("waiting in the Ur-Quan's sphere, then talked away", 11, (5800, 6100),
                            [[40, H_LEFT], [700, 0]], tick_count=5,
                            after=dict(status=0, seed=4321, crews=[42] * 5, steps=[[40, 0], [200, H_RIGHT]])),
        # A meeting fought: the player retreated, the group re-forming with
        # the crews the battle left it (its second ship's 17), coming back
        # through its vortex after the transition's 100 steps.
        hyperspace_scenario("turning in the Kohr-Ah's sphere, then fought", 2024, (6300, 6600),
                            [[30, 0], [80, H_RIGHT | H_THRUST], [60, H_THRUST], [600, H_LEFT]], tick_count=20,
                            after=dict(status=1, seed=99, crews=[42, 17, 42, 42],
                                       steps=[[30, 0], [300, H_RIGHT | H_THRUST]])),
        # Out of the Spathi's sphere (radius 990 round (2549, 3600)) at
        # speed: the groups chasing go as they leave it (their vortex out),
        # or fall behind out of the flagship's span.
        hyperspace_scenario("leaving the Spathi's sphere", 77, (2549, 3600 + 900),
                            [[90, 0], [900, H_THRUST]], thrusters=11, tick_count=2),
        hyperspace_scenario("outrunning the Ilwrath", 5, (400, 1700),
                            [[100, 0], [700, H_RIGHT | H_THRUST], [200, H_THRUST]], thrusters=11, tick_count=3,
                            facing=4),
        # The Slylandro probes, every day before the starbase (one at a time),
        # and the Melnorme at 30% with no fuel (one at a time too).
        hyperspace_scenario("the probes", 99, (9000, 300),
                            [[200, 0], [500, H_RIGHT]], starbase=0, multiplier=1, tick_count=1),
        # A probe met and destroyed (stage 3b-1 item 8): fought and won, or
        # sent the destruct code (an instant victory: UQM's Battle fights
        # nothing, and UninitEncounter destroys every ship), the encounter
        # written back with no ships (RaceCommunication's status 1); it goes,
        # and the next day's probe comes, one at a time.
        hyperspace_scenario("the probes, then destroyed", 99, (9000, 300),
                            [[200, 0], [500, H_RIGHT]], starbase=0, multiplier=1, tick_count=1,
                            after=dict(status=1, seed=2418, crews=[], steps=[[120, 0], [200, H_LEFT]])),
        hyperspace_scenario("no fuel", 314, (9000, 300),
                            [[24 * 12, 0]], fuel=0, starbase=1, multiplier=4, day_ticks=12, tick_count=1),
        # Groups kept from before: placed where they were, without a vortex;
        # one out of the flagship's span goes at once; a group re-forming
        # after a battle waits, then comes back in; one talked away goes.
        hyperspace_scenario("groups kept from before", 8, (5800, 6100), [[400, 0]], tick_count=300,
                            encounters=[[R_URQUAN, 0, 3, 5750, 6000, 2662, 5900, 6150, 0],
                                        [R_KOHR_AH, 0, 2, 6000, 6250, 2662, 5800 + 600, 6100, 0],
                                        [R_URQUAN, ENCOUNTER_REFORMING, 4, 5750, 6000, 2662, 5810, 6120, 0],
                                        [R_KOHR_AH, ENCOUNTER_REFORMING | ONE_SHOT_ENCOUNTER, 1, 6000, 6250, 2662,
                                         5790, 6090, 0],
                                        [R_SPATHI, 0, 0, 5750, 6000, 2662, 0, 0, 0]]),
        # A sphere of one's own (a fleet moved, as 3c's events move them):
        # small, so the groups leave it soon after coming in.
        hyperspace_scenario("a small sphere", 4242, (3000, 3000), [[24 * 8, H_RIGHT]],
                            fleets=[[R_KOHR_AH, 20, 3050, 3020]], tick_count=4),
        # Into a star (6166, 6000) in the Ur-Quan's and the Kohr-Ah's
        # spheres: the flagship spins in (init_transition), still in
        # hyperspace (IN_BATTLE) until the spin's last frame: the groups
        # fly at it as it drifts, and a day ending in the spin rolls.
        hyperspace_scenario("into a star", 606, (6126, 6000), [[60, 0], [400, H_THRUST]], facing=4,
                            day_ticks=12, tick_count=1),
    ]


class HyperspaceProgram:
    """The hyperspace program with its sheets and masks."""

    def __init__(self, exe, sheets_file, masks):
        self.exe, self.sheets_file, self.masks = exe, sheets_file, masks
        out = subprocess.run(["nm", "-n", exe], capture_output=True, text=True, check=True).stdout
        self.symbols = [(int(a, 16), name) for a, kind, name in
                        (line.split() for line in out.splitlines() if len(line.split()) == 3)
                        if kind in "tT"]

    def place(self, address):
        import bisect
        i = bisect.bisect_right([a for a, _ in self.symbols], address) - 1
        return self.symbols[i][1]

    def run(self, scenarios):
        lines = [hyperspace_line(s, n) for n, s in enumerate(scenarios)]
        res = subprocess.run([self.exe, self.sheets_file, self.masks], input="\n".join(lines) + "\n",
                             capture_output=True, text=True)
        if res.returncode:
            raise RuntimeError(f"the hyperspace program failed ({res.returncode}): {res.stderr.strip()}")
        runs = json.loads("[" + res.stdout.replace("}\n{", "},\n{") + "]")
        if len(runs) != len(scenarios):
            raise RuntimeError("the hyperspace program lost a scenario")
        places = {}
        for run in runs:
            for frame in run["frames"] + run.get("after", []):
                frame[6] = [places.setdefault(a, self.place(a)) for a in frame[6]]
        return runs


def run_hyperspace(uqm, exe, strings_exe, images_exe, package, scenarios):
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, list(enumerate(HYPERSPACE_SHEETS)))
        sheets_file = os.path.join(tmp, "sheets.txt")
        with open(sheets_file, "w") as f:
            for n, key in enumerate(HYPERSPACE_SHEETS):
                f.write(f"{n} {key} {len(hashes[key])}\n")
        return HyperspaceProgram(exe, sheets_file, masks).run(scenarios)


def check_hyperspace(scenarios, runs):
    """What the recordings must show, or the scenarios don't test it."""
    def frames(name):
        return runs[[s["name"] for s in scenarios].index(name)]["frames"]
    def any_frame(name, test):
        return any(test(f) for f in frames(name))
    met = [r["name"] for r in runs if any(f[4] and any(e[12] and e[12][16] for e in f[8]) for f in r["frames"])]
    if len(met) < 2:
        raise RuntimeError(f"hyperspace: only {met} meet a group")
    # Out of the sphere: an element whose transition_state goes below 0
    # (its vortex out), and one that went without one.
    if not any_frame("leaving the Spathi's sphere", lambda f: any(e[11] < 0 for e in f[8])):
        raise RuntimeError("hyperspace: no group left the Spathi's sphere")
    places = {p for r in runs for f in r["frames"] for p in f[6]}
    for p in ("check_hyperspace_encounter", "AddEncounterElement", "SeedUniverse", "AddAmbientElement"):
        if p not in places:
            raise RuntimeError(f"hyperspace: nothing drawn by {p}")
    if not any_frame("the probes", lambda f: sum(e[0] == R_SLYLANDRO for e in f[8]) == 1):
        raise RuntimeError("hyperspace: no probe came")
    if any_frame("the probes", lambda f: sum(e[0] == R_SLYLANDRO for e in f[8]) > 1):
        raise RuntimeError("hyperspace: two probes at once")
    if not any_frame("no fuel", lambda f: any(e[0] == R_MELNORME for e in f[8])):
        raise RuntimeError("hyperspace: the Melnorme didn't come to a ship with no fuel")
    kept = frames("groups kept from before")
    if len(kept[0][8]) >= 5:
        raise RuntimeError("hyperspace: no kept group went on the first frame")
    if not any(e[11] >= 9 for f in kept for e in f[8]):
        raise RuntimeError("hyperspace: no group re-formed")
    # Into a star: the spin (the flagship's preprocess unhyper_transition,
    # no group met) runs over a day's end, with groups flying at it, and
    # the run ends in the star's system.
    spin = [f for f in frames("into a star") if f[4]]
    if not spin or any(e[12] and e[12][16] for f in spin for e in f[8]):
        raise RuntimeError("hyperspace: the flagship didn't spin into a star")
    if len({tuple(f[5]) for f in spin}) < 2 or not any(e[12] for e in spin[0][8]):
        raise RuntimeError("hyperspace: no day ended in the spin, or no group flew at it")
    if len({(f[0], f[1]) for f in spin}) < 2:
        raise RuntimeError("hyperspace: the flagship didn't drift in its spin")
    if not runs[[s["name"] for s in scenarios].index("into a star")]["end"][1] & 0x800:
        raise RuntimeError("hyperspace: the star's system didn't start (START_INTERPLANETARY)")
    # Meetings followed back into hyperspace (stage 3b-1 item 5): the
    # flagship spinning back in (hyper_transition) before it flies; the
    # group talked away gone, the one fought re-forming (its transition's
    # 100 steps) with the crews the battle left it.
    for name, crews in (("waiting in the Ur-Quan's sphere, then talked away", None),
                        ("turning in the Kohr-Ah's sphere, then fought", [42, 17, 42, 42])):
        run = runs[[s["name"] for s in scenarios].index(name)]
        after = run.get("after")
        if not after or not any(f[4] == 2 for f in after) or after[-1][4] == 2:
            raise RuntimeError(f"hyperspace: '{name}' wasn't followed back, or the flagship didn't spin back in")
        met = [e for e in run["frames"][-1][8] if e[12] and e[12][16]][0]
        if crews is None:
            if len(after[0][8]) != len(run["frames"][-1][8]) - 1:
                raise RuntimeError("hyperspace: the group talked away didn't go")
        elif after[0][8][0][3] != crews or after[0][8][0][11] != 100 or after[0][8][0][0] != met[0]:
            raise RuntimeError("hyperspace: the group fought didn't re-form with its crews")
    # The probe destroyed (stage 3b-1 item 8): gone as hyperspace comes
    # back, and another comes on a later day, one at a time.
    run = runs[[s["name"] for s in scenarios].index("the probes, then destroyed")]
    after = run.get("after")
    if not after or any(e[0] == R_SLYLANDRO for e in after[0][8]):
        raise RuntimeError("hyperspace: the probe destroyed didn't go")
    if not any(sum(e[0] == R_SLYLANDRO for e in f[8]) == 1 for f in after) or \
            any(sum(e[0] == R_SLYLANDRO for e in f[8]) > 1 for f in after):
        raise RuntimeError("hyperspace: no next probe came after the one destroyed, or two at once")


def record_hyperspace(uqm, exe, strings_exe, images_exe, package):
    """UQM's hyperspace encounters, frame by frame (golden/hyperspace.c),
    with the package's masks of hyperspace's sheets."""
    scenarios = hyperspace_scenarios()
    runs = run_hyperspace(uqm, exe, strings_exe, images_exe, package, scenarios)
    check_hyperspace(scenarios, runs)
    data = header(uqm, ["tools/extract/golden/hyperspace.c", "tools/extract/golden/hyperspace_stubs.c",
                        "src/uqm/battle.c", "src/uqm/hyper.c", "src/uqm/galaxy.c", "src/libs/math/random.c"]
                  + [f"src/{f}" for f in HYPERSPACE_LINK], [
        "UQM 0.8.0's own hyperspace encounters, recorded by running battle.c's Battle in hyperspace as "
        "starcon.c does (see tools/extract/golden/hyperspace.c): a new game (UQM's own "
        "InitGameStructures, InitGameClock, AddInitialGameEvents), then TFB_SeedRandom from the seed, "
        "InitShips (InitGalaxy's stars, the flagship, LoadHyperspace), and DoBattle's frames: "
        "ProcessInput, on_battle_frame (GameClockTick, whose day runs HYPERSPACE_ENCOUNTER_EVENT: "
        "check_race_growth and check_hyperspace_encounter; then SeedUniverse: the stars, "
        "ProcessEncounters with AddEncounterElement and ProcessEncounter, the ambient star), "
        "RedrawQueue (process.c's elements, collisions over the masks, encounter_collision). The game "
        "is as if loaded (LastActivity CHECK_LOAD), so the flagship flies from the first frame.",
        "Contact is found with the masks of hyperspace's sheets (sheets) from the official content "
        f"package (SHA-256 {CONTENT_SHA256}). Everything else is code.",
        "input: each scenario's: the seed, the flagship's place (universe), facing, fuel and fusion "
        "thrusters (a new game's ship otherwise), the clock's day in frames (day_ticks) and the frames "
        "to its next (tick_count), STARBASE_AVAILABLE, SLYLANDRO_MULTIPLIER, MELNORME_ANGER, fleets "
        "changed [race, actual_strength, x, y], encounters kept from before [race, flags, ships, "
        "origin x, y, radius, loc_pt x, y, transition_state], and steps [frames, keys] (1 left, 2 "
        "right, 4 thrust).",
        "frames: one a frame, recorded at the end of on_battle_frame (after SeedUniverse, before "
        "RedrawQueue): [log_x, log_y, ShipFacing, the flagship's velocity vector [w, h], whether the "
        "flagship has met a group or touched a star (its preprocess unhyper_transition: it spins in, still in "
        "hyperspace, IN_BATTLE, until the spin's last frame), the date [y, m, d], the "
        "functions that drew a number since the last frame (the first frame's: Battle's start too), "
        "TFB_Random's seed, the encounters]. An encounter: [race, flags, num_ships, [crew_level...], "
        "origin x, y, radius, loc_pt x, y, log_x, log_y, transition_state, element]; element null or "
        "[state_flags, life_span, frame index, turn_wait, thrust_wait, hit_points, death_func set, "
        "TravelAngle, vector w, h, fract w, h, error w, h, incr w, h, hTarget set, current x, y, next "
        "x, y, primitive type].",
        "end: the frames run and CurrentActivity after (a meeting ends the run: START_ENCOUNTER; a star, "
        "START_INTERPLANETARY). "
        "start_seed: TFB_Random's seed as the first frame began (after InitGalaxy's and spawn_ship's "
        "draws, which a port's own seed stands for).",
        "after (stage 3b-1 item 5): a scenario whose run ends in a meeting (START_ENCOUNTER, the flagship "
        "spun into the group it touched) followed back into hyperspace: RaceCommunication's end for the "
        "group met (the head of encounter_q, where cleanup_hyperspace puts it): its ships with the crews "
        "input.after gives, ENCOUNTER_REFORMING, and ONE_SHOT_ENCOUNTER if status is 0 (no battle); then "
        "starcon.c's loop (START_ENCOUNTER cleared, the flagship's velocity zeroed) and Battle in hyperspace "
        "again, not as a loaded game, seeded from input.after.seed: its frames as the first run's, the "
        "transition field 2 while the flagship spins back in (hyper_transition); after_end and "
        "after_start_seed as end and start_seed.",
    ])
    data["sheets"] = HYPERSPACE_SHEETS
    data["scenarios"] = [{"name": s["name"],
                          "input": {k: v for k, v in s.items() if k != "name"},
                          "start_seed": r["start_seed"], "frames": r["frames"], "end": r["end"],
                          **({"after_start_seed": r["after_start_seed"], "after": r["after"],
                              "after_end": r["after_end"]} if "after" in r else {})}
                         for s, r in zip(scenarios, runs)]
    return data


GROUPS_PROGRAM = os.path.join(HERE, "golden", "groups.c")
GROUPS_STUBS = os.path.join(HERE, "golden", "groups_stubs.c")
# The sheets a system's contact tests (CheckIntersect's worlds, process.c's
# flagship and groups: the one-pixel DecFrameIndex (stars_in_space)), in
# the order groups.c numbers them.
GROUPS_SHEETS = ["graphics.flagship", "graphics.planets", "graphics.orbitbackground", "graphics.stars"]


def groups_link(uqm):
    """UQM's code the groups program runs: the hyperspace program's (the
    elements, the ships, the new game, the clock) and the systems
    program's (the generators, the orbits)."""
    return list(dict.fromkeys(HYPERSPACE_LINK + [f for f in systems_link(uqm)]))


# races.h's numbers.
R_ILWRATH_SHIP, R_SLYLANDRO_SHIP = 14, 16
# Stars (star_array's numbers): Sol, the colony, a system 324 units from the
# Ur-Quan's centre (in the Kohr-Ah's sphere too), others 416 and 417 from
# it, and the Spathi's homeworld.
S_SOL, S_COLONY, S_URQUAN, S_URQUAN2, S_URQUAN3, S_SPATHI = 82, 114, 311, 321, 303, 222
# A system no race's sphere holds at the start (9708:250), but the races
# met anywhere (stage 3b-1 item 8).
S_NO_SPHERE = 14
# Goals (groups.c's): a planet, the nearest group, wait, out, away.
G_PLANET, G_GROUP, G_WAIT, G_EXIT, G_AWAY, G_CENTER = 0, 1, 2, 3, 4, 5
# Game states groups.c sets, by its numbers.
ST_PROBE_DELIVERED, ST_MULTIPLIER, ST_MISSION, ST_STARBASE = 0, 1, 2, 3


def groups_scenario(name, visits, thrusters=2, jets=2, date=(2155, 2, 17), states=(), fleets=(), drone_dead=0):
    """A scenario for golden/groups.c: visits [star, seed, days before,
    [[goal, arg, frames]...], and optionally the visit's meetings' ends
    [[status, seed, [crew...]]...] (stage 3b-1 item 5: status 1 a battle,
    each crew -1 as it is, 0 destroyed; a meeting past them a talk without
    a battle)]."""
    return dict(name=name, thrusters=thrusters, jets=jets, date=list(date), states=[list(x) for x in states],
                fleets=[list(f) for f in fleets], drone_dead=drone_dead,
                visits=[[v[0], v[1], v[2], [list(g) for g in v[3]],
                         [[m[0], m[1], list(m[2])] for m in (v[4] if len(v) > 4 else [])]] for v in visits])


def groups_line(sc, n):
    parts = [f"scenario{n}", sc["thrusters"], sc["jets"], *sc["date"], len(sc["states"])]
    for x in sc["states"]:
        parts += x
    parts.append(len(sc["fleets"]))
    for f in sc["fleets"]:
        parts += f
    parts += [sc["drone_dead"], len(sc["visits"])]
    for star, seed, days, goals, meetings in sc["visits"]:
        parts += [star, seed, days, len(goals)]
        for g in goals:
            parts += g
        parts.append(len(meetings))
        for status, mseed, crews in meetings:
            parts += [status, mseed, len(crews)] + crews
    return " ".join(str(p) for p in parts)


def groups_scenarios():
    """UQM's system groups for one flagship: the special systems' own (the
    drone at Sol, the Ur-Quan at the colony) and the random draws in the
    spheres of 3b-1's races, kept, expired and drawn again."""
    return [
        # The drone orbits Earth; flying into Earth's view it detects the
        # flagship from twice as far, and meets it; it then flies off and
        # out of the system (check_probe), and is gone. Sol is then drawn
        # afresh on every entry (the Ilwrath's sphere moved over it, as
        # 3c's events move fleets).
        groups_scenario("the drone at Earth", [
            [S_SOL, 101, 0, [[G_PLANET, 2, 900], [G_GROUP, 0, 900], [G_WAIT, 0, 500], [G_EXIT, 0, 2000]]],
            [S_SOL, 102, 2, [[G_WAIT, 0, 120], [G_EXIT, 0, 2000]]],
            [S_SOL, 103, 1, [[G_WAIT, 0, 60]]],
        ], states=[[ST_STARBASE, 0]], fleets=[[R_ILWRATH_SHIP, 256, 1752, 1450]]),
        # The message delivered and the drone still about: it's an ordinary group.
        groups_scenario("the drone's message delivered", [
            [S_SOL, 7, 0, [[G_WAIT, 0, 200], [G_EXIT, 0, 2000]]],
        ], states=[[ST_PROBE_DELIVERED, 1]]),
        # The drone gone: Sol's random draw.
        groups_scenario("Sol without the drone", [
            [S_SOL, 55, 0, [[G_WAIT, 0, 300], [G_PLANET, 2, 900], [G_WAIT, 0, 300]]],
        ], drone_dead=1, fleets=[[R_ILWRATH_SHIP, 256, 1752, 1450]], states=[[ST_MULTIPLIER, 4]]),
        # The Ur-Quan guarding the colony: in orbit, detects, meets.
        groups_scenario("the colony's guard", [
            [S_COLONY, 31, 0, [[G_PLANET, 0, 1500], [G_GROUP, 0, 900], [G_WAIT, 0, 300], [G_EXIT, 0, 2000]]],
        ]),
        # The Ur-Quan's and the Kohr-Ah's spheres: a random draw, its
        # groups orbiting, exploring and on station, detecting the
        # flagship and turning on it, the one met re-forming and the rest
        # standing down; back within the week (new tasks, no new draw),
        # then after another system and after a week (new draws).
        groups_scenario("the Ur-Quan's sphere", [
            [S_URQUAN, 4242, 0, [[G_WAIT, 0, 240], [G_GROUP, 0, 1500], [G_WAIT, 0, 400], [G_AWAY, 0, 300],
                                 [G_EXIT, 0, 3000]]],
            [S_URQUAN, 4243, 3, [[G_WAIT, 0, 300], [G_EXIT, 0, 3000]]],
            [S_URQUAN, 4244, 8, [[G_WAIT, 0, 100]]],
            [S_URQUAN2, 4245, 1, [[G_WAIT, 0, 100]]],
            [S_URQUAN, 4246, 1, [[G_WAIT, 0, 100]]],
        ]),
        # Into a planet's view: orbiting it (the groups' elements made again
        # as the system comes back), then a group met there and flown away
        # from (the escape counter's frames: the port's keeps the ship from
        # the worlds too, docs/differences.md row 56, so none is touched).
        groups_scenario("into a planet's view", [
            [S_URQUAN3, 77, 0, [[G_PLANET, 0, 1500], [G_CENTER, 0, 400], [G_WAIT, 0, 200], [G_GROUP, 0, 2500],
                                [G_AWAY, 0, 120], [G_EXIT, 0, 3000]]],
        ], thrusters=6, jets=4),
        # A five ships' group met in a planet's view, read for the encounter
        # with their captains' names drawn again, one on a name the group
        # file holds for an earlier ship (drawn once more); out at once after.
        groups_scenario("a meeting in a planet's view", [
            [S_URQUAN, 77, 0, [[G_PLANET, 0, 1500], [G_WAIT, 0, 200], [G_GROUP, 0, 1500], [G_WAIT, 0, 1]]],
        ], thrusters=6, jets=4),
        # A meeting fought (stage 3b-1 item 5): the group's ships as the
        # battle left them (its second ship destroyed, the third with 9
        # crew), the group re-forming with those left, and the race's groups
        # turning on the flagship as the system comes back (EncounterRace,
        # DoMissions), TFB_Random seeded afresh by the battle; a second
        # group met is then destroyed whole, and is gone.
        groups_scenario("a meeting fought", [
            [S_URQUAN, 4242, 0, [[G_WAIT, 0, 240], [G_GROUP, 0, 1500], [G_WAIT, 0, 300], [G_GROUP, 0, 2500],
                                 [G_WAIT, 0, 300]],
             [[1, 515, [-1, 0, 9]], [1, 616, [0, 0, 0, 0, 0, 0, 0]]]],
        ]),
        # The Slylandro probes (stage 3b-1 item 8), in a system no sphere
        # holds (at 9708:250): drawn at 5% times SLYLANDRO_MULTIPLIER, weighed
        # as 4 (one or two groups of one probe); each met and destroyed (a
        # battle won, or the destruct code's instant victory), and the
        # probes turned on the flagship after. With the multiplier 0 (or
        # the ramp-down's end), none.
        groups_scenario("the probes in a system", [
            [S_NO_SPHERE, 1, 0, [[G_WAIT, 0, 240], [G_GROUP, 0, 2500], [G_WAIT, 0, 300], [G_GROUP, 0, 2500],
                                 [G_WAIT, 0, 300]],
             [[1, 2418, [0]], [1, 2419, [0]]]],
        ], states=[[ST_MULTIPLIER, 4], [ST_STARBASE, 1]]),
        groups_scenario("no probes without the multiplier", [
            [S_NO_SPHERE, 1, 0, [[G_WAIT, 0, 120]]],
        ], states=[[ST_MULTIPLIER, 0], [ST_STARBASE, 1]]),
        # The Spathi at home: at least five groups.
        groups_scenario("the Spathi at home", [
            [S_SPATHI, 12, 0, [[G_WAIT, 0, 600], [G_GROUP, 0, 2000], [G_WAIT, 0, 400]]],
        ]),
    ]


class GroupsProgram(HyperspaceProgram):
    """The groups program with its sheets and masks."""

    def run(self, scenarios):
        lines = [groups_line(s, n) for n, s in enumerate(scenarios)]
        res = subprocess.run([self.exe, self.sheets_file, self.masks], input="\n".join(lines) + "\n",
                             capture_output=True, text=True)
        if res.returncode:
            raise RuntimeError(f"the groups program failed ({res.returncode}): {res.stderr.strip()}")
        runs = json.loads("[" + res.stdout.replace("}\n{", "},\n{") + "]")
        if len(runs) != len(scenarios):
            raise RuntimeError("the groups program lost a scenario")
        places = {}
        for run in runs:
            for visit in run["visits"]:
                for frame in visit[1]:
                    frame[9] = [places.setdefault(a, self.place(a)) for a in frame[9]]
        return runs


def run_groups(uqm, exe, strings_exe, images_exe, package, scenarios):
    with tempfile.TemporaryDirectory() as tmp:
        masks, hashes = write_masks(strings_exe, images_exe, package, tmp, list(enumerate(GROUPS_SHEETS)))
        sheets_file = os.path.join(tmp, "sheets.txt")
        with open(sheets_file, "w") as f:
            for n, key in enumerate(GROUPS_SHEETS):
                f.write(f"{n} {key} {len(hashes[key])}\n")
            f.write("4 sun 8\n")   # SunFrame: never tested
        return GroupsProgram(exe, sheets_file, masks).run(scenarios)


def check_groups(scenarios, runs):
    """What the recordings must show, or the scenarios don't test it."""
    def visits(name):
        return runs[[s["name"] for s in scenarios].index(name)]["visits"]
    frames = [f for r in runs for v in r["visits"] for f in v[1]]
    met = [f for f in frames if f[8] == 1]
    if len(met) < 4:
        raise RuntimeError(f"groups: only {len(met)} meetings")
    places = {p for f in frames for p in f[9]}
    for p in ("BuildGroups", "GetGroupInfo", "NotifyOthers", "ip_group_preprocess"):
        if p not in places:
            raise RuntimeError(f"groups: nothing drawn by {p}")
    drone = visits("the drone at Earth")
    if not any(f[8] == 1 and any(g[1] == 23 for g in f[12]) for f in drone[0][1]):
        raise RuntimeError("groups: the drone wasn't met")
    if not any(g[1] == 23 and g[7] == 0 for g in drone[0][1][-1][12]):
        raise RuntimeError("groups: the drone didn't leave the system")
    if "BuildGroups" not in drone[1][1][0][9] or "BuildGroups" not in drone[2][1][0][9]:
        raise RuntimeError("groups: Sol without the drone wasn't drawn afresh on each entry")
    ur = visits("the Ur-Quan's sphere")
    first = [p for p in ur[0][1][0][9]]
    if "BuildGroups" not in first or "BuildGroups" in ur[1][1][0][9]:
        raise RuntimeError("groups: the draw wasn't kept within the week")
    if "BuildGroups" not in ur[2][1][0][9] or "BuildGroups" not in ur[4][1][0][9]:
        raise RuntimeError("groups: the draw wasn't made again after a week or another system")
    tasks = {g[3] & 3 for f in frames for g in f[12]}
    if not {0, 1, 3} <= tasks:
        raise RuntimeError(f"groups: tasks seen {tasks}")
    if not any(g[3] & 4 for f in frames for g in f[12]) or not any(g[4] == 0 and g[3] & 3 != 2 for f in frames for g in f[12]):
        raise RuntimeError("groups: no group stood down or intercepted")
    for r in runs:
        for v in r["visits"]:
            since = None
            for k, f in enumerate(v[1]):
                if f[8] == 1:
                    since = k
                elif f[8] == 3 and since is not None and k - since <= 111:
                    raise RuntimeError("groups: a world touched in the escape counter's frames (the port's differ: row 56)")
    if not any(f[8] == 3 for f in frames):
        raise RuntimeError("groups: no orbit")
    again = visits("a meeting in a planet's view")[0][1]
    # At most five ships (MAX of RACE_ENCOUNTER_MAKEUP): six names or more is one drawn again.
    if not (again[-2][8] == 1 and again[-1][9].count("NameCaptain") > 5):
        raise RuntimeError("groups: no captain's name drawn again on a name the group file holds")
    if len(visits("the Spathi at home")[0][1][0][12]) < 5:
        raise RuntimeError("groups: fewer than five groups at the Spathi's home")
    # A meeting fought (stage 3b-1 item 5): back from the first battle, the
    # numbers seeded afresh and the race's groups intercepting (DoMissions);
    # the second group destroyed whole is gone; a third meeting a talk.
    fought = visits("a meeting fought")[0][1]
    mets = [k for k, f in enumerate(fought) if f[8] == 1]
    if len(mets) < 3:
        raise RuntimeError("groups: fewer than three meetings in 'a meeting fought'")
    back = fought[mets[0] + 1]
    race = [g[1] for g in fought[mets[0]][12] if g[0] == fought[mets[0]][-1]][0]
    if back[10] != 515 or not all(g[4] == 0 and not g[3] & 4 for g in back[12] if g[1] == race):
        raise RuntimeError("groups: no fresh numbers, or the race's groups not intercepting, after a battle")
    gone = fought[mets[1]][-1]
    if any(g[0] == gone for g in fought[mets[1] + 1][12]):
        raise RuntimeError("groups: a group destroyed whole is still in the system")
    # The probes (stage 3b-1 item 8): drawn by the multiplier, each met
    # and destroyed gone, none with the multiplier 0.
    probes = visits("the probes in a system")[0][1]
    if not probes[0][12] or any(g[1] != R_SLYLANDRO_SHIP for g in probes[0][12]):
        raise RuntimeError("groups: no probes drawn, or another race's groups")
    mets = [k for k, f in enumerate(probes) if f[8] == 1]
    if len(mets) != len(probes[0][12]) or probes[-1][12]:
        raise RuntimeError("groups: the probes weren't each met and gone")
    if visits("no probes without the multiplier")[0][1][0][12]:
        raise RuntimeError("groups: probes drawn with the multiplier 0")


def record_groups(uqm, exe, strings_exe, images_exe, package):
    """UQM's system groups, frame by frame (golden/groups.c), with the
    package's masks of a system's sheets."""
    scenarios = groups_scenarios()
    runs = run_groups(uqm, exe, strings_exe, images_exe, package, scenarios)
    check_groups(scenarios, runs)
    data = header(uqm, ["tools/extract/golden/groups.c", "tools/extract/golden/groups_stubs.c",
                        "src/uqm/planets/solarsys.c", "src/uqm/ipdisp.c", "src/uqm/grpinfo.c", "src/uqm/state.c",
                        "src/libs/math/random.c"] + [f"src/{f}" for f in groups_link(uqm)], [
        "UQM 0.8.0's own system groups, recorded by running solarsys.c's InitSolarSys and IP_frame (their "
        "drawing left out) with ipdisp.c, grpinfo.c (the group files in memory, state.c) and process.c's "
        "RedrawQueue (see tools/extract/golden/groups.c), after a new game (UQM's own InitGameStructures, "
        "InitGameClock, AddInitialGameEvents, InitGroupInfo). Each visit seeds TFB_Random, then the "
        "generator's initNpcs (BuildGroups, GetGroupInfo; init_probe at Sol, the colony's guard), "
        "ResetSolarSys (DoMissions), and IP_frame's frames with a test pilot's keys. A meeting "
        "(START_ENCOUNTER) is followed as the game comes back from a talk without a battle "
        "(UninitSolarSys's GetGroupInfo, BuildBattle's ESCAPE_COUNTER, a new SOLARSYS_STATE, InitSolarSys "
        "coming back with reinitNpcs: check_probe at Sol, ResetSolarSys). Leaving runs uninitNpcs; the "
        "next visit's days then pass on the clock's fields (no events).",
        "Contact is found with the masks of a system's sheets (sheets) from the official content package "
        f"(SHA-256 {CONTENT_SHA256}). Everything else is code.",
        "input: each scenario's flagship (fusion thrusters, turning jets), date, game states set "
        "(0 PROBE_MESSAGE_DELIVERED, 1 SLYLANDRO_MULTIPLIER, 2 UTWIG_SUPOX_MISSION, 3 STARBASE_AVAILABLE), "
        "fleets changed [race, actual_strength, x, y], whether the drone's group is made gone, and visits "
        "[star, seed, days before, goals [kind, arg, frames]] (0 a planet, 1 the nearest group drawn, 2 "
        "wait, 3 out of the system, 4 away from the nearest group, 5 the middle of the screen). Entering orbit "
        "comes back to flight at once, as EnterPlanetOrbit does around the orbit. A visit's fifth field, "
        "its meetings' ends in turn (stage 3b-1 item 5): [status, seed, crews]: status 1 a battle, the "
        "group's ships as GetGroupInfo read them given those crews (-1 as it is, 0 destroyed), "
        "UninitEncounter (EncounterRace the first ship's race, the destroyed gone), TFB_Random seeded from "
        "seed (Battle's), then the game coming back as from a talk; a meeting past them a talk.",
        "visits: [star, frames], a frame recorded after RedrawQueue: [keys (1 left, 2 right, 4 thrust), "
        "facing, ip_location x, y, ShipStamp x, y, zoom radius, inner planet or -1, event (0, 1 met, 2 "
        "left the system, 3 orbit), the functions that drew a number since the last frame (the first frame's: "
        "initNpcs' too), TFB_Random's seed, the elements' order (group_id each), the groups "
        "(ip_group_q)], with the group met (EncounterGroup) last on a meeting's frame. A group: "
        "[group_id, race_id, sys_loc, task, dest_loc, orbit_pos, group_counter, in_system, loc x, y, "
        "element]; element null or [state_flags, life_span, thrust_wait, TravelAngle, vector w, h, fract "
        "w, h, error w, h, incr w, h, current x, y, next x, y (display units, the space's offset taken "
        "out), primitive type].",
    ])
    data["sheets"] = GROUPS_SHEETS
    data["scenarios"] = [{"name": s["name"], "input": {k: v for k, v in s.items() if k != "name"},
                          "visits": r["visits"]} for s, r in zip(scenarios, runs)]
    return data


CLOCK_PROGRAM = os.path.join(HERE, "golden", "clock.c")
# UQM's code the clock program runs: the events, the game state and a new
# game's structures (InitGameStructures), the fleets' functions (build.c),
# the ships' list (master.c) and every ship whose RACE_DESC a new game's
# avail_race_q is made from (ARILOU_ID to MMRNMHRM_ID, and the probe).
CLOCK_LINK = ["uqm/gameev.c", "uqm/globdata.c", "uqm/displist.c", "uqm/build.c", "uqm/master.c",
              "libs/math/random.c", "libs/math/sqrt.c"]
CLOCK_LINK += [f"uqm/ships/{ship}/{ship}.c" for ship in sorted(
    ["androsyn", "arilou", "blackurq", "chenjesu", "chmmr", "druuge", "human", "ilwrath", "melnorme",
     "mmrnmhrm", "mycon", "orz", "pkunk", "probe", "shofixti", "slylandr", "spathi", "supox", "syreen",
     "thradd", "umgah", "urquan", "utwig", "vux", "yehat", "zoqfot"])]

# The clock's scenarios: commands for golden/clock.c ("new", "day N",
# "move N", "hyper 0|1", "rampdown", "dest R X Y D F", "growth R G F E",
# "track R", "system S", "add R M D Y E", "strength R N", "ally R F",
# "crew B C", "distress N", "state F N"; races by races.h's *_SHIP number,
# events by gameev.h's, systems by gendef.h's *_DEFINED, F a story field
# of golden/clock.c's MISSION_FIELDS or KOHR_AH_FIELDS by name).
# A new game's Slylandro ramp-ups fall
# on 17 February 2155 (at once), 18 August 2155, 16 February and 16 August
# 2156 (a leap year), and the one on 14 February 2157 (728 days in) finds
# the multiplier at 4 and stops.
CLOCK_SCENARIOS = [
    ("a new game", ["new", "hyper 1", "day 40", "hyper 0", "day 100", "hyper 1", "day 600"]),
    # MoveGameClockDays (LOST_DAYS), the starbase's two weeks: over the
    # Arilou's entrance (17 March), then in hyperspace over the second
    # ramp-up (18 August).
    ("the starbase's two weeks", ["new", "day 20", "move 14", "day 140", "hyper 1", "move 14", "hyper 0",
                                  "day 10"]),
    # Told the self-destruct code (slyhome.c) at a multiplier of 3: the
    # ramp-down runs at once and every 23 days down to 0, and the ramp-up
    # on 16 August 2156 finds the code on the ship and stops.
    ("the Slylandro ramp-down", ["new", "day 400", "rampdown", "day 160"]),
    # The fleets growing, shrinking and moving as 3c's events set them
    # (gameev.c's SetRaceDest and the growth thradd_mission and
    # ilwrath_mission give), check_race_growth running each day:
    # the Thraddash losing half their 150 over 14 days on their way to the
    # Kohr-Ah (thradd_mission's -75 / 14, its fraction 91); the Ur-Quan
    # growing 127 and 200/256 a day to MAX_FLEET_STRENGTH; the Spathi
    # losing 50 a day, extinct on the fourth (and no longer moving); the
    # Mycon with no growth but a fraction, which counts as a loss; the
    # Kohr-Ah heading 255 days west, rounding towards 0, then losing 100
    # and a half a day from an error term of 0; the Ilwrath arriving in 5
    # days and running SLYLANDRO_RAMP_UP (16) as their arrival; the
    # Slylandro (met anywhere), the Chmmr and the Yehat rebels (no sphere)
    # given growth or a heading and unchanged. StartSphereTracking: the
    # Ilwrath's before and after moving (known where they were first), the
    # Arilou's (UQM's 0 for race 0, tracked all the same), the extinct
    # Spathi's (0), the Slylandro's and the Chmmr's (nothing to track), the
    # Thraddash's where they've moved to. The two weeks pass in hyperspace
    # with them on the move.
    ("the fleets", ["new", "track 14", "growth 8 251 91 127", "dest 8 4879 7201 14 255", "growth 18 127 200 127",
                    "growth 6 206 0 127", "dest 6 3000 3000 10 255", "growth 15 0 128 127",
                    "dest 21 1000 1000 255 255", "dest 14 2500 8070 5 16", "growth 16 5 0 127", "growth 1 5 0 127",
                    "dest 22 100 100 3 255", "day 20", "track 14", "track 0", "track 6", "track 16", "track 1",
                    "track 8", "hyper 1", "move 14", "growth 21 156 128 0", "day 10"]),
    # The events with no mission (stage 3c item 1), queued as the talks
    # queue them, with the location gates: the Arilou's look at the Umgah
    # (14) in 10 days; the Shofixti's return (8) in two months, with 300
    # crew bought at 5 each; the Spathi's shield (11), allied, in 40 days;
    # the Zoq-Fot-Pik's distress (6) in 20, waiting twice in their system
    # (6), then their death six months on, waiting once there too; the
    # Spathi's shield waiting three times in theirs (5); the Arilou's portal
    # opening and closing meanwhile; the Yehat rebels (15) at once, and the
    # rebels' sphere (22) tracked.
    ("the events with no mission", ["new", "add 1 0 10 0 14", "crew 300 5", "add 1 2 0 0 8", "ally 6 1",
                                    "add 1 0 40 0 11", "add 1 0 20 0 6", "system 6", "day 30", "system 5",
                                    "day 25", "hyper 1", "day 15", "system 6", "move 150", "hyper 0", "day 10",
                                    "add 1 0 0 0 15", "track 22"]),
    # The Zoq-Fot-Pik rescued before their death (their thanks set
    # ZOQFOT_DISTRESS 0), so it does nothing; the Spathi's shield with no
    # Spathi fleet left, which does nothing; the Shofixti's return at a
    # crew price of 1, which wraps round the BYTE to 255.
    ("the events undone", ["new", "add 1 3 0 0 6", "strength 6 0", "ally 6 1", "add 1 0 5 0 11", "crew 2 1",
                           "add 1 0 7 0 8", "day 10", "move 90", "distress 0", "move 200", "day 2"]),
    # The missions (stage 3c item 3), each started as its talk starts it,
    # day by day over its whole course. The Pkunk's (4): queued six months
    # on by their friendship, their migration to the Yehat (11) begun, then
    # turned home by a good reason (at once) 60 days on, home after as long
    # again, three months' wait, off again, and absorbed by the Yehat on
    # arriving (the Yehat's sphere tracked). PKUNK_SWITCH set before each
    # departure, to see the mission clear it.
    ("the Pkunk's migration", ["new", "add 1 6 0 0 4", "state PKUNK_SWITCH 1", "move 175", "day 15", "day 60",
                               "add 1 0 0 0 4", "day 70", "move 80", "state PKUNK_SWITCH 1", "day 20", "day 175"]),
    # The Ilwrath's war (12, at once from their "gods"), the Thraddash (8)
    # allied and never gone: 90 days to the Thraddash's borders, then both
    # races losing all over 128 days as the Ilwrath close on the midpoint,
    # the Thraddash no longer allies, their visits counted afresh.
    ("the Ilwrath's war", ["new", "ally 8 1", "state THRADD_VISITS 3", "add 1 0 0 0 12", "day 230"]),
    # The Thraddash's mission (5, at once from their talk) begun while the
    # Ilwrath are on their way: recalled home from the Kohr-Ah as the war
    # starts, and on arriving the Ilwrath's mission run at once from
    # theirs, sending them to the midpoint.
    ("the Thraddash recalled to the war", ["new", "add 1 0 0 0 12", "day 80", "add 1 0 0 0 5", "day 180"]),
    # The Thraddash's mission over its whole course (to the Kohr-Ah, half
    # lost, home), then the Ilwrath's war with them at home.
    ("the Thraddash's mission", ["new", "add 1 0 0 0 5", "day 50", "add 1 0 0 0 12", "day 230"]),
    # The Mycon's (13, at once from their talk): to Organon in 30 days (the
    # starbase's two weeks passing over their arrival), half lost there
    # over 14, then on to (6392, 2200) knowing the ambush.
    ("the Mycon's mission", ["new", "add 1 0 0 0 13", "day 20", "move 14", "day 50"]),
    # The Utwig's and the Supox's (9, at once from the Utwig's talk): to
    # the Kohr-Ah in 21 days, their visits, information and news counted
    # afresh; half of each lost over 160 days; home in 21.
    ("the Utwig's and the Supox's mission", ["new", "state UTWIG_VISITS 3", "state UTWIG_INFO 2", "state SUPOX_VISITS 4",
                                             "state SUPOX_INFO 1", "state UTWIG_WAR_NEWS 2", "state SUPOX_WAR_NEWS 1",
                                             "add 1 0 0 0 9", "day 30", "state UTWIG_WAR_NEWS 1", "state SUPOX_WAR_NEWS 1",
                                             "day 190"]),
    # Missions with no fleet: the Utwig gone (only the count goes on), the
    # Pkunk and the Thraddash gone (nothing; the Thraddash's count goes on);
    # and the Mycon with too little to lose a whole ship a day (their
    # growth 0, its fraction counted), so at Organon their event finds no
    # growth and makes them wait there again.
    ("the missions undone", ["new", "strength 9 0", "add 1 0 0 0 9", "strength 4 0", "add 1 0 0 0 4", "strength 8 0",
                             "add 1 0 0 0 5", "strength 15 20", "add 1 0 0 0 13", "day 90"]),
    # The Kohr-Ah (stage 3c item 4). A new game's victory (3) four years
    # on, 17 February 2159 (1,461 days: seven moves of 200 and the 61st day
    # after), and the genocide (10) race by race, each the Kohr-Ah's
    # fleet's arrival, day by day to Sol and KOHR_AH_KILLED_ALL (the game
    # over, CrewEnlisted ~0) on 19 November 2159: the Druuge first though
    # the Mycon are nearer, then the nearest each time; the Arilou killed
    # as any race, the Melnorme and the Slylandro (met anywhere) and the
    # Ur-Quan never; the Yehat rebels (no sphere) killed with the Yehat at
    # their place, the humans at Sol. The Kohr-Ah's and the Ur-Quan's talk
    # counts (set before, and again on the way) reset by each step. The
    # flagship at the Sa-Matra (29) once the frenzy has begun holds
    # nothing.
    ("the Kohr-Ah's victory and genocide", ["new", "state KOHR_AH_VISITS 2", "state KOHR_AH_REASONS 1",
                                            "state KOHR_AH_PLEAD 3", "state KOHR_AH_INFO 2", "state URQUAN_VISITS 5"]
     + ["move 200"] * 7 + ["day 61", "system 29", "day 40", "state KOHR_AH_VISITS 1", "state URQUAN_VISITS 2",
                           "day 240"]),
    # The flagship at the Sa-Matra as the victory falls: it waits, queued
    # again as the genocide 7 days on, twice more; then in hyperspace the
    # genocide begins.
    ("the Sa-Matra's gate", ["new"] + ["move 200"] * 7 + ["day 55", "system 29", "day 20", "hyper 1", "day 20"]),
    # The Utwig's and the Supox's mission (9) begun: the victory a year
    # late, the genocide on 17 February 2160.
    ("the victory a year late", ["new", "add 1 0 0 0 9"] + ["move 200"] * 7 + ["day 61", "move 200", "move 165",
                                                                                "day 30"]),
    # The Mycon (15) sent off to a place by the Kohr-Ah (as a mission
    # leaves a fleet away from home) and the Druuge (13) gone: the victory
    # heads for the Mycon there, a day away (the speed's least), and kills
    # them away from home the same day. The Druuge back (their strength) as
    # the Kohr-Ah head for the Zoq-Fot-Pik (19): arrived at the Zoq-Fot-Pik,
    # the Druuge come first and the scan stops at them, so the Zoq-Fot-Pik
    # (after them in the list) aren't killed; the Kohr-Ah head on for the
    # Druuge and kill them.
    ("the Druuge first, and a fleet away from home", ["new", "dest 15 6100 6300 20 255", "strength 13 0"]
     + ["move 200"] * 7 + ["day 61", "day 3", "strength 13 254", "day 80"]),
]


def record_clock(uqm, exe):
    """The game clock and its events as UQM's own clock.c and gameev.c run
    them (golden/clock.c), day by day. Code only, no content."""
    events = {e["event"]: e["id"] for e in extract_uqm.extract_events(uqm)["events"]}
    scenarios = []
    for name, commands in CLOCK_SCENARIOS:
        out = subprocess.run([exe], input="\n".join(commands) + "\n", capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"clock {name}: exit {out.returncode}: {out.stderr}")
        steps = [json.loads(line) for line in out.stdout.splitlines()]
        if len(steps) != len(clock_step_commands(commands)):
            raise RuntimeError(f"clock {name}: {len(steps)} steps, not {len(clock_step_commands(commands))}")
        scenarios.append({"name": name, "commands": commands, "steps": steps})
    check_clock(scenarios, events)
    data = header(uqm, ["tools/extract/golden/clock.c", "src/uqm/clock.c"] + [f"src/{f}" for f in CLOCK_LINK], [
        "The game clock and its events, recorded by running UQM 0.8.0's own "
        "clock.c (InitGameClock, GameClockTick, MoveGameClockDays, AddEvent, "
        "processClockDayEvents) and gameev.c (AddInitialGameEvents, "
        "EventHandler) in the recording program tools/extract/golden/clock.c. "
        "Code only, no content. A new game is starcon.c's: "
        "InitGameStructures (globdata.c, so avail_race_q is UQM's own, "
        "made from each ship's RACE_DESC by master.c's LoadMasterShipList "
        "and load_ship; the graphics, strings and state files stubbed), "
        "InitGameClock and AddInitialGameEvents. check_race_growth "
        "(gameev.c) runs on the fleets each day; check_hyperspace_encounter "
        "is only counted. SetRaceDest (gameev.c) and StartSphereTracking "
        "(build.c) are UQM's own.",
        "Each scenario: its commands (new: a new game; day N: N days of "
        "GameClockTick; move N: MoveGameClockDays (N); hyper 0|1: the "
        "flagship out of or in hyperspace; rampdown: what slyhome.c does "
        "when told of the self-destruct code; dest R X Y D F: SetRaceDest "
        "(R, X, Y, D, F), race R (races.h's *_SHIP number) heading for (X, "
        "Y) D days away, to run event F there (255 none); growth R G F E: "
        "race R's growth, growth_fract and growth_err_term set, as 3c's "
        "events set them; track R: StartSphereTracking (R); system S: the "
        "flagship in interplanetary flight in the system whose generator is "
        "gendef.h's S (CurStarDescPtr's Index; 0 none), as the location "
        "gates read it; add R M D Y E: AddEvent (R ? RELATIVE_EVENT : "
        "ABSOLUTE_EVENT, M, D, Y, E), as a talk queues an event; strength R "
        "N: race R's actual_strength N; ally R F: SetRaceAllied (R, F); crew "
        "B C: CREW_PURCHASED0 and 1 the crew bought B, CrewCost C; distress "
        "N: ZOQFOT_DISTRESS N; state F N: the story field F N (a mission's "
        "or the Kohr-Ah's), as a talk or --start-state sets it) and its steps: one after new, rampdown, track "
        "and add, one each day of day, one for the whole of move.",
        "A step: [[year, month, day] after it, [[event, depth, year, month, "
        "day]...] each event EventHandler ran, in order, depth 0 for those "
        "clock.c ran (from the queue's head, or AddEvent's due at once) and "
        "more for one run at once from within another, with the date it ran "
        "on, check_hyperspace_encounter's calls, SLYLANDRO_MULTIPLIER after, "
        "[[event, year, month, day]...] the queue after, [[race, "
        "allied_state, actual_strength, known_strength, loc.x, loc.y, "
        "known_loc.x, known_loc.y, growth, growth_fract, growth_err_term, "
        "days_left, func_index, dest_loc.x, dest_loc.y]...] the fleets "
        "(FLEET_INFO in avail_race_q) that changed since the step before "
        "(every race's after new), [ARILOU_SPACE, ARILOU_CHECKED_UMGAH, "
        "ZOQFOT_DISTRESS, SPATHI_SHIELDED_SELVES, CREW_PURCHASED0, "
        "CREW_PURCHASED1, CrewCost, PKUNK_MISSION, PKUNK_ON_THE_MOVE, "
        "PKUNK_SWITCH, YEHAT_ABSORBED_PKUNK, THRADD_MISSION, "
        "ILWRATH_FIGHT_THRADDASH, HELIX_UNPROTECTED, THRADD_VISITS, "
        "MYCON_KNOW_AMBUSH, UTWIG_SUPOX_MISSION, UTWIG_VISITS, UTWIG_INFO, "
        "SUPOX_VISITS, SUPOX_INFO, UTWIG_WAR_NEWS, SUPOX_WAR_NEWS, "
        "KOHR_AH_FRENZY, KOHR_AH_VISITS, KOHR_AH_REASONS, KOHR_AH_PLEAD, "
        "KOHR_AH_INFO, URQUAN_VISITS, KOHR_AH_KILLED_ALL, and 1 if "
        "CrewEnlisted is ~0, the game over, else 0] after it], "
        "and after track one more: "
        "StartSphereTracking's result. Events are gameev.h's numbers "
        "(data/events.json), races races.h's (data/races.json's order). An "
        "event a fleet's arrival runs (gameev.c's own EventHandler call) "
        "isn't listed, only what it does.",
    ])
    data["scenarios"] = scenarios
    return data


def clock_step_commands(commands):
    """The command each of a scenario's steps comes from, one per step (a
    day's for each day)."""
    words = []
    for c in commands:
        word, *n = c.split()
        if word == "day":
            words += ["day"] * int(n[0])
        elif word in ("new", "move", "rampdown", "track", "add"):
            words.append(word)
    return words


def check_clock(scenarios, events):
    """Refuses a recording whose scenarios don't show what they're for."""
    ENCOUNTER, ARILOU, RAMP_UP, RAMP_DOWN = 2, 0, 16, 17
    if (events["HYPERSPACE_ENCOUNTER_EVENT"], events["ARILOU_ENTRANCE_EVENT"], events["SLYLANDRO_RAMP_UP"],
            events["SLYLANDRO_RAMP_DOWN"]) != (ENCOUNTER, ARILOU, RAMP_UP, RAMP_DOWN):
        raise RuntimeError("clock: the events' numbers aren't gameev.h's as this check knows them")
    by = {s["name"]: s for s in scenarios}

    def ran(step, event):
        return any(r[0] == event for r in step[1])

    new = by["a new game"]["steps"]
    if [s[3] for s in new if ran(s, RAMP_UP)] != [1, 2, 3, 4, 4] or any(e[0] == RAMP_UP for e in new[-1][4]):
        raise RuntimeError("clock: a new game's ramp-ups don't reach 4 and stop")
    if not all(ran(s, ENCOUNTER) for s in new[1:]) or sum(s[2] for s in new) != 640:
        raise RuntimeError("clock: the encounter event didn't run once a day, rolling in hyperspace")
    weeks = by["the starbase's two weeks"]
    moves = [s for s, c in zip(weeks["steps"], clock_step_commands(weeks["commands"])) if c == "move"]
    if len(moves) != 2 or not ran(moves[0], ARILOU) or not ran(moves[1], RAMP_UP) or moves[1][2] != 14:
        raise RuntimeError("clock: the two weeks don't pass over the Arilou and a ramp-up")
    down = by["the Slylandro ramp-down"]["steps"]
    if sum(ran(s, RAMP_DOWN) for s in down) != 4 or down[-1][3] != 0:
        raise RuntimeError("clock: the ramp-down doesn't run down to 0")
    # The fleets: a new game's every race's, each with no growth; nothing
    # changes them in the first three scenarios after it.
    CHMMR, SPATHI, THRADDASH, ILWRATH, SLYLANDRO, URQUAN, KOHR_AH, REBELS = 1, 6, 8, 14, 16, 18, 21, 22
    for scenario in scenarios:
        steps = scenario["steps"]
        if [f[0] for f in steps[0][5]] != list(range(24)) or any(f[8:12] != [0, 0, 127, 0] for f in steps[0][5]):
            raise RuntimeError(f"clock {scenario['name']}: a new game's fleets aren't every race's, with no growth")
        if (scenario["name"] in ("a new game", "the starbase's two weeks", "the Slylandro ramp-down")
                and any(s[5] for s in steps[1:])):
            raise RuntimeError(f"clock {scenario['name']}: a fleet changed without growth or a heading")
    fleets = by["the fleets"]["steps"]

    def last(race):
        return [f for s in fleets for f in s[5] if f[0] == race][-1]

    # [race, allied_state, actual_strength, known_strength, loc.x, loc.y, ...,
    # growth 8, growth_fract 9, growth_err_term 10, days_left 11, ...]
    if (last(SPATHI)[1:3] != [0, 0] or last(SPATHI)[11] == 0 or last(URQUAN)[2] != 2794
            or last(ILWRATH)[4:6] != [2500, 8070] or last(ILWRATH)[11] != 0 or last(THRADDASH)[4:6] != [4879, 7201]
            or not 0 < last(KOHR_AH)[4] < 6000 or last(SLYLANDRO)[2:6] != [65535, 0, 333, 9812]
            or last(CHMMR)[2] != 0 or last(REBELS)[2:6] != [0, 0, 4970, 40] or last(REBELS)[11] != 3):
        raise RuntimeError("clock: the fleets don't grow, shrink, die and move as the scenario has them")
    tracked = [s[7] for s in fleets if len(s) > 7]
    if tracked != [ILWRATH, ILWRATH, 0, 0, SLYLANDRO, 1, THRADDASH]:
        raise RuntimeError(f"clock: StartSphereTracking gave {tracked}")
    if not any(s[3] == 2 for s in fleets[:8]):
        raise RuntimeError("clock: the Ilwrath's arrival didn't run its event")
    # The events with no mission: [ARILOU_SPACE, ARILOU_CHECKED_UMGAH,
    # ZOQFOT_DISTRESS, SPATHI_SHIELDED_SELVES, CREW_PURCHASED0, 1, CrewCost].
    DISTRESS, DEATH, SHOFIXTI_RETURN, SHIELD, UMGAH_CHECK, REBELS_EVENT = 6, 7, 8, 11, 14, 15
    SHOFIXTI, YEHAT, ZOQFOT = 5, 11, 19
    ev = by["the events with no mission"]["steps"]

    def runs(steps, event):
        return sum(r[0] == event for s in steps for r in s[1])

    def fleet_of(steps, race):
        return [f for s in steps for f in s[5] if f[0] == race][-1]

    if (runs(ev, DISTRESS) != 3 or runs(ev, SHIELD) != 4 or runs(ev, DEATH) != 2 or runs(ev, SHOFIXTI_RETURN) != 1
            or runs(ev, UMGAH_CHECK) != 1 or runs(ev, REBELS_EVENT) != 1 or runs(ev, ARILOU) < 6
            or ev[-1][6][1:7] != [2, 2, 1, 0, 0, 3] or fleet_of(ev, ZOQFOT)[1:3] != [0, 0]
            or fleet_of(ev, SPATHI)[1:3] != [2, 0] or fleet_of(ev, SHOFIXTI)[1] != 1
            or fleet_of(ev, REBELS)[2] == 0 or fleet_of(ev, REBELS)[2] != fleet_of(ev, YEHAT)[2]
            or fleet_of(ev, REBELS)[4:6] != [5150, 0] or ev[-1][7] != REBELS):
        raise RuntimeError("clock: the events with no mission don't run, wait and change the story as the scenario has them")
    undone = by["the events undone"]["steps"]
    if (runs(undone, DEATH) != 1 or undone[-1][6][2:7] != [0, 0, 0, 0, 255] or fleet_of(undone, ZOQFOT)[1] == 0
            or fleet_of(undone, SPATHI)[1] != 1):
        raise RuntimeError("clock: the rescue, the Spathi gone and the crew price's wrap aren't as the scenario has them")
    # The missions: the story after each scenario, [..., CrewCost (6),
    # PKUNK_MISSION 7, PKUNK_ON_THE_MOVE 8, PKUNK_SWITCH 9,
    # YEHAT_ABSORBED_PKUNK 10, THRADD_MISSION 11, ILWRATH_FIGHT_THRADDASH
    # 12, HELIX_UNPROTECTED 13, THRADD_VISITS 14, MYCON_KNOW_AMBUSH 15,
    # UTWIG_SUPOX_MISSION 16, UTWIG_VISITS 17, UTWIG_INFO 18, SUPOX_VISITS
    # 19, SUPOX_INFO 20, UTWIG_WAR_NEWS 21, SUPOX_WAR_NEWS 22].
    PKUNK, SUPOX, UTWIG, MYCON = 4, 7, 9, 15
    PKUNK_EVENT, THRADD_EVENT, UTWIG_EVENT, ILWRATH_EVENT, MYCON_EVENT = 4, 5, 9, 12, 13

    def story(name):
        return by[name]["steps"][-1][6]

    pk = by["the Pkunk's migration"]["steps"]
    switch_after = [s[6][9] for s, c in zip(pk, clock_step_commands(by["the Pkunk's migration"]["commands"]))
                    if c == "day" and any(r[0] == PKUNK_EVENT for r in s[1])]
    if (runs(pk, PKUNK_EVENT) != 3 or story("the Pkunk's migration")[7:11] != [4, 1, 0, 1] or switch_after != [0, 0]
            or fleet_of(pk, PKUNK)[1:3] != [0, 0] or fleet_of(pk, PKUNK)[4:6] != [4970, 400]
            or fleet_of(pk, YEHAT)[3] != 1):
        raise RuntimeError("clock: the Pkunk don't migrate, turn home, wait and get absorbed as the scenario has them")
    for name, thradd_state in (("the Ilwrath's war", 0), ("the Thraddash recalled to the war", 4),
                               ("the Thraddash's mission", 4)):
        steps = by[name]["steps"]
        if (story(name)[11:15] != [thradd_state, 1, 1, 0] or fleet_of(steps, ILWRATH)[1:3] != [0, 0]
                or fleet_of(steps, THRADDASH)[1:3] != [0, 0] or fleet_of(steps, ILWRATH)[4:6] != [2517, 8214]):
            raise RuntimeError(f"clock {name}: the Ilwrath's war doesn't end with both races dead")
    if not any(r[0] == ILWRATH_EVENT and r[1] == 1 for s in by["the Thraddash recalled to the war"]["steps"] for r in s[1]):
        raise RuntimeError("clock: the recalled Thraddash's arrival doesn't run the Ilwrath's mission at once")
    if runs(by["the Thraddash's mission"]["steps"], THRADD_EVENT) != 1:
        raise RuntimeError("clock: the Thraddash's mission isn't started once by its talk")
    my = by["the Mycon's mission"]["steps"]
    if (runs(my, MYCON_EVENT) != 2 or story("the Mycon's mission")[15] != 1 or fleet_of(my, MYCON)[4:6] == [6858, 577]
            or fleet_of(my, MYCON)[8:10] != [0, 0] or fleet_of(my, MYCON)[12] != 255):
        raise RuntimeError("clock: the Mycon don't go to Organon, lose half and head on as the scenario has them")
    us = by["the Utwig's and the Supox's mission"]["steps"]
    if (runs(us, UTWIG_EVENT) != 3 or story("the Utwig's and the Supox's mission")[16:23] != [6, 0, 0, 0, 0, 0, 0]
            or fleet_of(us, UTWIG)[4:6] != [8534, 8797] or fleet_of(us, SUPOX)[4:6] != [7468, 9246]
            or fleet_of(us, UTWIG)[2] != 60 or fleet_of(us, SUPOX)[2] != 30):
        raise RuntimeError("clock: the Utwig and the Supox don't fight the Kohr-Ah and come home as the scenario has them")
    if story("the missions undone")[11] != 1 or story("the missions undone")[16] != 1 or story("the missions undone")[7] != 0:
        raise RuntimeError("clock: the missions with no fleet don't count as the scenario has them")
    # The Kohr-Ah: the story's [..., KOHR_AH_FRENZY 23, KOHR_AH_VISITS 24,
    # KOHR_AH_REASONS 25, KOHR_AH_PLEAD 26, KOHR_AH_INFO 27, URQUAN_VISITS
    # 28, KOHR_AH_KILLED_ALL 29, CrewEnlisted ~0 30].
    VICTORY, GENOCIDE = 3, 10
    HUMAN, DRUUGE, MELNORME, ZOQFOTPIK, ARILOU = 2, 13, 12, 19, 0
    if (events["KOHR_AH_VICTORIOUS_EVENT"], events["KOHR_AH_GENOCIDE_EVENT"]) != (VICTORY, GENOCIDE):
        raise RuntimeError("clock: the Kohr-Ah's events' numbers aren't gameev.h's as this check knows them")
    kv = by["the Kohr-Ah's victory and genocide"]["steps"]
    killed = [f[0] for s in kv for f in s[5] if f[2] == 0 and f[0] not in (KOHR_AH, REBELS)
              and any(g[0] == f[0] and g[2] != 0 for g in kv[0][5])]
    over = [i for i, s in enumerate(kv) if s[6][29]]
    at_sol = [i for i, s in enumerate(kv) if any(f[0] == KOHR_AH and f[4:6] == [1752, 1450] for f in s[5])]
    if (runs(kv, VICTORY) != 1 or [r[2:] for s in kv for r in s[1] if r[0] == VICTORY] != [[2159, 2, 17]]
            or killed[0] != DRUUGE or MYCON not in killed[1:] or ARILOU not in killed or URQUAN in killed
            or MELNORME in killed or SLYLANDRO in killed or len(killed) != len(set(killed))
            or fleet_of(kv, HUMAN)[1] != 0 or fleet_of(kv, REBELS)[1] != 0 or not over or over[:1] != at_sol[:1]
            or kv[over[0]][0] != [2159, 11, 19] or kv[over[0]][6][29:31] != [1, 1] or kv[over[0] - 1][6][29] != 0
            or kv[-1][6][23:29] != [1, 0, 0, 0, 0, 0] or kv[-1][6][29:31] != [1, 1]):
        raise RuntimeError("clock: the Kohr-Ah's victory and genocide don't run to Sol as the scenario has them")
    sg = by["the Sa-Matra's gate"]["steps"]
    genocides = [(r[2:], s[6][23]) for s in sg for r in s[1] if r[0] == GENOCIDE]
    if (runs(sg, VICTORY) != 1 or genocides != [([2159, 2, 24], 0), ([2159, 3, 3], 0), ([2159, 3, 10], 1)]):
        raise RuntimeError(f"clock: the Sa-Matra's gate holds the victory and genocide as {genocides}")
    late = by["the victory a year late"]["steps"]
    if ([(r[0], r[2:]) for s in late for r in s[1] if r[0] in (VICTORY, GENOCIDE)]
            != [(VICTORY, [2159, 2, 17]), (GENOCIDE, [2160, 2, 17])]
            or story("the victory a year late")[16] == 0 or story("the victory a year late")[23] != 1
            or fleet_of(late, DRUUGE)[2] != 0):
        raise RuntimeError("clock: the victory isn't a year late with the Utwig's and Supox's mission begun")
    df = by["the Druuge first, and a fleet away from home"]["steps"]
    if (fleet_of(df, MYCON)[1:3] != [0, 0] or fleet_of(df, MYCON)[4:6] != [6100, 6300]
            or fleet_of(df, DRUUGE)[1:3] != [0, 0] or fleet_of(df, ZOQFOTPIK)[2] == 0
            or not any(f[0] == KOHR_AH and f[4:6] == [3761, 5333] for s in df for f in s[5])):
        raise RuntimeError("clock: the Druuge don't come first, or the Mycon aren't killed away from home")


CMAP_PROGRAM = os.path.join(HERE, "golden", "cmap.c")

# Colour fade scenarios: commands for golden/cmap.c ("set F L SEED",
# "xform F L SEED TICKS", "step NOW").
CMAP_SCENARIOS = [
    ("one fade", ["set 0 0 1", "xform 0 0 2 84", "step 0", "step 21", "step 42", "step 63", "step 84", "step 105"]),
    ("steps off the frame", ["set 0 0 3", "xform 0 0 4 100", "step 7", "step 33", "step 50", "step 99", "step 100", "step 101"]),
    ("no time", ["set 3 3 5", "xform 3 3 6 0", "step 0", "step 1"]),
    ("less than no time", ["set 3 3 5", "xform 3 3 6 -5", "step 0", "step 1"]),
    ("a fade again on a map fading", ["set 2 2 7", "xform 2 2 8 200", "step 50", "xform 2 2 9 100", "step 75",
                                       "step 120", "step 150", "step 160"]),
    ("fades on several maps", ["set 0 4 10", "xform 1 1 11 60", "step 10", "xform 3 3 12 30", "xform 0 0 13 90",
                               "step 20", "step 40", "step 70", "step 100", "step 120"]),
    ("an entry of two maps fades the first and sets both", ["set 5 6 14", "xform 5 6 15 40", "step 10", "step 30",
                                                            "step 40", "step 41"]),
    ("a slot freed and taken again", ["set 0 2 16", "xform 0 0 17 10", "xform 1 1 18 100", "step 10", "xform 2 2 19 50",
                                      "step 30", "step 60", "step 100", "step 110"]),
    ("more fades than slots", ["set 0 19 20"] + [f"xform {m} {m} {21 + m} {200 + m}" for m in range(18)]
     + ["step 50", "step 150", "step 210", "step 220"]),
    ("the clock past the end at once", ["set 0 0 30", "xform 0 0 31 50", "step 500", "step 501"]),
]


def record_cmap(uqm, exe):
    """Colour map fades as UQM's own cmap.c runs them (golden/cmap.c), on a
    clock of the program's own. Code only, no content."""
    scenarios = []
    for name, commands in CMAP_SCENARIOS:
        out = subprocess.run([exe], input="\n".join(commands) + "\n", capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"cmap {name}: exit {out.returncode}")
        steps = []
        for line in out.stdout.splitlines():
            parts = line.split()
            steps.append([int(parts[0]), [[int(m), h] for m, h in (p.split(":") for p in parts[1:])]])
        if len(steps) != sum(c.startswith("step") for c in commands):
            raise RuntimeError(f"cmap {name}: {len(steps)} steps")
        scenarios.append({"name": name, "commands": commands, "steps": steps})
    data = header(uqm, ["tools/extract/golden/cmap.c", "src/libs/graphics/cmap.c"], [
        "Colour map fades, recorded by running UQM 0.8.0's own cmap.c "
        "(SetColorMap, XFormColorMap, XFormColorMap_step) on a clock of the "
        "recording program's own (tools/extract/golden/cmap.c). Code only, no "
        "content: the colour entries are made from a seed, as the program says.",
        "Each scenario: its commands (set F L SEED, xform F L SEED TICKS, step "
        "NOW) and, for each step, [changed, [[map, FNV-1a hash of its 768 "
        "colour bytes]...]] for every map set.",
    ])
    data["scenarios"] = scenarios
    return data


SCREENS_PROGRAM = os.path.join(HERE, "golden", "screens.c")
SCREENS_LINK = ["uqm/sis.c", "uqm/menu.c", "uqm/border.c", "uqm/util.c", "libs/graphics/pixmap.c",
                "uqm/build.c", "uqm/displist.c", "libs/math/random.c"]
SCREENS_STUBS = os.path.join(HERE, "golden", "screens_stubs.c")
SCREENS_FRAMES = ["graphics.starbase", "graphics.modulesmenu", "graphics.outfit", "graphics.playmenu",
                  "graphics.shipyard", "graphics.outfitmodules", "graphics.fontgradient",
                  "graphics.flagshipstatus", "graphics.blueprints", "graphics.status", "graphics.pickship"]
# The fonts' leading, as UQM's loader gives it (UqmFont, checked against
# it by fonts.json): StarCon, Tiny, Micro.
SCREENS_LEADING = "11 10 15"


def screens_scenarios():
    """The starbase's menu on each entry, moving round with the clock
    running, and outfitting: the outfit recording's scenarios that keep
    to the outfitting screen (not its long random walks nor the
    shipyard's crew), a few with other names."""
    out = []
    for state in range(4):
        steps = [f"base:{state}"]
        t = 0
        for k, key in enumerate("d d d d u u l r r r".split()):
            for _ in range(3):
                t += 13 + 7 * (k % 3)
                steps.append(f"t:{t}")
            steps.append(key)
        out.append({"name": f"base-{state}", "ship": talk_ship(), "state": {}, "costs": {}, "fuel_cost": 20,
                    "crew_cost": 3, "steps": steps})
    # Round the starbase's frames and past frame 0, some passes too soon to turn it, a key between.
    steps = ["base:1"]
    for k in range(1, 75):
        steps.append(f"t:{21 * k}")
        if k % 9 == 0:
            steps.append("r")
    out.append({"name": "base-turning", "ship": talk_ship(), "state": {}, "costs": {}, "fuel_cost": 20,
                "crew_cost": 3, "steps": steps})
    for sc in outfit_scenarios():
        if "walk" in sc["name"] or any(s in ("hire", "dismiss") for s in sc["steps"]):
            continue
        # Every slot's edges are outfit.json's; the drawing needs a few.
        if sc["name"].endswith("-middle") or (sc["name"].startswith("choose-slot-")
                                               and sc["name"] not in ("choose-slot-0", "choose-slot-7", "choose-slot-15")):
            continue
        out.append(sc)
    menu = next(sc for sc in out if sc["name"] == "menu")
    out.append({**menu, "name": "names-long", "ship": talk_ship(ship="The Long Voyage", captain="Captain Quinlan"),
                "steps": ["outfit", "d", "s", "c", "c"]})
    # The lander's shields, drawn on the landers unless the bomb is built.
    for name, shields in (("shields-all", 15), ("shields-some", 5)):
        out.append({**menu, "name": name, "state": {"LANDER_SHIELDS": shields},
                    "steps": ["outfit", "d", "s", "d", "s", "s", "c", "c", "c"]})
    out += shipyard_screens_scenarios()
    out += fleet_screens_scenarios()
    return out


def fleet_screens_scenarios():
    """The host's fleet drawn (stage 4b item 10): the shipyard's hangar
    with fleet ships (the doors opening on them as it opens, on a ship
    built, closing on one scrapped; each one's crew count, or SCRAP), the
    list of ships to build (DrawRaceStrings), the support ships round the
    flagship in the status area; the pick screen (DrawArmadaPickShip,
    DoPickBattleShip's cursor, its name and crew); and the roster (the
    icons, the lit one flashing on the clock, its crew)."""
    out = []
    human, spathi, arilou, orz, syreen, urquan, vux, shofixti, yehat, pkunk = (
        RACES.index(r) for r in ("HUMAN_SHIP", "SPATHI_SHIP", "ARILOU_SHIP", "ORZ_SHIP", "SYREEN_SHIP", "URQUAN_SHIP",
                                 "VUX_SHIP", "SHOFIXTI_SHIP", "YEHAT_SHIP", "PKUNK_SHIP"))

    def add(name, steps, fleet=(), allied=("HUMAN_SHIP",), armada=(), ship=None, crew_cost=3, seed=4242, state=None):
        out.append({"name": name, "ship": ship or talk_ship(ru=5000), "state": state or {}, "costs": {}, "fuel_cost": 20,
                    "crew_cost": crew_cost, "shofixti": False, "steps": steps.split(),
                    "fleet": [[RACES[r], *rest] for r, *rest in fleet], "allied": list(allied),
                    "armada": [[RACES[r], *rest] for r, *rest in armada], "seed": seed})

    some = [(spathi, 30, 6, 0), (arilou, 4, 7, 3), (human, 18, 5, 7), (orz, 9, 8, 11)]
    add("fleet-yard-open", "shipyard t:40 d u c", fleet=some)
    add("fleet-yard-open-full", "shipyard",
        fleet=[(r, c, 5 + i % 16, i) for i, (r, c) in enumerate(
            [(spathi, 30), (arilou, 6), (human, 18), (orz, 16), (syreen, 12), (urquan, 42), (vux, 20), (shofixti, 6),
             (yehat, 20), (pkunk, 8), (spathi, 1), (human, 3)])])
    add("fleet-yard-build", "shipyard s d s r r l s u u d c d r s",
        allied=("ARILOU_SHIP", "HUMAN_SHIP", "ORZ_SHIP", "SPATHI_SHIP"))
    add("fleet-yard-build-list-wraps", "shipyard s d r s l l l r c s s",
        allied=("ARILOU_SHIP", "HUMAN_SHIP", "SPATHI_SHIP"), fleet=[(spathi, 30, 6, 0)])
    add("fleet-yard-build-alone", "shipyard s d s r l s c", fleet=[(human, 18, 5, 1)])
    add("fleet-yard-build-poor", "shipyard s d s s c c", ship=talk_ship(ru=50), allied=("HUMAN_SHIP", "SPATHI_SHIP"))
    add("fleet-yard-crew", "shipyard s d s u u u d d d s c", fleet=some, allied=("HUMAN_SHIP", "SPATHI_SHIP", "ARILOU_SHIP"))
    add("fleet-yard-crew-full", "shipyard s d s u d s r r r s u u d s c", fleet=some,
        allied=("HUMAN_SHIP", "SPATHI_SHIP", "ARILOU_SHIP"))
    add("fleet-yard-scrap", "shipyard s d s d s c", fleet=[(spathi, 1, 6, 0), (arilou, 6, 7, 1)],
        allied=("SPATHI_SHIP", "ARILOU_SHIP"))
    add("fleet-yard-scrap-cancel", "shipyard s d s d d u d c c", fleet=[(spathi, 2, 6, 0)], allied=("SPATHI_SHIP",))
    add("fleet-yard-buy-back", "shipyard s d s d u u c c", fleet=[(spathi, 1, 6, 0)], allied=("SPATHI_SHIP",))
    add("fleet-yard-threshold", "shipyard s d s u d d c", fleet=[(human, 5, 6, 0)],
        state={"CREW_PURCHASED0": 999 & 255, "CREW_PURCHASED1": 999 >> 8})
    add("fleet-yard-flagship", "shipyard s s u d s c c", fleet=some)
    # The pick screen: waiting, destroyed (an X) and fled (blue) fleet ships round the flagship.
    armada = [(spathi, 30, 6, 0, 0), (arilou, 0, 7, 2, 1), (human, 10, 5, 4, 2), (urquan, 20, 9, 6, 0),
              (vux, 20, 10, 8, 0), (orz, 5, 11, 11, 0)]
    add("pick-cursor", "pick r r r r l l l l l l l u u d d r r r d l l l l", armada=armada)
    add("pick-select", "pick l s r r s", armada=armada)
    add("pick-flagship", "pick s", armada=armada)
    add("pick-flagship-crew", "pick r l s", armada=[(spathi, 30, 6, 1, 0)],
        ship=talk_ship(crew=12, modules=["CREW_POD"] * 2 + ["EMPTY"] * 13 + ["GUN_WEAPON"],
                       ship="The Long Voyage", captain="Captain Quinlan"))
    add("pick-all-gone", "pick r d l l s", armada=[(spathi, 0, 6, 0, 1), (arilou, 6, 7, 5, 2), (human, 0, 5, 9, 1)])
    add("pick-full", "pick r r r d d l l l l l l l l s",
        armada=[(r, c, 5 + i, i, st) for i, (r, c, st) in enumerate(
            [(spathi, 30, 0), (arilou, 6, 0), (human, 18, 1), (orz, 16, 0), (syreen, 12, 2), (urquan, 42, 0),
             (vux, 20, 0), (shofixti, 6, 0), (yehat, 20, 1), (pkunk, 8, 0), (spathi, 1, 0), (human, 3, 2)])])
    # The roster: the lit ship flashing on the clock, its crew moved both ways and to each limit.
    add("roster-cursor", "roster t:10 t:60 t:61 t:120 d t:200 d d u r l r t:400 u u l c", fleet=some)
    add("roster-crew", "roster s u u d t:90 d s d s d d u s c", fleet=[(spathi, 28, 6, 0), (arilou, 6, 7, 3)],
        ship=talk_ship(crew=3))
    add("roster-limits", "roster s u u u s r s d d d d s c", fleet=[(human, 18, 5, 0), (orz, 1, 8, 1)],
        ship=talk_ship(crew=49))
    add("roster-twelve", "roster " + "d " * 14 + "r " * 3 + "u " * 13 + "l t:500 s u s c",
        fleet=[(r, c, 5 + i, i) for i, (r, c) in enumerate(
            [(spathi, 30), (arilou, 6), (human, 18), (orz, 16), (syreen, 12), (urquan, 42), (vux, 20), (shofixti, 6),
             (yehat, 20), (pkunk, 8), (spathi, 1), (human, 3)])])
    add("roster-one", "roster d u r s u d s c", fleet=[(pkunk, 4, 9, 6)])
    # The lit ship's flash through its colours and round to red again (flashSupportShip's 8-bit steps).
    add("roster-flash", "roster " + " ".join(f"t:{60 * k}" for k in range(1, 36)) + " c", fleet=some)
    return out


def fleet_lines(sc):
    """golden/screens.c's fleet lines for a scenario: the seed, the races
    (standing, starting and most crew, icons and melee icon), the fleet and
    the armada."""
    crews = race_crews()
    ships = race_ships()
    races = []
    for i, r in enumerate(RACES):
        g = ships[i]["graphics"]
        races += [str(ALLIED["good"] if r in sc["allied"] else ALLIED["bad"]), str(crews[i][0]), str(crews[i][1]),
                  g.get("icons") or "-", g.get("melee_icon") or "-"]
    return [str(sc["seed"]), " ".join(races),
            " ".join(f"{RACES.index(r)} {c} {cap} {slot}" for r, c, cap, slot in sorted(sc["fleet"], key=lambda f: f[3])),
            " ".join(f"{RACES.index(r)} {c} {cap} {slot} {st}" for r, c, cap, slot, st in sc["armada"])]


def screens_frames():
    """The resources the screens recording loads: the starbase's own, and
    every race ship's status icons and melee icon (the fleet's)."""
    out = list(SCREENS_FRAMES)
    for ship in race_ships():
        g = ship["graphics"]
        out += [r for r in (g.get("icons"), g.get("melee_icon")) if r]
    return list(dict.fromkeys(out))


def shipyard_screens_scenarios():
    """The shipyard with the flagship alone (fleet ships come with stage
    4): its menu, the cursor round the hangar's slots and back to the
    flagship, the crew hired and sent away (the pods filling and
    emptying, too dear, full, none left, the price rising and falling at
    CREW_EXPENSE_THRESHOLD), Save game, leaving, the power lines on the
    clock, and blueprints of other ships."""
    out = []

    def add(name, steps, ship=None, state=None, crew_cost=3):
        out.append({"name": name, "ship": ship or talk_ship(ru=2000), "state": state or {}, "costs": {},
                    "fuel_cost": 20, "crew_cost": crew_cost, "shofixti": False, "steps": steps.split()})

    def times(start, n, step):
        return " ".join(f"t:{start + k * step}" for k in range(n))

    add("yard-menu", "shipyard d d d u u t:20 t:56 t:60 t:113 " + times(170, 20, 57) + " s")
    add("yard-menu-cancel", "shipyard c")
    add("yard-exit", "shipyard u s")
    add("yard-save", "shipyard d s d s")
    add("yard-ships", "shipyard s r r r r l u u u d d d l r c d u c")
    add("yard-ships-all", "shipyard s u " + "r " * 6 + "u " + "l " * 7 + "u d d c c")
    add("yard-crew", "shipyard s s u u u d d s c c")
    add("yard-crew-cancel", "shipyard s s u c s d c c")
    add("yard-crew-other-keys", "shipyard s s l r t:30 t:90 u s c")
    add("yard-crew-full", "shipyard s s u u d d c c", talk_ship(ru=2000, crew=48))
    add("yard-crew-poor", "shipyard s s u u u d u c c", talk_ship(ru=7))
    add("yard-crew-none", "shipyard s s d d d u c c", talk_ship(ru=2000, crew=2))
    add("yard-crew-threshold", "shipyard s s u u d d d u c c", state={"CREW_PURCHASED0": 999 & 255,
                                                                     "CREW_PURCHASED1": 999 >> 8})
    pods = ["CREW_POD", "CREW_POD", "STORAGE_BAY", "FUEL_TANK", "CREW_POD", "STORAGE_BAY", "HIGHEFF_FUELSYS",
            "DYNAMO_UNIT", "SHIVA_FURNACE", "TRACKING_SYSTEM", "ANTIMISSILE_DEFENSE", "EMPTY", "STORAGE_BAY",
            "BLASTER_WEAPON", "CANNON_WEAPON", "GUN_WEAPON"]
    add("yard-blueprint", "shipyard s s " + "u " * 6 + "d " * 4 + "c c d d s",
        talk_ship(ru=2000, crew=97, fuel=9050, landers=4, elements=[100, 90, 80, 70, 60, 50, 40, 31],
                  modules=pods, drives=[0, 3, 5, 6, 10], jets=[1, 2, 7]))
    add("yard-blueprint-empty", "shipyard s s u c c", talk_ship(ru=2000, crew=0, fuel=0, landers=0,
                                                              modules=["EMPTY"] * 16, drives=[], jets=[]))
    add("yard-blueprint-pods", "shipyard s s " + "d " * 6 + "u " * 3 + "c c",
        talk_ship(ru=2000, crew=150, fuel=1000, modules=["CREW_POD"] * 3 + ["EMPTY"] * 12 + ["GUN_WEAPON"]))
    add("yard-blueprint-bays", "shipyard c",
        talk_ship(ru=2000, crew=0, elements=[500, 499, 1, 0, 0, 0, 0, 0],
                  modules=["STORAGE_BAY"] * 3 + ["EMPTY"] * 13))
    add("yard-blueprint-bomb", "shipyard s s u c c",
        talk_ship(ru=2000, modules=["BOMB_MODULE_0", "BOMB_MODULE_1", "BOMB_MODULE_2", "BOMB_MODULE_3",
                                    "BOMB_MODULE_4", "BOMB_MODULE_5", "CREW_POD"] + ["EMPTY"] * 9),
        state={"CHMMR_BOMB_STATE": 3})
    return out


def record_screens(uqm, exe, outfit_exe, strings_exe, package):
    """What UQM draws on the starbase's screens (golden/screens.c): the
    menu, outfitting and the shipyard, step by step, with the flashing
    highlight and the hangar's colour map after each. The frame counts
    and rectangles and colortable.hangar's size are read from the
    content; the pictures aren't recorded."""
    import zipfile
    with open(os.path.join(REPO, "data", "game-state.json")) as f:
        game_state = json.load(f)
    fields = {s["name"]: s for s in game_state["states"]}
    state_bytes = (game_state["total_bits"] + 7) >> 3
    with open(os.path.join(REPO, "data", "new-game.json")) as f:
        prices = json.load(f)["prices"]
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        hangar = os.path.join(tmp, "hangar.ct")
        with open(hangar, "wb") as f:
            f.write(z.read(index["colortable.hangar"][1]))
        cmaps = len(json.loads(subprocess.run([strings_exe, "bintab", hangar], capture_output=True, text=True,
                                              check=True).stdout)[0])
        counts, rects = [], []
        for res in screens_frames():
            ani_path = index[res][1]
            lines = [l.split() for l in z.read(ani_path).decode("latin-1").splitlines() if l.strip()]
            counts += [res, str(len(lines))]
            for i, l in enumerate(lines):
                png = z.read(os.path.dirname(ani_path) + "/" + l[0])
                w, h = int.from_bytes(png[16:20], "big"), int.from_bytes(png[20:24], "big")
                rects.append(f"{res}:{i}:{l[3]}:{l[4]}:{w}:{h}")
    scenarios = []
    for sc in screens_scenarios():
        steps = sc["steps"]
        if steps[0] not in ("outfit", "shipyard", "pick", "roster") and not steps[0].startswith("base:"):
            raise RuntimeError(f"screens {sc['name']}: starts with {steps[0]}")
        if steps[0] == "outfit" and "fleet" not in sc:
            steps, _ = run_outfit(outfit_exe, {**sc, "shofixti": sc.get("shofixti", False)}, fields, state_bytes,
                                  prices)
        ship = sc["ship"]
        head = outfit_head(sc, fields, state_bytes, prices) + [
            f"{ship['ship']}|{ship['captain']}", " ".join(counts), " ".join(rects), SCREENS_LEADING, str(cmaps)]
        tail = fleet_lines(sc) if "fleet" in sc else []
        out = subprocess.run([exe], input="\n".join(head + [" ".join(steps)] + tail) + "\n", capture_output=True,
                             text=True)
        if out.returncode:
            raise RuntimeError(f"screens {sc['name']} ({out.returncode}): {out.stdout[-300:]}{out.stderr}")
        lines = [json.loads(x) for x in out.stdout.splitlines()]
        if len(lines) != len(steps):
            raise RuntimeError(f"screens {sc['name']}: {len(lines)} of {len(steps)} steps")
        scenarios.append({k: v for k, v in sc.items() if k not in ("steps",)} | {
            "steps": steps, "drawn": [x["draw"] for x in lines], "flash": [x["flash"] for x in lines],
            "cmap": [x["cmap"] for x in lines]})
    data = header(uqm, ["tools/extract/golden/screens.c", "tools/extract/golden/screens_stubs.c",
                        "src/uqm/starbase.c", "src/uqm/outfit.c", "src/uqm/shipyard.c", "src/uqm/sis.c",
                        "src/uqm/menu.c", "src/uqm/border.c", "src/uqm/util.c", "src/uqm/pickship.c",
                        "src/uqm/planets/roster.c", "src/uqm/build.c", "src/uqm/displist.c"], [
        "What UQM 0.8.0's own starbase screens draw, recorded by running its "
        "DoStarBase, DoOutfit and DoShipyard (with sis.c, menu.c, border.c and util.c's "
        "drawing) with each drawing call recorded in the screen's "
        "coordinates (tools/extract/golden/screens.c), on a clock of the "
        "program's own; the frames counted from the official content "
        f"package (SHA-256 {CONTENT_SHA256}), the fonts by their leading.",
        "Each scenario: as outfit.json's (the story state, prices, ship), "
        "and the steps (base:S opens the menu on entry S, outfit opens "
        "outfitting, t:N runs the screen at the clock's N with no key, the "
        "rest keys), with what each drew: [\"s\", resource, frame, x, y] "
        "a stamp, [\"f\", resource, frame, x, y, r, g, b] a filled stamp, "
        "[\"r\"/\"o\"/\"c\", x, y, width, height, r, g, b] a filled "
        "rectangle, an outline, the clip rectangle cleared, [\"p\", x, y, r, "
        "g, b] a point, [\"t\", font, x, y, align, count, text, r, g, b, "
        "effect] text ({gs:N} is GAME_STRING (N); effect the font effect's "
        "frame of graphics.fontgradient, or -1), each with its context's "
        "clip rectangle as x, y, width, height (UQM draws only inside it); "
        "and after each step, where the flashing highlight is (sis.c's "
        "SetFlashRect: [x, y, width, height] or null) and the colour map "
        "last set (the shipyard's power lines: a map of colortable.hangar, "
        "or -1).",
        "The shipyard (\"shipyard\" opens DoShipyard as DoStarBase does, "
        "with its menu on SHIPYARD; each later step runs on_input_frame, "
        "then the screen, as DoInput does).",
        "The host's fleet (stage 4b item 10; the scenarios with fleet): "
        "allied (the races GOOD_GUY in avail_race_q, each with its ship's "
        "starting and most crew, its status icons and melee icon), fleet "
        "(built_ship_q: [race, crew, captain's name, slot], races.h's race "
        "names), armada (race_q[0] for the pick screen, the flagship last: "
        "[race, crew, captain's name, slot, state], 0 waiting, 1 destroyed, "
        "2 fled) and seed (TFB_SeedRandom's, the captains CloneShipFragment "
        "names). In the shipyard, ShowCombatShip's doors (every frame of "
        "their opening or closing in the one step: the program's clock "
        "doesn't run in it), ShowShipCrew, DrawRaceStrings and the support "
        "ships in the status area (DrawSupportShips) are UQM's own. \"pick\" "
        "draws the pick screen as GetArmadaStarShip does (DrawArmadaPickShip, "
        "then DoPickBattleShip freshly, in the battle's SpaceContext: the "
        "space window), the keys going to DoPickBattleShip until select "
        "picks; the captains' names read {gs:N}, N the race strings' index. "
        "\"roster\" opens the roster (RosterMenu's setup in StatusContext, "
        "over whatever the status area shows), the keys and t:N going to "
        "DoModifyRoster until it's left: then the lit ship drawn unlit "
        "(RosterMenu's tail; its DrawStatusMessage (NULL), the flight's "
        "location, left out).",
        "frames: each resource's number of frames; hangar_maps: how many "
        "colour maps colortable.hangar has; rects: each frame's "
        "GetFrameRect, [x, y, width, height] (its corner less the hot "
        "spot, and its size), read from the content.",
    ])
    data["frames"] = dict(zip(counts[::2], map(int, counts[1::2])))
    data["hangar_maps"] = cmaps
    data["rects"] = {res: [] for res in screens_frames()}
    for r in rects:
        res, i, hx, hy, w, h = r.rsplit(":", 5)
        data["rects"][res].append([-int(hx), -int(hy), int(w), int(h)])
    data["scenarios"] = scenarios
    return data


# ------------------------------------------------------------ the encounter screens

ENCOUNTER_PROGRAM = os.path.join(HERE, "golden", "encounter.c")
# screens' and InitEncounter's second ring of ships' arithmetic (square_root, ARCTAN, SINE).
ENCOUNTER_LINK = SCREENS_LINK + ["uqm/trans.c", "libs/math/sqrt.c"]
ENCOUNTER_FRAMES = ["graphics.segue", "graphics.pickship", "graphics.playmenu", "graphics.fontgradient",
                    "graphics.flagshipstatus", "graphics.status"]


def encounter_scenarios():
    """The encounter screen (InitEncounter) in deep space and in a system,
    with groups of one ship to past the ten display points (the second
    ring), and an infinite fleet (sixty of its ship); the battle-won
    screen (UninitEncounter after a win) with one ship, five (a row), six
    (the row cleared, a second), with no key, a key held (the fades cut
    short) and Escape held; and the Thraddash's body count (the stage 3b-2
    plan's item 1): Thraddash battles won and not (only the ships with no
    crew destroyed, an infinite fleet's never), the count capped at 25 and
    brought back to it from past it, not counted with THRADD_MANNER set,
    nor for another race."""
    out = []

    def add(name, where, planet, ships, step, ship_name="EARTHLING CRUISER", body=0, manner=0):
        out.append({"name": name, "where": where, "planet": planet, "ship_name": ship_name,
                    "ships": [list(s) for s in ships], "step": step, "body_count": body, "thradd_manner": manner})

    add("deep-space-one", "hyper", "planet.oolite.large", [("SPATHI_SHIP", 30)], "encounter")
    add("deep-space-three", "hyper", "planet.water.large", [("URQUAN_SHIP", 42)] * 3, "encounter")
    add("system-seven", "system", "planet.redux.large", [("ILWRATH_SHIP", 22)] * 7, "encounter")
    add("ring-two", "hyper", "planet.acid.large", [("SLYLANDRO_SHIP", 12)] * 12, "encounter")
    add("infinite", "system", "planet.slaveshield.large", [("ILWRATH_SHIP", -1)], "encounter")
    for keys in ("none", "any", "escape"):
        add(f"won-one-{keys}", "hyper", "planet.oolite.large", [("SPATHI_SHIP", 30)], f"won:{keys}")
    add("won-five", "hyper", "planet.oolite.large", [("URQUAN_SHIP", 42)] * 5, "won:none")
    add("won-six", "system", "planet.water.large", [("ILWRATH_SHIP", 22)] * 6, "won:none",
        ship_name="THE LONG VOYAGE")
    add("won-seven-any", "system", "planet.water.large", [("BLACK_URQUAN_SHIP", 42)] * 7, "won:any")
    add("won-thraddash-three", "hyper", "planet.oolite.large", [("THRADDASH_SHIP", 8)] * 3, "won:none")
    add("won-thraddash-capped", "system", "planet.water.large", [("THRADDASH_SHIP", 8)] * 4, "won:escape", body=23)
    add("won-thraddash-past", "hyper", "planet.oolite.large", [("THRADDASH_SHIP", 8)], "won:escape", body=31)
    add("won-thraddash-manner", "hyper", "planet.oolite.large", [("THRADDASH_SHIP", 8)] * 2, "won:escape", body=5,
        manner=1)
    add("won-ilwrath-not-counted", "hyper", "planet.oolite.large", [("ILWRATH_SHIP", 22)] * 2, "won:escape", body=7)
    add("ended-thraddash-two-of-three", "hyper", "planet.oolite.large",
        [("THRADDASH_SHIP", 0), ("THRADDASH_SHIP", 5), ("THRADDASH_SHIP", 0)], "ended", body=10)
    add("ended-thraddash-none", "hyper", "planet.oolite.large", [("THRADDASH_SHIP", 8)] * 2, "ended", body=10)
    add("ended-thraddash-infinite", "system", "planet.water.large", [("THRADDASH_SHIP", -1)], "ended", body=3)
    return out


def encounter_frames():
    """The resources the encounter recording loads: its own, each race
    ship's status icons, and the gravity wells the scenarios name."""
    out = list(ENCOUNTER_FRAMES)
    ships = race_ships()
    for sc in encounter_scenarios():
        out.append(sc["planet"])
        for race, _ in sc["ships"]:
            out.append(ships[RACES.index(race)]["graphics"]["icons"])
    return list(dict.fromkeys(out))


def record_encounter(uqm, exe, strings_exe, package):
    """What UQM draws on the encounter screen and the battle-won screen
    (golden/encounter.c), each scenario's every call in its one step."""
    import zipfile
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        counts, rects = [], []
        for res in encounter_frames():
            ani_path = index[res][1]
            lines = [l.split() for l in z.read(ani_path).decode("latin-1").splitlines() if l.strip()]
            counts += [res, str(len(lines))]
            for i, l in enumerate(lines):
                png = z.read(os.path.dirname(ani_path) + "/" + l[0])
                w, h = int.from_bytes(png[16:20], "big"), int.from_bytes(png[20:24], "big")
                rects.append(f"{res}:{i}:{l[3]}:{l[4]}:{w}:{h}")
    ships = race_ships()
    scenarios = []
    for sc in encounter_scenarios():
        group = " ".join(f"{RACES.index(r)} {ships[RACES.index(r)]['graphics']['icons']} {c}" for r, c in sc["ships"])
        lines = [" ".join(counts), " ".join(rects), SCREENS_LEADING, sc["where"], sc["planet"], sc["ship_name"], group,
                 sc["step"], f"{sc['body_count']} {sc['thradd_manner']}"]
        out = subprocess.run([exe], input="\n".join(lines) + "\n", capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"encounter {sc['name']} ({out.returncode}): {out.stdout[-300:]}{out.stderr}")
        drawn = json.loads(out.stdout)
        scenarios.append(sc | {"body_count_after": drawn["body"], "drawn": drawn["draw"]})
    data = header(uqm, ["tools/extract/golden/encounter.c", "tools/extract/golden/screens.c",
                        "tools/extract/golden/screens_stubs.c", "src/uqm/encount.c", "src/uqm/sis.c",
                        "src/uqm/menu.c", "src/uqm/border.c", "src/uqm/util.c", "src/uqm/pickship.c",
                        "src/uqm/trans.c", "src/libs/math/sqrt.c"], [
        "What UQM 0.8.0's own encounter screen and battle-won screen draw, "
        "recorded by running its InitEncounter and UninitEncounter (encount.c, "
        "with sis.c, menu.c, border.c, util.c and pickship.c's drawing) with "
        "each drawing call recorded as screens.json's are "
        "(tools/extract/golden/encounter.c); the frames counted from the "
        f"official content package (SHA-256 {CONTENT_SHA256}), the fonts by "
        "their leading.",
        "Each scenario: where (hyper: IN_HYPERSPACE, \"ENCOUNTER IN DEEP "
        "SPACE\"; system: IN_INTERPLANETARY, \"ENCOUNTER AT\" the cluster, "
        "{cluster}, and GLOBAL_SIS (PlanetName), {planet}), the gravity "
        "well's resource (planet[0], drawn in the screen's middle), the "
        "flagship's name, the group's ships (npc_built_ship_q: [race, crew], "
        "crew -1 an infinite fleet; each race's strings read {gs:1000 + its "
        "number in races.h's order}), and the step: \"encounter\" draws "
        "InitEncounter's screen up to its menu; \"won:KEYS\" UninitEncounter "
        "after a battle won, every wait and fade in the one step: KEYS none "
        "(no key), any (a key held: AnyButtonPress TRUE, so each fade is "
        "cut to its last colour and WaitForAnyButton, waiting for a new "
        "press, runs out), escape (Escape held too: the debris's part "
        "skipped); \"ended\" UninitEncounter after a battle not won "
        "(battle_counter[1] 1: only the ships with crew 0 destroyed, an "
        "infinite fleet's, crew -1, never). body_count and thradd_manner: "
        "THRADDASH_BODY_COUNT and THRADD_MANNER before the step.",
        "drawn: the calls as screens.json's; body_count_after: "
        "THRADDASH_BODY_COUNT after the step (encount.c:760's count); frames "
        "and rects as there.",
    ])
    data["frames"] = dict(zip(counts[::2], map(int, counts[1::2])))
    data["rects"] = {res: [] for res in encounter_frames()}
    for r in rects:
        res, i, hx, hy, w, h = r.rsplit(":", 5)
        data["rects"][res].append([-int(hx), -int(hy), int(w), int(h)])
    data["scenarios"] = scenarios
    return data


# ------------------------------------------------------------ the lander report

REPORT_PROGRAM = os.path.join(HERE, "golden", "report.c")
REPORT_LINK = ["uqm/util.c", "libs/strings/unicode.c", "libs/graphics/pixmap.c"]
REPORT_SHEET = "graphics.orbitbackground"   # SpaceJunkFrame


def report_texts():
    """The reports recorded: lander reports from the content (the table
    and its entry) of every length, and texts of our own for what the
    content's don't have (words of 39 and 40 letters, a page filled
    exactly, blank lines, punctuation of every kind, a trailing newline,
    UTF-8 beyond ASCII, nothing)."""
    content = [("text.moonbase", 0), ("text.ruins", 0), ("text.aquahelix", 0), ("text.androsynthruins", 1),
               ("text.umgahcaster", 1), ("text.urquanwreck", 0), ("text.syreenvault", 2), ("text.fwiffo", 0),
               ("text.sundevice", 0)]
    exact = " ".join(["x" * 39] * 12)   # twelve rows of 39 letters: the last row fits
    ours = {
        "long-words": "Short " + "a" * 40 + " then " + "b" * 39 + " c " + "d" * 38 + ", " + "e" * 39 + ".",
        "exact-page": exact,
        "page-and-a-word": exact + " more",
        "newlines": "First line.\nSecond line\n\nAfter a blank line, more text.\n\n\nAnd the end",
        "many-lines": "\n".join(f"Line {n}, of many." for n in range(1, 31)),
        "punctuation": "Wait, what? Yes! No. Maybe... ok,fine;done. A.B,C!D?E. Commas,,, and stops... End?!",
        "trailing-newline": "Some text, then a newline.\n",
        "utf8": "Caf\u00e9 au lait, na\u00efve r\u00e9sum\u00e9. \u00c0 bient\u00f4t!",
        "nothing": "",
    }
    return content, ours


def record_report(uqm, exe, strings_exe, package):
    """UQM's lander report (golden/report.c): DoDiscoveryReport typing a
    report into the scan map's area, its pages and the keys that hurry it
    on, on the program's clock. The reports from the content are named by
    their table and entry, not written out; the frames' rectangles are
    read from the content."""
    import zipfile
    content, ours = report_texts()
    tables = read_string_tables(strings_exe, package, lambda key: key in {t for t, _ in content} | {"text.starcon"})
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        ani_path = index[REPORT_SHEET][1]
        lines = [l.split() for l in z.read(ani_path).decode("latin-1").splitlines() if l.strip()]
        rects = {}
        for i in (18, 21):
            png = z.read(os.path.dirname(ani_path) + "/" + lines[i][0])
            w, h = int.from_bytes(png[16:20], "big"), int.from_bytes(png[20:24], "big")
            rects[i] = (int(lines[i][3]), int(lines[i][4]), w, h)
    frames = len(lines)
    head = f"{frames} " + " ".join(f"{i}:{hx}:{hy}:{w}:{h}" for i, (hx, hy, w, h) in rects.items())

    def entry(table, i):
        data = bytes.fromhex(tables[table][1][i][1])
        return data[:-1] if data.endswith(b"\0") else data

    def run(text, keys, stop=None):
        events = " ".join(keys + ([f"stop:{stop}"] if stop else [])) or "-"
        inp = "\n".join([head, end_hex, text.hex(), events]) + "\n"
        out = subprocess.run([exe], input=inp, capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"report: exit {out.returncode}: {out.stderr}")
        return json.loads(out.stdout)

    # The end-of-page string: the program says which game string it is.
    end_hex = b"?".hex()
    end_index = run(b"", [])["end_string"]
    end_hex = entry("text.starcon", end_index).hex()

    def after_pages(text, keys, hold=50):
        """keys, then a key after each page as it waits, until the report
        closes: a while after the page's last letter, at times that fall
        on and between the waits' looks."""
        keys = list(keys)
        for n in range(64):
            last = max([int(k.split(":")[1]) for k in keys] + [0])
            got = run(text, keys, last + 30000)
            if got["restored"] >= 0:
                return keys, got
            at = max(got["calls"][-1][0], last) + 500 + (n * 17) % 41
            keys += [f"d:{at}", f"u:{at + hold}"]
        raise RuntimeError(f"report: never closes: {keys[:20]} {got['calls'][-3:]}")

    def taps(start, n, step, hold=1):
        return [k for t in range(start, start + n * step, step) for k in (f"d:{t}", f"u:{t + hold}")]

    def at_end_string(text):
        """A key as the first page's end-of-page string is typed."""
        first = next(t for t, c in run(text, [], 40000)["calls"] if c[0] == "t" and c[6].startswith("{end:"))
        return [f"d:{first + 5}", f"u:{first + 25}"]

    # The key patterns: name, the keys before the pages' ones, or what
    # makes them from the text (None: no keys at all).
    patterns = {
        "pages": [],
        "mid-typing": ["d:1000", "u:1030"],
        "quick-taps": taps(500, 6, 9) + taps(700, 3, 21),
        "held-across-page-end": ["d:800", "u:30000"],
        "held-from-start": ["d:0", "u:400"],
        "at-once-twice": ["d:300", "d:310", "u:320", "u:330"],
        "at-end-string": at_end_string,
        "mashing": taps(200, 40, 50, 10),
        "none": None,
    }
    scenarios = []

    def add(name, text, ref, pattern):
        keys = patterns[pattern]
        if keys is None:
            got, keys = run(text, [], 40000), ["stop:40000"]
        else:
            keys, got = after_pages(text, keys(text) if callable(keys) else keys)
        scenarios.append({"name": f"{name}/{pattern}", "text": ref, "keys": keys, "calls": got["calls"],
                          "restored": got["restored"], "released": got["released"]})

    for table, i in content:
        if table == "text.moonbase":
            chosen = list(patterns)
        elif table in ("text.ruins", "text.aquahelix"):
            chosen = ["pages", "mid-typing"]
        else:
            chosen = ["pages"]
        for pattern in chosen:
            add(f"{table}:{i}", entry(table, i), {"table": table, "index": i}, pattern)
    for name, text in ours.items():
        if name in ("exact-page", "page-and-a-word", "many-lines"):
            chosen = ["pages", "mid-typing", "held-across-page-end"]
        elif name == "nothing":
            chosen = ["pages"]
        else:
            chosen = ["pages", "mid-typing"]
        for pattern in chosen:
            add(name, text.encode(), {"literal": text}, pattern)

    data = header(uqm, ["tools/extract/golden/report.c", "src/uqm/planets/report.c", "src/uqm/util.c",
                        "src/uqm/gameinp.c", "src/libs/input/sdl/vcontrol.c"], [
        "UQM 0.8.0's own lander report, recorded by running its report.c "
        "(DoDiscoveryReport, MakeReport, ClearReportArea) with util.c's waits "
        "(tools/extract/golden/report.c): each drawing call in the screen's "
        "coordinates, at the time on the program's clock (UQM's ticks, 840 a "
        "second; the waits' SleepThread moves it on, nothing else takes "
        "time), with keys going down and up at scripted times, seen as UQM's "
        "input sees them (a key held, or one gone down since the last look). "
        "The fonts are UQM's default, OPT_PC, as screens.json's: the report "
        "area's stamps are frame 21 of graphics.orbitbackground and the text "
        "takes its colours from graphics.landerfonteffect. The frames are "
        f"read from the official content package (SHA-256 {CONTENT_SHA256}).",
        "Each scenario: its text (a lander report of the content, the table "
        "and the entry, the first being 0, as UQM's _GetStringData reads it, "
        "or a literal of ours), the keys (\"d:T\" down at T, \"u:T\" up at "
        "T; \"stop:T\" the recording stopped once the clock passed T, the "
        "report waiting for a key), the calls as [time, call], when "
        "DoDiscoveryReport put the screen back (restored) and when its "
        "WaitForNoInput saw every key up (released), -1 if not.",
        "A call: [\"s\", resource, frame, x, y] DrawStamp, [\"c\", x, y, width, "
        "height, r, g, b] ClearDrawable (the clip rectangle in the background "
        "colour), [\"t\", font, x, y, align, count, text, r, g, b, effect "
        "resource, effect frame] font_DrawText; each with the context's clip "
        "rectangle as x, y, width, height. The text is where its pointer is: "
        "\"{report:N}\" the report from byte N of its UTF-8, \"{end:N}\" "
        "MakeReport's end_page_buf (GAME_STRING (end_string) and a newline) "
        "from byte N; count characters of it are drawn.",
        "frames: graphics.orbitbackground's number of frames; rects: its "
        "frames 18 and 21's GetFrameRect, [x, y, width, height] (the corner "
        "less the hot spot, and the size).",
    ])
    data["end_string"] = end_index
    data["frames"] = frames
    data["rects"] = {str(i): [-hx, -hy, w, h] for i, (hx, hy, w, h) in rects.items()}
    data["scenarios"] = scenarios
    return data


# ------------------------------------------------------------ selling minerals

SELL_PROGRAM = os.path.join(HERE, "golden", "sell.c")
SELL_LINK = ["uqm/planets/cargo.c", "uqm/sis.c", "uqm/menu.c", "uqm/util.c", "libs/graphics/pixmap.c"]
SELL_SHEET = "graphics.miscdata"   # MiscDataFrame
SELL_ICON = 11                     # NUM_SCANDOT_TRANSITIONS * 2 + 3: the first element's icon
# A scenario's calls are kept whole up to this many; past it, the first
# SELL_HEAD and the last SELL_TAIL, with how many and a hash of them all.
SELL_WHOLE, SELL_HEAD, SELL_TAIL = 600, 150, 60


def sell_loads():
    """The holds sold: (name, the 8 element amounts, bio data, storage
    bays, resource units, CHMMR_BOMB_STATE, the key patterns tried, None
    for all of record_selling's)."""
    every = None
    return [
        ("one", [0, 0, 0, 12, 0, 0, 0, 0], 0, 1, 0, 0,
         ["none", "at-first-wait", "held", "mid-category", "last-category", "at-the-end"]),
        ("first-only", [5, 0, 0, 0, 0, 0, 0, 0], 0, 1, 40, 0,
         ["none", "at-first-wait", "held-let-go", "held", "mid-category"]),
        ("last-only", [0, 0, 0, 0, 0, 0, 0, 3], 2, 1, 0, 0, ["none", "at-first-wait", "mid-category"]),
        ("one-unit", [0, 1, 0, 0, 0, 0, 0, 0], 0, 1, 0, 0, ["none", "at-first-wait"]),
        ("several", [10, 0, 7, 0, 0, 3, 0, 0], 4, 1, 1234, 0, every),
        ("all-eight", [3, 4, 5, 6, 7, 8, 9, 10], 25, 1, 7, 0, every),
        ("full-hold", [0, 0, 0, 0, 500, 0, 0, 0], 0, 1, 0, 0, ["none", "mid-category"]),
        ("unlimited", [6, 0, 0, 2, 0, 0, 0, 4], 1, 2, 300, 2, ["none", "mid-category", "between-categories"]),
        ("big", [1500, 700, 300, 200, 100, 50, 25, 10], 120, 6, 65000, 0,
         ["none", "mid-category", "between-categories"]),
    ]


def record_selling(uqm, exe, strings_exe, package):
    """UQM's mineral selling (golden/sell.c): SellMinerals counting the
    hold down in the status area, a unit a millisecond, its waits, and
    keys that sell the rest at once, on the program's clock (in
    milliseconds). graphics.miscdata's frame count and its first element
    icon's size are read from the content."""
    import hashlib
    import zipfile
    with open(os.path.join(REPO, "data", "new-game.json")) as f:
        worth = json.load(f)["prices"]["element_worth"]
    worth = [worth[c] for c in ("COMMON", "CORROSIVE", "BASE_METAL", "NOBLE", "RARE_EARTH", "PRECIOUS",
                                "RADIOACTIVE", "EXOTIC")]
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        ani_path = index[SELL_SHEET][1]
        lines = [l.split() for l in z.read(ani_path).decode("latin-1").splitlines() if l.strip()]
        png = z.read(os.path.dirname(ani_path) + "/" + lines[SELL_ICON][0])
        icon = (int.from_bytes(png[16:20], "big"), int.from_bytes(png[20:24], "big"))
    frames = len(lines)

    def run(load, keys):
        _, amounts, bio, bays, ru, bomb, _ = load
        inp = "\n".join([" ".join(map(str, amounts + worth + [bio, bays, ru, bomb])),
                         f"{frames} {icon[0]}:{icon[1]}", " ".join(keys) or "-"]) + "\n"
        out = subprocess.run([exe], input=inp, capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"sell: exit {out.returncode}: {out.stderr}")
        return json.loads(out.stdout)

    def units(load, got):
        """When each category's units were counted (DeltaSISGauges' status message cleared), from a run with no keys."""
        times = [t for t, c in got["calls"] if c[0] == "c"]
        out, k = [], 0
        for amount in load[1]:
            out.append(times[k:k + amount])
            k += amount
        return out

    def patterns(load, plain):
        counted = [u for u in units(load, plain) if u]
        longest = max(counted, key=len)
        p = {
            "none": [],
            "at-first-wait": ["d:100", "u:150"],
            "held-let-go": ["h", "u:300"],
            "held": ["h", f"u:{counted[0][0] + 50}"],
            "mid-category": [f"d:{longest[len(longest) // 2]}", f"u:{longest[len(longest) // 2] + 30}"],
            "last-category": [f"d:{counted[-1][-1] - 1}", f"u:{counted[-1][-1] + 40}"] if len(counted[-1]) > 1 else None,
            "between-categories": [f"d:{counted[0][-1] + 100}", f"u:{counted[0][-1] + 140}"] if len(counted) > 1 else None,
            "tap-in-a-wait": [f"d:{counted[0][-1] + 60}", f"u:{counted[0][-1] + 60}"] if len(counted) > 1 else None,
            "at-the-end": [f"d:{plain['put_away'] - 200}", f"u:{plain['put_away'] - 150}"],
            "mashing": [k for t in range(150, plain["put_away"], 37) for k in (f"d:{t}", f"u:{t + 9}")],
        }
        return {name: keys for name, keys in p.items() if keys is not None}

    def calls(got):
        whole = got["calls"]
        if len(whole) <= SELL_WHOLE:
            return {"calls": whole}
        text = "\n".join(json.dumps(c, separators=(",", ":")) for c in whole)
        return {"head": whole[:SELL_HEAD], "tail": whole[-SELL_TAIL:], "count": len(whole),
                "sha256": hashlib.sha256(text.encode()).hexdigest()}

    scenarios = []
    for load in sell_loads():
        plain = run(load, [])
        chosen = patterns(load, plain)
        if load[6] is not None:
            if set(load[6]) - set(chosen):
                raise RuntimeError(f"sell {load[0]}: no {set(load[6]) - set(chosen)}")
            chosen = {k: chosen[k] for k in load[6]}
        for pattern, keys in chosen.items():
            got = plain if not keys else run(load, keys)
            if got["hold"] != [0] * 8 or got["mass"] != 0:
                raise RuntimeError(f"sell {load[0]}/{pattern}: the hold isn't empty: {got['hold']}")
            name, amounts, bio, bays, ru, bomb, _ = load
            scenarios.append({"name": f"{name}/{pattern}", "amounts": amounts, "bio": bio, "bays": bays,
                              "res_units": ru, "bomb_state": bomb, "keys": keys, "before": got["before"],
                              **calls(got), "put_away": got["put_away"], "res_units_after": got["res_units"]})

    data = header(uqm, ["tools/extract/golden/sell.c", "src/uqm/comm/starbas/starbas.c", "src/uqm/planets/cargo.c",
                        "src/uqm/sis.c", "src/uqm/util.c", "src/uqm/gameinp.c", "src/libs/input/sdl/vcontrol.c",
                        "src/libs/threads/thrcommon.c", "src/libs/time/sdl/sdltime.c"], [
        "UQM 0.8.0's own mineral selling, recorded by running starbas.c's "
        "SellMinerals with cargo.c's DrawCargoStrings and ShowRemainingCapacity and "
        "sis.c's DeltaSISGauges and DrawStatusMessage (tools/extract/golden/sell.c), "
        "from its start to the ClearSISRect that puts the cargo display away: each "
        "drawing call in the screen's coordinates, at the time on the program's "
        "clock, in milliseconds (SDL_GetTicks; GetTimeCounter's ticks, 840 a second, "
        "made from it as UQM's SDL clock makes them; TaskSwitch takes a millisecond, "
        "a sleep wakes at the first millisecond whose ticks reach its time, drawing "
        "takes none), with keys going down and up at scripted times, seen as UQM's "
        "input sees them (a key held, or one gone down since the last look). The "
        "resource units are the status message's (SMM_RES_UNITS, as at the starbase). "
        "graphics.miscdata's frame count and its frame 11's size are read from the "
        f"official content package (SHA-256 {CONTENT_SHA256}).",
        "worth: each element category's worth (ElementWorth, data/new-game.json's). "
        "Each scenario: the hold (amounts, the 8 element amounts; bio, the bio data; "
        "bays, the storage bays in the module slots), res_units and bomb_state "
        "(CHMMR_BOMB_STATE) as the selling starts; the keys (\"h\" a key held as it "
        "starts, gone down before it into the talk's replies; \"d:T\" down at T, "
        "\"u:T\" up at T); before, the status message as UQM's status area shows it "
        "there already (DrawStatusMessage (NULL), drawn at 0); the calls as [time, "
        "call]; put_away, when SellMinerals called ClearSISRect (DRAW_SIS_DISPLAY), "
        "the cargo display put away before the commander speaks; res_units_after.",
        f"A scenario with more than {SELL_WHOLE} calls keeps the first {SELL_HEAD} (head) "
        f"and the last {SELL_TAIL} (tail), how many there were (count) and the SHA-256 "
        "of them all, each written as compact JSON ([time,call], no spaces) and joined "
        "by newlines.",
        "A call: [\"s\", resource, frame, x, y] DrawStamp, [\"r\", x, y, width, height, "
        "r, g, b] DrawFilledRectangle, [\"c\", x, y, width, height, r, g, b] "
        "ClearDrawable (the clip rectangle in the background colour), [\"t\", font, "
        "x, y, align, count, text, r, g, b] font_DrawText ({gs:N} is GAME_STRING (N)); "
        "each with the context's clip rectangle as x, y, width, height.",
        "frames: graphics.miscdata's number of frames; icon: its frame 11's width "
        "and height (the icons' bounds DrawCargoDisplay takes).",
    ])
    data["worth"] = worth
    data["frames"] = frames
    data["icon"] = list(icon)
    data["scenarios"] = scenarios
    return data


# ------------------------------------------------------------ the talk's text

COMMTEXT_PROGRAM = os.path.join(HERE, "golden", "commtext.c")


def record_commtext(uqm, exe, strings_exe, package):
    """How UQM's comm.c lays out a talk's text (golden/commtext.c): each
    race's lines paged by trackplayer.c's SplitSubPages and drawn as its
    subtitles (add_text) in its font, sets of its lines as the player's
    replies (RefreshResponses, the player's font) and a summary of its
    lines (DoConvSummary, the tiny font). The lines and fonts are content:
    the recording names the lines by their place in the conversation and
    keeps only what is drawn where, and the pages' hashes and times."""
    import hashlib
    import zipfile
    with open(package, "rb") as f:
        if hashlib.sha256(f.read()).hexdigest() != CONTENT_SHA256:
            sys.exit(f"{package} isn't the official UQM 0.8.0 content package")
    with open(os.path.join(REPO, "data", "conversations.json")) as f:
        scripts = json.load(f)["scripts"]
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        names = z.namelist()

        def font_dir(key):
            path = index[key][1]
            for n in names:
                if n.startswith(path + "/") and not n.endswith("/"):
                    local = os.path.join(tmp, n)
                    os.makedirs(os.path.dirname(local), exist_ok=True)
                    with open(local, "wb") as f:
                        f.write(z.read(n))
            return os.path.join(tmp, path)

        commands, meta = [], []

        def cmd(line, what):
            commands.append(line)
            meta.append(what)

        cmd(f"font player {font_dir('font.player')}", None)
        cmd(f"font tiny {font_dir('font.tiny')}", None)
        races = []
        for sc in scripts:
            d = sc["description"]
            if d["font"] not in index:
                continue
            fname = sc["script"]
            cmd(f"font {fname} {font_dir(d['font'])}", None)
            fore, back = d["text_fore"][:3], d["text_back"][:3]
            cmd(f"alien {fname} {d['text_width'] or 238} {d['text_baseline'][0]} {d['text_baseline'][1]} "
                f"{d['text_align']} {d['text_valign']} {' '.join(map(str, fore + back))}", None)
            conv = os.path.join(tmp, f"{fname}.txt")
            with open(conv, "wb") as f:
                f.write(z.read(index[d["phrases"]][1].split(":")[0]))
            entries = read_conversations(strings_exe, [conv])[0]
            texts = [(i + 1, bytes.fromhex(t)[:-1]) for i, (n, length, t, c) in enumerate(entries) if length > 1]
            chosen = texts[::max(1, len(texts) // 20)][:20]
            chosen += sorted(texts, key=lambda x: -len(x[1]))[:4]
            race = {"script": fname, "lines": [], "replies": [], "summary": None}
            races.append(race)
            for number, text in chosen:
                line = {"phrase": number, "pages": None, "drawn": []}
                race["lines"].append(line)
                cmd("pages " + text.hex(), ("pages", line))
            # Replies: sets of its lines, the lit one and the first shown.
            rtexts = [t for _, t in texts]
            for k, (n, top, cur) in enumerate(((3, 0, 1), (5, 0, 4), (8, 0, 0), (8, 2, 3), (8, 1, 7))):
                pick = [texts[(k * 7 + j * 3) % len(texts)] for j in range(n)]
                reply = {"phrases": [p for p, _ in pick], "top": top, "cur": cur, "drawn": None}
                race["replies"].append(reply)
                cmd(f"replies player {top} {cur} " + " ".join(t.hex() for _, t in pick), ("replies", reply))
            # Feedback: a few of its lines as the reply chosen.
            race["feedback"] = []
            for p, t in (texts[0], texts[len(texts) // 2], max(texts, key=lambda x: len(x[1]))):
                fb = {"phrase": p, "drawn": None}
                race["feedback"].append(fb)
                cmd("feedback player " + t.hex(), ("replies", fb))
            race["summary_phrases"] = [p for p, _ in texts[:30]]
            cmd("summary tiny " + " ".join(t.hex() for _, t in texts[:30]), ("summary", race))
        # Run once to split the pages, then lay each page out.
        out = subprocess.run([exe], input="\n".join(commands) + "\n", capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"commtext: {out.stderr[-500:]}")
        results = [json.loads(x) for x in out.stdout.splitlines()]
        if len(results) != len(commands):
            raise RuntimeError(f"commtext: {len(results)} results for {len(commands)} commands")
        subs, sub_meta = [], []
        for c, what, r in zip(commands, meta, results):
            if what is None:
                if c.startswith("font") or c.startswith("alien"):
                    subs.append(c)
                    sub_meta.append(None)
                continue
            kind, obj = what
            if kind == "pages":
                obj["pages"] = [[hashlib.sha256(bytes.fromhex(p)).hexdigest()[:16], t] for p, t in r["split"]]
                for p, _ in r["split"]:
                    subs.append("sub " + p)
                    sub_meta.append(obj)
            elif kind == "replies":
                obj["drawn"] = r["draw"]
            else:
                obj["summary"] = r["pages"]
        out = subprocess.run([exe], input="\n".join(subs) + "\n", capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"commtext subtitles: {out.stderr[-500:]}")
        for what, line in zip(sub_meta, out.stdout.splitlines()):
            if what is not None:
                what["drawn"].append(json.loads(line)["draw"])
    data = header(uqm, ["tools/extract/golden/commtext.c", "src/uqm/comm.c", "src/libs/sound/trackplayer.c",
                        "src/libs/graphics/font.c"], [
        "How UQM 0.8.0's own comm.c lays out a talk's text, with its own "
        "font loading and TextRect (tools/extract/golden/commtext.c), for "
        "each race in data/conversations.json with its font, text width, "
        "baseline, alignments and colours from its description, from the "
        f"official content package (SHA-256 {CONTENT_SHA256}).",
        "lines: some of the race's conversation entries (phrase: the "
        "entry's number from 1), each paged by SplitSubPages (pages: the "
        "page's text's SHA-256, first 16 hex digits, and its time in "
        "milliseconds) and each page drawn as a subtitle by add_text (1, "
        "...): drawn, a list a page.",
        "replies: sets of entries as the player's replies (RefreshResponses "
        "in the player's font, font.player), the first shown and the lit "
        "one; summary: the race's first 30 entries as the subtitles of "
        "DoConvSummary (the tiny font, font.tiny), each page's drawing.",
        "feedback: some entries as the reply the player chose "
        "(FeedbackPlayerPhrase, the player's font).",
        "Drawing calls as commtext.c says: a line names its text by its "
        "place (the page; the reply in the set; the subtitle in the "
        "summary's list; the reply chosen) and its byte offset; the bullet, "
        "MORE and FEEDBACK (the game string \"(In response to your "
        "statement)\") are themselves, in hex.",
    ])
    data["races"] = races
    return data


def record_binary_tables(uqm, exe, package):
    """Every BINTAB resource in the official content package, as UQM's own
    _GetBinaryTableData reads it. Content, so only lengths and hashes."""
    import hashlib
    import zipfile
    with open(package, "rb") as f:
        if hashlib.sha256(f.read()).hexdigest() != CONTENT_SHA256:
            sys.exit(f"{package} isn't the official UQM 0.8.0 content package")
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        entries = [(key, path) for key, (kind, path)
                   in read_resource_index(exe, index_file).items()
                   if kind == "BINTAB"]
        files = []
        for i, (key, path) in enumerate(entries):
            local = os.path.join(tmp, f"{i}.bin")
            with open(local, "wb") as f:
                f.write(z.read(path))
            files.append(local)
        out = subprocess.run([exe, "bintab", *files], capture_output=True,
                             text=True, check=True).stdout
    tables = {}
    for (key, path), strings in zip(entries, json.loads(out)):
        tables[key] = {
            "path": path,
            "entries": [[length, hashlib.sha256(bytes.fromhex(data)).hexdigest()]
                        for length, data in strings],
        }
    data = header(uqm, ["tools/extract/golden/strings.c",
                        "src/libs/strings/getstr.c",
                        "src/libs/resource/loadres.c"], [
        "Every binary table (BINTAB resource) of the official UQM 0.8.0 "
        f"content package (SHA-256 {CONTENT_SHA256}), as UQM's own "
        "_GetBinaryTableData reads it: each entry is [length, SHA-256 of "
        "its bytes]. Content (CC BY-NC-SA 2.5), so only hashes.",
    ])
    data["tables"] = tables
    return data


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("--uqm", required=True,
                    help="path to the sc2 directory of UQM 0.8.0")
    ap.add_argument("--out", default=os.path.join(REPO, "tests", "golden"))
    ap.add_argument("--content", help="the official uqm-0.8.0-content.uqm; "
                    "with it, also record what UQM reads from the content "
                    "(as hashes)")
    ap.add_argument("--only", help="record only these, comma-separated: "
                    "a file's name without .json (planet-surfaces, "
                    "planetside and contact for the files those write)")
    args = ap.parse_args()
    if shutil.which("gcc") is None:
        sys.exit("gcc is required")
    build_dir = tempfile.mkdtemp(prefix="crateris-golden-")
    try:
        uqm = extract_uqm.Uqm(args.uqm, build_dir)
        flight = os.path.join(build_dir, "flight")
        math = os.path.join(build_dir, "math")
        build(uqm, os.path.join(HERE, "golden", "flight.c"), FLIGHT_LINK,
              flight)
        build(uqm, os.path.join(HERE, "golden", "math.c"), MATH_LINK, math)
        lander = os.path.join(build_dir, "lander")
        build(uqm, os.path.join(HERE, "golden", "lander.c"), LANDER_LINK,
              lander, extra=[LANDER_STUBS])
        systems = os.path.join(build_dir, "systems")
        build(uqm, os.path.join(HERE, "golden", "systems.c"),
              systems_link(uqm), systems, extra=[SYSTEMS_STUBS],
              libs=["-lm"])   # the rotating planet's sqrt, atan2, acos...
        fonts = os.path.join(build_dir, "fonts")
        images = os.path.join(build_dir, "images")
        if args.content:
            build(uqm, os.path.join(HERE, "golden", "images.c"), IMAGES_LINK,
                  images, extra=[SDL_STANDIN], libs=["-lpng"],
                  includes=[SDL_INCLUDE])
            build(uqm, os.path.join(HERE, "golden", "fonts.c"), FONTS_LINK,
                  fonts, extra=[SDL_STANDIN], libs=["-lpng"],
                  includes=[SDL_INCLUDE])
        commtext = os.path.join(build_dir, "commtext")
        if args.content:
            build(uqm, COMMTEXT_PROGRAM, FONTS_LINK, commtext, extra=[SDL_STANDIN], libs=["-lpng"],
                  includes=[SDL_INCLUDE, os.path.join(HERE, "golden")])
        screens = os.path.join(build_dir, "screens")
        if args.content:
            build(uqm, SCREENS_PROGRAM, SCREENS_LINK, screens, extra=[SCREENS_STUBS], libs=["-lm"])
        encounter = os.path.join(build_dir, "encounter")
        if args.content:
            build(uqm, ENCOUNTER_PROGRAM, ENCOUNTER_LINK, encounter, extra=[SCREENS_STUBS],
                  libs=["-lm", "-Wl,--wrap=AnyButtonPress", "-Wl,--wrap=GetClusterName"],
                  includes=[os.path.join(HERE, "golden")])
        report = os.path.join(build_dir, "report")
        if args.content:
            build(uqm, REPORT_PROGRAM, REPORT_LINK, report)
        sell = os.path.join(build_dir, "sell")
        if args.content:
            build(uqm, SELL_PROGRAM, SELL_LINK, sell, libs=["-lm", "-Wl,--wrap=ClearSISRect"])
        cmap = os.path.join(build_dir, "cmap")
        build(uqm, CMAP_PROGRAM, [], cmap)
        clock = os.path.join(build_dir, "clock")
        build(uqm, CLOCK_PROGRAM, CLOCK_LINK, clock)
        commanim = os.path.join(build_dir, "commanim")
        build(uqm, COMMANIM_PROGRAM, COMMANIM_LINK, commanim)
        devices = os.path.join(build_dir, "devices")
        build(uqm, DEVICES_PROGRAM, DEVICES_LINK, devices)
        outfit = os.path.join(build_dir, "outfit")
        build(uqm, OUTFIT_PROGRAM, OUTFIT_LINK, outfit, extra=[OUTFIT_STUBS], libs=["-lm"])
        battle = os.path.join(build_dir, "battle")
        build(uqm, os.path.join(HERE, "golden", "battle.c"), BATTLE_LINK, battle,
              extra=[BATTLE_STUBS])
        hyperspace = os.path.join(build_dir, "hyperspace")
        if args.content:
            build(uqm, HYPERSPACE_PROGRAM, HYPERSPACE_LINK, hyperspace, extra=[HYPERSPACE_STUBS])
        groups = os.path.join(build_dir, "groups")
        if args.content:
            build(uqm, GROUPS_PROGRAM, groups_link(uqm), groups, extra=[GROUPS_STUBS], libs=["-lm"])
        battle_draw = os.path.join(build_dir, "battledraw")
        build(uqm, os.path.join(HERE, "golden", "battle.c"), BATTLE_LINK, battle_draw,
              extra=[BATTLE_STUBS], defines=["RECORD_DRAW"])
        strings = os.path.join(build_dir, "strings")
        build(uqm, os.path.join(HERE, "golden", "strings.c"), STRINGS_LINK,
              strings, extra=[UIO_FILES], libs=["-lm"])   # the name index's hash table: ceil, floor
        os.makedirs(args.out, exist_ok=True)
        # Each job: the file it writes (or, for those that write several,
        # a name for them all) and how to record it.
        jobs = [("hyperspace-flight", lambda: record_flight(uqm, flight)),
                ("uqm-math", lambda: record_math(uqm, math)),
                ("lander", lambda: record_lander(uqm, lander)),
                ("star-systems", lambda: record_systems(uqm, systems)),
                ("world-surfaces", lambda: record_surfaces(uqm, systems)),
                ("ip-geometry", lambda: record_ip(uqm, systems)),
                ("system-layout", lambda: record_layout(uqm, systems)),
                ("system-sprites", lambda: record_sprites(uqm, systems)),
                ("planet-surfaces", lambda: record_planet_surfaces(uqm, systems, strings, images, args.content)),
                ("planetside", lambda: record_planetside(uqm, systems, strings, images, args.content)),
                ("landing", lambda: record_landing(uqm, systems)),
                ("outfit", lambda: record_outfit(uqm, outfit)),
                ("devices", lambda: record_devices(uqm, devices)),
                ("commanim", lambda: record_commanim(uqm, commanim)),
                ("cmap", lambda: record_cmap(uqm, cmap)),
                ("clock-events", lambda: record_clock(uqm, clock)),
                ("string-numbering", lambda: record_string_numbering(uqm, strings)),
                ("conversation-cases", lambda: record_conversation_cases(uqm, strings)),
                ("string-table-cases", lambda: record_string_table_cases(uqm, strings)),
                ("resource-index-cases", lambda: record_resource_index_cases(uqm, strings))]
        if args.content:
            jobs += [("string-tables", lambda: record_string_tables(uqm, strings, args.content)),
                     ("names", lambda: record_names(uqm, systems, strings, args.content)),
                     ("fonts", lambda: record_fonts(uqm, fonts, strings, args.content)),
                     ("images", lambda: record_images(uqm, images, args.content)),
                     ("conversations", lambda: record_conversations(uqm, strings, args.content)),
                     ("comm-graphics", lambda: record_comm_graphics(uqm, strings, images, args.content)),
                     ("binary-tables", lambda: record_binary_tables(uqm, strings, args.content)),
                     ("talks", lambda: record_talks(uqm, strings, args.content)),
                     ("commtext", lambda: record_commtext(uqm, commtext, strings, args.content)),
                     ("screens", lambda: record_screens(uqm, screens, outfit, strings, args.content)),
                     ("encounter-screens", lambda: record_encounter(uqm, encounter, strings, args.content)),
                     ("report", lambda: record_report(uqm, report, strings, args.content)),
                     ("selling", lambda: record_selling(uqm, sell, strings, args.content)),
                     ("contact", lambda: record_contact(uqm, systems, strings, images, args.content)),
                     ("hyperspace-encounters", lambda: record_hyperspace(uqm, hyperspace, strings, images,
                                                                         args.content)),
                     ("system-groups", lambda: record_groups(uqm, groups, strings, images, args.content)),
                     ("battle-masks", lambda: record_battle_masks(uqm, strings, images, args.content)),
                     ("battle", lambda: record_battle(uqm, battle, strings, images, args.content)),
                     ("battle-draw", lambda: record_battle_draw(uqm, battle, battle_draw, strings, images, args.content)),
                     ("battle-ships", lambda: record_battle_ships(uqm, battle, strings, images, args.content)),
                     ("battle-ai", lambda: record_battle_ai(uqm, battle, strings, images, args.content)),
                     ("battle-races", lambda: record_battle_races(uqm, battle, strings, images, args.content)),
                     ("battle-races-draw", lambda: record_battle_races_draw(uqm, battle_draw, strings, images,
                                                                            args.content)),
                     ("lander-pickups", lambda: record_lander_pickups(uqm, lander, strings, images, args.content)),
                     ("lander-creatures", lambda: record_lander_creatures(uqm, lander, strings, images, args.content)),
                     ("lander-hazards", lambda: record_lander_hazards(uqm, lander, strings, images, args.content)),
                     ("lander-drawing", lambda: record_lander_drawing(uqm, lander, strings, images, args.content))]
        only = set(args.only.split(",")) if args.only else None
        if only and only - {name for name, _ in jobs}:
            sys.exit(f"no such recording: {', '.join(sorted(only - {name for name, _ in jobs}))}")
        for job, record in jobs:
            if only and job not in only:
                continue
            data = record()
            # A job that writes several files gives {file: data}.
            for name, data in (data.items() if "_notice" not in data else [(job, data)]):
                path = os.path.join(args.out, name + ".json")
                with open(path, "w") as f:
                    json.dump(data, f, separators=(",", ":"))
                    f.write("\n")
                print(f"wrote {path}")
    finally:
        shutil.rmtree(build_dir, ignore_errors=True)


if __name__ == "__main__":
    main()
