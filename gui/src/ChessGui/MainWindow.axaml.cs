using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChessCore;
using ChessCore.Uci;

namespace ChessGui;

public sealed record MoveRow(string Number, string White, string Black, IBrush? WhiteBackground, IBrush? BlackBackground);

public sealed record MatchGameRow(string Number, string Description, string Winner);

public partial class MainWindow : Window
{
    private const int MaxLogLines = 3000;

    private static readonly IBrush LastMoveBackground = new SolidColorBrush(Color.Parse("#4D4A44"));
    private static readonly IBrush ErrorText = new SolidColorBrush(Color.Parse("#FF8A80"));
    private static readonly IBrush InfoText = new SolidColorBrush(Color.Parse("#B8B3AB"));
    private static readonly IBrush WhiteSwatch = new SolidColorBrush(Color.Parse("#F5F5F5"));
    private static readonly IBrush BlackSwatch = new SolidColorBrush(Color.Parse("#1A1A1A"));

    private readonly AppSettings settings;
    private readonly GameController controller;
    private readonly EngineCard[] engineCards;
    private readonly EvalRow[] evalRows;
    private readonly ObservableCollection<string> logLines = [];
    private readonly ConcurrentQueue<string> pendingLog = new();
    private bool updatingControls;
    private bool analysisDirty;
    private bool shutdownComplete;
    private (Game Game, int Plies)? shownMoves;
    private (int Slot, UciEngine? Engine)? optionsShownFor;
    private (MatchRun Match, int Games)? shownMatchGames;
    private (MatchRun? Match, int Games)? shownMatchEndings;

    public MainWindow()
    {
        InitializeComponent();

        engineCards =
        [
            new EngineCard(Engine1Name, Engine1Path, Engine1Args, Engine1Load, Engine1Reload, Engine1Unload, Engine1Analyse),
            new EngineCard(Engine2Name, Engine2Path, Engine2Args, Engine2Load, Engine2Reload, Engine2Unload, Engine2Analyse),
        ];
        evalRows =
        [
            new EvalRow(Eval1Row, Eval1Name, Eval1Role, Eval1Score, Eval1Depth, Eval1Nodes, Eval1Pv),
            new EvalRow(Eval2Row, Eval2Name, Eval2Role, Eval2Score, Eval2Depth, Eval2Nodes, Eval2Pv),
        ];

        settings = AppSettings.Load();
        controller = new GameController(settings);
        controller.StateChanged += Refresh;
        controller.AnalysisChanged += () => analysisDirty = true;
        controller.EngineLog += OnEngineLog;

        Board.MovePlayed += (_, move) => controller.HumanMove(move);
        Board.SizeChanged += (_, _) => AlignPlayerBars();
        LogList.ItemsSource = logLines;

        // Engine output can arrive hundreds of times a second; repaint it at a human pace.
        var uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        uiTimer.Tick += (_, _) => FlushEngineOutput();
        uiTimer.Start();

        LoadControlsFromSettings();
        AutoFlip();
        Refresh();

        Opened += async (_, _) =>
        {
            foreach (var slot in controller.Slots)
            {
                var saved = settings.Engines[slot.Index];
                var path = saved.Path is { } p && File.Exists(p) ? p
                    : slot.Index == 0 ? BundledEnginePath()
                    : null;
                if (path is not null)
                    await controller.LoadEngineAsync(slot, path, path == saved.Path ? saved.Arguments : null);
            }
        };
    }

