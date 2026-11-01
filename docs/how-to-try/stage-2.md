# How to try it: stage 2, solar systems and planets (in progress)

Stage 2 is being built in the order of `docs/plans/stage-2.md`. This page grows with it.

![Orbiting Earth's Moon after an auto scan: the planet report, and the Moon's minerals (small dots), its one energy node (red) and the 10 Brainbox Bulldozers (green)](../screenshots/stage2-orbit-scan.png)

## So far: every star system, generated as UQM does it

Items 1 to 10 are built: the system random number generator; the generation of every system's planets, moons and planet data, including the special systems (Sol, Spathiwa, the Chmmr, the Sa-Matra and the rest); entering orbit, which gives a world's planet report (the numbers on UQM's orbit screen) and its mineral, energy and life nodes; entering and leaving systems from hyperspace; flying inside a system (zooming, inner systems, orbit); the shared state of planets (which nodes have been taken), kept in the host save; all of it running in the game for every player (item 9); and the views (item 10): the outer and inner system views and the orbit screen with UQM's orbit and scan menus.

The drawings are placeholders until the content stage brings UQM's images and words: worlds are plain circles in a colour for their kind, the planet report's labels are our own wording, and the scan dots are coloured circles. Positions, sizes, colours of the orbits and the timing of the scan are UQM's.

Set up and build as for stage 1 (`docs/how-to-try/stage-1.md`), then:

```
dotnet test --filter StarSystemTests   # systems against UQM: 3 tests
dotnet test --filter SurfaceTests      # orbit, report and nodes against UQM: 6 tests
dotnet test --filter SystemTransitionTests   # entering and leaving systems: 9 tests
dotnet test --filter IpFlightTests    # flight inside systems: 10 tests
dotnet test --filter PlanetWorldStateTests   # taken nodes: 15 tests
dotnet test --filter ActivityTests    # hyperspace, systems and orbit in the game: 15 tests
dotnet test --filter SystemLayoutTests # the system views' layout against UQM: 3 tests
dotnet test --filter OrbitMenuTests    # orbit and scan menus, the scan: 10 tests
dotnet test --filter SmoothedMotionTests     # ships in systems drawn moving evenly: 4 tests
dotnet test                            # everything: 108 tests
python3 tools/test_network.py          # real host and guests: a guest joins while the host is in orbit, the host scans and leaves
```

`EveryStarSystemMatchesUqm` generates all 502 systems and compares every planet and moon (type, orbit, position, seed, number of moons) and every world's planet data (temperature, gravity, tectonics, weather, atmosphere, rotation, tilt and so on) with what UQM's own code produces. If anything differs, the failure lists the star, the world and the value.

`EveryWorldsOrbitAndNodesMatchUqm` enters orbit around all 3,806 worlds and compares the planet report, the scan seeds, every node (about 32,000) and the orbit screen's text with UQM's own code. `EarthsReportReadsAsInUqm` checks Earth reads 1.00 a.u., 1.00 atm, 22° c, weather and tectonics class 2, 1.00 Earth mass, radius and gravity, a 1.00-day day and 23° tilt, as on UQM's orbit screen. `SolHasItsStoryNodes` checks a few you'd recognise: Earth's real report values and its shield (no nodes), the Moon base with its 10 Brainbox Bulldozers, and Fwiffo's spot on Pluto.

`SystemTransitionTests` flies a ship into Sol, checks it arrives where UQM puts it (bottom middle of the outer system, facing up), then leaves and checks it comes out on the star, keeps its heading, doesn't fall straight back in, and can enter again after flying clear and turning round.

`IpFlightTests` replays eight flights recorded from UQM's own code, frame by frame: in Sol, flying to Earth and into orbit there, then to Jupiter; to Mercury and Pluto in a ship with no thrusters or turning jets; to Saturn in a fast ship; and five other systems. Each frame's facing, speed, position, zoom and events must match.

`PlanetWorldStateTests` takes nodes on the Moon and checks they're gone from what's left on its surface for every player, survive a save and load, and that older saves and bad worlds in a save are handled.

`EverySystemIsLaidOutAsUqmDrawsIt` lays out all 502 systems at every zoom, and every planet's inner view with its moons, and compares each orbit ellipse, world position and world size with what UQM's own drawing code computes.

`OrbitMenuTests` works the orbit menu as UQM's PC menus do (the cursor wraps round; Scan opens the scan menu on Auto scan; Navigate leaves orbit; Starmap, the manifest, the game menu and the lander say they come later), checks Earth's shield refuses scans and Jupiter refuses the lander, and checks the scan: drawn line by line in 1.7 seconds a scan type (the first line after one wait, as in UQM), each node flashing as the line passes it, the auto scan doing all three and then moving to Dispatch lander, a key finishing the scan being drawn, and a scan showing only what's left, with nodes a friend takes disappearing.

## Try it in the game

Build as for stage 1, then start a host already in orbit round Earth's Moon, and a guest:

```
godot --path game -- --host --name Host --start-orbit 82:2:1
godot --path game -- --join 127.0.0.1 --name Guest --id guest-1
```

On the host you see the orbit screen. In the menu on the left, arrows move, Enter selects and Esc goes back:

- **Scan** opens the scan menu with the planet report. **Auto scan** (where the cursor starts) scans for minerals, energy and life in turn, each drawn line by line; press any key to finish the one being drawn. Or pick one of the three scans. **Exit scan** or Esc goes back.
- **Navigate** leaves orbit. The others (Starmap, Manifest, Game menu, Dispatch lander) say they come in a later stage. Earth (`82:2`) has a shield, so scanning it is refused.

The guest joins in Earth's inner system, next to the host's world: Earth in the middle with the arc of its orbit, the starbase (grey square) and the Moon, and the host as a marker at the Moon. When the host leaves orbit, both ships fly in the inner system. Fly away from Earth to reach the outer view of Sol, and off the edge of the screen (through the outer view's zooms) to come back to hyperspace on Sol. Fly into a world to orbit it.

![The guest in Earth's inner system, just undocked; the host's ship has left orbit](../screenshots/stage2-inner-system.png)

![The guest in Sol's outer view, the host orbiting Neptune shown as a marker](../screenshots/stage2-outer-system.png)

![An energy scan being drawn on the Moon](../screenshots/stage2-scan-sweep.png)

`82` is Sol's star number; planets count from 0 (Earth is 2, its Moon is `82:2:1`, the starbase `82:2:0`). `--start-orbit` also works with a loaded save; a world that can't be orbited (a starbase, say) is refused with a message in the top-left.

The screenshots were taken under a virtual display, as the checks for this item:

```
xvfb-run -a -s "-screen 0 1024x768x24" godot --rendering-driver opengl3 --path game -- \
  --host --no-upnp --name Host --start-orbit 82:2:1 \
  --script "none:10,select:2,select:2,none:200" --quit-after 230 --screenshot scan.png
```

`--script` drives the menu with `up`, `down`, `select` and `cancel` (each pressed once as its step starts) as well as the flight keys.

To record the reference again from the UQM source (needs gcc and a checkout of UQM 0.8.0):

```
python3 tools/extract/golden_uqm.py --uqm ../uqm-0.8.0/sc2
git diff --stat tests/golden   # should show no changes
```
