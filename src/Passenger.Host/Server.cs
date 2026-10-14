using System.Diagnostics;
using System.Text.Json;

namespace Passenger.Host;

/// <summary>The host's side of the protocol: each request run on the thread pool as it comes, so one window's evaluation
/// never waits behind another's, and every line written whole under one lock. A request that throws answers with the
/// exception's message; the host goes on.</summary>
public sealed class Server(TextWriter protocol, HostReady ready)
{
    private readonly object writing = new();

    public void Run(TextReader input)
    {
        Write(new HostReply { Id = 0, Result = JsonSerializer.SerializeToElement(ready, HostJson.Options) });
        List<Task> running = [];
        while (input.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }
            HostRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<HostRequest>(line, HostJson.Options);
            }
            catch (JsonException error)
            {
                Console.Error.WriteLine($"Passenger.Host: a request that does not read: {error.Message}");
                continue;
            }
            if (request is null)
            {
                continue;
            }
            running.RemoveAll(t => t.IsCompleted);
            running.Add(Task.Run(() => Handle(request)));
        }
    }

    private async Task Handle(HostRequest request)
    {
        try
        {
            object result = request.Method switch
            {
                HostMethods.Sdk => Sdk(Read<SdkParams>(request)),
                HostMethods.Evaluate => Evaluate(request.Id, Read<EvaluateParams>(request)),
                HostMethods.Build => await Build(request.Id, Read<BuildParams>(request)),
                _ => throw new InvalidOperationException($"the host has no method {request.Method}"),
            };
            Write(new HostReply { Id = request.Id, Result = JsonSerializer.SerializeToElement(result, result.GetType(), HostJson.Options) });
        }
        catch (Exception error)
        {
            Write(new HostReply { Id = request.Id, Error = error.Message });
        }
    }

    private static T Read<T>(HostRequest request) =>
        request.Params.Deserialize<T>(HostJson.Options) ?? throw new InvalidOperationException($"{request.Method} was sent no parameters");

    private SdkAnswer Sdk(SdkParams asked) =>
        Sdks.For(asked.Folder, out string? problem) is { } sdk ? new SdkAnswer(sdk.MSBuildPath, null) : new SdkAnswer(null, problem);

    private EvaluateDone Evaluate(int id, EvaluateParams asked)
    {
        long began = Stopwatch.GetTimestamp();
        int count = 0;
        Evaluation.Run(asked, evaluated =>
        {
            Interlocked.Increment(ref count);
            Write(new HostReply { Id = id, Partial = JsonSerializer.SerializeToElement(evaluated, HostJson.Options) });
        });
        return new EvaluateDone(count, Stopwatch.GetElapsedTime(began).TotalSeconds);
    }

    private async Task<BuildDone> Build(int id, BuildParams asked)
    {
        long began = Stopwatch.GetTimestamp();
        int count = 0;
        await DesignTimeBuilds.Run(asked, built =>
        {
            Interlocked.Increment(ref count);
            Write(new HostReply { Id = id, Partial = JsonSerializer.SerializeToElement(built, HostJson.Options) });
        });
        return new BuildDone(count, Stopwatch.GetElapsedTime(began).TotalSeconds);
    }

    private void Write(HostReply reply)
    {
        string line = JsonSerializer.Serialize(reply, HostJson.Options);
        lock (writing)
        {
            protocol.WriteLine(line);
        }
    }
}
