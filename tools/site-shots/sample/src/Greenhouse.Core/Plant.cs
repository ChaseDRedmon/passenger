namespace Greenhouse.Core;

/// <summary>A plant on a bench: how much water its pot holds and how much it drinks a day.</summary>
public sealed record Plant(string Name, string Bench, double CapacityLitres, double DailyUseLitres)
{
    /// <summary>Whole days from a full pot to a dry one, never less than one.</summary>
    public int DaysBetweenWatering => Math.Max(1, (int)(CapacityLitres / DailyUseLitres));
}
