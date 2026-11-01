#!/usr/bin/env python3
# Attack on Crateris: extract gameplay data from The Ur-Quan Masters 0.8.0.
#
# Copyright (C) 2026 Attack on Crateris contributors.
# The data this script writes is derived from The Ur-Quan Masters source
# code, Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002,
# and the UQM contributors.
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
"""Extract UQM 0.8.0 gameplay data into readable JSON files.

The script reads the UQM source tree (the `sc2` directory of the official
git repository, branch v0.8.0). For every value it compiles a small C
program against UQM's own headers and tables and prints the result, so
macros, packed bit fields and enum values come from the C compiler, not
from hand-written parsing. Only names, order and comments are read as text.

Usage:
    python3 tools/extract/extract_uqm.py --uqm /path/to/uqm/sc2 --out data

Needs gcc. See docs/uqm-data.md.
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

PINNED_COMMIT = "199e3748cc41b688a65601e3b7b2a74bdb2c917c"
UQM_REPO = "https://git.code.sf.net/p/sc2/uqm"

NOTICE = (
    "Derived from The Ur-Quan Masters 0.8.0 source code, Copyright Paul "
    "Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM "
    "contributors. Licensed under the GNU General Public License, version 2 "
    "or (at your option) any later version. Extracted for Attack on Crateris "
    "by tools/extract/extract_uqm.py; do not edit by hand."
)

CONFIG_VALUES = {
    "@CONTENTDIR@": "content",
    "@WORDS_BIGENDIAN@": "",
    "@HAVE_READDIR_R@": "#define HAVE_READDIR_R",
    "@HAVE_SETENV@": "#define HAVE_SETENV",
    "@HAVE_STRUPR@": "",
    "@HAVE_STRCASECMP_UQM@": "#define HAVE_STRCASECMP_UQM",
    "@HAVE_STRICMP@": "",
    "@HAVE_GETOPT_LONG@": "#define HAVE_GETOPT_LONG",
    "@HAVE_ISWGRAPH@": "#define HAVE_ISWGRAPH",
    "@HAVE_WCHAR_T@": "#define HAVE_WCHAR_T",
    "@HAVE_WINT_T@": "#define HAVE_WINT_T",
    "@HAVE__BOOL@": "#define HAVE__BOOL",
}


class Uqm:
    """Access to a UQM source tree and a scratch build directory."""

    def __init__(self, sc2_dir, build_dir):
        self.root = os.path.abspath(sc2_dir)
        self.src = os.path.join(self.root, "src")
        self.uqm = os.path.join(self.src, "uqm")
        self.build = build_dir
        if not os.path.isfile(os.path.join(self.uqm, "plandata.c")):
            sys.exit(f"Not a UQM sc2 directory: {self.root}")
        with open(os.path.join(self.src, "config_unix.h.in")) as f:
            text = f.read()
        for key, value in CONFIG_VALUES.items():
            text = text.replace(key, value)
        with open(os.path.join(self.build, "config_unix.h"), "w") as f:
            f.write(text)
        self.counter = 0
        self._commit = None
        self._commit_known = False
        self._new_game = None

    def read(self, rel):
        with open(os.path.join(self.root, rel), encoding="latin-1") as f:
            return f.read()

    def commit(self):
        """HEAD of the UQM checkout, or None if it can't be verified.

        Only trusts git when sc2/ sits directly in the repository root, so
        a tarball unpacked inside some other repository isn't mistaken for
        a UQM checkout.
        """
        if self._commit_known:
            return self._commit
        self._commit_known = True
        try:
            top = subprocess.run(
                ["git", "-C", self.root, "rev-parse", "--show-toplevel"],
                capture_output=True, text=True, check=True).stdout.strip()
            if os.path.realpath(top) == os.path.realpath(
                    os.path.dirname(self.root)):
                self._commit = subprocess.run(
                    ["git", "-C", self.root, "rev-parse", "HEAD"],
                    capture_output=True, text=True,
                    check=True).stdout.strip()
        except (OSError, subprocess.CalledProcessError):
            pass
        return self._commit

    def run_c(self, body, includes, extra_dirs=()):
        """Compile and run a C program; return its stdout lines."""
        self.counter += 1
        name = os.path.join(self.build, f"h{self.counter}")
        lines = [f'#include "{inc}"' for inc in includes]
        lines += ["#include <stdio.h>", "int main(void) {", body,
                  "return 0; }"]
        with open(name + ".c", "w") as f:
            f.write("\n".join(lines) + "\n")
        cmd = ["gcc", "-w", "-no-pie", "-I", self.build, "-I", self.src,
               "-I", self.uqm]
        for d in extra_dirs:
            cmd += ["-I", d]
        cmd += [name + ".c", "-o", name,
                "-Wl,--unresolved-symbols=ignore-all"]
        res = subprocess.run(cmd, capture_output=True, text=True)
        if res.returncode != 0:
            raise RuntimeError(f"gcc failed for {name}.c:\n{res.stderr}")
        out = subprocess.run([name], capture_output=True, text=True,
                             check=True)
        return out.stdout.splitlines()

    def eval_ints(self, exprs, includes, extra_dirs=(), constant=False):
        """Evaluate integer C expressions; return a list of ints.

        With constant=True each expression must be an integer constant
        expression (it is used as an enum value), so strings, pointers and
        floating-point values fail to compile instead of being converted.
        One expression per line, so errors can be traced to an expression.
        """
        if constant:
            body = "\n".join(
                f'{{ enum {{ v = ({e}) }}; printf("%lld\\n", (long long)v); }}'
                for e in exprs)
        else:
            body = "\n".join(f'printf("%lld\\n", (long long)({e}));'
                             for e in exprs)
        return [int(v) for v in self.run_c(body, includes, extra_dirs)]


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    return re.sub(r"//[^\n]*", " ", text)


def enum_block(text, marker):
    """Return the body of the enum that contains identifier `marker`."""
    clean = strip_comments(text)
    for m in re.finditer(r"enum\s*\w*\s*\{(.*?)\}", clean, flags=re.S):
        if re.search(rf"\b{marker}\b", m.group(1)):
            return m.group(1)
    raise KeyError(marker)


def enum_names(text, marker, skip_aliases=True):
    """Ordered member names of the enum containing `marker`.

    Members that only rename another member (NAME = OTHER) and counting
    markers (FIRST_*, LAST_*, NUM_*) are skipped unless skip_aliases is
    False.
    """
    counting = re.compile(r"(FIRST|LAST|NUM|NUMBER_OF)_")
    names = []
    for part in enum_block(text, marker).split(","):
        part = part.strip()
        if not part or part.startswith("#"):
            continue
        m = re.match(r"([A-Za-z_]\w*)\s*(?:=\s*(.+))?$", part, flags=re.S)
        if not m:
            continue
        name, value = m.group(1), m.group(2)
        if skip_aliases:
            if counting.match(name):
                continue
            # NAME = OTHER is a second name for OTHER, unless OTHER is a
            # counting marker (then NAME starts a new run of values).
            if value and re.fullmatch(r"[A-Z_]\w*", value.strip()) \
                    and not counting.match(value.strip()):
                continue
        names.append(name)
    return names


def enum_map(uqm, text, marker, includes):
    """{value: name} for the enum containing `marker`, values compiled."""
    names = enum_names(text, marker)
    values = uqm.eval_ints(names, includes)
    out = {}
    for n, v in zip(names, values):
        out.setdefault(v, n)
    return out


def named_values(uqm, names, includes, extra_dirs=()):
    values = uqm.eval_ints(names, includes, extra_dirs)
    return dict(zip(names, values))


def wrap(uqm, sources, data, notes=None):
    obj = {
        "_notice": NOTICE,
        "_source": {
            "repository": UQM_REPO,
            "branch": "v0.8.0",
            "commit": uqm.commit(),
            "files": sources,
        },
    }
    if notes:
        obj["_notes"] = notes
    obj.update(data)
    return obj


# ---------------------------------------------------------------- star map

def extract_starmap(uqm):
    plandata_h = uqm.read("src/uqm/planets/plandata.h")
    gendef_h = uqm.read("src/uqm/gendef.h")
    sizes = enum_map(uqm, plandata_h, "DWARF_STAR", ["uqm/plandata.c"])
    colors = enum_map(uqm, plandata_h, "BLUE_BODY", ["uqm/plandata.c"])
    specials = enum_names(gendef_h, "SOL_DEFINED", skip_aliases=False)
    special_values = named_values(uqm, specials, ["uqm/gendef.h"])
    special_by_value = {v: k for k, v in special_values.items()}

    body = r"""
    int i, sentinels = 0;
    for (i = 0; sentinels < 2; ++i) {
        const STAR_DESC *s = &starmap_array[i];
        if (s->star_pt.x == MAX_X_UNIVERSE << 1) { ++sentinels; continue; }
        printf("%d %d %d %d %d %d %d %d\n", sentinels, s->star_pt.x,
            s->star_pt.y, STAR_TYPE (s->Type), STAR_COLOR (s->Type),
            s->Index, s->Prefix, s->Postfix);
    }"""
    stars, quasi = [], []
    for line in uqm.run_c(body, ["uqm/plandata.c"]):
        sec, x, y, size, color, idx, prefix, postfix = map(int, line.split())
        star = {
            "x": x, "y": y,
            "size": sizes[size],
            "color": colors[color],
            "special": special_by_value.get(idx) if idx else None,
            "name_prefix": prefix,
            "name_postfix": postfix,
        }
        (stars if sec == 0 else quasi).append(star)
    consts = named_values(uqm, ["NUM_SOLAR_SYSTEMS", "MAX_X_UNIVERSE",
                                "MAX_Y_UNIVERSE"], ["uqm/starmap.h",
                                                    "uqm/units.h"])
    notes = [
        "Coordinates are in universe units: 0..MAX_X_UNIVERSE. Divide by 10 "
        "for the coordinates the game shows (e.g. Sol 1752,1450 is 175.2, "
        "145.0).",
        "Star names are content, not code. name_postfix is an index into "
        "the game strings (STAR_STRING_BASE, gamestrings.txt), "
        "name_prefix 0 means none, otherwise index-1 into the Greek-letter "
        "numbers at STAR_NUMBER_BASE. See GetClusterName in starmap.c.",
        "special names the generator in planets/generate/ for special "
        "systems; null means an ordinary system made by gendefault.c.",
        "The random seed of a system is MAKE_DWORD (x, y) "
        "(GetRandomSeedForStar in planets/solarsys.c).",
        "quasispace lists the QuasiSpace portal exits and the Arilou "
        "homeworld, in QuasiSpace coordinates.",
    ]
    return wrap(uqm, ["src/uqm/plandata.c", "src/uqm/planets/plandata.h",
                      "src/uqm/gendef.h", "src/uqm/starmap.h"],
                {"constants": consts, "stars": stars, "quasispace": quasi},
                notes)


# ---------------------------------------------------------- planet types

def extract_planets(uqm):
    plandata_h = uqm.read("src/uqm/planets/plandata.h")
    elem_h = uqm.read("src/uqm/planets/elemdata.h")
    types = enum_map(uqm, plandata_h, "OOLITE_WORLD", ["uqm/plandata.c"])
    elements = enum_map(uqm, elem_h, "HYDROGEN", ["uqm/plandata.c"])
    colors = enum_map(uqm, plandata_h, "BLUE_BODY", ["uqm/plandata.c"])
    body = r"""
    int i, j;
    for (i = 0; i < NUMBER_OF_PLANET_TYPES; ++i) {
        const PlanetFrame *p = &planet_array[i];
        printf("%d %d %d %d %d %d %d %d %d", PLANSIZE (p->Type),
            PLANALGO (p->Type), PLANCOLOR (p->Type), p->BaseTectonics,
            p->AtmoAndDensity & 0x0F, p->AtmoAndDensity >> 4,
            p->num_faults, p->fault_depth, p->num_blemishes);
        printf(" %d", p->base_elevation);
        for (j = 0; j < NUM_USEFUL_ELEMENTS; ++j)
            printf(" %d %d %d", p->UsefulElements[j].ElementType,
                p->UsefulElements[j].Density & 0x0F,
                p->UsefulElements[j].Density >> 4);
        printf(" %s %s\n", p->CMapInstance ? p->CMapInstance : "-",
            p->XlatTabInstance ? p->XlatTabInstance : "-");
    }"""
    size_names = {0: "SMALL_ROCKY_WORLD", 1: "LARGE_ROCKY_WORLD",
                  2: "GAS_GIANT"}
    algo_names = {0: "TOPO_ALGO", 1: "CRATERED_ALGO", 2: "GAS_GIANT_ALGO"}
    consts = named_values(uqm, [
        "SMALL_ROCKY_WORLD", "LARGE_ROCKY_WORLD", "GAS_GIANT", "TOPO_ALGO",
        "CRATERED_ALGO", "GAS_GIANT_ALGO", "FEW", "MODERATE", "NUMEROUS",
        "LIGHT", "MEDIUM", "HEAVY", "NUMBER_OF_PLANET_TYPES"],
        ["uqm/plandata.c"])
    nothing = uqm.eval_ints(["NOTHING"], ["uqm/plandata.c"])[0]
    algo_by_value = {consts[v]: v for v in algo_names.values()}
    size_by_value = {consts[v]: v for v in size_names.values()}
    out = []
    for i, line in enumerate(uqm.run_c(body, ["uqm/plandata.c"])):
        f = line.split()
        nums = list(map(int, f[:10 + 3 * 8]))
        useful = []
        for j in range(8):
            e, qty, quality = nums[10 + 3 * j: 13 + 3 * j]
            if e == nothing:
                # The engine rebuilds the 8 slots by padding with empty
                # ones, so empty slots must be trailing and hold nothing.
                if qty or quality:
                    raise RuntimeError(f"{types[i]}: NOTHING with a deposit")
                continue
            if len(useful) < j:
                raise RuntimeError(f"{types[i]}: empty element slot before "
                                   "a used one")
            useful.append({"element": elements[e], "quantity_class": qty,
                           "quality_class": quality})
        out.append({
            "type": types[i],
            "size": size_by_value.get(nums[0], nums[0]),
            "map_algorithm": algo_by_value.get(nums[1], nums[1]),
            "color": colors.get(nums[2], nums[2]),
            "base_tectonics": nums[3],
            "density_class": nums[4],
            "atmosphere_class": nums[5],
            "num_faults": nums[6],
            "fault_depth": nums[7],
            "num_blemishes": nums[8],
            "base_elevation": nums[9],
            "useful_elements": useful,
            "color_map_resource": None if f[-2] == "-" else f[-2],
            "color_table_resource": None if f[-1] == "-" else f[-1],
        })
    notes = [
        "One entry per planet type (PlanetFrame), not per planet. Planets "
        "themselves are generated from seeds by planets/plangen.c, calc.c "
        "and gentopo.c.",
        "quantity_class: FEW, MODERATE or NUMEROUS (see constants); "
        "quality_class: LIGHT, MEDIUM or HEAVY. Packed as the low and high "
        "nibble of ELEMENT_ENTRY.Density.",
        "useful_elements lists the used slots of UsefulElements (8 in "
        "UQM). The unused slots (NOTHING, no deposit) are always the last "
        "ones, so the 8 slots are the list padded with empty ones; mineral "
        "generation draws a random number for every slot, empty or not.",
        "density_class and atmosphere_class are the low and high nibble of "
        "PlanetFrame.AtmoAndDensity.",
        "color_map_resource and color_table_resource are content resource "
        "ids (planet graphics), resolved by the client.",
    ]
    return wrap(uqm, ["src/uqm/plandata.c", "src/uqm/planets/plandata.h",
                      "src/uqm/planets/elemdata.h"],
                {"constants": consts, "planet_types": out}, notes)


def extract_elements(uqm):
    elem_h = uqm.read("src/uqm/planets/elemdata.h")
    categories = enum_names(elem_h, "CORROSIVE")
    elements = enum_map(uqm, elem_h, "HYDROGEN", ["uqm/plandata.c"])
    body = r"""
    int i;
    for (i = 0; i < NUMBER_OF_ELEMENTS; ++i)
        printf("%d\n", element_array[i]);"""
    cats = [int(v) for v in uqm.run_c(body, ["uqm/plandata.c"])]
    worth = parse_new_game_assignments(uqm)["element_worth"]
    return wrap(uqm, ["src/uqm/plandata.c", "src/uqm/planets/elemdata.h",
                      "src/uqm/globdata.c"], {
        "categories": [{"category": c, "worth": worth.get(c)}
                       for c in categories],
        "elements": [{"id": i, "element": elements[i],
                      "category": categories[c]}
                     for i, c in enumerate(cats)],
    }, ["worth is the starting resource-unit value per unit of cargo, set "
        "in InitGameStructures (globdata.c)."])


# -------------------------------------------------------------- lifeforms

def extract_lifeforms(uqm):
    src = uqm.read("src/uqm/planets/lander.c")
    block = src[src.index("CreatureData[]"):]
    block = block[:block.index("};")]
    # Each entry is "{attributes, MAKE_BYTE (v, hp)}," followed by a
    # "// Name" comment; pair them entry by entry.
    entry_re = re.compile(r"\{[^{}]*MAKE_BYTE\s*\([^)]*\)\s*\}\s*,?"
                          r"[ \t]*(?:\n[ \t]*)?(?://\s*([^\n]+))?")
    comments = [m.group(1) for m in entry_re.finditer(block)]
    body = r"""
    int i;
    for (i = 0; i < NUM_CREATURE_TYPES + NUM_SPECIAL_CREATURE_TYPES; ++i) {
        BYTE a = CreatureData[i].Attributes;
        BYTE v = CreatureData[i].ValueAndHitPoints;
        printf("%d %d %d %d %d %d\n", (a & BEHAVIOR_MASK) >> BEHAVIOR_SHIFT,
            (a & AWARENESS_MASK) >> AWARENESS_SHIFT,
            (a & SPEED_MASK) >> SPEED_SHIFT, (a & DANGER_MASK) >> DANGER_SHIFT,
            LONIBBLE (v), HINIBBLE (v));
    }"""
    behavior = ["HUNT", "FLEE", "UNPREDICTABLE"]
    awareness = ["LOW", "MEDIUM", "HIGH"]
    speed = ["MOTIONLESS", "SLOW", "MEDIUM", "FAST"]
    danger = ["HARMLESS", "WEAK", "NORMAL", "MONSTROUS"]
    out = []
    rows = uqm.run_c(body, ["uqm/planets/lander.c"])
    if len(rows) != len(comments):
        raise RuntimeError(f"{len(rows)} creatures but {len(comments)} "
                           "entries found in CreatureData")
    for i, line in enumerate(rows):
        b, aw, sp, dg, value, hp = map(int, line.split())
        out.append({
            "index": i,
            "label": comments[i].strip() if comments[i] else None,
            "behavior": behavior[b], "awareness": awareness[aw],
            "speed": speed[sp], "danger": danger[dg],
            "bio_value": value, "hit_points": hp,
        })
    n = named_values(uqm, ["NUM_CREATURE_TYPES",
                           "NUM_SPECIAL_CREATURE_TYPES"],
                     ["uqm/planets/lifeform.h"])
    return wrap(uqm, ["src/uqm/planets/lander.c",
                      "src/uqm/planets/lifeform.h"],
                {"constants": n, "lifeforms": out},
                ["label comes from the source comment next to each entry. "
                 "Display names are content.",
                 "The last NUM_SPECIAL_CREATURE_TYPES entries are special "
                 "creatures (Evil One, Brainbox Bulldozer, Zex's Beauty).",
                 "bio_value is the low nibble of ValueAndHitPoints (bio data "
                 "per creature), hit_points the high nibble (stun hits to "
                 "capture, scan.c)."])


# ------------------------------------------------------------------ ships

SHIP_FIELDS = r"""
    printf("%d %d %d %d %d %d\n", d->ship_info.ship_flags,
        d->ship_info.ship_cost, d->ship_info.crew_level,
        d->ship_info.max_crew, d->ship_info.energy_level,
        d->ship_info.max_energy);
    printf("%d %d %d\n", d->fleet.strength, d->fleet.known_loc.x,
        d->fleet.known_loc.y);
    printf("%d %d %d %d %d %d %d %d %d %d %d\n",
        d->characteristics.max_thrust, d->characteristics.thrust_increment,
        d->characteristics.energy_regeneration,
        d->characteristics.weapon_energy_cost,
        d->characteristics.special_energy_cost,
        d->characteristics.energy_wait, d->characteristics.turn_wait,
        d->characteristics.thrust_wait, d->characteristics.weapon_wait,
        d->characteristics.special_wait, d->characteristics.ship_mass);
    printf("%d %d\n", d->cyborg_control.ManeuverabilityIndex,
        d->cyborg_control.WeaponRange);
    printf("%s\n", d->ship_info.race_strings_rsc
        ? d->ship_info.race_strings_rsc : "-");
