#!/usr/bin/env python3
# Attack on Crateris: checks for the data extracted from UQM 0.8.0.
#
# Copyright (C) 2026 Attack on Crateris contributors.
#
# This program is free software; you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation; either version 2 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
"""Checks for data/*.json.

    python3 -m unittest discover -s tools/extract -v

The data checks need nothing but the repo. Set UQM_SC2 to the sc2
directory of UQM 0.8.0 to also re-run the extractor and confirm the
committed files match the source exactly.
"""

import json
import os
import re
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
DATA = os.path.join(REPO, "data")
FILES = ["starmap", "planet-types", "elements", "lifeforms", "ships",
         "races", "game-state", "new-game", "modules", "constants",
         "events"]


def load(name):
    with open(os.path.join(DATA, name + ".json")) as f:
        return json.load(f)


class Notices(unittest.TestCase):
    def test_every_file_has_gpl_notice_and_source(self):
        for name in FILES:
            d = load(name)
            self.assertIn("GNU General Public License", d["_notice"], name)
            self.assertEqual(d["_source"]["commit"],
                             "199e3748cc41b688a65601e3b7b2a74bdb2c917c",
                             name)


class Starmap(unittest.TestCase):
    def setUp(self):
        self.d = load("starmap")
        self.c = load("constants")["constants"]

    def test_counts(self):
        self.assertEqual(len(self.d["stars"]), 502)
        self.assertEqual(len(self.d["stars"]),
                         self.d["constants"]["NUM_SOLAR_SYSTEMS"])
        self.assertEqual(len(self.d["quasispace"]), 16)

    def star(self, special):
        return [s for s in self.d["stars"] if s["special"] == special]

    def test_spathiwa_matches_fwiffos_coordinates(self):
        # Fwiffo: "The coordinates of my homeworld, Spathiwa, are
        # 241.6, 368.7" (comm/spathi dialogue).
        (spa,) = self.star("SPATHI_DEFINED")
        self.assertEqual((spa["x"], spa["y"]), (2416, 3687))

    def test_sol_matches_sol_constant(self):
        (sol,) = self.star("SOL_DEFINED")
        self.assertEqual((sol["x"], sol["y"]),
                         (self.c["SOL_X"]["value"], self.c["SOL_Y"]["value"]))

    def test_samatra_at_delta_crateris(self):
        (sam,) = self.star("SAMATRA_DEFINED")
        self.assertEqual((sam["x"], sam["y"]), (6200, 5935))

    def test_coordinates_in_range(self):
        for s in self.d["stars"]:
            self.assertTrue(0 <= s["x"] <= 9999 and 0 <= s["y"] <= 9999)

    def test_ten_rainbow_worlds(self):
        self.assertEqual(len(self.star("RAINBOW_DEFINED")), 10)


class PlanetsAndElements(unittest.TestCase):
    def test_planet_types(self):
        d = load("planet-types")
        types = d["planet_types"]
        self.assertEqual(len(types), d["constants"]["NUMBER_OF_PLANET_TYPES"])
        self.assertEqual(types[0]["type"], "OOLITE_WORLD")
        names = {t["type"] for t in types}
        self.assertEqual(len(names), len(types))
        elements = {e["element"] for e in load("elements")["elements"]}
        for t in types:
            self.assertLessEqual(len(t["useful_elements"]), 8)
            for u in t["useful_elements"]:
                self.assertIn(u["element"], elements)

    def test_yttric_world_minerals(self):
        t = [t for t in load("planet-types")["planet_types"]
             if t["type"] == "YTTRIC_WORLD"][0]
        self.assertEqual([u["element"] for u in t["useful_elements"]],
                         ["YTTERBIUM", "YTTRIUM"])

    def test_elements(self):
        d = load("elements")
        self.assertEqual(len(d["elements"]), 133)
        self.assertEqual(d["elements"][0]["element"], "HYDROGEN")
        worth = {c["category"]: c["worth"] for c in d["categories"]}
        self.assertEqual(worth, {"COMMON": 1, "CORROSIVE": 2,
                                 "BASE_METAL": 3, "NOBLE": 4,
                                 "RARE_EARTH": 5, "PRECIOUS": 6,
                                 "RADIOACTIVE": 8, "EXOTIC": 25})

    def test_lifeforms(self):
        d = load("lifeforms")
        lf = d["lifeforms"]
        self.assertEqual(len(lf), d["constants"]["NUM_CREATURE_TYPES"] +
                         d["constants"]["NUM_SPECIAL_CREATURE_TYPES"])
        self.assertEqual(lf[0]["label"], "Roto-Dendron")
        self.assertEqual((lf[0]["bio_value"], lf[0]["hit_points"]), (1, 1))
        self.assertEqual(lf[-1]["label"], "Zex's Beauty")


