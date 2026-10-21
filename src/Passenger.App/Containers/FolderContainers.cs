namespace Passenger.App.Containers;

/// <summary>What one open folder had the engine start, ended with the folder (<see cref="ProjectSession"/>'s end): each
/// container removed and each Compose project taken down. Its <see cref="Labels"/> go on all of it, so what a crash
/// leaves is removed at the next start (<see cref="ContainerLabels"/>). A UI-thread object of the session's.</summary>
public sealed class FolderContainers(string? folder, ContainerEngine engine)
{
    private readonly List<string> containers = [];
    private readonly List<IReadOnlyList<string>> projects = [];

    /// <summary>The engine the folder's window uses.</summary>
    public ContainerEngine Engine { get; } = engine;

    /// <summary>The <c>--label</c> words for what the folder starts: the state's, the process's and the folder's.</summary>
    public IReadOnlyList<string> Labels => ContainerLabels.Arguments(Engine, folder);

    /// <summary>The containers and Compose projects started for the folder and not yet ended.</summary>
    public int Count => containers.Count + projects.Count;

    /// <summary>A container started for the folder, removed with it.</summary>
    public void Started(string container)
    {
        UiThread.Check();
        containers.Add(container);
    }

    /// <summary>A Compose project brought up for the folder (its <c>compose -p NAME -f …</c> words), taken down with it.</summary>
    public void StartedProject(IReadOnlyList<string> compose)
    {
        UiThread.Check();
        projects.Add(compose);
    }

    /// <summary>A container or project ended by other means (its Stop), which the folder's end leaves alone.</summary>
    public void Ended(string container) => containers.Remove(container);

    /// <summary>A Compose project taken down by other means (a run's Stop), which the folder's end leaves alone.</summary>
    public void EndedProject(IReadOnlyList<string> compose)
    {
        UiThread.Check();
        projects.RemoveAll(p => p.SequenceEqual(compose));
    }

    /// <summary>Whether a Compose project (its words) is one the folder started and has yet to end.</summary>
    public bool Holds(IReadOnlyList<string> compose) => projects.Any(p => p.SequenceEqual(compose));

    /// <summary>Ends what the folder started: its containers removed and its projects taken down, off the UI thread, done
    /// once the engine has answered (or given up after its limit).</summary>
    public Task EndAsync()
    {
        string[] removing = [.. containers];
        IReadOnlyList<string>[] downing = [.. projects];
        containers.Clear();
        projects.Clear();
        if (removing.Length == 0 && downing.Length == 0)
        {
            return Task.CompletedTask;
        }
        // Not held: nothing it does is drawn, and the process waits for it once the window's loop has stopped.
        return Task.Run(async () =>
        {
            List<Task> ending = [.. downing.Select(project => Quietly([.. project, "down", "--remove-orphans"]))];
            if (removing.Length > 0)
            {
                ending.Add(Quietly(["rm", "-f", .. removing]));
            }
            await Task.WhenAll(ending);
        });

        async Task Quietly(IReadOnlyList<string> words)
        {
            try
            {
                Core.ChildResult result = await Engine.RunAsync(words, ContainerEngine.AskLimit * 3);
                if (!result.Succeeded)
                {
                    IdeErrors.Write($"Containers: {ContainerEngine.Words(words)} failed as the folder closed: {ContainerEngine.Said(result)}");
                }
            }
            catch (Exception failed) when (failed is TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception)
            {
                IdeErrors.Write($"Containers: {ContainerEngine.Words(words)} failed as the folder closed: {failed.Message}");
            }
        }
    }
}
