using System.Globalization;
using Full.NET.Modules.SerialNumbers.Contracts;

namespace Full.NET.Modules.SerialNumbers.Domain;

internal static class SerialNumberResetBucket
{
    public static string Create(
        SerialNumberResetInterval interval,
        DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return interval switch
        {
            SerialNumberResetInterval.Never => "all",
            SerialNumberResetInterval.Day =>
                utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            SerialNumberResetInterval.Month =>
                utc.ToString("yyyyMM", CultureInfo.InvariantCulture),
            SerialNumberResetInterval.Year =>
                utc.ToString("yyyy", CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(interval)),
        };
    }
}