class Ships(unittest.TestCase):
    def setUp(self):
        self.ships = {s["id"]: s for s in load("ships")["ships"]}

    def test_all_ship_directories(self):
        self.assertEqual(len(self.ships), 28)
        for s in self.ships.values():
            self.assertIsNotNone(s["species"], s["id"])

    def test_spathi_eluder(self):
        s = self.ships["spathi"]
        self.assertEqual((s["crew"], s["max_crew"], s["energy"],
                          s["max_energy"], s["cost"]), (30, 30, 10, 10, 18))
        ch = s["characteristics"]
        self.assertEqual((ch["max_thrust"], ch["thrust_increment"],
                          ch["turn_wait"], ch["ship_mass"]), (48, 12, 1, 5))
        self.assertEqual(s["defines"]["MISSILE_DAMAGE"], 1)
        # DISPLAY_TO_WORLD (30) == 30 << 2
        self.assertEqual(s["defines"]["MISSILE_SPEED"], 120)

    def test_defines_agree_with_race_desc(self):
        for s in self.ships.values():
            d = s["defines"]
            # In UQM the MAX_CREW define is the starting crew; most ships
            # start full, but the Syreen start with 12 of 42.
            if "MAX_CREW" in d:
                self.assertEqual(d["MAX_CREW"], s["crew"], s["id"])
            if "MAX_THRUST" in d:
                self.assertEqual(d["MAX_THRUST"],
                                 s["characteristics"]["max_thrust"], s["id"])

    def test_ur_quan_spheres_around_samatra(self):
        self.assertEqual((self.ships["urquan"]["fleet"]["home_x"],
                          self.ships["urquan"]["fleet"]["home_y"]),
                         (5750, 6000))
        self.assertEqual((self.ships["blackurq"]["fleet"]["home_x"],
                          self.ships["blackurq"]["fleet"]["home_y"]),
                         (6000, 6250))

    def test_races(self):
        races = load("races")["races"]
        cost = {r["race"]: r.get("ship_cost") for r in races}
        self.assertEqual(cost["URQUAN_SHIP"], 3000)
        self.assertEqual(cost["SHOFIXTI_SHIP"], 500)
        # 23 races in the game; the drone/Sa-Matra entry has no cost.
        self.assertEqual(len([r for r in races if "ship_cost" in r]), 23)

    def test_syreen_start_below_max_crew(self):
        s = self.ships["syreen"]
        self.assertEqual((s["crew"], s["max_crew"]), (12, 42))


class StateAndRules(unittest.TestCase):
    def test_game_state_bits_are_contiguous(self):
        d = load("game-state")
        bit = 0
        names = set()
        for s in d["states"]:
            self.assertEqual(s["start_bit"], bit, s["name"])
            self.assertNotIn(s["name"], names)
            names.add(s["name"])
            bit += s["bits"]
        self.assertEqual(bit, d["total_bits"])
        self.assertEqual(len(d["states"]), 447)
        for st in d["states"]:
            self.assertEqual(set(st) - {"comment"},
                             {"name", "bits", "start_bit"}, st["name"])

    def test_new_game(self):
        d = load("new-game")
        self.assertEqual(d["start_date"], {"year": 2155, "month": 2,
                                           "day": 17})
        f = d["flagship"]
        self.assertEqual((f["crew"], f["fuel"], f["landers"],
                          f["resource_units"]), (50, 1000, 1, 0))
        self.assertEqual(f["slots"]["module"]["15"], "GUN_WEAPON")
        self.assertEqual((d["prices"]["crew"], d["prices"]["fuel"]), (3, 20))
        events = {e["event"] for e in load("events")["events"]}
        for e in d["initial_events"]:
            self.assertIn(e["event"], events)

    def test_constants(self):
        c = {k: v["value"] for k, v in load("constants")["constants"].items()}
        self.assertEqual(c["HYPERSPACE_CLOCK_RATE"], 5)
        self.assertEqual(c["INTERPLANETARY_CLOCK_RATE"], 30)
        self.assertEqual(c["RUN_AWAY_FUEL_COST"], 500)
        self.assertEqual(c["ONE_SECOND"] // c["BATTLE_FRAME_RATE"], 24)
        for k, v in c.items():
            self.assertIsNotNone(v, k)

    def test_events(self):
        ev = load("events")["events"]
        self.assertEqual(len(ev), 18)
        self.assertEqual([e["id"] for e in ev], list(range(18)))


@unittest.skipUnless(os.environ.get("UQM_SC2"),
                     "set UQM_SC2 to the UQM 0.8.0 sc2 directory")
class MatchesSource(unittest.TestCase):
    def test_extractor_reproduces_committed_data(self):
        with tempfile.TemporaryDirectory() as out:
            subprocess.run([sys.executable, "-I",
                            os.path.join(HERE, "extract_uqm.py"),
                            "--uqm", os.environ["UQM_SC2"], "--out", out],
                           check=True, capture_output=True)
            for name in FILES:
                with open(os.path.join(out, name + ".json")) as f:
                    fresh = json.load(f)
                self.assertEqual(fresh, load(name), name)

    def test_initial_events_match_gameev(self):
        path = os.path.join(os.environ["UQM_SC2"], "src/uqm/gameev.c")
        with open(path) as f:
            text = f.read()
        body = text[text.index("AddInitialGameEvents (void)"):]
        body = body[:body.index("}")]
        calls = re.findall(r"AddEvent\s*\(([^;]*)\);", body)
        names = [c.split(",")[-1].strip() for c in calls]
        self.assertEqual(names, [e["event"] for e in
                                 load("new-game")["initial_events"]])


if __name__ == "__main__":
    unittest.main()
