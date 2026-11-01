# How to try it: stage 2, solar systems and planets (in progress)

Stage 2 is being built in the order of `docs/plans/stage-2.md`. This page grows with it.

## So far: every star system, generated as UQM does it

Items 1 to 7 are built: the system random number generator; the generation of every system's planets, moons and planet data, including the special systems (Sol, Spathiwa, the Chmmr, the Sa-Matra and the rest); entering orbit, which gives a world's planet report (the numbers on UQM's orbit screen) and its mineral, energy and life nodes; entering and leaving systems from hyperspace; and flying inside a system (zooming, inner systems, orbit). There is nothing new on screen yet; the system views come with item 10.

Set up and build as for stage 1 (`docs/how-to-try/stage-1.md`), then:

```
dotnet test --filter StarSystemTests   # systems against UQM: 3 tests
dotnet test --filter SurfaceTests      # orbit, report and nodes against UQM: 4 tests
dotnet test --filter SystemTransitionTests   # entering and leaving systems: 5 tests
dotnet test --filter IpFlightTests    # flight inside systems: 10 tests
dotnet test                            # everything: 48 tests
```

`EveryStarSystemMatchesUqm` generates all 502 systems and compares every planet and moon (type, orbit, position, seed, number of moons) and every world's planet data (temperature, gravity, tectonics, weather, atmosphere, rotation, tilt and so on) with what UQM's own code produces. If anything differs, the failure lists the star, the world and the value.

`EveryWorldsOrbitAndNodesMatchUqm` enters orbit around all 3,806 worlds and compares the planet report, the scan seeds, every node (about 32,000) and the orbit screen's text with UQM's own code. `EarthsReportReadsAsInUqm` checks Earth reads 1.00 a.u., 1.00 atm, 22° c, weather and tectonics class 2, 1.00 Earth mass, radius and gravity, a 1.00-day day and 23° tilt, as on UQM's orbit screen. `SolHasItsStoryNodes` checks a few you'd recognise: Earth's real report values and its shield (no nodes), the Moon base with its 10 Brainbox Bulldozers, and Fwiffo's spot on Pluto.

`SystemTransitionTests` flies a ship into Sol, checks it arrives where UQM puts it (bottom middle of the outer system, facing up), then leaves and checks it comes out on the star, keeps its heading, doesn't fall straight back in, and can enter again after flying clear and turning round.

`IpFlightTests` replays eight flights recorded from UQM's own code, frame by frame: in Sol, flying to Earth and into orbit there, then to Jupiter; to Mercury and Pluto in a ship with no thrusters or turning jets; to Saturn in a fast ship; and five other systems. Each frame's facing, speed, position, zoom and events must match.

To record the reference again from the UQM source (needs gcc and a checkout of UQM 0.8.0):

```
python3 tools/extract/golden_uqm.py --uqm ../uqm-0.8.0/sc2
git diff --stat tests/golden   # should show no changes
```
