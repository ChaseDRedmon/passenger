using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

if (args is ["child"])
{
	Console.WriteLine("child ran");
	return;
}
if (args is ["flood"])
{
	// 64 KB on standard output, more than a pipe holds unread, then an exit code of its own.
	for (int i = 0; i < 1024; i++)
	{
		Console.Out.Write($"{i:D5} {new string((char)('a' + i % 26), 57)}\n");
	}
	Environment.ExitCode = 42;
	return;
}
if (args is ["busy"])
{
	Busy.Spin(TimeSpan.FromMilliseconds(800));
	Console.WriteLine($"allocated {Busy.Allocate(60_000)}");
	Children.Run();
	return;
}
if (args is ["emitted"])
{
	Emitted.Run();
	return;
}
if (args is ["reads"])
{
	Reads.Run();
	return;
}
if (args is ["corelib"])
{
	CoreLibCalls.Run();
	return;
}
Wrapper.CallWithCatch(() => throw new InvalidOperationException("thrown in user code"));
Attributed.StepThrough();
Attributed.NonUser();
Attributed.Hidden();
Console.WriteLine("end");
Captures.Outer(41);
Nested.Throw();
Displays.Show();
Interpreter.Run();
Jumps.Run();
Threads.Run();
Children.Run();

// The cases Passenger's own debugger tests add to upstream's. Tests find their lines by the code on them.
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

static class Nested
{
	public static void Throw()
	{
		try
		{
			try { throw new Exception("Message1"); }
			catch (Exception e1) { throw new InvalidOperationException("Message2", e1); }
		}
		catch { }
	}
}

// [DebuggerDisplay] as upstream's #51 and #53 spell out what it should show.
static class Displays
{
	public static void Show()
	{
		Money money = new() { Amount = 12.5m, Currency = "USD" };
		QuotedMoney quoted = new() { Amount = 12.5m, Currency = "USD" };
		Braced braced = new();
		Missing missing = new();
		Link tail = new("tail", null);
		Link head = new("head", tail);
		Labelled labelled = new();
		Holder holder = new();
		Derived derived = new();
		Box<Money> box = new() { Value = new Money { Amount = 1m, Currency = "EUR" } };
		Dictionary<string, Point> byName = new() { ["origin"] = new Point(0, 0) };
		LiftedObject lifted = new();
		Angry angry = new();
		Console.WriteLine("displays");
	}
}

[DebuggerDisplay("{Amount} {Currency,nq}")]
sealed class Money { public decimal Amount; public string Currency = ""; }

[DebuggerDisplay("{Amount} {Currency}")]
sealed class QuotedMoney { public decimal Amount; public string Currency = ""; }

[DebuggerDisplay("\\{{Value}\\}")]
sealed class Braced { public int Value = 3; }

[DebuggerDisplay("Value={Value} Bad={NoSuchMember}")]
sealed class Missing { public int Value = 2; }

[DebuggerDisplay("{_name,nq} -> {Next._name,nq}")]
sealed class Link(string name, Link? next) { string _name = name; public Link? Next = next; }

[DebuggerDisplay("{Value}", Name = "renamed", Type = "RetypedThing")]
sealed class Labelled { public int Value = 11; }

sealed class Holder { public Labelled Item = new(); }

[DebuggerDisplay("base {Tag,nq}")]
class Base { public string Tag = "b"; }
sealed class Derived : Base { public int Extra = 9; }

[DebuggerDisplay("({X}, {Y})")]
readonly struct Point(int x, int y) { public int X { get; } = x; public int Y { get; } = y; }

[DebuggerDisplay("box of {Value}")]
sealed class Box<T> { public T? Value; }

sealed class LiftedObject
{
	[DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
	public Inner Part = new();
}

sealed class Inner { public int A = 1; public string B = "b"; }

[DebuggerTypeProxy(typeof(AngryProxy))]
sealed class Angry { public int Real = 1; }

sealed class AngryProxy
{
	public AngryProxy(Angry target) => throw new InvalidOperationException("no");
	public int Doubled => 2;
}

// What the debugger's IL interpreter must get right (upstream #30).
static class Interpreter
{
	public static void Run()
	{
		int myInt = 5;
		uint myUInt = 7;
		SignedEnum minusOne = SignedEnum.MinusOne;
		WideEnum wide = WideEnum.Max;
		Pair<int> pairOfInt = new() { Value = 1 };
		object boxedPair = pairOfInt;
		Counter counter = new();
		decimal price = 12.5m;
		Bumper.Work(new Bumped());
		Console.WriteLine("interpreter");
	}
}

enum SignedEnum : sbyte { MinusOne = -1 }
enum WideEnum : ushort { Max = 65535 }
struct Pair<T> { public T Value; }
struct Counter { public int Count; public void Increment() => Count++; }
interface IBump { void Bump(); }
struct Bumped : IBump { public int N; public void Bump() => N++; }

static class Bumper
{
	public static void Work<T>(T item) where T : IBump
	{
		Console.WriteLine("bumper");
	}
}

// Upstream's #59: two workers waiting while Main stops.
static class Threads
{
	static readonly ManualResetEventSlim Gate = new(false);
	static readonly CountdownEvent Started = new(2);

