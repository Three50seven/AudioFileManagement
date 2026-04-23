using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AudioFileMetadataProcessor.Domain;

namespace AudioFileMetadataProcessor.Helpers
{
    public static class MetadataHelper
    {
        private static readonly JsonSerializerOptions _jsonSerializerOptions = ConfigurationHelper.JsonOptions;

        public static Dictionary<string, string> ReadCurrentTags(string filePath)
        {
            var tags = new Dictionary<string, string>();

            try
            {
                using (var file = TagLib.File.Create(filePath))
                {
                    tags["Title"] = file.Tag.Title ?? "";
                    tags["Artist"] = string.Join(", ", file.Tag.Performers ?? new string[0]);
                    tags["Album"] = file.Tag.Album ?? "";
                    tags["Year"] = file.Tag.Year.ToString();
                    tags["Genre"] = string.Join(", ", file.Tag.Genres ?? new string[0]);
                    tags["Track"] = file.Tag.Track.ToString();
                    tags["Disc"] = file.Tag.Disc.ToString();
                    tags["AlbumArtist"] = string.Join(", ", file.Tag.AlbumArtists ?? new string[0]);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error reading tags: {ex.Message}");
            }

            return tags;
        }

        public static void DisplayCurrentTags(Dictionary<string, string> tags)
        {
            Logger.Log("Current metadata:");
            foreach (var tag in tags)
            {
                if (!string.IsNullOrEmpty(tag.Value) && tag.Value != "0")
                    Logger.Log($"  {tag.Key}: {tag.Value}");
            }
        }

        public static void DisplayUpdatedMetadata(TrackMetadata metadata)
        {
            Logger.Log("Updated metadata:");
            Logger.Log($"  Title: {metadata.Title}");
            Logger.Log($"  Artist: {metadata.Artist}");
            Logger.Log($"  Album: {metadata.Album}");
            if (!string.IsNullOrEmpty(metadata.AlbumArtist))
                Logger.Log($"  Album Artist: {metadata.AlbumArtist}");
            if (!string.IsNullOrEmpty(metadata.ReleaseDate))
                Logger.Log($"  Year: {metadata.ReleaseDate}");
            if (metadata.TrackNumber > 0)
                Logger.Log($"  Track: {metadata.TrackNumber}");
            if (metadata.TrackCount > 0)
                Logger.Log($"  Track Count: {metadata.TrackCount}");
            if (metadata.DiscNumber > 0)
                Logger.Log($"  Disc: {metadata.DiscNumber}");
            if (metadata.DiscCount > 0)
                Logger.Log($"  Disc Count: {metadata.DiscCount}");
            if (!string.IsNullOrEmpty(metadata.Genre))
                Logger.Log($"  Genre: {metadata.Genre}");
            if (!string.IsNullOrEmpty(metadata.CoverArtUrl))
                Logger.Log($"  Cover Art: Embedded");
        }

        public static void UpdateTags(string filePath, TrackMetadata metadata)
        {
            try
            {
                using (var file = TagLib.File.Create(filePath))
                {
                    if (!string.IsNullOrEmpty(metadata.Title))
                        file.Tag.Title = metadata.Title;

                    if (!string.IsNullOrEmpty(metadata.Artist))
                        file.Tag.Performers = new[] { metadata.Artist };

                    if (!string.IsNullOrEmpty(metadata.Album))
                        file.Tag.Album = metadata.Album;

                    if (!string.IsNullOrEmpty(metadata.AlbumArtist))
                        file.Tag.AlbumArtists = new[] { metadata.AlbumArtist };

                    if (!string.IsNullOrEmpty(metadata.ReleaseDate))
                    {
                        if (uint.TryParse(metadata.ReleaseDate, out uint year))
                            file.Tag.Year = year;
                    }

                    if (metadata.TrackNumber > 0)
                        file.Tag.Track = (uint)metadata.TrackNumber;

                    if (metadata.TrackCount > 0)
                        file.Tag.TrackCount = (uint)metadata.TrackCount;

                    if (metadata.DiscNumber > 0)
                        file.Tag.Disc = (uint)metadata.DiscNumber;

                    if (metadata.DiscCount > 0)
                        file.Tag.DiscCount = (uint)metadata.DiscCount;

                    if (!string.IsNullOrEmpty(metadata.Genre))
                        file.Tag.Genres = metadata.Genre.Split(',').Select(g => g.Trim()).ToArray();

                    file.Save();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating tags: {ex.Message}");
            }
        }

        public static async Task EmbedArtwork(string filePath, string imageUrl)
        {
            try
            {
                byte[] imageBytes;
                if (!string.IsNullOrEmpty(Program.CacheKey) && Program.CoverArtCache.TryGetValue(Program.CacheKey, out var cachedBytes))
                {
                    imageBytes = cachedBytes;
                }
                else
                {
                    imageBytes = await ConfigurationHelper.HttpClient.GetByteArrayAsync(imageUrl);

                    // Compress image before caching/embedding.
                    int maxDimension = ConfigurationHelper.GetValue("AppSettings:MaxImageDimension", 800);
                    int jpegQuality = ConfigurationHelper.GetValue("AppSettings:ImageQuality", 85);

                    try
                    {
                        var compressed = ImageProcessing.CompressImage(imageBytes, maxDimension, jpegQuality);
                        if (compressed != null && compressed.Length > 0)
                        {
                            imageBytes = compressed;
                        }
                    }
                    catch (Exception ex)
                    {
                        // If compression fails, fall back to original bytes
                        Logger.Log($"Warning: image compression failed, using original image bytes: {ex.Message}");
                    }

                    if (!string.IsNullOrEmpty(Program.CacheKey))
                    {
                        Program.CoverArtCache[Program.CacheKey] = imageBytes;
                    }
                }

                using (var file = TagLib.File.Create(filePath))
                {
                    string? mimeType = ImageProcessing.GetImageMimeType(imageBytes);

                    var picture = new TagLib.Picture
                    {
                        Data = imageBytes,
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = mimeType,
                        Description = "Front Cover"
                    };

                    file.Tag.Pictures = new[] { picture };
                    file.Save();
                }

                Logger.Log("✓ Artwork embedded successfully");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error embedding artwork: {ex.Message}");
            }
        }

        public static TrackMetadata CreateMetadataFromSeeder(SeederData seeder)
        {
            var keepStyling = !string.IsNullOrEmpty(seeder.KeepTextStyling) &&
                  bool.TryParse(seeder.KeepTextStyling.Trim().ToLower(), out bool keepStyleResult) &&
                  keepStyleResult;

            var metadata = new TrackMetadata
            {
                Title = keepStyling ? seeder.Title : ToProperCase(seeder.Title),
                Artist = keepStyling ? seeder.Artist : ToProperCase(seeder.Artist),
                Album = keepStyling ? seeder.Album : ToProperCase(seeder.Album),
                AlbumArtist = keepStyling ? seeder.AlbumArtist : ToProperCase(seeder.AlbumArtist),
                Genre = ToProperCase(seeder.Genre),
                ReleaseDate = seeder.Year
            };

            // Parse track number
            if (!string.IsNullOrEmpty(seeder.TrackNumber))
            {
                var trackParts = seeder.TrackNumber.Split('/');
                if (trackParts.Length > 0 && int.TryParse(trackParts[0], out int trackNum))
                {
                    metadata.TrackNumber = trackNum;
                }
                if (trackParts.Length > 1 && int.TryParse(trackParts[1], out int trackCount))
                {
                    metadata.TrackCount = trackCount;
                }
            }

            // Parse disc number
            if (!string.IsNullOrEmpty(seeder.DiscNumber))
            {
                var discParts = seeder.DiscNumber.Split('/');
                if (discParts.Length > 0 && int.TryParse(discParts[0], out int discNum))
                {
                    metadata.DiscNumber = discNum;
                }
                if (discParts.Length > 1 && int.TryParse(discParts[1], out int discCount))
                {
                    metadata.DiscCount = discCount;
                }
            }

            return metadata;
        }

        public static string CreateOutputFileName(string inputPath, SeederData? seederData)
        {
            if (seederData != null && !string.IsNullOrEmpty(seederData.TrackNumber) && !string.IsNullOrEmpty(seederData.Title))
            {
                // Parse track number
                var trackParts = seederData.TrackNumber.Split('/');
                string trackNum = trackParts.Length > 0 ? trackParts[0].PadLeft(2, '0') : "00";

                // Clean title for filename
                string cleanTitle = CleanFilename(ToProperCase(seederData.Title));
                return $"{trackNum} {cleanTitle}";
            }
            return Path.GetFileNameWithoutExtension(inputPath);
        }

        public static string CleanFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return string.Empty;

            // Define invalid characters for filenames
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string[] additionalInvalid = { ":", "*", "?", "\"", "<", ">", "|", "/", "\\" };

            string cleaned = filename;

            // Remove invalid filename characters
            foreach (char c in invalidChars)
            {
                cleaned = cleaned.Replace(c, '_');
            }

            // Remove additional problematic characters
            foreach (string s in additionalInvalid)
            {
                cleaned = cleaned.Replace(s, "_");
            }

            // Clean up multiple spaces and trim
            while (cleaned.Contains("  "))
            {
                cleaned = cleaned.Replace("  ", " ");
            }

            return cleaned.Trim();
        }

        public static string ToProperCase(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return StringHelpers.ToTitleCaseWithExceptions(input.ToLower());
        }

        public static async Task<string?> SearchForCoverArt(SeederData seeder)
        {
            try
            {
                // Check if seeder has an AlbumArtUrl specified and use it if available
                if (!string.IsNullOrEmpty(seeder.AlbumArtUrl))
                {
                    Logger.Log($"  Using album art URL from seeder data: {seeder.AlbumArtUrl}");
                    return seeder.AlbumArtUrl;
                }

                // Check cache for cover art URL
                if (!string.IsNullOrEmpty(Program.CacheKey) && Program.CoverArtUrlCache.TryGetValue(Program.CacheKey, out var cachedUrl))
                {
                    Logger.Log($"  Using cached cover art URL since artist and album match the key: {Program.CacheKey}");
                    return cachedUrl;
                }

                // Build search query for MusicBrainz
                var queryParts = new List<string>();

                if (!string.IsNullOrEmpty(seeder.Album))
                    queryParts.Add($"release:\"{seeder.Album}\"");

                if (!string.IsNullOrEmpty(seeder.Artist))
                    queryParts.Add($"artist:\"{seeder.Artist}\"");

                queryParts.Add("status:official");
                queryParts.Add("format:cd");
                queryParts.Add("type:album");

                if (queryParts.Count == 0) return null;

                string? query = string.Join(" AND ", queryParts);
                string? encodedQuery = Uri.EscapeDataString(query);
                string? baseUrl = ConfigurationHelper.MusicBrainzBaseUrl;
                if (string.IsNullOrEmpty(baseUrl)) return null;

                string? searchUrl = $"{baseUrl}release/?query={encodedQuery}&limit=3&fmt=json";

                Logger.Log($"  Searching for cover art: {query}");
                Logger.Log($"  MusicBrainz Search URL: {searchUrl}");

                // Add rate limiting delay
                int delayMs = ConfigurationHelper.GetValue("MusicBrainz:RequestDelayMs", 1000);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                var response = await ConfigurationHelper.HttpClient.GetStringAsync(searchUrl);

                var searchResult = JsonSerializer.Deserialize<MusicBrainzReleaseSearchResponse>(
                    response,
                    _jsonSerializerOptions
                );

                if (searchResult?.Releases != null && searchResult.Releases.Length > 0)
                {
                    // Try to find exact album match first for U.S. releases, otherwise use first result
                    var release = searchResult.Releases.FirstOrDefault(r =>
                        string.Equals(r.Title, seeder.Album, StringComparison.OrdinalIgnoreCase) && r.Country == "US")
                        ?? searchResult.Releases[0];

                    if (release?.Id != null)
                    {
                        var coverArtUrl = await GetCoverArtUrl(release.Id);

                        // Cache the cover art URL for this artist/album
                        if (!string.IsNullOrEmpty(Program.CacheKey) && !string.IsNullOrEmpty(coverArtUrl))
                        {
                            Program.CoverArtUrlCache ??= new Dictionary<string, string>();
                            Program.CoverArtUrlCache[Program.CacheKey] = coverArtUrl;
                        }

                        return coverArtUrl;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Log($"Cover art search error: {ex.Message}");
                return null;
            }
        }

        public static async Task<string?> GetCoverArtUrl(string? releaseId)
        {
            if (string.IsNullOrEmpty(releaseId))
            {
                return null; // Return null if releaseId is null or empty
            }

            try
            {
                string? coverArtUrl = $"https://coverartarchive.org/release/{releaseId}";
                var response = await ConfigurationHelper.HttpClient.GetStringAsync(coverArtUrl);
                var coverArt = JsonSerializer.Deserialize<CoverArtResponse>(
                    response,
                    _jsonSerializerOptions
                );

                // Get the front cover or first available image
                var frontCover = coverArt?.Images?.FirstOrDefault(img =>
                    img.Front == true || img.Types?.Contains("Front") == true);

                return frontCover?.Image ?? coverArt?.Images?[0]?.Image;
            }
            catch
            {
                return null; // Return null if an exception occurs
            }
        }
    }
}