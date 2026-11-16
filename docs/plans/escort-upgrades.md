# Small item: escorts' upgrades

**Status:** the owner's decision of 2026-11-13, go-ahead given 2026-11-16 for this item alone. Built (2026-11-16), awaiting review: see "Progress", and `docs/how-to-try/escort-upgrades.md` to try it.

## Goal

Escorts (guests' ships) may also buy crew pods, fuel tanks, fusion thrusters and turning jets at the starbase, each capped below the flagship by a host setting; the flagship then keeps only the high-efficiency fuel system and the bomb's parts (`docs/design.md`, "Escort upgrades"; `docs/differences.md` row 9). The caps start at the owner's placeholders, to be tuned in playtesting: thrusters 8, turning jets 6, fuel tanks 4, crew pods 2. Everything else follows the flagship's (UQM's) outfitting: how they're bought and sold, their prices and refunds, and what they do to the ship.

Before this, escorts bought fuel and crew within their kit, landers and storage bays (stage 3, decision 10) and the combat modules (stage 4, decision 3); their thrusters, jets, fuel tank and crew were their kit's, never bought or sold.

## How UQM does it

All of it is the flagship's, in `../uqm-0.8.0/sc2/src/uqm/`:

- **Buying** (`outfit.c`, `DoInstallModule`, line 176): a select on an empty slot opens the choice on the row's piece (the drives on the fusion thruster, the jets on the turning jets, the landers on the lander, the modules on the crew pod, then any module for sale); a select on the choice buys it if the wallet has `ModuleCost[piece] * MODULE_COST_SCALE`, else `MENU_SOUND_FAILURE`. The only limits are the slots (11 drive, 8 jet, 16 module: `sis.h`) and the wallet.
- **Selling** (same function, line 258): a select on a full slot sells it back at its price now, refused only for a crew pod the crew needs (`CrewEnlisted > CREW_POD_CAPACITY * (pods - 1)`), a fuel tank the fuel needs (`FuelOnBoard > the tanks left + FUEL_RESERVE`) and a storage bay the minerals need. Thrusters and jets always sell, the starting ones too.
- **What they hold** (`sis.c`): `GetCrewPodCapacity` (50 a pod, `CREW_POD_CAPACITY`), `GetFuelTankCapacity` (`FUEL_RESERVE` and 50 units a tank, `FUEL_TANK_CAPACITY`), `CountSISPieces`.
- **Crew** (`shipyard.c`, `DMS_HireFlagShipCrew`, line 717): hired into that room at the story's crew price, which rises with the crew bought (`CrewTransaction`).
- **What they do** (`ships/sis_ship/sis_ship.c`, `init_sis`, line 919): `InitDriveSlots` (line 878) and `InitJetSlots` (903) set the thrust and turning from the thrusters and jets, for battle, interplanetary flight and hyperspace alike; the most crew is the pods' (`InitModuleSlots`, 826).

## The readings

The decision leaves these open; each is the one closest to UQM's flagship rules and the decision:

1. **The kit's pieces count toward the caps.** A cap is the most of a module the escort holds, however it came by it: the default kit's 2 thrusters, 2 jets and 1 fuel tank count, so a new escort may buy 6 more thrusters, 4 jets, 3 tanks and 2 crew pods. UQM's slots don't tell a starting piece from a bought one, and neither do ours (`ShipModules`).
2. **The caps are host settings** (`GameSettings.EscortCaps`, `World/GameSettings.cs`), as the escort's kit is: each from 0 to the slots its module goes in (11, 8, 16, 16), anything else refused, from a new game or a save. They're saved with the settings; a save without them (every save so far) has the placeholders, so the save's version stays 12. An escort past a cap (the host lowered it) keeps what it has and may sell, but buys no more until it's under.
3. **At a cap, the choice still opens and the purchase is refused**, as one too dear is (`MENU_SOUND_FAILURE`, the client's "That can't be done."): the drive and jet rows' choices open on their piece as the flagship's do, and the modules' row offers the crew pod and the fuel tank among the rest. Only the high-efficiency fuel system is skipped for an escort, as before. Since every row's first piece is now one an escort may hold, an escort's choice opens where the flagship's does (before, it skipped to the first it could buy, or didn't open on a drive or jet slot).
4. **Selling is the flagship's**: every one sells back at its price now, the kit's thrusters, jets and fuel tank too (500 each at a new game's prices, as UQM's flagship sells its starting ones and as the escort's kit's gun, lander and storage bay already sell); a crew pod the crew needs and a fuel tank the fuel needs are refused, by UQM's own tests. A host who finds selling the kit too generous can give a smaller kit (`GameSettings.EscortLoadout`).
5. **A crew pod holds 50 crew** (`CREW_POD_CAPACITY`), beside the kit's own 10: an escort with the 2 pods holds 110. Crew is hired into the room at the group's crew price, as the flagship's (stage 3, decision 5). A pod is needed while the crew is more than the other pods and the kit hold.
6. **A fuel tank holds 50 units** (`FUEL_TANK_CAPACITY`), with the reserve of 10: the kit's one tank 60, four 210. A tank with fuel it needs isn't sold, as UQM refuses it.
7. **In flight and in battle the flagship's code** (as the combat modules already are): the hyperspace and interplanetary drive from the thrusters and jets (`ShipDrive.ForHyperspace`, `PlayerShip.Refit`), the battle's thrust and turning (`SisShip`, `InitDriveSlots` and `InitJetSlots`), the most crew in battle the pods' and the kit's and the captain (stage 4a's difference: the kit's crew counts beside the pods). An escort flies and fights as a flagship with the same slots would, with its kit's crew beside its pods.
8. **On the starbase's screens the crew fill the pods first**; the kit's crew have no seats on UQM's picture, so the crew past the pods aren't drawn (before, a point was drawn at the window's corner for a crew member hired or sent away with no pod). Only the totals matter to the rules, so the order is only the drawing's.

Nothing here needs the owner.

## Work

1. **The rules** (`Starbase/Outfitting.cs`): `EscortMayHold` adds the four modules; `CanHold` refuses an escort past its cap (`AtEscortCap`, `Held`); the caps come with the rules (`EscortCaps`, the host's from `GameWorld`). Selling needed nothing new: UQM's tests already counted the kit's crew.
2. **The setting** (`World/GameSettings.cs`): `EscortCaps`, checked by `GameWorld` and `HostSave`, a save without it or with it null given the defaults.
3. **The menu** (`Starbase/OutfitMenu.cs`): the escort's skip on opening a choice goes (every row's first piece is an escort's now).
4. **The screens** (`Presentation/StarbaseScreens.cs`): no crew point past the pods.
5. **The wire and saves:** nothing new. A ship's slots are already saved (since save version 9) and sent (since protocol version 11), and the client draws the outfit screen from the host's drawing (`StarbaseDrawing`), so a guest's screen shows what the host's rules allow.
6. **Tests** (topic `starbase`): `EscortUpgradeTests` (new); `OutfittingTests` and `StarbaseTests` changed where escorts were refused these modules.
7. **Docs:** `docs/how-to-try/escort-upgrades.md`, `differences.md` rows 9 and 46, `design.md`, `porting-inventory.md`, `handoff.md`.

## Progress

| Item | State | Where |
| --- | --- | --- |
| Escorts' upgrades | Built, awaiting review | **The rules:** `src/Crateris.Core/Starbase/Outfitting.cs` (`EscortMayHold` with crew pods, fuel tanks, thrusters and jets; `Held`, `AtEscortCap`, `CanHold` refusing past the caps; the caps given to the rules), `src/Crateris.Core/World/GameSettings.cs` (`EscortCaps`, the record of the four caps with `For` and `Check`), `GameWorld.cs` (the host's caps to the rules and the menu; checked on a new game), `HostSave.cs` (null caps the defaults, out of range refused: "Bad settings: escort caps: ..."). **The menu:** `Starbase/OutfitMenu.cs`, the escort's choice opening where the flagship's does. **The screens:** `src/Crateris.Presentation/StarbaseScreens.cs`, crew points only in pods. **Tests:** `tests/Crateris.Core.Tests/EscortUpgradeTests.cs` (the placeholders; buying each up to its cap, the kit's counted, at the flagship's prices, the flagship uncapped; the host's caps, a kit past them; selling as the flagship, a pod or tank still needed refused, the kit's sold; crew and fuel bought into the new room; in battle the flagship's code; a guest's upgrades flown, saved and sent; the caps saved, a save without them or null or partial given the defaults; caps out of range refused; the menu offering them and refusing past the caps; the crew drawn in pods); `OutfittingTests` (escorts refused only the high-efficiency fuel system and the bomb; the menu's offers and choices as the flagship's) and `StarbaseTests` (a guest's upgrades through the world, the host's caps through the keys and the save). **Docs:** as in "Work" |
