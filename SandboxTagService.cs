using Birko.Configuration;
using Birko.Data.InMemory.Stores;
using Birko.Data.Tagging;

namespace Birko.Sandbox;

/// <summary>
/// The smallest real <see cref="TagServiceBase"/> a consumer can write: twelve data-access hooks over
/// two InMemory stores.
/// </summary>
/// <remarks>
/// <para>
/// This is not a stub. <see cref="TagServiceBase"/> owns the behaviour worth checking — de-duplicating
/// a tag by name, stamping <c>TenantGuid</c> on every insert, refusing a tag that belongs to another
/// tenant — and a backend is the only thing it needs from a consumer. Writing one here is exactly what
/// the README tells a reader to do, so the check measures the framework rather than a fake.
/// </para>
/// <para>
/// ⚠ The tenant-scoping contract is the implementer's, not the base's, and it is easy to get wrong:
/// the base stamps <c>TenantGuid</c> on writes, but every read and delete hook below receives no
/// tenant argument, so each one has to scope itself. An unscoped hook returns and deletes other
/// tenants' rows. That is why <see cref="GetTagByIdAsync"/> filters too, even though a Guid lookup
/// looks unambiguous.
/// </para>
/// </remarks>
public sealed class SandboxTagService : TagServiceBase
{
    private readonly InMemoryStore<Tag> _tags = new();
    private readonly InMemoryStore<EntityTag> _links = new();
    private readonly Guid _tenant;

    public SandboxTagService(Guid tenant)
    {
        _tenant = tenant;
        _tags.SetSettings(new Settings { Location = "memory://tags", Name = "tags" });
        _links.SetSettings(new Settings { Location = "memory://tags", Name = "links" });
    }

    protected override Guid GetCurrentTenantId() => _tenant;

    // ── tags ────────────────────────────────────────────────────────────────────────────────────

    protected override Task<Tag> CreateTagInternalAsync(Tag tag, CancellationToken ct)
    {
        tag.Guid = _tags.Create(tag);
        return Task.FromResult(tag);
    }

    protected override Task<Tag?> GetTagByIdAsync(Guid tagId, CancellationToken ct)
        => Task.FromResult(Owned().FirstOrDefault(t => t.Guid == tagId));

    protected override Task<Tag?> FindTagByNameAsync(string name, CancellationToken ct)
        => Task.FromResult(Owned().FirstOrDefault(t => t.Name == name));

    protected override Task<IReadOnlyList<Tag>> ListAllTagsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Tag>>(Owned().ToList());

    protected override Task<IReadOnlyList<Tag>> SearchTagsByNameAsync(string query, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Tag>>(
            Owned().Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList());

    protected override Task UpdateTagInternalAsync(Tag tag, CancellationToken ct)
    {
        _tags.Update(tag);
        return Task.CompletedTask;
    }

    protected override Task DeleteTagInternalAsync(Tag tag, CancellationToken ct)
    {
        _tags.Delete(tag);
        return Task.CompletedTask;
    }

    // ── entity links ────────────────────────────────────────────────────────────────────────────

    protected override Task<IReadOnlyList<EntityTag>> GetEntityTagLinksAsync(string entityType, Guid entityId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EntityTag>>(
            OwnedLinks().Where(l => l.EntityType == entityType && l.EntityId == entityId).ToList());

    protected override Task CreateEntityTagAsync(EntityTag link, CancellationToken ct)
    {
        _links.Create(link);
        return Task.CompletedTask;
    }

    protected override Task DeleteEntityTagAsync(EntityTag link, CancellationToken ct)
    {
        _links.Delete(link);
        return Task.CompletedTask;
    }

    protected override Task DeleteAllEntityTagsForTagAsync(Guid tagId, CancellationToken ct)
    {
        foreach (var link in OwnedLinks().Where(l => l.TagId == tagId).ToList())
        {
            _links.Delete(link);
        }
        return Task.CompletedTask;
    }

    protected override Task<IReadOnlyList<EntityTag>> GetEntityTagLinksBatchAsync(
        string entityType, IReadOnlyList<Guid> entityIds, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EntityTag>>(
            OwnedLinks().Where(l => l.EntityType == entityType && entityIds.Contains(l.EntityId)).ToList());

    // Every read and delete hook goes through these two: the tenant term is the implementer's
    // obligation, and putting it in one place per entity is what stops a hook forgetting it.
    private IEnumerable<Tag> Owned() => _tags.Read().Where(t => t.TenantGuid == _tenant);

    private IEnumerable<EntityTag> OwnedLinks() => _links.Read().Where(l => l.TenantGuid == _tenant);
}
