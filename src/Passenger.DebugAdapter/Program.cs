// Passenger's .NET debug adapter, as the IDE carries it under debugger/ (AdapterProgram has its flags):
//   Passenger.DebugAdapter --interpreter=vscode [--engineLogging=FILE] [--symbolCache=FOLDER] [--remote]
return await Passenger.DebugAdapter.AdapterProgram.RunAsync(args, "Passenger.DebugAdapter");
