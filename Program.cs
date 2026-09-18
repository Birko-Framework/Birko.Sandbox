using Birko.AI.Factories;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Caching;
using Birko.Caching.Memory;
using Birko.Communication.GraphQL;
using Birko.Communication.REST;
using Birko.Configuration;
using Birko.Data.Composition;
using Birko.Data.InMemory.Stores;
using Birko.Data.JSON.Stores;
using Birko.Data.Migrations.SQL;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.Models;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.XML.Stores;
using Birko.Health;
using Birko.Health.Checks;
using Birko.Models.ValueObjects;
using Birko.Security;
using Birko.Security.BCrypt.Hashing;
using Birko.Serialization;
using Birko.Serialization.Newtonsoft;
using Birko.Time;
using Birko.Workflow.Definition;
using Birko.Workflow.Execution;

namespace Birko.Sandbox;

/// <summary>
/// Birko Framework smoke harness — the "first test place".
/// </summary>
/// <remarks>
/// Wires up a slice of every layer and does a small round-trip, so a framework change that breaks a
/// CONSUMER shows up here rather than in somebody's application. It is deliberately not a test
/// suite - the framework's own 167 test projects do that. What this proves is that the pieces still
/// compose from OUTSIDE, through the same $(BirkoSrc) / .projitems mechanism the wiki tells readers
/// to use.
///
/// Three outcomes, and the middle one is the point:
///   OK   - ran for real.
///   CFG  - settings and wiring verified without contacting anything. The honest maximum for a
///          backend that needs a server, and still the thing that actually breaks when the framework
///          moves underneath a consumer.
///   --   - needs hardware or an external account; cannot be checked here at all.
///
/// Add a check by writing a method and putting it in the list below.
/// </remarks>
internal static class Program
{
    private static Task<int> Main() => Harness.RunAsync(Checks);

    private static readonly IReadOnlyList<Check> Checks = new[]
    {
        // ── core ────────────────────────────────────────────────────────────────────────────────
        Check.Sync("core", "Configuration / settings identity", CoreConfiguration),
        Check.Sync("core", "Time / date-time provider", CoreTime),
        Check.Sync("core", "Serialization round-trip (Newtonsoft)", CoreSerialization),
        Check.Sync("core", "Models / Money value object", CoreValueObjects),

        // ── data ────────────────────────────────────────────────────────────────────────────────
        Check.Sync("data", "Stores / InMemory CRUD", DataInMemory),
        Check.Sync("data", "Stores / InMemory bulk + filter delete", DataInMemoryBulk),
        Check.Sync("data", "Stores / ordering + paging", DataOrdering),
        Check.Sync("data", "Stores / JSON file round-trip", DataJson),
        Check.Sync("data", "Stores / XML file round-trip", DataXml),
        Check.Sync("data", "Stores / SQLite CRUD (file-backed)", DataSqlite),
        Check.Sync("data", "Stores / SQLite decimal precision", DataSqliteMapping),
        Check.Sync("data", "Stores / whole-table write is refused", DataWholeTableGuard),
        Check.Sync("data", "Migrations / SQL runner applies a version", DataMigrations),
        new Check("data", "Decorators / injected clock stamps rows", DataDecoratorsAsync),
        new Check("data", "Caching / get, set, get-or-set", DataCachingAsync),

        // ── data needing a server: wiring only ──────────────────────────────────────────────────
        Check.Sync("data (server-backed)", "PostgreSQL settings + connection string", () => SqlWiring("PostgreSQL")),
        Check.Sync("data (server-backed)", "MySQL settings + connection string", () => SqlWiring("MySQL")),
        Check.Sync("data (server-backed)", "SQL Server settings + connection string", () => SqlWiring("MSSql")),

        // ── services ────────────────────────────────────────────────────────────────────────────
        Check.Sync("services", "Workflow build + transition", ServicesWorkflow),
        new Check("services", "Background job enqueue/dequeue", ServicesJobsAsync),
        new Check("services", "Health checks / runner reports", ServicesHealthAsync),
        Check.Sync("services", "AI provider factory wiring (no call)", ServicesAiWiring),

        // ── communication: wiring only, nothing is sent ─────────────────────────────────────────
        Check.Sync("communication", "REST client wiring (no request sent)", CommsRest),
        Check.Sync("communication", "GraphQL request build + settings", CommsGraphQL),

        // ── security ────────────────────────────────────────────────────────────────────────────
        Check.Sync("security", "BCrypt hash + verify", SecurityBCrypt),
    };

