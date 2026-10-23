using Microsoft.EntityFrameworkCore;

namespace Greenhouse.Api;

/// <summary>A plant on a bench, and how much it drinks a day.</summary>
public sealed class Plant
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Bench { get; set; }
    public double DailyUseLitres { get; set; }
    public List<Watering> Waterings { get; set; } = [];
}

/// <summary>One watering: when, and how much.</summary>
public sealed class Watering
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public DateTime At { get; set; }
    public double Litres { get; set; }
}

public sealed class GreenhouseDb(DbContextOptions<GreenhouseDb> options) : DbContext(options)
{
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<Watering> Waterings => Set<Watering>();

    /// <summary>The greenhouse as it stands on the first morning: six plants, each watered a few times.</summary>
    public void Seed()
    {
        if (Plants.Any())
        {
            return;
        }
        DateTime morning = new(2026, 10, 1, 7, 0, 0, DateTimeKind.Utc);
        (string Name, string Bench, double DailyUse)[] plants =
        [
            ("Basil", "North", 0.20), ("Tomato", "South", 0.60), ("Fig", "South", 0.45),
            ("Fern", "Shade", 0.15), ("Chilli", "North", 0.30), ("Lemon", "Glasshouse", 0.50),
        ];
        foreach ((string name, string bench, double dailyUse) in plants)
        {
            Plant plant = new() { Name = name, Bench = bench, DailyUseLitres = dailyUse };
            for (int day = 0; day < 4; day++)
            {
                plant.Waterings.Add(new Watering { At = morning.AddDays(day * 2), Litres = dailyUse * 2 });
            }
            Plants.Add(plant);
        }
        SaveChanges();
    }
}
