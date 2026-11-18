/*
 * Attack on Crateris: do-nothing stand-ins for the parts of UQM that
 * hyperspace.c links but doesn't need to run hyperspace: drawing (the
 * screen, the status panel, the radar, the transition), sound and music,
 * the clock's waiting, string tables, colour maps, and the game's other
 * screens (battle_stubs.c's, but for what hyperspace.c runs for real:
 * hyper.c's own, build.c's and master.c's).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * Compiled without UQM's headers, so each stand-in can take any
 * arguments; functions return 0.
 */
long Async_process () { return 0; }
long BatchGraphics () { return 0; }
long ClearDrawable () { return 0; }
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
long FlushSounds () { return 0; }
long font_DrawText () { return 0; }
long free_gravity_well () { return 0; }
long GetContextClipRect () { return 0; }
long GetInitialMeleeStarShips () { return 0; }
long GetStringLength () { return 0; }
long InitSISContexts () { return 0; }
long LoadMusicInstance () { return 0; }
long log_add () { return 0; }
long MeleeGameOver () { return 0; }
long MeleeShipDeath () { return 0; }
long PlayMusic () { return 0; }
long PlaySound () { return 0; }
long PLRPlaying () { return 0; }
long ProcessSound () { return 0; }
long ReleaseStringTable () { return 0; }
long RemoveSoundsForObject () { return 0; }
long ScreenTransition () { return 0; }
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
long SetTransitionSource () { return 0; }
long SleepThreadUntil () { return 0; }
long StopMusic () { return 0; }
long StopSound () { return 0; }
long TaskSwitch () { return 0; }
long TFB_DrawScreen_SetMipmap () { return 0; }
long UnbatchGraphics () { return 0; }
long UpdateSoundPositions () { return 0; }
long utf8StringCopy () { return 0; }
long ClearSISRect () { return 0; }
long DrawAutoPilotMessage () { return 0; }
long DrawHyperCoords () { return 0; }
long DrawPoint () { return 0; }
long DrawSISFrame () { return 0; }
long DrawSISMessage () { return 0; }
long GetColorMapAddress () { return 0; }
long RepairSISBorder () { return 0; }
long SetColorMap () { return 0; }
long SetFlashRect () { return 0; }
long WaitForNoInput () { return 0; }
long DrawStatusMessage () { return 0; }
long CaptureStringTable () { return 0; }
long LoadStringTableInstance () { return 0; }
long SleepThread () { return 0; }
long DrawMenuStateStrings () { return 0; }
long DrawSISTitle () { return 0; }
long DrawOval () { return 0; }
long FadeScreen () { return 0; }
long SetContextDrawMode () { return 0; }
long RepairBackRect () { return 0; }
long GetTimeCounter () { return 0; }
long FleetIsInfinite () { return 0; }
long UpdateShipFragCrew () { return 0; }
long DeltaSISGauges () { return 0; }
