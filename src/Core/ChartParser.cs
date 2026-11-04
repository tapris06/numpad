using System.Globalization;
using System.Text;

namespace NumpadNine.Core;

public static class ChartParser
{
    private readonly record struct Position(int Line, int Column);

    public static Chart Parse(string text)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = new StringBuilder();
        var positions = new List<Position>();
        bool inBody = false;
        var lines = text.TrimStart('\uFEFF').Replace("\r", "").Split('\n');
        for (int line = 0; line < lines.Length; line++)
        {
            string raw = lines[line];
            if (!inBody)
            {
                string value = raw.Trim();
                if (value.Length == 0) continue;
                int equals = value.IndexOf('=');
                if (!value.StartsWith('&') || equals < 2)
                    throw new ChartException("&항목=값 형식의 헤더가 필요합니다.", line + 1, 1);
                string name = value[1..equals];
                if (name is not ("format" or "title" or "artist" or "difficulty" or "audio" or "first" or "inote"))
                    throw new ChartException($"알 수 없는 항목: {name}", line + 1, 1);
                if (!headers.TryAdd(name, value[(equals + 1)..].Trim()))
                    throw new ChartException($"항목 중복: {name}", line + 1, 1);
                if (name == "inote")
                {
                    if (headers[name].Length > 0)
                        throw new ChartException("본문은 &inote= 다음 줄부터 작성하세요.", line + 1, equals + 2);
                    inBody = true;
                }
                continue;
            }
            int comment = raw.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) raw = raw[..comment];
            for (int col = 0; col < raw.Length; col++)
                if (!char.IsWhiteSpace(raw[col])) { body.Append(raw[col]); positions.Add(new(line + 1, col + 1)); }
        }

        string Required(string name) => headers.TryGetValue(name, out var value) && value.Length > 0
            ? value : throw new ChartException($"필수 항목 누락: {name}");
        if (Required("format") != "numpad-chart-v1")
            throw new ChartException("지원하지 않는 채보 형식입니다.");
        string title = Required("title"), audio = Required("audio");
        if (!decimal.TryParse(Required("first"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal first))
            throw new ChartException("first는 초 단위 숫자로 작성하세요.");
        if (!inBody) throw new ChartException("&inote= 항목이 필요합니다.");
        var parser = new BodyParser(body.ToString(), positions, first);
        var (notes, end) = parser.Run();
        var chart = new Chart(title, headers.GetValueOrDefault("artist", ""),
            headers.GetValueOrDefault("difficulty", ""), audio, ToUs(first), end, notes);
        ChartValidator.Validate(chart);
        return chart;
    }

    private static long ToUs(decimal seconds)
    {
        try { return checked((long)decimal.Round(seconds * 1_000_000m, 0, MidpointRounding.AwayFromZero)); }
        catch (OverflowException) { throw new ChartException("채보의 시간 값이 너무 큽니다."); }
    }

    private sealed class BodyParser(string body, List<Position> positions, decimal first)
    {
        private int _at;
        private decimal _cursor = first;
        private decimal _bpm;
        private decimal _divider;
        private decimal? _fixedStep;
        private bool _hasNotes;
        private bool _needsNote;
        private readonly List<NoteEvent> _notes = [];

        private ChartException Error(string message)
        {
            var position = positions.Count > 0 ? positions[Math.Min(_at, positions.Count - 1)] : new Position(1, 1);
            return new(message, position.Line, position.Column);
        }

        private string Delimited(char closing)
        {
            int start = ++_at;
            while (_at < body.Length && body[_at] != closing) _at++;
            if (_at == body.Length) throw Error($"닫는 기호 {closing}가 필요합니다.");
            string value = body[start.._at];
            _at++;
            return value;
        }

        private decimal Positive(string value, bool integer = false)
        {
            string pattern = integer ? @"^[0-9]+$" : @"^[0-9]+(?:\.[0-9]+)?$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, pattern)
                || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
                || result <= 0 || (integer && decimal.Truncate(result) != result))
                throw Error(integer ? "양의 정수가 필요합니다." : "양수가 필요합니다.");
            return result;
        }

        private decimal Length(string value)
        {
            if (value.StartsWith('#')) return Positive(value[1..]);
            decimal bpm = _bpm;
            int hash = value.IndexOf('#');
            if (hash >= 0) { bpm = Positive(value[..hash]); value = value[(hash + 1)..]; }
            var pair = value.Split(':');
            if (pair.Length != 2 || bpm <= 0) throw Error("롱노트 길이는 [분할:개수], [#초], [BPM#분할:개수]로 작성하세요.");
            return 240m / bpm / Positive(pair[0], true) * Positive(pair[1], true);
        }

        public (IReadOnlyList<NoteEvent> Notes, long End) Run()
        {
            while (_at < body.Length)
            {
                char token = body[_at];
                if (token is '(' or '{')
                {
                    if (_hasNotes || _needsNote) throw Error("설정은 칸의 노트 앞에 작성하세요.");
                    if (token == '(') _bpm = Positive(Delimited(')'));
                    else
                    {
                        if (_bpm <= 0) throw Error("BPM을 분할보다 먼저 선언하세요.");
                        string value = Delimited('}');
                        if (value.StartsWith('#')) _fixedStep = Positive(value[1..]);
                        else { _divider = Positive(value, true); _fixedStep = null; }
                    }
                    continue;
                }
                if (token == ',')
                {
                    if (_needsNote) throw Error("/ 뒤에 노트가 필요합니다.");
                    if (_bpm <= 0 || (_fixedStep is null && _divider <= 0)) throw Error("BPM과 칸 분할을 먼저 선언하세요.");
                    _cursor += _fixedStep ?? 240m / _bpm / _divider;
                    _hasNotes = false;
                    _at++;
                    continue;
                }
                if (token == '/')
                {
                    if (!_hasNotes || _needsNote) throw Error("/ 앞뒤에는 노트가 필요합니다.");
                    _needsNote = true;
                    _at++;
                    continue;
                }
                if (token == 'E')
                {
                    if (_hasNotes || _needsNote) throw Error("마지막 노트 뒤에 쉼표를 작성하세요.");
                    if (++_at != body.Length) throw Error("E 뒤에는 노트를 기록할 수 없습니다.");
                    return (_notes, ToUs(_cursor));
                }
                if (token is >= '1' and <= '9')
                {
                    if (_hasNotes && !_needsNote) throw Error("동시 노트는 /로 구분하세요.");
                    if (_bpm <= 0 || (_fixedStep is null && _divider <= 0)) throw Error("BPM과 칸 분할을 먼저 선언하세요.");
                    int start = _at;
                    while (_at < body.Length && body[_at] is >= '1' and <= '9') _at++;
                    string keys = body[start.._at];
                    if (_at >= body.Length || body[_at] is not ('R' or 'C')) throw Error("버튼 뒤에 방향 R 또는 C가 필요합니다.");
                    Axis axis = body[_at++] == 'C' ? Axis.C : Axis.R;
                    long? endUs = null;
                    if (_at < body.Length && body[_at] == 'h')
                    {
                        if (keys.Length != 1) throw Error("롱노트는 각 버튼을 /로 구분해 작성하세요.");
                        if (++_at >= body.Length || body[_at] != '[') throw Error("롱노트에 [길이]가 필요합니다.");
                        endUs = ToUs(_cursor + Length(Delimited(']')));
                    }
                    foreach (char key in keys) _notes.Add(new(_notes.Count, key - '0', axis, ToUs(_cursor), endUs));
                    _hasNotes = true;
                    _needsNote = false;
                    continue;
                }
                throw Error($"알 수 없는 기호: {token}");
            }
            throw Error("채보 종료 기호 E가 필요합니다.");
        }
    }
}
