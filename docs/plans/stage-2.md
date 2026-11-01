# Stage 2 plan: solar systems and planets

**Status:** approved by the owner on 2026-11-01, with all six recommendations accepted, and three more decisions made during the work (nine in all; see "Decisions"). Built: all 11 items are done (see "Progress"). How to try it: `docs/how-to-try/stage-2.md`.

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
- World state for planets: which nodes have been taken (scans aren't remembered, as in UQM). Nothing can be taken until the lander exists, but the state goes in now, so it is shared from the start.
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

- **Entering a system:** in hyperspace the flagship collides with a star (`hyper_collision`, `InterplanetaryTransition` in `hyper.c`). It always arrives at the bottom middle of the outer system at the widest zoom, facing up and at rest, whichever way it came in (`InitSolarSys`). (An earlier version of this plan said it arrives on the side it came from; UQM's code says otherwise.)
- **Leaving a system:** flying off the screen at the widest zoom ends interplanetary flight. Back in hyperspace the ship sits exactly on the star, at rest, keeping its system heading; it can't fall back into that star until it has cleared it once (`CurStarDescPtr` and `hyper_death`).
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
- Taken nodes are world state, shared by everyone, so a scan always shows what's left, including what a friend has just taken (porting inventory, "Orbit and scanning").
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
8. **Planet world state.** Taken nodes per world, shared, in the host save. Check: unit tests and save round trips.
9. **Server activities.** A per-player activity state machine (hyperspace, outer system, inner system, orbit) with each activity stepped at its own frame rate, snapshots per activity, and the join rules above. Save format version 4 (older saves still load). Versions 2 and 3 already exist: 2 added the star each ship has just left (review of items 4 to 7), 3 added planet world state (item 8). Check: unit tests, plus the network test extended to two players in one system and a guest joining while the host is in orbit.
10. **Client views.** Placeholder system view (sun, orbit rings, planets as circles in their UQM colours), inner system view with moons, orbit screen with the planet report and scan dots, HUD. Check: screenshots under a virtual display.
11. **Docs.** `docs/how-to-try/stage-2.md`; update the porting inventory, differences and data docs.

Rough size: items 2, 4 and 6 are the large ones. Everything else is small to medium.

## Progress

| Item | State | Where |
| --- | --- | --- |
| 1. System RNG | Built | `src/Crateris.Core/Systems/SysRandom.cs`; checked against sequences for 52 seeds |
| 2. Generating every system | Built | `Orbits.cs`, `PlanetAnalysis.cs`, `SystemGenerator.cs`; all 502 systems, 3,800 planets and moons match UQM (`StarSystemTests`) |
| 3. Special-system layouts | Built | In `SystemGenerator.cs`, checked by the same all-stars test |
| 4. Mineral, energy and life nodes | Built | `Surface.cs` (`SolarSystem.EnterOrbit`): scan seeds, the planet report as `generateOrbital` leaves it, and every node; all 3,806 worlds and about 32,000 nodes match UQM (`SurfaceTests`) |
| 5. Entering and leaving systems | Built | `SystemTransition.cs` (arrival and leaving), `IpGeometry.cs` (screen and zoom geometry, checked against UQM: `tests/golden/ip-geometry.json`), `Hyperspace/StarContact.cs` (when a ship touches a star; provisional circles until the content stage, `docs/differences.md` rows 23 and 24) |
| 6. Flight inside a system | Built | `IpFlight.cs`: controls and thrust, zoom levels, entering and leaving inner systems, touching a world to enter orbit, coming back from orbit, leaving the system. Eight flights (3 in Sol, 5 elsewhere, 14,000 frames) match UQM frame by frame (`IpFlightTests`, `tests/golden/ip-flight.json`). Contact uses provisional circles (`docs/differences.md` row 25) |
| 7. Orbit and the planet report | Built | `PlanetReport.cs`: the orbit screen's numbers (orbit, atmosphere, temperature, weather, tectonics, mass, radius, gravity, day, tilt) in UQM's units, on `OrbitData.Scan`. Checked against the text UQM's own `PrintCoarseScanPC` prints for all 3,800 worlds that can be orbited (`tests/golden/world-surfaces.json`). Entering orbit came with items 4 and 6, leaving it with item 6 (`IpFlight.ReturnFromOrbit`) |
| 8. Planet world state | Built | `World/PlanetWorldState.cs` on `GameWorld.Planets`: taken nodes per world as UQM's three 32-bit retrieve masks (`isNodeRetrieved`, `setNodeRetrieved`, `countNodesRetrieved`), and the nodes still on a world. Scans aren't remembered, as in UQM: a scan shows what's left at that moment, so while a friend collects on the surface, each new scan shows fewer nodes (a first version remembered scans; withdrawn, `docs/differences.md` row 26). One for the whole game, so every player sees the same. Saved in the host save (version 3; older saves load with nothing touched); bad worlds in a save are refused (`PlanetWorldStateTests`) |
| 9. Server activities | Built | `GameWorld`: each player is in hyperspace, a system's outer view, an inner system or orbit (`PlayerShip.Activity`). Flying into a star enters its system; leaving it comes out on the star; touching a world enters orbit and the action key (Enter) leaves it. The world steps at 24 frames a second; players in systems take 30 system frames a second, one or two per world frame. Snapshots carry each ship's activity and system position, and the taken nodes of orbited worlds; clients generate systems themselves from the star. A guest's first join in a system undocks there, and while the host is in orbit next to the host's world. Save version 4 keeps ships in systems and orbit; protocol version 2. `--start-orbit star:planet[:moon]` puts the host in orbit at start, in a new or loaded game (testing). Checked by `ActivityTests` and the network test's new stage 2 phase |
| 10. Client views | Built | `game/scripts/SystemView.cs`: the outer view (sun, orbit ellipses in UQM's five temperature colours, planets at the ship's zoom) and the inner view (the planet, the arc of its own orbit, its moons), laid out by `Systems/SystemLayout.cs`, which the flight now uses too; every star at every zoom and every inner view match what UQM's own `DrawSystem` would draw (`SystemLayoutTests`, `tests/golden/system-layout.json`). Other players in the same view are drawn like your ship, with names; players in orbit are markers at their world; movement is smoothed (`SmoothedMotion`). The orbit screen (`OrbitView`): the world, UQM's PC orbit menu, and in the scan menu the planet report and the scan map, scanned line by line at UQM's speed with the dots flashing as UQM's do (`Systems/OrbitMenu.cs`, `OrbitMenuTests`). Navigate leaves orbit; entries for later stages say so. Screenshots under a virtual display in `docs/how-to-try/stage-2.md`; the network test now scans the Moon and leaves orbit through the menu |
| 11. Docs | Built | `docs/how-to-try/stage-2.md` rewritten as the stage's guide (solo walkthrough of Sol, Earth and the Moon, playing with friends, what the tests check, known limits); README, `docs/design.md` (build order), `docs/porting-inventory.md`, `docs/differences.md` (change history) and `docs/uqm-data.md` (references and data used) brought up to date |

Found while porting, all kept as UQM does them:

- UQM's random generator isn't quite textbook Park–Miller: when the intermediate value goes negative it wraps as an unsigned number and subtracts `M`, which lands 2 higher than the textbook result. Kept, since every system depends on it.
- The planet report and the orbit colours analyse a planet differently (the report adds a greenhouse bonus, which uses up random numbers), so a planet's report can show a different temperature from the one its orbit colour suggests, and its other values come from a shifted sequence too. Both ways are ported.
- Some special generators move a planet to a new orbit but keep the seed of its old position (for example the Androsynth and Ilwrath homeworlds); a few compute a new one (the Druuge and VUX worlds). Ported as written.
- At the Maidens' star UQM runs the default generator twice, using up random numbers; ported as written.
- Item 4 had to take in part of item 7: a world's scan seeds come from its system's `generateOrbital`, which also overrides the report for special worlds (Sol's real planets, homeworlds). So `EnterOrbit` gives the report values too; item 7 added the numbers as the orbit screen shows them.
- UQM's two report styles (PC text and 3DO graphics, an option) show the same numbers, so the server sends the numbers and the client picks the style. Item 10 shows the PC style with placeholder labels until the content stage.
- UQM has two menu styles too (PC and 3DO, an option). Item 10 ports the PC menus, UQM's default: the orbit menu is Scan, Starmap, Manifest (Cargo, Devices, Roster), Game menu and Navigate; the scan menu is Mineral, Energy, Biological, Auto scan, Dispatch lander and Exit, opening on Auto scan, which moves to Dispatch lander when done. Starmap, the manifest's entries, the game menu and the lander are there but say they come in a later stage. A key during a scan finishes the scan type being drawn, as in UQM.
- UQM's outer view draws the orbit ellipses lower than the sun (`GetOrbitRect` with `DISPLAY_FACTOR / 4`), with the planets on their ellipses. Kept, since the positions of planets on screen decide when a ship touches them.
- Smoothing (decision 8) draws ships in systems on the local clock, one and a half world frames and one system frame behind the newest snapshot (about 95 ms), so the 30-a-second frames are spread evenly even when snapshots arrive unevenly over the network. A ship back in a view after a gap (orbit, or lost snapshots) isn't blended with where it was. Steering is still sent at once.
- Special worlds use their own seed order: Sol and Spathiwa draw minerals before life, the Earth colony's planet gets no scan seeds at all (it's shielded, so it can't be scanned anyway). Every world in the Spathi system except Spathiwa has no life, and every world in a Mycon system has none.
- Story nodes are generated as at the start of a new game, with nothing picked up: ruins (16 energy nodes) on the old homeworlds, artifacts (one energy node) such as the Burvixese broadcasters, the Sun Device and the Utwig bomb, Fwiffo on Pluto and the Moon base. Picking them up is stage 2b/3.
- The flight traces are recorded with the same circle model as the engine, built into the recording program, so everything except the circle sizes is checked against UQM's own code. One rule isn't reached by the recorded flights: leaving an inner system while already touching another planet, where UQM turns contacts off until the ship is clear. It's ported as written but not checked by a trace.
- Players in systems run 30 frames a second as 1 or 2 system frames per 24-a-second world frame (five every four). Every frame is UQM's, but they arrive in that rhythm rather than evenly spaced; the client can smooth it when drawing.
- Touching a world where UQM starts a conversation (starbases, the Sa-Matra, the Umgah homeworld, the Slylandro gas giant) leaves the ship flying beside it until stage 3 (`docs/differences.md` row 27).
- Autopilot isn't ported (the star map comes later); without it, UQM's controls are the only input.
- A new game puts players in hyperspace on Sol (stage 1), so the new game marks Sol as the star just left, and a guest undocking from the host inherits it; otherwise the first thrust would drop them straight into the Sol system. The star just left is kept in the host save.
- Until conversations exist (stage 3) no aliens are at their homeworlds, so a homeworld is orbited as when its owners are gone, as decided in decision 5. Two worlds can't be orbited even so, because UQM always starts a conversation there: the Umgah homeworld (the Talking Pet's planet) and the Slylandro gas giant. Starbases and the Sa-Matra can't be orbited either.

## Decisions

All six recommendations were accepted on 2026-11-01 (decisions 1 to 6); decisions 7 to 9 were made before item 10.

1. **Landing and mining are stage 2b,** straight after stage 2 and the content stage. Stage 2b covers the lander: landing fuel, crew losses, creatures, hazards, cargo and the shared landing screen.
2. **Content download is its own small stage between 2 and 2b:** the first-run download with its notice (`docs/design.md`, "First-run content download") and a minimal content reader, names first, then planet colour maps. Until then, stage 2 shows numbers and placeholder shapes.
3. **Who you see in a system:** players in the same view only (the same outer system, or the same planet's inner system). Players in orbit are shown as markers at their world.
4. **Two frame rates:** systems run at 30 frames a second and hyperspace at 24, as in UQM.
5. **Systems stay empty of alien ships** until conversations exist in stage 3. That includes Fwiffo at Pluto.
6. **Joining while the host is in orbit:** the guest undocks in the inner system next to the host's world.

Decided before item 10, on 2026-11-01:

7. **Worlds that start a conversation** (Earth's and the Syreen starbase, the Umgah homeworld, the Slylandro gas giant, the Sa-Matra): until stage 3, nothing happens. The ship stays flying beside the world, with no message (`docs/differences.md` row 27).
8. **Other players in a system view** are drawn like your own ship, with their names, in the outer view or the inner system they're in; players in orbit show as a marker at their world. Movement is drawn smoothly: the 30-a-second system frames, which arrive one or two per world frame, are spread out evenly on screen.
9. **Scanning works as in UQM** (the permanent orbit screen, not a temporary shortcut): from orbit, Scan opens the scan menu (mineral, energy, biological or auto scan), and the scan is drawn line by line. Entries that need later stages are shown and say so. Only the drawing is placeholder until the content stage.

## Risks

- **Exact generation:** the RNG call order in generation is easy to get subtly wrong. That's why item 2 checks every system on the map, not a sample.
- **Special generators and story state:** some layouts depend on story flags (for example, shields over Earth and Procyon). Stage 2 uses the new-game state; layouts that change with the story get revisited in stage 3.
- **The outer system's zoom:** its rules affect where the ship ends up when entering and leaving systems, so it's traced frame by frame, not approximated.