"""

SHIP_FLAG_NAMES = ["FIRES_FORE", "FIRES_RIGHT", "FIRES_AFT", "FIRES_LEFT",
                   "SEEKING_WEAPON", "SEEKING_SPECIAL", "POINT_DEFENSE",
                   "IMMEDIATE_WEAPON", "CREW_IMMUNE", "DONT_CHASE",
                   "PLAYER_CAPTAIN"]


def file_defines(text):
    """Object-like #define names written in a ship file, in order."""
    names = []
    for m in re.finditer(r"^\s*#\s*define\s+([A-Za-z_]\w*)(\(?)", text,
                         flags=re.M):
        if m.group(2) == "(":
            continue
        if m.group(1).endswith("_H_") or m.group(1).endswith("_H"):
            continue
        names.append(m.group(1))
    return names


def eval_defines(uqm, names, includes, extra_dirs):
    """Evaluate the names that are integer constants; drop the rest
    (strings, floats, pointers, macros needing runtime values)."""
    remaining = list(names)
    while remaining:
        try:
            return dict(zip(remaining, uqm.eval_ints(
                remaining, includes, extra_dirs, constant=True)))
        except RuntimeError as err:
            bad = set()
            for m in re.finditer(r"h\d+\.c:(\d+):", str(err)):
                line = int(m.group(1))
                # line 1..k are includes, then stdio, then main.
                idx = line - len(includes) - 3
                if 0 <= idx < len(remaining):
                    bad.add(remaining[idx])
            if not bad:
                raise
            remaining = [n for n in remaining if n not in bad]
    return {}


