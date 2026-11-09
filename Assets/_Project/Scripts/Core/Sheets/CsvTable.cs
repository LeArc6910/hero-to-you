using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Game.Core
{
    internal sealed class CsvRow
    {
        readonly List<string> _header;
        readonly List<string> _fields;

        public CsvRow(int line, List<string> header, List<string> fields)
        {
            Line = line;
            _header = header;
            _fields = fields;
        }

        public int Line { get; }

        public string this[string column]
        {
            get
            {
                int index = _header.IndexOf(column);
                return index >= 0 && index < _fields.Count ? _fields[index].Trim() : "";
            }
        }
    }

    internal sealed class CsvTable
    {
        CsvTable(List<string> header, List<CsvRow> rows)
        {
            Header = header;
            Rows = rows;
        }

        public List<string> Header { get; }
        public List<CsvRow> Rows { get; }

        public static CsvTable Read(string csv)
        {
            var records = ReadRecords(csv ?? "");
            if (records.Count == 0) return new CsvTable(new List<string>(), new List<CsvRow>());

            var header = records[0].Fields.Select(h => h.Trim().ToLowerInvariant()).ToList();
            var rows = records.Skip(1)
                .Where(r => !r.Fields.All(string.IsNullOrWhiteSpace) && !r.Fields[0].TrimStart().StartsWith("#"))
                .Select(r => new CsvRow(r.Line, header, r.Fields))
                .ToList();
            return new CsvTable(header, rows);
        }

        public List<string> MissingColumns(params string[] required) => required.Where(c => !Header.Contains(c)).ToList();

        sealed class Record
        {
            public Record(int line) { Line = line; }

            public int Line { get; }
            public List<string> Fields { get; } = new List<string>();
        }

        static List<Record> ReadRecords(string csv)
        {
            if (csv.Length > 0 && csv[0] == '﻿') csv = csv.Substring(1);

            var records = new List<Record>();
            var field = new StringBuilder();
            int line = 1;
            var record = new Record(line);
            bool inQuotes = false;

            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else if (c != '\r')
                    {
                        if (c == '\n') line++;
                        field.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        record.Fields.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        record.Fields.Add(field.ToString());
                        field.Clear();
                        records.Add(record);
                        line++;
                        record = new Record(line);
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }

            if (field.Length > 0 || record.Fields.Count > 0)
            {
                record.Fields.Add(field.ToString());
                records.Add(record);
            }
            return records;
        }
    }

    internal static class SheetValues
    {
        public static T ParseEnum<T>(string raw, string label) where T : struct, Enum
        {
            if (raw.Length > 0 && char.IsLetter(raw[0]) && Enum.TryParse(raw, true, out T result)) return result;
            throw new FormatException($"알 수 없는 {label} '{raw}' (가능한 값: {string.Join(", ", Enum.GetNames(typeof(T)))})");
        }

        // A yes/no column: blank or "no" is no.
        public static bool ParseBool(string raw, string label) => raw.ToLowerInvariant() switch
        {
            "" or "no" => false,
            "yes" => true,
            _ => throw new FormatException($"{label}는 yes나 no입니다: '{raw}'"),
        };

        // A whole number that may be negative.
        public static int ParseSigned(string raw, string label)
        {
            if (int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number)) return number;
            throw new FormatException($"{label} '{raw}'는 정수여야 합니다.");
        }

        public static int ParseNumber(string raw, string label)
        {
            if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int number)) return number;
            throw new FormatException($"{label} '{raw}'는 0 이상의 정수가 아닙니다.");
        }

        public static float ParseDecimal(string raw, string label)
        {
            if (float.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float number)) return number;
            throw new FormatException($"{label} '{raw}'는 0 이상의 수가 아닙니다.");
        }

        public static bool IsValidId(string id) =>
            id.Length > 0 && id.All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '_' || c == '-'));

        // #RRGGBB.
        public static bool IsColor(string text) =>
            text.Length == 7 && text[0] == '#' && text.Skip(1).All(c => Uri.IsHexDigit(c));
    }
}
