using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hold.Data;

/// <summary>
/// SQLite has no DateTimeOffset type; EF stores it as TEXT, where "2026-01-01 12:00:00+05:00"
/// sorts after "2026-01-01 10:00:00+00:00" despite being the earlier instant. Normalising to
/// UTC on write removes that ambiguity, so every row in the .db file is directly comparable
/// by eye and by string.
///
/// It does not make ORDER BY translatable: the SQLite provider rejects DateTimeOffset in an
/// ORDER BY clause outright, regardless of offset. Sort dates in memory (see ListService).
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(value => value.ToUniversalTime(), value => value)
    {
    }
}