def extract_ships(uqm):
    ships_dir = os.path.join(uqm.uqm, "ships")
    races_h = uqm.read("src/uqm/races.h")
    species = enum_names(races_h, "ARILOU_ID", skip_aliases=False)
    species = [s for s in species if s != "LAST_MELEE_ID"]
    species_values = named_values(uqm, species, ["uqm/races.h"])
    flag_values = named_values(uqm, SHIP_FLAG_NAMES, ["uqm/races.h"])
    # loadship.c maps each SPECIES_ID to a code resource id such as
    # "ship.spathi.code"; the middle part is the ship directory.
    body = r"""
    int i;
    for (i = 0; i < sizeof code_resources / sizeof code_resources[0]; ++i)
        printf("%d %s\n", i, code_resources[i] ? code_resources[i] : "-");"""
    species_by_value = {v: k for k, v in species_values.items()}
    species_by_res = {}
    for line in uqm.run_c(body, ["uqm/loadship.c"]):
        i, res = line.split()
        m = re.fullmatch(r"ship\.(\w+)\.code", res)
        if m:
            species_by_res[m.group(1)] = species_by_value.get(int(i))
    # A ship directory's resinst.h names its resources "ship.<name>.*".
    species_by_dir = {}
    for name in os.listdir(ships_dir):
        resinst = os.path.join(ships_dir, name, "resinst.h")
        if os.path.isfile(resinst):
            m = re.search(r'"ship\.(\w+)\.', uqm.read(
                f"src/uqm/ships/{name}/resinst.h"))
            if m:
                species_by_dir[name] = species_by_res.get(m.group(1))
    out = []
    for name in sorted(os.listdir(ships_dir)):
        cfile = os.path.join(ships_dir, name, name + ".c")
        if not os.path.isfile(cfile):
            continue
        text = uqm.read(f"src/uqm/ships/{name}/{name}.c")
        m = re.search(r"static\s+RACE_DESC\s+(\w+)\s*=", text)
        if not m:
            continue
        desc = m.group(1)
        inc = [f"uqm/ships/{name}/{name}.c"]
        extra = [os.path.join(ships_dir, name)]
        lines = uqm.run_c(f"RACE_DESC *d = &{desc};\n" + SHIP_FIELDS,
                          inc, extra)
        info = list(map(int, lines[0].split()))
        fleet = list(map(int, lines[1].split()))
        ch = list(map(int, lines[2].split()))
        intel = list(map(int, lines[3].split()))
        flags = [f for f in SHIP_FLAG_NAMES if info[0] & flag_values[f]]
        defines = eval_defines(uqm, file_defines(text), inc, extra)
        out.append({
            "id": name,
            "species": species_by_dir.get(name),
            "flags": flags,
            "cost": info[1],
            "crew": info[2], "max_crew": info[3],
            "energy": info[4], "max_energy": info[5],
            "fleet": {"strength": fleet[0],
                      "home_x": fleet[1], "home_y": fleet[2]},
            "characteristics": {
                "max_thrust": ch[0], "thrust_increment": ch[1],
                "energy_regeneration": ch[2], "weapon_energy_cost": ch[3],
                "special_energy_cost": ch[4], "energy_wait": ch[5],
                "turn_wait": ch[6], "thrust_wait": ch[7],
                "weapon_wait": ch[8], "special_wait": ch[9],
                "ship_mass": ch[10],
            },
            "ai": {"maneuverability_index": intel[0],
                   "weapon_range": intel[1]},
            "race_strings_resource": None if lines[4] == "-" else lines[4],
            "defines": defines,
        })
    return wrap(uqm, ["src/uqm/ships/*/*.c", "src/uqm/races.h",
                      "src/uqm/loadship.c"], {
        "species_ids": species_values,
        "ships": out,
    }, [
        "One entry per ship directory in src/uqm/ships. Values are the "
        "RACE_DESC initialiser of each ship, as compiled.",
        "fleet.strength and fleet.home_x/home_y are the starting size and "
        "centre of the race's sphere of influence (FLEET_STUFF), in "
        "universe units; strength 0 means no sphere.",
        "Waits are in battle frames (24 per second, BATTLE_FRAME_RATE). "
        "Speeds and distances are in world units; DISPLAY_TO_WORLD (x) is "
        "x << 2 (units.h).",
        "defines lists every integer #define in the ship's source file, "
        "including weapon speed, life, damage and range, which are not "
        "stored in RACE_DESC. Behaviour (weapons, specials, AI) stays code "
        "and is ported by hand.",
        "species is the SPECIES_ID that loads this ship (code_resources "
        "in loadship.c).",
    ])


