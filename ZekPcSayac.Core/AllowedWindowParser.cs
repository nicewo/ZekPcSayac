using System.Globalization;
using System.Text.Json;

namespace ZekPcSayac.Core;

/// <summary>
/// Firestore REST arrayValue nesnesinden (deviceConfig.allowedWindows) pencere listesini okur.
/// Geçersiz öğeler atlanır ve sayısı döndürülür.
/// </summary>
public static class AllowedWindowParser
{
    private const string TimeFormat = "HH:mm";

    /// <param name="arrayValue">{ "values": [ { "mapValue": { "fields": { days, start, end } } }, ... ] } biçiminde değer.</param>
    public static (List<AllowedWindow> Windows, int Skipped) Parse(JsonElement arrayValue)
    {
        var windows = new List<AllowedWindow>();
        int skipped = 0;

        if (!TryGetArray(arrayValue, "values", out JsonElement values))
        {
            return (windows, 0);
        }

        foreach (JsonElement item in values.EnumerateArray())
        {
            if (TryParseItem(item, out AllowedWindow? window))
            {
                windows.Add(window!);
            }
            else
            {
                skipped++;
            }
        }

        return (windows, skipped);
    }

    private static bool TryParseItem(JsonElement item, out AllowedWindow? window)
    {
        window = null;

        if (!TryGetObject(item, "mapValue", out JsonElement map) ||
            !TryGetObject(map, "fields", out JsonElement fields))
        {
            return false;
        }

        if (!TryGetObject(fields, "days", out JsonElement daysWrapper) ||
            !TryGetObject(daysWrapper, "arrayValue", out JsonElement daysArray) ||
            !TryParseDays(daysArray, out int[] days))
        {
            return false;
        }

        if (!TryParseTime(fields, "start", out TimeOnly start) ||
            !TryParseTime(fields, "end", out TimeOnly end))
        {
            return false;
        }

        if (start == end)
        {
            return false;
        }

        window = new AllowedWindow(days, start, end);
        return true;
    }

    private static bool TryParseDays(JsonElement daysArray, out int[] days)
    {
        days = [];
        if (!TryGetArray(daysArray, "values", out JsonElement values))
        {
            return false;
        }

        var parsed = new SortedSet<int>();
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (!TryGetString(value, "integerValue", out string? text) ||
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int day) ||
                day < 1 || day > 7)
            {
                return false;
            }

            parsed.Add(day);
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        days = [.. parsed];
        return true;
    }

    private static bool TryParseTime(JsonElement fields, string name, out TimeOnly time)
    {
        time = default;
        return TryGetObject(fields, name, out JsonElement wrapper) &&
               TryGetString(wrapper, "stringValue", out string? text) &&
               TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement child)
    {
        child = default;
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(name, out child) &&
               child.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetArray(JsonElement parent, string name, out JsonElement child)
    {
        child = default;
        return parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(name, out child) &&
               child.ValueKind == JsonValueKind.Array;
    }

    private static bool TryGetString(JsonElement parent, string name, out string? value)
    {
        value = null;
        if (parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out JsonElement element) &&
            element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return value is not null;
        }

        return false;
    }
}
