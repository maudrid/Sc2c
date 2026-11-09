/*
 * Attack on Crateris: the rest of UQM that outfit.c, shipyard.c, sis.c and
 * menu.c call, for the starbase screens' recording program (golden/screens.c).
 * They only draw, flash, load or wait, or serve the escort fleet's ships
 * and the input loop, none of which the recording drives: each does
 * nothing and gives 0. Declared without UQM's headers (each is called
 * through its own prototype; 0 is a null pointer, FALSE or 0 for all).
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 */

#define NOTHING(name) long name (void) { return 0; }

NOTHING (Alarm_addAbsoluteMs)
NOTHING (Alarm_remove)
NOTHING (AnyButtonPress)
NOTHING (CloneShipFragment)
NOTHING (CountLinks)
NOTHING (DestroyStringTable)
NOTHING (DoShipSpin)
NOTHING (FindMasterShipIndex)
NOTHING (Flash_nextTime)
NOTHING (Flash_postUpdate)
NOTHING (Flash_preUpdate)
NOTHING (Flash_process)
NOTHING (Flash_setFrameTime)
NOTHING (Flash_setMergeFactors)
NOTHING (Flash_setSpeed)
NOTHING (Flash_start)
NOTHING (FlushInput)
NOTHING (FreeLink)
NOTHING (GetClusterName)
NOTHING (GetEscortByStarShipIndex)
NOTHING (GetIndexFromStarShip)
NOTHING (GetStarShipFromIndex)
NOTHING (InsertQueue)
NOTHING (ReleaseStringTable)
NOTHING (RemoveQueue)
NOTHING (SetInputCallback)
NOTHING (SleepThreadUntil)
NOTHING (TextRect)
NOTHING (inHyperSpace)
NOTHING (log_add)
NOTHING (skipUTF8Chars)

int ScreenWidth = 320, ScreenHeight = 240;
void *CurStarDescPtr;
