using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hold.Data;

/// <summary>
/// Npgsql maps DateTimeOffset to timestamptz, which stores an instant and no offset at all —
/// so a value written as +05:00 comes back as UTC regardless. Converting on write makes that
/// explicit rather than incidental: what goes in is what comes out, and a caller that builds a
/// DateTimeOffset from local time cannot be surprised by the round trip.
///
/// Kept from the SQLite era, where it was load-bearing for a different reason — dates were TEXT
/// and offsets made string comparison lie. Postgres orders timestamptz correctly on its own.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(value => value.ToUniversalTime(), value => value)
    {
    }
}