    // ── core ────────────────────────────────────────────────────────────────────────────────────

    private static Outcome CoreConfiguration()
    {
        var settings = new Settings { Name = "sandbox", Location = "memory://sandbox" };
        return Outcome.From(
            settings.Name == "sandbox" && !string.IsNullOrEmpty(settings.GetId()),
            "settings did not produce an identity");
    }

    private static Outcome CoreTime()
    {
        var clock = new SystemDateTimeProvider();
        var now = clock.UtcNow;
        return Outcome.From(now.Year > 2000, $"expected a plausible instant, got {now:O}");
    }

    private static Outcome CoreSerialization()
    {
        ISerializer serializer = new NewtonsoftJsonSerializer();
        var json = serializer.Serialize(new SandboxEntity { Name = "beta", Value = 7 });
        if (string.IsNullOrWhiteSpace(json)) return Outcome.Fail("serialize produced nothing");

        var back = serializer.Deserialize<SandboxEntity>(json);
        return Outcome.From(back is { Name: "beta", Value: 7 }, "round-trip lost the values");
    }

    private static Outcome CoreValueObjects()
    {
        // Money carries its currency, so the type refuses a mistake a raw decimal cannot see.
        var total = new Money(10.50m, "EUR").Add(new Money(4.50m, "EUR"));
        if (total.Amount != 15.00m || total.CurrencyCode != "EUR")
            return Outcome.Fail($"expected 15.00 EUR, got {total}");

        try
        {
            new Money(1m, "EUR").Add(new Money(1m, "USD"));
            return Outcome.Fail("adding two currencies was allowed");
        }
        catch (InvalidOperationException)
        {
            return Outcome.Ok;
        }
    }

    // ── data ────────────────────────────────────────────────────────────────────────────────────

    private static Outcome DataInMemory()
    {
        var store = new InMemoryStore<SandboxEntity>();

        var id = store.Create(new SandboxEntity { Name = "alpha", Value = 1 });
        if (id == Guid.Empty || store.Count() != 1) return Outcome.Fail("create did not persist");

        var read = store.Read(id);
        if (read is null || read.Name != "alpha") return Outcome.Fail("read did not return the row");

        read.Value = 42;
        store.Update(read);
        if (store.Read(id)?.Value != 42) return Outcome.Fail("update did not stick");

        store.Delete(read);
        return Outcome.From(store.Read(id) is null && store.Count() == 0, "delete did not remove the row");
    }

    private static Outcome DataInMemoryBulk()
    {
        var store = new InMemoryStore<SandboxEntity>();
        store.Create(new[]
        {
            new SandboxEntity { Name = "x", Value = 1 },
            new SandboxEntity { Name = "y", Value = 2 },
            new SandboxEntity { Name = "z", Value = 3 },
        });
        if (store.Count() != 3) return Outcome.Fail("bulk create did not persist all three");

        store.Delete(e => e.Value > 2);
        return Outcome.From(store.Count() == 2, $"filter delete left {store.Count()}, expected 2");
    }

    private static Outcome DataOrdering()
    {
        var store = new InMemoryStore<SandboxEntity>();
        foreach (var n in new[] { "c", "a", "b" })
            store.Create(new SandboxEntity { Name = n, Value = 0 });

        var page = store.Read(null, Birko.Data.Stores.OrderBy<SandboxEntity>.By(e => e.Name), 2, 0).ToList();
        return Outcome.From(page.Count == 2 && page[0].Name == "a",
            $"expected [a,b], got [{string.Join(",", page.Select(p => p.Name))}]");
    }

    private static Outcome DataJson() => InTempDirectory(dir =>
    {
        var store = new JsonStore<SandboxEntity>();
        store.SetSettings(new Settings(dir, "entities.json"));
        var id = store.Create(new SandboxEntity { Name = "json", Value = 1 });

        // A second store over the same file proves it really reached disk, not a field.
        var reader = new JsonStore<SandboxEntity>();
        reader.SetSettings(new Settings(dir, "entities.json"));

        return Outcome.From(reader.Read(id)?.Name == "json", "the row did not survive the file");
    });

