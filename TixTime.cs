namespace TixClock;

/// <summary>Turns a time into the four counts a TIX clock shows.</summary>
internal static class TixTime
{
    /// <summary>Number of squares in each block: hour tens, hour units, minute tens, minute units.</summary>
    public static readonly int[] BlockColumns = [1, 3, 2, 3];
    public const int Rows = 3;

    public static int[] Digits(DateTime time, bool use24Hour)
    {
        int hour = time.Hour;
        if (!use24Hour)
        {
            hour %= 12;
            if (hour == 0) hour = 12;
        }
        return [hour / 10, hour % 10, time.Minute / 10, time.Minute % 10];
    }

    /// <summary>Lights <paramref name="lit"/> random squares out of <paramref name="count"/>, avoiding a repeat of <paramref name="previous"/> where possible.</summary>
    public static bool[] RandomPattern(int count, int lit, bool[]? previous)
    {
        var pattern = new bool[count];
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Array.Clear(pattern);
            int[] order = Enumerable.Range(0, count).ToArray();
            Random.Shared.Shuffle(order);
            for (int i = 0; i < lit; i++) pattern[order[i]] = true;

            if (previous is null || lit == 0 || lit == count || !pattern.AsSpan().SequenceEqual(previous)) break;
        }
        return pattern;
    }

    public static string FormatTime(DateTime time, bool use24Hour) =>
        time.ToString(use24Hour ? "HH:mm" : "h:mm tt");

    public static string FormatDate(DateTime time) => time.ToString("dddd d MMMM");
}
