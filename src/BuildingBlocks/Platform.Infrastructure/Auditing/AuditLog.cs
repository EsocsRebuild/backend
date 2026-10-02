using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Application.Tenancy;

namespace Platform.Infrastructure.Auditing;

/// <summary>Scoped buffer of audit events; drained by the save interceptor or <see cref="FlushAsync"/>.</summary>
public sealed class AuditLog(
    ITenantContext tenant,
    ICurrentUser user,
    IRequestInfo request,
    IHttpContextAccessor http,
    IServiceProvider services,
    TimeProvider clock) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<AuditEvent> _pending = [];

    public void Record(string action, string summary, AuditSeverity severity = AuditSeverity.Info, AuditTarget? target = null,
        IReadOnlyDictionary<string, AuditChange>? changes = null, AuditActor? actor = null)
    {
        var principal = http.HttpContext?.User;
        _pending.Add(new AuditEvent
        {
            TenantId = tenant.TenantId,
            Action = action,
            Summary = summary.Length > 500 ? summary[..500] : summary,
            Severity = severity,
            ActorId = actor is null ? user.MembershipId ?? user.UserId : actor.Id,
            ActorName = actor is null ? principal?.FindFirstValue(PlatformClaims.Name) : actor.Name,
            ActorEmail = actor is null ? user.Email : actor.Email,
            TargetType = target?.Type,
            TargetId = target?.Id,
            TargetLabel = target?.Label is { Length: > 200 } label ? label[..200] : target?.Label,
            Changes = changes is null ? null : JsonSerializer.Serialize(
                changes.ToDictionary(c => c.Key, c => new { from = c.Value.From, to = c.Value.To }), Json),
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            RequestId = request.CorrelationId,
            CreatedAt = clock.GetUtcNow(),
        });
    }

    /// <summary>Hands buffered events to a context that is about to save (same transaction).</summary>
    public IReadOnlyList<AuditEvent> Drain()
    {
        var events = _pending.ToList();
        _pending.Clear();
        return events;
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        // Resolved lazily: the store's DbContext depends on the save interceptor, which depends on this log.
        var events = Drain();
        await services.GetRequiredService<IAuditEventStore>().SaveAsync(events, cancellationToken);
    }
}
