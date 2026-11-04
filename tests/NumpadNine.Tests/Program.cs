using System.Text.Json;
using NumpadNine.Core;

int passed = 0, failed = 0, assertions = 0;
void Check(bool condition, string message = "assertion failed") { assertions++; if (!condition) throw new Exception(message); }
void Equal<T>(T expected, T actual) { Check(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}"); }
void Reject(Action action) { bool rejected = false; try { action(); } catch (ChartException) { rejected = true; } Check(rejected, "invalid chart accepted"); }
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e}"); }
}
string Source(string body, string first = "0") => $"&format=numpad-chart-v1\n&title=Test\n&audio=music.wav\n&first={first}\n&inote=\n{body}";
Chart Make(params NoteEvent[] notes) => new("Test", "", "", "music.wav", 0, 20_000_000, notes);
PlaySession Hold() => new(Make(new NoteEvent(0, 6, Axis.C, 1_000_000, 3_000_000)));
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();

Test("head timing boundaries favor better grades", () =>
{
    (long Time, Grade? Grade)[] cases = [
        (-120001, null), (-120000, Grade.Miss), (-90001, Grade.Miss), (-90000, Grade.Good),
        (-60001, Grade.Good), (-60000, Grade.Perfect), (-30001, Grade.Perfect),
        (-30000, Grade.DetailedPerfect), (0, Grade.DetailedPerfect), (30000, Grade.DetailedPerfect),
        (30001, Grade.Perfect), (60000, Grade.Perfect), (60001, Grade.Good),
        (90000, Grade.Good), (90001, Grade.Miss)
    ];
    foreach (var item in cases) Equal(item.Grade, Rules.HeadGrade(item.Time));
});

Test("sustain thresholds use exact integer comparisons", () =>
{
    Equal(Grade.DetailedPerfect, Rules.SustainGrade(2000, 2000));
    Equal(Grade.Perfect, Rules.SustainGrade(1999, 2000));
    Equal(Grade.Perfect, Rules.SustainGrade(1500, 2000));
    Equal(Grade.Good, Rules.SustainGrade(1499, 2000));
    Equal(Grade.Good, Rules.SustainGrade(1000, 2000));
    Equal(Grade.Miss, Rules.SustainGrade(999, 2000));
    Equal(Grade.Miss, Rules.SustainGrade(0, 0));
});

Test("reference chart compiles to the documented events", () =>
{
    var chart = ChartParser.Parse(File.ReadAllText(Path.Combine(root, "examples/charts/spec-demo.npd")));
    using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "examples/charts/spec-demo.chart.json")));
    var expected = json.RootElement.GetProperty("notes").EnumerateArray().ToArray();
    Equal(9, chart.Notes.Count); Equal(13L, chart.Weight); Equal(4_500_000L, chart.EndUs);
    for (int i = 0; i < expected.Length; i++)
    {
        Equal(expected[i].GetProperty("key").GetInt32(), chart.Notes[i].Key);
        Equal(expected[i].GetProperty("direction").GetString(), chart.Notes[i].Axis.ToString());
        Equal(expected[i].GetProperty("time_ms").GetInt64() * 1000, chart.Notes[i].StartUs);
        Equal(expected[i].GetProperty("type").GetString() == "hold", chart.Notes[i].IsHold);
        if (chart.Notes[i].IsHold) Equal(expected[i].GetProperty("end_ms").GetInt64() * 1000, chart.Notes[i].EndUs!.Value);
    }
});

