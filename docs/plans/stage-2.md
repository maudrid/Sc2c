# Stage 2 plan: solar systems and planets

**Status:** approved by the owner on 2026-11-01, with all six recommendations accepted (see "Decisions"). In progress: items 1 to 3 are built (see "Progress").

Build order item: "Solar systems and planets from seeds" (`docs/design.md`).

## Goal

Players fly from hyperspace into a star system, around it and its planets, into orbit around a planet or moon, and back out to hyperspace, each on their own. Every system's planets and moons, and each world's minerals, energy and life, are generated from the star's seed exactly as UQM 0.8.0 generates them. The host sends seeds, not planet data. Several players can be in one system at once.

### What the owner will be able to try when it's done

- Fly into Sol from hyperspace and see its nine planets laid out as in UQM. Fly to Earth and see its moon; go into orbit around Earth and read its planet report (mass, radius, gravity, temperature, weather, tectonics).
- Scan a planet and see where its mineral, energy and life nodes are.
- Fly out of the system and be back in hyperspace next to the star.
- Do all of that with a friend: both in the same system, one in orbit while the other flies around; a guest who joins while the host is in orbit undocks next to the host's planet.

## Scope

**In stage 2:**

- Entering and leaving star systems from hyperspace.
- Flight inside a system (UQM calls it interplanetary or IP flight): the outer system with its zoom levels, the inner system around one planet and its moons.
- Generating every system: sun, planets, moons, their types, orbits and seeds, and each world's planetary data (`PLANET_INFO`: temperature, gravity, weather and so on).
- The layouts of the 28 special systems (Sol, Procyon, Spathiwa and the rest): which planets and moons they have, so these systems look right.
- Orbiting a planet or moon, and its planet report.
- Scanning: generating mineral, energy and life nodes for a world, and showing where they are.
- World state for planets: which worlds have been scanned, and which nodes have been taken. Nothing can be taken until the lander exists, but the state goes in now, so it is shared from the start.
- Per-player activities on the server (hyperspace, outer system, inner system, orbit), saved in the host save.
- A placeholder client view for systems and orbit, like stage 1's.

**Not in stage 2** (and where it goes):

| Left out | Why | Goes to |
| --- | --- | --- |
| Landing, mining, lifeforms, hazards, the shared landing screen | A big system of its own (`lander.c` is 2,100 lines), with combat-like rules | Stage 2b (decision 1) |
| Alien ships flying in systems (`ip_group_q`) and contact with them, including Fwiffo at Pluto | Contact starts a conversation or a battle, which don't exist yet. Systems are empty of aliens until then (decision 5) | Stages 3 and 4 |
| Story hooks in special systems: NPCs, artifacts, ruins, pickups, Fwiffo at Pluto | Story flags and conversations | Stage 3 |
| The starbase at Earth | It's a conversation | Stage 3 |
| Star and planet names, planet report wording, drawing planets as UQM does (rotating spheres, surface maps) | UQM content | The content stage between 2 and 2b (decision 2) |
| Autopilot, QuasiSpace, the star map screen | Not needed to explore a system | Later; QuasiSpace with stage 3 (it's story-gated) |

## How UQM does it

Paths are relative to `sc2/src/uqm/`.

- **Entering a system:** in hyperspace the flagship collides with a star (`hyper_collision`, `InterplanetaryTransition` in `hyper.c`). It arrives at the edge of the outer system, on the side it came from.
- **Generation** (`planets/solarsys.c` `LoadSolarSys`, `planets/orbits.c` `FillOrbits`, `planets/calc.c`, `planets/generate/`):
  - The system RNG (`SysGenRNG`, a `RandomContext` from `libs/math/random2.c`) is seeded with the star's seed `MAKE_DWORD (x, y)`.
  - The sun gets a `rand_seed` from it, then the system's generator places the planets: `gendefault.c` uses `FillOrbits`, and special systems override it.
  - Each planet gets a type, an orbit and its own `rand_seed`, and `DoPlanetaryAnalysis` computes its `PLANET_INFO` from those.
  - Moons are generated when the player enters a planet's inner system (`GenerateMoons`, seeded with the planet's seed).
  - The order of RNG calls matters, so the port has to make the same calls in the same order.