# -------------------------------------------------------------- races

def macro_list(text, name):
    m = re.search(rf"#define\s+{name}\s*\\\n(.*?)(?<!\\)\n\s*\n", text,
                  flags=re.S)
    if not m:
        raise KeyError(name)
    body = strip_comments(m.group(1)).replace("\\", " ")
    return [p.strip() for p in body.split(",") if p.strip()]


def extract_races(uqm):
    races_h = uqm.read("src/uqm/races.h")
    race_names = enum_names(races_h, "ARILOU_SHIP", skip_aliases=False)
    race_names = [r for r in race_names if r not in ("NUM_AVAILABLE_RACES",
                                                     "SAMATRA_SHIP")]
    n_cost, n_speed = uqm.eval_ints(
        ["sizeof ((COUNT[]){RACE_SHIP_COST}) / sizeof (COUNT)",
         "sizeof ((COUNT[]){RACE_IP_SPEED}) / sizeof (COUNT)"],
        ["uqm/races.h"])
    comm = macro_list(races_h, "RACE_COMMUNICATION")
    if max(n_cost, n_speed, len(comm)) > len(race_names):
        raise RuntimeError(
            f"race tables don't line up: {len(race_names)} races, "
            f"{n_cost} costs, {n_speed} speeds, {len(comm)} conversations")
    costs = uqm.eval_ints([f"((COUNT[]){{RACE_SHIP_COST}})[{i}]"
                           for i in range(n_cost)], ["uqm/races.h"])
    speeds = uqm.eval_ints([f"((COUNT[]){{RACE_IP_SPEED}})[{i}]"
                            for i in range(n_speed)], ["uqm/races.h"])
    out = []
    for i, r in enumerate(race_names):
        entry = {"race": r}
        if i < len(costs):
            entry["ship_cost"] = costs[i]
        if i < len(speeds):
            entry["ip_speed"] = speeds[i]
        if i < len(comm):
            entry["conversation"] = comm[i]
        out.append(entry)
    return wrap(uqm, ["src/uqm/races.h"], {"races": out}, [
        "Order is the *_SHIP enum, which is the order of avail_race_q "
        "(FLEET_INFO, one per race in the game).",
        "ship_cost (RACE_SHIP_COST) is used for battle salvage: cost / 8 "
        "resource units per enemy ship destroyed (UninitEncounter, "
        "encount.c).",
        "ip_speed (RACE_IP_SPEED) is the speed of the race's ships in "
        "solar systems (ipdisp.c).",
        "conversation (RACE_COMMUNICATION) is the CONVERSATION id used "
        "when meeting this race.",
    ])


