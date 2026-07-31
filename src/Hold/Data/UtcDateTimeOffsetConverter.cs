using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hold.Data;

public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(value => value.ToUniversalTime(), value => value)
    {
    }
}
