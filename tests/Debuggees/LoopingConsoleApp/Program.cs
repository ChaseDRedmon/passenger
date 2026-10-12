// A program that never ends on its own, for Pause and evaluation. With no argument it spins in managed code; with
// "sleep" it sleeps in each turn, so a pause lands outside managed code; it says when it is in its loop. With "count"
// a second thread counts while the first stops, to show an evaluation leaves the other threads paused; with
// "parallel" a loop's iterations run on several threads, and with "parallel throws" one of them throws. With "step"
// or "await" the first thread waits, in a call or an await, for a second to pass a line five times.
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
if (args is ["step" or "await"])
{
    Thread passing = new(() =>
    {
        for (long pass = 0; ; pass++)
        {
            Passes.Count = pass; // passed by another thread
        }
    }) { IsBackground = true };
    passing.Start();
    if (args is ["await"])
    {
        Passes.Await().GetAwaiter().GetResult();
        return;
    }
    Passes.Wait(5); // step over the call while another thread passes
    Console.WriteLine("stepped"); // the call's step ends here
    Console.WriteLine("ran on"); // a step lost runs on to here
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

static class Passes
{
    public static long Count;

    // Returns once the other thread has passed its line as many times again, having waited at least once.
    public static void Wait(int times)
    {
        long start = Volatile.Read(ref Count);
        do
        {
            Thread.Sleep(10); // wait for the other thread
        }
        while (Volatile.Read(ref Count) < start + times);
    }

    public static async Task Await()
    {
        await WaitOnAnotherThread(); // step over the await while another thread passes
        Console.WriteLine("awaited"); // the await's step ends here
        Console.WriteLine("ran on after the await"); // a step over the await lost runs on to here
    }

    // Apart from the await, whose line a breakpoint would otherwise take to mean the lambda's.
    private static Task WaitOnAnotherThread() => Task.Run(() => Wait(5));
}
