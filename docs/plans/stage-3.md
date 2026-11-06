# Stage 3 plan: story, conversations and the starbase

**Status:** approved by the owner on 2026-11-06, with all thirteen recommendations accepted (see "Decisions"). Stage 3a is being built: see "Progress".

Build order item: "3. Story flags, conversations, dialogue locks, story pauses and the group inventory" (`docs/design.md`), after stage 2b. Stage 2b left these to stage 3 as well: energy nodes, selling minerals and bio data, buying landers, crew, fuel and lander upgrades, cargo trading between players, and the worlds that start a conversation (`docs/differences.md` row 27).

A few terms used below:
- **A script** is one race's conversation code in UQM: C functions that say lines, offer replies and change the story.
- **A segue** is how a conversation ends: in peace, in a battle, in instant victory, or in game over.
- **A path** through a script is one sequence of replies from its start to its end.

## Splitting stage 3

Stage 3 as listed is much the largest stage. UQM's conversation scripts alone are about 23,000 lines of C in 27 directories, and each is a tree of branches that reads and writes the story state.

Most of them can only start once there are alien ships in space to meet. A race's ships fly at you in hyperspace or in a system and hail you, or you orbit their homeworld and they come out to talk; 17 of UQM's special systems start a conversation that way. Nothing in the engine flies those ships yet. Most of them can also end in a battle, which is stage 4.

So this plan splits stage 3 in three (decision 1) and plans the first part in detail:

| Part | What | Needs |
| --- | --- | --- |
| **3a. Talking, the story state and Earth** (this plan) | Covers the shared story state, the conversation engine and screen, story pauses, the dialogue lock and the group inventory. Also UQM's opening at Earth: the commander, Mercury's radioactives, the Lunar base and the Ilwrath ship. Then the allied starbase: talking to the commander, selling minerals, outfitting. Also captain and ship names, wallets and cargo trading | Nothing new: Sol, landing and mining are built |
| 3b. Ships in space and the races | Alien ships in systems and hyperspace (`encount.c`, `ipdisp.c`, and `hyper.c`'s encounters), among them the Ur-Quan drone at Earth and the Slylandro probes. Also hailing and the choice to attack or talk, the homeworlds that start a conversation, and the races' conversations met those ways, each checked against UQM. Plus Melnorme trading and credits | 3a's engine. Battles (stage 4) for the conversations that end in one |
| 3c. Story events and devices | The event calendar (`gameev.c`: the Ilwrath and Mycon missions, the Kohr-Ah's genocide and the rest), starting with the events whose days the clock passed in 3a and 3b. Energy nodes and their story hooks on the special worlds, Fwiffo on Pluto among them. The devices and using them (`devices.c`), the other time skips, and the story's cutscenes | 3a and 3b |

When 3b and 3c come is decision 2.

## Goal of stage 3a

The group plays UQM's opening together:

1. **The commander hails.** They fly to Earth, and the starbase's commander hails them. Every player sees Commander Hayes on UQM's communication screen, animated as UQM animates him, his words paged at UQM's pace. The player who flew in picks the replies, and the others watch while the game waits for everyone (a story pause).
2. **The errands.** The group fetches radioactives from Mercury and finds and takes the Lunar base on Luna.
3. **The alliance.** They tell the commander, and on that same visit the opening runs as it does in UQM:
   - an Ilwrath ship answers the Ur-Quan probe's call and attacks;
   - the commander, impressed, makes the alliance, and the starter names it;
   - two weeks pass for everyone;
   - the starbase opens, with 2,500 resource units for the group.

From then on each player can dock at the starbase on their own. There they can talk to the commander, one player at a time, sell their minerals for resource units, and buy fuel, crew, landers and modules for their own ship. Players can also give each other resource units, and cargo within one system.

### What the owner will be able to try when it's done

- Start a new game and fly to Earth. The commander hails you, and every player sees the talk while the clock is stopped. The replies your choices allow appear as in UQM, with your ship's and captain's names in them.
- Land on Mercury, pick up radioactives and come back. The talk goes on: the commander gives fuel or a lander with crew if you've run short, as he does in UQM.
- Land on Luna and pick up the Lunar base. The lander report shows and the lander takes off. Back at Earth, report it, and the opening plays out: the Ilwrath ship, the commander's alliance and its name, two weeks passing, and the starbase opening.
- Dock at the allied starbase with a friend docked too. Each of you has the starbase menu. One of you talks to the commander while the other's Talk option says he's busy. Sell your minerals and buy fuel and landers: each wallet changes only for its owner.
- In the same system, give a friend some of your minerals; anywhere, give them resource units.
- Save mid-story and load. The story state, each wallet and the starbase are as they were.

## Scope

**In stage 3a:**

- **The story state.** UQM's 447 game-state fields (1,238 bits, already extracted to `data/game-state.json`), held once for the whole game, saved and sent to every client. A few fields that hold one activity's working state are kept per player or per talk instead (item 1). The story's non-bit state goes in too: the prices, the element worths, the races' alliances and the date the bulletins read.
- **The conversation content.** Each race's lines (the `CONVERSATION` resources), its animation frames, colour maps and font.
- **The conversation engine** on the host, with UQM's script calls, checked against UQM's own scripts. Three scripts are ported:
  - the commander's (`comm/comandr/comandr.c`);
  - the allied starbase's (`comm/starbas/starbas.c`);
  - the Ilwrath's opening branch (`comm/ilwrath/ilwrathc.c`).
- **The communication screen** on the clients: the alien's window and animations, the subtitles, the replies and the conversation summary.
- **Story pauses and who replies,** and the dialogue lock for ordinary conversations.
- **The opening at Earth,** as `starbase.c`'s `VisitStarBase` runs it. This includes the Lunar base, the first energy node, with its lander report.
- **Captain and ship names** for every player. UQM's talk speaks them, and the alliance's name can use the captain's.
- **The group inventory:** the device states, which are shared because the story state is, and the Manifest's Devices list.
- **The allied starbase:** docking, its menu, selling minerals, outfitting (fuel, crew, landers, modules) and the shipyard's crew.
- **Wallets and cargo trading.** Each player has their own resource units, and transfers are allowed. Players in the same system can trade cargo (`docs/differences.md` rows 11 and 22).

**Not in stage 3a** (and where it goes):

| Left out | Why | Goes to |
| --- | --- | --- |
| The Ur-Quan drone orbiting Earth until it has delivered its message (`gensol.c`'s `init_probe`, `urquanc.c`), and the Slylandro probes in hyperspace | They're ships in space. Until 3b the drone isn't there, so `PROBE_MESSAGE_DELIVERED` stays unset and the commander's question about "the red thing" isn't offered. That's a temporary row in `docs/differences.md` | 3b |
| Every other race's conversation; the homeworlds that start one (17 special systems; row 27 for the others) | They need ships in space to meet | 3b |
| Melnorme trading, credits, bio data's worth, lander upgrades and shields to buy | Only the Melnorme sell them, met in space | 3b |
| Fwiffo on Pluto: Pluto's energy node, which costs the lander crew, starts a talk with a Spathi and adds him to the fleet (`gensol.c`, `spathic.c`) | A story hook with a talk and a fleet ship | 3c (the talk in 3b's engine). Until then Pluto's node stays where it is |
| The event calendar, the other energy nodes and their story hooks, using devices, the Talking Pet's time skip | Each is a story event of its own. Events fall due as the clock runs, but none runs until 3c: the new game's events are kept and saved, so 3c runs them from where the clock is (item 1) | 3c |
| Real battles, the Ilwrath fight among them, and buying fleet ships at the shipyard | Combat is stage 4 | Stage 4 (decision 3 for the Ilwrath fight) |
| Speech and music | Nothing plays sound yet. The base content has the music; the voices are an add-on | A later stage with audio |

## How UQM does it

Paths are relative to `sc2/src/uqm/` in UQM 0.8.0.

### The state

- **The story state** (`globdata.h`, `GET_GAME_STATE`, `SET_GAME_STATE`) is one packed array of 1,238 bits in 447 named fields. Every script and generator reads and writes it directly.
- **Fields that aren't story.** A few are the working state of what's happening now:
  - `GLOBAL_FLAGS_AND_DATA` says where a talk is: at the starbase, at a homeworld, or at a broadcaster. `init_race` picks the Spathi's script by it, and the screen's title follows it.
  - `BATTLE_SEGUE` holds a talk's ending.
  - `STARBASE_VISITED` is set by the allied commander's talk and cleared when the player leaves the starbase menu.
  - `ESCAPE_COUNTER` and `BATTLE_PLANET` are the same kind of field.
  - The lander upgrade and shield bits (`IMPROVED_LANDER_*`, `LANDER_SHIELDS`), which stage 2b made each ship's own.
- **State outside the bits.** The scripts read the rest of the game state too:
  - the prices: `ModuleCost`, `FuelCost`, and `CrewCost`, which rises by 2 once the crew bought (`CREW_PURCHASED0/1`) crosses `CREW_EXPENSE_THRESHOLD` (`shipyard.c`, `CrewTransaction`);
  - each element's worth (`ElementWorth`);
  - the races' alliances (`CheckAlliance`, from the race list);
  - the flagship's fleet (`CalculateEscortsWorth`; one bulletin adds three Arilou ships, `AddEscortShips`);
  - the date (`GameClock`, `CheckTiming` for the bulletins).

### A conversation

(`comm.c`, `commglue.c`, `comm.h`)

- **Starting.** `InitCommunication` picks a script by its conversation id (`init_race`). The script gives the race's description (`LOCDATA`): its animation sequences, text placement, colour maps, font and music, plus the functions for the start, after the talk, and clean-up. The script's set-up changes that description before the talk. For example, the commander's turns an ambient animation and the low-power music on or off by `RADIOACTIVES_PROVIDED`.
- **The script itself** is C code made of callbacks:
  - `NPCPhrase` queues the alien's lines by their id;
  - `Response(id, func)` offers the player a reply, with the function to call if it's chosen (8 replies at most);
  - `PLAYER_SAID` tells a callback which reply led to it;
  - `PHRASE_ENABLED` and `DISABLE_PHRASE` mark the player's phrases used up; the table is reloaded for each talk;
  - `construct_response`, `NPCNumber` and `NPCPhrase_splice` splice names and numbers into lines.
- **Running.** The engine plays the queued lines, then shows the replies. Choosing a reply calls its function, which queues more lines and offers more replies; a call that offers none ends the talk. The talk ends with its segue (`setSegue`).
- **Looking back.** The player can review the conversation so far (`DoConvSummary`) and replay the last line.
- **Reaching outside the talk.** Scripts read and change the flagship directly (`GLOBAL_SIS`):
  - **The commander's gifts:** 5 units of fuel to a ship with under 2 units, or a lander with 4 crew to a ship with none.
  - **Selling minerals:** the starbase's commander buys the hold's minerals, each unit at its element's worth (`SellMinerals`).
  - **The mission talk** grades the flagship's modules.
  - **Names:** the talk speaks the captain's and the ship's (`CommanderName`, `ShipName`).
  - **The screen:** scripts drive the animation and colours (`AlienTalkSegue`, `XFormColorMap`, turning an ambient animation off).
  - **The random generator:** they use `TFB_Random`, the same generator the animations draw from, for things like the allied commander's greeting.
- **With or without voices.** A few lines differ depending on whether voices are on (`usingSpeech`).
- **The lines** (content `comm.<race>.dialogue`): a text file of entries, each `#(ID) voice.ogg` followed by its text. The player's phrases are in the same file, under their own ids.
- **The pace without voices** (`SplitSubPages` in `libs/sound/trackplayer.c`). Each line of an entry is a page, shown for 80 milliseconds a character (`TEXT_SPEED`), and at least a second.
  - A line broken mid-sentence ends with "..." and the next page starts with "..".
  - Spliced text joins the page before it and adds no time.
  - The last page stays until the player moves on.
- **The animations** (`commanim.c`):
  - ambient sequences (blinking and the like), each with its frame and restart rates, played random, cyclic, back and forth or once, blocking each other by masks;
  - a talking sequence while the alien speaks;
  - a transition;
  - colour-map animations.

  Frames come from `comm.<race>.graphics` and colour maps from `comm.<race>.colortable`.

### The opening

(`starbase.c`, `VisitStarBase`; `comm/comandr/comandr.c`)

Until `STARBASE_AVAILABLE` is set, touching Earth's starbase starts the commander's talk. Its first visits go:

1. **First contact.** He asks for radioactives, which are on Mercury. UQM never takes them out of the hold: bringing them sets `RADIOACTIVES_PROVIDED`.
2. **The Lunar base.** He warns of the Lunar base. It's an energy node on Luna (`gensol.c`, `pickupEnergy`). Picking it up shows the lander report, sends the lander up, and sets `MOONBASE_DESTROYED` and `MOONBASE_ON_SHIP`.

On the visit after that, it all runs at once:

1. **The report.** The player reports the base dealt with (three replies), and the commander warns that the Ilwrath are coming (`PROBE_ILWRATH_ENCOUNTER`).
2. **The Ilwrath ship.** It talks (`ilwrathc.c`). Its four replies all lead to the battle (`Segue_hostile`). Losing is game over, and there's no retreating before the starbase is available (`battle.c`).
3. **The alliance.** The commander talks again ("What a beautiful sight..."), makes the alliance, and the player names it from four choices, one of them using the captain's name (`NEW_ALLIANCE_NAME`). This sets `STARBASE_AVAILABLE`.
4. **Two weeks pass.** With the Lunar base aboard, the screen fades to black over two seconds and 14 days pass (`DoTimePassage`).
5. **The starbase is ready.** The allied commander's talk (`starbas.c`) says so and gives 2,500 resource units. It notes the date for the bulletins and clears `MOONBASE_ON_SHIP` after.
6. **The starbase menu** opens.

### The allied starbase

(`DoStarBase`)

The starbase menu has four choices:
- **Talk to the commander** (`starbas.c`, the largest script): the mission, the history, the alien races, new devices (`DiscussDevices`), news bulletins by date (`CheckBulletins`), and selling minerals, with lines chosen by how much has been sold before (`LIGHT/MEDIUM/HEAVY_MINERAL_LOAD`).
- **Outfit the flagship** (`outfit.c`): fuel, landers up to 10, and modules. It also renames the ship and has save and load.
- **The shipyard** (`shipyard.c`): crew for the flagship and fleet ships. It also has save and load.
- **Leave.**

### The clock

The clock stops in conversations and at the starbase in UQM. Here it runs in every activity at one rate (`docs/differences.md` row 1); only story pauses and the host's pause stop it.

## Co-op rules for stage 3a

Decided already (`docs/design.md`, `docs/differences.md`):

- **One story state for the group** (row 10): plot checks look at the group, so the device states are the group inventory.
- **Story conversations and events** pause the game for everyone and show it to all; whoever started it replies, the others watch (row 2). A time skip is a story pause too (row 3; it gains the alliance's two weeks).
- **Ordinary conversations:** one player per alien race at a time (the dialogue lock, row 17), released when the talk ends or the player disconnects.
- **Money and cargo:** separate wallets, transfers allowed (row 11). Cargo trading between players in the same solar system, limited by the receiver's free storage (row 22).
- **Escorts are full ships:** they can talk to aliens (design: "Escort abilities").

New for stage 3a: decisions 3 to 12 below.

## Work, in order

Each item ends with tests that run headless, each test class in a topic (`tests/topics.json`; new topics for the story and the starbase). Conversations are checked as the lander was: UQM's own scripts run in the recording program with the engine stubbed and the replies scripted, and the port must say the same, offer the same and change the same.

1. **The story state.**
   - **Shared fields:** the 447 fields as world state, read and written by name with UQM's field widths, except those kept per player or per talk (decision 11), which are listed with the reason for each (in the code, since `data/` holds only what's extracted from UQM).
   - **The non-bit story state:** the prices with the crew price's rise, the element worths, the races' alliances, and the event queue from `data/new-game.json`, kept and saved but not run until 3c.
   - The system generator's story inputs (`SystemStoryState`) are read from it.
   - **Saves:** version 6; older saves load with UQM's new-game state.
   - **Clients** are sent the changes only (protocol version 7).
   - Check: unit tests, save and wire round trips, and every field's place against `globdata.h` (the extractor's own check).
2. **Names.** Each player's captain and ship names, kept in the save and sent with the ship. UQM's defaults for the flagship (from the game strings), and decision 10 for guests. Renaming the ship is in outfitting (item 8).
   - Check: unit tests, saves.
3. **The conversation content.**
   - **The lines:** the `CONVERSATION` reader gives entries by id, the alien's lines and the player's phrases, as UQM's loader splits them.
   - **Each race's description** (`LOCDATA`) as it stands once the script's set-up has run, recorded from UQM rather than read from the source, since set-up changes it.
   - **The rest of the content:** the `comm.*` graphics, colour tables and fonts.
   - Check: what UQM's own loaders give, recorded as hashes (content stage, decision 5).
4. **The conversation engine.** On the host, UQM's script calls in C#:
   - `NPCPhrase`, `Response` (8 at most), `PLAYER_SAID` and the phrase table, copied for each talk;
   - `construct_response`, `NPCNumber`, `NPCPhrase_splice` and the segues;
   - the story state through the world, and the ship and names per decision 4;
   - lines with voices off (`usingSpeech` false), as UQM without the voice add-on;
   - a random generator for the scripts of its own on the host (decision 12).

   A talk is a state machine: the alien's lines, then the replies, then the chosen reply's function.

   The recording program gains a conversation mode. It compiles a race's script with UQM's own `commglue.c` and stub engine calls, and uses the content's string tables, since phrases are turned off and spliced in them. It starts the script from a given story state, ship and date, takes replies from a script, and records the talk: every line queued, every reply offered, every state and ship write, every animation and colour call, every random number drawn, and the segue. It also walks every path through a script from a set of starting states chosen to reach each condition the script reads.
   - Check: the engine's port of the commander's script against every recorded path.
5. **The opening.**
   - **The scripts:** `comandr.c` and `ilwrathc.c`'s opening branch ported.
   - **The sequence:** `VisitStarBase`'s, in UQM's order on the visit the base is reported: the commander, the Ilwrath ship and its battle (decision 3), the commander again with the alliance's name, the two weeks passing (a story pause), then the allied commander's "starbase is ready" talk with its 2,500 resource units (decision 5).
   - **The Lunar base:** Sol's `pickupEnergy` for Luna, the first energy node on a surface, with the lander report text, and the lander that picked it up taking off (decision 9).
   - **Touching Earth's starbase** starts it, which ends row 27 for the starbase.
   - Check: every recorded path through the commander's script; the sequence and the Lunar base by tests of our own against `starbase.c` and `gensol.c`; the lander report against UQM's.
6. **Story pauses.**
   - The clock and every player's activity stop: flight, landers on surfaces, the orbit menus.
   - Every client is shown the talk (decision 6 says which talks). Only the player who started it replies, or moves the text on (decision 7).
   - A player joining during one watches it.
   - The starter leaving hands the replies on (decision 7).
   - A time skip shows its fade to everyone.
   - Check: tests of our own; the network test, with a guest watching the host's talk.
7. **The dialogue lock.**
   - One player per race at a time. The conversation ids map to races: the Yehat and their rebels are one race; the Spathi's one id runs two scripts.
   - At the starbase the lock is on the menu's Talk option: a second player is told who's talking (decision 8).
   - The lock is released when the talk ends or its player leaves.
   - Check: tests of our own.
8. **The allied starbase.**
   - **Docking:** touching it once it's available.
   - **The menu:** every docked player has their own menu at once, and only the host has save and load.
   - **The commander's talk:** `starbas.c` ported. The mission, the history, the races, the new devices, the bulletins by date, and selling minerals into the talker's wallet.
   - **Outfitting,** by `outfit.c`'s rules: fuel and its tanks, landers up to 10, modules in their slots, renaming the ship, and prices from the story state. The flagship has the full list; escorts per decision 9.
   - **The shipyard's crew** for each player's own ship, at the shared crew price.
   - Check:
     - every recorded path through `starbas.c` from a set of states and dates;
     - outfitting's rules and prices against UQM's code, recorded: the recording program's outfitting mode, with `DoInstallModule`, `ChangeFuelQuantity` and `CrewTransaction` and the drawing stubbed.
9. **Wallets, transfers and cargo trading.**
   - Each player's ship has its own resource units (`ResUnits`).
   - A player can give units to another player anywhere, and minerals and bio data to a player in the same system, up to their free storage.
   - These go as reliable messages, refused with a reason.
   - Check: tests of our own; saves.
10. **The group inventory.** The fields that are things the group carries (the `*_DEVICE` fields and the like) are marked so in our data file (item 1). The Manifest's Devices list shows them as `devices.c` lists them. Using them is 3c.
    - Check: the list's order and names against `devices.c`, recorded.
11. **Client views.**
    - The communication screen: the alien's window with its animations and colour maps, the subtitles in the race's font and colours, the replies, the summary.
    - What a watching player sees during a story pause.
    - The starbase menu, outfitting and the shipyard's crew screen.
    - The wallet and trading screens, and the lander report.
    - Check: what UQM draws, recorded as in 2b's item 10. The communication screen's animations run on time, so the recording program steps a clock of its own. Screenshots too.
12. **Docs.**
    - `docs/how-to-try/stage-3a.md`.
    - Update the design, porting inventory (the commander's talk is story only before the alliance), differences (row 3's new time skip, row 27, the drone's absence until 3b, decision 3's battle) and data docs.

Rough size:
- **The large ones:** items 4, 8 and 11 (the engine and its recording program; `starbas.c` is 1,961 lines and outfitting 795; the communication screen's drawing).
- **Then:** items 5 and 6.
- **Small to medium:** the rest.

## Progress

| Item | State | Where |
| --- | --- | --- |
| 1. The story state | Built | `src/Crateris.Core/Story/`: `GameStateTable` (UQM's 447 fields from `data/game-state.json`, checked to sit end to end in 1,238 bits); `StoryState`, the group's one story state on the host: the fields packed and read as `getGameState`, `setGameState` and their 32-bit forms do (a value too big runs into the next bits in its byte, as in UQM), the fields that aren't story refused with where each lives (`StoryState.NotShared`, decision 11; `docs/differences.md` row 36: where a talk is and how it ends, the allied commander's greeting, a battle's escape counter and planet, the lander's upgrades and shields, the Melnorme credits, which side of QuasiSpace a ship is on, whether a lander is down and whether a visit changed its world), the prices (`CrewCost`, `FuelCost`, `ModuleCost` in units of `MODULE_COST_SCALE`), with `CrewTransaction`'s rise of 2 once 1,000 crew are bought (wrapping as UQM's byte does), the element worths, each race's standing (`allied_state`: the humans allied at the start, as `InitGameStructures` makes them, every other race not) and the clock's events (`EventQueue`: `AddEvent` and `ValidateEvent`'s dates, same-day events in the order added, `NUM_EVENTS` at most). A new game runs `AddInitialGameEvents`: the one event due at once (`SLYLANDRO_RAMP_UP`, which only changes the story) runs, the rest wait for stage 3c. The rules come from `data/` (`StoryRules`, with the system catalogue); a world with systems has a story state, and the systems' story inputs (`SystemStoryState`) are read from it; nothing in 3a changes them, so a save whose story would make other systems is refused. Saves (version 6) keep the fields that aren't 0 by name, the prices, the standing and the events. Until 3c no event runs, so the clock passes their days and they stay queued; UQM's clock runs only the first event and only on its day (`processClockDayEvents`), so 3c has to run those already due when it starts, or one in the past would hold up the rest. Older saves load with a new game's story, its events counted from the save's own day (those already past, such as the Arilou's date, aren't queued, as `AddEvent` queues none in the past); an older save with a story, or a newer one without, is refused. A bit a too-big value runs into a field kept elsewhere is in snapshots but not in saves, which write the story's fields by name. Snapshots (protocol version 7) carry the story only when it changed: the bytes that differ from the guest's base, and the prices and standing only if they changed (one field changed: a few bytes). The list in the plan's data file is a table in the code (`NotShared`), since `data/` holds only what's extracted from UQM. Checked by `StoryStateTests` (every field against a bit-by-bit reading, without touching any other; UQM's overflow; the new game's prices and events; event dates and order; the crew price; saves and bad saves; snapshots in full and as deltas over random changes; bad wire data) |
| 2. Names | Built | `src/Crateris.Core/World/ShipNames.cs`: each player's ship name (`PlayerShip.Name`) and captain's name (`Player.CaptainName`), as UQM's talks will speak them. The flagship's are UQM's until renamed: "Vindicator" and "Zelnick", the content's `NAMING_STRING_BASE` + 2 and + 3 (`InitGameStructures`); a guest's escort is named from a new host setting when it's made (`GameSettings.EscortShipName`, placeholder "Wayfarer"), and a guest's captain is the name they join with (decision 10). The flagship's UQM names and a guest's join name as captain aren't stored, so the host's world needs no content to hold them; an escort's ship name is stored from the start, like a rename. `ShipNames.Ship` and `Captain` give the names, for a player or a snapshot's ship, with the content's strings. Every name is taken as UQM's text entry takes one typed (`DoTextEntry`): each character in turn if it's printable (`UniChar_isPrint`: no controls, no private use area) and still fits in 15 bytes (`SIS_NAME_SIZE`); text that isn't valid UTF-16 is left out; empty is allowed. (UQM cuts its default names with `utf8StringCopy`, which can split a character; the shipped ones fit whole.) `GameWorld.Rename` renames either or both (outfitting calls it in item 8). Saves (version 7) keep the names; an escort from an older save is named from the settings (the default if the setting is missing or null), and an older save with names, or a name UQM couldn't hold, is refused. Snapshots (protocol version 8) carry each ship's stored names, a delta only when they change; a name UQM couldn't hold is refused. Checked by `ShipNamesTests` (cutting and the printable rule, the defaults against the content, a guest's names and their following the join name until renamed, renaming in full snapshots and deltas and back to none, saves old and new, a missing setting, bad names) and `SnapshotCodecTests`, whose random ships now have names |
| 3. The conversation content | Built | `src/Crateris.Core/Content/Conversation.cs`: a CONVERSATION resource read exactly as UQM's `_GetConversationData` reads it: entries by `#(NAME)` header with their voice clip, their lines joined, the quirks of `strtok` and `fgets`, and the 2,048-entry limit; `IndexOf` finds a name's first entry (UQM's own game code never looks lines up by name; the scripts number them). `data/conversations.json` (extracted, `docs/uqm-data.md`): each of the 27 scripts' race description (`LOCDATA`) as its init function leaves it at a new game: the extractor compiles each script's own file and runs its init function with every story bit 0, a new game's alliances and UQM's 320 by 240 screen, since the subtitles' place and width are worked out at run time; its phrase ids in `strings.h`'s order; and which scripts each conversation id runs (`init_race`, and `InitCommunication`'s running a drone's talk as the Ur-Quan's); read by `src/Crateris.Core/Data/ConversationScripts.cs`. An init function's branches for a later story (the commander dropping his low-power colour cycle and music, the Spathi's and the Utwig's music) are code, ported with each script. Every script's phrase ids match its conversation's entries name for name, so the port can name lines as the scripts do and number them as UQM does. Frames (`comm.*.graphics`) read as `_GetCelData` does with the content stage's `Animation`; colour tables and fonts with its `BinaryTable` and `UqmFont`, already checked against UQM for every comm resource. A colour animation's index wraps round its table, as UQM's `SetAbsStringTableIndex` does (the Talking Pet's strobe runs 30 maps over 2); an alternate song the base content hasn't (the commander's low-power one, the rebels', the starbase's) will fall back to the song when the screen plays it (item 11), as `HailAlien` does. The Zoq-Fot-Pik's subtitles have no place in the description (0, 0): their script sets it per speaker as they talk. Checked against UQM's own loader: inputs of our own for each quirk (`tests/golden/conversation-cases.json`, in full) and every conversation in the package (`conversations.json`, the texts as hashes), recorded by the strings program's new `conversation` mode; every conversation's frames (`comm-graphics.json`: each `.ani` line and its image as UQM's PNG loader gives it, hashed). `ConversationContentTests` (topic content) also checks each description's resources load and its picture animations' frames are all there; `tools/extract/test_data.py` checks the data |

## Decisions

All thirteen recommendations were accepted on 2026-11-06. Recommendations first.

1. **Splitting stage 3.**
   - **Three parts, 3a, 3b and 3c, as above (recommended).** 3a can be played through, from the opening to the allied starbase, without ships in space or battles.
   - One stage 3 as listed: a single plan of about twice 2b's size before anything can be played.
2. **When combat comes.**
   - **Stage 4 (combat) after 3a, then 3b and 3c (recommended).** Almost every race's conversation can end in a battle, and meeting a race means its ships flying at you. With combat built first, each conversation ported in 3b can be played through as UQM plays it.
   - 3a, 3b, 3c, then combat, as the build order lists them. Each battle ending would need a stand-in until stage 4: the races could be talked to but not fought.
3. **The Ilwrath ship in the opening,** before combat exists. In UQM it talks, then fights: losing is game over, and you can't retreat.
   - **It hails and talks, and its battle counts as won (recommended).** The opening goes on as after a victory: the commander's next words don't depend on how the fight went, only on the Ilwrath having come. A temporary row in `docs/differences.md` until stage 4 puts the real fight in, which also decides who fights it.
   - Leave the Ilwrath out, as if beaten. Quicker, but the opening skips a scene.
   - Keep the opening short of the alliance until stage 4. The starbase would stay unavailable through 3a.
4. **Whose ship and names a conversation uses,** where UQM's scripts use the flagship's.
   - **The starter's ship and names, except where the story is the flagship's (recommended).**
     - The starter's: they reply, so their names are spoken, the commander's fuel and lander go to their ship if it has run short, and their minerals are sold.
     - The flagship's: the mission talk's grading of the ship's modules, since the flagship carries the mission and its upgrades.
     - In a story talk the others watch, and their ships are untouched.
   - The host's flagship always: the story is the flagship's, but a guest's minerals couldn't be sold.
5. **Gifts to the group, and the counters a script keeps.** These are the starbase's 2,500 resource units; the counters that pick the commander's "again" lines (`GIVEN_FUEL_BEFORE`, `LANDERS_LOST`) and the sale lines (`*_MINERAL_LOAD`); and the crew bought, which raises the crew price.
   - **The 2,500 units are split equally between the players in the game, as battle salvage is; the counters are shared, as in UQM (recommended).** One starbase's supplies and crew are the group's, so a second player gets the "again" lines and the smaller fuel gift, and the crew price rises with what the group has bought: the group pays what a solo player would.
   - Everything to the starter, and the counters per player: each player has UQM's own talk and prices, but the group gets more from Earth than a solo player does.
6. **Which conversations are story** (an open item in `docs/design.md`).
   - **For 3a, by what they do (recommended).**
     - Story: the commander's talks before the alliance, the Ilwrath's, the two weeks passing, the "starbase is ready" talk, and the talks `VisitStarBase` starts on arrival later (after the Chmmr's bomb, in 3c).
     - Ordinary, under the lock: talking to the allied commander from the starbase menu.
     - Each conversation's rule goes in a data file of our own, so 3b and 3c add theirs the same way.
   - Every conversation is story: simple, but each trip to sell minerals would stop the whole group.
7. **During a story pause.**
   - **Everything stops, and replies pass on if the starter leaves (recommended).**
     - Flight, landers, menus and the clock stop, and every client shows the talk.
     - If the starter leaves, the replies pass to the next player in join order, the host first, so the story can't be left half-told.
     - The text goes at UQM's pace without voices (above: 80 milliseconds a character, a second at least a page). The replying player can move it on and back as UQM allows, and everyone watching follows them.
   - Everything stops, and the talk ends if the starter leaves. Simpler, but a dropped connection could cut off a story branch with its state half-set.
8. **A second player at a locked conversation.**
   - **Told who's talking (recommended):** at the starbase, the Talk option says the commander is busy with that player, and the rest of the menu works. In 3b, a ship hailing a race already in a talk is told the same and left in flight.
   - Watching the other player's talk: closer to a story talk, but it would show someone else's sale.
9. **The Lunar base with several landers on Luna.** UQM sends up only the lander that picks it up.
   - **The same: that lander takes off and shows its player the lander report; the others stay and are told (recommended).** The talk it leads to is at Earth, a story pause for all.
   - Every lander on Luna takes off with it.
10. **Escorts at the starbase, and guests' names** (the escort module set is still an open item).
    - **Escorts buy fuel, crew and landers within their kit's limits, and storage bays (recommended).** The limits are 10 crew and a placeholder fuel tank, both settings, as the escort's kit is. The other modules wait for the escort module set, so the flagship keeps its unique upgrades (design: "Escort upgrades"). A guest's captain is named by the name they join with, and their ship by a setting's default until they rename it.
    - Decide the escort module set before item 8.
11. **The fields that are kept per player or per talk,** not shared.
    - **Kept per player or per talk (recommended):** where a talk is (`GLOBAL_FLAGS_AND_DATA`), its ending (`BATTLE_SEGUE`), whether this player has had the allied commander's greeting this visit (`STARBASE_VISITED`), the escape counter and battle planet, and the lander upgrades and shields (each ship's own since 2b). Sharing these would let one player's visit change another's.
    - Share every field as UQM has it: simplest, but two players docked at once would change each other's greetings and bulletins.
12. **The scripts' random numbers.** In UQM the scripts and the animations draw from one generator, so the allied commander's greeting depends on how the animations have run.
    - **A generator of its own for the scripts on the host, seeded per talk (recommended):** every client sees the same talk, and the animations, drawn on each client, can't change it. The recordings seed UQM's generator and record each draw, so the port draws the same numbers in the same places.
    - One generator shared with the animations, as UQM: the host would have to run the animations to keep it.
13. **How conversations are checked.**
    - **Recorded from UQM's own scripts, every path (recommended),** as every stage so far: the recording program runs each script with the engine stubbed and walks its replies. The port must match it line for line, reply for reply, and write for write.
    - Port and check them by tests of our own only. Quicker, but nothing would show where a branch differs from UQM.

## Risks

- **Recording the scripts.** They call into the engine for much more than lines and replies:
  - the animation and colour calls;
  - the flagship and its fleet (`AddEscortShips`, `CalculateEscortsWorth`), the race list (`CheckAlliance`) and the ship queues (`CloneShipFragment`);
  - the clock and `CheckTiming`, and `usingSpeech`;
  - the new-game state (`InitGameStructures`, which needs the game strings);
  - the input and drawing that selling minerals waits on (`DrawCargoStrings`, `AnyButtonPress`, `SleepThread`);
  - `AlienTalkSegue`, whose aborted waits skip the ones after.

  The stubs record each call and give UQM's answers from a small model of that state. If a script needs more of UQM than stubs can give, item 4 says so and records what it can.
- **Walking every path.** Branches depend on the story state, the ship and the date as well as on the replies, so a walk is only as good as its starting states. Each script's walk starts from states chosen to reach each condition it reads, and the checks list them.
- **The communication screen runs on time,** where the lander ran on frames. The recording program steps a clock of its own, as the lander's did for its waits.
- **Size.** 3a ports two scripts and part of a third, plus the engine and the screen. 3b's two dozen scripts reuse those, so 3a's cost is mostly the engine and the screen.
