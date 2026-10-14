// What Smart Step Into and the debugger's synthetic variables ($exception, the values calls return) are tried on. Tests
// find a line by the code on it, so each case's lines are its own; a new case goes below the others.
Calls.Run();
Lambdas.Run();
Awaits.RunAsync().GetAwaiter().GetResult();
Returns.Run();
Throws.Run();

static class Calls
{
    public static void Run()
    {
        int nested = Outer(Inner());
        int sum = Add(Add(1, 2), 3);
        Console.WriteLine($"nested {nested} {sum}");
    }

    static int Inner() => 20;

    static int Outer(int value)
    {
        return value + 1;
    }

    static int Add(int a, int b)
    {
        return a + b;
    }
}

static class Lambdas
{
    public static void Run()
    {
        int[] doubled = [.. Numbers().Select(n => Twice(n))];
        Console.WriteLine($"doubled {doubled.Length}");
    }

    static int[] Numbers() => [1, 2, 3];

    static int Twice(int n)
    {
        return n * 2;
    }
}

static class Awaits
{
    public static async Task RunAsync()
    {
        int computed = await ComputeAsync(Seed());
        Console.WriteLine($"computed {computed}");
    }

    static int Seed() => 4;

    static async Task<int> ComputeAsync(int value)
    {
        await Task.Yield();
        return value * 3;
    }
}

static class Returns
{
    public static void Run()
    {
        string described = Describe(Count());
        double ratio = Ratio(3, 4);
        Spot spot = Locate(2, 3);
        Console.WriteLine($"{described} {ratio} {spot}");
    }

    static int Count() => 42;

    static string Describe(int count) => $"count {count}";

    static double Ratio(int part, int whole) => (double)part / whole;

    static Spot Locate(int x, int y) => new(x, y);
}

static class Throws
{
    public static void Run()
    {
        try
        {
            Fail("bad input");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("caught");
        }
    }

    static void Fail(string reason) => throw new InvalidOperationException(reason);
}

readonly record struct Spot(int X, int Y);