    private static Outcome DataXml() => InTempDirectory(dir =>
    {
        var store = new XmlStore<SandboxEntity>();
        store.SetSettings(new Settings(dir, "entities.xml"));
        var id = store.Create(new SandboxEntity { Name = "xml", Value = 2 });

        var reader = new XmlStore<SandboxEntity>();
        reader.SetSettings(new Settings(dir, "entities.xml"));

        return Outcome.From(reader.Read(id)?.Value == 2, "the row did not survive the file");
    });

    private static Outcome DataSqlite() => InTempDirectory(dir =>
    {
        var store = new SQLiteStore<SandboxRow>();
        store.SetSettings(new SqLiteSettings(dir, "sandbox.db"));

        // the table is created on first use - no migration, no setup step
        var id = store.Create(new SandboxRow { Name = "widget", Price = 9.99m });
        if (id == Guid.Empty) return Outcome.Fail("create returned no id");
        if (store.Read(id)?.Name != "widget") return Outcome.Fail("read did not return the row");

        store.Create(new SandboxRow { Name = "gadget", Price = 24.00m });
        var cheap = store.Read(r => r.Price < 10m, null, null, null).ToList();

        return Outcome.From(store.Count() == 2 && cheap.Count == 1 && cheap[0].Name == "widget",
            $"expected 2 rows and 1 match, got {store.Count()} and {cheap.Count}");
    });

    private static Outcome DataSqliteMapping() => InTempDirectory(dir =>
    {
        // PrecisionField + ScaleField is the pair that decides whether money survives; a precision
        // without a scale silently truncates to whole units on some providers.
        var store = new SQLiteStore<SandboxRow>();
        store.SetSettings(new SqLiteSettings(dir, "mapping.db"));

        var id = store.Create(new SandboxRow { Name = "precise", Price = 12.34m });
        var back = store.Read(id);

        return Outcome.From(back?.Price == 12.34m, $"expected 12.34, got {back?.Price}");
    });

    private static Outcome DataWholeTableGuard() => InTempDirectory(dir =>
    {
        var store = new SQLiteStore<SandboxRow>();
        store.SetSettings(new SqLiteSettings(dir, "guard.db"));
        store.Create(new SandboxRow { Name = "keep", Price = 1m });

        // A filter that reduces to "everything" must be refused - say DeleteAll() if you mean it.
        var empty = new List<string>();
        try
        {
            store.Delete(r => !empty.Contains(r.Name!));
            return Outcome.Fail("a whole-table delete was NOT refused");
        }
        catch (Birko.Data.Exceptions.WholeTableWriteException)
        {
            return Outcome.From(store.Count() == 1, "the row was destroyed despite the refusal");
        }
    });

    private static Outcome DataMigrations() => InTempDirectory(dir =>
    {
        // Stores create their own table on first use; a migration is how you version a schema
        // deliberately instead. UseTransaction = false because this migration drives the connector's
        // own connection - an outer transaction on a second connection would lock the SQLite file.
        var connector = DataBase.GetConnector<SqLiteConnector>(new SqLiteSettings(dir, "migrated.db"));
        var runner = new SqlMigrationRunner(connector, new SqlMigrationSettings { UseTransaction = false });
        runner.RegisterMigrations(new CreateTablesMigration(connector, new[] { typeof(SandboxRow) }));
        runner.Initialize();

        var result = runner.Migrate();
        if (!result.Success) return Outcome.Fail($"migrate failed: {result.ErrorMessage}");

        return Outcome.From(runner.CurrentVersion == 1, $"expected version 1, got {runner.CurrentVersion}");
    });

    private static async Task<Outcome> DataDecoratorsAsync()
    {
        // StoreWrapperBuilder inspects T and applies only the decorators it implements: SandboxLogRow
        // is ITimestamped, so the timestamp wrapper goes on and stamps from the clock it is handed.
        // That is the point of the layer - the framework's clock rather than a DateTime.UtcNow buried
        // in a store, which is what makes the behaviour reachable from a consumer at all.
        var clock = new TestDateTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tenants = new TenantContext();

        var store = StoreWrapperBuilder.Build<SandboxLogRow>(
            new AsyncInMemoryStore<SandboxLogRow>(), clock, tenantContext: tenants);

        var id = await store.CreateAsync(new SandboxLogRow { Name = "stamped" });
        var row = await store.ReadAsync(id);

        return Outcome.From(row?.CreatedAt.Year == 2030,
            $"expected the injected clock's 2030, got {row?.CreatedAt:O}");
    }

