/*
 * Attack on Crateris: do-nothing stand-ins for the parts of UQM that
 * battle.c links but doesn't need to run a battle: drawing (the screen,
 * the status panel, the transition), sound and music, the clock's
 * waiting, string tables, and the game's other screens and ship queues.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Compiled without UQM's headers, so each stand-in can take any
 * arguments; functions return 0 and the data are zeroed space. None of
 * them is reached in a battle but to draw, to play a sound or to wait, but
 * for PLRPlaying: with no music playing, readyForBattleEnd (tactrans.c)
 * is ready at once, so new_ship never waits for a victory ditty (a dead
 * ship's element goes after the frames cleanup_dead_ship gives it).
 */

long Async_process () { return 0; }
long BatchGraphics () { return 0; }
long Build () { return 0; }
long ClearDrawable () { return 0; }
long CloneShipFragment () { return 0; }
long CreateContextAux () { return 0; }
long CurrentInputToBattleInput () { return 0; }
long DestroyContext () { return 0; }
long DestroyDrawable () { return 0; }
long DestroyMusic () { return 0; }
long DestroyStringTable () { return 0; }
long DrawBatch () { return 0; }
long DrawFilledRectangle () { return 0; }
long DrawFilledStamp () { return 0; }
long DrawRectangle () { return 0; }
long DrawStamp () { return 0; }
long FindMasterShip () { return 0; }
long FlushSounds () { return 0; }
long font_DrawText () { return 0; }
long free_gravity_well () { return 0; }
long FreeHyperspace () { return 0; }
long GetContextClipRect () { return 0; }
long GetInitialMeleeStarShips () { return 0; }
long GetStringAddress () { return 0; }
long GetStringLength () { return 0; }
long hyper_transition () { return 0; }
long InitGroupInfo () { return 0; }
long InitPlanetInfo () { return 0; }
long InitSISContexts () { return 0; }
long LoadHyperspace () { return 0; }
long LoadMusicInstance () { return 0; }
long log_add () { return 0; }
long MeleeGameOver () { return 0; }
long MeleeShipDeath () { return 0; }
long MoveSIS () { return 0; }
long PlayMusic () { return 0; }
long PlaySound () { return 0; }
long PLRPlaying () { return 0; }
long ProcessSound () { return 0; }
long ReleaseStringTable () { return 0; }
long RemoveSoundsForObject () { return 0; }
long ScreenTransition () { return 0; }
long SetAbsStringTableIndex () { return 0; }
long SetContext () { return 0; }
long SetContextBackGroundColor () { return 0; }
long SetContextClipRect () { return 0; }
long SetContextFGFrame () { return 0; }
long SetContextFont () { return 0; }
long SetContextForeGroundColor () { return 0; }
long SetContextOrigin () { return 0; }
long SetGraphicScale () { return 0; }
long SetGraphicScaleMode () { return 0; }
long SetMenuSounds () { return 0; }
long SetRaceAllied () { return 0; }
long SetTransitionSource () { return 0; }
long SleepThreadUntil () { return 0; }
long StopMusic () { return 0; }
long StopSound () { return 0; }
long TaskSwitch () { return 0; }
long TFB_DrawScreen_SetMipmap () { return 0; }
long UnbatchGraphics () { return 0; }
long UninitGroupInfo () { return 0; }
long UninitPlanetInfo () { return 0; }
long UpdateSoundPositions () { return 0; }
long utf8StringCopy () { return 0; }

char CurStarDescPtr[64], FlagStatFrame[64], FontGradFrame[64], GameSounds[64],
		GameStrings[64], master_q[256], MiscDataFrame[64], StatusFrame[64];
