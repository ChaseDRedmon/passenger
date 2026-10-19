using Greenhouse.Core;

WateringPlan plan = new();
plan.Add(new Plant("Basil", "North", 0.8, 0.4));
plan.Add(new Plant("Tomato", "South", 3.0, 1.0));
plan.Add(new Plant("Fig", "South", 12.0, 1.5));
plan.Add(new Plant("Fern", "Shade", 1.5, 0.5));

for (int day = 1; day <= 7; day++)
{
    double litres = plan.LitresFor(day);
    string names = string.Join(", ", plan.DueOn(day).Select(p => p.Name));
    Console.WriteLine($"Day {day}: {litres:F1} L for {names}");
}

double days = Forecast.DaysOfWater(400, plan, runs: 200_000);
Console.WriteLine($"A 400 L rain tank lasts about {days:F0} days");
