namespace NumpadNine.Core;

public sealed record Judgment(int NoteId, int Key, Axis Axis, JudgmentPart Part,
    Grade Grade, long TimeUs, long? ErrorUs = null, double? SustainRatio = null);

public sealed class NoteState(NoteEvent note)
{
    public NoteEvent Note { get; } = note;
    public Grade? Head { get; internal set; }
    public long? HitUs { get; internal set; }
    public Grade? Sustain { get; internal set; }
    public double? SustainRatio { get; internal set; }
}

public sealed class ScoreState(long weight)
{
    private long? _batchTime;
    private int _batchBase;
    private int _batchSuccess;
    private bool _batchMiss;
    private int _settledMax;
    public long Weight { get; } = weight > 0 ? weight : throw new ArgumentOutOfRangeException(nameof(weight));
    public long Numerator { get; private set; }
    public long Score => Numerator / Weight;
    public int Combo { get; private set; }
    public int MaxCombo => Math.Max(_settledMax, Combo);
    public int Judged { get; private set; }
    public IReadOnlyDictionary<Grade, int> Counts => _counts;
    private readonly Dictionary<Grade, int> _counts = Enum.GetValues<Grade>().ToDictionary(g => g, _ => 0);

    public void Apply(Judgment judgment)
    {
        if (_batchTime is not null && judgment.TimeUs < _batchTime)
            throw new InvalidOperationException("판정은 확정 시각 순서로 적용해야 합니다.");
        if (_batchTime != judgment.TimeUs)
        {
            _settledMax = Math.Max(_settledMax, Combo);
            _batchBase = Combo;
            _batchSuccess = 0;
            _batchMiss = false;
            _batchTime = judgment.TimeUs;
        }
        _counts[judgment.Grade]++;
        Judged++;
        Numerator = checked(Numerator + Rules.Points(judgment.Grade)
            * (judgment.Part == JudgmentPart.Sustain ? 2 : 1));
        if (judgment.Grade == Grade.Miss) _batchMiss = true;
        else _batchSuccess++;
        Combo = (_batchMiss ? 0 : _batchBase) + _batchSuccess;
    }
}

public sealed class PlaySession
{
    private readonly List<(long Start, long End)>[] _intervals = Enumerable.Range(0, 10)
        .Select(_ => new List<(long, long)>()).ToArray();
    private readonly long?[] _downSince = new long?[10];
    private readonly NoteState[][] _byKey;
    private readonly long[] _deadlines;
    private readonly List<Judgment> _outbox = [];
    private int _deadlineIndex;
    private long _time = long.MinValue;

    public Chart Chart { get; }
    public IReadOnlyList<NoteState> States { get; }
    public ScoreState Score { get; }
    public bool Complete => States.All(n => n.Head.HasValue && (!n.Note.IsHold || n.Sustain.HasValue));
    public long TimeUs => _time;
    public bool IsDown(int key) => key is >= 1 and <= 9 && _downSince[key].HasValue;

    public PlaySession(Chart chart)
    {
        ChartValidator.Validate(chart);
        Chart = chart;
        States = chart.Notes.Select(n => new NoteState(n)).ToArray();
        _byKey = Enumerable.Range(0, 10).Select(key => States.Where(s => s.Note.Key == key)
            .OrderBy(s => s.Note.StartUs).ThenBy(s => s.Note.Id).ToArray()).ToArray();
        _deadlines = chart.Notes.SelectMany(n => n.IsHold
                ? new[] { n.StartUs + Rules.LateGoodUs + 1, n.EndUs!.Value }
                : new[] { n.StartUs + Rules.LateGoodUs + 1 })
            .Distinct().Order().ToArray();
        Score = new(chart.Weight);
    }

