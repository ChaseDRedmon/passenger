using Greenhouse.Core;

WateringPlan plan = new();
plan.Add(new Plant("Basil", "North", 0.8, 0.4));
plan.Add(new Plant("Tomato", "South", 3.0, 1.0));
plan.Add(new Plant("Fig", "South", 12.0, 1.5));

double days = Forecast.DaysOfWater(250, plan, runs: 10_000);
Console.WriteLine($"A rain tank lasts about {days:F0} days");
