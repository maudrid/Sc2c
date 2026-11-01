# Content stage plan: the UQM content, names and planets

**Status:** draft, waiting for the owner's approval and the decisions below.

Build order item: "Content: the first-run content download and a minimal content reader (names, then planet colour maps)" (`docs/design.md`), between stage 2 and stage 2b (stage 2, decision 2).

## Goal

The game gets the UQM 0.8.0 content the way `docs/design.md` decided ("First-run content download"): on first run, one screen with the content notice and a Download button; the engine fetches the official package, checks it and keeps it unchanged. Then the game starts using it where stage 2 drew placeholders: real names for stars, planets and moons, UQM's own words and fonts on the orbit screen, and planets drawn as UQM draws them, from the surface map on the scan screen to the rotating planet in orbit. The placeholder contact circles (`docs/differences.md` rows 23 and 25) are replaced by the real sprite shapes, and the star-entry animation (row 24) is added.

### What the owner will be able to try when it's done

- Start the game on a machine without the content: the notice screen appears, Download fetches 11.5 MB, the file is checked and the game starts. Starting again goes straight in.
- Fly into Sol and see "Sol" in the status, its planets drawn as UQM's planet sprites in their colours, and "Earth", "Luna" and so on in the inner system.
- Orbit the Moon: Luna turns slowly on the orbit screen, lit as in UQM. The report reads in UQM's words and font ("Orbit:", "Atmo:", "Temp:" and the rest), and the scan map shows the Moon's real surface, tinted as each scan line passes.
- Orbit Earth and see its shield.
- Fly into a star and see the ship spin onto it as UQM does.
- Open the credits screen and see the content notice.

## Scope

**In the content stage:**

- The first-run screen, the download, the SHA-256 check and where the package is kept.
- A content reader: the package (a zip), its resource index (`uqm.rmp`), string tables, colour tables, animation lists and their PNG frames, and fonts.
- Names: stars, planets and moons, and the orbit screen's words (title, report labels, menu entries, scan names), the months in the date.
- UQM's fonts for the text UQM draws.
- Planet surfaces: the topography generated from each world's seed (code in `planets/plangen.c` and `gentopo.c`, to port exactly), coloured with the planet type's colour tables (content), and the predefined surfaces of special worlds (Earth and others, content images).
- The orbit screen's rotating planet, lit and tilted as UQM draws it, with the shield effect on shielded worlds.
- Sprites in the system views: the sun, planet and moon images in their colours, the starbase and the Sa-Matra, the flagship; and the star sprites in hyperspace.
- Contact by the real sprite shapes instead of circles (rows 23 and 25), and the star-entry animation on the client (row 24).
- A credits/about screen with the content notice.

**Not in the content stage** (and where it goes):

| Left out | Why | Goes to |
| --- | --- | --- |
| Sound, music, voiceovers and their optional packages | Nothing plays sound yet | A later stage with audio (decision 2) |
| The zoomed-in landing surface (`TopoScale4x`) | Only the lander uses it | Stage 2b. Note: it uses a hidden random generator that keeps its state across planets for the whole session (`frandom` in `plangen.c`), so the surface you land on depends on which planets were landed on before. Stage 2b has to decide how to match that |
| Conversation graphics, portraits, dialogue text | They belong with conversations | Stage 3 |
| Ship and battle graphics | They belong with combat | Stage 4 |
| The star map screen, menus outside the orbit screen | Not built yet | With the stages that build them |

## How UQM does it

Paths are relative to `sc2/` in UQM 0.8.0.

