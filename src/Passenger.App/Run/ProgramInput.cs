using Passenger.App.ToolWindows;

namespace Passenger.App.Run;

/// <summary>A program's standard input as a console writes it: what the person typed, in order, then its end, written
/// off the UI thread so a program that is not reading never holds the window. A program gone takes what was left.</summary>
public static class ProgramInput
{
    public static ConsoleInput Of(StreamWriter writer)
    {
        Task writing = Task.CompletedTask;
        void Then(Func<Task> next) => writing = writing.ContinueWith(async _ =>
        {
            try
            {
                await next();
            }
            catch (Exception gone) when (gone is IOException or ObjectDisposedException)
            {
                // The program has ended, or closed its input.
            }
        }, TaskScheduler.Default).Unwrap();
        return new ConsoleInput(
            text => Then(async () =>
            {
                await writer.WriteAsync(text);
                await writer.FlushAsync();
            }),
            () => Then(() =>
            {
                writer.Close();
                return Task.CompletedTask;
            }));
    }
}
