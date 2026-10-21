using System;
using System.Globalization;
using System.Text;

namespace SpinTheScrap.Core
{
    /// <summary>状態を 1 行の文字列にする（区切りは タブ）。数値は往復で値が変わらない書き方にする。</summary>
    public sealed class SnapshotWriter
    {
        readonly StringBuilder sb = new StringBuilder();

        SnapshotWriter Token(string value)
        {
            if (value.IndexOf('\t') >= 0) throw new ArgumentException("Tabs are not allowed", nameof(value));
            if (sb.Length > 0) sb.Append('\t');
            sb.Append(value);
            return this;
        }

        public SnapshotWriter Write(string value) => Token(value ?? "");
        public SnapshotWriter Write(int value) => Token(value.ToString(CultureInfo.InvariantCulture));
        public SnapshotWriter Write(ulong value) => Token(value.ToString(CultureInfo.InvariantCulture));
        public SnapshotWriter Write(double value) => Token(value.ToString("R", CultureInfo.InvariantCulture));
        public SnapshotWriter Write(bool value) => Token(value ? "1" : "0");

        public override string ToString() => sb.ToString();
    }

    /// <summary>SnapshotWriter で書いた文字列を前から順に読む。</summary>
    public sealed class SnapshotReader
    {
        readonly string[] tokens;
        int index;

        public SnapshotReader(string text)
        {
            tokens = string.IsNullOrEmpty(text) ? new string[0] : text.Split('\t');
        }

        public bool AtEnd => index >= tokens.Length;

        public string String()
        {
            if (AtEnd) throw new FormatException("Snapshot ended early");
            return tokens[index++];
        }

        public int Int() => int.Parse(String(), CultureInfo.InvariantCulture);
        public ulong ULong() => ulong.Parse(String(), CultureInfo.InvariantCulture);
        public double Double() => double.Parse(String(), NumberStyles.Float, CultureInfo.InvariantCulture);
        public bool Bool() => String() == "1";
    }
}
