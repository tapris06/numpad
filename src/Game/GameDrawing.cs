using Godot;
using NumpadNine.Core;

namespace NumpadNine.Game;

public partial class GameRoot
{
    private sealed record ScreenColors(Color Paper, Color Surface, Color Ink, Color Muted, Color Line,
        Color Hover, Color Lane, Color LaneGuide);
    private static readonly ScreenColors LightColors = new(new("#fbfaf7"), Colors.White, new("#252c3a"),
        new("#78808d"), new("#e7e6e1"), new("#f1f1ed"), new("#f2f1ed"), new("#e7e5df"));
    private static readonly ScreenColors DarkColors = new(Colors.Black, new("#15181f"), new("#eef1f6"),
        new("#a9b1bd"), new("#303642"), new("#252a34"), new("#14171d"), new("#292f39"));
    private static readonly Color Red = new("#e45660"), Blue = new("#397dd8"), Yellow = new("#e8bd36"),
        Green = new("#4b9f75"), Purple = new("#9463c2"), Orange = new("#ec893e");
    private static readonly Color RgbRed = new("#e44550"), RgbBlue = new("#2b75d8"), RgbGreen = new("#3e9f6f"),
        RgbYellow = new("#e8b924"), Cyan = new("#31c6df"), Magenta = new("#df55c9");
    private static readonly Color[] LightRainbow = [new("#ff5369"), new("#ff9e4a"), new("#ffdb48"),
        new("#4ad678"), new("#3ed8e8"), new("#4d9bff"), new("#b787ff")];
    private static readonly Color[] DarkRainbow = [new("#ff8595"), new("#ffb573"), new("#ffe461"),
        new("#75e6a1"), new("#6fe8f4"), new("#8bbbff"), new("#d7afff")];
    private static readonly Grade[] GradeOrder = [Grade.Colorful, Grade.Perfect, Grade.Good, Grade.Miss];
    private ScreenColors Palette => _invertColors ? DarkColors : LightColors;
    private Color[] Rainbow => _invertColors ? DarkRainbow : LightRainbow;
    private Color Paper => Palette.Paper;
    private Color Surface => Palette.Surface;
    private Color Ink => Palette.Ink;
    private Color Muted => Palette.Muted;
    private Color Line => Palette.Line;
    private Color OnInk => _invertColors ? Colors.Black : Colors.White;
    private float KeyFillTint => _invertColors ? 0.70f : 0.88f;
    private const float RgbKeyBorderTint = 0.48f;
    private Color Tint(Color color, float amount) => _invertColors ? color.Darkened(amount) : color.Lightened(amount);
    private Color KeyLabel(Color color, float amount) => _invertColors ? color.Lightened(amount) : color.Darkened(amount);
    private SystemFont _font = null!, _bold = null!, _numbers = null!;
    private readonly StyleBoxFlat _box = new();
    private float W => GetViewportRect().Size.X;
    private float H => GetViewportRect().Size.Y;
    private const float Cell = 76, Gap = 8;
    private const int KeyCornerRadius = 13;
    private Vector2 Grid => new(168, H - 276);
    private float GridSize => Cell * 3 + Gap * 2;
    private float JudgeY => Grid.Y - 18;
    private float JudgeX => Grid.X + GridSize + 18;
    private float LaneLength => Math.Max(1f, Math.Min(JudgeY - 170f, W - 72f - JudgeX));
    private float Top => JudgeY - LaneLength;
    private float Right => JudgeX + LaneLength;

    private void InitDrawing()
    {
        _font = new SystemFont { FontNames = ["Segoe UI", "Malgun Gothic"], FontWeight = 400 };
        _bold = new SystemFont { FontNames = ["Segoe UI", "Malgun Gothic"], FontWeight = 700 };
        _numbers = new SystemFont { FontNames = ["Bahnschrift", "Segoe UI", "Malgun Gothic"], FontWeight = 600 };
    }

    private void Text(string text, float x, float y, int size = 18, Color? color = null, bool bold = false)
        => DrawString(bold ? _bold : _font, new(x, y), text, HorizontalAlignment.Left, -1, size, color ?? Ink);

