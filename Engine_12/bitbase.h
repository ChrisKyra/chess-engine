#pragma once

#include "types.h"

// King and pawn against king, solved exactly.  Every such position -- 196,608 of
// them once the pawn is mirrored onto files a to d -- is classified as a win or a
// draw at start-up by retrograde analysis, which takes a few milliseconds.  The
// evaluation then knows the true result of any KPK ending instead of guessing.
namespace Bitbase {

// Works the table out.  Call once at start-up, after Bitboards::init().
void init();

// Whether the side with the pawn wins.  `strong` is that side: its king and
// pawn, the lone king, and who is to move.
bool kpk_win(Color strong, Square strong_king, Square pawn, Square weak_king, Color side_to_move);

} // namespace Bitbase
