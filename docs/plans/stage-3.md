# Stage 3 plan: story, conversations and the starbase

**Status:** a draft for the owner's approval. Nothing is built until the decisions below are made.

Build order item: "3. Story flags, conversations, dialogue locks, story pauses and the group inventory" (`docs/design.md`), after stage 2b. Stage 2b left these to stage 3 as well: energy nodes, selling minerals and bio data, buying landers, crew, fuel and lander upgrades, cargo trading between players, and the worlds that start a conversation (`docs/differences.md` row 27).

## Splitting stage 3

Stage 3 as listed is much the largest stage. UQM's 27 conversation scripts alone are about 24,000 lines of C, and each is a tree of branches that reads and writes the story state. Most of them can only start once there are alien ships in space to meet: a race's ships fly at you in hyperspace or in a system and hail you. Nothing in the engine flies those ships yet. Most of them can also end in a battle, which is stage 4. So this plan splits stage 3 in three (decision 1) and plans the first part in detail:

| Part | What | Needs |
| --- | --- | --- |
| **3a. Talking, the story state and Earth** (this plan) | The story state shared by the group, the conversation engine and screen, story pauses, the dialogue lock, the group inventory, the opening at Earth (the commander, Mercury's radioactives, the Lunar base, the Ilwrath ship), and the allied starbase: talking to the commander, selling minerals, outfitting; wallets and cargo trading | Nothing new: Sol, landing and mining are built |
| 3b. Ships in space and the races | Alien ships in systems and hyperspace (`encount.c`, `ipdisp.c`, the hyperspace encounters in `hyper.c`), hailing and the attack-or-talk choice, and the races' conversations met that way, each checked against UQM. Melnorme trading and credits | 3a's engine; battles (stage 4) for the conversations that end in one |
| 3c. Story events and devices | The event calendar (`gameev.c`: the Ilwrath and Mycon missions, the Kohr-Ah's genocide and the rest), energy nodes and their story hooks on the special worlds, the devices and using them (`devices.c`), time skips, the story's cutscenes | 3a and 3b |

When 3b and 3c come is decision 2.

## Goal of stage 3a

The group plays UQM's opening together. They fly to Earth, and the starbase's commander hails them: every player sees Commander Hayes on UQM's communication screen, animated as UQM animates him, his words paged at UQM's pace. The player who flew in picks the replies and the others watch, while the game waits for everyone (a story pause). The group fetches radioactives from Mercury, finds the Lunar base on Luna, and brings it back. They meet the Ilwrath ship that answers the Ur-Quan probe's call and name their alliance. The starbase joins them, and two weeks pass for everyone. From then on each player can dock at the starbase on their own: talk to the commander (one at a time), sell their minerals for resource units, and buy fuel, crew, landers and modules for their own ship. Players in the same system can give each other cargo and resource units.

### What the owner will be able to try when it's done

- Start a new game and fly to Earth. The commander hails you, and every player sees the conversation, the clock stopped. Choose the replies: the ones your choices allow appear, as in UQM, with your ship's and captain's names in them.
- Land on Mercury, pick up radioactives and come back: the commander's talk goes on, and he gives you fuel or a lander if you've run short, as he does in UQM.
- Land on Luna and pick up the Lunar base: the lander report shows, the lander takes off, and back at Earth the opening carries on to the Ilwrath ship, the alliance's name and the two weeks passing.
- Dock at the allied starbase with a friend docked too. Each of you has the starbase menu. One of you talks to the commander while the other is told he's busy. Sell your minerals, buy fuel and landers, and see each wallet change only for its owner.
- In the same system, give a friend some of your minerals and resource units.
- Save mid-story and load: the story state, each wallet and the starbase are as they were.

## Scope

**In stage 3a:**

- The story state: UQM's 447 game-state fields (1,238 bits, already extracted to `data/game-state.json`), held once for the whole game, saved and sent to every client.
- Reading the conversation content: each race's lines (the `CONVERSATION` resources), its animation frames, colour maps and font.
- The conversation engine on the host, with UQM's script calls, checked against UQM's own scripts. The first scripts ported are the commander's (`comandr.c`) and the allied starbase's (`starbas.c`), plus the Ilwrath's opening branch (`ilwrath.c`).
- The communication screen on the clients: the alien's window and animations, the subtitles, the replies, the conversation summary.
- Story pauses and who replies; the dialogue lock for ordinary conversations.
- The opening at Earth, as `starbase.c`'s `VisitStarBase` runs it, and the Lunar base: the first energy node, with its lander report.
- The group inventory: the device states, shared because the story state is, and the Manifest's Devices list.
- The allied starbase: docking, its menu, selling minerals, outfitting (fuel, crew, landers, modules), and the shipyard's crew.
- Wallets (resource units per player) and transfers; cargo trading between players in one system (`docs/differences.md` rows 11 and 22).

**Not in stage 3a** (and where it goes):

| Left out | Why | Goes to |
| --- | --- | --- |
| Alien ships in space, hailing, every race's conversation but the commander's, the starbase's and the Ilwrath's opening branch | They need ships in space to meet | 3b |
| Melnorme trading, credits, bio data's worth, lander upgrades and shields to buy | Only the Melnorme sell them, met in space | 3b |
| The event calendar, the other energy nodes and their story hooks, using devices, the Talking Pet's time skip | Each is a story event of its own | 3c |
| Real battles, the Ilwrath fight among them, and buying fleet ships at the shipyard | Combat is stage 4 | Stage 4 (decision 3 for the Ilwrath fight) |
| Speech and music | Nothing plays sound yet; the base content has the music, the voices are an add-on | A later stage with audio |

## How UQM does it

Paths are relative to `sc2/src/uqm/` in UQM 0.8.0.

- **The story state** (`globdata.h`, `GET_GAME_STATE`, `SET_GAME_STATE`): one packed array of 1,238 bits in 447 named fields. Every script and generator reads and writes it directly.
- **A conversation** (`comm.c`, `commglue.c`, `comm.h`):
  - **Starting.** `InitCommunication` picks a script by its conversation id (`init_race`). The script gives the race's description (`LOCDATA`): its animation sequences, text placement, colour maps, font and music. It also gives three functions: the start, after the talk, and clean-up.
  - **The script.** The script is C code made of callbacks. `NPCPhrase` queues the alien's lines by their id. `Response(id, func)` offers the player a reply, with the function to call if it's chosen. `PLAYER_SAID` tells a callback which reply led to it. `PHRASE_ENABLED` and `DISABLE_PHRASE` mark the player's phrases used up, for that talk only. `construct_response` and `NPCNumber` splice names and numbers into lines.
  - **Running.** The engine plays the queued lines, then shows the replies. Choosing a reply calls its function, which queues more lines and offers more replies. A call that offers none ends the talk.
  - **Ending.** The talk ends in peace, a battle, an instant victory or game over (`setSegue`).
  - **Extras.** The player can review the conversation so far (`DoConvSummary`) and replay the last line.
- **Reaching outside the talk:** scripts also read and change the flagship directly (`GLOBAL_SIS`):
  - the commander gives a lander or 5 units of fuel to a ship that has run short;
  - the starbase's commander buys the hold's minerals (`SellMinerals`: each unit at its element's worth, `ElementWorth`);
  - names come from the flagship (`CommanderName`, `ShipName`).

  Scripts also drive the animation and colours (`AlienTalkSegue`, `XFormColorMap`, turning an ambient animation off).
- **The lines** (content `comm.<race>.dialogue`): a text file of entries, each `#(ID) voice.ogg` followed by its text. Player phrases are in the same file, by their own ids. Without voice, the track player pages each line and shows each page for 80 milliseconds a character (`TEXT_SPEED`, `libs/sound/trackplayer.c`).
- **The animations** (`commanim.c`): ambient sequences (blinking and the like, each with its frame and restart rates, random or cyclic, blocking each other by masks), a talking sequence while the alien speaks, a transition, and colour-map animations. Frames come from `comm.<race>.graphics` and colour maps from `comm.<race>.colortable`.
- **The opening** (`starbase.c`, `VisitStarBase`): until `STARBASE_AVAILABLE` is set, touching Earth's starbase starts the commander's talk (`comandr.c`).
  - **The commander's talk.** It goes in steps:
    - first contact;
    - the radioactives he needs (from Mercury; UQM never takes them out of the hold);
    - the Lunar base he warns of;
    - the Ilwrath ship.
  - **The Lunar base.** It's an energy node on Luna (`gensol.c`, `pickupEnergy`). Picking it up shows the lander report, takes the lander off, and sets `MOONBASE_DESTROYED` and `MOONBASE_ON_SHIP`.
  - **The Ilwrath ship.** Once the commander has told you of the probe (`PROBE_ILWRATH_ENCOUNTER`), an Ilwrath ship arrives. It talks (`ilwrath.c`) and fights you, and the commander talks again after.
  - **Two weeks pass.** With the Lunar base aboard, two weeks pass with the screen faded to black (`DoTimePassage`, 14 days). The commander then makes the alliance and asks its name, and the starbase is available.
- **The allied starbase** (`DoStarBase`): a menu of talking to the commander (`comm/starbas/starbas.c`, the largest script), outfitting the flagship (`outfit.c`: fuel, landers, modules), the shipyard (`shipyard.c`: crew for the flagship, fleet ships), and leaving.
  - **The commander's talk** covers the mission, the history, the alien races, news bulletins by date (`CheckBulletins`), and selling minerals.
  - **Prices** come from the game state: `ModuleCost`, `FuelCost`, `CrewCost` (in `data/new-game.json`).
- **The clock** stops in conversations and at the starbase in UQM. Here it runs in every activity at one rate (`docs/differences.md` row 1), and only story pauses and the host's pause stop it.

## Co-op rules for stage 3a

Decided already (`docs/design.md`, `docs/differences.md`):

- **One story state for the group** (row 10): plot checks look at the group, so the device states are the group inventory.
- **Story conversations and events pause the game for everyone and show it to all; whoever started it replies, the others watch** (row 2). A time skip is a story pause too (row 3).
- **Ordinary conversations: one player per alien race at a time** (the dialogue lock, row 17), released when the talk ends or the player disconnects.
- **Separate wallets, transfers allowed** (row 11). **Cargo trading** between players in the same solar system, limited by the receiver's free storage (row 22).
- **Escorts are full ships:** they can talk to aliens (design: "Escort abilities").

New for stage 3a: decisions 3 to 9 below.

## Work, in order

Each item ends with tests that run headless. Conversations are checked as the lander was: UQM's own scripts are run in the recording program with the engine stubbed and the replies scripted, and the engine's port must say the same, offer the same and change the same.

1. **The story state.** The 447 fields as world state, read and written by name, with UQM's field widths. The system generator's story inputs (`SystemStoryState`) are read from it. It's saved (save version 6; older saves load with UQM's new-game state) and sent to clients as changes only (protocol version 7).
   - Check: unit tests, save and wire round trips; every field's place against `globdata.h` (the extractor's own check).
2. **The conversation content.**
   - The `CONVERSATION` reader: entries by id, the alien's lines and the player's phrases, as UQM's loader splits them.
   - Each race's animation description, from the scripts' `LOCDATA`, extracted to `data/conversations.json`.
   - The `comm.*` graphics, colour tables and fonts.
   - Check: what UQM's own loaders give, recorded as hashes (content stage, decision 5); the extracted descriptions against the source.
3. **The conversation engine.** On the host, with UQM's script calls in C#:
   - `NPCPhrase`, `Response`, `PLAYER_SAID`, the phrase table copied for each talk, `construct_response`, `NPCNumber`, the segues;
   - the story state through the world, and the talking player's ship where UQM reads the flagship (decision 4);
   - a talk is a state machine: the alien's lines, then the replies, then the chosen reply's function.

   The recording program gains a conversation mode. It compiles a race's script with stub engine calls, starts it from a given story state and ship, takes replies from a script, and records the talk: every line queued, every reply offered, every state and ship write, every animation call, and the segue.

   It also walks every path through a script: each reply at each step, up to a depth, from a set of starting states.
   - Check: the engine's port of the commander's script against every recorded path.
4. **The opening.**
   - `comandr.c` ported.
   - `VisitStarBase`'s sequence before the starbase is available: the commander, the Ilwrath ship (`ilwrath.c`'s opening branch, then decision 3), the commander again, the two weeks passing (a story pause, decision 6), the alliance's name.
   - The Lunar base: Sol's `pickupEnergy` for Luna, the first energy node on a surface, with the lander report text and its take-off.
   - Touching Earth's starbase starts it, which ends the temporary row 27 for the starbase.
   - Check: every recorded path through the commander's script; the sequence and the Lunar base by tests of our own against `starbase.c` and `gensol.c`; the lander report against UQM's.