Test("first, rests, tempo and subdivision changes", () =>
{
    var chart = ChartParser.Parse(Source("(120){4}1C,(240){8}2R,,E", "0.125"));
    Equal(125000L, chart.Notes[0].StartUs); Equal(625000L, chart.Notes[1].StartUs); Equal(875000L, chart.EndUs);
});
Test("negative first is retained", () => Equal(-500000L, ChartParser.Parse(Source("(120){4}1C,,,E", "-0.5")).Notes[0].StartUs));
Test("comments, BOM and whitespace", () => Equal(2, ChartParser.Parse("\uFEFF" + Source("(120) {4}\n69 C, // comment\n,E")).Notes.Count));
Test("hold duration stays fixed across later tempo changes", () =>
{
    var chart = ChartParser.Parse(Source("(120){4}7Ch[4:2],(240){4}8R,,,,E"));
    Equal(1000000L, chart.Notes[0].EndUs!.Value);
});
Test("explicit second step and hold duration", () =>
{
    var chart = ChartParser.Parse(Source("(120){#0.35}7Ch[#1.25],,,,,E"));
    Equal(1250000L, chart.Notes[0].EndUs!.Value); Equal(1750000L, chart.EndUs);
});
Test("hold BPM override is local", () =>
{
    var chart = ChartParser.Parse(Source("(120){4}9Rh[180#4:3],8C,,,E"));
    Equal(1000000L, chart.Notes[0].EndUs!.Value); Equal(500000L, chart.Notes[1].StartUs);
});
Test("decimal BPM and triplets", () =>
{
    var chart = ChartParser.Parse(Source("(120.5){12}1C,2R,3C,,E"));
    Check(Math.Abs(chart.Notes[1].StartUs - 165975L) <= 1);
});
Test("all nine buttons map to correct rows and columns", () =>
{
    for (int key = 1; key <= 9; key++)
    {
        Equal((key - 1) % 3, new NoteEvent(0, key, Axis.C, 0).Lane);
        Equal(2 - (key - 1) / 3, new NoteEvent(0, key, Axis.R, 0).Lane);
    }
});

Test("reject malformed notes and metadata", () =>
{
    string[] invalid = ["(120){4}6,E", "(120){4}0C,E", "(120){4}6Ch,E", "(120){4}69Ch[4:1],,,E",
        "(120){4}6Ch[0:1],,,E", "{4}(120)6C,E", "(0){4}6C,E", "(120){4}6C/,,E",
        "(120){4}6C6R,E", "(120){4}6C,", "(120){4}6C,E1C,", "(120){4}E",
        "(120){4}6Ch[#2],E", "(120){4}6C// no comma\nE", "(120){4}6C,(150)/7C,E"];
    foreach (string body in invalid) Reject(() => ChartParser.Parse(Source(body)));
    Reject(() => ChartParser.Parse(Source("(120){4}6C,E").Replace("&title=Test", "&title=Test\n&title=Duplicate")));
    Reject(() => ChartParser.Parse(Source("(120){4}6C,E").Replace("numpad-chart-v1", "unknown")));
});
Test("syntax errors include a location", () =>
{
    try { ChartParser.Parse(Source("(120){4}\n0C,E")); Check(false); }
    catch (ChartException e) { Check(e.Line >= 7 && e.Column > 0); }
});
Test("closed hold interval prohibits heads in both directions", () =>
{
    foreach (Axis axis in Enum.GetValues<Axis>())
        foreach (long at in new[] { 1000000L, 1500000L, 2000000L })
        {
            Reject(() => ChartValidator.Validate(Make(new(0, 6, Axis.C, 1000000, 2000000), new(1, 6, axis, at))));
            Reject(() => ChartValidator.Validate(Make(new(0, 6, Axis.C, 1000000, 2000000), new(1, 6, axis, at, at + 500000))));
        }
    ChartValidator.Validate(Make(new(0, 6, Axis.C, 1000000, 2000000), new(1, 9, Axis.C, 1500000)));
    ChartValidator.Validate(Make(new(0, 6, Axis.C, 1000000, 2000000), new(1, 6, Axis.R, 2000001)));
});
Test("same-key simultaneous taps are rejected", () => Reject(() => ChartParser.Parse(Source("(120){4}6R/6C,,E"))));