# ------------------------------------------------------------ game state

def extract_game_state(uqm):
    text = uqm.read("src/uqm/globdata.h")
    start = text.index("START_GAME_STATE\n")
    end = text.index("END_GAME_STATE", start + 20)
    block = text[start:end]
    # Comment lines indented three tabs or more describe the entry above.
    # (globdata.h has only two section headings, near the top, so they are
    # not used to group the entries.)
    entries = []
    last = None
    for line in block.splitlines():
        m = re.search(r"ADD_GAME_STATE\s*\(\s*(\w+)\s*,\s*(\d+)\s*\)", line)
        tabs = len(line) - len(line.lstrip("\t"))
        text_part = line.strip()
        if m:
            last = {"name": m.group(1), "bits": int(m.group(2)),
                    "comment": []}
            entries.append(last)
            c = re.search(r"/\*(.*?)\*/|//(.*)$", line)
            if c:
                last["comment"].append((c.group(1) or c.group(2)).strip())
        elif not text_part:
            last = None
        elif tabs >= 3 and last is not None:
            cleaned = re.sub(r"^(/\*|\*/|\*|//)\s?", "", text_part)
            cleaned = re.sub(r"\s*\*/$", "", cleaned).strip()
            if cleaned:
                last["comment"].append(cleaned)
        elif tabs == 2 and text_part.startswith(("/*", "//")):
            last = None
    starts = uqm.eval_ints([e["name"] for e in entries] +
                           ["NUM_GAME_STATE_BITS"], ["uqm/globdata.h"])
    total = starts.pop()
    for e, s in zip(entries, starts):
        e["start_bit"] = s
        text_comment = " ".join(x for x in e.pop("comment") if x)
        if text_comment:
            e["comment"] = text_comment
    return wrap(uqm, ["src/uqm/globdata.h"], {
        "total_bits": total,
        "states": entries,
    }, [
        "Every story flag and counter in UQM, packed as bit fields "
        "(GET_GAME_STATE / SET_GAME_STATE). In Attack on Crateris they are "
        "world state, read and written by name.",
        "comment is the source comment for the entry, where there is one.",
    ])


