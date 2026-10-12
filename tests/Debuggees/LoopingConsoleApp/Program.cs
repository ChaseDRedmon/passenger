// A program that never ends on its own, for Pause: it spins in managed code, or with an argument sleeps in each turn,
// so a pause lands outside managed code. It says when it is in its loop.
long turns = 0;
bool sleeps = args.Length > 0;
Console.WriteLine("looping");
while (true)
{
    turns++;
    if (sleeps)
    {
        Thread.Sleep(20);
    }
}
