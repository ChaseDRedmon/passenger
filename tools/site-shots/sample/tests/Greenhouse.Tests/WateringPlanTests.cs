using Greenhouse.Core;
using Xunit;

namespace Greenhouse.Tests;

public sealed class WateringPlanTests
{
    [Fact]
    public void A_plant_is_due_on_the_days_its_pot_runs_dry()
    {
        WateringPlan plan = new();
        plan.Add(new Plant("Basil", "North", 0.8, 0.2));

        Assert.Equal([4, 8, 12], Enumerable.Range(1, 12).Where(day => plan.DueOn(day).Any()));
    }

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(4, 0.8)]
    [InlineData(12, 3.8)]
    public void A_round_fills_every_pot_due(int day, double litres)
    {
        WateringPlan plan = new();
        plan.Add(new Plant("Basil", "North", 0.8, 0.2));
        plan.Add(new Plant("Tomato", "South", 3.0, 0.9));

        Assert.Equal(litres, plan.LitresFor(day));
    }

    [Fact]
    public void A_bigger_tank_lasts_longer()
    {
        WateringPlan plan = new();
        plan.Add(new Plant("Fig", "South", 12.0, 1.5));

        Assert.True(Forecast.DaysOfWater(200, plan, runs: 100) > Forecast.DaysOfWater(100, plan, runs: 100));
    }
}