- **Flight inside a system** is a different model from hyperspace:
  - Speed: `flagship_inertial_thrust` in `planets/solarsys.c` uses a top speed of `(thrusters + 5) × 8` and a thrust step of 8.
  - Turning: a turn wait of `17 − 2 × jets`.
  - Rate: it runs at **30 frames a second** (`IP_FRAME_RATE`), not 24.
  - Cost: it burns **no fuel**.
- **Zoom and planet contact** (`CheckShipLocation`):
  - In the outer system the view zooms in and out as the ship moves (`FindRadius`, `ScaleSystem`). Leaving the screen at the widest zoom returns to hyperspace (`END_INTERPLANETARY`).
  - Touching a planet in the outer system enters its inner system (`enterInnerSystem`). Touching a world in the inner system enters orbit (`enterOrbital`).
- **Orbit and scanning** (`planets/planets.c`, `scan.c`, `surface.c`, `report.c`):
  - The planet report shows `PLANET_INFO`.
  - Mineral, energy and life nodes are generated from the world's seed and per-scan seeds (`GenerateMineralDeposits`, `GenerateLifeForms`, `GenerateRandomNodes`). Their positions and types are deterministic.
  - Taken nodes are recorded per star in the star info state file (`state.c`, `doc/devel/statefiles`).

## Co-op rules for stage 2

From decisions already made (`docs/design.md`, `docs/differences.md`):

- Each player is in their own activity: hyperspace, a system's outer system, a planet's inner system, or orbit. Several players can share a system.
- The clock runs at the one constant rate in every activity (row 1), including in systems and in orbit.
- Scanned worlds and taken nodes are world state, shared by everyone (porting inventory, "Orbit and scanning").
- A guest who joins while the host isn't flying appears at the host's position in the surrounding solar system (row 14). For orbit, that's the inner system of the host's planet.
- Cargo trading within a system (row 22) needs cargo, which comes with the lander; not in stage 2.

New for stage 2 (decisions 3, 4 and 6):

- **Who you see:** players see the other players in the same view: the same outer system, or the same planet's inner system. A player in orbit is shown as a marker at their world. Players in another system or in hyperspace aren't shown.
- **Two frame rates:** the host keeps stepping hyperspace at 24 frames a second and steps players in systems at 30, each activity at its own rate, so both match UQM frame for frame.
- **Joining while the host is in orbit:** the guest's escort appears in the inner system next to the host's world, and is shown undocking there.

## Work, in order

Each item ends with tests that run headless. Where UQM code produces the answer, the test compares against traces recorded from UQM's own code, as in stage 1 (`tools/extract/golden_uqm.py`).

