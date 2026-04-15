using System.Text.Json;

namespace Scribegate.Cli;

public static class OutputFormatter
{
    public static bool JsonMode { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Print(object data)
    {
        if (JsonMode)
        {
            Console.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
            return;
        }

        if (data is string s)
        {
            Console.WriteLine(s);
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
    }

    public static void PrintTable(string[] headers, IEnumerable<string[]> rows)
    {
        if (JsonMode)
        {
            var list = rows.Select(row =>
            {
                var dict = new Dictionary<string, string>();
                for (int i = 0; i < headers.Length && i < row.Length; i++)
                    dict[headers[i]] = row[i];
                return dict;
            }).ToList();
            Console.WriteLine(JsonSerializer.Serialize(list, JsonOptions));
            return;
        }

        var allRows = new List<string[]> { headers };
        allRows.AddRange(rows);

        var widths = new int[headers.Length];
        foreach (var row in allRows)
        {
            for (int i = 0; i < row.Length && i < widths.Length; i++)
                widths[i] = Math.Max(widths[i], (row[i] ?? "").Length);
        }

        foreach (var row in allRows)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var val = i < row.Length ? (row[i] ?? "") : "";
                Console.Write(val.PadRight(widths[i] + 2));
            }
            Console.WriteLine();

            if (row == headers)
            {
                for (int i = 0; i < headers.Length; i++)
                    Console.Write(new string('-', widths[i]) + "  ");
                Console.WriteLine();
            }
        }
    }
}