5. **Story pauses.**
   - The clock and every player's activity stop: flight, landers on surfaces, the orbit menus.
   - Every client is shown the conversation; only the player who started it replies, or skips a line (decision 7).
   - A player joining during one watches it.
   - The starter leaving hands the replies on (decision 6).
   - Check: tests of our own; the network test, with a guest watching the host's talk.
6. **The dialogue lock.** Ordinary conversations: one player per conversation id at a time. A second player is told who's talking and their ship stays where it is, as it does today (decision 8). The lock is released when the talk ends or its player leaves.
   - Check: tests of our own.
7. **The allied starbase.**
   - Docking: touching it once it's available.
   - The menu, with every docked player in their own menu at once.
   - The commander's talk, `starbas.c` ported: the mission, the history, the races, the bulletins by date, and selling minerals into the talker's wallet.
   - Outfitting, `outfit.c`'s rules: fuel and its tanks, landers up to 10, modules in their slots, prices from the story state. The flagship gets the full list; escorts per decision 9.
   - The shipyard's crew for each player's own ship.
   - Check: every recorded path through `starbas.c` from a set of states and dates; outfitting's rules and prices against UQM's code, recorded (the recording program's outfitting mode, `DoInstallModule`, `ChangeFuelQuantity`, `CrewTransaction`, with the drawing stubbed).
8. **Wallets, transfers and cargo trading.**
   - Resource units per player, as each ship's own (`ResUnits`).
   - Giving units to another player anywhere; giving minerals and bio data to a player in the same system, up to their free storage.
   - Reliable messages, refused with a reason.
   - Check: tests of our own; saves.
9. **The group inventory.** The device fields (`*_DEVICE` and the others that are things the group carries) are listed as such in `data/game-state.json`'s notes. The Manifest's Devices list shows them, as `devices.c` lists them. Using them is 3c.
   - Check: the list's order and names against `devices.c`, recorded.
10. **Client views.**
    - The communication screen: the alien's window with its animations and colour maps, the subtitles in the race's font and colours, the replies, the summary.
    - What a watching player sees during a story pause.
    - The starbase menu, outfitting and the shipyard's crew screen.
    - The wallet and trading screens.
    - The lander report.

    Check: what UQM draws, recorded as in 2b's item 10. The communication screen's animations run on time, so the recording program steps a clock of its own. Plus screenshots.
11. **Docs.** `docs/how-to-try/stage-3a.md`; update the design, porting inventory, differences and data docs.

Rough size: items 3, 7 and 10 are the large ones (the engine and its recording program; `starbas.c` is 1,961 lines and outfitting 795; the communication screen's drawing), then 4 and 5. Items 1, 2, 6, 8 and 9 are small to medium.

## Decisions

Recommendations first.

1. **Splitting stage 3.**
   - **Three parts, 3a, 3b and 3c, as above (recommended).** 3a can be played through, from the opening to the allied starbase, without ships in space or battles.
   - One stage 3 as listed: a single plan of about twice 2b's size before anything can be played.
2. **When combat comes.**
   - **Stage 4 (combat) after 3a, then 3b and 3c (recommended).** Almost every race's conversation can end in a battle (`Segue_hostile`), and meeting a race means its ships flying at you. With combat built first, each conversation ported in 3b can be played through as UQM plays it.
   - 3a, 3b, 3c, then combat, as the build order lists them. Each hostile ending would need a stand-in until stage 4, and the races could be talked to but not fought.
3. **The Ilwrath ship in the opening,** before combat exists.
   - **It hails and talks, and the battle counts as won (recommended).** The opening goes on as after a victory, the commander's "glad you won" line included. This is a temporary row in `docs/differences.md` until stage 4 puts the real fight in.
   - Leave the Ilwrath out: the state is set as if it had been beaten. Quicker, but the opening skips a scene.
   - Keep the opening short of the alliance until stage 4. The starbase would stay unavailable through 3a.
4. **Whose ship a conversation reads and changes,** where UQM's scripts reach the flagship (fuel and a lander from the commander, the minerals sold, the names spoken).
   - **The ship of the player who started the talk (recommended):** they reply, so it's their names and their hold. In a story talk the others watch, and their ships are untouched.
   - The host's flagship always: the story is the flagship's, but a guest's minerals couldn't be sold.
5. **Which conversations are story** (an open item in `docs/design.md`).
   - **For 3a, by what they do (recommended):** the commander's talks before the alliance and the ones `VisitStarBase` starts on arrival (with the Lunar base aboard, and later the bomb) are story, and so are the Ilwrath's and the two weeks passing. Talking to the allied commander from the starbase menu is ordinary, under the lock. Each conversation's rule goes in `data/conversations.json`, so 3b and 3c add theirs the same way.
   - Every conversation is story: simple, but each trip to sell minerals would stop the whole group.
6. **During a story pause.**
   - **Everything stops, and replies pass on if the starter leaves (recommended).**
     - Everything stops: flight, landers, menus and the clock.
     - Every client shows the conversation.
     - If the starter leaves, the replies pass to the next player in join order, the host first. The story can't then be left half-told.
     - A time skip shows its fade to everyone.
   - Everything stops, and the talk ends if the starter leaves. Simpler, but a dropped connection could cut off a story branch with its state half-set.
7. **The text's pace,** with no voices yet.
   - **UQM's pace without voices (recommended):** each page shows for 80 milliseconds a character. The replying player can skip ahead and back as UQM allows, and everyone watching follows them.
   - Each player paces their own copy: watchers could fall behind the replies.
8. **A second player at a locked conversation.**
   - **Told who's talking; the ship stays touching the starbase until it's free (recommended),** as touching a world that starts a conversation does now (row 27).
   - Watching the other player's talk: closer to a story talk, but it would show someone else's sale.
9. **Escorts at the starbase** (the escort module set is still an open item).
   - **Fuel, crew, landers and storage bays for now (recommended).** The other modules wait for the escort module set, so the flagship keeps its unique upgrades (design: "Escort upgrades"). A setting, as the escort's kit is.
   - Decide the escort module set before 3a's item 7.
10. **How conversations are checked.**
    - **Recorded from UQM's own scripts, every path (recommended),** as every stage so far: the recording program runs each script with the engine stubbed and walks its replies, and the port must match line for line, reply for reply, and write for write.
    - Port and check them by tests of our own only. Quicker, but nothing would show where a branch differs from UQM.

## Risks

- **Recording the scripts.** They call into the engine for more than lines and replies: animation and colour calls, the flagship, the ship queues, names. The stubs record each call. A script that waits on the engine (`AlienTalkSegue`) is recorded as the call, and the lines around it keep their order. If a script needs more of UQM than stubs can give, item 3 says so and records what it can.
- **Walking every path.** Branches depend on the story state and the ship as well as on the replies, so a path walk is only as good as its starting states. Each script's walk starts from states chosen to reach each condition it reads, and the plan's checks list them.
- **The communication screen runs on time,** where the lander ran on frames. The recording program steps a clock of its own, as the lander's did for its waits.
- **Size:** 3a ports two scripts and part of a third, the engine and the screen. 3b's two dozen scripts reuse them, so 3a's cost is mostly the engine and the screen.
