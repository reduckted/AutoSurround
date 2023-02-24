#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace AutoSurround;

[Export]
public class LanguageConfiguration {

    private readonly Dictionary<string, Dictionary<char, char>> _surroundingPairsByFileExtension;
    private readonly Dictionary<string, Dictionary<char, char>> _surroundingPairsByFileName;
    private readonly HashSet<char> _openingChars;


    public LanguageConfiguration() {
        _surroundingPairsByFileExtension = new Dictionary<string, Dictionary<char, char>>(StringComparer.OrdinalIgnoreCase);
        _surroundingPairsByFileName = new Dictionary<string, Dictionary<char, char>>(StringComparer.OrdinalIgnoreCase);
        _openingChars = new HashSet<char>();
        LoadConfiguration("vscode");
        LoadConfiguration("liquid");
        LoadConfiguration("cmake");
    }


    private void LoadConfiguration(string name) {
        IEnumerable<JsonEntry> entries;
        string resourceName;


        resourceName = "AutoSurround.Languages." + name + ".json";
        var names = typeof(LanguageConfiguration).Assembly.GetManifestResourceNames();

        using (Stream stream = typeof(LanguageConfiguration).Assembly.GetManifestResourceStream(resourceName)) {
            using (StreamReader textReader = new(stream)) {
                using (JsonTextReader jsonReader = new(textReader)) {
                    entries = JsonSerializer.CreateDefault().Deserialize<IEnumerable<JsonEntry>>(jsonReader);
                }
            }
        }

        foreach (JsonEntry entry in entries) {
            Dictionary<char, char> pairs;


            pairs = entry.SurroundingPairs.ToDictionary((x) => x.Open[0], (x) => x.Close[0]);
            _openingChars.UnionWith(pairs.Keys);

            foreach (string extension in entry.Extensions) {
                _surroundingPairsByFileExtension.Add(extension, pairs);
            }

            foreach (string fileName in entry.FileNames) {
                _surroundingPairsByFileName.Add(fileName, pairs);
            }
        }
    }


    public bool IsPossiblyOpeningChar(char ch) {
        return _openingChars.Contains(ch);
    }


    public bool TryGetClosingChar(string fileName, char opening, out char closing) {
        if (_surroundingPairsByFileExtension.TryGetValue(Path.GetExtension(fileName), out var pairs)) {
            return pairs.TryGetValue(opening, out closing);
        }

        if (_surroundingPairsByFileName.TryGetValue(fileName, out pairs)) {
            return pairs.TryGetValue(opening, out closing);
        }

        closing = default;
        return false;
    }


    private class JsonEntry {

        public IEnumerable<string> Extensions { get; set; } = Enumerable.Empty<string>();


        public IEnumerable<string> FileNames { get; set; } = Enumerable.Empty<string>();


        public IEnumerable<JsonSurroundingPair> SurroundingPairs { get; set; } = Enumerable.Empty<JsonSurroundingPair>();

    }


    private class JsonSurroundingPair {

        public string Open { get; set; } = "";


        public string Close { get; set; } = "";

    }

}
