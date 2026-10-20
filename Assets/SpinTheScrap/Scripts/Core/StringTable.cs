using System;
using System.Collections.Generic;
using System.Text;

namespace SpinTheScrap.Core
{
    /// <summary>
    /// 外部化した文字列テーブル（要件書 8.1「文字列はすべて外部テーブル化」）。
    /// CSV 形式：1 行目がヘッダー（key,ja,en）。ダブルクォートで囲めばカンマと改行を含められる。
    /// 発売時は Unity Localization に移行する前提で、キー名はそのまま流用できるようにしておく。
    /// </summary>
    public sealed class StringTable
    {
        readonly Dictionary<string, string>[] tables;

        public Language Language { get; set; } = Language.Japanese;

        StringTable(Dictionary<string, string>[] tables)
        {
            this.tables = tables;
        }

        public static StringTable Empty() =>
            new StringTable(new[] { new Dictionary<string, string>(), new Dictionary<string, string>() });

        public static StringTable FromCsv(string csv)
        {
            var rows = ParseCsv(csv);
            if (rows.Count == 0) return Empty();

            var header = rows[0];
            int ja = header.IndexOf("ja"), en = header.IndexOf("en");
            if (header.Count == 0 || header[0] != "key" || ja < 0 || en < 0)
                throw new FormatException("CSV header must be: key,ja,en");

            var table = Empty();
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0]) || row[0].StartsWith("#")) continue;
                if (ja < row.Count) table.tables[(int)Language.Japanese][row[0]] = row[ja];
                if (en < row.Count) table.tables[(int)Language.English][row[0]] = row[en];
            }
            return table;
        }

        public bool Contains(string key, Language language) => tables[(int)language].ContainsKey(key);

        /// <summary>見つからなければ英語、それもなければキーそのものを返す（未翻訳を目視で見つけやすくする）。</summary>
        public string Get(string key)
        {
            if (tables[(int)Language].TryGetValue(key, out var text) && text.Length > 0) return text;
            if (tables[(int)Language.English].TryGetValue(key, out text) && text.Length > 0) return text;
            return key;
        }

        public string Format(string key, params object[] args) => string.Format(Get(key), args);

        static List<List<string>> ParseCsv(string csv)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    case '﻿':
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
