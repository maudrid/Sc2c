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
                if result[0] == 2:
                    # True colour without alpha (orbit background frame 21,
                    # never tested for contact): its mask is unknown, 2s,
                    # and the systems program stops if it's ever tested.
                    _, _, w, h = result
                    bits, digest = bytes([2]) * (w * h), None
                else:
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
                        "src/libs/graphics/sdl/canvas.c"], [
        "The collision masks of the sprite sheets UQM tests for contact, "
        f"from the official content package (SHA-256 {CONTENT_SHA256}): "
        "each frame as _GetCelData loads it, read by UQM's own PNG loader, "
        "with its colour key set as process_image sets it, and a pixel able "
        "to touch as TFB_DrawCanvas_Intersect decides it. By resource id, "
        "each frame is [width, height, hot spot x, hot spot y, SHA-256 of "
        "the mask: a byte a pixel, row by row, 1 where it can touch]. A "
        "true colour frame without alpha (only the orbit background's last, "
        "never tested for contact) has null: UQM converts it to the "
        "screen's format, which isn't modelled. Content, so hashes only.",
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
           "GUN_WEAPON": 9, "EMPTY": 22}
# A new game's module slots (InitGameStructures, data/new-game.json).
NEW_GAME_MODULES = ["EMPTY", "FUEL_TANK", "CREW_POD"] + ["EMPTY"] * 5 + \
    ["STORAGE_BAY"] + ["EMPTY"] * 6 + ["GUN_WEAPON"]
RADIOACTIVE = 6   # elemdata.h's category


def talk_ship(crew=50, fuel=1000, landers=1, ru=0, elements=None,
              modules=None, captain="Zelnick", ship="Vindicator"):
    """A flagship for a talk: a new game's unless told otherwise."""
    return {"crew": crew, "fuel": fuel, "landers": landers, "ru": ru,
            "elements": elements or [0] * 8,
            "modules": modules or NEW_GAME_MODULES,
            "captain": captain, "ship": ship}


def commander_starts():
    """Starting states for the commander's talk (comandr.c), chosen to
    reach every condition it reads: which visit it is (Intro: the Ilwrath
    come, a visit before, the radioactives brought, the base destroyed),
    radioactives in the hold, no lander (with the Chmmr's bomb not yet
    started, and one lost before), fuel under 2 units (given before or
    not), the base promised or asked about before, a full crew or not, a
    ship without fuel tanks (the gift capped at the reserve), and the
    names spliced into the replies."""
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


COMM_SCRIPTS = {"comandr": commander_starts}
# The scripts' random seed where a start doesn't give one.
TALK_SEED = 12345


def build_comm(uqm, script, init, out):
    path = os.path.join(uqm.uqm, "comm", script, script + ".c")
    build(uqm, COMM_PROGRAM, COMM_LINK, out, extra=[UIO_FILES],
          libs=["-lm", "-Wl,--wrap=DeltaSISGauges", "-Wl,--wrap=DrawLanders",
                "-Wl,--wrap=TFB_Random"],
          includes=[os.path.join(HERE, "golden")],
          defines=[f'SCRIPT_FILE="{path}"', f"SCRIPT_INIT={init}"])


