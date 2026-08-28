using System.Text;

namespace TickLab.Gateway.FileBridge;

internal static class CsvLineParser
{
    public static IReadOnlyList<string> Parse(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool insideQuotes = false;

        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];

            if (character == '"')
            {
                bool escapedQuote =
                    insideQuotes &&
                    index + 1 < line.Length &&
                    line[index + 1] == '"';

                if (escapedQuote)
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }

                continue;
            }

            if (character == ',' && !insideQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        fields.Add(current.ToString());
        return fields;
    }
}
