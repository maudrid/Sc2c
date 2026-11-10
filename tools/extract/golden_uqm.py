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
        "order IP_frame runs them (see tools/extract/golden/systems.c). "
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


def battle_sheets():
    """The sheets a battle tests for contact, the big size only (farray[0],
    InitIntersectFrame in collide.h): the flagship's and the Ilwrath's
    ship and weapon (and special, for a ship that has one) as their
    RACE_DESCs name them (data/ships.json), the arena's asteroids, blasts
    and explosions (InitSpace, init.c), stars_in_space, whose last frame
    is floating crew, lasers and ion trails (graphics.stars), and every
    planet the battle can have (load_gravity_well, cons_res.c: each
    planet type's and a shielded world's, data/planet-types.json). The
    sheet numbers only name the images program's files."""
    with open(os.path.join(REPO, "data", "ships.json")) as f:
        ships = {s["id"]: s for s in json.load(f)["ships"]}
    with open(os.path.join(REPO, "data", "planet-types.json")) as f:
        planets = json.load(f)
    ids = []
    for ship in BATTLE_SHIPS:
        g = ships[ship]["graphics"]
        ids += [g[k][0] for k in ("ship", "weapon", "special") if g[k][0]]
    ids += ["graphics.asteroid.large", "graphics.blast.large",
            "graphics.boom.large", "graphics.stars"]
    ids += [t["battle_sprites"][0] for t in planets["planet_types"]]
    ids.append(planets["shielded_battle_sprites"][0])
    return list(enumerate(ids))


def record_battle_masks(uqm, strings_exe, images_exe, package):
    """The collision masks of the battle's sheets (battle_sheets)."""
    with tempfile.TemporaryDirectory() as tmp:
        _, hashes = write_masks(strings_exe, images_exe, package, tmp,
                                battle_sheets())
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
        f"{', '.join(BATTLE_SHIPS)}'s ship, weapon and special sheets as "
        "data/ships.json names them, the asteroids, blasts and explosions "
        "(InitSpace), graphics.stars (floating crew, lasers and ion "
        "trails are its last frame) and every planet's battle sprite "
        "(load_gravity_well).",
        "By resource id, each frame is [width, height, hot spot x, hot "
        "spot y, SHA-256 of the mask: a byte a pixel, row by row, 1 where "
        "it can touch]. Content, so hashes only.",
    ])
    data["sheets"] = hashes
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
               "uqm/ships/sis_ship/sis_ship.c", "uqm/ships/ilwrath/ilwrath.c"]
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

# An element as the battle program writes it.
# A frame as it writes it: its ships, elements and draws.
F_ZOOM, F_SHIPS, F_ELEMENTS, F_DRAWS = 3, 7, 8, 9
E_FLAGS, E_LIFE, E_CREW, E_MASS = 0, 1, 2, 3
E_CX, E_CY, E_SHEET, E_FRAME, E_PLAYER, E_PRIM = 15, 16, 19, 20, 21, 22
E_BLACK, E_SLOT, E_TARGET, E_COLOR = 23, 24, 25, 26
PLAYER_SHIP_FLAG, FINITE_LIFE_FLAG, CREW_OBJECT_FLAG = 1 << 2, 1 << 10, 1 << 14


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
        parts.append(0)
        g = ships[ship["ship"]]["graphics"]
        parts += [numbers[r] for r in g["ship"] + g["weapon"]]
        parts += [ship["crew"], ship["max_crew"], ship["energy"], ship["max_energy"], ship["flags"]]
        parts += [ship["characteristics"][c] for c in CHARACTERISTICS]
        parts += [ship[k] for k in ("weapons", "life", "speed", "hits", "damage", "offset",
                                    "track_wait", "laser_range")]
    parts += scenario.get("fleet", [1, 1])
    parts.append(len(scenario["steps"]))
    for step in scenario["steps"]:
        parts += list(step)
    parts.append(len(scenario.get("events", [])))
    for event in scenario.get("events", []):
        parts += list(event)
    return " ".join(str(p) for p in parts)