# --------------------------------------------------------------- new game

def parse_new_game_assignments(uqm):
    """Integer values InitGameStructures (globdata.c) sets for a new game.

    Handles chained assignments (A = B = value). Slots[i] targets are the
    loops that first fill every slot with its empty marker. The start
    position (set through a function) is left out; extract_new_game
    records it separately.
    """
    if uqm._new_game is not None:
        return uqm._new_game
    text = uqm.read("src/uqm/globdata.c")
    start = text.index("\nInitGameStructures (void)")
    body = strip_comments(text[start:text.index("\n}\n", start)])
    keep = re.compile(r"(ElementWorth|ModuleCost)\[\w+\]$|CrewCost$|"
                      r"FuelCost$|(DriveSlots|JetSlots|ModuleSlots)\[(\d+|i)\]$|"
                      r"CrewEnlisted$|FuelOnBoard$|NumLanders$|ResUnits$")
    target_re = re.compile(r"GLOBAL(_SIS)?\s*\(\s*([^()]+?)\s*\)\s*$")
    pairs = []
    for stmt in body.split(";"):
        parts = stmt.split("=")
        if len(parts) < 2 or "==" in stmt:
            continue
        value = parts[-1].strip()
        for t in parts[:-1]:
            m = target_re.search(t)
            if m and keep.match(m.group(2)):
                key = ("SIS." if m.group(1) else "") + m.group(2)
                pairs.append((key, value))
    values = uqm.eval_ints([v for _, v in pairs],
                           ["uqm/globdata.h", "uqm/sis.h"])
    result = {"element_worth": {}, "module_cost": {}, "other": {}}
    for (k, _), v in zip(pairs, values):
        m = re.match(r"(ElementWorth|ModuleCost)\[(\w+)\]", k)
        if m:
            group = "element_worth" if m.group(1) == "ElementWorth" \
                else "module_cost"
            result[group][m.group(2)] = v
        else:
            result["other"][k] = v
    uqm._new_game = result
    return result


def c_function_body(text, signature):
    start = text.index("\n" + signature)
    return strip_comments(text[start:text.index("\n}\n", start)])


def extract_clock_start(uqm):
    """Start date (InitGameClock) and AddInitialGameEvents, as compiled."""
    clock = c_function_body(uqm.read("src/uqm/clock.c"),
                            "InitGameClock (void)")
    fields = {}
    for name in ("month_index", "day_index", "year_index"):
        m = re.search(rf"GameClock\.{name}\s*\)\s*=\s*([^;]+);", clock)
        if not m:
            raise RuntimeError(f"InitGameClock no longer sets {name}")
        fields[name] = m.group(1).strip()
    month, day, year = uqm.eval_ints(
        [fields["month_index"], fields["day_index"], fields["year_index"]],
        ["uqm/clock.h"])
    body = c_function_body(uqm.read("src/uqm/gameev.c"),
                           "AddInitialGameEvents (void)")
    calls = [[a.strip() for a in c.split(",")]
             for c in re.findall(r"AddEvent\s*\(([^;]*)\)\s*;", body)]
    if not calls or any(len(c) != 5 for c in calls):
        raise RuntimeError("unexpected AddEvent calls in gameev.c")
    nums = uqm.eval_ints([x for c in calls for x in c[:4]],
                         ["uqm/clock.h", "uqm/globdata.h", "uqm/gameev.h"])
    kinds = {v: k for k, v in named_values(
        uqm, ["ABSOLUTE_EVENT", "RELATIVE_EVENT"], ["uqm/clock.h"]).items()}
    events = []
    for i, c in enumerate(calls):
        kind, mo, d, y = nums[4 * i: 4 * i + 4]
        kind = kinds[kind].replace("_EVENT", "")
        if kind == "ABSOLUTE":
            events.append({"event": c[4], "kind": kind, "month": mo,
                           "day": d, "year": y})
        else:
            events.append({"event": c[4], "kind": kind, "months": mo,
                           "days": d, "years": y})
    return {"year": year, "month": month, "day": day}, events


