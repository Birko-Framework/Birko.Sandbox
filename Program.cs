using Birko.AI.Factories;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Configuration;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Serialization;
using Birko.Serialization.Newtonsoft;
using Birko.Time;
using Birko.Workflow.Definition;
using Birko.Workflow.Execution;

namespace Birko.Sandbox;

/// <summary>
/// Birko Framework integration smoke harness — the "first test place". Wires up a representative
/// slice of each layer and does a tiny round-trip, asserting success. Prints one OK/FAIL line per
/// layer; exits non-zero on any failure (CI-usable). Add a layer as a <c>Try*()</c> method and
/// register it in <see cref="Checks"/>.
/// </summary>
internal static class Program
{
    private static readonly (string Name, Func<Task<bool>> Run)[] Checks =
    {
        ("Configuration",                  () => Task.FromResult(TryConfiguration())),
        ("Stores / InMemory CRUD",         () => Task.FromResult(TryInMemoryStore())),
        ("Serialization round-trip",       () => Task.FromResult(TrySerialization())),
        ("Workflow build + run",           TryWorkflowAsync),
        ("Background job enqueue/process", TryBackgroundJobAsync),
        ("AI provider wiring (no call)",   () => Task.FromResult(TryAiWiring())),
    };

    private static async Task<int> Main()
    {
        Console.WriteLine("Birko.Sandbox — framework smoke harness");
        Console.WriteLine(new string('-', 48));

        int failed = 0;
        foreach (var (name, run) in Checks)
        {
            bool ok;
            string? detail = null;
            try { ok = await run(); }
            catch (Exception ex) { ok = false; detail = $"{ex.GetType().Name}: {ex.Message}"; }

            Console.WriteLine($"  [{(ok ? "OK  " : "FAIL")}] {name}{(detail is null ? "" : $"  — {detail}")}");
            if (!ok) failed++;
        }

        Console.WriteLine(new string('-', 48));
        Console.WriteLine(failed == 0
            ? $"All {Checks.Length} smoke checks passed."
            : $"{failed} of {Checks.Length} smoke checks FAILED.");
        return failed == 0 ? 0 : 1;
    }

    // ── Configuration ─────────────────────────────────────────────────────────
    private static bool TryConfiguration()
    {
        var settings = new Settings { Name = "sandbox", Location = "memory://sandbox" };
        return settings.Name == "sandbox" && !string.IsNullOrEmpty(settings.GetId());
    }

    // ── Stores / InMemory CRUD ──────────────────────────────────────────────────
    private static bool TryInMemoryStore()
    {
        var store = new InMemoryStore<SandboxEntity>();

        var entity = new SandboxEntity { Name = "alpha", Value = 1 };
        var id = store.Create(entity);
        if (id == Guid.Empty || store.Count() != 1) return false;

        var read = store.Read(id);
        if (read is null || read.Name != "alpha") return false;

        read.Value = 42;
        store.Update(read);
        if (store.Read(id)?.Value != 42) return false;

        store.Delete(read);
        return store.Read(id) is null && store.Count() == 0;
    }

    // ── Serialization round-trip ─────────────────────────────────────────────────
    private static bool TrySerialization()
    {
        ISerializer serializer = new NewtonsoftJsonSerializer();
        var original = new SandboxEntity { Name = "beta", Value = 7 };

        var json = serializer.Serialize(original);
        if (string.IsNullOrWhiteSpace(json)) return false;

        var back = serializer.Deserialize<SandboxEntity>(json);
        return back is not null && back.Name == "beta" && back.Value == 7;
    }

    // ── Workflow build + run ─────────────────────────────────────────────────────
    private static async Task<bool> TryWorkflowAsync()
    {
        var workflow = new WorkflowBuilder<WorkflowData>("Sandbox")
            .InitialState("Start")
            .State("Start").And()
            .State("Done").IsFinal().And()
            .Transition("go", "Start", "Done").And()
            .Build();

        var engine = new WorkflowEngine();
        var instance = WorkflowInstance<WorkflowData>.Create(workflow, new WorkflowData());

        var result = await engine.FireAsync(workflow, instance, "go");
        return result.IsSuccess && instance.CurrentState == "Done";
    }

    // ── Background job enqueue/process ───────────────────────────────────────────
    private static async Task<bool> TryBackgroundJobAsync()
    {
        var queue = new InMemoryJobQueue(new SystemDateTimeProvider());

        var jobId = await queue.EnqueueAsync(new JobDescriptor { JobType = nameof(SandboxJob) });
        if (jobId == Guid.Empty) return false;

        var dequeued = await queue.DequeueAsync();
        if (dequeued is null || dequeued.Id != jobId) return false;

        SandboxJob.Ran = false;
        await new SandboxJob().ExecuteAsync(new JobContext(dequeued.Id, 1, dequeued.EnqueuedAt));
        await queue.CompleteAsync(jobId);

        return SandboxJob.Ran;
    }

    // ── AI provider wiring (no live call) ────────────────────────────────────────
    private static bool TryAiWiring()
    {
        LlmProviderFactory.Register("sandbox-stub", _ => new SandboxLlmProvider());
        if (!LlmProviderFactory.IsRegistered("sandbox-stub")) return false;

        var provider = LlmProviderFactory.Create("sandbox-stub");
        return provider is not null && provider.Name == "sandbox-stub";
    }
}

/// <summary>Tiny entity used across the store + serialization checks.</summary>
internal sealed class SandboxEntity : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

/// <summary>Workflow payload for the workflow check (no fields needed).</summary>
internal sealed class WorkflowData { }

/// <summary>Job exercised by the background-job check.</summary>
internal sealed class SandboxJob : IJob
{
    public static bool Ran;

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        Ran = true;
        return Task.CompletedTask;
    }
}

/// <summary>Stub LLM provider — registered/created to prove the factory wiring; never invoked.</summary>
internal sealed class SandboxLlmProvider : ILlmProvider
{
    public string Name => "sandbox-stub";
    public Action<string, string>? MessageCallback { get; set; }

    public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("smoke harness — no live LLM calls");

    public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("smoke harness — no live LLM calls");
}
