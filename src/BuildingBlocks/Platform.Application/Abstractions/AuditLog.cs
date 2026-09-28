namespace Platform.Application.Abstractions;

public enum AuditSeverity
{
    Info,
    Warning,
    Critical,
}

public sealed record AuditTarget(string Type, string Id, string Label);

public sealed record AuditChange(object? From, object? To);

/// <summary>Who performed the action, when it is not the signed-in caller (e.g. during sign-in).</summary>
public sealed record AuditActor(Guid? Id, string? Name, string? Email);

/// <summary>
/// Human-readable, append-only activity log shown in the admin portal (who did what, when, from where).
/// Events are buffered and written in the same transaction as the next <c>SaveChanges</c> of any module,
/// so an audit record exists if and only if the change was committed. Use <see cref="FlushAsync"/> for
/// events that have no accompanying data change (e.g. a failed sign-in).
/// Never put passwords, tokens or secrets in <c>changes</c>.
/// </summary>
public interface IAuditLog
{
    /// <param name="action"><c>area.verb</c>, e.g. <c>member.deleted</c>, <c>auth.login_failed</c>.</param>
    /// <param name="summary">A sentence for people, e.g. "Deleted member Ada Okafor".</param>
    void Record(string action, string summary, AuditSeverity severity = AuditSeverity.Info, AuditTarget? target = null,
        IReadOnlyDictionary<string, AuditChange>? changes = null, AuditActor? actor = null);

    Task FlushAsync(CancellationToken cancellationToken);
}

public static class AuditChanges
{
    /// <summary>Before/after pairs for the fields whose values differ.</summary>
    public static IReadOnlyDictionary<string, AuditChange>? Diff(params (string Field, object? From, object? To)[] fields)
    {
        var changes = fields.Where(f => !Equals(f.From, f.To)).ToDictionary(f => f.Field, f => new AuditChange(f.From, f.To));
        return changes.Count == 0 ? null : changes;
    }
}
