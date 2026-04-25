using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AudioFileMetadataProcessor.Helpers
{
    /// <summary>
    /// Centralized configuration + shared HttpClient and JsonSerializerOptions.
    /// Call <see cref="Initialize"/> at program startup.
    /// </summary>
    public static class ConfigurationHelper
    {
        private static IConfiguration? _configuration;
        private static string? _musicBrainzBaseUrl;
        private static readonly HttpClient _httpClient = new();
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static HttpClient HttpClient => _httpClient;
        public static JsonSerializerOptions JsonOptions => _jsonOptions;
        public static string? MusicBrainzBaseUrl => _musicBrainzBaseUrl;
        public static IConfiguration? Configuration => _configuration;

        /// <summary>
        /// Build IConfiguration from appsettings files and configure HttpClient (User-Agent).
        /// Returns true if MusicBrainz:BaseUrl is present (same behavior as original LoadConfiguration).
        /// </summary>
        public static bool Initialize()
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

                _configuration = builder.Build();

                _musicBrainzBaseUrl = _configuration["MusicBrainz:BaseUrl"];

                // Set User-Agent for MusicBrainz (required)
                string? userAgent = _configuration["MusicBrainz:UserAgent"];
                if (!string.IsNullOrEmpty(userAgent))
                {
                    // Clear default headers and set User-Agent header
                    _httpClient.DefaultRequestHeaders.Clear();
                    // Some HttpClient implementations require a ProductInfoHeaderValue
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
                }

                return !string.IsNullOrEmpty(_musicBrainzBaseUrl);
            }
            catch (Exception ex)
            {
                Logger.Log($"Configuration error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Safe typed configuration accessor. Mirrors previous GetConfigurationValue behavior.
        /// </summary>
        public static T GetValue<T>(string key, T defaultValue)
        {
            if (_configuration == null)
            {
                throw new InvalidOperationException("Configuration is not loaded.");
            }

            var section = _configuration.GetSection(key);
            if (section == null || string.IsNullOrEmpty(section.Value))
            {
                return defaultValue;
            }

            return (T)Convert.ChangeType(section.Value, typeof(T));
        }

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
    }
}