1. **System RNG.** Port `RandomContext` (`libs/math/random2.c`). Check: sequences recorded from UQM for many seeds.
2. **Generating every system.** Port the generation pipeline: sun seed, `FillOrbits` and its star-colour distributions, planet types, orbits and seeds, `DoPlanetaryAnalysis` and the `calc.c` helpers, and moons. Check: a reference file recorded by running UQM's own generators for **all 502 stars**, every planet and moon of every system with type, orbit radius, position, seed and planet data. Any difference in any system fails the test.
3. **Special-system layouts.** Port the layout parts (planets, moons, orbital data) of the 28 special generators in `planets/generate/`, using the new-game story state. Story hooks are left as stubs for stage 3. Check: included in item 2's all-stars file, since those stars use their own generators there.
4. **Mineral, energy and life nodes.** Port node generation (`surface.c`, the generators' `generateMinerals`, `generateEnergy` and `generateLife` defaults). Check: node lists recorded from UQM for a large sample of worlds (several hundred, including special worlds).
5. **Entering and leaving systems.** Port the hyperspace-to-system transition and the way back out: where the ship appears, and where it reappears in hyperspace. Check: recorded traces for arriving from several directions and leaving at several points.
6. **Flight inside a system.** Port outer and inner system flight: `flagship_inertial_thrust`, `ProcessShipControls`, zoom changes, entering an inner system and orbit, and leaving each. Check: frame-by-frame traces recorded from UQM's code, as for hyperspace.
7. **Orbit and the planet report.** The server side of being in orbit: which world, its report data, leaving orbit. Check: report values against item 2's file.
8. **Planet world state.** Scanned worlds and taken nodes per star, shared, in the host save. Check: unit tests and save round trips.
9. **Server activities.** A per-player activity state machine (hyperspace, outer system, inner system, orbit) with each activity stepped at its own frame rate, snapshots per activity, and the join rules above. Save format version 2 (version 1 saves still load). Check: unit tests, plus the network test extended to two players in one system and a guest joining while the host is in orbit.
10. **Client views.** Placeholder system view (sun, orbit rings, planets as circles in their UQM colours), inner system view with moons, orbit screen with the planet report and scan dots, HUD. Check: screenshots under a virtual display.
11. **Docs.** `docs/how-to-try/stage-2.md`; update the porting inventory, differences and data docs.

Rough size: items 2, 4 and 6 are the large ones. Everything else is small to medium.

## Progress

| Item | State | Where |
| --- | --- | --- |
| 1. System RNG | Built | `src/Crateris.Core/Systems/SysRandom.cs`; checked against sequences for 52 seeds |
| 2. Generating every system | Built | `Orbits.cs`, `PlanetAnalysis.cs`, `SystemGenerator.cs`; all 502 systems, 3,800 planets and moons match UQM (`StarSystemTests`) |
| 3. Special-system layouts | Built | In `SystemGenerator.cs`, checked by the same all-stars test |
| 4 to 11 | Not started | |

How to try what is built so far: `docs/how-to-try/stage-2.md`.

Found while porting, all kept as UQM does them:

- UQM's random generator isn't quite textbook Park–Miller: when the intermediate value goes negative it wraps as an unsigned number and subtracts `M`, which lands 2 higher than the textbook result. Kept, since every system depends on it.
- The planet report and the orbit colours analyse a planet differently (the report adds a greenhouse bonus, which uses up random numbers), so a planet's report can show a different temperature from the one its orbit colour suggests, and its other values come from a shifted sequence too. Both ways are ported.
- Some special generators move a planet to a new orbit but keep the seed of its old position (for example the Androsynth and Ilwrath homeworlds); a few compute a new one (the Druuge and VUX worlds). Ported as written.
- At the Maidens' star UQM runs the default generator twice, using up random numbers; ported as written.

## Decisions

All six recommendations were accepted on 2026-11-01.

1. **Landing and mining are stage 2b,** straight after stage 2 and the content stage. Stage 2b covers the lander: landing fuel, crew losses, creatures, hazards, cargo and the shared landing screen.
2. **Content download is its own small stage between 2 and 2b:** the first-run download with its notice (`docs/design.md`, "First-run content download") and a minimal content reader, names first, then planet colour maps. Until then, stage 2 shows numbers and placeholder shapes.
3. **Who you see in a system:** players in the same view only (the same outer system, or the same planet's inner system). Players in orbit are shown as markers at their world.
4. **Two frame rates:** systems run at 30 frames a second and hyperspace at 24, as in UQM.
5. **Systems stay empty of alien ships** until conversations exist in stage 3. That includes Fwiffo at Pluto.
6. **Joining while the host is in orbit:** the guest undocks in the inner system next to the host's world.

## Risks

- **Exact generation:** the RNG call order in generation is easy to get subtly wrong. That's why item 2 checks every system on the map, not a sample.
- **Special generators and story state:** some layouts depend on story flags (for example, shields over Earth and Procyon). Stage 2 uses the new-game state; layouts that change with the story get revisited in stage 3.
- **The outer system's zoom:** its rules affect where the ship ends up when entering and leaving systems, so it's traced frame by frame, not approximated.