def extract_new_game(uqm):
    a = parse_new_game_assignments(uqm)
    sis = uqm.read("src/uqm/sis.h")
    modules = enum_names(sis, "FUSION_THRUSTER")
    module_values = named_values(uqm, modules + ["EMPTY_SLOT"],
                                 ["uqm/sis.h"])
    other = a["other"]
    start_date, events = extract_clock_start(uqm)
    # Slot assignments of the starting flagship, from InitGameStructures.
    kinds = {"DriveSlots": "drive", "JetSlots": "jet",
             "ModuleSlots": "module"}
    slots = {"drive": {}, "jet": {}, "module": {}}
    empty = {}
    empty_slot = module_values["EMPTY_SLOT"]
    for k, v in other.items():
        m = re.match(r"SIS\.(DriveSlots|JetSlots|ModuleSlots)\[(\d+|i)\]", k)
        if not m:
            continue
        kind = kinds[m.group(1)]
        if m.group(2) == "i":
            empty[kind] = {"value": v,
                           "expression": f"EMPTY_SLOT + {v - empty_slot}"}
            continue
        name = next((n for n, val in module_values.items()
                     if val == v and n != "EMPTY_SLOT"), None)
        slots[kind][int(m.group(2))] = name
    if set(empty) != set(slots):
        raise RuntimeError(f"empty-slot loops not found for "
                           f"{set(slots) - set(empty)}")
    plain = {k: v for k, v in other.items() if "Slots[" not in k}
    unit = named_values(uqm, ["MODULE_COST_SCALE"], ["uqm/sis.h"])
    sol = named_values(uqm, ["SOL_X", "SOL_Y"], ["uqm/races.h",
                                                 "uqm/hyper.h"])
    gd = uqm.read("src/uqm/globdata.c")
    if "UNIVERSE_TO_LOGX (SOL_X)" not in gd:
        raise RuntimeError("start position no longer Sol in globdata.c")
    return wrap(uqm, ["src/uqm/globdata.c", "src/uqm/clock.c",
                      "src/uqm/gameev.c", "src/uqm/sis.h"], {
        "start_date": start_date,
        "prices": {
            "crew": plain.get("CrewCost"),
            "fuel": plain.get("FuelCost"),
            "module_cost_scale": unit["MODULE_COST_SCALE"],
            "module_cost": a["module_cost"],
            "element_worth": a["element_worth"],
        },
        "flagship": {
            "crew": plain.get("SIS.CrewEnlisted"),
            "fuel": plain.get("SIS.FuelOnBoard"),
            "landers": plain.get("SIS.NumLanders"),
            "resource_units": plain.get("SIS.ResUnits"),
            "start_star": "SOL",
            "start_universe_x": sol["SOL_X"],
            "start_universe_y": sol["SOL_Y"],
            "slots": {k: {str(i): n for i, n in sorted(v.items())}
                      for k, v in slots.items()},
            "empty_slot_markers": empty,
        },
        "initial_events": events,
    }, [
        "Values set by InitGameStructures (globdata.c), InitGameClock (clock.c) "
        "and AddInitialGameEvents (gameev.c) when a new game starts.",
        "module_cost is in units of module_cost_scale resource units "
        "(price = cost * scale). Modules not listed start at 0, meaning "
        "not yet for sale.",
        "fuel is in 1/100 units (FUEL_TANK_SCALE): 1000 is 10 units of "
        "fuel.",
        "The flagship starts at Sol (log_x/log_y = UNIVERSE_TO_LOGX/Y of "
        "SOL_X/SOL_Y), in interplanetary flight.",
        "slots lists the filled slots by position; every other slot "
        "starts empty. Each kind of slot has its own empty marker "
        "(empty_slot_markers: drive EMPTY_SLOT + 0, jet + 1, module + 2), "
        "which the outfitting code (outfit.c) relies on.",
        "start_date comes from InitGameClock and initial_events from the "
        "AddEvent calls in AddInitialGameEvents, both evaluated by the "
        "compiler. A RELATIVE event with months, days and years all 0 runs "
        "at once instead of being scheduled (AddEvent, clock.c).",
        "The flagship's and commander's starting names are content strings.",
    ])


# --------------------------------------------------------------- modules

def extract_modules(uqm):
    sis = uqm.read("src/uqm/sis.h")
    names = enum_names(sis, "FUSION_THRUSTER", skip_aliases=False)
    names = [n for n in names if n not in ("NUM_PURCHASE_MODULES",
                                           "NUM_MODULES")]
    vals = named_values(uqm, names + ["NUM_PURCHASE_MODULES", "NUM_MODULES",
                                      "EMPTY_SLOT"], ["uqm/sis.h"])
    costs = parse_new_game_assignments(uqm)["module_cost"]
    out = [{"module": n, "id": vals[n],
            "purchasable": vals[n] < vals["NUM_PURCHASE_MODULES"],
            "start_cost": costs.get(n, 0)} for n in names]
    return wrap(uqm, ["src/uqm/sis.h", "src/uqm/globdata.c"], {
        "modules": out,
        "constants": {k: vals[k] for k in ("NUM_PURCHASE_MODULES",
                                           "NUM_MODULES", "EMPTY_SLOT")},
    }, ["start_cost is in units of MODULE_COST_SCALE resource units; 0 "
        "means not for sale at the start (more become available through "
        "the story).",
        "BOMB_MODULE_* are the parts of the Precursor bomb shown on the "
        "flagship at the end."])


# ------------------------------------------------------------ constants