- **The package.** `uqm-0.8.0-content.uqm` is a zip (11,547,353 bytes, 10,559 files, 13 MB unpacked, SHA-256 `77d75ac25e6fb755a33c4ba3b38a7b7bc41fcbc02896891b0cc9ac9214b72eef`, downloaded from the official SourceForge page on 2026-11-01). It holds `uqm.rmp`, `uqm.key`, `menu.key` and `base/`. UQM reads it in place without unpacking (`src/libs/uio/`).
- **The resource index.** `uqm.rmp` maps resource ids to a type and a path, one per line (`planet.water.colortable = BINTAB:base/planets/water.ct`). Types used here: `STRTAB` (string tables), `BINTAB` (colour and translate tables), `GFXRES` (`.ani` lists of PNG frames with their hot spots), `FONTRES` (`.fon` folders with one PNG per character).
- **Strings.** `base/gamestrings.txt` holds the game's strings as `#(name)` sections; code picks them by number in ranges defined in `src/uqm/gamestr.h` (stars, scan words, planet names, months, menu entries, orbit report labels).
- **Names** are code built from strings:
  - A star's name is a prefix (Alpha, Beta…) and a constellation (`GetClusterName` in `starmap.c`), from the prefix and postfix numbers already in `data/starmap.json`.
  - Most planets and moons have no name of their own: the orbit screen calls them by their type ("Water World", "Gas Giant": `GetPlanetTitle` in `planets/scan.c`), and an unnamed moon is the planet's name with "-A", "-B" after it. Sol's planets and moons, the starbase and the Sa-Matra are named in code (`GetNamedPlanetaryBody` in `planets/solarsys.c`).
- **Planet surfaces** (`planets/plangen.c` `GeneratePlanetSurface`, `planets/gentopo.c`): the system generator (`SysGenRNG`) is seeded with the world's seed, then a height map of 242 × 75 is made by the planet type's algorithm (faults, craters, gas giant bands and storms), and coloured through the type's translate and colour tables. Special worlds use a predefined image instead.
- **The rotating planet** (`plangen.c` `RenderPlanetSphere`, `CreateSphereTiltMap`, `GenerateLightMap`; `pl_stuff.c` `RotatePlanetSphere`): the surface map is wrapped onto a tilted, lit sphere and turned a column at a time. Shielded worlds get a throbbing shield.
- **Sprites and contact:** the views draw the sun, planets and flagship from `GFXRES` frames, coloured by colour tables. Contact is decided by their shapes (`DrawablesIntersect` in `src/libs/graphics/intersec.c`), which stage 2 replaced with circles.

## Co-op rules for the content stage

