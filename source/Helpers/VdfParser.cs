using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DivaModManager.Helpers
{
    /// <summary>
    /// Minimal KeyValues (VDF) parser/writer for Steam's localconfig.vdf files.
    ///
    /// VDF format example:
    ///   "UserLocalConfigStore"
    ///   {
    ///       "Software"
    ///       {
    ///           "Valve"
    ///           {
    ///               "Steam"
    ///               {
    ///                   "apps"
    ///                   {
    ///                       "1761390"
    ///                       {
    ///                           "LaunchOptions"        "WINEDLLOVERRIDES=\"dinput8.dll=n,b\" %command%"
    ///                       }
    ///                   }
    ///               }
    ///           }
    ///       }
    ///   }
    ///
    /// This is NOT a general-purpose VDF library — it handles only the subset that Steam uses
    /// for localconfig.vdf. Specifically:
    ///   - Keys and values are always quoted
    ///   - Values can contain escaped quotes (\")
    ///   - Children are wrapped in { }
    ///   - Comments start with //
    /// </summary>
    public class VdfValue
    {
        public string? StringValue { get; set; }
        public Dictionary<string, VdfValue>? Children { get; set; }

        public bool IsObject => Children != null;
        public bool IsString => StringValue != null;

        public VdfValue GetChild(string key)
        {
            if (Children == null) return new VdfValue();
            return Children.TryGetValue(key, out var v) ? v : new VdfValue();
        }

        public void SetChild(string key, VdfValue value)
        {
            Children ??= new Dictionary<string, VdfValue>();
            Children[key] = value;
        }

        public static VdfValue String(string s) => new() { StringValue = s };
        public static VdfValue Object() => new() { Children = new Dictionary<string, VdfValue>() };
    }

    public static class VdfParser
    {
        public static VdfValue Parse(string input)
        {
            var pos = 0;
            var root = VdfValue.Object();
            ParseBlock(input, ref pos, root.Children!, topLevel: true);
            return root;
        }

        private static void ParseBlock(string input, ref int pos, Dictionary<string, VdfValue> target, bool topLevel = false)
        {
            while (pos < input.Length)
            {
                SkipWhitespaceAndComments(input, ref pos);
                if (pos >= input.Length) return;

                var c = input[pos];
                if (c == '}')
                {
                    pos++;
                    return;
                }

                // Parse key
                var key = ParseQuotedString(input, ref pos);
                if (key == null) return;

                SkipWhitespaceAndComments(input, ref pos);
                if (pos >= input.Length) return;

                if (input[pos] == '{')
                {
                    pos++; // skip {
                    var child = VdfValue.Object();
                    ParseBlock(input, ref pos, child.Children!);
                    target[key] = child;
                }
                else
                {
                    var value = ParseQuotedString(input, ref pos);
                    target[key] = VdfValue.String(value ?? "");
                }
            }
        }

        private static string? ParseQuotedString(string input, ref int pos)
        {
            if (pos >= input.Length || input[pos] != '"') return null;
            pos++; // skip opening quote
            var sb = new StringBuilder();
            while (pos < input.Length && input[pos] != '"')
            {
                if (input[pos] == '\\' && pos + 1 < input.Length)
                {
                    // Escape sequence — preserve as-is (Steam uses \" for embedded quotes)
                    sb.Append(input[pos]);
                    sb.Append(input[pos + 1]);
                    pos += 2;
                }
                else
                {
                    sb.Append(input[pos]);
                    pos++;
                }
            }
            if (pos < input.Length) pos++; // skip closing quote
            return sb.ToString();
        }

        private static void SkipWhitespaceAndComments(string input, ref int pos)
        {
            while (pos < input.Length)
            {
                var c = input[pos];
                if (char.IsWhiteSpace(c))
                {
                    pos++;
                }
                else if (c == '/' && pos + 1 < input.Length && input[pos + 1] == '/')
                {
                    // Line comment
                    while (pos < input.Length && input[pos] != '\n') pos++;
                }
                else
                {
                    return;
                }
            }
        }

        public static string Serialize(VdfValue value, int indent = 0)
        {
            var sb = new StringBuilder();
            SerializeInto(sb, value, indent);
            return sb.ToString();
        }

        private static void SerializeInto(StringBuilder sb, VdfValue value, int indent)
        {
            if (value.Children == null) return;
            var pad = new string('\t', indent);
            foreach (var kv in value.Children)
            {
                if (kv.Value.IsObject)
                {
                    sb.Append(pad).Append('"').Append(kv.Key).Append('"').Append('\n');
                    sb.Append(pad).Append("{\n");
                    SerializeInto(sb, kv.Value, indent + 1);
                    sb.Append(pad).Append("}\n");
                }
                else
                {
                    sb.Append(pad).Append('"').Append(kv.Key).Append('"');
                    sb.Append('\t').Append('"').Append(kv.Value.StringValue ?? "").Append('"').Append('\n');
                }
            }
        }
    }
}
