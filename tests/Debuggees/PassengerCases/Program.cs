using System.Diagnostics;

Wrapper.CallWithCatch(() => throw new InvalidOperationException("thrown in user code"));
Attributed.StepThrough();
Attributed.NonUser();
Attributed.Hidden();
Console.WriteLine("end");
Captures.Outer(41);

// The cases Passenger's own debugger tests add to upstream's; each test names its lines.
// Code of the user's own that says it is not theirs: Just My Code passes over it, and a catch in it
// leaves an exception unhandled by the user.
[DebuggerNonUserCode]
static class Wrapper
{
	public static void CallWithCatch(Action callback)
	{
		try { callback(); } catch { }
	}
}

static class Attributed
{
	[DebuggerStepThrough] public static void StepThrough() => Console.WriteLine("1");
	[DebuggerNonUserCode] public static void NonUser() => Console.WriteLine("2");
	[DebuggerHidden] public static void Hidden() => Console.WriteLine("3");
}

static class Slow
{
	// A getter that never returns: the debugger must stop evaluating it, and go on.
	public static int Forever { get { while (true) { } } }
}

static class Captures
{
	public static void Outer(int captured)
	{
		Func<int> f = () => captured + 1;
		Console.WriteLine(f());
	}
}