- **Every player downloads the content once** on their own machine; nothing from the content goes over the network. The host still sends star, world and seeds, and each client draws from its own copy.
- **Contact needs the shapes,** and the host decides contact, so from item 9 the host needs the content too (it's a player, so it has it). Guests need it to draw (decision 4).
- The content is never put in the repository or the engine's downloads (design, "Distribution and donations").

## Work, in order

Each item ends with tests that run headless. Where UQM code produces the answer, the test compares with results recorded from UQM's own code, as before. Results that come from the content (strings, colours, pixels) are recorded as hashes, not as content (decision 5).

1. **Package and resource index.** Open the `.uqm` file in place, check its SHA-256, read `uqm.rmp`, and read files by resource id. Check: unit tests on a small test zip made by the tests; tests on the real package when one is present (`CRATERIS_CONTENT`, or the game's own copy), skipped otherwise.
2. **First-run screen and download.** The notice from `docs/design.md`, the Download button, progress, the hash check (a file that doesn't match is deleted and the player is told), retry, and keeping the file unchanged in the game's user folder. "Use a file I already have" with the same check (decision 3). A credits/about screen with the notice. `--content PATH` for tests and servers. Check: the logic in `Crateris.Core` tested headless with a local test server; a screenshot of the screen.
3. **String tables and UQM's words.** Parse `gamestrings.txt` as UQM does, and the numbering from `gamestr.h`. Check: every string as UQM's own string loader reads it, recorded as hashes.
4. **Names.** Star names, planet and moon names and the orbit title, ported from `GetClusterName`, `GetNamedPlanetaryBody`, `GetPlanetOrMoonName` and `GetPlanetTitle`. Shown in the status, the system views and the orbit screen. Check: the names UQM's own code gives for every star and every world, as hashes, plus a few in plain words in the tests' code that the owner would recognise (Sol, Earth, Luna, Titan).
5. **Fonts and the orbit screen's words.** Read UQM's fonts; draw the orbit screen's title, report labels, menu entries and scan names in UQM's words and fonts (decision 6). Check: screenshots.
6. **Planet surfaces.** Port the height-map generation (`gentopo.c`, the crater, storm and gas giant algorithms, `ValidateMap`, `DitherMap`) and the colouring. Predefined surfaces for special worlds. Check: every world's height map against UQM's own code (code only, so recorded directly as hashes per world, with a few full maps), and every world's coloured map as hashes.
7. **The rotating planet.** Port the sphere: tilt map, light map, rendering and rotation, and the shield effect. Check: UQM's own rendering of a sample of worlds at several rotation steps, as pixel hashes; screenshots.
8. **Sprites in the views.** Planets, moons, sun, starbase, Sa-Matra and flagship in the system views from their frames and colour tables; the star sprites in hyperspace. Check: screenshots against UQM's layout (positions are already checked).
9. **Contact by shape and the star-entry animation.** Port UQM's sprite intersection; the host uses the real shapes for stars and worlds (rows 23 and 25 done). The flight traces of stages 1 and 2 are recorded again with the shapes; nothing else in them may change. The client plays the star-entry animation (row 24 done). Check: the traces, frame by frame.
10. **Docs.** `docs/how-to-try/content-stage.md`; update the design, porting inventory, differences and data docs.

Rough size: items 6 and 7 are the large ones (`plangen.c` is 1,950 lines), then 9. Items 1 to 5 are small to medium and make a stage the owner can already try (download, names, words).

## Decisions

The owner's choices, each with a recommendation.

1. **How much of the art in this stage.**
   - **Everything listed above (recommended):** names, words, surfaces, the rotating planet, sprites, contact by shape and the star-entry animation, in that order, so the first five items can be tried before the large ones. It finishes what stage 2 left as placeholders, and rows 23 to 25 already say "replaced in the content stage".
   - **Names and words only (items 1 to 5),** leaving surfaces, sprites and contact for a later art stage. Quicker to stage 2b, but the orbit screen stays a plain circle until then.
2. **Voiceovers and 3DO music.** The design's notice has tick boxes for them.
   - **Leave them out until sound exists (recommended):** nothing plays them yet, and they're 134 MB. The notice gets its tick boxes in the stage that adds audio; `docs/design.md` is updated to say so.
   - Offer them now, downloaded and kept for later.
3. **"Use a file I already have."**
   - **Offer it (recommended):** the same SHA-256 check as a download, then the file is copied in unchanged. Players who already have UQM 0.8.0 installed (its content folder holds this same file) or who are offline don't need SourceForge. The design chose a download over pointing at a copy; this keeps the download as the main path and only adds a checked alternative.
   - Download only, as the design says.
4. **A host without the content.** Once contact uses the real shapes (item 9), the host needs them.
   - **The game needs the content to host or join (recommended).** One way to play, one set of rules. Tests that need the content say so and are skipped where it isn't present; the network test is given a copy.
   - Keep the circles as a fallback for hosts without content. Two contact rules to keep in step, and a game played on circles would differ from one played on shapes.
5. **Reference results from the content.** The tests compare with what UQM's own code produces, but results drawn from the content (strings, colours, pixels) can't go in the repository, which is GPL and must not carry content (CC BY-NC-SA).
   - **Record them as hashes (recommended):** the reference holds a SHA-256 of each result; the tests read the content, work out the same results and compare hashes. Results from code alone (height maps, positions) are recorded as before.
   - Have the tests run UQM's own code each time (needs gcc and the UQM source on every test machine).
6. **UQM's fonts.**
   - **UQM's fonts for what UQM draws (recommended):** the orbit screen's title, menus, report and scan names, as UQM shows them. The engine's own font for its own text (status panel, join messages, later chat), which UQM never had.
   - The engine's font everywhere.
7. **The rotating planet.**
   - **Port UQM's rendering exactly (recommended),** checked by pixel hashes, like everything else that UQM's code decides.
   - A simpler textured sphere from the same surface map: quicker, but not UQM's look.

## Risks

- **Floating point in the sphere:** `RenderPlanetSphere` and the light map use `double` maths. C# follows the same IEEE rules, but the order of operations has to match exactly to get the same pixels. Item 7 checks pixel hashes for that reason, and starts with one world before doing all.
- **Download hosting:** SourceForge picks a mirror and redirects several times (checked: it works from here). If a mirror fails the player can retry; "use a file I have" (decision 3) is the fallback.
- **Re-recording traces with shapes (item 9)** needs UQM's intersection code to run in the recording program with frames loaded from the package. If that turns out too hard, the shapes can be recorded once into the reference as hashes and the intersection tested on its own; item 9 says which.
- **Legal:** the notice text is a draft to check with a lawyer before public release (design, open items). Nothing in this stage publishes the content; each player's engine downloads it from the official project.
