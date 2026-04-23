using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioFileMetadataProcessor.Domain;
using AudioFileMetadataProcessor.Helpers;

namespace AudioFileMetadataProcessor.Helpers
{
    public static class SeederHelper
    {
        public static Dictionary<string, SeederData>? LoadSeedersFile(string filePath)
        {
            try
            {
                var seedersData = new Dictionary<string, SeederData>(StringComparer.OrdinalIgnoreCase);
                var lines = File.ReadAllLines(filePath);

                Logger.Log($"Loading seeders from: {filePath}");

                bool isFirstLine = true;
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#")) continue;

                    // Skip header line if it looks like one
                    if (isFirstLine && (trimmedLine.ToLower().Contains("artist") || trimmedLine.ToLower().Contains("title")))
                    {
                        isFirstLine = false;
                        continue;
                    }
                    isFirstLine = false;

                    var parts = ParseCsvLine(trimmedLine);
                    if (parts.Count >= 9) // Need at least 9 columns, KeepTextStyling and AlbumArtUrl are optional
                    {
                        var seeder = new SeederData
                        {
                            Artist = parts[0].Trim(),
                            AlbumArtist = parts[1].Trim(),
                            Title = parts[2].Trim(),
                            Album = parts[3].Trim(),
                            Year = parts[4].Trim(),
                            TrackNumber = parts[5].Trim(),
                            DiscNumber = parts[6].Trim(),
                            Genre = parts[7].Trim(),
                            FileName = parts[8].Trim(),
                            KeepTextStyling = parts.Count > 9 ? parts[9].Trim() : string.Empty,
                            AlbumArtUrl = parts.Count > 10 ? parts[10].Trim() : string.Empty
                        };

                        // Use filename as key
                        if (!string.IsNullOrEmpty(seeder.FileName))
                        {
                            seedersData[seeder.FileName] = seeder;
                        }
                    }
                }

                Logger.Log($"Loaded {seedersData.Count} seeder entries");
                return seedersData;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading seeders file: {ex.Message}");
                return null;
            }
        }

        public static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = "";
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.Trim('"'));
                    current = "";
                }
                else
                {
                    current += c;
                }
            }

            result.Add(current.Trim('"'));
            return result;
        }

        public static SeederData? GetSeederDataForFile(string filePath, ProcessingConfig config)
        {
            string? fileName = Path.GetFileNameWithoutExtension(filePath);

            // Check seeders file data
            if (config.SeedersData != null)
            {
                // Try exact filename match first
                if (config.SeedersData.TryGetValue(fileName, out SeederData? exactMatch))
                {
                    Logger.Log("  Found exact filename match");
                    return exactMatch;
                }

                // Try partial filename match
                var partialMatch = config.SeedersData.Values.FirstOrDefault(s =>
                    !string.IsNullOrEmpty(s.FileName) &&
                    fileName.Contains(s.FileName, StringComparison.OrdinalIgnoreCase));

                if (partialMatch != null)
                {
                    Logger.Log("  Found partial filename match");
                    return partialMatch;
                }
            }

            return null;
        }
    }
}