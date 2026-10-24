using System.Net.Http.Json;

namespace Greenhouse.Sensors;

/// <summary>The API as the sensors see it.</summary>
public sealed class GreenhouseClient(HttpClient http)
{
    public async Task<List<ThirstyPlant>> ThirstyAsync(CancellationToken stopping) =>
        await http.GetFromJsonAsync<List<ThirstyPlant>>("/plants/thirsty", stopping) ?? [];

    public async Task<PlantDetails?> PlantAsync(int id, CancellationToken stopping) =>
        await http.GetFromJsonAsync<PlantDetails>($"/plants/{id}", stopping);

    public async Task WaterAsync(int id, double litres, CancellationToken stopping) =>
        (await http.PostAsync($"/plants/{id}/waterings?litres={litres}", null, stopping)).EnsureSuccessStatusCode();
}

public sealed record ThirstyPlant(int Id, string Name, string Bench, DateTime? LastWatered);

public sealed record PlantDetails(int Id, string Name, string Bench, int Waterings);

/// <summary>Each second, asks which plants are thirsty and looks at one of them; waters the first now and then.</summary>
public sealed class SoilSensors(GreenhouseClient greenhouse, ILogger<SoilSensors> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using PeriodicTimer second = new(TimeSpan.FromSeconds(1));
        int reading = 0;
        while (await second.WaitForNextTickAsync(stopping))
        {
            reading++;
            try
            {
                List<ThirstyPlant> thirsty = await greenhouse.ThirstyAsync(stopping);
                PlantDetails? plant = await greenhouse.PlantAsync(reading % 6 + 1, stopping);
                logger.LogInformation("Reading {Reading}: {Count} thirsty, {Plant} watered {Times} times",
                    reading, thirsty.Count, plant?.Name, plant?.Waterings);
                if (reading % 5 == 0 && thirsty.Count > 0)
                {
                    await greenhouse.WaterAsync(thirsty[0].Id, 0.5, stopping);
                }
            }
            catch (HttpRequestException e)
            {
                logger.LogWarning("The greenhouse did not answer: {Reason}", e.Message);
            }
        }
    }
}
