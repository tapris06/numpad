using Godot;
using NumpadNine.Core;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace NumpadNine.Game;

public partial class GameRoot : Node2D
{
    private enum Screen { Select, Playing, Paused, Results }
    private enum PlayMode { Manual, Auto, Example }
    private sealed record Song(Chart Chart, string Source);
    private sealed record ReplayInput(long TimeUs, int Key, bool Down, bool IsTap);
    private sealed record HitFlash(int Key, Axis Axis, Grade Grade, long ErrorUs, double At);
    private readonly List<Song> _songs = [];
    private readonly Dictionary<string, Rect2> _buttons = [];
    private readonly List<HitFlash> _flashes = [];
    private readonly List<ReplayInput> _replay = [];
    private readonly long[] _exampleFadeStartUs = new long[10];
    private readonly HashSet<string> _captured = [];
    private const int MinSpeedTenths = 5, MaxSpeedTenths = 50;
    private const int MaxSongOffsetMs = 500;
    private const int PositionGuideStepMs = 100, DefaultPositionGuideMs = 500, MaxPositionGuideMs = 1000;
    private const int SongListRows = 4;
    private const long ExampleTapFlashUs = 90_000;
    private const long ExampleKeyFadeUs = 100_000;
    private static readonly string[] BuiltInCharts = [
        "res://content/charts/first-circuit-easy.npd",
        "res://content/charts/first-circuit-normal.npd",
        "res://content/charts/first-circuit-hard.npd",
        "res://content/charts/long-wave-easy.npd",
        "res://content/charts/long-wave-normal.npd",
        "res://content/charts/long-wave-hard.npd",
        "res://content/charts/spark-rush-easy.npd",
        "res://content/charts/spark-rush-normal.npd",
        "res://content/charts/spark-rush-hard.npd"
    ];
    private static int BuiltInSongCount => BuiltInCharts.Length;
    private const double BaseScrollPixelsPerSecond = 174.0;
    private readonly int[] _fpsOptions = [60, 120, 180, 240];
    private Screen _screen;
    private PlayMode _playMode;
    private int _selected, _songListStart, _speedTenths = 10, _offsetMs, _songOffsetMs, _replayIndex, _fpsIndex = 3;
    private float _volume = 0.7f;
    private AudioStreamPlayer _music = null!;
    private FileDialog _picker = null!;
    private PlaySession? _session;
    private long _originTick, _audioZeroTick, _audioOutputLatencyUs, _playEndUs, _pauseTick, _pausedSongUs;
    private long _lastSongUs = long.MinValue;
    private bool _audioStarted, _musicFinished, _testExit, _captureBusy, _debug, _invertColors;
    private bool _positionGuide;
    private int _positionGuideMs = DefaultPositionGuideMs;
    private string _error = "", _captureDir = "";
    private double _age, _lastJudgmentAt = -10;
    private Grade? _lastGrade;
    private long? _lastError;
    private Vector2 _mouse;
    private double _songLength;
    private readonly ConfigFile _settings = new();
    private long Tick => checked((long)Time.GetTicksUsec());
    private long SongUs => _screen == Screen.Paused ? _pausedSongUs
        : _lastSongUs = Math.Max(_lastSongUs, Tick - _originTick);
    private bool IsAutomatic => _playMode != PlayMode.Manual;
    private bool IsExample => _playMode == PlayMode.Example;
    private long JudgeUs => SongUs + (IsAutomatic ? 0 : _offsetMs * 1000L);
    private double SpeedMultiplier => _speedTenths / 10.0;
    private double ScrollPixelsPerSecond => BaseScrollPixelsPerSecond * SpeedMultiplier;
    private double ApproachSeconds => LaneLength / ScrollPixelsPerSecond;

    public override void _Ready()
    {
        InitDrawing();
        _music = new AudioStreamPlayer { Name = "Music" };
        AddChild(_music);
        _music.Finished += () => _musicFinished = true;
        LoadSettings();
        _picker = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Filters = ["*.npd ; Numpad Nine 채보"],
            UseNativeDialog = true,
            Title = "채보 열기",
            ModeOverridesTitle = false
        };
        AddChild(_picker);
        _picker.FileSelected += path =>
        {
            if (LoadSong(path)) SelectSong(_songs.Count - 1);
            QueueRedraw();
        };
        foreach (string path in BuiltInCharts) LoadSong(path);