CONSTANTS = [
    # (name, expression, includes, unit or meaning)
    ("HYPERSPACE_CLOCK_RATE", ["uqm/clock.h"], "seconds per game day"),
    ("INTERPLANETARY_CLOCK_RATE", ["uqm/clock.h"], "seconds per game day"),
    ("START_YEAR", ["uqm/clock.h"], "year"),
    ("ONE_SECOND", ["libs/timelib.h"], "time units per second"),
    ("BATTLE_FRAME_RATE", ["libs/timelib.h", "uqm/battle.h"],
     "time units per battle frame"),
    ("NUM_DRIVE_SLOTS", ["uqm/sis.h"], "slots"),
    ("NUM_JET_SLOTS", ["uqm/sis.h"], "slots"),
    ("NUM_MODULE_SLOTS", ["uqm/sis.h"], "slots"),
    ("CREW_POD_CAPACITY", ["uqm/sis.h"], "crew"),
    ("STORAGE_BAY_CAPACITY", ["uqm/sis.h"], "cargo units"),
    ("FUEL_TANK_SCALE", ["uqm/sis.h"], "internal fuel units per fuel unit"),
    ("FUEL_TANK_CAPACITY", ["uqm/sis.h"], "internal fuel units"),
    ("HEFUEL_TANK_CAPACITY", ["uqm/sis.h"], "internal fuel units"),
    ("FUEL_RESERVE", ["uqm/sis.h"], "internal fuel units"),
    ("MODULE_COST_SCALE", ["uqm/sis.h"], "resource units"),
    ("CREW_EXPENSE_THRESHOLD", ["uqm/sis.h"], "resource units"),
    ("BIO_CREDIT_VALUE", ["uqm/sis.h"], "Melnorme credits per bio unit"),
    ("MAX_BUILT_SHIPS", ["uqm/sis.h"], "escort ships"),
    ("MAX_LANDERS", ["uqm/sis.h"], "landers"),
    ("NUM_PLAYERS", ["uqm/init.h"], "battle sides (UQM)"),
    ("NUM_SIDES", ["uqm/init.h"], "battle sides (UQM)"),
    ("MAX_SHIPS_PER_SIDE", ["uqm/races.h"], "ships"),
    ("MAX_ENCOUNTERS", ["uqm/globdata.h"], "hyperspace encounters"),
    ("MAX_BATTLE_GROUPS", ["uqm/globdata.h"], "groups in a system"),
    ("NUM_SOLAR_SYSTEMS", ["uqm/starmap.h"], "stars"),
    ("MAX_X_UNIVERSE", ["uqm/units.h"], "universe units"),
    ("MAX_Y_UNIVERSE", ["uqm/units.h"], "universe units"),
    ("SOL_X", ["uqm/units.h", "uqm/starmap.h", "uqm/hyper.h"],
     "universe units"),
    ("SOL_Y", ["uqm/units.h", "uqm/starmap.h", "uqm/hyper.h"],
     "universe units"),
    ("ARILOU_SPACE_X", ["uqm/hyper.h"], "universe units"),
    ("ARILOU_SPACE_Y", ["uqm/hyper.h"], "universe units"),
    ("QUASI_SPACE_X", ["uqm/hyper.h"], "universe units"),
    ("QUASI_SPACE_Y", ["uqm/hyper.h"], "universe units"),
    ("NUM_EVENTS", ["uqm/gameev.h"], "events"),
    ("YEARS_TO_KOHRAH_VICTORY", ["uqm/globdata.h"], "years"),
]

# Constants defined inside a function, so not reachable from headers.
LOCAL_CONSTANTS = [
    ("RUN_AWAY_FUEL_COST", "5 * FUEL_TANK_SCALE", ["uqm/sis.h"],
     "src/uqm/pickship.c", "internal fuel units lost when the flagship "
     "flees last"),
    ("LOST_DAYS", "14", [], "src/uqm/starbase.c",
     "days skipped while the bomb is installed (DoTimePassage)"),
]


def extract_constants(uqm):
    out = {}
    for name, includes, unit in CONSTANTS:
        value = uqm.eval_ints([name], ["uqm/races.h"] + includes)[0]
        out[name] = {"value": value, "unit": unit}
    for name, expr, includes, where, unit in LOCAL_CONSTANTS:
        src = uqm.read(where)
        if not re.search(rf"#define\s+{name}\s+\(?{re.escape(expr)}\)?"
                         r"[ \t]*(?:/[/*]|$)", src, flags=re.M):
            raise RuntimeError(f"{name} no longer matches {where}")
        value = uqm.eval_ints([expr], ["uqm/races.h"] + includes)[0]
        out[name] = {"value": value, "unit": unit, "defined_in": where}
    return wrap(uqm, ["src/uqm/*.h", "src/libs/timelib.h"] +
                [c[3] for c in LOCAL_CONSTANTS],
                {"constants": out},
                ["Named limits and rates from UQM headers, evaluated by the "
                 "compiler."])


# --------------------------------------------------------------- events

def extract_events(uqm):
    text = uqm.read("src/uqm/gameev.h")
    names = enum_names(text, "ARILOU_ENTRANCE_EVENT")
    vals = named_values(uqm, names, ["uqm/gameev.h"])
    return wrap(uqm, ["src/uqm/gameev.h", "src/uqm/gameev.c"], {
        "events": [{"event": n, "id": vals[n]} for n in names],
    }, ["Event ids used by the game clock (AddEvent, EventHandler in "
        "gameev.c). What each event does is code; see docs/uqm-data.md."])


# ------------------------------------------------------------------ main

DATASETS = {
    "starmap": extract_starmap,
    "planet-types": extract_planets,
    "elements": extract_elements,
    "lifeforms": extract_lifeforms,
    "ships": extract_ships,
    "races": extract_races,
    "game-state": extract_game_state,
    "new-game": extract_new_game,
    "modules": extract_modules,
    "constants": extract_constants,
    "events": extract_events,
}


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("--uqm", required=True,
                    help="path to the sc2 directory of UQM 0.8.0")
    ap.add_argument("--out", default="data", help="output directory")
    ap.add_argument("--only", nargs="*", choices=sorted(DATASETS),
                    help="extract only these datasets")
    ap.add_argument("--keep-build", action="store_true",
                    help="keep the scratch C files for inspection")
    args = ap.parse_args()
    if shutil.which("gcc") is None:
        sys.exit("gcc is required")
    build = tempfile.mkdtemp(prefix="crateris-extract-")
    try:
        uqm = Uqm(args.uqm, build)
        commit = uqm.commit()
        if commit is None:
            print("warning: can't verify the UQM commit (not a git checkout "
                  "of the UQM repository); _source.commit will be null",
                  file=sys.stderr)
        elif commit != PINNED_COMMIT:
            print(f"warning: UQM tree is at {commit}, expected "
                  f"{PINNED_COMMIT}", file=sys.stderr)
        os.makedirs(args.out, exist_ok=True)
        for name in args.only or DATASETS:
            data = DATASETS[name](uqm)
            path = os.path.join(args.out, name + ".json")
            with open(path, "w") as f:
                json.dump(data, f, indent=2)
                f.write("\n")
            print(f"wrote {path}")
    finally:
        if args.keep_build:
            print(f"scratch files kept in {build}")
        else:
            shutil.rmtree(build, ignore_errors=True)


if __name__ == "__main__":
    main()