def walk_talk(exe, files, start, fields, state_bytes, functions):
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
    head = "\n".join([
        *files, state.hex(),
        " ".join(map(str, [ship["crew"], ship["fuel"], ship["landers"], ship["ru"],
                           *ship["elements"], *(MODULES[m] for m in ship["modules"])])),
        ship["captain"].encode().hex(), ship["ship"].encode().hex(),
        str(start.get("seed", TALK_SEED))])
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

    steps, seen, todo = [], set(), [[]]
    while todo:
        replies = todo.pop()
        events = run(replies)
        # The events since the last reply (all of them for the first step).
        if replies:
            events = events[max(i for i, e in enumerate(events) if "reply" in e):]
        last = events[-1]
        if "offer" in last:
            key = last.pop("key")
            if key in seen:
                last["seen"] = True
            else:
                seen.add(key)
                todo.extend(replies + [i] for i in reversed(range(len(last["offer"]))))
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
    talks = {}
    with zipfile.ZipFile(package) as z, tempfile.TemporaryDirectory() as tmp:
        index_file = os.path.join(tmp, "uqm.rmp")
        with open(index_file, "wb") as f:
            f.write(z.read("uqm.rmp"))
        index = read_resource_index(strings_exe, index_file)
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
            talks[script] = [{**start, "steps": walk_talk(exe, files, start, fields, state_bytes, functions)}
                             for start in starts()]
    data = header(uqm, ["tools/extract/golden/comm.c", "src/uqm/commglue.c", "src/uqm/sis.c",
                        "src/libs/math/random.c", "src/libs/strings/getstr.c"]
                  + [f"src/uqm/comm/{s}/{s}.c" for s in COMM_SCRIPTS], [
        "Every path through each ported conversation script, recorded by "
        "running UQM's own script with UQM's commglue.c, sis.c and string "
        "tables, and the rest of the talk engine stubbed "
        "(tools/extract/golden/comm.c says what each event is). Each start "
        "is a story state (fields not given are 0), a flagship (crew, fuel "
        "in 1/100 units, landers, resource units, element amounts, module "
        "slots, captain's and ship's names), the seed of the scripts' random "
        "numbers (TFB_SeedRandom; 12345 where not given) and its steps: the replies "
        "chosen from the start, each the place in the replies offered, and "
        "the events after the last of them up to the next replies offered "
        "or the talk's end. An offer marked seen reaches a talk state "
        "already walked from this start, so the walk goes no further.",
        "Texts are content (CC BY-NC-SA 2.5), so only the first 16 hex "
        "digits of each line's SHA-256 are here; set names the field "
        "written (data/game-state.json); offer gives each reply's phrase "
        "number, the script's function it calls and its text's hash.",
    ])
    data["talks"] = talks
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
        strings = os.path.join(build_dir, "strings")
        build(uqm, os.path.join(HERE, "golden", "strings.c"), STRINGS_LINK,
              strings, extra=[UIO_FILES], libs=["-lm"])   # the name index's hash table: ceil, floor
        os.makedirs(args.out, exist_ok=True)
        for name, data in (("hyperspace-flight", record_flight(uqm, flight)),
                           ("uqm-math", record_math(uqm, math)),
                           ("lander", record_lander(uqm, lander)),
                           ("star-systems", record_systems(uqm, systems)),
                           ("world-surfaces", record_surfaces(uqm, systems)),
                           ("ip-geometry", record_ip(uqm, systems)),
                           ("system-layout", record_layout(uqm, systems)),
                           ("system-sprites", record_sprites(uqm, systems)),
                           *record_planet_surfaces(uqm, systems, strings, images, args.content).items(),
                           *record_planetside(uqm, systems, strings, images, args.content).items(),
                           ("landing", record_landing(uqm, systems)),
                           ("string-numbering", record_string_numbering(uqm, strings)),
                           ("conversation-cases", record_conversation_cases(uqm, strings)),
                           ("string-table-cases", record_string_table_cases(uqm, strings)),
                           ("resource-index-cases", record_resource_index_cases(uqm, strings)),
                           *([("string-tables", record_string_tables(uqm, strings, args.content)),
                              ("names", record_names(uqm, systems, strings, args.content)),
                              ("fonts", record_fonts(uqm, fonts, strings, args.content)),
                              ("images", record_images(uqm, images, args.content)),
                              ("conversations", record_conversations(uqm, strings, args.content)),
                              ("comm-graphics", record_comm_graphics(uqm, strings, images, args.content)),
                              ("binary-tables", record_binary_tables(uqm, strings, args.content)),
                              ("talks", record_talks(uqm, strings, args.content)),
                              *record_contact(uqm, systems, strings, images, args.content).items(),
                              ("lander-pickups", record_lander_pickups(uqm, lander, strings, images, args.content)),
                              ("lander-creatures", record_lander_creatures(uqm, lander, strings, images, args.content)),
                              ("lander-hazards", record_lander_hazards(uqm, lander, strings, images, args.content)),
                              ("lander-drawing", record_lander_drawing(uqm, lander, strings, images, args.content))]
                             if args.content else [])):
            path = os.path.join(args.out, name + ".json")
            with open(path, "w") as f:
                json.dump(data, f, separators=(",", ":"))
                f.write("\n")
            print(f"wrote {path}")
    finally:
        shutil.rmtree(build_dir, ignore_errors=True)


if __name__ == "__main__":
    main()
