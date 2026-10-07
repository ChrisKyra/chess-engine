namespace ChessCore;

/// <summary>
/// Balanced, well-known openings used to start match games, so two deterministic
/// engines don't replay the same game over and over.
/// </summary>
/// <remarks>
/// The first 38 lines are the original list and stay first, so a short match plays the
/// same openings it always did. The other 255 are main lines and established sidelines,
/// grouped by opening and mostly 14 to 16 half-moves deep. Every line ends with White to
/// move, none ends in check or halfway through an exchange, and no two reach the same
/// position (the tests check the first three). A few candidates that left one side
/// clearly better were dropped. The engines themselves know none of these: only the GUI
/// uses them, to set up the starting position of each match pair.
/// </remarks>
public static class Openings
{
    public static IReadOnlyList<(string Name, string Moves)> All { get; } =
    [
        ("Ruy Lopez", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6"),
        ("Italian Game", "e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 c2c3 g8f6"),
        ("Two Knights Defence", "e2e4 e7e5 g1f3 b8c6 f1c4 g8f6"),
        ("Scotch Game", "e2e4 e7e5 g1f3 b8c6 d2d4 e5d4"),
        ("Four Knights Game", "e2e4 e7e5 g1f3 b8c6 b1c3 g8f6"),
        ("Petrov Defence", "e2e4 e7e5 g1f3 g8f6 f3e5 d7d6"),
        ("Philidor Defence", "e2e4 e7e5 g1f3 d7d6 d2d4 g8f6"),
        ("Vienna Game", "e2e4 e7e5 b1c3 g8f6 f2f4 d7d5"),
        ("Sicilian Najdorf", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6"),
        ("Sicilian Dragon", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 g7g6"),
        ("Sicilian Classical", "e2e4 c7c5 g1f3 b8c6 d2d4 c5d4 f3d4 g8f6"),
        ("Sicilian Taimanov", "e2e4 c7c5 g1f3 e7e6 d2d4 c5d4 f3d4 b8c6"),
        ("Sicilian Alapin", "e2e4 c7c5 c2c3 g8f6 e4e5 f6d5"),
        ("French Winawer", "e2e4 e7e6 d2d4 d7d5 b1c3 f8b4"),
        ("French Advance", "e2e4 e7e6 d2d4 d7d5 e4e5 c7c5"),
        ("Caro-Kann Classical", "e2e4 c7c6 d2d4 d7d5 b1c3 d5e4 c3e4 c8f5"),
        ("Scandinavian Defence", "e2e4 d7d5 e4d5 d8d5 b1c3 d5a5"),
        ("Pirc Defence", "e2e4 d7d6 d2d4 g8f6 b1c3 g7g6"),
        ("Modern Defence", "e2e4 g7g6 d2d4 f8g7 b1c3 d7d6"),
        ("Alekhine Defence", "e2e4 g8f6 e4e5 f6d5 d2d4 d7d6"),
        ("Queen's Gambit Declined", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6"),
        ("Queen's Gambit Accepted", "d2d4 d7d5 c2c4 d5c4 g1f3 g8f6"),
        ("Slav Defence", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6"),
        ("Semi-Slav Defence", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 e7e6"),
        ("King's Indian Defence", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6"),
        ("Nimzo-Indian Defence", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4"),
        ("Queen's Indian Defence", "d2d4 g8f6 c2c4 e7e6 g1f3 b7b6"),
        ("Grünfeld Defence", "d2d4 g8f6 c2c4 g7g6 b1c3 d7d5"),
        ("Catalan Opening", "d2d4 g8f6 c2c4 e7e6 g2g3 d7d5 f1g2 f8e7"),
        ("Benoni Defence", "d2d4 g8f6 c2c4 c7c5 d4d5 e7e6"),
        ("Dutch Defence", "d2d4 f7f5 g2g3 g8f6 f1g2 e7e6"),
        ("London System", "d2d4 d7d5 c1f4 g8f6 e2e3 c7c5"),
        ("Colle System", "d2d4 d7d5 g1f3 g8f6 e2e3 e7e6"),
        ("Trompowsky Attack", "d2d4 g8f6 c1g5 e7e6 e2e4 h7h6"),
        ("English Opening", "c2c4 e7e5 b1c3 g8f6 g2g3 d7d5"),
        ("Symmetrical English", "c2c4 c7c5 g1f3 g8f6 b1c3 b8c6"),
        ("Réti Opening", "g1f3 d7d5 g2g3 g8f6 f1g2 e7e6"),
        ("Bird Opening", "f2f4 d7d5 g1f3 g8f6 e2e3 g7g6"),

        // 1.e4 e5: Ruy Lopez
        ("Ruy Lopez Closed", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 f1e1 b7b5 a4b3 d7d6 c2c3 e8g8"),
        ("Ruy Lopez Breyer", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 f1e1 b7b5 a4b3 d7d6 c2c3 e8g8 h2h3 c6b8"),
        ("Ruy Lopez Chigorin", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 f1e1 b7b5 a4b3 d7d6 c2c3 e8g8 h2h3 c6a5"),
        ("Ruy Lopez Zaitsev", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 f1e1 b7b5 a4b3 d7d6 c2c3 e8g8 h2h3 c8b7"),
        ("Ruy Lopez Marshall Attack", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 f1e1 b7b5 a4b3 e8g8 c2c3 d7d5"),
        ("Ruy Lopez Anti-Marshall", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 f1e1 b7b5 a4b3 e8g8 a2a4 b5b4"),
        ("Ruy Lopez Berlin Defence", "e2e4 e7e5 g1f3 b8c6 f1b5 g8f6 e1g1 f6e4 d2d4 e4d6 b5c6 d7c6 d4e5 d6f5"),
        ("Ruy Lopez Berlin d3", "e2e4 e7e5 g1f3 b8c6 f1b5 g8f6 d2d3 f8c5 c2c3 e8g8 e1g1 d7d6"),
        ("Ruy Lopez Open", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f6e4 d2d4 b7b5 a4b3 d7d5 d4e5 c8e6"),
        ("Ruy Lopez Exchange", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5c6 d7c6 e1g1 f7f6 d2d4 e5d4 f3d4 c6c5"),
        ("Ruy Lopez Steinitz Deferred", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 d7d6 c2c3 c8d7 d2d4 g8f6"),
        ("Ruy Lopez Arkhangelsk", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 b7b5 a4b3 c8b7 f1e1 f8c5 c2c3 d7d6"),
        ("Ruy Lopez Moller", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8c5 c2c3 b7b5 a4c2 d7d5"),
        ("Ruy Lopez Classical", "e2e4 e7e5 g1f3 b8c6 f1b5 f8c5 c2c3 g8f6 e1g1 e8g8 d2d4 c5b6"),
        ("Ruy Lopez Cozio", "e2e4 e7e5 g1f3 b8c6 f1b5 g8e7 e1g1 g7g6 c2c3 f8g7 d2d4 e5d4 c3d4 d7d5"),
        ("Ruy Lopez Worrall", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 d1e2 b7b5 a4b3 e8g8 c2c3 d7d5"),
        ("Ruy Lopez Delayed Exchange", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 a4c6 d7c6 d2d3 f6d7 b1d2 e8g8"),
        ("Ruy Lopez d3 System", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 d2d3 b7b5 a4b3 f8e7 e1g1 e8g8 a2a4 c8b7"),

        // 1.e4 e5: Italian, Scotch, others
        ("Giuoco Pianissimo", "e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 d2d3 g8f6 e1g1 d7d6 c2c3 e8g8 f1e1 a7a6"),
        ("Italian a4 System", "e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 c2c3 g8f6 d2d3 d7d6 e1g1 a7a6 a2a4 e8g8 f1e1 c5a7"),
        ("Italian Main Line d4", "e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 c2c3 g8f6 d2d4 e5d4 c3d4 c5b4 c1d2 b4d2 b1d2 d7d5"),
        ("Italian Bb6 Line", "e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 c2c3 g8f6 d2d3 e8g8 e1g1 d7d5 e4d5 f6d5 f1e1 c5b6"),
        ("Hungarian Defence", "e2e4 e7e5 g1f3 b8c6 f1c4 f8e7 d2d4 d7d6 b1c3 g8f6 h2h3 e8g8 e1g1 e5d4 f3d4 f8e8"),
        ("Two Knights d3", "e2e4 e7e5 g1f3 b8c6 f1c4 g8f6 d2d3 f8e7 e1g1 e8g8 f1e1 d7d6 c2c3 c6a5"),
        ("Two Knights Ng5 Main Line", "e2e4 e7e5 g1f3 b8c6 f1c4 g8f6 f3g5 d7d5 e4d5 c6a5 c4b5 c7c6 d5c6 b7c6 b5e2 h7h6"),
        ("Two Knights Modern Attack", "e2e4 e7e5 g1f3 b8c6 f1c4 g8f6 d2d4 e5d4 e1g1 f6e4 f1e1 d7d5 c4d5 d8d5 b1c3 d5a5"),
        ("Scotch Classical", "e2e4 e7e5 g1f3 b8c6 d2d4 e5d4 f3d4 f8c5 c1e3 d8f6 c2c3 g8e7 f1c4 e8g8"),
        ("Scotch Mieses", "e2e4 e7e5 g1f3 b8c6 d2d4 e5d4 f3d4 g8f6 d4c6 b7c6 e4e5 d8e7 d1e2 f6d5 c2c4 c8a6"),
        ("Scotch Four Knights", "e2e4 e7e5 g1f3 b8c6 b1c3 g8f6 d2d4 e5d4 f3d4 f8b4 d4c6 b7c6 f1d3 d7d5 e4d5 c6d5"),
        ("Scotch Gambit", "e2e4 e7e5 g1f3 b8c6 d2d4 e5d4 f1c4 g8f6 e4e5 d7d5 c4b5 f6e4 f3d4 c8d7"),
        ("Four Knights Spanish", "e2e4 e7e5 g1f3 b8c6 b1c3 g8f6 f1b5 f8b4 e1g1 e8g8 d2d3 d7d6 c1g5 b4c3 b2c3 d8e7"),
        ("Four Knights Rubinstein", "e2e4 e7e5 g1f3 b8c6 b1c3 g8f6 f1b5 c6d4 b5a4 f8c5 f3e5 e8g8 e5d3 c5b6"),
        ("Four Knights Italian", "e2e4 e7e5 g1f3 b8c6 b1c3 g8f6 f1c4 f8c5 d2d3 d7d6 e1g1 e8g8 a2a3 a7a6"),
        ("Three Knights Glek", "e2e4 e7e5 g1f3 b8c6 b1c3 g8f6 g2g3 f8c5 f1g2 d7d6 d2d3 e8g8 e1g1 a7a6"),
        ("Petrov Classical", "e2e4 e7e5 g1f3 g8f6 f3e5 d7d6 e5f3 f6e4 d2d4 d6d5 f1d3 f8e7 e1g1 b8c6 c2c4 c6b4"),
        ("Petrov Nc3 Line", "e2e4 e7e5 g1f3 g8f6 f3e5 d7d6 e5f3 f6e4 b1c3 e4c3 d2c3 f8e7 c1e3 b8c6 d1d2 c8e6"),
        ("Petrov Three Knights", "e2e4 e7e5 g1f3 g8f6 b1c3 b8c6 d2d4 e5d4 f3d4 f8b4 d4c6 b7c6 f1d3 d7d5"),
        ("Petrov Steinitz Attack", "e2e4 e7e5 g1f3 g8f6 d2d4 f6e4 f1d3 d7d5 f3e5 b8d7 e5d7 c8d7 e1g1 f8d6"),
        ("Philidor Hanham", "e2e4 d7d6 d2d4 g8f6 b1c3 e7e5 g1f3 b8d7 f1c4 f8e7 e1g1 e8g8 f1e1 c7c6 a2a4 b7b6"),
        ("Vienna Falkbeer", "e2e4 e7e5 b1c3 g8f6 f1c4 b8c6 d2d3 c6a5 g1e2 a5c4 d3c4 f8c5"),
        ("Vienna Three Knights", "e2e4 e7e5 b1c3 b8c6 g1f3 g8f6 g2g3 d7d5 e4d5 f6d5 f1g2 d5c3 b2c3 f8d6"),
        ("Bishop's Opening", "e2e4 e7e5 f1c4 g8f6 d2d3 c7c6 g1f3 d7d5 c4b3 f8d6 b1c3 e8g8"),
        ("King's Gambit Accepted", "e2e4 e7e5 f2f4 e5f4 g1f3 d7d5 e4d5 g8f6 f1b5 c7c6 d5c6 b8c6 d2d4 f8d6"),
        ("King's Gambit Declined", "e2e4 e7e5 f2f4 f8c5 g1f3 d7d6 c2c3 g8f6 f4e5 d6e5 d2d4 e5d4 c3d4 c5b4 c1d2 b4d2 b1d2 e8g8"),
        ("Center Game", "e2e4 e7e5 d2d4 e5d4 d1d4 b8c6 d4e3 g8f6 b1c3 f8b4 c1d2 e8g8 e1c1 f8e8"),
        ("Ponziani Opening", "e2e4 e7e5 g1f3 b8c6 c2c3 g8f6 d2d4 f6e4 d4d5 c6e7 f3e5 e7g6"),
        ("Danish Gambit Declined", "e2e4 e7e5 d2d4 e5d4 c2c3 d7d5 e4d5 d8d5 c3d4 b8c6 g1f3 c8g4"),

        // Sicilian
        ("Sicilian Najdorf English Attack", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6 c1e3 e7e5 d4b3 c8e6 f2f3 f8e7"),
        ("Sicilian Najdorf Bg5", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6 c1g5 e7e6 f2f4 f8e7 d1f3 d8c7"),
        ("Sicilian Najdorf Be2", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6 f1e2 e7e5 d4b3 f8e7 e1g1 e8g8"),
        ("Sicilian Najdorf h3", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6 h2h3 e7e5 d4e2 h7h5 g2g3 c8e6"),
        ("Sicilian Najdorf Bc4", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6 f1c4 e7e6 c4b3 b7b5 e1g1 f8e7"),
        ("Sicilian Najdorf f4", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6 f2f4 e7e5 d4f3 b8d7 a2a4 f8e7"),
        ("Sicilian Dragon Yugoslav Attack", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 g7g6 c1e3 f8g7 f2f3 e8g8 d1d2 b8c6"),
        ("Sicilian Dragon Classical", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 g7g6 f1e2 f8g7 e1g1 e8g8 c1e3 b8c6"),
        ("Sicilian Accelerated Dragon", "e2e4 c7c5 g1f3 b8c6 d2d4 c5d4 f3d4 g7g6 b1c3 f8g7 c1e3 g8f6 f1c4 e8g8 c4b3 d7d6"),
        ("Sicilian Maroczy Bind", "e2e4 c7c5 g1f3 b8c6 d2d4 c5d4 f3d4 g7g6 c2c4 g8f6 b1c3 d7d6 f1e2 c6d4 d1d4 f8g7"),
        ("Sicilian Scheveningen", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 e7e6 f1e2 f8e7 e1g1 e8g8 f2f4 b8c6"),
        ("Sicilian Keres Attack", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 e7e6 g2g4 h7h6 h2h4 b8c6 h1g1 h6h5"),
        ("Sicilian Sveshnikov", "e2e4 c7c5 g1f3 b8c6 d2d4 c5d4 f3d4 g8f6 b1c3 e7e5 d4b5 d7d6 c1g5 a7a6 b5a3 b7b5"),
        ("Sicilian Kalashnikov", "e2e4 c7c5 g1f3 b8c6 d2d4 c5d4 f3d4 e7e5 d4b5 d7d6 b1c3 a7a6 b5a3 b7b5"),
        ("Sicilian Rauzer", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 b8c6 c1g5 e7e6 d1d2 a7a6 e1c1 c8d7"),
        ("Sicilian Sozin", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 b8c6 f1c4 e7e6 c1e3 f8e7 d1e2 a7a6"),
        ("Sicilian Classical Be2", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 b8c6 f1e2 e7e5 d4b3 f8e7 e1g1 e8g8"),
        ("Sicilian Taimanov English Attack", "e2e4 c7c5 g1f3 e7e6 d2d4 c5d4 f3d4 b8c6 b1c3 d8c7 c1e3 a7a6 d1d2 g8f6 e1c1 f8b4"),
        ("Sicilian Kan", "e2e4 c7c5 g1f3 e7e6 d2d4 c5d4 f3d4 a7a6 f1d3 g8f6 e1g1 d8c7 d1e2 d7d6 c2c4 g7g6"),
        ("Sicilian Four Knights", "e2e4 c7c5 g1f3 e7e6 d2d4 c5d4 f3d4 g8f6 b1c3 b8c6 d4c6 b7c6 e4e5 f6d5 c3e4 d8c7"),
        ("Sicilian Paulsen Hedgehog", "e2e4 c7c5 g1f3 e7e6 d2d4 c5d4 f3d4 a7a6 c2c4 g8f6 b1c3 d8c7 a2a3 b7b6 f1e2 c8b7"),
        ("Sicilian Rossolimo", "e2e4 c7c5 g1f3 b8c6 f1b5 g7g6 e1g1 f8g7 f1e1 e7e5 b5c6 d7c6 d2d3 d8e7"),
        ("Sicilian Rossolimo e6", "e2e4 c7c5 g1f3 b8c6 f1b5 e7e6 e1g1 g8e7 f1e1 a7a6 b5f1 d7d5 e4d5 e7d5"),
        ("Sicilian Moscow", "e2e4 c7c5 g1f3 d7d6 f1b5 c8d7 b5d7 d8d7 e1g1 b8c6 c2c3 g8f6 f1e1 e7e6 d2d4 c5d4 c3d4 d6d5"),
        ("Sicilian Moscow Nd7", "e2e4 c7c5 g1f3 d7d6 f1b5 b8d7 d2d4 g8f6 b1c3 c5d4 d1d4 e7e5 d4d3 h7h6"),
        ("Sicilian Alapin d5", "e2e4 c7c5 c2c3 d7d5 e4d5 d8d5 d2d4 g8f6 g1f3 e7e6 f1e2 b8c6 e1g1 c5d4 c3d4 f8e7"),
        ("Sicilian Alapin Nf6 Main", "e2e4 c7c5 c2c3 g8f6 e4e5 f6d5 d2d4 c5d4 g1f3 e7e6 c3d4 b7b6 b1c3 d5c3 b2c3 d8c7"),
        ("Sicilian Closed", "e2e4 c7c5 b1c3 b8c6 g2g3 g7g6 f1g2 f8g7 d2d3 d7d6 c1e3 e7e5 d1d2 g8e7"),
        ("Sicilian Grand Prix", "e2e4 c7c5 b1c3 b8c6 f2f4 g7g6 g1f3 f8g7 f1c4 e7e6 f4f5 g8e7 f5e6 d7e6 d2d3 e8g8"),
        ("Sicilian Smith-Morra Declined", "e2e4 c7c5 d2d4 c5d4 c2c3 g8f6 e4e5 f6d5 g1f3 b8c6 c3d4 d7d6 f1c4 d5b6"),
        ("Sicilian O'Kelly", "e2e4 c7c5 g1f3 a7a6 c2c3 d7d5 e4d5 d8d5 d2d4 g8f6 f1e2 e7e6 e1g1 b8c6"),
        ("Sicilian Nimzowitsch", "e2e4 c7c5 g1f3 g8f6 e4e5 f6d5 b1c3 e7e6 c3d5 e6d5 d2d4 b8c6 d4c5 f8c5 d1d5 d8b6"),
        ("Sicilian Bb5 Kan Hybrid", "e2e4 c7c5 g1f3 e7e6 c2c3 d7d5 e4d5 e6d5 d2d4 b8c6 f1b5 f8d6 d4c5 d6c5 e1g1 g8e7"),
        ("Sicilian Hyperaccelerated Fianchetto", "e2e4 c7c5 g1f3 g7g6 c2c3 f8g7 d2d4 c5d4 c3d4 d7d5 e4e5 b8c6 b1c3 c8g4 f1e2 e7e6"),
        ("Sicilian Bowdler Attack", "e2e4 c7c5 f1c4 e7e6 b1c3 a7a6 a2a4 b8c6 d2d3 g7g6 g1f3 f8g7 e1g1 g8e7"),
        ("Sicilian Kopec System", "e2e4 c7c5 g1f3 d7d6 c2c3 g8f6 f1d3 b8c6 d3c2 c8g4 d2d3 e7e5 h2h3 g4h5"),

        // French
        ("French Winawer Main Line", "e2e4 e7e6 d2d4 d7d5 b1c3 f8b4 e4e5 c7c5 a2a3 b4c3 b2c3 g8e7 d1g4 e8g8 f1d3 b8c6"),
        ("French Winawer Positional", "e2e4 e7e6 d2d4 d7d5 b1c3 f8b4 e4e5 c7c5 a2a3 b4c3 b2c3 g8e7 g1f3 d8a5 c1d2 b8c6"),
        ("French Classical Steinitz", "e2e4 e7e6 d2d4 d7d5 b1c3 g8f6 e4e5 f6d7 f2f4 c7c5 g1f3 b8c6 c1e3 c5d4 f3d4 f8c5"),
        ("French Classical Bg5", "e2e4 e7e6 d2d4 d7d5 b1c3 g8f6 c1g5 f8e7 e4e5 f6d7 g5e7 d8e7 f2f4 e8g8 g1f3 c7c5"),
        ("French MacCutcheon", "e2e4 e7e6 d2d4 d7d5 b1c3 g8f6 c1g5 f8b4 e4e5 h7h6 g5d2 b4c3 b2c3 f6e4 d1g4 g7g6"),
        ("French Rubinstein", "e2e4 e7e6 d2d4 d7d5 b1c3 d5e4 c3e4 b8d7 g1f3 g8f6 e4f6 d7f6 c1e3 f6d5 e3d2 c7c5"),
        ("French Tarrasch Open", "e2e4 e7e6 d2d4 d7d5 b1d2 c7c5 e4d5 e6d5 g1f3 b8c6 f1b5 f8d6 d4c5 d6c5 e1g1 g8e7"),
        ("French Tarrasch Closed", "e2e4 e7e6 d2d4 d7d5 b1d2 g8f6 e4e5 f6d7 f1d3 c7c5 c2c3 b8c6 g1e2 c5d4 c3d4 f7f6"),
        ("French Tarrasch Guimard", "e2e4 e7e6 d2d4 d7d5 b1d2 b8c6 g1f3 g8f6 e4e5 f6d7 f1b5 a7a6 b5a4 f7f6"),
        ("French Tarrasch Be7", "e2e4 e7e6 d2d4 d7d5 b1d2 f8e7 g1f3 g8f6 f1d3 c7c5 e4e5 f6d7 c2c3 b8c6 e1g1 g7g5"),
        ("French Advance Main", "e2e4 e7e6 d2d4 d7d5 e4e5 c7c5 c2c3 b8c6 g1f3 d8b6 a2a3 c5c4 b1d2 c6a5 f1e2 c8d7"),
        ("French Advance Bd7", "e2e4 e7e6 d2d4 d7d5 e4e5 c7c5 c2c3 b8c6 g1f3 c8d7 f1e2 g8e7 b1a3 c5d4 c3d4 e7f5"),
        ("French Exchange", "e2e4 e7e6 d2d4 d7d5 e4d5 e6d5 g1f3 g8f6 f1d3 f8d6 e1g1 e8g8 c1g5 c8g4"),
        ("French King's Indian Attack", "e2e4 e7e6 d2d3 d7d5 b1d2 g8f6 g1f3 c7c5 g2g3 b8c6 f1g2 f8e7 e1g1 e8g8"),
        ("French Burn", "e2e4 e7e6 d2d4 d7d5 b1c3 g8f6 c1g5 d5e4 c3e4 f8e7 g5f6 e7f6 g1f3 e8g8 d1d2 b8d7"),

        // Caro-Kann
        ("Caro-Kann Advance Short", "e2e4 c7c6 d2d4 d7d5 e4e5 c8f5 g1f3 e7e6 f1e2 c6c5 c1e3 b8d7 e1g1 g8e7"),
        ("Caro-Kann Advance Nc3", "e2e4 c7c6 d2d4 d7d5 e4e5 c8f5 b1c3 e7e6 g2g4 f5g6 g1e2 c6c5 h2h4 h7h5"),
        ("Caro-Kann Advance c5", "e2e4 c7c6 d2d4 d7d5 e4e5 c6c5 d4c5 e7e6 c1e3 b8d7 g1f3 f8c5 e3c5 d7c5"),
        ("Caro-Kann Classical Main", "e2e4 c7c6 d2d4 d7d5 b1c3 d5e4 c3e4 c8f5 e4g3 f5g6 h2h4 h7h6 g1f3 b8d7 h4h5 g6h7"),
        ("Caro-Kann Karpov", "e2e4 c7c6 d2d4 d7d5 b1c3 d5e4 c3e4 b8d7 g1f3 g8f6 e4f6 d7f6 f3e5 c8e6 f1e2 g7g6"),
        ("Caro-Kann Karpov Ng5", "e2e4 c7c6 d2d4 d7d5 b1c3 d5e4 c3e4 b8d7 f1c4 g8f6 e4g5 e7e6 d1e2 d7b6 c4d3 h7h6"),
        ("Caro-Kann Bronstein-Larsen", "e2e4 c7c6 d2d4 d7d5 b1c3 d5e4 c3e4 g8f6 e4f6 g7f6 c2c3 c8f5 g1f3 e7e6"),
        ("Caro-Kann Panov", "e2e4 c7c6 d2d4 d7d5 e4d5 c6d5 c2c4 g8f6 b1c3 e7e6 g1f3 f8e7 c4d5 f6d5 f1d3 b8c6"),
        ("Caro-Kann Exchange", "e2e4 c7c6 d2d4 d7d5 e4d5 c6d5 f1d3 b8c6 c2c3 g8f6 c1f4 c8g4 d1b3 d8d7"),
        ("Caro-Kann Two Knights", "e2e4 c7c6 b1c3 d7d5 g1f3 c8g4 h2h3 g4f3 d1f3 g8f6 d2d3 e7e6 f1e2 b8d7 e1g1 f8d6"),
        ("Caro-Kann KIA", "e2e4 c7c6 d2d3 d7d5 b1d2 e7e5 g1f3 f8d6 g2g3 g8f6 f1g2 e8g8 e1g1 f8e8"),

        // Pirc, Modern, Alekhine, Scandinavian, others
        ("Pirc Classical", "e2e4 d7d6 d2d4 g8f6 b1c3 g7g6 g1f3 f8g7 f1e2 e8g8 e1g1 c7c6 a2a4 b8d7"),
        ("Pirc Austrian", "e2e4 d7d6 d2d4 g8f6 b1c3 g7g6 f2f4 f8g7 g1f3 e8g8 f1d3 b8a6 e1g1 c7c5"),
        ("Pirc 150 Attack", "e2e4 d7d6 d2d4 g8f6 b1c3 g7g6 c1e3 c7c6 d1d2 f8g7 e3h6 g7h6 d2h6 d8a5"),
        ("Pirc Byrne", "e2e4 d7d6 d2d4 g8f6 b1c3 g7g6 c1g5 f8g7 d1d2 h7h6 g5h4 c7c6 f2f4 d8b6"),
        ("Modern Defence Main", "e2e4 g7g6 d2d4 f8g7 b1c3 d7d6 c1e3 a7a6 d1d2 b8d7 f2f4 b7b5"),
        ("Modern Averbakh", "e2e4 g7g6 d2d4 f8g7 c2c4 d7d6 b1c3 b8c6 c1e3 e7e5 d4d5 c6e7"),
        ("Modern Gurgenidze", "e2e4 g7g6 d2d4 f8g7 b1c3 c7c6 f2f4 d7d5 e4e5 h7h5 g1f3 c8g4"),
        ("Alekhine Modern", "e2e4 g8f6 e4e5 f6d5 d2d4 d7d6 g1f3 c8g4 f1e2 e7e6 e1g1 f8e7 c2c4 d5b6 h2h3 g4h5"),
        ("Alekhine Four Pawns Declined", "e2e4 g8f6 e4e5 f6d5 d2d4 d7d6 c2c4 d5b6 e5d6 e7d6 b1c3 f8e7 f1e2 e8g8 g1f3 c8g4"),
        ("Alekhine Exchange", "e2e4 g8f6 e4e5 f6d5 d2d4 d7d6 c2c4 d5b6 e5d6 c7d6 b1c3 g7g6 c1e3 f8g7 a1c1 e8g8"),
        ("Alekhine Kengis", "e2e4 g8f6 e4e5 f6d5 d2d4 d7d6 g1f3 d6e5 f3e5 c7c6 f1e2 c8f5 e1g1 b8d7"),
        ("Scandinavian Qd6", "e2e4 d7d5 e4d5 d8d5 b1c3 d5d6 d2d4 g8f6 g1f3 a7a6 f1e2 b8c6 e1g1 c8f5"),
        ("Scandinavian Qa5 Main", "e2e4 d7d5 e4d5 d8d5 b1c3 d5a5 d2d4 g8f6 g1f3 c7c6 f1c4 c8f5 c1d2 e7e6 c3d5 a5d8"),
        ("Scandinavian Qd8", "e2e4 d7d5 e4d5 d8d5 b1c3 d5d8 d2d4 g8f6 g1f3 c7c6 f1c4 c8f5 d1e2 e7e6"),
        ("Scandinavian Modern", "e2e4 d7d5 e4d5 g8f6 d2d4 f6d5 g1f3 g7g6 f1e2 f8g7 e1g1 e8g8 c2c4 d5b6"),
        ("Owen Defence", "e2e4 b7b6 d2d4 c8b7 f1d3 e7e6 g1f3 c7c5 c2c3 g8f6 d1e2 f8e7 e1g1 e8g8"),
        ("Nimzowitsch Defence", "e2e4 b8c6 g1f3 d7d6 d2d4 g8f6 b1c3 c8g4 c1e3 e7e6 h2h3 g4h5 f1b5 a7a6"),
        ("St. George Defence", "e2e4 a7a6 d2d4 b7b5 g1f3 c8b7 f1d3 e7e6 e1g1 c7c5 c2c3 g8f6 d1e2 b8c6"),

        // Queen's Gambit Declined
        ("QGD Orthodox", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c1g5 f8e7 e2e3 e8g8 g1f3 b8d7 a1c1 c7c6 f1d3 d5c4 d3c4 f6d5"),
        ("QGD Tartakower", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c1g5 f8e7 e2e3 e8g8 g1f3 h7h6 g5h4 b7b6 c4d5 f6d5 h4e7 d8e7"),
        ("QGD Lasker", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c1g5 f8e7 e2e3 e8g8 g1f3 h7h6 g5h4 f6e4 h4e7 d8e7"),
        ("QGD Exchange", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c4d5 e6d5 c1g5 c7c6 d1c2 f8e7 e2e3 b8d7 f1d3 e8g8"),
        ("QGD Exchange Nge2", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c4d5 e6d5 c1g5 f8e7 e2e3 e8g8 f1d3 b8d7 g1e2 f8e8"),
        ("QGD Bf4", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 g1f3 f8e7 c1f4 e8g8 e2e3 c7c5 d4c5 e7c5 d1c2 b8c6"),
        ("QGD Ragozin", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 g1f3 f8b4 c1g5 h7h6 g5f6 d8f6 e2e3 e8g8 a1c1 d5c4 f1c4 c7c5"),
        ("QGD Westphalia", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c1g5 b8d7 e2e3 f8e7 g1f3 e8g8 d1c2 c7c5"),
        ("QGD Semi-Tarrasch", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 g1f3 c7c5 c4d5 f6d5 e2e3 b8c6 f1d3 f8e7 e1g1 e8g8"),
        ("QGD Tarrasch", "d2d4 d7d5 c2c4 e7e6 b1c3 c7c5 c4d5 e6d5 g1f3 b8c6 g2g3 g8f6 f1g2 f8e7 e1g1 e8g8"),
        ("QGD Cambridge Springs", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c1g5 b8d7 e2e3 c7c6 g1f3 d8a5 f3d2 f8b4 d1c2 e8g8"),
        ("QGD Harrwitz", "d2d4 d7d5 c2c4 e7e6 g1f3 g8f6 b1c3 f8e7 c1f4 e8g8 e2e3 b8d7 c4c5 c7c6 f1d3 b7b6"),
        ("QGD Alatortsev", "d2d4 d7d5 c2c4 e7e6 b1c3 f8e7 c4d5 e6d5 c1f4 c7c6 e2e3 c8f5 g2g4 f5e6"),
        ("Chigorin Defence", "d2d4 d7d5 c2c4 b8c6 g1f3 c8g4 c4d5 g4f3 g2f3 d8d5 e2e3 e7e5 b1c3 f8b4"),
        ("Albin Countergambit Declined", "d2d4 d7d5 c2c4 e7e5 d4e5 d5d4 g1f3 b8c6 g2g3 c8e6 b1d2 d8d7 f1g2 e8c8"),
        ("Baltic Defence", "d2d4 d7d5 c2c4 c8f5 b1c3 e7e6 d1b3 b8c6 c1d2 a8b8 g1f3 g8f6"),

        // QGA and Slav
        ("QGA Classical", "d2d4 d7d5 c2c4 d5c4 g1f3 g8f6 e2e3 e7e6 f1c4 c7c5 e1g1 a7a6 d4c5 d8d1 f1d1 f8c5"),
        ("QGA e4 Line", "d2d4 d7d5 c2c4 d5c4 e2e4 e7e5 g1f3 e5d4 f1c4 f8b4 b1d2 b8c6 e1g1 g8f6"),
        ("QGA Nc3", "d2d4 d7d5 c2c4 d5c4 g1f3 g8f6 b1c3 a7a6 e2e4 b7b5 e4e5 f6d5 a2a4 d5c3 b2c3 d8d5"),
        ("QGA Bg4", "d2d4 d7d5 c2c4 d5c4 g1f3 g8f6 e2e3 c8g4 f1c4 e7e6 h2h3 g4h5 b1c3 b8d7 e1g1 f8d6"),
        ("Slav Main Line", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 d5c4 a2a4 c8f5 e2e3 e7e6 f1c4 f8b4 e1g1 e8g8"),
        ("Slav Dutch Variation", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 d5c4 a2a4 c8f5 f3e5 e7e6 f2f3 c6c5 e2e4 f5g6"),
        ("Slav Chebanenko", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 a7a6 e2e3 b7b5 b2b3 c8g4 f1e2 b8d7 e1g1 e7e6"),
        ("Slav Exchange", "d2d4 d7d5 c2c4 c7c6 c4d5 c6d5 b1c3 g8f6 c1f4 b8c6 e2e3 c8f5 g1f3 e7e6 f1b5 f6d7"),
        ("Slav Schlechter", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 e2e3 g7g6 b1c3 f8g7 f1e2 e8g8 e1g1 c8g4"),
        ("Slav e3 Bf5", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 e2e3 c8f5 b1c3 e7e6 f3h4 f5g6 h4g6 h7g6 f1d3 b8d7"),
        ("Slav Quiet Qc2", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 d1c2 d5c4 c2c4 c8f5 g2g3 e7e6 f1g2 b8d7 e1g1 f8e7"),
        ("Semi-Slav Meran", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 e7e6 e2e3 b8d7 f1d3 d5c4 d3c4 b7b5 c4d3 c8b7"),
        ("Semi-Slav Anti-Meran", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 e7e6 e2e3 b8d7 d1c2 f8d6 f1d3 e8g8 e1g1 d5c4 d3c4 b7b5 c4d3 c8b7"),
        ("Semi-Slav Moscow", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 e7e6 c1g5 h7h6 g5f6 d8f6 e2e3 b8d7 f1d3 d5c4 d3c4 g7g6 e1g1 f8g7"),
        ("Semi-Slav Botvinnik", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 e7e6 c1g5 d5c4 e2e4 b7b5 e4e5 h7h6 g5h4 g7g5"),
        ("Semi-Slav Stoltz", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 e7e6 e2e3 b8d7 d1c2 f8d6 b2b3 e8g8 f1e2 b7b6"),
        ("Semi-Slav Noteboom", "d2d4 d7d5 c2c4 c7c6 g1f3 e7e6 b1c3 d5c4 a2a4 f8b4 e2e3 b7b5 c1d2 a7a5"),
        ("Semi-Slav Marshall Gambit Declined", "d2d4 d7d5 c2c4 c7c6 b1c3 e7e6 g1f3 g8f6 g2g3 d5c4 f1g2 b7b5 f3e5 c8b7 e1g1 b8d7"),

        // Indian defences
        ("Nimzo-Indian Rubinstein", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 e2e3 e8g8 f1d3 d7d5 g1f3 c7c5 e1g1 b8c6 a2a3 b4c3 b2c3 d5c4 d3c4 d8c7"),
        ("Nimzo-Indian Classical", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 d1c2 e8g8 a2a3 b4c3 c2c3 d7d5 g1f3 d5c4 c3c4 b7b6"),
        ("Nimzo-Indian Classical d5", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 d1c2 d7d5 c4d5 e6d5 c1g5 h7h6 g5h4 c7c5 d4c5 g7g5"),
        ("Nimzo-Indian Kasparov", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 g1f3 c7c5 g2g3 c5d4 f3d4 e8g8 f1g2 d7d5 c4d5 f6d5 e1g1 b8c6"),
        ("Nimzo-Indian Samisch", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 a2a3 b4c3 b2c3 c7c5 f2f3 d7d5 c4d5 f6d5 d4c5 f7f5"),
        ("Nimzo-Indian Leningrad", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 c1g5 h7h6 g5h4 c7c5 d4d5 d7d6 e2e3 b4c3 b2c3 e6e5"),
        ("Nimzo-Indian Hubner", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 e2e3 c7c5 f1d3 b8c6 g1f3 b4c3 b2c3 d7d6 e3e4 e6e5"),
        ("Nimzo-Indian Fischer", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 e2e3 b7b6 g1e2 c8a6 a2a3 b4e7 e2g3 e8g8 e3e4 d7d5"),
        ("Nimzo-Indian Romanishin", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 g1f3 b7b6 c1g5 c8b7 e2e3 h7h6 g5h4 g7g5 h4g3 f6e4"),
        ("Queen's Indian Petrosian", "d2d4 g8f6 c2c4 e7e6 g1f3 b7b6 a2a3 c8b7 b1c3 d7d5 c4d5 f6d5 d1c2 d5c3 b2c3 f8e7"),
        ("Queen's Indian Fianchetto", "d2d4 g8f6 c2c4 e7e6 g1f3 b7b6 g2g3 c8a6 b2b3 f8b4 c1d2 b4e7 f1g2 c7c6 d2c3 d7d5"),
        ("Queen's Indian Bb7", "d2d4 g8f6 c2c4 e7e6 g1f3 b7b6 g2g3 c8b7 f1g2 f8e7 e1g1 e8g8 b1c3 f6e4 c1d2 e7f6"),
        ("Queen's Indian e3", "d2d4 g8f6 c2c4 e7e6 g1f3 b7b6 e2e3 c8b7 f1d3 d7d5 e1g1 f8d6 b1c3 e8g8 b2b3 b8d7"),
        ("Bogo-Indian", "d2d4 g8f6 c2c4 e7e6 g1f3 f8b4 c1d2 d8e7 g2g3 b8c6 b1c3 b4c3 d2c3 f6e4 a1c1 e8g8"),
        ("Bogo-Indian Bxd2", "d2d4 g8f6 c2c4 e7e6 g1f3 f8b4 c1d2 b4d2 d1d2 d7d5 g2g3 e8g8 f1g2 b8d7 e1g1 c7c6"),
        ("Catalan Open", "d2d4 g8f6 c2c4 e7e6 g2g3 d7d5 f1g2 d5c4 g1f3 f8e7 e1g1 e8g8 d1c2 a7a6 c2c4 b7b5"),
        ("Catalan Closed", "d2d4 g8f6 c2c4 e7e6 g2g3 d7d5 f1g2 f8e7 g1f3 e8g8 e1g1 c7c6 d1c2 b8d7 b1d2 b7b6"),
        ("Catalan Bb4+", "d2d4 g8f6 c2c4 e7e6 g2g3 d7d5 f1g2 f8b4 c1d2 b4e7 g1f3 e8g8 e1g1 c7c6 d1c2 b7b6"),
        ("Catalan Nc6", "d2d4 g8f6 c2c4 e7e6 g2g3 d7d5 g1f3 d5c4 f1g2 b8c6 d1a4 f8b4 c1d2 f6d5 d2b4 d5b4 e1g1 a8b8"),
        ("King's Indian Classical", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 g1f3 e8g8 f1e2 e7e5 e1g1 b8c6 d4d5 c6e7"),
        ("King's Indian Bayonet", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 g1f3 e8g8 f1e2 e7e5 e1g1 b8c6 d4d5 c6e7 b2b4 f6h5"),
        ("King's Indian Petrosian", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 g1f3 e8g8 f1e2 e7e5 d4d5 a7a5 c1g5 h7h6 g5h4 b8a6"),
        ("King's Indian Gligoric", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 g1f3 e8g8 f1e2 e7e5 c1e3 f6g4 e3g5 f7f6 g5h4 b8c6"),
        ("King's Indian Exchange", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 g1f3 e8g8 f1e2 e7e5 d4e5 d6e5 d1d8 f8d8 c1g5 d8e8"),
        ("King's Indian Samisch", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 f2f3 e8g8 c1e3 e7e5 d4d5 c7c6 d1d2 c6d5 c4d5 a7a6"),
        ("King's Indian Samisch c5", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 f2f3 e8g8 c1e3 c7c5 g1e2 b8c6 d4d5 c6e5"),
        ("King's Indian Four Pawns", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 f2f4 e8g8 g1f3 c7c5 d4d5 e7e6 f1e2 e6d5 c4d5 c8g4"),
        ("King's Indian Averbakh", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 f1e2 e8g8 c1g5 c7c5 d4d5 h7h6 g5e3 e7e6"),
        ("King's Indian Makogonov", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 h2h3 e8g8 g1f3 e7e5 d4d5 a7a5 c1g5 b8a6"),
        ("King's Indian Fianchetto", "d2d4 g8f6 c2c4 g7g6 g1f3 f8g7 g2g3 e8g8 f1g2 d7d6 e1g1 b8d7 b1c3 e7e5 e2e4 c7c6"),
        ("King's Indian Panno", "d2d4 g8f6 c2c4 g7g6 g1f3 f8g7 g2g3 e8g8 f1g2 d7d6 e1g1 b8c6 b1c3 a7a6 d4d5 c6a5"),
        ("King's Indian Yugoslav", "d2d4 g8f6 c2c4 g7g6 g1f3 f8g7 g2g3 e8g8 f1g2 d7d6 e1g1 c7c5 b1c3 b8c6 d4d5 c6a5"),
        ("Grünfeld Exchange", "d2d4 g8f6 c2c4 g7g6 b1c3 d7d5 c4d5 f6d5 e2e4 d5c3 b2c3 f8g7 g1f3 c7c5 a1b1 e8g8"),
        ("Grünfeld Classical Exchange", "d2d4 g8f6 c2c4 g7g6 b1c3 d7d5 c4d5 f6d5 e2e4 d5c3 b2c3 f8g7 f1c4 c7c5 g1e2 b8c6 c1e3 e8g8"),
        ("Grünfeld Russian", "d2d4 g8f6 c2c4 g7g6 b1c3 d7d5 g1f3 f8g7 d1b3 d5c4 b3c4 e8g8 e2e4 a7a6 f1e2 b7b5"),
        ("Grünfeld Bf4", "d2d4 g8f6 c2c4 g7g6 b1c3 d7d5 c1f4 f8g7 e2e3 c7c5 d4c5 d8a5 a1c1 d5c4 f1c4 e8g8"),
        ("Grünfeld Fianchetto", "d2d4 g8f6 c2c4 g7g6 g2g3 d7d5 c4d5 f6d5 f1g2 d5b6 g1f3 f8g7 b1c3 b8c6 e2e3 e8g8"),
        ("Grünfeld Bg5", "d2d4 g8f6 c2c4 g7g6 b1c3 d7d5 c1g5 f6e4 g5h4 e4c3 b2c3 d5c4 e2e3 c8e6"),
        ("Grünfeld e3", "d2d4 g8f6 c2c4 g7g6 b1c3 d7d5 e2e3 f8g7 g1f3 e8g8 f1e2 c7c5 e1g1 c5d4 e3d4 b8c6"),
        ("Benoni Modern Main", "d2d4 g8f6 c2c4 c7c5 d4d5 e7e6 b1c3 e6d5 c4d5 d7d6 e2e4 g7g6 g1f3 f8g7 f1e2 e8g8 e1g1 f8e8"),
        ("Benoni Fianchetto", "d2d4 g8f6 c2c4 c7c5 d4d5 e7e6 b1c3 e6d5 c4d5 d7d6 g1f3 g7g6 g2g3 f8g7 f1g2 e8g8 e1g1 f8e8"),
        ("Czech Benoni", "d2d4 g8f6 c2c4 c7c5 d4d5 e7e5 b1c3 d7d6 e2e4 f8e7 g1f3 e8g8 f1e2 f6e8"),
        ("Old Benoni", "d2d4 c7c5 d4d5 e7e5 e2e4 d7d6 b1c3 f8e7 g1f3 c8g4 f1e2 g4f3 e2f3 e7g5"),
        ("Old Indian", "d2d4 g8f6 c2c4 d7d6 b1c3 b8d7 e2e4 e7e5 g1f3 c7c6 f1e2 f8e7 e1g1 e8g8"),
        ("Budapest Declined", "d2d4 g8f6 c2c4 e7e5 d4e5 f6g4 c1f4 b8c6 g1f3 f8b4 b1d2 d8e7 e2e3 g4e5 a2a3 b4d2 d1d2 d7d6"),
        ("Blumenfeld Declined", "d2d4 g8f6 c2c4 e7e6 g1f3 c7c5 d4d5 b7b5 c1g5 e6d5 c4d5 h7h6 g5f6 d8f6"),

        // Dutch and others after 1.d4
        ("Dutch Leningrad", "d2d4 f7f5 g2g3 g8f6 f1g2 g7g6 g1f3 f8g7 e1g1 e8g8 c2c4 d7d6 b1c3 d8e8"),
        ("Dutch Stonewall", "d2d4 f7f5 g2g3 g8f6 f1g2 e7e6 g1f3 d7d5 e1g1 f8d6 c2c4 c7c6 b2b3 d8e7"),
        ("Dutch Classical", "d2d4 f7f5 c2c4 g8f6 g2g3 e7e6 f1g2 f8e7 g1f3 e8g8 e1g1 d7d6 b1c3 d8e8"),
        ("Dutch Nc3", "d2d4 f7f5 b1c3 g8f6 c1g5 d7d5 e2e3 e7e6 f1d3 f8e7 g1f3 e8g8"),
        ("London Main", "d2d4 g8f6 g1f3 e7e6 c1f4 c7c5 e2e3 b8c6 c2c3 d7d5 b1d2 f8d6 f4g3 e8g8 f1d3 b7b6"),
        ("London vs King's Indian", "d2d4 g8f6 c1f4 g7g6 e2e3 f8g7 g1f3 e8g8 f1e2 d7d6 h2h3 c7c5 c2c3 d8b6 d1b3 b8c6"),
        ("Torre Attack", "d2d4 g8f6 g1f3 e7e6 c1g5 c7c5 e2e3 h7h6 g5h4 b7b6 b1d2 c8b7 c2c3 f8e7"),
        ("Torre vs King's Indian", "d2d4 g8f6 g1f3 g7g6 c1g5 f8g7 b1d2 e8g8 e2e4 d7d6 c2c3 h7h6 g5h4 c7c5"),
        ("Colle-Zukertort", "d2d4 d7d5 g1f3 g8f6 e2e3 e7e6 f1d3 c7c5 b2b3 b8c6 e1g1 f8d6 c1b2 e8g8 b1d2 d8e7"),
        ("Veresov Attack", "d2d4 g8f6 b1c3 d7d5 c1g5 b8d7 g1f3 h7h6 g5h4 e7e6 e2e3 f8e7"),
        ("Trompowsky Ne4", "d2d4 g8f6 c1g5 f6e4 g5f4 c7c5 f2f3 d8a5 c2c3 e4f6 d4d5 a5b6"),
        ("Trompowsky d5", "d2d4 g8f6 c1g5 d7d5 g5f6 e7f6 e2e3 c7c6 c2c4 d5c4 f1c4 f8d6"),
        ("Queen's Pawn Accelerated London", "d2d4 d7d5 c1f4 c7c5 e2e3 b8c6 g1f3 g8f6 c2c3 e7e6 b1d2 f8d6 f4g3 e8g8"),
        ("Stonewall Attack", "d2d4 d7d5 e2e3 g8f6 f1d3 c7c5 c2c3 b8c6 f2f4 c8g4 g1f3 e7e6"),
        ("Slav a6 e3", "d2d4 d7d5 c2c4 c7c6 b1c3 g8f6 e2e3 a7a6 g1f3 b7b5 b2b3 c8g4 f1e2 e7e6"),

        // English
        ("English Four Knights", "c2c4 e7e5 b1c3 g8f6 g1f3 b8c6 g2g3 d7d5 c4d5 f6d5 f1g2 d5b6 e1g1 f8e7 d2d3 e8g8"),
        ("English Four Knights e3", "c2c4 e7e5 b1c3 g8f6 g1f3 b8c6 e2e3 f8b4 d1c2 b4c3 c2c3 d8e7 a2a3 a7a5 d2d3 d7d5"),
        ("English Reversed Dragon", "c2c4 e7e5 b1c3 g8f6 g2g3 d7d5 c4d5 f6d5 f1g2 d5b6 g1f3 b8c6 e1g1 f8e7 a2a3 e8g8"),
        ("English Closed", "c2c4 e7e5 b1c3 b8c6 g2g3 g7g6 f1g2 f8g7 d2d3 d7d6 e2e4 g8e7 g1e2 e8g8 e1g1 f7f5"),
        ("English Botvinnik", "c2c4 e7e5 b1c3 b8c6 g2g3 g7g6 f1g2 f8g7 e2e4 d7d6 g1e2 g8e7 d2d3 e8g8 e1g1 c8e6"),
        ("English Bremen", "c2c4 e7e5 b1c3 g8f6 g2g3 f8b4 f1g2 e8g8 e2e4 b4c3 b2c3 c7c6 g1e2 d7d5"),
        ("English Symmetrical Hedgehog", "c2c4 c7c5 g1f3 g8f6 b1c3 e7e6 g2g3 b7b6 f1g2 c8b7 e1g1 f8e7 d2d4 c5d4 d1d4 d7d6"),
        ("English Symmetrical Main", "c2c4 c7c5 b1c3 b8c6 g2g3 g7g6 f1g2 f8g7 g1f3 e7e6 e1g1 g8e7 d2d3 e8g8 c1d2 d7d5"),
        ("English Symmetrical Four Knights", "c2c4 c7c5 g1f3 g8f6 b1c3 b8c6 g2g3 d7d5 c4d5 f6d5 f1g2 d5c7 e1g1 e7e5 d2d3 f8e7"),
        ("English Anglo-Indian", "c2c4 g8f6 b1c3 e7e6 g1f3 d7d5 d2d4 f8e7 c1f4 e8g8 e2e3 c7c5 d4c5 e7c5"),
        ("English Anglo-Indian g6", "c2c4 g8f6 b1c3 g7g6 g2g3 f8g7 f1g2 e8g8 e2e4 d7d6 g1e2 e7e5 e1g1 c7c6 d2d3 a7a6"),
        ("English Mikenas", "c2c4 g8f6 b1c3 e7e6 e2e4 d7d5 e4e5 d5d4 e5f6 d4c3 b2c3 d8f6 d2d4 c7c5 g1f3 h7h6"),
        ("English Anglo-Slav", "c2c4 c7c6 g1f3 d7d5 e2e3 g8f6 b1c3 e7e6 b2b3 b8d7 c1b2 f8d6 d2d4 e8g8 f1d3 d5c4 d3c4 c6c5"),
        ("English Agincourt", "c2c4 e7e6 g1f3 d7d5 g2g3 g8f6 f1g2 f8e7 e1g1 e8g8 b2b3 c7c5 c1b2 b8c6 e2e3 b7b6"),
        ("English Keres", "c2c4 e7e5 b1c3 g8f6 g2g3 c7c6 g1f3 e5e4 f3d4 d7d5 c4d5 d8b6 d4b3 c6d5 f1g2 a7a5"),
        ("English Great Snake", "c2c4 g7g6 b1c3 f8g7 g2g3 c7c5 f1g2 b8c6 e2e3 e7e6 g1e2 g8e7 d2d4 c5d4 e2d4 e8g8"),
        ("English Romanishin", "c2c4 g8f6 g1f3 e7e6 g2g3 a7a6 f1g2 b7b5 b2b3 c8b7 e1g1 c7c5 b1c3 b5b4 c3a4 d7d6"),

        // Reti, KIA, flank openings
        ("Réti Accepted", "g1f3 d7d5 c2c4 d5c4 e2e3 g8f6 f1c4 e7e6 e1g1 c7c5 d2d4 a7a6 b2b3 b8d7 c1b2 b7b5"),
        ("Réti Advance", "g1f3 d7d5 c2c4 d5d4 b2b4 g7g6 c1b2 f8g7 g2g3 e7e5 d2d3 a7a5 b4b5 c7c5"),
        ("Réti Slav Setup", "g1f3 d7d5 c2c4 c7c6 b2b3 g8f6 g2g3 c8f5 f1g2 e7e6 e1g1 b8d7 c1b2 h7h6 d2d3 f8e7"),
        ("Réti Double Fianchetto", "g1f3 g8f6 g2g3 g7g6 b2b3 f8g7 c1b2 e8g8 f1g2 d7d6 e1g1 e7e5 d2d3 b8c6 c2c4 f8e8"),
        ("King's Indian Attack", "g1f3 d7d5 g2g3 g8f6 f1g2 c7c6 e1g1 c8g4 d2d3 b8d7 b1d2 e7e5 e2e4 d5e4 d3e4 f8c5"),
        ("King's Indian Attack vs c5", "g1f3 c7c5 g2g3 b8c6 f1g2 g7g6 e1g1 f8g7 d2d3 d7d6 e2e4 e7e5 c2c3 g8e7 a2a3 e8g8"),
        ("English Symmetrical Nb4 Line", "g1f3 g8f6 c2c4 c7c5 b1c3 d7d5 c4d5 f6d5 e2e4 d5b4 f1c4 b4d3 e1e2 d3f4 e2f1 f4e6"),
        ("Larsen Opening", "b2b3 e7e5 c1b2 b8c6 e2e3 d7d5 f1b5 f8d6 f2f4 d8h4 g2g3 h4e7"),
        ("Larsen d5", "b2b3 d7d5 c1b2 g8f6 e2e3 e7e6 g1f3 c7c5 c2c4 b8c6 c4d5 e6d5 f1e2 f8e7"),
        ("Bird Leningrad", "f2f4 g8f6 g1f3 g7g6 g2g3 f8g7 f1g2 e8g8 e1g1 d7d6 d2d3 c7c5 d1e1 b8c6"),
        ("Bird Classical", "f2f4 d7d5 g1f3 g8f6 e2e3 c7c5 b2b3 b8c6 c1b2 c8g4 f1e2 e7e6 b1c3 f8e7"),
        ("Larsen c4 Line", "b2b3 e7e5 c1b2 b8c6 c2c4 g8f6 g1f3 e5e4 f3d4 f8c5 d4c6 d7c6 e2e3 c8f5"),
        ("Van Geet Opening", "b1c3 d7d5 e2e4 d5d4 c3e2 e7e5 e2g3 c8e6 c2c3 c7c5 g1f3 b8c6"),
        ("English vs Dutch", "c2c4 f7f5 b1c3 g8f6 g2g3 g7g6 f1g2 f8g7 d2d3 e8g8 e2e3 d7d6 g1e2 c7c6"),
        ("English Flohr-Mikenas d5", "c2c4 g8f6 b1c3 e7e6 e2e4 c7c5 e4e5 f6g8 g1f3 b8c6 d2d4 c5d4 f3d4 c6e5 d4b5 a7a6"),
        ("English Carls-Bremen", "c2c4 e7e5 b1c3 g8f6 g2g3 c7c6 d2d4 e5d4 d1d4 d7d5 g1f3 f8e7 c4d5 c6d5 f1g2 b8c6"),
    ];

    /// <summary>Resolves an opening's UCI moves from the start position.</summary>
    public static IReadOnlyList<Move> Parse(string uciMoves)
    {
        var position = Position.Start;
        var moves = new List<Move>();
        foreach (var token in uciMoves.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var move = position.FindUciMove(token) ?? throw new FormatException($"Illegal opening move '{token}' in \"{uciMoves}\".");
            moves.Add(move);
            position = position.Play(move);
        }
        return moves;
    }

    /// <summary>
    /// The opening for a pair of match games (both colours play the same one): the book
    /// line for this pair, followed by <paramref name="randomPlies"/> random legal moves.
    /// </summary>
    /// <remarks>
    /// With <paramref name="seed"/> 0 the pairs go through the list in order, from the
    /// first opening. Any other seed shuffles the order, a different shuffle for each
    /// seed, so a match with a fresh seed starts from different openings; every opening
    /// is still used once before any comes round again. The same seed always gives the
    /// same openings and random moves, so a match can be repeated.
    /// </remarks>
    public static (string Name, IReadOnlyList<Move> Moves) ForMatchGame(int pairIndex, int randomPlies, int seed = 0)
    {
        int round = pairIndex / All.Count;
        int slot = pairIndex % All.Count;
        var (name, uci) = All[seed == 0 ? slot : ShuffledOrder(unchecked(seed + round * 7919))[slot]];
        var moves = Parse(uci).ToList();
        var position = moves.Aggregate(Position.Start, (p, m) => p.Play(m));
        var random = new Random(seed == 0 ? pairIndex : unchecked(seed * 1_000_003 + pairIndex));

        for (int i = 0; i < randomPlies; i++)
        {
            // Never pick a move that ends the game on the spot.
            var candidates = position.LegalMoves.Where(m => position.Play(m).LegalMoves.Count > 0).ToList();
            if (candidates.Count == 0)
                break;
            var move = candidates[random.Next(candidates.Count)];
            moves.Add(move);
            position = position.Play(move);
        }

        return (randomPlies > 0 ? $"{name} + {randomPlies} random" : name, moves);
    }

    /// <summary>The indices of <see cref="All"/> in an order shuffled by <paramref name="seed"/> (Fisher-Yates).</summary>
    private static int[] ShuffledOrder(int seed)
    {
        var order = Enumerable.Range(0, All.Count).ToArray();
        var random = new Random(seed);
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }
}