	public static void Run()
	{
		Thread named = new(Wait) { Name = "worker-named" };
		Thread unnamed = new(Wait);
		named.Start();
		unnamed.Start();
		Started.Wait();
		Console.WriteLine("both workers are waiting");
		Gate.Set();
		named.Join();
		unnamed.Join();
	}

	static void Wait()
	{
		Started.Signal();
		Gate.Wait();
	}
}

// Set Next Statement's case: three additions to skip over or go back to.
static class Jumps
{
	public static void Run()
	{
		int jumps = 0;
		jumps += 1;
		jumps += 10;
		jumps += 100;
		Console.WriteLine($"jumps {jumps}");
	}
}

// A program being debugged that starts a .NET program of its own, which must run as it would undebugged.
static class Children
{
	public static void Run()
	{
		using Process child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, [typeof(Children).Assembly.Location, "child"]) { RedirectStandardOutput = true })!;
		bool ended = child.WaitForExit(15000);
		string said = ended ? child.StandardOutput.ReadToEnd().Trim() : "nothing, held at its start";
		if (!ended)
		{
			child.Kill();
		}
		Console.WriteLine($"child said: {said}");
	}
}

// Code the program makes as it runs (System.Reflection.Emit), in a module with no file, calling back into the user's.
public static class Emitted
{
	public static void Run()
	{
		AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("EmittedAtRunTime"), AssemblyBuilderAccess.Run);
		TypeBuilder type = assembly.DefineDynamicModule("EmittedAtRunTime").DefineType("EmittedCaller", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		MethodBuilder call = type.DefineMethod("CallBack", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(int)]);
		ILGenerator il = call.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Call, typeof(Emitted).GetMethod(nameof(CalledBack))!);
		il.Emit(OpCodes.Ret);
		Action<int> callBack = type.CreateType().GetMethod("CallBack")!.CreateDelegate<Action<int>>();
		callBack(7);
	}

	public static void CalledBack(int value)
	{
		Console.WriteLine($"called back with {value}");
	}
}

// A getter blocked in a read of input, outside the runtime: an evaluation of it cannot be stopped until the program has a
// line. A worker passes a line of its own meanwhile.
static class Reads
{
	public static string Input => Console.ReadLine() ?? "none";

	public static void Run()
	{
		Thread worker = new(() =>
		{
			Thread.Sleep(1500);
			Console.WriteLine("worker ran");
		});
		int before = 1;
		worker.Start();
		Console.WriteLine($"before {before}");
		Console.WriteLine($"read {Input}");
		worker.Join();
	}
}

// The profiler's case: a method that keeps the thread busy, so its samples stand out.
static class Busy
{
	public static double Spin(TimeSpan time)
	{
		Stopwatch clock = Stopwatch.StartNew();
		double total = 0;
		while (clock.Elapsed < time)
		{
			for (int i = 1; i < 10000; i++)
			{
				total += Math.Sqrt(i);
			}
		}
		Console.WriteLine($"spun {total > 0}");
		return total;
	}

	/// <summary>About 60 MB of short-lived kilobyte arrays: allocation ticks and young collections.</summary>
	public static long Allocate(int count)
	{
		long kept = 0;
		for (int i = 0; i < count; i++)
		{
			byte[] block = new byte[1024];
			block[i % 1024] = 1;
			kept += block.Length;
		}
		return kept;
	}
}

// Code of the user's that CoreLib calls back: the frame below it is CoreLib's, which ships without symbols, so the
// debugger decompiles it (the largest assembly a program loads) to show it with source.
static class CoreLibCalls
{
	public static void Run()
	{
		List<int> each = [7];
		each.ForEach(value =>
		{
			Console.WriteLine($"called by CoreLib with {value}");
		});
	}
}