    private static async Task<Outcome> DataCachingAsync()
    {
        using var cache = new MemoryCache();

        await cache.SetAsync("k", 41);
        var hit = await cache.GetAsync<int>("k");
        if (!hit.HasValue || hit.Value != 41) return Outcome.Fail("set/get did not round-trip");

        var miss = await cache.GetAsync<int>("absent");
        if (miss.HasValue) return Outcome.Fail("an absent key reported a hit");

        // GetOrSet must run the factory once and serve the stored value afterwards.
        var calls = 0;
        await cache.GetOrSetAsync("lazy", _ => { calls++; return Task.FromResult(7); });
        var second = await cache.GetOrSetAsync("lazy", _ => { calls++; return Task.FromResult(7); });

        return Outcome.From(calls == 1 && second == 7, $"factory ran {calls} times, expected 1");
    }

    private static Outcome SqlWiring(string provider)
    {
        // Nothing is contacted. What this asserts is that the settings type still composes a
        // connection string and still carries the shape the store expects - which is what breaks
        // when the framework moves under a consumer.
        var connection = provider switch
        {
            "PostgreSQL" => new PostgreSqlSettings { Location = "localhost", Name = "sandbox", UserName = "u", Password = "p", Port = 5432 }.GetConnectionString(),
            "MySQL" => new MySqlSettings { Location = "localhost", Name = "sandbox", UserName = "u", Password = "p", Port = 3306 }.GetConnectionString(),
            _ => new MSSqlSettings { Location = "localhost", Name = "sandbox", UserName = "u", Password = "p", Port = 1433 }.GetConnectionString(),
        };

        return string.IsNullOrWhiteSpace(connection)
            ? Outcome.Fail($"{provider} settings produced no connection string")
            : Outcome.Wiring("settings ok, not contacted");
    }

    // ── services ────────────────────────────────────────────────────────────────────────────────

    private static Outcome ServicesWorkflow()
    {
        var workflow = new WorkflowBuilder<WorkflowData>("Sandbox")
            .InitialState("Start")
            .State("Start").And()
            .State("Done").IsFinal().And()
            .Transition("go", "Start", "Done").And()
            .Build();

        var instance = WorkflowInstance<WorkflowData>.Create(workflow, new WorkflowData());
        var result = new WorkflowEngine().FireAsync(workflow, instance, "go").GetAwaiter().GetResult();

        return Outcome.From(result.IsSuccess && instance.CurrentState == "Done",
            $"expected Done, got {instance.CurrentState}");
    }

    private static async Task<Outcome> ServicesJobsAsync()
    {
        var queue = new InMemoryJobQueue(new SystemDateTimeProvider());

        var jobId = await queue.EnqueueAsync(new JobDescriptor { JobType = nameof(SandboxJob) });
        if (jobId == Guid.Empty) return Outcome.Fail("enqueue returned no id");

        var dequeued = await queue.DequeueAsync();
        if (dequeued?.Id != jobId) return Outcome.Fail("dequeued a different job");

        SandboxJob.Ran = false;
        await new SandboxJob().ExecuteAsync(new JobContext(dequeued.Id, 1, dequeued.EnqueuedAt));
        await queue.CompleteAsync(jobId);

        return Outcome.From(SandboxJob.Ran, "the job did not execute");
    }

    private static async Task<Outcome> ServicesHealthAsync()
    {
        var runner = new HealthCheckRunner()
            .Register("memory", new MemoryHealthCheck())
            .Register("always-ok", new AlwaysHealthyCheck());

        var report = await runner.RunAsync();

        if (report.Entries.Count != 2) return Outcome.Fail($"expected 2 entries, got {report.Entries.Count}");
        return Outcome.From(report.Entries["always-ok"].Status == HealthStatus.Healthy,
            $"the always-healthy check reported {report.Entries["always-ok"].Status}");
    }