        string? requested = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--autoplay") _playMode = PlayMode.Auto;
            else if (arg == "--example-play") _playMode = PlayMode.Example;
            else if (arg == "--input-audit") _debug = true;
            else if (arg == "--test-exit") _testExit = true;
            else if (arg.StartsWith("--capture-dir=")) _captureDir = arg[14..];
            else if (arg.StartsWith("--chart=")) requested = arg[8..];
            else if (arg == "--normal") _selected = 1;
            else if (arg == "--hard") _selected = 2;
        }
        if (requested is not null && LoadSong(requested)) _selected = _songs.Count - 1;
        SelectSong(_selected);
        if (IsAutomatic && _songs.Count > 0) CallDeferred(MethodName.BeginPlay);
    }

    private bool LoadSong(string path)
    {
        try
        {
            string text = FileAccess.GetFileAsString(path);
            if (text.Length == 0) throw new ChartException("채보 파일을 읽을 수 없습니다.");
            var song = new Song(ChartParser.Parse(text), path);
            if (_songs.Count > BuiltInSongCount) _songs[BuiltInSongCount] = song;
            else _songs.Add(song);
            _error = "";
            return true;
        }
        catch (Exception e) { _error = e.Message; return false; }
    }

    private void SelectSong(int index)
    {
        if (_songs.Count == 0) return;
        _selected = Math.Clamp(index, 0, _songs.Count - 1);
        if (_selected < _songListStart) _songListStart = _selected;
        else if (_selected >= _songListStart + SongListRows) _songListStart = _selected - SongListRows + 1;
        _songListStart = Math.Clamp(_songListStart, 0, Math.Max(0, _songs.Count - SongListRows));
    }

    private void ScrollSongList(int delta)
    {
        if (_songs.Count == 0) return;
        _songListStart = Math.Clamp(_songListStart + delta, 0, Math.Max(0, _songs.Count - SongListRows));
        _selected = Math.Clamp(_selected, _songListStart, Math.Min(_songs.Count - 1, _songListStart + SongListRows - 1));
    }

    private static string AudioPath(Song song)
    {
        string basePath = song.Source[..song.Source.LastIndexOfAny(['/', '\\'])];
        if (!basePath.StartsWith("res://")) return System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, song.Chart.Audio));
        var parts = new List<string>();
        foreach (string part in (basePath[6..] + "/" + song.Chart.Audio.Replace('\\', '/')).Split('/'))
        {
            if (part is "." or "") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return "res://" + string.Join('/', parts);
    }

    private static AudioStream? LoadAudio(string path)
    {
        if (path.StartsWith("res://")) return ResourceLoader.Load<AudioStream>(path);
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => AudioStreamWav.LoadFromFile(path),
            ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
            ".mp3" => new AudioStreamMP3 { Data = FileAccess.GetFileAsBytes(path) },
            _ => null
        };
    }

    private void BeginPlay()
    {
        if (_songs.Count == 0) return;
        try
        {
            _music.Stop();
            _music.StreamPaused = false;
            _music.Stream = LoadAudio(AudioPath(_songs[_selected]));
            if (_music.Stream is null) throw new Exception("음원을 읽을 수 없습니다. WAV, OGG, MP3 경로를 확인하세요.");
            _songLength = _music.Stream.GetLength();
            if (_songLength * 1_000_000 < _songs[_selected].Chart.EndUs)
                throw new Exception("음원 길이가 채보의 종료 시점보다 짧습니다.");
            _session = new(_songs[_selected].Chart);
            _audioStarted = _musicFinished = false;
            long preroll = Math.Max(2_000_000L, (long)Math.Ceiling(ApproachSeconds * 1_000_000))
                + Math.Max(0L, -_session.Chart.FirstUs);
            // Keep the chart clock fixed; only the audible music start moves with the song offset.
            _originTick = Tick + preroll;
            _audioZeroTick = _originTick + _songOffsetMs * 1000L;
            _audioOutputLatencyUs = (long)(AudioServer.GetOutputLatency() * 1_000_000);
            _playEndUs = Math.Max(_session.Chart.EndUs,
                (long)Math.Ceiling(_songLength * 1_000_000) + _songOffsetMs * 1000L);
            _lastSongUs = long.MinValue;
            _replay.Clear();
            foreach (var note in _session.Chart.Notes)
            {
                _replay.Add(new(note.StartUs, note.Key, true, !note.IsHold));
                _replay.Add(new(note.EndUs ?? note.StartUs + 1, note.Key, false, !note.IsHold));
            }
            _replay.Sort((a, b) => a.TimeUs != b.TimeUs ? a.TimeUs.CompareTo(b.TimeUs) : a.Down.CompareTo(b.Down));
            _replayIndex = 0;
            Array.Fill(_exampleFadeStartUs, long.MinValue);
            _flashes.Clear();
            _lastGrade = null;
            _lastJudgmentAt = -10;
            _screen = Screen.Playing;
            _error = "";
        }
        catch (Exception e)
        {
            _error = e.Message;
            _screen = Screen.Select;
            if (_testExit) { GD.PrintErr(e); GetTree().Quit(1); }
        }
    }

    public override void _Process(double delta)
    {
        _age += delta;
        _mouse = GetGlobalMousePosition();
        if (_screen == Screen.Playing && _session is not null)
        {
            if (!_audioStarted)
            {
                long audibleTick = Tick + (long)(AudioServer.GetTimeToNextMix() * 1_000_000) + _audioOutputLatencyUs;
                if (audibleTick >= _audioZeroTick)
                {
                    // Account for the frame's scheduling delay without moving the chart clock.
                    _music.Play((float)((audibleTick - _audioZeroTick) / 1e6));
                    _audioStarted = true;
                }
            }
            long now = Math.Max(_session.TimeUs, JudgeUs);
            if (IsAutomatic)
            {
                while (_replayIndex < _replay.Count && _replay[_replayIndex].TimeUs <= now)
                {
                    long at = _replay[_replayIndex].TimeUs;
                    var downs = new List<int>();
                    while (_replayIndex < _replay.Count && _replay[_replayIndex].TimeUs == at)
                    {
                        var input = _replay[_replayIndex++];
                        if (input.Down)
                        {
                            downs.Add(input.Key);
                            if (IsExample && input.IsTap)
                                _exampleFadeStartUs[input.Key] = at + ExampleTapFlashUs;
                        }
                        else
                        {
                            _session.Release(input.Key, at);
                            if (IsExample && !input.IsTap)
                                _exampleFadeStartUs[input.Key] = at;
                        }
                    }
                    if (downs.Count > 0) _session.PressKeys(downs, at);
                }
            }
            _session.AdvanceTo(now);
            CollectJudgments();
            if (_audioStarted && _musicFinished && SongUs >= _playEndUs && _session.Complete)
                FinishPlay();
            if (SongUs > 4_000_000) RequestCapture("play");
            if (SongUs > 11_000_000) RequestCapture("holds");
        }
        else if (_screen == Screen.Select && _age > 0.5) RequestCapture("select");
        _flashes.RemoveAll(f => _age - f.At > 0.65);
        // Canvas redraw callbacks can allocate OpenGL buffers even when Godot skips presenting frames.
        if (RenderingServer.RenderLoopEnabled && DisplayServer.WindowCanDraw()) QueueRedraw();
    }

    private void CollectJudgments()
    {
        if (_session is null) return;
        foreach (var judgment in _session.DrainJudgments())
        {
            _flashes.Add(new(judgment.Key, judgment.Axis, judgment.Grade, judgment.ErrorUs ?? 0, _age));
            _lastGrade = judgment.Grade;
            _lastError = judgment.ErrorUs;
            _lastJudgmentAt = _age;
        }
    }

    private void FinishPlay()
    {
        _screen = IsExample ? Screen.Select : Screen.Results;
        _session!.ReleaseAll(Math.Max(_session.TimeUs, JudgeUs));
        if (!IsExample) RequestCapture("results");
        if (_testExit)
        {
            var score = _session.Score;
            var result = new
            {
                title = _session.Chart.Title,
                difficulty = _session.Chart.Difficulty,
                score = score.Score,
                maxCombo = score.MaxCombo,
                judged = score.Judged,
                weight = score.Weight,
                counts = score.Counts.ToDictionary(k => k.Key.ToString(), v => v.Value),
                complete = _session.Complete
            };
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            GD.Print("PLAY_RESULT " + JsonSerializer.Serialize(result));
            if (_captureDir.Length > 0)
            {
                System.IO.Directory.CreateDirectory(_captureDir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(_captureDir, "result.json"), json);
            }
            ExitAfterCapture();
        }
        else if (IsExample) ReturnToSelect();
    }

    private async void ExitAfterCapture()
    {
        await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit(IsAutomatic && _session!.Score.Score != 1_010_000 ? 1 : 0);
    }

    private void PausePlay()
    {
        if (_screen != Screen.Playing || _session is null) return;
        _pausedSongUs = SongUs;
        _pauseTick = Tick;
        if (!IsExample) _session.ReleaseAll(Math.Max(_session.TimeUs, JudgeUs));
        _music.StreamPaused = true;
        _screen = Screen.Paused;
    }

    private void ResumePlay()
    {
        if (_screen != Screen.Paused) return;
        long pauseLength = Tick - _pauseTick;
        _originTick += pauseLength;
        _audioZeroTick += pauseLength;
        _music.StreamPaused = false;
        _screen = Screen.Playing;
    }

    private void ReturnToSelect()
    {
        _music.Stop();
        _screen = Screen.Select;
        _playMode = PlayMode.Manual;
        SaveSettings();
    }

    private static int NumpadKey(InputEventKey input)
    {
        foreach (Key key in new[] { input.PhysicalKeycode, input.Keycode })
            if (key >= Key.Kp1 && key <= Key.Kp9) return (int)(key - Key.Kp0);
        return 0;
    }

    public override void _Input(InputEvent input)
    {
        if (_debug && input is InputEventKey audit && !audit.Echo)
            GD.Print($"INPUT physical={audit.PhysicalKeycode} logical={audit.Keycode} key={NumpadKey(audit)} down={audit.Pressed}");
        if (input is InputEventMouseButton mouse && mouse.Pressed)
        {
            if (_screen == Screen.Select && !_picker.Visible
                && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                ScrollSongList(mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
                return;
            }
            if (mouse.ButtonIndex == MouseButton.Left)
                foreach (var button in _buttons.Reverse())
                    if (button.Value.HasPoint(mouse.Position)) { Activate(button.Key); break; }
        }
        if (input is not InputEventKey key || key.Echo) return;
        if (_screen == Screen.Playing && !IsAutomatic && _session is not null)
        {
            int number = NumpadKey(key);
            if (number > 0)
            {
                long at = Math.Max(_session.TimeUs, JudgeUs);
                if (key.Pressed) _session.Press(number, at); else _session.Release(number, at);
                CollectJudgments();
                return;
            }
        }
        if (!key.Pressed || _picker.Visible) return;
        switch (key.Keycode)
        {
            case Key.Enter:
            case Key.KpEnter:
                if (_screen == Screen.Select) Activate("play");
                else if (_screen == Screen.Results) BeginPlay();
                else if (_screen == Screen.Paused) ResumePlay();
                break;
            case Key.Escape:
                if (_screen == Screen.Playing) PausePlay();
                else if (_screen == Screen.Paused) ResumePlay();
                else if (_screen == Screen.Results) ReturnToSelect();
                break;
            case Key.F2: if (_screen != Screen.Select) BeginPlay(); break;
            case Key.F3: _debug = !_debug; break;
            case Key.Up: if (_screen == Screen.Select && _songs.Count > 0) SelectSong((_selected + _songs.Count - 1) % _songs.Count); break;
            case Key.Down: if (_screen == Screen.Select && _songs.Count > 0) SelectSong((_selected + 1) % _songs.Count); break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && _playMode != PlayMode.Auto) PausePlay();
    }

    private void Activate(string action)
    {
        if (action.StartsWith("song:")) { SelectSong(int.Parse(action[5..])); return; }
        if (action.StartsWith("fps:"))
        {
            int index = Array.IndexOf(_fpsOptions, int.Parse(action[4..]));
            if (index >= 0) { _fpsIndex = index; Engine.MaxFps = _fpsOptions[index]; SaveSettings(); }
            return;
        }
        switch (action)
        {
            case "play": _playMode = PlayMode.Manual; BeginPlay(); break;
            case "example": _playMode = PlayMode.Example; BeginPlay(); break;
            case "pause": PausePlay(); break;
            case "resume": ResumePlay(); break;
            case "restart": BeginPlay(); break;
            case "back": ReturnToSelect(); break;
            case "open": _picker.PopupCentered(new Vector2I(940, 650)); break;
            case "songs-up": ScrollSongList(-1); break;
            case "songs-down": ScrollSongList(1); break;
            case "invert-colors": _invertColors = !_invertColors; SaveSettings(); break;
            case "position-guide": _positionGuide = !_positionGuide; SaveSettings(); break;
            case "position-guide-time-" when _positionGuide:
                _positionGuideMs = Math.Max(PositionGuideStepMs, _positionGuideMs - PositionGuideStepMs);
                SaveSettings();
                break;
            case "position-guide-time+" when _positionGuide:
                _positionGuideMs = Math.Min(MaxPositionGuideMs, _positionGuideMs + PositionGuideStepMs);
                SaveSettings();
                break;
            case "speed-": _speedTenths = Math.Max(MinSpeedTenths, _speedTenths - 1); SaveSettings(); break;
            case "speed+": _speedTenths = Math.Min(MaxSpeedTenths, _speedTenths + 1); SaveSettings(); break;
            case "offset-": _offsetMs = Math.Max(-150, _offsetMs - 5); SaveSettings(); break;
            case "offset+": _offsetMs = Math.Min(150, _offsetMs + 5); SaveSettings(); break;
            case "song-offset-": _songOffsetMs = Math.Max(-MaxSongOffsetMs, _songOffsetMs - 5); SaveSettings(); break;
            case "song-offset+": _songOffsetMs = Math.Min(MaxSongOffsetMs, _songOffsetMs + 5); SaveSettings(); break;
            case "volume-": _volume = Math.Max(0, _volume - 0.1f); SetVolume(); SaveSettings(); break;
            case "volume+": _volume = Math.Min(1, _volume + 0.1f); SetVolume(); SaveSettings(); break;
        }
    }

    private void SetVolume() => _music.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, _volume));

    private void LoadSettings()
    {
        if (_settings.Load("user://settings.cfg") == Error.Ok)
        {
            if (_settings.HasSectionKey("play", "scroll_speed_tenths"))
                _speedTenths = Math.Clamp(_settings.GetValue("play", "scroll_speed_tenths").AsInt32(), MinSpeedTenths, MaxSpeedTenths);
            else
                _speedTenths = _settings.GetValue("play", "speed", 1).AsInt32() switch { 0 => 7, 2 => 14, _ => 10 };
            _offsetMs = Math.Clamp(_settings.GetValue("play", "offset", 0).AsInt32(), -150, 150);
            _songOffsetMs = Math.Clamp(_settings.GetValue("play", "song_offset", 0).AsInt32(), -MaxSongOffsetMs, MaxSongOffsetMs);
            _volume = Math.Clamp((float)_settings.GetValue("play", "volume", 0.7).AsDouble(), 0, 1);
            _invertColors = _settings.GetValue("play", "invert_colors", false).AsBool();
            _positionGuide = _settings.GetValue("play", "position_guide", false).AsBool();
            int savedGuideMs = _settings.GetValue("play", "position_guide_ms", DefaultPositionGuideMs).AsInt32();
            _positionGuideMs = Math.Clamp(savedGuideMs / PositionGuideStepMs * PositionGuideStepMs,
                PositionGuideStepMs, MaxPositionGuideMs);
            int savedFps = _settings.GetValue("play", "fps", 240).AsInt32();
            int savedIndex = Array.IndexOf(_fpsOptions, savedFps);
            _fpsIndex = savedIndex >= 0 ? savedIndex : 3;
        }
        Engine.MaxFps = _fpsOptions[_fpsIndex];
        SetVolume();
    }

    private void SaveSettings()
    {
        _settings.SetValue("play", "scroll_speed_tenths", _speedTenths);
        _settings.SetValue("play", "offset", _offsetMs);
        _settings.SetValue("play", "song_offset", _songOffsetMs);
        _settings.SetValue("play", "volume", _volume);
        _settings.SetValue("play", "invert_colors", _invertColors);
        _settings.SetValue("play", "position_guide", _positionGuide);
        _settings.SetValue("play", "position_guide_ms", _positionGuideMs);
        _settings.SetValue("play", "fps", _fpsOptions[_fpsIndex]);
        _settings.Save("user://settings.cfg");
    }

    private void RequestCapture(string name)
    {
        if (_captureDir.Length == 0 || _captureBusy || _captured.Contains(name)) return;
        _captureBusy = true;
        _captured.Add(name);
        CaptureAfterFrame(name);
    }

    private async void CaptureAfterFrame(string name)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            var image = GetViewport().GetTexture().GetImage();
            if (image is not null && !image.IsEmpty())
            {
                System.IO.Directory.CreateDirectory(_captureDir);
                image.SavePng(System.IO.Path.Combine(_captureDir, name + ".png"));
            }
        }
        catch (Exception e) { GD.PrintErr("Capture: " + e.Message); }
        _captureBusy = false;
    }
}