Test("earliest eligible note wins over closest note", () =>
{
    var session = new PlaySession(Make(new(0, 6, Axis.C, 1000000), new(1, 6, Axis.R, 1080000)));
    session.Press(6, 1050000); Equal(Grade.Perfect, session.States[0].Head!.Value); Check(session.States[1].Head is null);
    session.Release(6, 1051000); session.Press(6, 1080000); Equal(Grade.DetailedPerfect, session.States[1].Head!.Value);
});
Test("pre-held keys do not hit a new head", () =>
{
    var session = new PlaySession(Make(new NoteEvent(0, 6, Axis.C, 1000000)));
    session.Press(6, 879999); session.AdvanceTo(1000000); Check(session.States[0].Head is null);
    session.Release(6, 1001000); session.Press(6, 1010000); Equal(Grade.DetailedPerfect, session.States[0].Head!.Value);
});
Test("partial chord produces only the handled key judgment", () =>
{
    var session = new PlaySession(Make(new(0, 6, Axis.C, 1000000), new(1, 9, Axis.C, 1000000)));
    session.Press(6, 1000000); var hits = session.DrainJudgments(); Equal(1, hits.Count); Equal(6, hits[0].Key);
    Check(session.States[1].Head is null); Equal(0, session.DrainJudgments().Count);
});
Test("extra keys can consume an upcoming note as early miss", () =>
{
    var session = new PlaySession(Make(new(0, 6, Axis.C, 1000000), new(1, 3, Axis.R, 1100000)));
    session.PressKeys([6, 3], 1000000);
    Equal(Grade.DetailedPerfect, session.States[0].Head!.Value); Equal(Grade.Miss, session.States[1].Head!.Value); Equal(1, session.Score.Combo);
});
Test("empty key presses have no score or combo penalty", () =>
{
    var session = new PlaySession(Make(new NoteEvent(0, 6, Axis.C, 1000000)));
    session.Press(6, 1000000); session.Press(1, 1010000); Equal(1, session.Score.Combo); Equal(1, session.Score.Judged);
});
Test("repeated keydown does not hit the next note", () =>
{
    var session = new PlaySession(Make(new(0, 6, Axis.C, 1000000), new(1, 6, Axis.R, 1020000)));
    session.Press(6, 1000000); session.Press(6, 1010000); Check(session.States[1].Head is null);
    session.Release(6, 1015000); session.Press(6, 1020000); Equal(2, session.Score.Judged);
});
Test("late good is inclusive and expiry follows one microsecond later", () =>
{
    var hit = new PlaySession(Make(new NoteEvent(0, 6, Axis.C, 1000000)));
    hit.Press(6, 1090000); Equal(Grade.Good, hit.States[0].Head!.Value);
    var miss = new PlaySession(hit.Chart); miss.AdvanceTo(1090000); Check(miss.States[0].Head is null);
    miss.AdvanceTo(1090001); Equal(Grade.Miss, miss.States[0].Head!.Value);
});
Test("good head uses actual hit time for full sustain", () =>
{
    var session = Hold(); session.Press(6, 1080000); session.Release(6, 3000000);
    Equal(Grade.Good, session.States[0].Head!.Value); Equal(Grade.DetailedPerfect, session.States[0].Sustain!.Value);
    Equal(840000L, session.Score.Score);
});
Test("missed head uses scheduled interval: 75 percent", () =>
{
    var session = Hold(); session.Press(6, 1500000); session.AdvanceTo(3000000);
    Equal(Grade.Miss, session.States[0].Head!.Value); Equal(Grade.Perfect, session.States[0].Sustain!.Value);
    Equal(666666L, session.Score.Score);
});
Test("missed head uses scheduled interval: 50 percent", () =>
{
    var session = Hold(); session.Press(6, 2000000); session.AdvanceTo(3000000);
    Equal(Grade.Good, session.States[0].Sustain!.Value);
});
Test("late grab cannot turn 25 percent into full sustain", () =>
{
    var session = Hold(); session.Press(6, 2500000); session.AdvanceTo(3000000);
    Equal(Grade.Miss, session.States[0].Sustain!.Value); Equal(0L, session.Score.Score);
});
Test("early miss can still maintain the whole scheduled interval", () =>
{
    var session = Hold(); session.Press(6, 900000); session.Release(6, 3000000);
    Equal(Grade.Miss, session.States[0].Head!.Value); Equal(Grade.DetailedPerfect, session.States[0].Sustain!.Value);
    Equal(673333L, session.Score.Score);
});
Test("regrab accumulates without resetting the reference", () =>
{
    var session = Hold(); session.Press(6, 1000000); session.Release(6, 1500000); session.Press(6, 2000000); session.Release(6, 3000000);
    Equal(Grade.Perfect, session.States[0].Sustain!.Value); Equal(0.75, session.States[0].SustainRatio!.Value);
    Equal(1003333L, session.Score.Score);
});
Test("short hold completed before late head has defined zero sustain", () =>
{
    var session = new PlaySession(Make(new NoteEvent(0, 6, Axis.C, 1000000, 1050000)));
    session.Press(6, 1080000); Equal(Grade.Good, session.States[0].Head!.Value); Equal(Grade.Miss, session.States[0].Sustain!.Value);
});
Test("simultaneous mixed judgments reset then count successes, independent of order", () =>
{
    Grade[][] orders = [[Grade.DetailedPerfect, Grade.Miss, Grade.Good], [Grade.Miss, Grade.Good, Grade.DetailedPerfect], [Grade.Good, Grade.DetailedPerfect, Grade.Miss]];
    foreach (var order in orders)
    {
        var score = new ScoreState(200);
        for (int i = 0; i < 120; i++) score.Apply(new(i, 1, Axis.C, JudgmentPart.Head, Grade.Perfect, i));
        foreach (var grade in order) score.Apply(new(200, 2, Axis.R, JudgmentPart.Head, grade, 200));
        Equal(2, score.Combo); Equal(120, score.MaxCombo);
    }
});
Test("automatic miss and simultaneous sustain success share combo semantics", () =>
{
    var session = new PlaySession(Make(new(0, 2, Axis.C, 0, 1090001), new(1, 1, Axis.R, 1000000)));
    session.Press(2, 0); session.AdvanceTo(1090001); Equal(1, session.Score.Combo);
});
Test("all-perfect reference autoplay reaches exact normalized score", () =>
{
    var chart = ChartParser.Parse(File.ReadAllText(Path.Combine(root, "examples/charts/spec-demo.npd")));
    var session = new PlaySession(chart);
    session.PressKeys([6, 9, 3], 0); session.ReleaseAll(1000);
    session.Press(7, 1000000); session.Press(4, 1500000);
    session.Release(4, 2500000); session.Release(7, 3000000);
    session.PressKeys([2, 8, 5, 7], 4000000); session.ReleaseAll(4001000); session.AdvanceTo(chart.EndUs);
    Equal(1010000L, session.Score.Score); Equal(11, session.Score.Combo); Equal(11, session.Score.MaxCombo); Check(session.Complete);
});
Test("unplayed chart finishes with all misses", () =>
{
    var session = Hold(); session.AdvanceTo(4000000); Check(session.Complete); Equal(0L, session.Score.Score); Equal(2, session.Score.Counts[Grade.Miss]);
});
Test("time cannot run backwards", () =>
{
    var session = Hold(); session.AdvanceTo(1000);
    bool rejected = false; try { session.AdvanceTo(999); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected);
});

