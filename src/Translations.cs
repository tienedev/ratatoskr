using System.Collections.Generic;
using System.IO;

namespace Ratatoskr
{
    internal static class Translations
    {
        private static Dictionary<string, string> _words;
        public static string Language { get; private set; } = "English";

        public static void Load(string language)
        {
            Language = string.IsNullOrWhiteSpace(language) ? "English" : language.Trim();
            _words = null;
        }

        public static string Get(string token)
        {
            if (string.IsNullOrEmpty(token) || token[0] != '$') return null;
            if (_words == null) _words = Build(Language);
            return _words.TryGetValue(token.Substring(1), out var w) ? w : null;
        }

        private static Dictionary<string, string> Build(string language)
        {
            var words = new Dictionary<string, string>();
            var loc = Localization.instance;
            var settings = Localization.m_localizationSettings;
            if (loc == null || settings == null) return words;

            foreach (var asset in settings.Localizations)
            {
                if (asset == null) continue;
                var reader = new StringReader(asset.text);
                var header = reader.ReadLine()?.Split(',');
                if (header == null) continue;
                var col = -1;
                for (var i = 0; i < header.Length; i++)
                    if (header[i].Trim('"') == language) { col = i; break; }
                if (col < 0) continue;

                foreach (var row in loc.DoQuoteLineSplit(reader))
                {
                    if (row.Count <= col || row[0].Length == 0 || row[0].StartsWith("//")) continue;
                    var text = row[col].Trim();
                    if (text.Length > 0 && text[0] != '\r') words[row[0]] = text;
                }
            }
            return words;
        }
    }
}