    private void BeginTime(long timeUs, bool inclusive)
    {
        if (timeUs < _time) throw new ArgumentOutOfRangeException(nameof(timeUs), "시간은 뒤로 갈 수 없습니다.");
        while (_deadlineIndex < _deadlines.Length && (inclusive
                   ? _deadlines[_deadlineIndex] <= timeUs : _deadlines[_deadlineIndex] < timeUs))
            ProcessAutomatic(_deadlines[_deadlineIndex++]);
        _time = timeUs;
    }

    private void Emit(Judgment judgment)
    {
        Score.Apply(judgment);
        _outbox.Add(judgment);
    }

    private void JudgeHead(NoteState state, Grade grade, long timeUs, long? errorUs)
    {
        state.Head = grade;
        state.HitUs = timeUs;
        Emit(new(state.Note.Id, state.Note.Key, state.Note.Axis, JudgmentPart.Head, grade, timeUs, errorUs));
    }

    private long HeldUs(int key, long start, long end)
    {
        if (end <= start) return 0;
        long sum = 0;
        foreach (var segment in _intervals[key])
            sum += Math.Max(0, Math.Min(end, segment.End) - Math.Max(start, segment.Start));
        if (_downSince[key] is long down)
            sum += Math.Max(0, end - Math.Max(start, down));
        return Math.Clamp(sum, 0, end - start);
    }

    private void FinishHolds(long timeUs)
    {
        foreach (var state in States.Where(s => s.Note.IsHold && s.Head.HasValue
                     && !s.Sustain.HasValue && s.Note.EndUs <= timeUs))
        {
            long start = state.Head == Grade.Miss ? state.Note.StartUs : state.HitUs!.Value;
            long end = state.Note.EndUs!.Value;
            long duration = end - start;
            long down = HeldUs(state.Note.Key, start, end);
            state.Sustain = Rules.SustainGrade(down, duration);
            state.SustainRatio = duration > 0 ? (double)down / duration : 0;
            Emit(new(state.Note.Id, state.Note.Key, state.Note.Axis, JudgmentPart.Sustain,
                state.Sustain.Value, timeUs, SustainRatio: state.SustainRatio));
        }
    }

    private void ProcessAutomatic(long timeUs)
    {
        foreach (var state in States.Where(s => !s.Head.HasValue
                     && s.Note.StartUs + Rules.LateGoodUs + 1 <= timeUs))
            JudgeHead(state, Grade.Miss, timeUs, null);
        FinishHolds(timeUs);
    }

    public void AdvanceTo(long timeUs) => BeginTime(timeUs, true);

    public void Press(int key, long timeUs) => PressKeys([key], timeUs);

    public void PressKeys(IEnumerable<int> keys, long timeUs)
    {
        BeginTime(timeUs, false);
        ProcessAutomatic(timeUs);
        foreach (int key in keys.Distinct())
        {
            if (key is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(keys));
            if (_downSince[key].HasValue) continue;
            _downSince[key] = timeUs;
            var state = _byKey[key].FirstOrDefault(s => !s.Head.HasValue
                && timeUs - s.Note.StartUs >= Rules.EarlyMissUs);
            if (state is null) continue;
            var grade = Rules.HeadGrade(timeUs - state.Note.StartUs);
            if (grade.HasValue) JudgeHead(state, grade.Value, timeUs, timeUs - state.Note.StartUs);
        }
        FinishHolds(timeUs);
    }

    public void Release(int key, long timeUs)
    {
        if (key is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(key));
        BeginTime(timeUs, false);
        if (_downSince[key] is long down)
        {
            _intervals[key].Add((down, timeUs));
            _downSince[key] = null;
        }
        ProcessAutomatic(timeUs);
    }

    public void ReleaseAll(long timeUs)
    {
        BeginTime(timeUs, false);
        for (int key = 1; key <= 9; key++)
            if (_downSince[key] is long down)
            {
                _intervals[key].Add((down, timeUs));
                _downSince[key] = null;
            }
        ProcessAutomatic(timeUs);
    }

    public IReadOnlyList<Judgment> DrainJudgments()
    {
        var result = _outbox.ToArray();
        _outbox.Clear();
        return result;
    }
}
