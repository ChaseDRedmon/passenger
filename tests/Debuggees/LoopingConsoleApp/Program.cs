// A program that never ends on its own, for Pause and evaluation. With no argument it spins in managed code; with
// "sleep" it sleeps in each turn, so a pause lands outside managed code; it says when it is in its loop. With "count"
// a second thread counts while the first stops, to show an evaluation leaves the other threads paused; with
// "parallel" a loop's iterations run on several threads, and with "parallel throws" one of them throws.
if (args is ["count"])
{
    Thread counting = new(() =>
    {
        while (true)
        {
            Counter.Value++;
        }
    }) { IsBackground = true };
    counting.Start();
    Thread.Sleep(100);
    Slow slow = new();
    Console.WriteLine("counting"); // stop while counting
    return;
}
if (args is ["parallel", ..])
{
    bool throws = args is [_, "throws"];
    Parallel.For(0, 400, i =>
    {
        int square = i * i; // each iteration
        if (throws && i == 200)
        {
            try
            {
                throw new InvalidOperationException("iteration 200");
            }
            catch (InvalidOperationException)
            {
            }
        }
    });
    return;
}
long turns = 0;
bool sleeps = args is ["sleep"];
Console.WriteLine("looping");
while (true)
{
    turns++;
    if (sleeps)
    {
        Thread.Sleep(20);
    }
}

static class Counter
{
    public static long Value;

    // Waits for the counting thread, which an evaluation leaves paused.
    public static long Next
    {
        get
        {
            long start = Value;
            while (Value == start)
            {
            }
            return Value;
        }
    }
}

class Slow
{
    public string Value
    {
        get
        {
            Thread.Sleep(300);
            return "slow";
        }
    }
}
