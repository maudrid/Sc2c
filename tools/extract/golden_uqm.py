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
STRINGS_LINK = ["libs/strings/getstr.c", "libs/strings/strings.c",
                "libs/memory/w_memlib.c", "libs/resource/propfile.c"]

# The resource types UQM 0.8.0 registers (every InstallResTypeVectors
# call); any other type is UNKNOWNRES, as newResourceDesc in resinit.c has
# it (and ResourceIndex in ContentPackage.cs).
FONTS_LINK = ["libs/strings/unicode.c", "libs/graphics/boxint.c"]
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
             "uqm/planets/surface.c", "uqm/gendef.c",
             "uqm/plandata.c", "uqm/starmap.c", "uqm/globdata.c",
             "libs/math/random2.c", "uqm/trans.c", "libs/math/sqrt.c",
             "uqm/velocity.c"] +
            sorted(f"uqm/planets/generate/{n}" for n in os.listdir(gen)
                   if n.endswith(".c")))


# Our own stand-ins for the parts of UQM the systems program doesn't need.
SYSTEMS_STUBS = os.path.join(HERE, "golden", "stubs.c")


def build(uqm, main_c, link, out, extra=(), libs=()):
    cflags = ["-w", "-no-pie", "-I", uqm.build, "-I", uqm.src, "-I", uqm.uqm,
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


def record_ip_flight(uqm, exe):
    out = subprocess.run([exe, "flight"], capture_output=True, text=True,
                         check=True).stdout
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
        "Sprite intersection (DrawablesIntersect) uses circles instead of "
        "UQM's content sprite masks: ship_radius for the flagship, 0 for "
        "UQM's one-pixel frame, planet_radius by planet image size index, "
        "junk_radius for the starbase and the Sa-Matra, in display pixels. "
        "The engine uses the same model until the content stage.",
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
        systems = os.path.join(build_dir, "systems")
        build(uqm, os.path.join(HERE, "golden", "systems.c"),
              systems_link(uqm), systems, extra=[SYSTEMS_STUBS])
        fonts = os.path.join(build_dir, "fonts")
        if args.content:
            build(uqm, os.path.join(HERE, "golden", "fonts.c"), FONTS_LINK,
                  fonts, libs=["-lpng"])
        strings = os.path.join(build_dir, "strings")
        build(uqm, os.path.join(HERE, "golden", "strings.c"), STRINGS_LINK,
              strings)
        os.makedirs(args.out, exist_ok=True)
        for name, data in (("hyperspace-flight", record_flight(uqm, flight)),
                           ("uqm-math", record_math(uqm, math)),
                           ("star-systems", record_systems(uqm, systems)),
                           ("world-surfaces", record_surfaces(uqm, systems)),
                           ("ip-geometry", record_ip(uqm, systems)),
                           ("ip-flight", record_ip_flight(uqm, systems)),
                           ("system-layout", record_layout(uqm, systems)),
                           ("string-numbering", record_string_numbering(uqm, strings)),
                           ("string-table-cases", record_string_table_cases(uqm, strings)),
                           ("resource-index-cases", record_resource_index_cases(uqm, strings)),
                           *([("string-tables", record_string_tables(uqm, strings, args.content)),
                              ("names", record_names(uqm, systems, strings, args.content)),
                              ("fonts", record_fonts(uqm, fonts, strings, args.content))]
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