class BattleProgram:
    """The battle program with its sheets and masks, run on scenarios."""

    def __init__(self, exe, sheets_file, masks):
        self.exe, self.sheets_file, self.masks = exe, sheets_file, masks
        self.ids = battle_program_sheets()
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
        res = subprocess.run([self.exe, self.sheets_file, self.masks], input="\n".join(lines) + "\n",
                             capture_output=True, text=True)
        if res.returncode < 0:
            # UQM's own code crashing: AbandonShip events emptying a ship (the
            # Syreen's song, UQM's one caller, leaves one crew) can leave
            # floating crew whose ship is freed under them (crew_preprocess).
            raise RuntimeError(f"the battle program crashed (signal {-res.returncode}); possibly an "
                               "AbandonShip event emptying a ship (clamp_events keeps one crew aboard). "
                               f"{res.stderr.strip()}")
        if res.returncode:
            raise RuntimeError(f"the battle program refused a battle: {res.stderr.strip()}")
        out = res.stdout
        battles = json.loads("[" + out.replace("}\n{", "},\n{") + "]")
        if len(battles) != len(scenarios):
            raise RuntimeError("the battle program lost a battle")
        places = {}
        for battle in battles:
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
    cut to the ship's crew then, less one, and dropped if none can go."""
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
        if ship and fighting(battle["frames"][frame], target) and left > 1:
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
                    "ended": battle["ended"], "stars": battle["stars"], "frames": frames})
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
                or by_name[name]["crew_enlisted"] != c[-1] - 1 or not c[-1] \
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
                    "stars": battle["stars"], "frames": frames})
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
COMM_LINK = STRINGS_LINK + ["uqm/commglue.c", "uqm/sis.c", "libs/math/random.c"]
COMM_PROGRAM = os.path.join(HERE, "golden", "comm.c")

# sis.h's module numbers, for the ship's module slots (EMPTY_SLOT + 2 is
# an empty module slot).
MODULES = {"PLANET_LANDER": 0, "FUSION_THRUSTER": 1, "TURNING_JETS": 2,
           "CREW_POD": 3, "STORAGE_BAY": 4, "FUEL_TANK": 5,
           "HIGHEFF_FUELSYS": 6, "DYNAMO_UNIT": 7, "SHIVA_FURNACE": 8,
           "GUN_WEAPON": 9, "BLASTER_WEAPON": 10, "CANNON_WEAPON": 11,
           "TRACKING_SYSTEM": 12, "ANTIMISSILE_DEFENSE": 13,
           "BOMB_MODULE_0": 14, "EMPTY": 22}
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
    """The Ilwrath ship at Earth (ilwrathc.c's PROBE_ILWRATH_ENCOUNTER
    branch, the only one stage 3a ports): its replies, and how many times
    the player has asked the Ilwrath for news before (which only the
    branches met in space read)."""
    return [{"name": "at-earth", "state": {"PROBE_ILWRATH_ENCOUNTER": 1}, "ship": talk_ship()},
            {"name": "at-earth-info-asked", "state": {"PROBE_ILWRATH_ENCOUNTER": 1, "ILWRATH_INFO": 3},
             "ship": talk_ship()}]


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


COMM_SCRIPTS = {"comandr": commander_starts, "ilwrath": ilwrath_starts, "starbas": starbase_starts}
# A script's file where it isn't named after its folder.
COMM_FILES = {"ilwrath": "ilwrathc.c"}
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


