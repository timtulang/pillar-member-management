using System.Data;
using System.Globalization;
using Dapper;
using Npgsql;

namespace Pillar.Data;

public sealed class Row(IDictionary<string, object> values)
{
    public string S(string key) => values.TryGetValue(key, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
    public Guid G(string key) => Guid.Parse(S(key));
    public long N(string key) => string.IsNullOrEmpty(S(key)) ? 0 : long.Parse(S(key), CultureInfo.InvariantCulture);
    public bool B(string key) => values.TryGetValue(key, out var v) && v is true;
    public DateTime? T(string key) => !values.TryGetValue(key, out var v) ? null : v switch
    {
        DateTime d => d,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        DateTimeOffset d => d.UtcDateTime,
        _ => null
    };
    public string Date(string key) => T(key)?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—";
}

public sealed class Db(NpgsqlDataSource source)
{
    public async Task<List<Row>> Read(string sql, object? args = null)
    {
        await using var c = await source.OpenConnectionAsync();
        return (await c.QueryAsync(sql, args)).Select(r => new Row((IDictionary<string, object>)r)).ToList();
    }

    public async Task<Row?> One(string sql, object? args = null) => (await Read(sql, args)).SingleOrDefault();

    public async Task<T> Write<T>(Guid actor, string action, string entity, Guid entityId,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work, string[]? fields = null)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        // Shared lock order: relationship graph, then person/event/card rows as needed.
        // A separate command gives graph validation a fresh READ COMMITTED snapshot.
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(3402026)", transaction: tx);
        var result = await work(c, tx);
        await c.ExecuteAsync("""
            INSERT INTO pillar.audit_events(actor_user_id,action,entity_type,entity_id,correlation_id,changed_fields)
            VALUES (@actor,@action,@entity,@entityId,@correlation,CAST(@fields AS jsonb))
            """, new { actor, action, entity, entityId, correlation = Guid.NewGuid(), fields = System.Text.Json.JsonSerializer.Serialize(fields ?? []) }, tx);
        await tx.CommitAsync();
        return result;
    }

    public Task<int> Execute(Guid actor, string action, string entity, Guid id, string sql, object args, string[]? fields = null) =>
        Write(actor, action, entity, id, (c, t) => c.ExecuteAsync(sql, args, t), fields);
}