    private static Outcome ServicesAiWiring()
    {
        const string name = "sandbox-stub";
        LlmProviderFactory.Register(name, _ => new SandboxLlmProvider());
        if (!LlmProviderFactory.IsRegistered(name)) return Outcome.Fail("registration did not take");

        var provider = LlmProviderFactory.Create(name);
        return provider?.Name == name
            ? Outcome.Wiring("factory ok, no model called")
            : Outcome.Fail("the factory returned the wrong provider");
    }

    // ── communication ───────────────────────────────────────────────────────────────────────────

    private static Outcome CommsRest()
    {
        // Constructed and configured only - no request is issued, so this needs no server and still
        // catches what actually breaks: the client no longer composing from outside.
        using var client = new RestClient("https://example.invalid/api");
        client.DefaultHeaders["X-Sandbox"] = "1";
        client.Timeout = 5000;

        return client.BaseURI.StartsWith("https://example.invalid", StringComparison.Ordinal)
               && client.DefaultHeaders["X-Sandbox"] == "1"
               && client.Timeout == 5000
            ? Outcome.Wiring("client configured, nothing sent")
            : Outcome.Fail("the client did not keep its configuration");
    }

    private static Outcome CommsGraphQL()
    {
        var settings = new GraphQLSettings { Location = "example.invalid", Port = 443, UseSecure = true };
        if (string.IsNullOrWhiteSpace(settings.Endpoint)) return Outcome.Fail("settings produced no endpoint");

        var request = new GraphQLRequestBuilder()
            .Query("query Ping($id: ID!) { node(id: $id) { id } }")
            .Variables(new Dictionary<string, object?> { ["id"] = "1" })
            .OperationName("Ping")
            .Build();

        return request.OperationName == "Ping" && request.Query.Contains("node", StringComparison.Ordinal)
            ? Outcome.Wiring($"request built for {settings.Endpoint}")
            : Outcome.Fail("the builder produced the wrong request");
    }

    // ── security ────────────────────────────────────────────────────────────────────────────────

    private static Outcome SecurityBCrypt()
    {
        IPasswordHasher hasher = new BCryptPasswordHasher();
        const string password = "correct horse battery staple";

        var hash = hasher.Hash(password);
        if (string.IsNullOrWhiteSpace(hash) || hash == password)
            return Outcome.Fail("the hash is empty or is the plain value");

        return Outcome.From(hasher.Verify(password, hash) && !hasher.Verify("wrong", hash),
            "verify accepted a wrong password or rejected the right one");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="body"/> against a throwaway directory and always cleans up.</summary>
    private static Outcome InTempDirectory(Func<string, Outcome> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "birko-sandbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            return body(dir);
        }
        finally
        {
            // SQLite may still hold the file; a leftover temp directory is not worth failing a run.
            try { Directory.Delete(dir, recursive: true); } catch { /* ignored */ }
        }
    }
}

/// <summary>Entity for the store and serialization checks.</summary>
/// <remarks>
/// Public, not internal, and that is a constraint of the XML backend rather than a style choice:
/// System.Xml.Serialization refuses a non-public type outright ("Only public types can be
/// processed"), so an internal entity works everywhere else and fails only there. The harness found
/// this by running - which is the sort of thing it exists for.
/// </remarks>
public sealed class SandboxEntity : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

/// <summary>Timestamped entity for the decorator check — AbstractLogModel is ITimestamped.</summary>
internal sealed class SandboxLogRow : AbstractLogModel
{
    public string? Name { get; set; }
}

/// <summary>Attribute-mapped row for the SQLite and migration checks.</summary>
[Table("SandboxRows")]
internal sealed class SandboxRow : AbstractDatabaseModel
{
    [MaxLengthField(200)]
    public string? Name { get; set; }

    [PrecisionField(18), ScaleField(2)]
    public decimal Price { get; set; }
}

/// <summary>Health check that always passes — proves the runner collects and reports.</summary>
internal sealed class AlwaysHealthyCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
        => Task.FromResult(HealthCheckResult.Healthy("sandbox"));
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

/// <summary>Stub provider — registered and created to prove the factory wiring; never invoked.</summary>
internal sealed class SandboxLlmProvider : ILlmProvider
{
    public string Name => "sandbox-stub";
    public Action<string, string>? MessageCallback { get; set; }

    public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("smoke harness — no live LLM calls");

    public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("smoke harness — no live LLM calls");
}
