using Greenhouse.Api;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string database = Path.Combine(AppContext.BaseDirectory, "greenhouse.db");
builder.Services.AddDbContext<GreenhouseDb>(options => options
    .UseSqlite($"Data Source={database}")
    .EnableSensitiveDataLogging(builder.Environment.IsDevelopment()));

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    GreenhouseDb db = scope.ServiceProvider.GetRequiredService<GreenhouseDb>();
    // Every start begins on the first morning again.
    db.Database.EnsureDeleted();
    db.Database.EnsureCreated();
    db.Seed();
}

app.MapGet("/plants/thirsty", async (GreenhouseDb db, DateTime? at) =>
{
    DateTime now = at ?? DateTime.UtcNow;
    List<Plant> plants = await db.Plants.OrderBy(p => p.Bench).ThenBy(p => p.Name).ToListAsync();
    List<ThirstyPlant> thirsty = [];
    foreach (Plant plant in plants)
    {
        Watering? last = await db.Waterings
            .Where(w => w.PlantId == plant.Id)
            .OrderByDescending(w => w.At)
            .FirstOrDefaultAsync();
        double litresLeft = last is null ? 0 : last.Litres - (now - last.At).TotalDays * plant.DailyUseLitres;
        if (litresLeft < plant.DailyUseLitres)
        {
            thirsty.Add(new ThirstyPlant(plant.Id, plant.Name, plant.Bench, last?.At));
        }
    }
    return thirsty;
});

app.MapGet("/plants/{id:int}", async (GreenhouseDb db, int id) =>
    await db.Plants.Include(p => p.Waterings).FirstOrDefaultAsync(p => p.Id == id) is { } plant
        ? Results.Ok(new PlantDetails(plant.Id, plant.Name, plant.Bench, plant.Waterings.Count))
        : Results.NotFound());

app.MapPost("/plants/{id:int}/waterings", async (GreenhouseDb db, int id, double litres) =>
{
    if (!await db.Plants.AnyAsync(p => p.Id == id))
    {
        return Results.NotFound();
    }
    Watering watering = new() { PlantId = id, At = DateTime.UtcNow, Litres = litres };
    db.Waterings.Add(watering);
    await db.SaveChangesAsync();
    return Results.Created($"/plants/{id}/waterings/{watering.Id}", watering);
});

app.Run();

public sealed record ThirstyPlant(int Id, string Name, string Bench, DateTime? LastWatered);

public sealed record PlantDetails(int Id, string Name, string Bench, int Waterings);
