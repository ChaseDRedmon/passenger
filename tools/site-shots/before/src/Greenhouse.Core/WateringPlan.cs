namespace Greenhouse.Core;

/// <summary>Which plants need water on which day, from what each pot holds and drinks.</summary>
public sealed class WateringPlan
{
    private readonly List<Plant> plants = [];

    public IReadOnlyList<Plant> Plants => plants;

    public void Add(Plant plant) => plants.Add(plant);

    public Plant? Find(string name) =>
        plants.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<Plant> DueOn(int day) =>
        plants.Where(p => day % p.DaysBetweenWatering == 0);

    /// <summary>A day's round: every pot due that day, filled to the top.</summary>
    public double LitresFor(int day)
    {
        double litres = 0;
        foreach (Plant plant in DueOn(day))
        {
            litres += plant.CapacityLitres;
        }
        return litres;
    }
}
