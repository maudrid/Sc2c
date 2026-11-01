/*
 * Attack on Crateris: do-nothing stand-ins for the parts of UQM that
 * systems.c links but does not need: graphics, sound, input, menus,
 * conversations, ship queues and saved planet state.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Compiled without UQM's headers, so each stand-in can take any
 * arguments. Functions return 0, except resource loaders, which return a
 * dummy handle so that loaded resources read as present.
 * StartSphereTracking returns 0: no alien ships are at any homeworld, so
 * no conversation starts and every world can be orbited and scanned.
 * isNodeRetrieved returns 0: nothing has been picked up.
 */

static char dummy_handle[64];

long AllocLink () { return 0; }
long BatchGraphics () { return 0; }
long BuildGroups () { return 0; }
void *CaptureDrawable () { return dummy_handle; }
void *CaptureStringTable () { return dummy_handle; }
long CargoMenu () { return 0; }
long ClearDrawable () { return 0; }
long CloneShipFragment () { return 0; }
long CountLinks () { return 0; }
void *CreateContextAux () { return dummy_handle; }
void *CreateDrawable () { return dummy_handle; }
void *DecFrameIndex () { return dummy_handle; }
long DeltaSISGauges () { return 0; }
long DeltaVelocityComponents () { return 0; }
long DestroyContext () { return 0; }
long DestroyDrawable () { return 0; }
long DestroyMusic () { return 0; }
long DestroyStringTable () { return 0; }
long DevicesMenu () { return 0; }
long DoDiscoveryReport () { return 0; }
long DoInput () { return 0; }
long DoMenuChooser () { return 0; }
long DoMissions () { return 0; }
long DrawAutoPilotMessage () { return 0; }
long DrawHyperCoords () { return 0; }
long DrawMenuStateStrings () { return 0; }
long DrawOval () { return 0; }
long DrawSISFrame () { return 0; }
long DrawSISMessage () { return 0; }
long DrawSISTitle () { return 0; }
long DrawStamp () { return 0; }
long DrawStatusMessage () { return 0; }
long DrawablesIntersect () { return 0; }
long EscortFeasibilityStudy () { return 0; }
long FadeScreen () { return 0; }
long FindMasterShip () { return 0; }
long FreeLanderFont () { return 0; }
long FreePlanet () { return 0; }
long GameClockTick () { return 0; }
long GameOptions () { return 0; }
long GetColorMapAddress () { return 0; }
long GetContextClipRect () { return 0; }
long GetCurrentVelocityComponents () { return 0; }
long GetFrameIndex () { return 0; }
long GetGroupInfo () { return 0; }
long GetPlanetInfo () { return 0; }
long GetStarShipFromIndex () { return 0; }
long GetStringTableCount () { return 0; }
long GetStringTableIndex () { return 0; }
long GetTimeCounter () { return 0; }
void *IncFrameIndex () { return dummy_handle; }
long InitCommunication () { return 0; }
long InitDisplayList () { return 0; }
long InitGroupInfo () { return 0; }
long InitPlanetInfo () { return 0; }
long InitQueue () { return 0; }
long InitSISContexts () { return 0; }
long InsertQueue () { return 0; }
long KillLanderCrewSeq () { return 0; }
void *LoadGraphicInstance () { return dummy_handle; }
long LoadLanderData () { return 0; }
void *LoadMusicInstance () { return dummy_handle; }
long LoadStdLanderFont () { return 0; }
void *LoadStringTableInstance () { return dummy_handle; }
long MoveGameClockDays () { return 0; }
long NotifyOthers () { return 0; }
long PlanetOrbitMenu () { return 0; }
long PlayMusic () { return 0; }
long PutGroupInfo () { return 0; }
long PutPlanetInfo () { return 0; }
long PutQueue () { return 0; }
long RedrawQueue () { return 0; }
long ReinitQueue () { return 0; }
long ReleaseDrawable () { return 0; }
long ReleaseStringTable () { return 0; }
long RemoveQueue () { return 0; }
long RepairSISBorder () { return 0; }
long RosterMenu () { return 0; }
long ScreenTransition () { return 0; }
long SetColorMap () { return 0; }
long SetContext () { return 0; }
long SetContextBackGroundColor () { return 0; }
long SetContextClipRect () { return 0; }
long SetContextFGFrame () { return 0; }
long SetContextForeGroundColor () { return 0; }
long SetFlashRect () { return 0; }
long SetLanderTakeoff () { return 0; }
long SetMenuSounds () { return 0; }
long SetRaceAllied () { return 0; }
void *SetRelFrameIndex () { return dummy_handle; }
long SetTransitionSource () { return 0; }
long SetVelocityComponents () { return 0; }
long SleepThreadUntil () { return 0; }
long StartSphereTracking () { return 0; }
long StopMusic () { return 0; }
long TFB_Random () { return 0; }
long UninitGroupInfo () { return 0; }
long UninitPlanetInfo () { return 0; }
long UninitQueue () { return 0; }
long UnbatchGraphics () { return 0; }
long countNodesRetrieved () { return 0; }
long free_ship () { return 0; }
long isNodeRetrieved () { return 0; }
long load_ship () { return 0; }
long setNodeRetrieved () { return 0; }
long log_add () { return 0; }

char CurrentInputState[65536];
char DisplayArray[65536];
char FlagStatFrame[65536];
char FontGradFrame[65536];
char LastActivity[65536];
char MenuSounds[65536];
char MiscDataFrame[65536];
char NextActivity[65536];
char OffScreenContext[65536];
char PLRPlaying[65536];
char PlayerControls[65536];
char PulsedInputState[65536];
char Screen[65536];
char SpaceContext[65536];
char StarMap[65536];
char master_q[65536];
char optWhichMenu[65536];
char stars_in_space[65536];
