# Stage 2 plan: solar systems and planets

**Status:** draft for the owner's review. Nothing here is built yet. Decisions in the "Open questions" section are needed before or during the work; each has a recommendation.

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
| Landing, mining, lifeforms, hazards, the shared landing screen | A big system of its own (`lander.c` is 2,100 lines), with combat-like rules | Stage 2b, if you agree (open question 1) |
| Alien ships flying in systems (`ip_group_q`) and contact with them | Contact starts a conversation or a battle, which don't exist yet | Stages 3 and 4 |
| Story hooks in special systems: NPCs, artifacts, ruins, pickups, Fwiffo at Pluto | Story flags and conversations | Stage 3 |
| The starbase at Earth | It's a conversation | Stage 3 |
| Drawing planets as UQM does (rotating spheres, surface maps) | Needs the UQM content's colour tables | When content download exists (open question 2) |
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

New for stage 2, proposed (see open questions 3 to 6):

- **Who you see:** players see the other players in the same view: the same outer system, or the same planet's inner system. A player in orbit is shown as a marker at their world. Players in another system or in hyperspace aren't shown.
- **Two frame rates:** the host keeps stepping hyperspace at 24 frames a second and steps players in systems at 30, each activity at its own rate, so both match UQM frame for frame.

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

## Open questions

1. **Landing and mining: in stage 2 or a stage 2b?** Recommendation: **stage 2b**, straight after stage 2. The lander is its own large system: landing fuel, crew losses, creatures, hazards, cargo, and the shared landing screen. Keeping it separate gets systems and orbit into your hands sooner.
2. **Content download: when?** Planet names, star names, planet graphics and the planet report's wording are all UQM content. Without it, stage 2 shows numbers and placeholder shapes. Recommendation: build the first-run content download and a minimal content reader (names first, then planet colour maps) as a small stage between 2 and 2b, or as the last part of stage 2.
3. **Who you see in a system:** is "same view only, orbiting players shown as markers at their world" right? The alternative is showing every player in the system on the outer system view.
4. **Two frame rates:** OK to step systems at 30 frames a second and hyperspace at 24, matching UQM? The alternative, everything at 24, would make flight inside a system 20% slower than UQM.
5. **Empty systems until stage 3:** alien ships in systems (including Fwiffo at Pluto) won't appear until conversations exist. OK for stage 2?
6. **Joining while the host is in orbit:** the guest's escort appears in the inner system next to the host's world, and is shown undocking there. OK?

## Risks

- **Exact generation:** the RNG call order in generation is easy to get subtly wrong. That's why item 2 checks every system on the map, not a sample.
- **Special generators and story state:** some layouts depend on story flags (for example, shields over Earth and Procyon). Stage 2 uses the new-game state; layouts that change with the story get revisited in stage 3.
- **The outer system's zoom:** its rules affect where the ship ends up when entering and leaving systems, so it's traced frame by frame, not approximated.
