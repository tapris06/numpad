namespace NumpadNine.Core;

public enum Axis { R, C }
public enum Grade { Miss, Good, Perfect, Colorful }
public enum JudgmentPart { Head, Sustain }

public sealed record NoteEvent(int Id, int Key, Axis Axis, long StartUs, long? EndUs = null)
{
    public bool IsHold => EndUs.HasValue;
    public int Lane => Axis == Axis.C ? (Key - 1) % 3 : 2 - (Key - 1) / 3;
}

public sealed record Chart(string Title, string Artist, string Difficulty, string Audio,
    long FirstUs, long EndUs, IReadOnlyList<NoteEvent> Notes)
{
    public long Weight => Notes.Sum(n => n.IsHold ? 3L : 1L);
    public long LastRequiredUs => Notes.Max(n => Math.Max(n.StartUs + Rules.LateGoodUs, n.EndUs ?? n.StartUs));
}

public sealed class ChartException(string message, int line = 0, int column = 0)
    : Exception(line > 0 ? $"{line}행 {column}열: {message}" : message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

public static class ChartValidator
{
    public static void Validate(Chart chart)
    {
        if (chart.Notes.Count == 0) throw new ChartException("입력 노트가 하나 이상 필요합니다.");
        foreach (var note in chart.Notes)
        {
            if (note.Key is < 1 or > 9) throw new ChartException($"버튼 범위 오류: {note.Key}");
            if (note.IsHold && note.EndUs <= note.StartUs)
                throw new ChartException($"{note.Id}번 롱노트의 길이는 0보다 커야 합니다.");
        }
        foreach (var keyNotes in chart.Notes.GroupBy(n => n.Key))
        {
            var ordered = keyNotes.OrderBy(n => n.StartUs).ThenBy(n => n.Id).ToArray();
            NoteEvent? activeHold = null;
            for (int i = 0; i < ordered.Length; i++)
            {
                var note = ordered[i];
                if (i > 0 && note.StartUs == ordered[i - 1].StartUs)
                    throw new ChartException($"{note.Key}번 키를 같은 시점에 중복 배치했습니다.");
                if (activeHold is not null && note.StartUs <= activeHold.EndUs)
                    throw new ChartException($"{note.Key}번 키: 롱노트 닫힌구간 안에 다른 시작 탭이 있습니다.");
                if (note.IsHold) activeHold = note;
            }
        }
        if (chart.EndUs <= chart.LastRequiredUs)
            throw new ChartException("E를 마지막 판정 시간과 모든 롱노트 종료 뒤에 배치하세요.");
    }
}

public static class Rules
{
    public const long EarlyMissUs = -120_000;
    public const long LateGoodUs = 90_000;

    public static Grade? HeadGrade(long errorUs)
    {
        if (errorUs < EarlyMissUs) return null;
        if (errorUs is >= -30_000 and <= 30_000) return Grade.Colorful;
        if (errorUs is >= -60_000 and <= 60_000) return Grade.Perfect;
        if (errorUs is >= -90_000 and <= 90_000) return Grade.Good;
        return Grade.Miss;
    }

    public static Grade SustainGrade(long downUs, long lengthUs)
    {
        if (lengthUs <= 0) return Grade.Miss;
        downUs = Math.Clamp(downUs, 0, lengthUs);
        if (downUs == lengthUs) return Grade.Colorful;
        if ((decimal)downUs * 4 >= (decimal)lengthUs * 3) return Grade.Perfect;
        if ((decimal)downUs * 2 >= lengthUs) return Grade.Good;
        return Grade.Miss;
    }

    public static long Points(Grade grade) => grade switch
    {
        Grade.Colorful => 1_010_000,
        Grade.Perfect => 1_000_000,
        Grade.Good => 500_000,
        _ => 0
    };
}
