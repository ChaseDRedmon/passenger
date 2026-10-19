using System.Collections.ObjectModel;
using Greenhouse.Core;

namespace Greenhouse.App.ViewModels;

/// <summary>The greenhouse window: its plants, the one chosen and today's round.</summary>
public sealed class MainViewModel : ViewModel
{
    private readonly WateringPlan plan = new();
    private PlantViewModel? selected;
    private int day = 6;

    public MainViewModel()
    {
        plan.Add(new Plant("Basil", "North", 0.8, 0.4));
        plan.Add(new Plant("Tomato", "South", 3.0, 1.0));
        plan.Add(new Plant("Fig", "South", 12.0, 1.5));
        plan.Add(new Plant("Fern", "Shade", 1.5, 0.5));
        foreach (Plant plant in plan.Plants)
        {
            Plants.Add(new PlantViewModel(plant, 0.35 + (plant.Name.Length * 0.08)));
        }
        selected = Plants[1];
    }

    public string Title => "Greenhouse";

    public string Summary => $"Day {day}: {plan.LitresFor(day):F1} L for {string.Join(", ", plan.DueOn(day).Select(p => p.Name))}";

    public ObservableCollection<PlantViewModel> Plants { get; } = [];

    public PlantViewModel? Selected
    {
        get => selected;
        set => Set(ref selected, value);
    }

    public void Water()
    {
        foreach (PlantViewModel plant in Plants.Where(p => day % p.Plant.DaysBetweenWatering == 0))
        {
            plant.Moisture = 1;
        }
        day++;
        Changed(nameof(Summary));
    }
}
