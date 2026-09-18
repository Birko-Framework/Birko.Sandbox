using System.Diagnostics;

namespace Birko.Sandbox;

/// <summary>How a check ended.</summary>
internal enum Status
{
    /// <summary>Ran for real and did what it claimed.</summary>
    Ok,

    /// <summary>
    /// Configuration and wiring verified without contacting anything.
    /// </summary>
    /// <remarks>
    /// For a backend that needs a server, this is the honest maximum a smoke harness can assert on a
    /// bare machine: the settings produce a connection string, the store type constructs and accepts
    /// them. It proves the wiring compiles and composes, which is what actually breaks when the
    /// framework changes underneath a consumer. It does NOT prove the backend works - the live suites
    /// in the framework's own CI do that.
    /// </remarks>
    Wiring,

    /// <summary>Cannot be checked here at all - needs hardware or an external account.</summary>
    Skipped,

    Failed,
}

internal sealed record Outcome(Status Status, string? Note = null)
{
    public static readonly Outcome Ok = new(Status.Ok);
    public static Outcome Wiring(string note) => new(Status.Wiring, note);
    public static Outcome Skip(string why) => new(Status.Skipped, why);
    public static Outcome Fail(string why) => new(Status.Failed, why);

    /// <summary>Ok when true, Failed with <paramref name="whenFalse"/> otherwise.</summary>
    public static Outcome From(bool ok, string whenFalse) => ok ? Ok : Fail(whenFalse);
}

internal sealed record Check(string Group, string Name, Func<Task<Outcome>> Run)
{
    public static Check Sync(string group, string name, Func<Outcome> run)
        => new(group, name, () => Task.FromResult(run()));
}

/// <summary>
/// Runs the checks and prints one grouped, timed report.
/// </summary>
/// <remarks>
/// Exit code is the number of failures, so CI can gate on it. Wiring-only and skipped checks never
/// fail the run: a machine without PostgreSQL is not a broken framework, and reporting it as one
/// would train everybody to ignore the output.
/// </remarks>
internal static class Harness
{
    public static async Task<int> RunAsync(IReadOnlyList<Check> checks)
    {
        Console.WriteLine();
        Console.WriteLine("Birko.Sandbox — framework smoke harness");
        Console.WriteLine();

        var results = new List<(Check Check, Outcome Outcome, long Ms)>();

        foreach (var group in checks.GroupBy(c => c.Group))
        {
            Write("  " + group.Key.ToUpperInvariant(), ConsoleColor.White);
            Console.WriteLine();

            foreach (var check in group)
            {
                var sw = Stopwatch.StartNew();
                Outcome outcome;
                try
                {
                    outcome = await check.Run().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    outcome = Outcome.Fail($"{ex.GetType().Name}: {ex.Message}");
                }
                sw.Stop();

                results.Add((check, outcome, sw.ElapsedMilliseconds));
                Report(check, outcome, sw.ElapsedMilliseconds);
            }

            Console.WriteLine();
        }

        return Summarise(results);
    }

    private static void Report(Check check, Outcome outcome, long ms)
    {
        var (tag, colour) = outcome.Status switch
        {
            Status.Ok => ("OK  ", ConsoleColor.Green),
            Status.Wiring => ("CFG ", ConsoleColor.Cyan),
            Status.Skipped => ("--  ", ConsoleColor.DarkGray),
            _ => ("FAIL", ConsoleColor.Red),
        };

        Console.Write("    [");
        Write(tag, colour);
        Console.Write("] ");

        var name = check.Name.Length > 42 ? check.Name[..42] : check.Name.PadRight(42);
        Console.Write(name);

        // timing is noise for anything that did not actually run
        if (outcome.Status is Status.Ok or Status.Wiring)
        {
            Write($"{ms,6} ms", ConsoleColor.DarkGray);
        }

        if (outcome.Note is { Length: > 0 } note)
        {
            Write($"  {note}", outcome.Status == Status.Failed ? ConsoleColor.Red : ConsoleColor.DarkGray);
        }

        Console.WriteLine();
    }

    private static int Summarise(List<(Check Check, Outcome Outcome, long Ms)> results)
    {
        int ok = results.Count(r => r.Outcome.Status == Status.Ok);
        int wiring = results.Count(r => r.Outcome.Status == Status.Wiring);
        int skipped = results.Count(r => r.Outcome.Status == Status.Skipped);
        var failures = results.Where(r => r.Outcome.Status == Status.Failed).ToList();

        Console.WriteLine(new string('-', 64));
        Write($"  {ok} verified", ConsoleColor.Green);
        Console.Write(" · ");
        Write($"{wiring} wiring-only", ConsoleColor.Cyan);
        Console.Write(" · ");
        Write($"{skipped} skipped", ConsoleColor.DarkGray);
        Console.Write(" · ");
        Write($"{failures.Count} failed", failures.Count == 0 ? ConsoleColor.DarkGray : ConsoleColor.Red);
        Console.WriteLine();

        if (failures.Count > 0)
        {
            Console.WriteLine();
            Write("  FAILURES", ConsoleColor.Red);
            Console.WriteLine();
            foreach (var (check, outcome, _) in failures)
            {
                Console.WriteLine($"    {check.Group} / {check.Name}");
                Console.WriteLine($"      {outcome.Note}");
            }
        }

        Console.WriteLine();
        return failures.Count;
    }

    private static void Write(string text, ConsoleColor colour)
    {
        // Redirected output (CI logs, a file) has no usable colour, and setting it there emits
        // escape codes nobody reads.
        if (Console.IsOutputRedirected)
        {
            Console.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = colour;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }
}