    private void Center(string text, Rect2 rect, int size = 18, Color? color = null, bool bold = false)
    {
        Font font = bold ? _bold : _font;
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new(rect.Position.X + (rect.Size.X - width) / 2,
            rect.GetCenter().Y + (font.GetAscent(size) - font.GetDescent(size)) / 2),
            text, HorizontalAlignment.Left, -1, size, color ?? Ink);
    }

    private void Number(string text, float x, float y, int size, Color? color = null)
        => DrawString(_numbers, new(x, y), text, HorizontalAlignment.Left, -1, size, color ?? Ink);

    private void Round(Rect2 rect, Color fill, int radius = 12, Color? border = null, int borderWidth = 1)
    {
        _box.BgColor = fill;
        _box.CornerRadiusTopLeft = _box.CornerRadiusTopRight = _box.CornerRadiusBottomLeft = _box.CornerRadiusBottomRight = radius;
        _box.BorderColor = border ?? fill;
        int width = border.HasValue ? borderWidth : 0;
        _box.BorderWidthTop = _box.BorderWidthRight = _box.BorderWidthBottom = _box.BorderWidthLeft = width;
        DrawStyleBox(_box, rect);
    }

    private void Button(string id, string text, Rect2 rect, bool primary = false, int size = 18)
    {
        _buttons[id] = rect;
        bool hover = rect.HasPoint(_mouse);
        Color fill = primary ? Ink : Surface;
        if (hover) fill = primary ? Tint(Ink, 0.12f) : Palette.Hover;
        Round(rect, fill, 10, primary ? null : Line);
        Center(text, rect, size, primary ? OnInk : Ink, true);
    }

    private void Logo(float x, float y, float unit = 8)
    {
        for (int i = 0; i < 9; i++)
        {
            int row = i / 3, column = i % 3;
            int key = 7 - row * 3 + column;
            Round(new(x + column * (unit + 3), y + row * (unit + 3), unit, unit), Mixed(ColorBit(key)), 2);
        }
    }

    private static int ColorBit(int key) => key switch { 7 or 6 or 2 => 1, 8 or 4 or 3 => 2, _ => 4 };
    private Color Mixed(int bits) => bits switch
    {
        1 => _invertColors ? RgbRed : Red,
        2 => _invertColors ? RgbBlue : Blue,
        4 => _invertColors ? RgbGreen : Yellow,
        3 => _invertColors ? Magenta : Purple,
        5 => _invertColors ? RgbYellow : Orange,
        6 => _invertColors ? Cyan : Green,
        7 => _invertColors ? Colors.White : Ink,
        _ => Muted
    };
    private static string GradeName(Grade grade) => grade switch
    {
        Grade.Colorful => "COLORFUL", Grade.Perfect => "PERFECT", Grade.Good => "GOOD", _ => "MISS"
    };
    private Color GradeColor(Grade grade) => grade switch
    {
        Grade.Colorful => Rainbow[6], Grade.Perfect => Rainbow[2], Grade.Good => Rainbow[5], _ => Rainbow[0]
    };

    private void DrawGradeText(string label, Grade grade, Vector2 position, int size, float alpha = 1f)
    {
        var outline = new Color(Ink, alpha * 0.90f);
        int outlineSize = size >= 24 ? 2 : 1;
        if (grade != Grade.Colorful)
        {
            Color color = GradeColor(grade);
            color.A = alpha;
            if (!_invertColors)
                DrawStringOutline(_bold, position, label, HorizontalAlignment.Left, -1, size, outlineSize, outline);
            Text(label, position.X, position.Y, size, color, true);
            return;
        }

        Color[] colors = Rainbow;
        var canvas = GetCanvasItem();
        for (int i = 0; i < label.Length; i++)
        {
            Color color = colors[i * colors.Length / label.Length];
            color.A = alpha;
            if (!_invertColors)
                _bold.DrawCharOutline(canvas, position, label[i], size, outlineSize, outline);
            _bold.DrawChar(canvas, position, label[i], size, color);
            position.X += _bold.GetCharSize(label[i], size).X;
        }
    }

    private void CenterGradeText(string label, Grade grade, Rect2 rect, int size, float alpha = 1f)
    {
        float width = 0;
        if (grade == Grade.Colorful)
            foreach (char letter in label) width += _bold.GetCharSize(letter, size).X;
        else width = _bold.GetStringSize(label, HorizontalAlignment.Left, -1, size).X;
        Vector2 center = rect.GetCenter();
        DrawGradeText(label, grade, new(center.X - width / 2,
            center.Y + (_bold.GetAscent(size) - _bold.GetDescent(size)) / 2), size, alpha);
    }

    private void DrawGradeMarker(Grade grade, Vector2 center, float radius)
    {
        if (grade != Grade.Colorful)
        {
            DrawCircle(center, radius, GradeColor(grade));
            return;
        }

        Color[] colors = Rainbow;
        float step = Mathf.Tau / colors.Length;
        for (int i = 0; i < colors.Length; i++)
            DrawArc(center, radius - 1, i * step, (i + 1) * step, 4, colors[i], 2, true);
    }

    public override void _Draw()
    {
        _buttons.Clear();
        DrawRect(new(Vector2.Zero, GetViewportRect().Size), Paper);
        if (_screen == Screen.Select) DrawSelect();
        else if (_screen == Screen.Results) DrawResults();
        else
        {
            DrawPlay();
            if (_screen == Screen.Paused) DrawPause();
        }
        if (_debug)
        {
            Round(new(16, H - 44, 550, 30), Ink, 6);
            Text($"FPS {Engine.GetFramesPerSecond()}   song {SongUs / 1e6:F3}s   input {_offsetMs:+0;-0;0}ms", 27, H - 23, 14, OnInk);
        }
    }

    private void DrawHeader(string section)
    {
        Logo(64, 46, 8);
        Text("NUMPAD NINE", 108, 72, 23, Ink, true);
        Text(section, 300, 72, 14, Muted);
        DrawLine(new(64, 104), new(W - 64, 104), Line, 1);
    }

    private void DrawSelect()
    {
        DrawHeader("");
        Text("곡 선택", 72, 202, 38, Ink, true);
        float cardsWidth = W - 650;
        bool compact = _songs.Count >= SongListRows;
        float cardHeight = compact ? 82 : 100;
        float cardSpacing = compact ? 94 : 116;
        float cardTop = compact ? 258 : 282;
        if (_songs.Count > SongListRows)
        {
            Text($"{_songListStart + 1}–{Math.Min(_songListStart + SongListRows, _songs.Count)} / {_songs.Count}", 76, 239, 13, Muted);
            Button("songs-up", "↑", new(72 + cardsWidth - 86, 214, 36, 32), size: 16);
            Button("songs-down", "↓", new(72 + cardsWidth - 42, 214, 36, 32), size: 16);
            float trackHeight = cardSpacing * SongListRows - 12;
            float thumbHeight = trackHeight * SongListRows / _songs.Count;
            float thumbTop = cardTop + (trackHeight - thumbHeight) * _songListStart / (_songs.Count - SongListRows);
            Round(new(84 + cardsWidth, cardTop, 4, trackHeight), Line, 2);
            Round(new(84 + cardsWidth, thumbTop, 4, thumbHeight), Muted, 2);
        }
        for (int row = 0; row < Math.Min(_songs.Count - _songListStart, SongListRows); row++)
        {
            int i = _songListStart + row;
            var song = _songs[i];
            var rect = new Rect2(72, cardTop + row * cardSpacing, cardsWidth, cardHeight);
            bool selected = _selected == i;
            _buttons[$"song:{i}"] = rect;
            Round(rect, Surface, 14, selected ? Ink : Line, selected ? 2 : 1);
            var icon = new Rect2(rect.Position.X + 18, rect.Position.Y + (compact ? 15 : 21),
                compact ? 52 : 68, compact ? 52 : 72);
            Round(icon, selected ? Ink : Palette.Hover, 12);
            float logoUnit = compact ? 6 : 8, logoSize = logoUnit * 3 + 6;
            Logo(icon.GetCenter().X - logoSize / 2, icon.GetCenter().Y - logoSize / 2, logoUnit);
            float titleX = compact ? 160 : 176;
            Text(song.Chart.Title, titleX, rect.Position.Y + (compact ? 34 : 43), compact ? 23 : 25, Ink, true);
            Text(song.Chart.Artist.Length > 0 ? song.Chart.Artist : "Custom chart", titleX + 1,
                rect.Position.Y + (compact ? 60 : 76), compact ? 13 : 14, Muted);
            string difficulty = song.Chart.Difficulty.Length > 0 ? song.Chart.Difficulty : "CUSTOM";
            Color difficultyColor = difficulty switch { "EASY" => Green, "NORMAL" => Blue, "HARD" => Red, _ => Muted };
            var tag = new Rect2(rect.End.X - 154, rect.Position.Y + (compact ? 16 : 22), 130, compact ? 26 : 30);
            Round(tag, Tint(difficultyColor, 0.9f), 7);
            Center(difficulty, tag, 12, difficultyColor, true);
            Text($"{song.Chart.Notes.Count} NOTES", rect.End.X - 151, rect.Position.Y + (compact ? 64 : 80), 13, Muted, true);
        }
        float panelX = W - 522;
        Round(new(panelX, 282, 450, 377), Surface, 16, Line);
        Text("키 배치", panelX + 28, 324, 22, Ink, true);
        Button("position-guide", _positionGuide ? "위치 보조 켜짐" : "위치 보조 꺼짐",
            new(panelX + 124, 300, 143, 38), _positionGuide, 15);
        Button("invert-colors", _invertColors ? "색 반전 켜짐" : "색 반전 꺼짐",
            new(panelX + 279, 300, 143, 38), _invertColors, 15);
        DrawPreviewGrid(new(panelX + 50, 354), 62);
        if (_positionGuide)
        {
            float guideX = panelX + 280;
            Center("보조 표시 시간", new(guideX, 395, 142, 24), 14, Muted, true);
            Button("position-guide-time-", "−", new(guideX, 429, 32, 38), size: 16);
            Center($"{_positionGuideMs} ms", new(guideX + 32, 429, 78, 38), 16, Ink, true);
            Button("position-guide-time+", "+", new(guideX + 110, 429, 32, 38), size: 16);
        }
        Text("FPS 제한", panelX + 28, 587, 13, Muted, true);
        for (int i = 0; i < _fpsOptions.Length; i++)
            Button($"fps:{_fpsOptions[i]}", _fpsOptions[i].ToString(),
                new(panelX + 28 + i * 96, 605, 86, 34), _fpsIndex == i, 15);

        float settingY = H - 225;
        Text("스크롤 속도", 76, settingY - 17, 13, Muted, true);
        Button("speed-", "−", new(72, settingY, 40, 48));
        Center($"{SpeedMultiplier:F1}×", new(112, settingY, 92, 48), 19, Ink, true);
        Button("speed+", "+", new(204, settingY, 40, 48));
        Text("입력 보정", 270, settingY + 29, 15, Muted);
        Button("offset-", "−", new(355, settingY, 40, 48));
        Center($"{_offsetMs:+0;-0;0} ms", new(395, settingY, 94, 48), 17, Ink, true);
        Button("offset+", "+", new(489, settingY, 40, 48));
        Text("볼륨", 555, settingY + 29, 15, Muted);
        Button("volume-", "−", new(604, settingY, 40, 48));
        Center($"{MathF.Round(_volume * 100)}%", new(644, settingY, 70, 48), 16, Ink, true);
        Button("volume+", "+", new(714, settingY, 40, 48));
        Text("노래 오프셋", 780, settingY + 29, 15, Muted);
        Button("song-offset-", "−", new(884, settingY, 40, 48));
        Center($"{_songOffsetMs:+0;-0;0} ms", new(924, settingY, 104, 48), 17, Ink, true);
        Button("song-offset+", "+", new(1028, settingY, 40, 48));

        float exampleWidth = Math.Min(200, cardsWidth * 0.34f);
        float playWidth = cardsWidth - exampleWidth - 12;
        Button("play", "플레이 시작  →", new(72, H - 142, playWidth, 62), true, 21);
        Button("example", "오토플레이", new(84 + playWidth, H - 142, exampleWidth, 62), size: 18);
        Button("open", "채보 파일 열기", new(panelX, H - 142, 450, 62), size: 18);
        if (_error.Length > 0)
        {
            Color errorFill = _invertColors ? Red.Darkened(0.88f) : new("#fff0ed");
            Color errorBorder = _invertColors ? Red.Darkened(0.65f) : new("#f0d4ce");
            Round(new(72, H - 310, cardsWidth, 56), errorFill, 8, errorBorder);
            Text(_error.Length > 88 ? _error[..88] + "…" : _error, 87, H - 276, 14, Red);
        }
    }

    private void DrawPreviewGrid(Vector2 origin, float size)
    {
        int[,] keys = { { 7, 8, 9 }, { 4, 5, 6 }, { 1, 2, 3 } };
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                int key = keys[row, col];
                Color color = Mixed(ColorBit(key));
                var rect = new Rect2(origin + new Vector2(col * (size + 8), row * (size + 8)), new(size, size));
                Round(rect, Tint(color, KeyFillTint), 12, Tint(color, _invertColors ? RgbKeyBorderTint : 0.65f));
                Center(key.ToString(), rect, 23, KeyLabel(color, 0.25f), true);
                DrawLine(new(rect.Position.X + 17, rect.End.Y - 11), new(rect.End.X - 17, rect.End.Y - 11), color, 3, true);
            }
    }

    private void DrawPlay()
    {
        if (_session is null) return;
        Logo(64, 42, 8);
        Text("NUMPAD NINE", 108, 68, 21, Ink, true);
        Text(_session.Chart.Title, 342, 64, 21, Ink, true);
        string modeLabel = _playMode switch
        {
            PlayMode.Auto => "  ·  AUTO PLAY",
            PlayMode.Example => "  ·  오토플레이",
            _ => ""
        };
        Text(_session.Chart.Difficulty + modeLabel, 343, 88, 11, Muted, true);
        if (!IsExample)
        {
            Number(_session.Score.Score.ToString("N0"), W - 365, 75, 38);
            Text("SCORE", W - 363, 40, 10, Muted, true);
        }
        Button("pause", "일시정지", new(W - 154, 40, 90, 42), size: 13);
        DrawRect(new(64, 112, W - 128, 3), Line);
        float progress = (float)Math.Clamp((double)SongUs / Math.Max(_playEndUs, 1_000_000L), 0, 1);
        DrawRect(new(64, 112, (W - 128) * progress, 3), Blue);

        // The lane geometry is separate from input and score state.
        for (int lane = 0; lane < 3; lane++)
        {
            float x = Grid.X + lane * (Cell + Gap);
            float y = Grid.Y + lane * (Cell + Gap);
            Round(new(x, Top, Cell, LaneLength), Palette.Lane, 8);
            DrawLine(new(x + Cell / 2, Top + 10), new(x + Cell / 2, JudgeY), Palette.LaneGuide, 1);
            Round(new(JudgeX, y, LaneLength, Cell), Palette.Lane, 8);
            DrawLine(new(JudgeX, y + Cell / 2), new(Right - 10, y + Cell / 2), Palette.LaneGuide, 1);
        }
        Center("↓", new(Grid.X, Top - 43, GridSize, 30), 21, Muted);
        Text("←", Right - 24, Grid.Y - 22, 22, Muted);
        DrawHoldBodies();
        DrawHeads();
        DrawLine(new(Grid.X - 6, JudgeY), new(Grid.X + GridSize + 6, JudgeY), Ink, 3, true);
        DrawLine(new(JudgeX, Grid.Y - 6), new(JudgeX, Grid.Y + GridSize + 6), Ink, 3, true);
        DrawGrid();
        if (_positionGuide) DrawPositionGuide(_session);
        DrawHitFlashes();

        float centerX = JudgeX + 240;
        if (!IsExample) DrawPlayJudgments(_session.Score, centerX);
        if (SongUs < 0)
        {
            int count = Math.Max(1, (int)Math.Ceiling(-SongUs / 1e6));
            Round(new(centerX - 96, 330, 192, 108), Surface, 18, Line);
            Center(count.ToString(), new(centerX - 96, 333, 192, 76), 48, Blue, true);
        }
    }

    private void DrawPlayJudgments(ScoreState score, float centerX)
    {
        var comboRect = new Rect2(centerX - 140, 185, 280, 104);
        Center(score.Combo.ToString(), comboRect, 70, Ink, true);
        Center("COMBO", new(centerX - 140, 287, 280, 28), 12, Muted, true);
        if (_lastGrade.HasValue && _age - _lastJudgmentAt < 0.85)
        {
            float fade = 1 - (float)Math.Clamp((_age - _lastJudgmentAt - 0.45) / 0.4, 0, 1);
            CenterGradeText(GradeName(_lastGrade.Value), _lastGrade.Value, new(centerX - 150, 344, 300, 42), 28, fade);
            if (_lastError.HasValue && _lastGrade != Grade.Miss)
                Center($"{_lastError.Value / 1000.0:+0.0;-0.0;0.0} ms", new(centerX - 130, 392, 260, 24), 13, Muted);
        }
        float statX = W - 345;
        Text("판정 기록", statX, 191, 13, Muted, true);
        for (int i = 0; i < GradeOrder.Length; i++)
        {
            Grade grade = GradeOrder[i];
            float y = 234 + i * 49;
            DrawGradeMarker(grade, new(statX + 5, y - 5), 4);
            DrawGradeText(GradeName(grade), grade, new(statX + 22, y), 16);
            Number(score.Counts[grade].ToString(), W - 103, y + 1, 21);
        }
        Text($"최대 콤보  {score.MaxCombo}", statX, 459, 14, Muted);
    }

    private float NotePosition(Axis axis, long targetUs)
    {
        float distance = (float)((targetUs - SongUs) / 1e6 * ScrollPixelsPerSecond);
        return axis == Axis.C ? JudgeY - distance : JudgeX + distance;
    }

    private void DrawHoldBodies()
    {
        if (_session is null) return;
        long now = SongUs, visibleEnd = now + (long)(ApproachSeconds * 1_000_000);
        foreach (Axis axis in Enum.GetValues<Axis>())
            for (int lane = 0; lane < 3; lane++)
            {
                var notes = _session.States.Where(s => s.Note.IsHold && s.Note.Axis == axis && s.Note.Lane == lane
                    && s.Note.EndUs > now && s.Note.StartUs < visibleEnd).Select(s => s.Note).ToArray();
                var cuts = notes.SelectMany(n => new[] { Math.Max(n.StartUs, now), Math.Min(n.EndUs!.Value, visibleEnd) })
                    .Distinct().Order().ToArray();
                for (int i = 0; i + 1 < cuts.Length; i++)
                {
                    long a = cuts[i], b = cuts[i + 1];
                    int bits = notes.Where(n => n.StartUs < b && n.EndUs > a).Aggregate(0, (mask, n) => mask | ColorBit(n.Key));
                    if (bits == 0 || b <= a) continue;
                    Color color = Mixed(bits);
                    float p1 = NotePosition(axis, a), p2 = NotePosition(axis, b);
                    Rect2 rect = axis == Axis.C
                        ? new(Grid.X + lane * (Cell + Gap) + 12, p2, Cell - 24, Math.Max(1, p1 - p2))
                        : new(p1, Grid.Y + lane * (Cell + Gap) + 12, Math.Max(1, p2 - p1), Cell - 24);
                    DrawRect(rect, Tint(color, _invertColors ? 0.65f : 0.69f));
                    Rect2 stripe = axis == Axis.C
                        ? new(rect.GetCenter().X - 3, rect.Position.Y, 6, rect.Size.Y)
                        : new(rect.Position.X, rect.GetCenter().Y - 3, rect.Size.X, 6);
                    DrawRect(stripe, color);
                }
            }
    }

    private void DrawHeads()
    {
        if (_session is null) return;
        long now = SongUs;
        var groups = _session.States.Where(s => !s.Head.HasValue && s.Note.StartUs > now - 130000
                && s.Note.StartUs < now + ApproachSeconds * 1_000_000)
            .GroupBy(s => (s.Note.Axis, s.Note.Lane, s.Note.StartUs)).OrderByDescending(g => g.Key.StartUs);
        foreach (var group in groups)
        {
            var (axis, lane, time) = group.Key;
            // Heads show only new inputs at this time; sustained colors belong to the hold bodies.
            int bits = group.Aggregate(0, (mask, s) => mask | ColorBit(s.Note.Key));
            Color color = Mixed(bits);
            float p = NotePosition(axis, time);
            Rect2 rect;
            if (axis == Axis.C)
            {
                if (p < Top || p > Grid.Y - 2) continue;
                rect = new(Grid.X + lane * (Cell + Gap) + 5, p - 8, Cell - 10, 16);
            }
            else
            {
                if (p < Grid.X + GridSize + 2 || p > Right) continue;
                rect = new(p - 8, Grid.Y + lane * (Cell + Gap) + 5, 16, Cell - 10);
            }
            Round(rect, color, 5);
            if (bits == 7)
            {
                if (axis == Axis.C) DrawLine(rect.Position + new Vector2(10, 5), new(rect.End.X - 10, rect.Position.Y + 5), OnInk, 2, true);
                else DrawLine(rect.Position + new Vector2(5, 10), new(rect.Position.X + 5, rect.End.Y - 10), OnInk, 2, true);
            }
        }
    }

    private Rect2 KeyRect(int key)
    {
        int row = 2 - (key - 1) / 3, col = (key - 1) % 3;
        return new(Grid + new Vector2(col * (Cell + Gap), row * (Cell + Gap)), new(Cell, Cell));
    }

    private void DrawGrid()
    {
        long exampleSongUs = IsExample ? SongUs : 0;
        for (int key = 1; key <= 9; key++)
        {
            Color color = Mixed(ColorBit(key));
            Rect2 rect = KeyRect(key);
            float lit = _session?.IsDown(key) == true ? 1f : 0f;
            if (IsExample && lit == 0f && _exampleFadeStartUs[key] != long.MinValue)
            {
                float fadeProgress = (exampleSongUs - _exampleFadeStartUs[key]) / (float)ExampleKeyFadeUs;
                float remaining = 1f - Math.Clamp(fadeProgress, 0f, 1f);
                lit = remaining * remaining;
            }

            Round(rect, Tint(color, KeyFillTint).Lerp(color, lit), KeyCornerRadius,
                Tint(color, _invertColors ? RgbKeyBorderTint : 0.66f).Lerp(color, lit));
            Center(key.ToString(), rect, 27, KeyLabel(color, 0.3f).Lerp(OnInk, lit), true);
            if (lit < 1f)
            {
                color.A *= 1f - lit;
                DrawLine(new(rect.Position.X + 21, rect.End.Y - 14), new(rect.End.X - 21, rect.End.Y - 14), color, 3, true);
            }
        }
    }

    private void DrawPositionGuide(PlaySession session)
    {
        const float startInset = 8;
        long leadUs = _positionGuideMs * 1000L;
        long now = SongUs;
        long previewEnd = now + leadUs;
        var upcoming = session.States.Where(s => !s.Head.HasValue && s.Note.StartUs < previewEnd)
            .OrderByDescending(s => s.Note.StartUs);
        foreach (var state in upcoming)
        {
            NoteEvent note = state.Note;
            float progress = 1f - Math.Clamp((note.StartUs - now) / (float)leadUs, 0f, 1f);
            float inset = startInset * (1f - progress);
            float alpha = progress * progress * (3f - 2f * progress);
            int radius = KeyCornerRadius - (int)MathF.Round(inset * 0.5f);
            Color border = new(Mixed(ColorBit(note.Key)), alpha);
            Round(KeyRect(note.Key).Grow(-inset), Colors.Transparent, radius, border, borderWidth: 2);
        }
    }

    private void DrawHitFlashes()
    {
        foreach (var flash in _flashes)
        {
            float t = (float)(_age - flash.At);
            float alpha = Math.Max(0, 1 - t / 0.45f);
            if (alpha <= 0) continue;
            Color color = Mixed(ColorBit(flash.Key)); color.A = alpha;
            int lane = flash.Axis == Axis.C ? (flash.Key - 1) % 3 : 2 - (flash.Key - 1) / 3;
            Vector2 center = flash.Axis == Axis.C
                ? new(Grid.X + lane * (Cell + Gap) + Cell / 2, JudgeY)
                : new(JudgeX, Grid.Y + lane * (Cell + Gap) + Cell / 2);
            if (flash.Grade != Grade.Miss)
            {
                DrawCircle(center, 7 + t * 50, new Color(color, alpha * 0.12f));
                DrawArc(center, 11 + t * 45, 0, Mathf.Tau, 32, color, 2, true);
                if (!IsExample) DrawRect(KeyRect(flash.Key).Grow(t * 13), color, false, 2);
            }
            else if (!IsExample)
            {
                Color miss = new(Red, alpha * 0.5f);
                DrawLine(center + new Vector2(-6, -6), center + new Vector2(6, 6), miss, 2, true);
                DrawLine(center + new Vector2(-6, 6), center + new Vector2(6, -6), miss, 2, true);
            }
        }
    }

    private void DrawPause()
    {
        _buttons.Clear();
        Color overlay = _invertColors ? new(Colors.Black, 0.62f) : new(0.14f, 0.17f, 0.22f, 0.54f);
        DrawRect(new(Vector2.Zero, GetViewportRect().Size), overlay);
        var panel = new Rect2(W / 2 - 230, H / 2 - 160, 460, 320);
        Round(panel, Paper, 22);
        Center("일시정지", new(panel.Position.X, panel.Position.Y + 28, 460, 53), 30, Ink, true);
        Button("resume", "계속 플레이", new(panel.Position.X + 38, panel.Position.Y + 111, 384, 56), true);
        Button("restart", "다시 시작", new(panel.Position.X + 38, panel.Position.Y + 183, 384, 48), size: 16);
        Button("back", "곡 선택", new(panel.Position.X + 38, panel.Position.Y + 247, 384, 48), size: 16);
    }

    private void DrawResults()
    {
        if (_session is null) return;
        DrawHeader(_playMode == PlayMode.Auto ? "자동 연주 결과" : "플레이 결과");
        Text("플레이 완료", 104, 207, 43, Ink, true);
        Text(_session.Chart.Title + "  /  " + _session.Chart.Difficulty, 107, 249, 20, Muted);
        Round(new(104, 288, W - 208, 352), Surface, 20, Line);
        Text("TOTAL SCORE", 146, 339, 12, Muted, true);
        Number(_session.Score.Score.ToString("N0"), 140, 442, 83);
        Text("최대 콤보", 151, 508, 15, Muted);
        Number(_session.Score.MaxCombo.ToString(), 147, 571, 43);
        ScoreState score = _session.Score;
        if (score.Judged > 0 && score.Counts[Grade.Miss] == 0)
        {
            var (label, grade) = score.Counts[Grade.Colorful] == score.Judged ? ("AC  ·  ALL COLOR", Grade.Colorful)
                : score.Counts[Grade.Good] == 0 ? ("AP  ·  ALL PERFECT", Grade.Perfect)
                : ("FC  ·  FULL COMBO", Grade.Good);
            var badge = new Rect2(315, 521, 220, 39);
            Round(badge, Tint(GradeColor(grade), 0.91f), 8);
            CenterGradeText(label, grade, badge, 14);
        }
        DrawLine(new(W / 2 + 54, 332), new(W / 2 + 54, 592), Line, 1);
        for (int i = 0; i < GradeOrder.Length; i++)
        {
            Grade grade = GradeOrder[i];
            float y = 364 + i * 63;
            DrawGradeMarker(grade, new(W / 2 + 102, y - 6), 5);
            DrawGradeText(GradeName(grade), grade, new(W / 2 + 125, y), 19);
            Number(score.Counts[grade].ToString(), W - 228, y + 3, 29);
        }
        Button("restart", "한 번 더 플레이  →", new(104, 690, (W - 238) / 2, 65), true, 20);
        Button("back", "곡 선택으로", new(W / 2 + 15, 690, (W - 238) / 2, 65), size: 19);
    }
}