Test("bundled demo charts validate and autoplay to exact maximum", () =>
{
    foreach (string file in Directory.GetFiles(Path.Combine(root, "content/charts"), "*.npd"))
    {
        var chart = ChartParser.Parse(File.ReadAllText(file));
        var session = new PlaySession(chart);
        var actions = chart.Notes.SelectMany(n => new[] {
            (At: n.StartUs, Key: n.Key, Down: true),
            (At: n.EndUs ?? n.StartUs + 1, Key: n.Key, Down: false)
        }).OrderBy(x => x.At).ThenBy(x => x.Down).GroupBy(x => x.At);
        foreach (var batch in actions)
        {
            foreach (var up in batch.Where(x => !x.Down)) session.Release(up.Key, batch.Key);
            var downs = batch.Where(x => x.Down).Select(x => x.Key).ToArray();
            if (downs.Length > 0) session.PressKeys(downs, batch.Key);
        }
        session.AdvanceTo(chart.EndUs);
        Check(session.Complete, file); Equal(1010000L, session.Score.Score);
        Equal(0, session.Score.Counts[Grade.Miss]); Check(chart.Notes.Count > 30);
    }
});

Console.WriteLine($"\n{passed} passed, {failed} failed; {assertions} assertions.");
return failed == 0 ? 0 : 1;
