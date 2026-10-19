namespace Greenhouse.Core;

/// <summary>How long a rain tank lasts, from many simulated summers.</summary>
public static class Forecast
{
    public static double DaysOfWater(double tankLitres, WateringPlan plan, int runs)
    {
        Random random = new(1027);
        long days = 0;
        for (int run = 0; run < runs; run++)
        {
            days += Simulate(tankLitres, plan, random);
        }
        return (double)days / runs;
    }

    private static int Simulate(double tank, WateringPlan plan, Random random)
    {
        int day = 0;
        while (tank > 0 && day < 365)
        {
            tank -= Thirst(plan, random);
            day++;
        }
        return day;
    }

    /// <summary>What every plant drinks in one day, more on a hot one.</summary>
    private static double Thirst(WateringPlan plan, Random random)
    {
        double litres = 0;
        foreach (Plant plant in plan.Plants)
        {
            double heat = 0.6 + (random.NextDouble() * 0.8);
            litres += plant.DailyUseLitres * heat;
        }
        return litres;
    }
}
