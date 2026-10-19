using Greenhouse.Core;

namespace Greenhouse.App.ViewModels;

/// <summary>One plant in the list, with how wet its pot is now (0 dry, 1 full).</summary>
public sealed class PlantViewModel(Plant plant, double moisture) : ViewModel
{
    private double moisture = moisture;

    public Plant Plant { get; } = plant;

    public string Name => Plant.Name;

    public string Bench => $"{Plant.Bench} bench, every {Plant.DaysBetweenWatering} days";

    public double Moisture
    {
        get => moisture;
        set => Set(ref moisture, Math.Clamp(value, 0, 1));
    }
}
