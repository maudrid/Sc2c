# UQM data reference

The parts of UQM 0.8.0 that Attack on Crateris needs as data: where each part lives in the UQM source, what its fields mean, and which file in `data/` it becomes. It also lists the runtime structures the server's own data model replaces, and the parts that stay code or content.

Reference source: official UQM git (`https://git.code.sf.net/p/sc2/uqm`), branch `v0.8.0`, commit `199e3748`. Paths are relative to its `sc2/` directory. UQM's own developer notes in `sc2/doc/devel/` (CC BY 2.0) cover some of this in more depth; the useful ones are named below.

## How the data files are made

`tools/extract/extract_uqm.py` reads the UQM source and writes `data/*.json`. For every value it compiles a small C program against UQM's own headers and tables and prints the result, so macros, packed bit fields and enum values come from the C compiler. Only names, order and source comments are read as text.

Every file starts with `_notice` (GPL notice and UQM copyright), `_source` (repository, branch, commit, files; the commit is null if the source isn't a git checkout of the UQM repository, so it can't be verified) and often `_notes` (units and meanings). Don't edit the files by hand; change the extractor and run it again.

**Regenerate the data:**

```
git clone --depth 1 -b v0.8.0 https://git.code.sf.net/p/sc2/uqm ../uqm-0.8.0
python3 tools/extract/extract_uqm.py --uqm ../uqm-0.8.0/sc2 --out data
```

Needs Python 3 and gcc. Takes about 10 seconds.

**Check the data:**

```
python3 -m unittest discover -s tools/extract -v
UQM_SC2=../uqm-0.8.0/sc2 python3 -m unittest discover -s tools/extract -v
```

The first checks the committed files (counts, known values, notices): 30 checks, three of which need the UQM source and are skipped. The second runs those three too: one re-runs the extractor and confirms the committed files match the source exactly, another checks that the extractor's evaluation of `#define`s accepts only integer constants, and the last that the new game's events are `gameev.c`'s.

## Reference traces from UQM's own code

`tools/extract/golden_uqm.py` builds small C programs in `tools/extract/golden/` against the UQM source, linked with UQM's real code, and records what that code does into `tests/golden/`. The C# port is tested against these files (`dotnet test`), so it must match UQM exactly, not just approximately.