def walk_talk(exe, files, music, start, fields, state_bytes, functions, element_worth):
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
        f"{escorts[0]} {escorts[1]}",
        " ".join(music)])
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
    every condition each reads."""
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
    talks = {}
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
        names = set(z.namelist())
        # The music that loads: in the index, with its file in the package.
        music = sorted(k for k, (kind, path) in index.items()
                       if kind == "MUSICRES" and path in names)
        for script, starts in COMM_SCRIPTS.items():
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
            talks[script] = [{**start, "steps": walk_talk(exe, files, music, start, fields, state_bytes, functions,
                                                                  element_worth)}
                             for start in starts()]
    data = header(uqm, ["tools/extract/golden/comm.c", "src/uqm/commglue.c", "src/uqm/sis.c",
                        "src/libs/math/random.c", "src/libs/strings/getstr.c"]
                  + [f"src/uqm/comm/{s}/{COMM_FILES.get(s, s + '.c')}" for s in COMM_SCRIPTS], [
        "Every path through each ported conversation script, recorded by "
        "running UQM's own script with UQM's commglue.c, sis.c and string "
        "tables, and the rest of the talk engine stubbed "
        "(tools/extract/golden/comm.c says what each event is). Each start "
        "is a story state (fields not given are 0), a flagship (crew, fuel "
        "in 1/100 units, landers, resource units, element amounts, module "
        "slots, the drive and jet slots with a thruster or jets, captain's "
        "and ship's names), the date (a new game's where not given), the "
        "seed of the scripts' random numbers (TFB_SeedRandom; 12345 where "
        "not given), the races' standings where not a new game's "
        "(alliances: good, bad or dead), the escort fleet's worth and room "
        "for more ships (escorts; none where not given), and its steps: the replies "
        "chosen from the start, each the place in the replies offered, and "
        "the events after the last of them up to the next replies offered "
        "or the talk's end. An offer marked seen reaches a talk state "
        "already walked from this start, so the walk goes no further; a "
        "start with follow walks only the replies calling those functions, "
        "and one with prefix starts its walk after those replies (the rest "
        "is walked from other starts). Voices are off (usingSpeech false); "
        "selling minerals runs as if a button were pressed at once.",
        "Texts are content (CC BY-NC-SA 2.5), so only the first 16 hex "
        "digits of each line's SHA-256 are here; set names the field "
        "written (data/game-state.json); offer gives each reply's phrase "
        "number, the script's function it calls and its text's hash.",
    ])
    data["talks"] = talks
    return data


# ------------------------------------------------------------ outfitting

# The outfitting recording program (golden/outfit.c): UQM's outfit.c and
# shipyard.c included, sis.c and menu.c linked, the rest stubbed.
OUTFIT_PROGRAM = os.path.join(HERE, "golden", "outfit.c")
OUTFIT_LINK = ["uqm/sis.c", "uqm/menu.c"]
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
    data = header(uqm, ["tools/extract/golden/outfit.c", "src/uqm/outfit.c", "src/uqm/shipyard.c",
                        "src/uqm/sis.c", "src/uqm/menu.c"], [
        "Outfitting and the shipyard's crew, recorded by running UQM 0.8.0's "
        "own DoOutfit, DoInstallModule and ChangeFuelQuantity (outfit.c) and "
        "DMS_ModifyCrew and CrewTransaction (shipyard.c) with sis.c and "
        "menu.c, the drawing stubbed (tools/extract/golden/outfit.c). Code "
        "only, no content.",
        "Each scenario: a story state (fields not given are 0), the module "
        "costs where not a new game's (before MODULE_COST_SCALE), FuelCost, "
        "CrewCost, whether the Shofixti are allied, a flagship as in "
        "talks.json, and its steps: outfit opens outfitting; u, d, l, r, s, "
        "c, pu and pd are keys (up, down, left, right, select, cancel, page "
        "up and down); hire and dismiss the shipyard's up and down on the "
        "flagship's crew. results: after each step, whether it was refused "
        "(MENU_SOUND_FAILURE), whether outfitting was left, the cursor "
        "(CurState and delta_item: UQM's numbers), the ship (crew, fuel in "
        "1/100 units, landers, resource units), its module, drive and jet "
        "slots (sis.h's numbers; each only where it changed, and after the "
        "first step), and the crew price and crew bought.",
    ])
    data["scenarios"] = scenarios
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
SCREENS_LINK = ["uqm/sis.c", "uqm/menu.c", "uqm/border.c", "uqm/util.c", "libs/graphics/pixmap.c"]
SCREENS_STUBS = os.path.join(HERE, "golden", "screens_stubs.c")
SCREENS_FRAMES = ["graphics.starbase", "graphics.modulesmenu", "graphics.outfit", "graphics.playmenu",
                  "graphics.shipyard", "graphics.outfitmodules", "graphics.fontgradient",
                  "graphics.flagshipstatus", "graphics.blueprints"]
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
    return out


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
        for res in SCREENS_FRAMES:
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
        if steps[0] not in ("outfit", "shipyard") and not steps[0].startswith("base:"):
            raise RuntimeError(f"screens {sc['name']}: starts with {steps[0]}")
        if steps[0] == "outfit":
            steps, _ = run_outfit(outfit_exe, {**sc, "shofixti": sc.get("shofixti", False)}, fields, state_bytes,
                                  prices)
        ship = sc["ship"]
        head = outfit_head(sc, fields, state_bytes, prices) + [
            f"{ship['ship']}|{ship['captain']}", " ".join(counts), " ".join(rects), SCREENS_LEADING, str(cmaps)]
        out = subprocess.run([exe], input="\n".join(head + [" ".join(steps)]) + "\n", capture_output=True, text=True)
        if out.returncode:
            raise RuntimeError(f"screens {sc['name']}: {out.stdout[-300:]}{out.stderr}")
        lines = [json.loads(x) for x in out.stdout.splitlines()]
        if len(lines) != len(steps):
            raise RuntimeError(f"screens {sc['name']}: {len(lines)} of {len(steps)} steps")
        scenarios.append({k: v for k, v in sc.items() if k not in ("steps",)} | {
            "steps": steps, "drawn": [x["draw"] for x in lines], "flash": [x["flash"] for x in lines],
            "cmap": [x["cmap"] for x in lines]})
    data = header(uqm, ["tools/extract/golden/screens.c", "tools/extract/golden/screens_stubs.c",
                        "src/uqm/starbase.c", "src/uqm/outfit.c", "src/uqm/shipyard.c", "src/uqm/sis.c",
                        "src/uqm/menu.c", "src/uqm/border.c", "src/uqm/util.c"], [
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
        "then the screen, as DoInput does) has the flagship alone: fleet "
        "ships come with stage 4.",
        "frames: each resource's number of frames; hangar_maps: how many "
        "colour maps colortable.hangar has; rects: each frame's "
        "GetFrameRect, [x, y, width, height] (its corner less the hot "
        "spot, and its size), read from the content.",
    ])
    data["frames"] = dict(zip(counts[::2], map(int, counts[1::2])))
    data["hangar_maps"] = cmaps
    data["rects"] = {res: [] for res in SCREENS_FRAMES}
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
        report = os.path.join(build_dir, "report")
        if args.content:
            build(uqm, REPORT_PROGRAM, REPORT_LINK, report)
        sell = os.path.join(build_dir, "sell")
        if args.content:
            build(uqm, SELL_PROGRAM, SELL_LINK, sell, libs=["-lm", "-Wl,--wrap=ClearSISRect"])
        cmap = os.path.join(build_dir, "cmap")
        build(uqm, CMAP_PROGRAM, [], cmap)
        commanim = os.path.join(build_dir, "commanim")
        build(uqm, COMMANIM_PROGRAM, COMMANIM_LINK, commanim)
        devices = os.path.join(build_dir, "devices")
        build(uqm, DEVICES_PROGRAM, DEVICES_LINK, devices)
        outfit = os.path.join(build_dir, "outfit")
        build(uqm, OUTFIT_PROGRAM, OUTFIT_LINK, outfit, extra=[OUTFIT_STUBS], libs=["-lm"])
        battle = os.path.join(build_dir, "battle")
        build(uqm, os.path.join(HERE, "golden", "battle.c"), BATTLE_LINK, battle,
              extra=[BATTLE_STUBS])
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
                     ("report", lambda: record_report(uqm, report, strings, args.content)),
                     ("selling", lambda: record_selling(uqm, sell, strings, args.content)),
                     ("contact", lambda: record_contact(uqm, systems, strings, images, args.content)),
                     ("battle-masks", lambda: record_battle_masks(uqm, strings, images, args.content)),
                     ("battle", lambda: record_battle(uqm, battle, strings, images, args.content)),
                     ("battle-ships", lambda: record_battle_ships(uqm, battle, strings, images, args.content)),
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
