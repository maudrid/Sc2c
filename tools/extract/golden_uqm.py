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


def build(uqm, main_c, link, out):
    cflags = ["-w", "-no-pie", "-I", uqm.build, "-I", uqm.src, "-I", uqm.uqm,
              "-I", os.path.join(uqm.uqm, "ships", "sis_ship")]
    objs = []
    for i, src in enumerate([main_c] + [os.path.join(uqm.src, f)
                                        for f in link]):
        obj = os.path.join(uqm.build, f"{os.path.basename(out)}_{i}.o")
        subprocess.run(["gcc", *cflags, "-c", src, "-o", obj], check=True,
                       capture_output=True, text=True)
        objs.append(obj)
    subprocess.run(["gcc", "-no-pie", *objs, "-o", out,
                    "-Wl,--unresolved-symbols=ignore-all"], check=True,
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


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("--uqm", required=True,
                    help="path to the sc2 directory of UQM 0.8.0")
    ap.add_argument("--out", default=os.path.join(REPO, "tests", "golden"))
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
        os.makedirs(args.out, exist_ok=True)
        for name, data in (("hyperspace-flight", record_flight(uqm, flight)),
                           ("uqm-math", record_math(uqm, math))):
            path = os.path.join(args.out, name + ".json")
            with open(path, "w") as f:
                json.dump(data, f, separators=(",", ":"))
                f.write("\n")
            print(f"wrote {path}")
    finally:
        shutil.rmtree(build_dir, ignore_errors=True)


if __name__ == "__main__":
    main()
