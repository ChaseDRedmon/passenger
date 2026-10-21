using Passenger.ContainerTools;

// Passenger's container tools, run by the IDE through docker exec. The first word is the mode; a mode or flag it does not
// know is fatal (exit code 2), so a typo never runs something other than what was asked.
//   passenger-tools dap --interpreter=vscode [--symbolCache=FOLDER] [--remote] [--engineLogging=FILE]
//   passenger-tools processes
//   passenger-tools version
const string Usage = """
    passenger-tools MODE [FLAGS]
      dap        Passenger's debug adapter on standard input and output (dap --help lists its flags)
      processes  this container's .NET programs as a JSON array, those the debugger cannot attach to saying why
      version    the tools' version, which is the IDE's that they came with
    """;

if (args is [] or ["--help" or "-h"])
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

string[] rest = args[1..];
switch (args[0])
{
    case "dap":
        return await Passenger.DebugAdapter.AdapterProgram.RunAsync(rest, "passenger-tools dap");
    case "processes":
        if (rest.Length > 0)
        {
            Console.Error.WriteLine($"processes takes no flags, not '{string.Join(' ', rest)}'");
            return 2;
        }
        Processes.Write(Console.Out);
        return 0;
    case "version":
        if (rest.Length > 0)
        {
            Console.Error.WriteLine($"version takes no flags, not '{string.Join(' ', rest)}'");
            return 2;
        }
        // As the IDE's Product.Version reads its own: the informational version, less the commit after its '+'.
        Console.WriteLine((System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Processes).Assembly)
            ?.InformationalVersion ?? "0.0.0").Split('+')[0]);
        return 0;
    // #345's profiling adds its modes here: trace (EventPipe streamed out over standard output) and heap (the heap graph
    // read in the container, since a Linux runtime's DAC loads only on Linux).
    default:
        Console.Error.WriteLine($"Unknown mode '{args[0]}'");
        Console.Error.WriteLine(Usage);
        return 2;
}
