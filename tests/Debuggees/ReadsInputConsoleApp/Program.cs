// Reads a line, so the debugger must give it an input of its own: the adapter's is the protocol.
string? line = Console.ReadLine();
Console.WriteLine($"read: {line ?? "<end of input>"}");
