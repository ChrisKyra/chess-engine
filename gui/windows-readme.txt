Chess -- play against Bitboard Engine 13
========================================

Getting started
---------------
1. Unzip this folder anywhere (for example to your Desktop). Keep ChessGui.exe
   and engine.exe together in the same folder.
2. Double-click ChessGui.exe. Nothing needs to be installed.

   The first time, Windows may say "Windows protected your PC", because the
   program is not signed by a registered publisher. Click "More info", then
   "Run anyway".

3. The engine loads by itself as Engine 1. You play White and the engine plays
   Black: press Start and make your move by dragging a piece or clicking it and
   then its square.

Changing the game
-----------------
- Game tab: choose who plays White and Black (Human, Engine 1 or Engine 2),
  then press Start. Swap sides exchanges colours, Undo takes a move back, Flip
  turns the board.
- Engines tab: the engine's options. Threads uses more CPU cores (stronger);
  Hash is its memory in MB. Load... adds a second engine (for example
  Stockfish) as Engine 2.
- Match tab: let two engines play a series of games against each other.

The engine thinks for about half a second per move. Settings are saved in
%APPDATA%\ChessGui.

Requirements: Windows 10 or 11, 64-bit, on an Intel or AMD processor from 2010
or later.