- **`hyperspace-flight.json`:** 9 flight scenarios, frame by frame: thrusting, turning at top speed, spinning in place, running out of fuel, the fuel counter rolling over on long trips, tapping thrust, both turn keys, and fuel above tank capacity. Recorded by running UQM's `ship_preprocess`, `sis_hyper_preprocess`, `inertial_thrust`, `init_sis` and `MoveSIS`; only drawing calls are stubbed.
- **`uqm-math.json`:** `sinetab`, `ARCTAN`, `square_root`, universe/log conversions and velocity operations.
- **`star-systems.json`:** every one of the 502 star systems as UQM generates it at the start of a new game: the sun's seed, every planet and every moon (type, orbit radius, position, seed, number of moons) and each world's planet data (`PLANET_INFO`: temperature, gravity, tectonics, weather, atmosphere and so on), plus `RandomContext` sequences for many seeds. Recorded by `tools/extract/golden/systems.c`, which includes `solarsys.c` and runs UQM's own `LoadSolarSys`, `GenerateMoons`, `FillOrbits` and `DoPlanetaryAnalysis` with every generator in `planets/generate/`. Graphics and strings are stubbed; the game state is all zeros, as in a new game. Planet data is recorded the way the planet report computes it (with the greenhouse bonus); the orbit colour's temperature, computed without it, is recorded too, since the two make different RNG calls.
- **`world-surfaces.json`:** entering orbit around every world: whether orbit is reached, the three scan seeds, the planet report as the system's `generateOrbital` leaves it, and every mineral, energy and life node (position, element or creature, deposit size). Then the orbit screen's report text as UQM's own `PrintCoarseScanPC` prints it (numbers only: labels and unit words are content strings, empty in the recording). Recorded by the same program in `orbits` mode, which runs each star's own `generateOrbital`, then its `generateMinerals`, `generateEnergy` and `generateLife` node by node as `scan.c` does. Conversations, graphics and the rest are do-nothing stand-ins (`tools/extract/golden/stubs.c`); no alien ships are present and nothing has been picked up, so homeworlds are orbited as when their owners are gone.
- **`ip-geometry.json`:** the interplanetary screen and zoom constants, where a ship arriving from hyperspace is put, and samples of UQM's conversions between system locations and the screen.
- **`system-layout.json`:** what UQM's `DrawSystem` would draw for every star: each planet's orbit ellipse, position and image size at each of the outer view's four zooms, and each planet's inner view (the arc of its own orbit, its image size, and its moons' orbits, positions and sizes). Recorded by the same program in `layout` mode, from UQM's own `GetPlanetOrbitRect`, `ValidateOrbits`, `enterInnerSystem` and `ValidateInnerOrbits`.
- **`system-sprites.json`:** the sprites UQM's own `DrawSystem` stamps in every star's views, in the order it draws them: for the outer view at each zoom and each planet's inner view, each stamp's sheet, frame, place and colour table entry (the sun's frame by star size and zoom, each world's by image size and the side its sun is on, the starbase, a shielded planet's shield and the Sa-Matra from the orbit background), and a hash of `CreateStarBackGround`'s background stars. Recorded by the same program in `sprites` mode, whose `DrawStamp` and `SetColorMap` note what they're given. Code only: frames are numbers, nothing is read from the content.
- **`ip-flight.json`:** eight interplanetary flights, frame by frame (3 in Sol, 5 in other systems; about 14,000 frames), recorded by running UQM's own flight code in the order `IP_frame` runs it, with `ipdisp.c` linked for the flagship's movement. A small test pilot in the recording program chooses the input (turn towards a goal planet or moon, thrust when facing it); the engine's tests replay that input. Contact is UQM's own `DrawablesIntersect` (`intersec.c`, `boxint.c`, linked) with the sprites' masks from the package, so recording it needs `--content` (the trace itself is positions and events, not content). Since content stage item 9; until then circles stood in for the masks, and the re-recorded flights match the old ones frame for frame up to the first contact.

- **`string-numbering.json`:** the game strings' numbering from `gamestr.h`: each range's base and count (`STAR_STRING_BASE` and the rest) and `GAMESTR_COUNT`, and `menustat.h`'s numbers for the orbit screen's PC menus (`PM_ALT_SCAN` and the rest).
- **`string-table-cases.json`:** string table inputs written for the test (in `tools/extract/golden_uqm.py`, not UQM content), each with exactly what UQM's own `_GetStringData` makes of it, byte for byte: its quirks with headers, blank lines, line endings, NULs, long lines and the 2,048-string limit.
- **`resource-index-cases.json`:** resource index inputs written for the test, read by UQM's own `PropFile_from_string` (`src/libs/resource/propfile.c`), with its rules for a later id replacing an earlier one and for unknown types. The recorder reads the package's `uqm.rmp` the same way, so it records the tables the engine reads.
- **`string-tables.json`** (needs `--content`): every string table of the official content package as UQM's loader reads it, kept as each string's length and SHA-256 only. The strings themselves are content (CC BY-NC-SA 2.5) and aren't in the repository (content stage, decision 5).
- **`names.json`** (needs `--content`): every name UQM's own code gives with the package's game strings: each star's `GetClusterName`, and for each planet (seen from its inner system) the name `generateName` leaves in `GLOBAL_SIS (PlanetName)`, its `GetPlanetTitle` and `GetPlanetOrMoonName`, and each moon's title and name. Recorded by the systems program's `names` mode; kept as SHA-256 hashes only, as names are content.
- **`fonts.json`** (needs `--content`): every font of the package as UQM's own `_GetFontData` loads it (images read by UQM's own PNG loader), and texts laid out by UQM's `TextRect` and `_text_blt`: texts of our own and the game strings the orbit screen draws. Recorded by `tools/extract/golden/fonts.c` (built with libpng); kept as SHA-256 hashes only, as fonts are content.
- **`images.json`** (needs `--content`): images as UQM's own PNG loader (`TFB_png_to_sdl`, `libs/graphics/sdl/png2sdl.c`) loads them, recorded by `tools/extract/golden/images.c`: images of our own for every path through it (in full, with their results), and up to 40 of each kind of PNG in the package (as hashes). The recorders build UQM's loader against libpng and a small stand-in for SDL 1.2 (`tools/extract/golden/sdl/SDL.h`, `sdl.c`), so SDL isn't needed; libpng's development files are.
- **`surface-heights.json`**: every orbitable world's height map as UQM's own `GeneratePlanetSurface` makes it on entering orbit from a new game (the systems program's `surfaces` mode, which runs it with `plangen.c` and `gentopo.c` and stops once the map is coloured). Code only: SHA-256 per world, and three whole maps.
- **`surface-colors.json`** (needs `--content`): every world's coloured map as UQM's `RenderTopography` draws it with the package's colour maps and translate tables; hashes only.
- **`planetside-heights.json`:** every landable world's planetside map, the 4x height map UQM's `TopoScale4x` makes from its height map (worlds neither shielded nor gas giants), as SHA-256 hashes. Recorded by the systems program's `planetside` mode, which runs each world in a child process of its own so that `frandom` starts afresh for each world, as the engine does (`docs/differences.md` row 29).
- **`planetside-colors.json`** (needs `--content`): those maps as UQM's `RenderTopography` colours them at four times the size, with the package's colour tables; hashes only.
- **`lander.json`:** twelve landings of UQM's own lander, frame by frame (about 7,900 frames): the landing, scripted driving, turning and firing, with and without the speed and shot upgrades, at the poles and across the east-west edge, and the take-off, with `TFB_Random` seeded. Each frame gives the lander's place, facing, height above the ground, crew, its bolts and `TFB_Random`'s seed. Recorded by the `lander` program (`tools/extract/golden/lander.c`), which runs `lander.c`'s `PlanetSide` with the drawing stubbed and a clock that only sleeps move on; code only.
- **`lander-pickups.json`** (needs `--content`): fifteen landings of UQM's own lander among mineral nodes, frame by frame (about 17,300 frames): on the three worlds with the most mineral nodes and no energy or life, and among nodes packed round the site, at each pole, either side of the east-west edge and just across it (touched the nearer way round), with and without the cargo upgrade and firing, and with little storage room left. Each frame adds the nodes left, the lander's hold by category, the mineral text and the nodes taken; after the landing, what the ship got. Contact uses UQM's own `DrawablesIntersect` with the package's masks of `graphics.lander` and `graphics.miscdata` (their sizes, hot spots and hashes are in the file); the positions and pickups are code.
- **`lander-creatures.json`** (needs `--content`): thirteen landings of UQM's own lander among creatures, frame by frame (about 8,100 frames): on nine worlds with creatures and no energy nodes, chosen so every ordinary kind (0 to 22) is on one, with their minerals too; among the most dangerous kinds with 4 crew, without and with the biological shield; and among harmless ones to can and pick up. Life nodes are placed with `generateBioNode`'s random draws; each frame adds every creature's place, stun points, kind or value, sheet and frame, heading wait and flash, and the lander's damage and explosion. Contact uses the package's masks of the lander, the scan dots, the canister and `graphics.life.0` to `25`; the positions and events are code.
- **`lander-hazards.json`** (needs `--content`): twenty-one landings of UQM's own lander among quakes, lightning and lava, frame by frame (about 10,100 frames), with `PlanetSide`'s chances set from the world's tectonics, weather and temperature: on real worlds with their nodes (the shakiest, the stormiest, stormy and hot, hot with life, one with all three, and two milder ones, without and with every shield); on the worst of them with 6 crew, bare, with every shield and with each hazard's shield alone; on a hot world with quakes at each pole and either side of the east-west edge; taking off at once; and three long landings with every shield, with far-off copies of the world's deposits added, where UQM's display queue (150 elements, nodes and all) is full for over 1,400 frames. Eighteen of the twenty-one landers are lost. Each frame adds every hazard's place, life, kind (harmless lightning apart), frame, heading (a lightning bolt's starting life, which shares its field) and frame wait. Contact uses the package's masks of `graphics.quake`, `graphics.lightning` and `graphics.lavaspot` too. Also every orbited world's chances of each hazard (`GetHazardChance`, `GetThermalHazardRating`), from its report in `world-surfaces.json`. The lander recordings take each frame as `ScrollPlanetSide` ends, after a crew member lightning killed.
- **`lander-drawing.json`** (needs `--content`): what UQM's own lander draws, frame by frame: six landings from the pickup, creature and hazard recordings, cut to a few hundred frames, each frame adding every drawing call on the surface view (the map, the display list from its head, the lander, its shadow and shield, the mineral text) and in the lander's gauges (`InitLander`, the warm-up, crew losses, the hold's bars), in order; and twelve Manifest cargo displays as `cargo.c`'s `DrawCargoStrings` draws them. Code and the masks' shapes; the game's strings are empty here.
- **`landing.json`:** for every world, whether its orbit is entered, whether `DoScan` lets the lander go down there (neither shielded nor a gas giant) and the fuel `getLandingFuelNeeded` says the landing costs, in 1/100 units. Recorded by the systems program's `landing` mode; code only.
- **`planet-spheres.json`** (needs `--content`): the rotating planet as UQM's `RenderPlanetSphere` draws it, for Earth and the first other world of each planet type with and without a shield, at six rotation steps, and the shield halo; hashes only. Earth runs through UQM's own defined-planet branch of `GeneratePlanetSurface`: its two images (`graphics.earthmask`) are decoded by UQM's own PNG loader (the images program) and handed to the systems program as PPM/PGM files. The surfaces mode hashes every map and frame itself (`tools/extract/golden/sha256.h`) and prints only the three whole height maps in hex. `surface-heights.json` also has each world's light map hash.
- **`sprite-masks.json`** (needs `--content`): the collision masks of the sprite sheets UQM tests for contact (the flagship in a system and in hyperspace, the worlds, the orbit background, the hyperspace stars, and the sheets of UQM's one-pixel frame), each frame read by UQM's own PNG loader with its colour key set as `process_image` sets it and a pixel counted as `TFB_DrawCanvas_Intersect` counts it (the images program's `mask` mode, which models UQM's conversion of a true colour image without alpha to the screen's 32 bits, `TFB_DrawImage_New`); hashes only. The systems program loads the same masks into real frames for `ip-flight.json` and `intersections.json`.
- **`battle-masks.json`** (needs `--content`): the same for the sheets a battle tests for contact, the big size only (`InitIntersectFrame` takes `farray[0]`): the flagship's and the Ilwrath's ship and weapon sheets as `data/ships.json` names them, the asteroids, blasts and explosions, `graphics.stars` (floating crew, lasers and ion trails are its last frame) and every planet's battle sprite (`data/planet-types.json`). The planets are true colour without alpha, so their masks are the converted images': that step (`TFB_DisplayFormatAlpha` to the 32-bit screen without alpha, SDL 1.2's or 2's: `pure.c`, `opengl.c`, `sdl2_pure.c`) is modelled by the images program, whose SDL stand-in has no conversion, the PNG decoding, colour keys and hot spots being UQM's own. Checked by `BattleContentTests`.
- **`battle.json`** (needs `--content`): fifteen battles of UQM's own battle engine, frame by frame (about 12,700 frames), recorded by the `battle` program (`tools/extract/golden/battle.c`), which runs `battle.c`'s `Battle` as an encounter does, linked with UQM's real `process.c`, `collide.c`, `gravity.c`, `misc.c`, `weapon.c`, `ship.c`, `tactrans.c`, `init.c`, `status.c` and `shipstat.c` (drawing, sound and music stubbed; `galaxy.c` and `random.c` included). Each battle is a seed and scripted keys for both sides (some flown by a pilot in `golden_uqm.py` steering at the planet, the other ship, an asteroid or floating crew; the keys are what's kept), with test ships: UQM's stock ship code with plain missiles, homing missiles and lasers made by `weapon.c`, flying the flagship's or the Ilwrath's sheets and numbers. The battles: warping in (with the planet first put on an asteroid), flying into the planet until the ship dies of it, ramming, shooting until one dies, homing missiles, shooting asteroids (they break up and come back on the seam), the display list full (150 elements), crew floating (`AbandonShip` called directly; picked up, and turning to another ship when theirs dies), turning ships scraping each other, a retreat, two quick ships jostling, the step zoom, and two long battles at random. Each frame gives `TFB_Random`'s seed, the camera's scroll, zoom and corner, both ships' crew, energy, facing, counters and status, every element as Super Melee's netplay checksum takes it (flags, life, crew, mass, turn and thrust waits, velocity, current and next place) with its frame, side, primitive, slot and target, and each random number drawn by the UQM function that drew it (named from the program's symbols); after a battle's first frames, a frame's ships, elements and draws are a hash. The battle's stars (`InitGalaxy`) too. Contact uses the package's masks of the battle's sheets (big size; the others only counted). Checked by `BattleEngineTests`.
- **`intersections.json`** (needs `--content` to record): UQM's own `DrawablesIntersect` on 4,000 cases chosen by the systems program's `intersect` mode: pairs of frames UQM tests for contact (and a few others) near each other, still or moving, with a time limit, and what it returns (when they touch, and where both end). Positions and times only.
- **`binary-tables.json`** (needs `--content`): every BINTAB resource as UQM's `_GetBinaryTableData` reads it (the strings program's `bintab` mode); lengths and hashes only.
- **`conversation-cases.json`:** conversation inputs written for the test (in `tools/extract/golden_uqm.py`, not UQM content), each with exactly what UQM's own `_GetConversationData` makes of it, entry by entry: the name, the text byte for byte and the voice clip, through its quirks with headers (a clip after a tab, none, a name without parentheses, parentheses in the clip, one longer than a line), blank lines, line endings, NULs, long lines read in 1,023-byte pieces (a '#' starting a piece starts an entry), a repeated name and the 2,048-entry limit. Recorded by the strings program's `conversation` mode, linked with `stringhashtable.c` for the name index.
- **`conversations.json`** (needs `--content`): every conversation (CONVERSATION resource) of the package as UQM's loader reads it: each entry's name and voice clip, and its text's length and SHA-256 (the texts are content).
- **`comm-graphics.json`** (needs `--content`): every conversation's frames (`comm.*.graphics`) as `_GetCelData` loads them: each frame's image, transparent colour, colour map and hot spot from its `.ani` line, and the SHA-256 of the image as UQM's own PNG loader gives it. The conversations' colour tables and fonts are in `binary-tables.json` and `fonts.json` already.
- **`talks.json`** (needs `--content`): every path through each ported conversation script (the commander's before the alliance, `comandr.c`; the Ilwrath's at Earth; the allied commander's, `starbas.c`), as UQM's own script runs it. The `comm` program (`tools/extract/golden/comm.c`) is built once per script with the script's own file included, and linked with UQM's real `commglue.c` (lines, names and numbers spliced in, the replies' constructed texts, the segues), `sis.c` (`DeltaSISGauges` and the capacities it keeps the crew and fuel within), `random.c` and the string table loader, which reads the script's lines and colour maps from the package. The rest of the talk engine (the track player, the replies' list, the animations and colour fades, the drawing) is stubbed, and each stub records what the script asked of it: every line queued with its phrase number and voice clip, every wait, colour fade and screen change, every story field written, every change to the ship, every random number drawn, the replies offered (their phrase, the script's function and their text as shown) and the segue. The walk runs the script from each of a set of starting states chosen to reach every condition it reads, with each reply in turn, depth first, and stops a path where the whole talk state (story, ship, used-up phrases, screen, replies offered) was already walked from that start (the random seed included). The song is the alternate one only if the package has it, as HailAlien falls back. Texts are kept as hashes. A start gives the ship's drive and jet slots, the races' standings (`CheckAlliance`) and the escort fleet's worth and room where they matter (the allied commander's bulletins and advice); `AddEscortShips` and `EnableTalkingAnim` are recorded, and selling minerals runs as if a key were pressed (each category at once). A start with `follow` walks only the replies calling those functions, and one with `prefix` starts its walk after those replies, so the allied commander's lists of races (a reply each, taken away once asked) aren't walked in every order from every start.
- **`devices.json`:** the Manifest's Devices list, as UQM's own `InventoryDevices` makes it, `DoManipulateDevices` moves its cursor and `DrawDevices` draws it (`planets/devices.c`, included in `tools/extract/golden/devices.c`, linked with `util.c`'s `DrawStarConBox` and `unicode.c`), its drawing primitives recording each call in the screen's coordinates with its context's clip rectangle, as `selling.json`'s do. Each scenario is a story state (each device alone, the Ultron at each repair, those never listed, several, all, and seeded mixes) and keys (walks over the pages); after the list opens and after each key, the cursor, the top row shown, whether the list was left and the calls drawn. The game strings are the program's own, `{gs:N}` and, by `N % 4`, nothing, ` b`, ` b c` or `é b` after it, so every way a name splits into its two lines is recorded; the icons are `MiscDataFrame`'s frames as asked for. Code only.
- **`outfit.json`:** outfitting and the shipyard's crew, key by key, as UQM's own `DoOutfit`, `DoInstallModule` and `ChangeFuelQuantity` (`outfit.c`) and `DMS_ModifyCrew` and `CrewTransaction` (`shipyard.c`) run them. The `outfit` program (`tools/extract/golden/outfit.c`) includes both files and links `sis.c` and `menu.c`, the drawing, sounds and waits stubbed (`outfit_stubs.c`), a refusal (`MENU_SOUND_FAILURE`) recorded. Each scenario is a story state, the prices, the Shofixti's standing and a flagship, and a list of steps (open outfitting, a key, or the shipyard's hire and dismiss); after each the cursor (UQM's `CurState` and `delta_item`), the ship, its slots (where they changed) and the crew price and count are recorded. The scenarios try each rule on purpose, then 40 seeded random walks over the menus. Code only.
- **`commanim.json`:** every conversation's alien animations as UQM's own `ProcessCommAnimations` (`commanim.c`, with its own `TFB_Random`) runs them on the recording program's clock (`tools/extract/golden/commanim.c`), from each script's description in `data/conversations.json`: each race's seed and steps (ticks, and talking, stopping, pausing and flags), and the steps where something happened: the frames stamped, the colour fades started, the talking and transition descriptors' flags. Code only.
- **`cmap.json`:** colour map fades as UQM's own `cmap.c` (`SetColorMap`, `XFormColorMap`, `XFormColorMap_step`) runs them on the recording program's clock (`tools/extract/golden/cmap.c`), from colour entries made from a seed: after each step, which maps changed and a hash of each one's colours. Code only.
- **`commtext.json`** (needs `--content`): how UQM's own `comm.c` lays out a talk's text in each race's font, width, baseline, alignments and colours (`tools/extract/golden/commtext.c`): some of each race's lines paged by `SplitSubPages` (each page's hash and time) and drawn as subtitles by `add_text`; sets of lines as the player's replies (`RefreshResponses`, `font.player`); a summary (`DoConvSummary`, `font.tiny`). The lines are content, so only what is drawn where, and hashes.
- **`screens.json`** (needs `--content`): what UQM's own starbase screens draw, call by call, as `DoStarBase`, `DoOutfit` and `DoShipyard` (`starbase.c`, `outfit.c`, `shipyard.c`, with `sis.c`, `menu.c`, `border.c` and `util.c`) run them (`tools/extract/golden/screens.c`, its drawing primitives recording each call in the screen's coordinates with its context's clip rectangle, the rest stubbed in `screens_stubs.c`): the menu turning on the program's clock, outfitting (the outfit scenarios that keep to its screen), and the shipyard with the flagship alone, its `on_input_frame` run before each step as `DoInput` does. After each step, the flashing highlight's rectangle (`SetFlashRect`, whose `Flash_*` calls the program keeps) and the colour map last set (the power lines cycle `colortable.hangar`). The frames' counts and rectangles and `colortable.hangar`'s size are read from the content; the pictures aren't recorded. The port's screens are checked against it call for call at every step (and painted from the content with `ScreenRaster`, which checks the painter takes every call).

- **`report.json`** (needs `--content`): the lander report as UQM's own `report.c` (`DoDiscoveryReport`, `MakeReport`, `ClearReportArea`) types it, with `util.c`'s waits (`tools/extract/golden/report.c`): each drawing call in the screen's coordinates at its time on the program's clock (only the waits' `SleepThread` moves it on), with keys going down and up at scripted times, seen as UQM's input sees them (`AnyButtonPress`: a key held, or one gone down since the last look; `FlushInput` forgets those held). The fonts are UQM's default, `OPT_PC`. The reports are the Lunar base's and other lander reports of the content, named by their table and entry, and texts of our own; a call's text is a byte offset into the report or the end-of-page string, which are content and aren't written out. Also when the screen was put back and every key was up. The frames' rectangles are read from the content.

- **`selling.json`** (needs `--content`): the allied commander buying the hold's minerals as UQM's own `starbas.c` (`SellMinerals`, included in `tools/extract/golden/sell.c`, linked with `cargo.c`'s `DrawCargoStrings` and `ShowRemainingCapacity`, `sis.c`'s `DeltaSISGauges` and `DrawStatusMessage`, and `util.c`'s `DrawStarConBox`) counts it down in the status area, from its start to the `ClearSISRect` that puts the cargo display away (wrapped, so the program records when and stops; the commander's lines after it are `talks.json`'s): each drawing call in the screen's coordinates at its time on the program's clock, in milliseconds, as `SDL_GetTicks` counts them (`GetTimeCounter`'s ticks made from them as UQM's SDL clock makes them; `TaskSwitch` takes a millisecond, a sleep wakes at the first millisecond whose ticks reach its time, drawing takes none), with keys going down and up at scripted times seen as `report.json`'s are, and keys held as it starts (gone down into the talk's replies). The holds: one category, the first or last alone, one unit, several with empty categories between, all eight, a full hold, unlimited resource units (`CHMMR_BOMB_STATE` 2) and a big load of 2,885 units; the keys: none, at the first wait, held from the start (let go before or after the first look), mid-category, in the last category, between categories, a tap within a wait, at the end, mashing. Also the status message as UQM's status area already shows it (the resource units before the sale), when the display was put away and the resource units then. A scenario of more than 600 calls keeps its first 150 and last 60, how many there were and a SHA-256 of them all, so the file stays small (about 420 KB). The element worths are `data/new-game.json`'s; `graphics.miscdata`'s frame count and its first element icon's size are read from the content; the texts are numbers and `{gs:N}` game strings, so the tests don't need the content.

```
python3 tools/extract/golden_uqm.py --uqm ../uqm-0.8.0/sc2 [--content uqm-0.8.0-content.uqm]
```

`--only NAME[,NAME]` records only those files (by name without `.json`; `planet-surfaces`, `planetside` and `contact` for the files those write).

With `--content` (the official package; anything else is refused) it also records what UQM reads from the content, as hashes.

## Units

UQM mixes several unit systems. The engine keeps UQM's units internally, so ported formulas stay exact, and converts only for display.

| Quantity | UQM unit | Conversion | Source |
| --- | --- | --- | --- |
| Hyperspace position | Universe units, 0 to 9999 on each axis | Shown to the player divided by 10: Sol 1752, 1450 is 175.2, 145.0 | `units.h` (`MAX_X_UNIVERSE`) |
| Combat position, speed, range | World units | `DISPLAY_TO_WORLD (x)` is `x << 2` | `units.h` |
| Time | `ONE_SECOND` = 840 ticks | A battle frame is 35 ticks, so 24 frames per second (`BATTLE_FRAME_RATE`) | `libs/timelib.h`, `battle.h` |
| Ship waits (turn, thrust, weapon, energy) | Battle frames | Divide by 24 for seconds | ship files |
| Fuel | 1/100 of a fuel unit (`FUEL_TANK_SCALE` 100) | 1000 is the 10 units shown | `sis.h` |
| Module prices | Units of 50 resource units (`MODULE_COST_SCALE`) | Price = cost x 50 | `sis.h` |
| Game clock | Seconds of real time per game day | Hyperspace 5, solar system 30 in UQM; Crateris uses one constant rate | `clock.h` |

## Data files

### `starmap.json`: stars and QuasiSpace

From `starmap_array` in `src/uqm/plandata.c` (type `STAR_DESC`, `src/uqm/planets/planets.h`).

| UQM field | JSON | Meaning |
| --- | --- | --- |
| `star_pt` | `x`, `y` | Position in universe units |
| `Type` (packed) | `size`, `color` | `STAR_TYPE` gives `DWARF_STAR`, `GIANT_STAR` or `SUPER_GIANT_STAR`; `STAR_COLOR` gives `BLUE_BODY` and so on (`planets/plandata.h`) |
| `Index` | `special` | Special-system generator (`*_DEFINED` in `gendef.h`), or null for an ordinary system |
| `Prefix`, `Postfix` | `name_prefix`, `name_postfix` | Indexes into the content's star name strings (see Content below) |

502 hyperspace stars, then 16 QuasiSpace entries (15 portal exits and the Arilou homeworld). A system's random seed is `MAKE_DWORD (x, y)` (`GetRandomSeedForStar`, `planets/solarsys.c`), so the planets of every system follow from this file plus the generator code.

**Used by:** hyperspace and the star map (stage 1), solar systems (stage 2).

### `planet-types.json`: the 59 planet types

From `planet_array` in `src/uqm/plandata.c` (type `PlanetFrame`, `planets/plandata.h`). One entry per planet type, not per planet.

| UQM field | JSON | Meaning |
| --- | --- | --- |
| `Type` (packed) | `size`, `map_algorithm`, `color` | Small rocky, large rocky or gas giant; how the surface map is drawn; colour in a solar system |
| `BaseTectonics` | `base_tectonics` | Earth is 100 |
| `AtmoAndDensity` (packed) | `density_class`, `atmosphere_class` | Low and high nibble |
| `UsefulElements[8]` | `useful_elements` | Up to 8 minerals: element, quantity class (`FEW`, `MODERATE`, `NUMEROUS`), quality class (`LIGHT`, `MEDIUM`, `HEAVY`) |
| `num_faults`, `fault_depth`, `num_blemishes`, `base_elevation` | same names | Surface map generator parameters |
| `CMapInstance`, `XlatTabInstance` | `color_map_resource`, `color_table_resource` | Content resource ids for the planet's colours |
| (`cons_res.c`) | `battle_sprites` | The planet's sprite in a battle, big, medium and small, as `load_gravity_well` names them (`planet.<type>.large` and so on); the file's `shielded_battle_sprites` are a shielded world's (`slaveshield`), whatever its type |

Individual planets come from `planets/plangen.c`, `calc.c`, `gentopo.c` and `orbits.c` (code). `PLANET_INFO` (`plandata.h`: tilt, tectonics, weather, gravity, temperature and so on) is computed per planet by `calc.c`, not stored.

**Used by:** solar systems and planets (stage 2: sizes, densities, elements and the planet report; `color` is the colour family, which picks the world sprite's colour map from `colortable.orbplan` since the content stage, and the circle shown in orbit for the moment before the rotating planet is ready), lander and mining, combat (stage 4: `battle_sprites`, the battle's planet, `Combat/BattleContent.cs`).

### `elements.json`: minerals

From `element_array` in `src/uqm/plandata.c` and the enums in `src/uqm/planets/elemdata.h`. 133 elements, each in one of 8 categories (`COMMON` to `EXOTIC`). `worth` is the starting resource-unit value per unit of cargo in each category, set in `InitGameStructures` (`globdata.c`).

**Used by:** solar systems and planets (stage 2: element ids of mineral deposits, and the category, which picks a mineral dot's colour on the scan map), lander and mining.

### `lifeforms.json`: creatures

From `CreatureData` in `src/uqm/planets/lander.c` (type `LIFEFORM_DESC`, `planets/lifeform.h`). 23 ordinary creatures and 3 special ones (Evil One, Brainbox Bulldozers, Zex's Beauty).

| UQM field | JSON | Meaning |
| --- | --- | --- |
| `Attributes` (packed) | `behavior`, `awareness`, `speed`, `danger` | Hunt, flee or unpredictable; low to high; motionless to fast; harmless to monstrous |
| `ValueAndHitPoints` (packed) | `bio_value`, `hit_points` | Low nibble: bio data per creature. High nibble: stun hits to capture |
| (source comment) | `label` | Creature name from the source comment; display names are content |

### `ships.json`: all 28 ships

From the `RACE_DESC` initialiser in each `src/uqm/ships/<dir>/<dir>.c` (types in `races.h`).

| UQM structure | JSON | Meaning |
| --- | --- | --- |
| `SHIP_INFO` | `flags`, `cost`, `crew`, `max_crew`, `energy`, `max_energy` | `flags`: firing directions and behaviour hints (`FIRES_FORE`, `SEEKING_WEAPON`, `POINT_DEFENSE`, `SHIELD_DEFENSE` and others), by name; the file's `flag_values` gives each name's bit (`races.h`), and the extractor stops on a bit it has no name for. `cost` is the Super Melee point cost |
| `FLEET_STUFF` | `fleet.strength`, `fleet.home_x`, `fleet.home_y` | Starting size and centre of the race's sphere of influence |
| `CHARACTERISTIC_STUFF` | `characteristics.*` | Thrust, energy regeneration, weapon and special energy cost, waits (in frames), mass |
| `INTEL_STUFF` | `ai.maneuverability_index`, `ai.weapon_range` | Inputs to the combat AI |
| `DATA_STUFF`, `SHIP_INFO` resources | `graphics.ship`, `graphics.weapon`, `graphics.special` (each big, medium, small), `graphics.captain`, `graphics.icons`, `graphics.melee_icon` | The content resource ids the ship's battle sheets, captain's window, status panel icons and system icon come from (`ship_rsc`, `weapon_rsc`, `special_rsc`, `captain_rsc`, `icons_rsc`, `melee_icon_rsc`); null for `NULL_RESOURCE`. A size without its own resource takes the one before (`load_animation`: the Umgah's one-size spritz) |
| `#define`s in the file | `defines` | Every integer constant in the ship's source, including weapon speed, life, damage and range, which are not in `RACE_DESC` |
| (`loadship.c`) | `species` | The `SPECIES_ID` that loads this ship |

Quirk to keep: UQM's `MAX_CREW` define is the *starting* crew. Most ships start full, but the Syreen start with 12 of 42.

Includes the flagship (`sis_ship`; its real stats come from installed modules), the Sa-Matra (`lastbat`) and the Slylandro probe (`probe`). What weapons and specials *do* is code (`*_preprocess`, `*_postprocess`, collision and AI functions in the same files) and is ported by hand, one C# class per ship.

**Used by:** combat (stage 4: `src/Crateris.Core/Combat/ShipDescriptions.cs` reads every ship's description, checking `flag_values` against its own; `Combat/BattleContent.cs` takes the battle's sheets from `graphics`), fleets and encounters.

### `races.json`: per-race tables

From `races.h`: the `*_SHIP` enum (the order of `avail_race_q`, one `FLEET_INFO` per race), `RACE_SHIP_COST` (battle salvage is cost / 8 per enemy ship destroyed), `RACE_IP_SPEED` (speed in solar systems) and `RACE_COMMUNICATION` (which conversation a race uses). 23 races have a cost; the last entry (drone/Sa-Matra) has only a speed and a conversation. The extractor checks the table lengths against the race list. `conversation_races` (`RACE_SHIP_FOR_COMM`) gives each `CONVERSATION` id (commglue.h's enum) the race it's with, as `InitCommunication` reads it; the dialogue lock uses it.

### `game-state.json`: story flags

From the `ADD_GAME_STATE` list in `src/uqm/globdata.h`. 447 named flags and counters packed into 1,238 bits: `name`, `bits`, `start_bit`, and its source comment where there is one. The source doesn't group the flags (it has only two section headings, near the top); the name prefix usually says which race or topic a flag belongs to. UQM reads them with `GET_GAME_STATE` / `SET_GAME_STATE`.

In Crateris these are world state, read and written by name. The story state (`src/Crateris.Core/Story/StoryState.cs`, stage 3a) keeps UQM's bit layout: fields are packed and read as `getGameState` and `setGameState` do, so a value too big for its field runs into the next bits as in UQM, and clients are sent the bytes that changed. Saves write the fields by name.

**Used by:** the story state, read by the conversation scripts (stage 3a) and the system generator's story inputs; story events (stage 3c) and anything else that checks the story.

### `events.json` and the clock

`events.json` lists the 18 clock events from `gameev.h` (Arilou entrance and exit, Kohr-Ah victory and genocide, the Pkunk, Thraddash, Ilwrath, Mycon and Utwig/Supox missions and others). What each event does is code in `EventHandler` (`gameev.c`).

The clock itself is `CLOCK_STATE` (`clock.h`): day, month, year, ticks, and a queue of `EVENT`s (date plus event id). Events are absolute (a date) or relative (days from now). See `doc/devel/timing`.

### `new-game.json`: starting values

From `InitGameStructures` (`globdata.c`), `InitGameClock` (`clock.c`) and `AddInitialGameEvents` (`gameev.c`):

- **Start date:** 17 February 2155.
- **Prices:** crew 3, fuel 20 resource units; module costs and mineral worth.
- **Flagship:** 50 crew, 10 fuel units, 1 lander, 0 resource units, at Sol, with two fusion thrusters, two turning jets, a fuel tank, a crew pod, a storage bay and a gun.
- **Empty slots:** each kind of slot has its own empty marker: drive `EMPTY_SLOT` (20), jet `EMPTY_SLOT + 1` (21), module `EMPTY_SLOT + 2` (22). The outfitting code (`outfit.c`) tells slot kinds apart by these, so keep them.
- **Initial events:** hyperspace encounters start being generated after 1 day; Arilou entrance on 17 March 2155; Kohr-Ah victory after 4 years; Slylandro probes start ramping up at once. A relative event with all offsets 0 runs immediately instead of being scheduled (`AddEvent`, `clock.c`).

The start date and events are read from `InitGameClock` and `AddInitialGameEvents` and evaluated by the compiler, not copied by hand.

**Used by:** a new game: the start date and the flagship (stage 1 on), and since stage 3a the story state's prices, element worths and event queue (`Story/StoryRules.cs`, with `races.json`, `events.json`, `modules.json` and `constants.json`'s `CREW_EXPENSE_THRESHOLD`).

### `modules.json`: flagship modules

The module enum from `sis.h`: lander, thruster, jets, crew pod, storage bay, fuel tank, high-efficiency fuel system, dynamo, Shiva furnace, gun, blaster, cannon, tracking system, point defence, and the 6 bomb parts. `start_cost` is the price at the start (0 means not for sale yet; the rest become available through the story).

**Used by:** the story state's module prices and outfitting (stage 3a), and choosing the escort module set (an open item).

### `conversations.json`: the conversation scripts

Each of the 27 scripts in `src/uqm/comm/*/` (one per folder): its init function, its race description (`LOCDATA`) as that function leaves it at the start of a new game, and its phrase ids. The extractor compiles a program for each script that includes the script's own `.c` file, runs its init function with the story bits it reads at 0 (as at a new game), a new game's alliances and UQM's 320 by 240 screen (the subtitles' place and width are worked out from `SCREEN_WIDTH`, a variable), and prints the description it returns, every macro evaluated (`ONE_SECOND / 15` is 56 time units); the phrase ids are `strings.h`'s enum as the preprocessor leaves it (its `#if 0` block in the Pkunk's dropped). Then which scripts `init_race` (`commglue.c`) may run for each conversation id.

| UQM | Field | Notes |
| --- | --- | --- |
| `AlienFrameRes`, `AlienFontRes`, `AlienColorMapRes`, `AlienSongRes`, `AlienAltSongRes`, `ConversationPhrasesRes` | `description.frames`, `font`, `color_map`, `song`, `alt_song`, `phrases` | Content resource ids (`comm.commander.graphics` and so on); `description.phrases` is the conversation's resource id. An alternate song is played when `song_flags` has `LDASF_USE_ALTERNATE`, if it loads: the base content has the Safe Ones' only (the commander's low-power one, the rebels' and the starbase's are the 3DO music add-on's), so the others fall back to the song, as `HailAlien` does |
| `AlienTextFColor`, `AlienTextBColor`, `AlienTextBaseline`, `AlienTextWidth`, `AlienTextAlign`, `AlienTextValign` | `text_fore`, `text_back`, `text_baseline`, `text_width`, `text_align`, `text_valign` | Where the alien's subtitles go; colours as `[r, g, b, a]` |
| `AlienAmbientArray`, `AlienTransitionDesc`, `AlienTalkDesc` | `ambient`, `transition`, `talk` | Each `ANIMATION_DESC` as `[StartIndex, NumFrames, AnimFlags, BaseFrameRate, RandomFrameRate, BaseRestartRate, RandomRestartRate, BlockMask]` |
| `strings.h` | `phrases` (beside `description`) | The phrase ids in order: the script's phrase `n` is entry `n - 1` of its conversation (`NPCPhrase`), and every script's list matches its conversation's entries name for name (`ConversationContentTests`) |

The description is the new game's. Where an init function reads the story it may leave a different one later (the commander drops his low-power colour cycle and music once he has the radioactives; the Spathi and the Utwig change their music); those branches are code, ported with each script. `init_race` runs the commander's script before the starbase is available and the starbase's after, and the Spathi's away from home and at it (by `GLOBAL_FLAGS_AND_DATA`). `URQUAN_DRONE_CONVERSATION` has no case of its own: `InitCommunication` (`comm.c`) runs it as `URQUAN_CONVERSATION` (`as`), so a drone talks with the Ur-Quan's script. The Zoq-Fot-Pik's description has no subtitle place (0, 0): their script sets one for the Zoq and one for the Pik as each talks (`zoqfotc.c`).

**Used by:** the conversation engine and the communication screen (stage 3a).

### `devices.json`: the group inventory

The devices in `globdata.h`'s order (the order the Manifest lists them), from `planets/devices.c`'s `InventoryDevices`: each device's number, the story field that says it's aboard (aboard when it isn't 0, or with `value` when it's that value: the Ultron's four repairs share `ULTRON_CONDITION`), and whether it's ever listed (the three data plates have no case, and the destruct code's is compiled out under `NEVER`). These fields are the group inventory: things the group carries, in the one shared story state. A device's name is the game strings' `DEVICE_STRING_BASE + 1 + id` (two words, a line each), its icon `MiscDataFrame`'s `77 + id`.

**Used by:** the Manifest's Devices list (`Story/Devices.cs`, stage 3a) and `--start-devices`; using the devices is stage 3c.

### `constants.json`: limits and rates

Named constants from UQM headers: clock rates, slot counts, capacities, fuel and cost scales, universe size, Sol, QuasiSpace and Arilou positions, queue limits, and two constants defined inside functions (`RUN_AWAY_FUEL_COST` 500 in `pickship.c`, `LOST_DAYS` 14 in `starbase.c`).

## Runtime state: what the server's data model replaces

These UQM structures hold the game while it runs and in save files. They aren't data files; they're the starting point for the server's world state and player state. `doc/devel/savefile` and `doc/devel/statefiles` describe how UQM saves them.

| UQM structure | Where | Holds | In Crateris |
| --- | --- | --- | --- |
| `SIS_STATE` | `sis.h` | The flagship: position, resource units, fuel, crew, module, drive and jet slots, landers, cargo, names | Player state, one per player. The host's is the flagship; guests' escorts use the same shape with a smaller module list |
| `GAME_STATE` | `globdata.h` | Everything else: prices, current activity, clock, autopilot, location in a solar system, the queues below, story bits | Split into world state (prices, clock, queues, story) and per-player state (activity, location, autopilot) |
| `CLOCK_STATE` + `EVENT` queue | `clock.h` | Date and scheduled story events | World state, one shared clock |
| `FLEET_INFO` (`avail_race_q`) | `races.h` | Per race: allied or not, sphere of influence (centre, strength, growth, destination), what the player knows of it | World state |
| `SHIP_FRAGMENT` (`built_ship_q`) | `races.h` | The flagship's escort ships (max 12): race, crew | The host's AI fleet |
| `SHIP_FRAGMENT` (`npc_built_ship_q`) | `races.h` | The enemy ships of the current encounter | Per battle |
| `ENCOUNTER` (`encounter_q`) | `encount.h` | Hyperspace encounters (the black globes): race, ship count, position (max 16) | World state, shared by players in hyperspace |
| `IP_GROUP` (`ip_group_q`) | `grpinfo.h` | Alien groups in the current solar system: race, task, destination, orbit, position | Per solar system, shared by the players there |
| `PLANET_DESC`, `SOLARSYS_STATE` | `planets/planets.h` | The generated planets and moons of the current system | Generated per system from the seed; not saved |
| Star info state file | `state.c`, `doc/devel/statefiles` | Per star: which mineral, energy and life nodes have been taken on each world (one bit each) | World state, so a second player sees the first one's work |
| Battle group state files | `grpinfo.c`, `doc/devel/statefiles` | Random and scripted groups per system (`DEFGRP_*`) | World state |

## Code, not data

These are logic and get ported by hand to C#, using the data files above. Where they hold numbers, the numbers stay in the code next to the logic.

| What | Where |
| --- | --- |
| Hyperspace movement, encounters, QuasiSpace | `hyper.c`, `encount.c` |
| Solar system flight, orbits, NPC groups | `planets/solarsys.c`, `orbits.c`, `ipdisp.c` |
| Planet and surface generation | `planets/plangen.c`, `calc.c`, `gentopo.c`, `surface.c`, `scan.c` |
| Special systems | `planets/generate/gen*.c` (28 plus `gendefault.c`) |
| Lander | `planets/lander.c` |
| Combat engine and ship behaviour | `battle.c`, `process.c`, `collide.c`, `gravity.c`, `velocity.c`, `weapon.c`, `tactrans.c`, `ships/*/` |
| Combat AI | `cyborg.c`, `intel.c`, `*_intelligence` in ship files |
| Conversation trees | `comm/<race>/*.c` (27 scripts) |
| Story events | `gameev.c` |
| Starbase, outfitting, shipyard | `starbase.c`, `outfit.c`, `shipyard.c` |
| Random numbers | `src/libs/math/random.c`, `random2.c` (Park–Miller, A = 16807, M = 2^31 - 1); must match exactly |

## Content, not ours

The content package (CC BY-NC-SA 2.5) is downloaded on first run and read in place by each player's engine (`src/Crateris.Core/Content/ContentPackage.cs`): `uqm-0.8.0-content.uqm` is a zip holding `uqm.rmp` (the resource index: `id = TYPE:path`), `uqm.key`, `menu.key` and `base/`. Only the official file is accepted, by its SHA-256. Nothing from it is copied into the repository; tests that need it find it through `CRATERIS_CONTENT` or the game's own copy. The data files refer to it only by index or resource id:

- **Star names:** `name_postfix` indexes the game strings (`content/base/gamestrings.txt`, `STAR_STRING_BASE`); `name_prefix` 0 means none, otherwise index - 1 into the Greek-letter numbers at `STAR_NUMBER_BASE` (`GetClusterName`, `starmap.c`).
- **Ship and race text, art, sounds:** `race_strings_resource` and the `ship.<name>.*` ids in `content/uqm.rmp`.
- **Planet colours:** `color_map_resource`, `color_table_resource`.
- **Dialogue lines:** referenced by phrase id from the conversation scripts; each script's ids are in `conversations.json`, and `Conversation` (`src/Crateris.Core/Content/Conversation.cs`) reads the lines as UQM does.
- **Creature, module, element and device names:** content strings.

## Not extracted yet

- **Scripted battle groups (`DEFGRP_*`) and per-race encounter makeup:** built in code (`grpinfo.c`, `build.c`, generators); stage 2 and 3.
- **Melnorme prices and sales:** in `comm/melnorm/melnorm.c`, ported with the conversation (stage 3b).
- **Sun data (`sundata.h`):** star size and energy constants, small enough to port with the generator.
