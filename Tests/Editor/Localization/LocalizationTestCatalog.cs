using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using dev.limitex.avatar.compressor.editor;
using NUnit.Framework;

namespace dev.limitex.avatar.compressor.tests
{
    internal static class LocalizationTestCatalog
    {
        internal static string LocalizationFolder =>
            AvatarCompressorLocalization.LocalizationFolder;

        internal static readonly string[] BundledLocales =
        {
            "en-US",
            "zh-Hans",
            "zh-Hant",
            "ja-JP",
            "ko-KR",
        };

        internal static string PackageRoot =>
            LocalizationFolder.Substring(
                0,
                LocalizationFolder.LastIndexOf("/Editor/", StringComparison.Ordinal)
            );

        internal static string[] PoPaths()
        {
            return Directory
                .GetFiles(LocalizationFolder, "*.po")
                .Select(path => path.Replace('\\', '/'))
                .ToArray();
        }

        internal static Dictionary<string, string> ReadMessages(string path)
        {
            var messages = new Dictionary<string, string>(StringComparer.Ordinal);
            var id = new StringBuilder();
            var value = new StringBuilder();
            StringBuilder current = null;

            void Commit()
            {
                if (current != value || id.Length == 0)
                    return;

                string key = id.ToString();
                Assert.IsFalse(messages.ContainsKey(key), $"Duplicate key '{key}'.");
                messages.Add(key, value.ToString());
            }

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("msgid \"", StringComparison.Ordinal))
                {
                    Commit();
                    id.Clear().Append(ReadQuotedValue(line));
                    value.Clear();
                    current = id;
                }
                else if (line.StartsWith("msgstr \"", StringComparison.Ordinal))
                {
                    value.Clear().Append(ReadQuotedValue(line));
                    current = value;
                }
                else if (current != null && line.StartsWith("\"", StringComparison.Ordinal))
                {
                    current.Append(ReadQuotedValue(line));
                }
            }

            Commit();
            return messages;
        }

        private static string ReadQuotedValue(string line)
        {
            int firstQuote = line.IndexOf('"');
            return line.Substring(firstQuote + 1, line.Length - firstQuote - 2);
        }

        internal static string[] GetPlaceholders(string value)
        {
            return Regex
                .Matches(value, @"\{\d+(?::[^}]*)?\}")
                .Cast<Match>()
                .Select(match => match.Value)
                .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