    /// <summary>
    /// The engine shipped with the GUI: inside Chess.app (Contents/Resources, see
    /// build-mac-app.sh), or next to the executable, as engine.exe in the Windows zip
    /// (.github/workflows/windows.yml).
    /// </summary>
    private static string? BundledEnginePath()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "engine"),
            Path.Combine(AppContext.BaseDirectory, "engine"),
            Path.Combine(AppContext.BaseDirectory, "engine.exe"),
        ];
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    // ----------------------------------------------------------------------
    // Refreshing the view
    // ----------------------------------------------------------------------

    private void Refresh()
    {
        var game = controller.Game;
        var position = game.Position;
        var result = game.Result;

        Board.Position = position;
        Board.LastMove = game.LastMove;
        Board.CanMove = controller.CanHumanMove;

        StatusText.Text = result.IsOver ? result.Describe() : DescribeTurn();
        MessageText.Text = controller.Message;
        // Both lines have a fixed height and trim; the tooltip shows anything cut off.
        ToolTip.SetTip(StatusText, StatusText.Text);
        ToolTip.SetTip(MessageText, string.IsNullOrEmpty(controller.Message) ? null : controller.Message);
        MessageText.Foreground = controller.MessageIsError ? ErrorText : InfoText;

        foreach (var slot in controller.Slots)
            RefreshEngineCard(slot);
        RefreshPlayerChoiceNames();

        bool anyEngine = controller.Slots.Any(s => s.Engine is not null);
        bool started = controller.IsStarted;
        bool engineBusy = controller.Slots.Any(s => s.IsThinking || s.IsAnalysing || s.Engine is { IsBusy: true });
        StartButton.IsEnabled = !started && !controller.IsMatchRunning && !controller.Slots.Any(s => s.IsStarting);
        QuitButton.IsEnabled = started || controller.IsMatchRunning || engineBusy;
        UndoButton.IsEnabled = started && game.PlyCount > 0;
        EngineMoveButton.IsEnabled = started && anyEngine && !result.IsOver && !controller.IsMatchRunning;
        EngineMoveButton.Content = controller.IsEngineThinking ? "Move now" : "Engine move";
        PauseButton.IsEnabled = started && !result.IsOver && !controller.IsMatchRunning;
        PauseButton.Content = controller.IsPaused ? "Resume" : "Pause";

        if (!FenBox.IsKeyboardFocusWithin)
            FenBox.Text = position.ToFen();

        RefreshPlayerBars();
        RefreshMoves();
        RefreshAnalysis();
        RefreshOptionsPanel();
        RefreshMatch();
    }

    private string DescribeTurn()
    {
        var position = controller.Game.Position;
        var side = position.SideToMove == PieceColor.White ? "White" : "Black";
        var check = position.IsInCheck ? " — check" : "";

        if (!controller.IsStarted)
        {
            return controller.Game.PlyCount > 0
                ? "Game stopped — press Start for a new game"
                : $"Press Start to begin — {side} to move";
        }
        if (controller.Match is { IsRunning: true, IsPaused: true })
            return $"{side} to move{check} (match paused)";
        if (controller.ThinkingSlot is { } thinking)
            return $"{side} to move{check} — {thinking.Name} is thinking…";
        if (controller.IsPaused)
            return $"{side} to move{check} (engines paused)";
        if (controller.PlayerFor(position.SideToMove) != PlayerKind.Human && controller.SlotFor(position.SideToMove)?.Engine is null)
            return $"{side} to move{check} — {controller.SlotFor(position.SideToMove)!.Label} isn't loaded, you can move for it";
        return $"{side} to move{check}";
    }

    private void RefreshEngineCard(EngineSlot slot)
    {
        var card = engineCards[slot.Index];
        var saved = settings.Engines[slot.Index];
        card.Name.Text = slot.Engine?.Name ?? (slot.IsStarting ? "Starting…" : "Not loaded");
        card.Path.Text = slot.Engine?.ExecutablePath ?? saved.Path ?? "Load any UCI engine executable.";
        // The engine decides how it plays, so it can't be swapped mid-game.
        bool locked = controller.IsGameInProgress;
        card.Load.IsEnabled = !slot.IsStarting && !locked;
        card.Reload.IsEnabled = !slot.IsStarting && saved.Path is not null && !locked;
        card.Unload.IsEnabled = slot.Engine is not null && !locked;
        card.Args.IsEnabled = !locked;
    }

    /// <summary>Shows the loaded engines' names in the White/Black player lists.</summary>
    private void RefreshPlayerChoiceNames()
    {
        foreach (var box in new[] { WhitePlayerBox, BlackPlayerBox })
        {
            foreach (var slot in controller.Slots)
            {
                if (box.Items[slot.Index + 1] is not ComboBoxItem item)
                    continue;
                var text = slot.Engine is { } engine ? $"{slot.Label}: {engine.Name}" : slot.Label;
                if (!Equals(item.Content, text))
                    item.Content = text;
            }
        }
    }

    private void RefreshPlayerBars()
    {
        var top = Board.Flipped ? PieceColor.White : PieceColor.Black;
        var bottom = top.Opposite();

        TopName.Text = controller.PlayerName(top);
        BottomName.Text = controller.PlayerName(bottom);
        TopSwatch.Fill = top == PieceColor.White ? WhiteSwatch : BlackSwatch;
        BottomSwatch.Fill = bottom == PieceColor.White ? WhiteSwatch : BlackSwatch;

        bool thinking = controller.IsEngineThinking;
        var toMove = controller.Game.Position.SideToMove;
        TopThinking.Text = thinking && toMove == top ? "thinking…" : "";
        BottomThinking.Text = thinking && toMove == bottom ? "thinking…" : "";
        RefreshClocks();
        RefreshCaptured();
    }

    /// <summary>
    /// The panel left of the board during a match: next to each player's side, the
    /// pieces that player has captured and, for whoever is ahead in material, by how
    /// many pawns (1, 3, 3, 5, 9; promotions count as what the pawn became).
    /// </summary>
    private void RefreshCaptured()
    {
        var match = controller.Match;
        bool show = match is not null && ReferenceEquals(match.CurrentGame, controller.Game);
        CapturedPanel.IsVisible = show;
        if (!show)
            return;

        var game = controller.Game;
        var (byWhite, byBlack) = MaterialBalance.Captures(game);
        int balance = MaterialBalance.Balance(game.Position);
        var top = Board.Flipped ? PieceColor.White : PieceColor.Black;
        var bottom = top.Opposite();

        TopCaptured.Pieces = top == PieceColor.White ? byWhite : byBlack;
        BottomCaptured.Pieces = bottom == PieceColor.White ? byWhite : byBlack;
        TopCapturedHeading.Text = $"{(top == PieceColor.White ? "WHITE" : "BLACK")} CAPTURED";
        BottomCapturedHeading.Text = $"{(bottom == PieceColor.White ? "WHITE" : "BLACK")} CAPTURED";
        TopAdvantage.Text = Lead(top);
        BottomAdvantage.Text = Lead(bottom);

        // "+3" beside the side that is ahead; nothing beside the other one, or when level.
        string Lead(PieceColor color)
        {
            int lead = color == PieceColor.White ? balance : -balance;
            return lead > 0 ? $"+{lead.ToString(CultureInfo.InvariantCulture)}" : "";
        }
    }

    /// <summary>The clocks of the match game on the board, when it is played on a clock; empty otherwise.</summary>
    private void RefreshClocks()
    {
        var top = Board.Flipped ? PieceColor.White : PieceColor.Black;
        TopClock.Text = FormatClock(controller.MatchClockMs(top));
        BottomClock.Text = FormatClock(controller.MatchClockMs(top.Opposite()));

        static string FormatClock(long? ms)
        {
            if (ms is not { } left)
                return "";
            var inv = CultureInfo.InvariantCulture;
            var time = TimeSpan.FromMilliseconds(left);
            return left < 60_000
                ? (left / 1000.0).ToString("0.0", inv)
                : $"{(int)time.TotalMinutes}:{time.Seconds.ToString("00", inv)}";
        }
    }

    private void RefreshMoves()
    {
        var game = controller.Game;
        if (shownMoves is { } shown && ReferenceEquals(shown.Game, game) && shown.Plies == game.PlyCount)
            return;
        shownMoves = (game, game.PlyCount);

        var sans = game.SanMoves;
        IBrush? Highlight(int ply) => ply == sans.Count - 1 ? LastMoveBackground : null;

        var rows = new List<MoveRow>();
        int number = game.StartPosition.FullmoveNumber;
        int i = 0;
        if (game.StartPosition.SideToMove == PieceColor.Black && sans.Count > 0)
        {
            rows.Add(new MoveRow($"{number}.", "…", sans[0], null, Highlight(0)));
            i = 1;
            number++;
        }
        for (; i < sans.Count; i += 2, number++)
        {
            bool hasBlack = i + 1 < sans.Count;
            rows.Add(new MoveRow($"{number}.", sans[i], hasBlack ? sans[i + 1] : "", Highlight(i), hasBlack ? Highlight(i + 1) : null));
        }

        MovesList.ItemsSource = rows;
        Dispatcher.UIThread.Post(() => MovesScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>One evaluation row per loaded engine, plus the board arrow.</summary>
    private void RefreshAnalysis()
    {
        var current = controller.Game.Position;
        Move? arrow = null;
        bool arrowFromThinking = false;

        foreach (var slot in controller.Slots)
        {
            var row = evalRows[slot.Index];
            if (slot.Engine is not { } engine)
            {
                // Keep the row (and so the card's height) even when nothing is loaded.
                row.Name.Text = slot.Label;
                row.Role.Text = slot.IsStarting ? "starting…" : "not loaded";
                row.Score.Text = "—";
                row.Depth.Text = "Load it on the Engines tab";
                row.Nodes.Text = "";
                row.Pv.Text = "";
                ToolTip.SetTip(row.Pv, null);
                continue;
            }

            row.Name.Text = $"{engine.Name}  ·  {slot.Label}";
            row.Role.Text = $"{DescribePlays(slot)} · {(slot.IsThinking ? "thinking" : slot.IsAnalysing ? "analysing" : "idle")}";

            var info = slot.Info;
            var position = slot.InfoPosition;
            if (info is null || position is null)
            {
                row.Score.Text = "—";
                row.Depth.Text = slot.IsThinking || slot.IsAnalysing ? "Searching…"
                    : settings.Engines[slot.Index].Analyse ? "Waiting for its turn to analyse"
                    : "Turn on Analyse (Engines tab) to see its view";
                row.Nodes.Text = "";
                row.Pv.Text = "";
                continue;
            }

            bool live = ReferenceEquals(position, current);
            row.Score.Text = info.FormatScore(position.SideToMove) ?? "—";
            row.Depth.Text = (info.Depth is { } depth ? $"Depth {depth}" + (info.SelectiveDepth is { } sel ? $"/{sel}" : "") : "")
                + (live ? "" : "  ·  before the last move");
            var stats = new List<string>();
            if (info.Nodes is { } nodes) stats.Add($"{Count(nodes)} nodes");
            if (info.NodesPerSecond is { } nps) stats.Add($"{Count(nps)} nps");
            if (info.TimeMs is { } ms) stats.Add((ms / 1000.0).ToString("0.0s", CultureInfo.InvariantCulture));
            row.Nodes.Text = string.Join("  ·  ", stats);
            row.Pv.Text = info.Pv.Count > 0 ? San.FormatLine(position, info.Pv) : "";
            ToolTip.SetTip(row.Pv, string.IsNullOrEmpty(row.Pv.Text) ? null : row.Pv.Text);

            // Arrow: the engine choosing a move wins over a background analyser.
            if (live && info.Pv.Count > 0 && (slot.IsThinking || (slot.IsAnalysing && !arrowFromThinking && arrow is null)))
            {
                arrow = position.FindUciMove(info.Pv[0]);
                arrowFromThinking = slot.IsThinking;
            }
        }

        Board.HintMove = settings.ShowEngineArrow ? arrow : null;
    }

    private string DescribePlays(EngineSlot slot)
    {
        bool white = controller.SlotFor(PieceColor.White) == slot;
        bool black = controller.SlotFor(PieceColor.Black) == slot;
        return (white, black) switch
        {
            (true, true) => "plays both sides",
            (true, false) => "plays White",
            (false, true) => "plays Black",
            _ => "not playing",
        };
    }

    private static string Count(long value) => value switch
    {
        >= 1_000_000_000 => (value / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "B",
        >= 1_000_000 => (value / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "M",
        >= 10_000 => (value / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private void AlignPlayerBars()
    {
        TopBar.Width = BottomBar.Width = Math.Max(0, Board.BoardSize);
    }

    private void AutoFlip()
    {
        // Put the human's pieces at the bottom when playing against an engine.
        bool whiteHuman = settings.WhitePlayer == PlayerKind.Human;
        bool blackHuman = settings.BlackPlayer == PlayerKind.Human;
        if (whiteHuman != blackHuman)
            Board.Flipped = blackHuman;
    }

    // ----------------------------------------------------------------------
    // Engine output, log and options
    // ----------------------------------------------------------------------

    private void OnEngineLog(EngineSlot slot, EngineLogEntry entry)
    {
        var arrow = entry.Direction switch
        {
            EngineLogDirection.ToEngine => "»",
            EngineLogDirection.FromEngine => "«",
            _ => "!",
        };
        pendingLog.Enqueue($"{slot.Index + 1} {arrow} {entry.Text}");
    }

    private void FlushEngineOutput()
    {
        RefreshClocks();

        if (analysisDirty)
        {
            analysisDirty = false;
            RefreshAnalysis();
        }

        if (pendingLog.IsEmpty)
            return;
        while (pendingLog.TryDequeue(out var line))
            logLines.Add(line);
        if (logLines.Count > MaxLogLines + 500)
        {
            while (logLines.Count > MaxLogLines)
                logLines.RemoveAt(0);
        }
        if (LogExpander.IsExpanded && LogExpander.IsEffectivelyVisible)
            LogList.ScrollIntoView(logLines.Count - 1);
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(CommandBox.Text))
            return;
        controller.SendRawCommand(controller.Slots[Math.Max(0, CommandEngineBox.SelectedIndex)], CommandBox.Text.Trim());
        CommandBox.Text = "";
        e.Handled = true;
    }

    private void OnOptionsEngineChanged(object? sender, SelectionChangedEventArgs e) => RefreshOptionsPanel();

    private void RefreshOptionsPanel()
    {
        if (OptionsEngineBox is null || engineCards is null)
            return;
        var slot = controller.Slots[Math.Max(0, OptionsEngineBox.SelectedIndex)];
        if (optionsShownFor is { } shown && shown.Slot == slot.Index && shown.Engine == slot.Engine)
            return;
        optionsShownFor = (slot.Index, slot.Engine);
        BuildOptionsPanel(slot);
    }

    private void BuildOptionsPanel(EngineSlot slot)
    {
        OptionsPanel.Children.Clear();
        var engine = slot.Engine;

        if (engine is null || engine.Options.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = engine is null ? $"Load {slot.Label} to see its options." : "This engine has no options.",
            };
            hint.Classes.Add("muted");
            OptionsPanel.Children.Add(hint);
            return;
        }

        // The check boxes by option name, so the strength slider can tick UCI_LimitStrength.
        var checks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in engine.Options)
        {
            var value = controller.SavedOptionValue(slot, option) ?? option.Default ?? "";
            Control editor;

            switch (option.Type)
            {
                case UciOptionType.Check:
                    // A single "_" in a check box's text marks a keyboard shortcut and is
                    // hidden ("UCI_Elo" would show as "UCIElo"); doubling it shows it.
                    var check = new CheckBox { Content = option.Name.Replace("_", "__"), IsChecked = value.Equals("true", StringComparison.OrdinalIgnoreCase) };
                    check.IsCheckedChanged += (_, _) => _ = controller.SetEngineOptionAsync(slot, option, check.IsChecked == true ? "true" : "false");
                    checks[option.Name] = check;
                    OptionsPanel.Children.Add(check);
                    continue;

                case UciOptionType.Button:
                    var button = new Button { Content = option.Name };
                    button.Click += (_, _) => _ = controller.SetEngineOptionAsync(slot, option, null);
                    OptionsPanel.Children.Add(button);
                    continue;

                case UciOptionType.Spin when option.Name.Equals("UCI_Elo", StringComparison.OrdinalIgnoreCase)
                                              && option is { Min: not null, Max: not null }:
                    editor = BuildEloSlider(slot, engine, option, value, checks);
                    break;

                case UciOptionType.Spin:
                    var spin = new NumericUpDown
                    {
                        Minimum = option.Min ?? int.MinValue,
                        Maximum = option.Max ?? int.MaxValue,
                        Increment = 1,
                        FormatString = "0",
                        Value = decimal.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null,
                    };
                    spin.ValueChanged += (_, e) =>
                    {
                        if (e.NewValue is { } v)
                            _ = controller.SetEngineOptionAsync(slot, option, ((long)v).ToString(CultureInfo.InvariantCulture));
                    };
                    editor = spin;
                    break;

                case UciOptionType.Combo:
                    var combo = new ComboBox { ItemsSource = option.Choices, SelectedItem = option.Choices.FirstOrDefault(c => c == value) };
                    combo.SelectionChanged += (_, _) =>
                    {
                        if (combo.SelectedItem is string choice)
                            _ = controller.SetEngineOptionAsync(slot, option, choice);
                    };
                    editor = combo;
                    break;

                default:
                    var text = new TextBox { Text = value };
                    void Commit()
                    {
                        if (text.Text != (controller.SavedOptionValue(slot, option) ?? option.Default ?? ""))
                            _ = controller.SetEngineOptionAsync(slot, option, text.Text ?? "");
                    }
                    text.LostFocus += (_, _) => Commit();
                    text.KeyDown += (_, e) =>
                    {
                        if (e.Key == Key.Enter)
                            Commit();
                    };
                    editor = text;
                    break;
            }

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
            var label = new TextBlock { Text = option.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            label.Classes.Add("label");
            ToolTip.SetTip(label, option.Name);
            Grid.SetColumn(editor, 1);
            row.Children.Add(label);
            row.Children.Add(editor);
            OptionsPanel.Children.Add(row);
        }
    }

    /// <summary>
    /// A slider for the playing strength an engine advertises as "UCI_Elo" (Stockfish
    /// does), dragged with the mouse in steps of 10. The engine only plays at that
    /// strength while UCI_LimitStrength is on, so moving the slider turns it on too.
    /// The value is sent once the slider has rested for a moment, not for every step
    /// of a drag, since each change is a setoption the engine has to acknowledge.
    /// </summary>
    private Control BuildEloSlider(EngineSlot slot, UciEngine engine, UciOption option, string value,
                                   Dictionary<string, CheckBox> checks)
    {
        var inv = CultureInfo.InvariantCulture;
        double min = option.Min!.Value, max = option.Max!.Value;
        double start = double.TryParse(value, NumberStyles.Integer, inv, out var parsed) ? Math.Clamp(parsed, min, max) : min;

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = start,
            SmallChange = 10,
            LargeChange = 100,
            TickFrequency = 10,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var shown = new TextBlock
        {
            Text = start.ToString("0", inv),
            Width = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
        };
        var hint = new TextBlock
        {
            Text = $"Drag to set the strength ({min.ToString("0", inv)}–{max.ToString("0", inv)} Elo). Moving it turns on UCI_LimitStrength; untick that to play at full strength again.",
            TextWrapping = TextWrapping.Wrap,
        };
        hint.Classes.Add("muted");

        var send = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        send.Tick += (_, _) =>
        {
            send.Stop();
            var elo = ((long)Math.Round(slider.Value)).ToString(inv);
            if (elo == (controller.SavedOptionValue(slot, option) ?? option.Default))
                return;
            _ = controller.SetEngineOptionAsync(slot, option, elo);

            // The Elo only counts with the strength limit on.
            if (checks.TryGetValue("UCI_LimitStrength", out var limit))
            {
                if (limit.IsChecked != true)
                    limit.IsChecked = true;   // its own handler sends the option
            }
            else if (engine.Options.FirstOrDefault(o => o.Name.Equals("UCI_LimitStrength", StringComparison.OrdinalIgnoreCase)) is { } limitOption)
            {
                _ = controller.SetEngineOptionAsync(slot, limitOption, "true");
            }
        };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
                return;
            shown.Text = slider.Value.ToString("0", inv);
            send.Stop();
            send.Start();
        };

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
        Grid.SetColumn(shown, 2);
        line.Children.Add(slider);
        line.Children.Add(shown);
        return new StackPanel { Spacing = 2, Children = { line, hint } };
    }

    // ----------------------------------------------------------------------
    // Engine match
    // ----------------------------------------------------------------------

    private void RefreshMatch()
    {
        var match = controller.Match;
        bool running = controller.IsMatchRunning;
        bool bothLoaded = controller.Slots.All(s => s.Engine is not null);
        var inv = CultureInfo.InvariantCulture;

        // Players, limits and engines stay fixed while a match is being played.
        GameSetupGrid.IsEnabled = !running;
        NewGameButton.IsEnabled = !running;
        SwapSidesButton.IsEnabled = !running;
        SetPositionButton.IsEnabled = !running;
        if (running)
            UndoButton.IsEnabled = false;

        MatchPairing.Text = $"{controller.Slots[0].Name}  vs  {controller.Slots[1].Name}";
        MatchSetupPanel.IsEnabled = !running;
        MatchStartButton.IsEnabled = !running && bothLoaded;
        MatchStartButton.Content = match is not null && !running ? "Start new match" : "Start match";
        MatchStopButton.IsEnabled = running;
        MatchPauseButton.IsEnabled = running;
        MatchPauseButton.Content = running && match!.IsPaused ? "Resume match" : "Pause match";

        var score = match?.Engine1;
        ResultName1.Text = $"Engine 1 · {match?.Engine1Name ?? controller.Slots[0].Name}";
        ResultName2.Text = $"Engine 2 · {match?.Engine2Name ?? controller.Slots[1].Name}";
        ResultWins1.Text = Number(score?.Wins);
        ResultLosses1.Text = Number(score?.Losses);
        ResultDraws1.Text = Number(score?.Draws);
        ResultWins2.Text = Number(score?.Losses);
        ResultLosses2.Text = Number(score?.Wins);
        ResultDraws2.Text = Number(score?.Draws);
        ResultScore1.Text = score is { Games: > 0 } ? Percent(score.Score) : "—";
        ResultScore2.Text = score is { Games: > 0 } ? Percent(1 - score.Score) : "—";

        if (match is null)
        {
            MatchProgress.Maximum = Math.Max(1, settings.MatchGames);
            MatchProgress.Value = 0;
            MatchProgressText.Text = bothLoaded ? "No match played yet." : "Load Engine 1 and Engine 2 on the Engines tab first.";
            MatchElo.Text = "";
            MatchSprt.Text = "";
            MatchPairs.Text = "";
            MatchColours.Text = "";
            RefreshMatchStatistics(null);
            MatchPgnPath.Text = "";
            MatchSavePgnButton.IsEnabled = false;
            MatchGamesList.ItemsSource = null;
            shownMatchGames = null;
            return;
        }

        MatchProgress.Maximum = match.TotalGames;
        MatchProgress.Value = match.GamesPlayed;
        var timeControl = match.TimeControl.Describe();
        MatchProgressText.Text = running && match.IsPaused
            ? $"Paused  ·  {timeControl}  ·  {match.GamesPlayed} of {match.TotalGames} finished — press Resume match to carry on"
            : running
            ? match.ParallelGames > 1
                ? $"{timeControl}  ·  {match.ParallelGames} games at once  ·  {match.GamesPlayed} of {match.TotalGames} finished  ·  board shows {match.CurrentOpening}"
                : $"{timeControl}  ·  {match.GamesPlayed} of {match.TotalGames} finished  ·  {match.CurrentOpening}"
            : $"{timeControl}  ·  {match.GamesPlayed} of {match.TotalGames} games played — "
              + (match.IsFinished ? "match finished" : match.EndReason is not null ? "SPRT decided" : "match stopped");

        MatchElo.Text = match.GamesPlayed == 0 ? ""
            : match.Engine1.EloEstimate() is { } estimate
                ? $"Engine 1 is {(Math.Round(estimate.Elo) + 0.0).ToString("+0;-0;0", inv)} ± {estimate.Margin.ToString("0", inv)} Elo compared with Engine 2 (95% confidence)."
                : match.Engine1.Score >= 1 ? "Engine 1 has won every game." : "Engine 2 has won every game.";
        if (match.Sprt is { } sprt)
        {
            var verdict = sprt.Decision switch
            {
                SprtDecision.H1Accepted => $"H1 accepted: Engine 1 is at least {sprt.Settings.Elo1.ToString("0.#", inv)} Elo stronger.",
                SprtDecision.H0Accepted => $"H0 accepted: Engine 1 is not {sprt.Settings.Elo1.ToString("0.#", inv)} Elo stronger (at most {sprt.Settings.Elo0.ToString("0.#", inv)}).",
                _ => "No decision yet.",
            };
            MatchSprt.Text = $"SPRT [{sprt.Settings.Elo0.ToString("0.#", inv)}, {sprt.Settings.Elo1.ToString("0.#", inv)}]: {sprt.DescribeLlr()} — {verdict}";
            var c = sprt.PairCounts;
            MatchPairs.Text = $"{sprt.Pairs} complete pairs, Engine 1's points per pair:  0: {c[0]}  ·  ½: {c[1]}  ·  1: {c[2]}  ·  1½: {c[3]}  ·  2: {c[4]}";
        }
        var white = match.Engine1AsWhite;
        var black = match.Engine1AsBlack;
        MatchColours.Text = $"Engine 1 with White: {white.Wins} W · {white.Losses} L · {white.Draws} D\n" +
                            $"Engine 1 with Black: {black.Wins} W · {black.Losses} L · {black.Draws} D";
        RefreshMatchStatistics(match);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var shownPath = match.PgnPath.StartsWith(home, StringComparison.Ordinal) ? "~" + match.PgnPath[home.Length..] : match.PgnPath;
        MatchPgnPath.Text = $"Each game is saved when it ends to {shownPath}";
        ToolTip.SetTip(MatchPgnPath, match.PgnPath);
        ToolTip.SetTip(MatchProgressText, MatchProgressText.Text);
        MatchSavePgnButton.IsEnabled = match.GamesPlayed > 0;

        if (shownMatchGames is { } shown && shown.Match == match && shown.Games == match.GamesPlayed)
            return;
        shownMatchGames = (match, match.GamesPlayed);
        MatchGamesList.ItemsSource = Enumerable.Reverse(match.Games).Take(20).Select(DescribeMatchGame).ToList();

        static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";
        static string Percent(double value) => (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// The Engine speed and How games ended cards. The speeds change with every move; the
    /// endings table only when a game finishes, so it is rebuilt only then.
    /// </summary>
    private void RefreshMatchStatistics(MatchRun? match)
    {
        var inv = CultureInfo.InvariantCulture;
        // The numbers leave no room for full names; those go in the line above the table.
        var name1 = match?.Engine1Name ?? controller.Slots[0].Name;
        var name2 = match?.Engine2Name ?? controller.Slots[1].Name;
        SpeedName1.Text = "Engine 1";
        SpeedName2.Text = "Engine 2";
        ToolTip.SetTip(SpeedName1, name1);
        ToolTip.SetTip(SpeedName2, name2);
        SpeedNps1.Text = match?.Engine1Speed.Describe() ?? "—";
        SpeedNps2.Text = match?.Engine2Speed.Describe() ?? "—";
        SpeedPerMove1.Text = match?.Engine1Speed.DescribeNodesPerMove() ?? "—";
        SpeedPerMove2.Text = match?.Engine2Speed.DescribeNodesPerMove() ?? "—";
        SpeedTime1.Text = match?.Engine1Speed.DescribeTimePerMove() ?? "—";
        SpeedTime2.Text = match?.Engine2Speed.DescribeTimePerMove() ?? "—";
        SpeedMoves1.Text = match is null ? "—" : MoveCount(match.Engine1Speed.Searches);
        SpeedMoves2.Text = match is null ? "—" : MoveCount(match.Engine2Speed.Searches);

        // Before a match, the threads and games at once the next one would use.
        var threads1 = match is not null ? match.Engine1Threads : controller.EngineOptionValue(controller.Slots[0], "Threads");
        var threads2 = match is not null ? match.Engine2Threads : controller.EngineOptionValue(controller.Slots[1], "Threads");
        int parallel = match?.ParallelGames ?? settings.MatchParallelGames;
        SpeedThreads1.Text = threads1 ?? "—";
        SpeedThreads2.Text = threads2 ?? "—";
        SpeedSetup.Text = $"Engine 1: {name1}\nEngine 2: {name2}" +
                          (match is null ? "" : $"\n{match.TimeControl.Describe()}, {parallel} game{(parallel == 1 ? "" : "s")} at once.");

        // Only the side to move thinks, so each game keeps at most the larger thread count busy.
        int busy = parallel * Math.Max(ParseThreads(threads1), ParseThreads(threads2));
        int cores = PerformanceCores();
        SpeedWarning.IsVisible = busy > cores;
        SpeedWarning.Text = $"Up to {busy} search threads run at once, but this computer has {cores} performance cores: " +
                            "the engines will slow each other down and these figures will come out low. " +
                            "Lower the games at once or the engines' Threads.";

        var key = (match, match?.GamesPlayed ?? -1);
        if (shownMatchEndings == key)
            return;
        shownMatchEndings = key;

        EndingsGrid.Children.Clear();
        EndingsGrid.RowDefinitions.Clear();
        var tally = match?.Endings;
        if (tally is null || tally.Games == 0)
        {
            MatchLength.Text = match is null ? "" : "No games finished yet.";
            return;
        }

        AddEndingsRow("", "Games", "Share", header: true);
        foreach (var (label, count, isTotal) in tally.Rows())
            AddEndingsRow(label, count.ToString(inv), (100.0 * count / tally.Games).ToString("0.0", inv) + "%", total: isTotal);

        string Moves(double? value) => value is { } v ? v.ToString("0.0", inv) : "—";
        MatchLength.Text = $"Average length: {Moves(tally.AverageMoves())} moves per game " +
                           $"(decisive {Moves(tally.AverageMoves(false))}, drawn {Moves(tally.AverageMoves(true))}), " +
                           "counted from the start position, opening included.";

        void AddEndingsRow(string label, string count, string share, bool header = false, bool total = false)
        {
            int row = EndingsGrid.RowDefinitions.Count;
            EndingsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = new[]
            {
                new TextBlock { Text = label, Margin = new Avalonia.Thickness(total || header ? 0 : 14, 3, 0, 3) },
                new TextBlock { Text = count, TextAlignment = TextAlignment.Right, Margin = new Avalonia.Thickness(0, 3) },
                new TextBlock { Text = share, TextAlignment = TextAlignment.Right, Margin = new Avalonia.Thickness(0, 3) },
            };
            for (int column = 0; column < cells.Length; column++)
            {
                var cell = cells[column];
                if (header)
                    cell.Classes.Add("columnHead");
                else if (total)
                    cell.FontWeight = FontWeight.SemiBold;
                else
                    cell.Foreground = InfoText;
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                EndingsGrid.Children.Add(cell);
            }
        }
    }

    // Move counts up to 99,999 in full; beyond that as "123k", so the column never overflows.
    private static string MoveCount(int moves) =>
        moves < 100_000 ? moves.ToString(CultureInfo.InvariantCulture)
                        : (moves / 1000).ToString(CultureInfo.InvariantCulture) + "k";

    private static int ParseThreads(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(1, n) : 1;

    private static int? performanceCores;

    /// <summary>
    /// The fast cores: on Apple silicon the performance cores (an engine thread landing on
    /// an efficiency core slows the whole search down), elsewhere every logical core.
    /// </summary>
    private static int PerformanceCores()
    {
        if (performanceCores is { } known)
            return known;
        int cores = Environment.ProcessorCount;
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                int value = 0;
                nint size = sizeof(int);
                if (sysctlbyname("hw.perflevel0.physicalcpu", ref value, ref size, 0, 0) == 0 && value > 0)
                    cores = value;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }
        performanceCores = cores;
        return cores;
    }

    [DllImport("libc", EntryPoint = "sysctlbyname")]
    private static extern int sysctlbyname(string name, ref int value, ref nint size, nint newValue, nint newSize);

    private static MatchGameRow DescribeMatchGame(MatchGameRecord game)
    {
        var (white, black) = game.Engine1White ? ("Engine 1", "Engine 2") : ("Engine 2", "Engine 1");
        var reason = game.Result.Reason switch
        {
            GameEndReason.Checkmate => "checkmate",
            GameEndReason.Stalemate => "stalemate",
            GameEndReason.InsufficientMaterial => "insufficient material",
            GameEndReason.FiftyMoveRule => "50-move rule",
            GameEndReason.ThreefoldRepetition => "repetition",
            GameEndReason.Timeout => "on time",
            GameEndReason.TimeoutVsInsufficientMaterial => "time, no mating material",
            GameEndReason.IllegalMove => "illegal move",
            GameEndReason.Adjudication => "adjudicated",
            _ => "",
        };
        var winner = game.Engine1Points switch
        {
            1.0 => "Engine 1 won",
            0.0 => "Engine 2 won",
            _ => "Draw",
        };
        return new MatchGameRow($"{game.Number}.", $"{white} (White) vs {black} · {reason} · {(game.Plies + 1) / 2} moves", winner);
    }

    private void OnMatchSetupChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (updatingControls || engineCards is null || MatchGamesBox is null || MatchRandomBox is null
            || MatchParallelBox is null || TimeValueBox is null || IncrementBox is null || SprtElo0Box is null
            || SprtElo1Box is null)
            return;
        settings.MatchGames = (int)(MatchGamesBox.Value ?? 1000);
        settings.MatchRandomPlies = (int)(MatchRandomBox.Value ?? 0);
        settings.MatchParallelGames = (int)(MatchParallelBox.Value ?? 1);
        if (TimeValueBox.Value is { } value)
        {
            switch (settings.MatchTimeControlKind)
            {
                case TimeControlKind.MoveTime: settings.MatchMoveTimeMs = (int)Math.Max(1, value); break;
                case TimeControlKind.Clock: settings.MatchClockSeconds = (double)Math.Max(0.1m, value); break;
                case TimeControlKind.Nodes: settings.MatchNodes = (long)Math.Max(1, value); break;
            }
        }
        settings.MatchIncrementSeconds = (double)(IncrementBox.Value ?? 0);
        settings.SprtElo0 = (double)(SprtElo0Box.Value ?? 0);
        settings.SprtElo1 = (double)(SprtElo1Box.Value ?? 10);
        settings.Save();
        RefreshTimeControlHelp();
        RefreshMatch();
    }

    private void OnTimeControlKindChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (updatingControls || engineCards is null || TimeControlBox is null)
            return;
        settings.MatchTimeControlKind = (TimeControlKind)Math.Max(0, TimeControlBox.SelectedIndex);
        settings.Save();
        ShowTimeControlValue();
        RefreshMatch();
    }

    private void OnSprtStopChanged(object? sender, RoutedEventArgs e)
    {
        if (updatingControls || engineCards is null)
            return;
        settings.SprtStopMatch = SprtStopCheck.IsChecked == true;
        settings.Save();
    }

    /// <summary>Puts the setting that belongs to the chosen time control into the value box, with a label and step to suit.</summary>
    private void ShowTimeControlValue()
    {
        bool wasUpdating = updatingControls;
        updatingControls = true;
        var kind = settings.MatchTimeControlKind;
        TimeValueLabel.IsVisible = TimeValueBox.IsVisible = kind != TimeControlKind.EngineDecides;
        IncrementLabel.IsVisible = IncrementBox.IsVisible = kind == TimeControlKind.Clock;
        (string label, decimal min, decimal step, string format, decimal value) = kind switch
        {
            TimeControlKind.MoveTime => ("Milliseconds per move", 1m, 100m, "0", (decimal)settings.MatchMoveTimeMs),
            TimeControlKind.Clock => ("Time per side (seconds)", 0.1m, 5m, "0.###", (decimal)settings.MatchClockSeconds),
            TimeControlKind.Nodes => ("Nodes per move", 1m, 10_000m, "0", (decimal)settings.MatchNodes),
            _ => ("", 0m, 1m, "0", 0m),
        };
        TimeValueLabel.Text = label;
        TimeValueBox.Minimum = min;
        TimeValueBox.Increment = step;
        TimeValueBox.FormatString = format;
        TimeValueBox.Value = Math.Max(min, value);
        updatingControls = wasUpdating;
        RefreshTimeControlHelp();
    }

    private void RefreshTimeControlHelp()
    {
        var tc = settings.ToMatchTimeControl();
        TimeControlHelp.Text = tc.Kind switch
        {
            TimeControlKind.EngineDecides =>
                "Each engine gets a plain \"go\" and decides for itself how long to think (Engines 9 to 12: up to 500 ms, " +
                "and no new depth after 250 ms).",
            TimeControlKind.MoveTime =>
                $"Each move: \"go movetime {tc.MoveTimeMs.ToString(CultureInfo.InvariantCulture)}\". The engine may use up to that much; " +
                "Engines 9 to 12 stop starting new depths after half of it.",
            TimeControlKind.Clock =>
                $"{tc.Describe()}: each side starts with that much time and gets the increment after every move. The GUI keeps " +
                "the clocks and sends them with every \"go\"; an engine that runs out loses on time. This is the setting " +
                "that tests time management.",
            _ => $"Each move: \"go nodes {tc.Nodes.ToString(CultureInfo.InvariantCulture)}\". The same search on any computer, " +
                 "however busy, but it hides speed differences between the engines.",
        };
    }

    private void OnStartMatch(object? sender, RoutedEventArgs e) =>
        controller.StartMatch(settings.MatchGames, settings.MatchRandomPlies, settings.MatchParallelGames,
                              settings.ToMatchTimeControl(), settings.ToSprtSettings(), settings.SprtStopMatch);

    private void OnStopMatch(object? sender, RoutedEventArgs e) => controller.StopMatch();

    private void OnPauseMatch(object? sender, RoutedEventArgs e) =>
        controller.SetMatchPaused(controller.Match is not { IsPaused: true });

    private async void OnSaveMatchPgn(object? sender, RoutedEventArgs e)
    {
        if (controller.Match is not { } match || !File.Exists(match.PgnPath))
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save match games",
            SuggestedFileName = Path.GetFileName(match.PgnPath),
            DefaultExtension = "pgn",
        });
        if (file is null)
            return;

        try
        {
            if (file.TryGetLocalPath() is { } target)
            {
                File.Copy(match.PgnPath, target, overwrite: true);
            }
            else
            {
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await using var source = File.OpenRead(match.PgnPath);
                await source.CopyToAsync(stream);
            }
            controller.ShowMessage($"Saved {match.GamesPlayed} games to {file.Name}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            controller.ShowMessage("Could not save the games: " + ex.Message, error: true);
        }
    }

    // ----------------------------------------------------------------------
    // Settings controls
    // ----------------------------------------------------------------------

    private void LoadControlsFromSettings()
    {
        updatingControls = true;
        WhitePlayerBox.SelectedIndex = (int)settings.WhitePlayer;
        BlackPlayerBox.SelectedIndex = (int)settings.BlackPlayer;
        ArrowCheck.IsChecked = settings.ShowEngineArrow;
        MatchGamesBox.Value = settings.MatchGames;
        MatchRandomBox.Value = settings.MatchRandomPlies;
        MatchParallelBox.Value = settings.MatchParallelGames;
        TimeControlBox.SelectedIndex = (int)settings.MatchTimeControlKind;
        IncrementBox.Value = (decimal)settings.MatchIncrementSeconds;
        SprtElo0Box.Value = (decimal)settings.SprtElo0;
        SprtElo1Box.Value = (decimal)settings.SprtElo1;
        SprtStopCheck.IsChecked = settings.SprtStopMatch;
        ShowTimeControlValue();
        foreach (var card in engineCards)
        {
            int index = Array.IndexOf(engineCards, card);
            card.Args.Text = settings.Engines[index].Arguments;
            card.Analyse.IsChecked = settings.Engines[index].Analyse;
        }
        OptionsEngineBox.SelectedIndex = 0;
        CommandEngineBox.SelectedIndex = 0;
        updatingControls = false;
    }

    private void OnPlayersChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (updatingControls || WhitePlayerBox is null || BlackPlayerBox is null || engineCards is null)
            return;
        controller.SetPlayers(
            (PlayerKind)Math.Max(0, WhitePlayerBox.SelectedIndex),
            (PlayerKind)Math.Max(0, BlackPlayerBox.SelectedIndex));
    }

    private void OnSwapSides(object? sender, RoutedEventArgs e)
    {
        var white = settings.WhitePlayer;
        var black = settings.BlackPlayer;
        updatingControls = true;
        WhitePlayerBox.SelectedIndex = (int)black;
        BlackPlayerBox.SelectedIndex = (int)white;
        updatingControls = false;
        controller.SetPlayers(black, white);
        AutoFlip();
        RefreshPlayerBars();
    }

    private void OnArrowChanged(object? sender, RoutedEventArgs e)
    {
        if (updatingControls || engineCards is null)
            return;
        settings.ShowEngineArrow = ArrowCheck.IsChecked == true;
        settings.Save();
        RefreshAnalysis();
    }

    private void OnAnalyseChanged(object? sender, RoutedEventArgs e)
    {
        if (updatingControls || engineCards is null || sender is not CheckBox check)
            return;
        controller.SetAnalyse(SlotOf(sender), check.IsChecked == true);
    }

    // ----------------------------------------------------------------------
    // Buttons and shortcuts
    // ----------------------------------------------------------------------

    private EngineSlot SlotOf(object? sender) =>
        controller.Slots[sender is Control { Tag: string tag } && int.TryParse(tag, out int index) ? index : 0];

    private async void OnLoadEngine(object? sender, RoutedEventArgs e)
    {
        var slot = SlotOf(sender);
        var options = new FilePickerOpenOptions { Title = $"Choose a UCI engine for {slot.Label}", AllowMultiple = false };
        var previous = settings.Engines[slot.Index].Path ?? settings.Engines.Select(x => x.Path).FirstOrDefault(p => p is not null);
        if (previous is not null && Path.GetDirectoryName(previous) is { } folder && Directory.Exists(folder))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await controller.LoadEngineAsync(slot, path, EngineArguments(slot));
    }

    private async void OnReloadEngine(object? sender, RoutedEventArgs e)
    {
        var slot = SlotOf(sender);
        if (settings.Engines[slot.Index].Path is { } path)
            await controller.LoadEngineAsync(slot, path, EngineArguments(slot));
    }

    private async void OnUnloadEngine(object? sender, RoutedEventArgs e) => await controller.UnloadEngineAsync(SlotOf(sender));

    private string? EngineArguments(EngineSlot slot) =>
        engineCards[slot.Index].Args.Text is { } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private void OnStart(object? sender, RoutedEventArgs e) => controller.Start();

    private async void OnQuit(object? sender, RoutedEventArgs e) => await controller.QuitAsync();

    private void OnNewGame(object? sender, RoutedEventArgs e) => NewGame();

    private void NewGame()
    {
        AutoFlip();
        controller.NewGame();
    }

    private void OnUndo(object? sender, RoutedEventArgs e) => controller.Undo();

    private void OnFlip(object? sender, RoutedEventArgs e) => Flip();

    private void Flip()
    {
        Board.Flipped = !Board.Flipped;
        RefreshPlayerBars();
    }

    private void OnEngineMove(object? sender, RoutedEventArgs e) => controller.EngineMoveNow();

    private void OnPause(object? sender, RoutedEventArgs e) => controller.SetPaused(!controller.IsPaused);

    private void OnSetPosition(object? sender, RoutedEventArgs e)
    {
        if (ChessCore.Position.TryParseFen(FenBox.Text, out var position, out var error))
            controller.NewGame(position);
        else
            controller.ShowMessage("Invalid FEN: " + error, error: true);
    }

    private async void OnCopyFen(object? sender, RoutedEventArgs e) =>
        await CopyAsync(controller.Game.Position.ToFen(), "FEN copied to the clipboard.");

    private async void OnCopyPgn(object? sender, RoutedEventArgs e) =>
        await CopyAsync(controller.ToPgn(), "PGN copied to the clipboard.");

    private async Task CopyAsync(string text, string confirmation)
    {
        if (Clipboard is not { } clipboard)
            return;
        await clipboard.SetTextAsync(text);
        controller.ShowMessage(confirmation);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || FocusManager?.GetFocusedElement() is TextBox)
            return;

        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (command && e.Key == Key.N)
            NewGame();
        else if (command && e.Key == Key.Z)
            controller.Undo();
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None)
            Flip();
        else
            return;
        e.Handled = true;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (shutdownComplete)
            return;

        // Give the engines a chance to quit cleanly before the window goes away.
        e.Cancel = true;
        shutdownComplete = true;
        await controller.DisposeAsync();
        Close();
    }

    private sealed record EngineCard(TextBlock Name, TextBlock Path, TextBox Args, Button Load, Button Reload, Button Unload, CheckBox Analyse);

    private sealed record EvalRow(Control Row, TextBlock Name, TextBlock Role, TextBlock Score, TextBlock Depth, TextBlock Nodes, SelectableTextBlock Pv);
}
