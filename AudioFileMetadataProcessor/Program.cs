using System.Text.Json;
using Microsoft.Extensions.Configuration;
using AudioFileMetadataProcessor.Helpers;
using NAudio.Wave;
using NAudio.Lame;
using NAudio.MediaFoundation;

namespace AudioFileMetadataProcessor
{
    // Add this NAudioConverter class inside the namespace but outside Program
    public static class NAudioConverter
    {
        private static bool _mediaFoundationInitialized = false;

        static NAudioConverter()
        {
            // Initialize MediaFoundation for M4A/AAC support
            try
            {
                MediaFoundationApi.Startup();
                _mediaFoundationInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Warning: MediaFoundation initialization failed: {ex.Message}");
                Logger.Log("M4A conversion may not be available on this system");
            }
        }

        public static async Task<string?> ConvertAudioFile(string inputPath, ProcessingConfig config)
        {
            try
            {
                string? outputDirectory = config.OutputDirectory;
                if (string.IsNullOrEmpty(outputDirectory))
                {
                    throw new InvalidOperationException("Output directory must be specified in AppSettings:OutputPath.");
                }

                // Get seeder data to create proper filename
                var seederData = Program.GetSeederDataForFile(inputPath, config);
                string fileName = CreateOutputFileName(inputPath, seederData);
                string? outputPath = Path.Combine(outputDirectory, $"{fileName}.{config.ConvertFormat}");

                // Ensure output directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                    Logger.Log($"Created output directory: {outputDirectory}");
                }

                Logger.Log($"Converting to: {Path.GetFileName(outputPath)}");

                // Perform the conversion based on target format
                bool success = config.ConvertFormat switch
                {
                    "mp3" => await ConvertToMp3(inputPath, outputPath, config.Quality),
                    "wav" => await ConvertToWav(inputPath, outputPath),
                    "m4a" => await ConvertToM4a(inputPath, outputPath, config.Quality),
                    _ => throw new NotSupportedException($"Format {config.ConvertFormat} is not supported")
                };

                if (success && File.Exists(outputPath))
                {
                    Logger.Log("✓ NAudio conversion completed successfully");
                    return outputPath;
                }
                else
                {
                    Logger.Log("✗ NAudio conversion failed");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"NAudio conversion error: {ex.Message}");
                return null;
            }
        }

        private static string CreateOutputFileName(string inputPath, SeederData? seederData)
        {
            if (seederData != null && !string.IsNullOrEmpty(seederData.TrackNumber) && !string.IsNullOrEmpty(seederData.Title))
            {
                // Parse track number
                var trackParts = seederData.TrackNumber.Split('/');
                string trackNum = trackParts.Length > 0 ? trackParts[0].PadLeft(2, '0') : "00";

                // Clean title for filename
                string cleanTitle = Program.CleanFilename(Program.ToProperCase(seederData.Title));
                return $"{trackNum} {cleanTitle}";
            }
            return Path.GetFileNameWithoutExtension(inputPath);
        }

        private static async Task<bool> ConvertToMp3(string inputPath, string outputPath, string? quality)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var reader = CreateAudioReader(inputPath);
                    if (reader == null) return false;

                    // Determine MP3 quality settings
                    LAMEPreset preset = LAMEPreset.STANDARD;
                    int? bitRate = null;

                    if (!string.IsNullOrEmpty(quality))
                    {
                        if (int.TryParse(quality, out int qualityValue))
                        {
                            if (qualityValue <= 9) // VBR quality (0-9)
                            {
                                preset = qualityValue switch
                                {
                                    0 => LAMEPreset.INSANE,
                                    1 => LAMEPreset.EXTREME,
                                    2 => LAMEPreset.STANDARD,
                                    3 => LAMEPreset.STANDARD_FAST,
                                    4 => LAMEPreset.MEDIUM,
                                    5 => LAMEPreset.MEDIUM_FAST,
                                    _ => LAMEPreset.STANDARD
                                };
                            }
                            else // CBR bitrate
                            {
                                bitRate = qualityValue;
                                preset = LAMEPreset.ABR_160;
                            }
                        }
                    }

                    // Convert to 16-bit for MP3 encoding
                    var resampler = new WaveFormatConversionStream(
                        new WaveFormat(reader.WaveFormat.SampleRate, 16, reader.WaveFormat.Channels),
                        reader);

                    using var writer = new LameMP3FileWriter(outputPath, resampler.WaveFormat, preset, null);
                    resampler.CopyTo(writer);

                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"MP3 conversion error: {ex.Message}");
                    return false;
                }
            });
        }

        private static async Task<bool> ConvertToWav(string inputPath, string outputPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var reader = CreateAudioReader(inputPath);
                    if (reader == null) return false;

                    // Convert to standard PCM format
                    var targetFormat = new WaveFormat(44100, 16, 2);
                    using var resampler = new WaveFormatConversionStream(targetFormat, reader);

                    WaveFileWriter.CreateWaveFile(outputPath, resampler);
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"WAV conversion error: {ex.Message}");
                    return false;
                }
            });
        }             

        private static async Task<bool> ConvertToM4a(string inputPath, string outputPath, string? quality)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!_mediaFoundationInitialized)
                    {
                        Logger.Log("MediaFoundation not available for M4A conversion");
                        return false;
                    }

                    using var reader = CreateAudioReader(inputPath);
                    if (reader == null) return false;

                    // M4A/AAC bitrate (default 192 kbps)
                    int bitRate = 192000;
                    if (!string.IsNullOrEmpty(quality) && int.TryParse(quality, out int br))
                    {
                        bitRate = br * 1000; // Convert kbps to bps
                    }

                    // Convert to appropriate format for AAC encoding
                    var targetFormat = new WaveFormat(44100, 16, 2);
                    using var resampler = new WaveFormatConversionStream(targetFormat, reader);

                    // Use MediaFoundation for M4A encoding
                    MediaFoundationEncoder.EncodeToAac(resampler, outputPath, bitRate);
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"M4A conversion error: {ex.Message}");
                    return false;
                }
            });
        }

        private static WaveStream? CreateAudioReader(string inputPath)
        {
            string extension = Path.GetExtension(inputPath).ToLowerInvariant();

            try
            {
                return extension switch
                {
                    ".mp3" => new Mp3FileReader(inputPath),
                    ".wav" => new WaveFileReader(inputPath),                    
                    ".m4a" or ".mp4" or ".aac" => _mediaFoundationInitialized ?
                        new MediaFoundationReader(inputPath) : null,
                    ".wma" => _mediaFoundationInitialized ?
                        new MediaFoundationReader(inputPath) : null,
                    _ => _mediaFoundationInitialized ?
                        new MediaFoundationReader(inputPath) : new AudioFileReader(inputPath)
                };
            }
            catch (Exception ex)
            {
                Logger.Log($"Error creating audio reader for {inputPath}: {ex.Message}");
                return null;
            }
        }

        public static void Cleanup()
        {
            if (_mediaFoundationInitialized)
            {
                try
                {
                    MediaFoundationApi.Shutdown();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    // Add this Logger class inside the namespace but outside Program
    public static class Logger
    {
        private static string? _logFilePath;
        private static readonly object _lock = new();

        public static void Initialize(string logDirectory)
        {
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            _logFilePath = Path.Combine(logDirectory, $"log_{DateTime.Now:yyyyMMdd}.txt");
        }

        public static void Log(string message)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_logFilePath))
                {
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }
            }
            Console.WriteLine(message); // Optional: still show in console
        }
    }

    class Program
    {
        private static readonly HttpClient _httpClient = new();
        private static IConfiguration? _configuration;
        private static string? _MUSICBRAINZ_BASE_URL;
        internal static readonly string[] _stringArray = [".mp3", ".m4a", ".wav", ".wma", ".aac"];

        // Add a static instance of JsonSerializerOptions to cache and reuse
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        static async Task Main(string[] args)
        {
            try
            {
                // Load configuration
                if (!LoadConfiguration())
                {
                    Console.WriteLine("Failed to load configuration. Please check appsettings.json");
                    return;
                }

                // Initialize logger
                string logPath = GetConfigurationValue("AppSettings:LogPath", "Logs");
                Logger.Initialize(logPath);

                Logger.Log("Audio Metadata Tagger & Converter (NAudio Edition)");
                Logger.Log("==================================================");

                // Always use InputPath from configuration
                string inputPath = GetConfigurationValue("AppSettings:InputPath", "");
                if (string.IsNullOrEmpty(inputPath))
                {
                    ShowUsage();
                    return;
                }
                Logger.Log($"Using InputPath from configuration: {inputPath}");

                var config = ParseArguments(args, inputPath);
                if (config == null) return;

                // NAudio doesn't require external tools - just log the conversion format
                if (config.ConvertFormat != null)
                {
                    Logger.Log($"Audio conversion enabled: {config.ConvertFormat.ToUpper()}");
                    Logger.Log("Using NAudio for fast, reliable conversion");
                }

                if (System.IO.File.Exists(config.InputPath))
                {
                    await ProcessAudioFile(config.InputPath, config);
                }
                else if (Directory.Exists(config.InputPath))
                {
                    await ProcessDirectory(config.InputPath, config);
                }
                else
                {
                    Logger.Log("Invalid file or directory path.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error: {ex.Message}");
            }
            finally
            {
                // Cleanup NAudio resources
                NAudioConverter.Cleanup();
            }

            Logger.Log("\nPress any key to exit...");
            Console.ReadKey();
        }

        static bool LoadConfiguration()
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

                _configuration = builder.Build();

                // Load settings (removed FFmpeg path as it's no longer needed)
                _MUSICBRAINZ_BASE_URL = _configuration["MusicBrainz:BaseUrl"];

                // Set User-Agent for MusicBrainz (required)
                string? userAgent = _configuration["MusicBrainz:UserAgent"];
                if (!string.IsNullOrEmpty(userAgent))
                {
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
                }

                return !string.IsNullOrEmpty(_MUSICBRAINZ_BASE_URL);
            }
            catch (Exception ex)
            {
                Logger.Log($"Configuration error: {ex.Message}");
                return false;
            }
        }

        static void ShowUsage()
        {
            Logger.Log("Usage:");
            Logger.Log("  AudioFileMetadataProcessor.exe [input_path] [options]\n");
            Logger.Log("Note: If no input_path is provided, the program will use AppSettings:InputPath from appsettings.json\n");
            Logger.Log("Options:");
            Logger.Log("  -convert <format>     Convert to specified format (mp3, wav, m4a)");
            Logger.Log("  -quality <value>      Audio quality for conversion:");
            Logger.Log("                        MP3: 0-9 (0=best, 9=worst) or bitrate (128, 192, 320)");
            Logger.Log("                        M4A: bitrate (128, 192, 256, 320)");
            Logger.Log("  -output <directory>   Output directory for converted files");
            Logger.Log("                        (Uses AppSettings:OutputPath from config if not specified)");
            Logger.Log("  -preserve-original    Keep original files when converting");
            Logger.Log("  -seeders-file <path>  CSV file with seeder data");
            Logger.Log("                        (Uses AppSettings:SeedersFileCSVFullPath from config if not specified)\n");
            Logger.Log("NAudio Features:");
            Logger.Log("  ✓ No external dependencies (FFmpeg not required)");
            Logger.Log("  ✓ Faster conversion with native .NET libraries");
            Logger.Log("  ✓ Better error handling and stability");
            Logger.Log("  ✓ Support for MP3, WAV, M4A formats\n");
            Logger.Log("Examples:");
            Logger.Log("  AudioFileMetadataProcessor.exe (uses config paths)");
            Logger.Log("  AudioFileMetadataProcessor.exe \"C:\\Music\" -convert mp3");
            Logger.Log("  AudioFileMetadataProcessor.exe -convert mp3 -quality 5");
        }

        static ProcessingConfig? ParseArguments(string[] args, string inputPath)
        {
            var config = new ProcessingConfig { InputPath = inputPath };

            // Set default output directory from configuration
            config.OutputDirectory = GetConfigurationValue("AppSettings:OutputPath", "");

            // Set default seeders file from configuration
            config.SeedersFile = GetConfigurationValue("AppSettings:SeedersFileCSVFullPath", "");

            for (int i = (args.Length > 0 && !args[0].StartsWith("-") ? 1 : 0); i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-convert":
                        if (i + 1 < args.Length)
                            config.ConvertFormat = args[++i].ToLower();
                        break;
                    case "-quality":
                        if (i + 1 < args.Length)
                            config.Quality = args[++i];
                        break;
                    case "-output":
                        if (i + 1 < args.Length)
                            config.OutputDirectory = args[++i];
                        break;
                    case "-preserve-original":
                        config.PreserveOriginal = true;
                        break;
                    case "-seeders-file":
                        if (i + 1 < args.Length)
                            config.SeedersFile = args[++i];
                        break;
                }
            }

            // Validate format
            if (config.ConvertFormat != null)
            {
                var validFormats = new[] { "mp3", "wav", "m4a" };
                if (!validFormats.Contains(config.ConvertFormat))
                {
                    Logger.Log($"Invalid format: {config.ConvertFormat}");
                    Logger.Log($"Valid formats: {string.Join(", ", validFormats)}");
                    return null;
                }
            }

            // Load seeders file if specified
            if (!string.IsNullOrEmpty(config.SeedersFile))
            {
                config.SeedersData = LoadSeedersFile(config.SeedersFile);
                if (config.SeedersData == null)
                {
                    Logger.Log($"Failed to load seeders file: {config.SeedersFile}");
                    return null;
                }
            }

            // Validate that we have seeders data
            if (config.SeedersData == null)
            {
                Logger.Log("Error: No seeder data provided. Use -seeders-file or configure AppSettings:SeedersFileCSVFullPath");
                return null;
            }

            return config;
        }

        static Dictionary<string, SeederData>? LoadSeedersFile(string filePath)
        {
            try
            {
                var seedersData = new Dictionary<string, SeederData>(StringComparer.OrdinalIgnoreCase);
                var lines = System.IO.File.ReadAllLines(filePath);

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
                    if (parts.Count >= 9) // Need all 9 columns: artist,albumartist,title,album,year,tracknumber,discnumber,genre,filename
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
                            FileName = parts[8].Trim()
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

        static List<string> ParseCsvLine(string line)
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

        static async Task ProcessDirectory(string directoryPath, ProcessingConfig config)
        {
            string[] audioExtensions = GetConfigurationValue("SupportedFormats:AudioExtensions", _stringArray);

            var audioFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(file => audioExtensions.Contains(Path.GetExtension(file).ToLower()));

            int? totalFiles = audioFiles.Count();
            int? currentFile = 0;

            foreach (string filePath in audioFiles)
            {
                currentFile++;
                Logger.Log($"\n[{currentFile}/{totalFiles}] Processing: {Path.GetFileName(filePath)}");
                await ProcessAudioFile(filePath, config);
            }
        }

        static async Task ProcessAudioFile(string filePath, ProcessingConfig config)
        {
            try
            {
                Logger.Log($"Analyzing: {Path.GetFileName(filePath)}");

                string? workingFilePath = filePath;

                // Step 1: Convert format if requested (using NAudio)
                if (config.ConvertFormat != null)
                {
                    string? currentExtension = Path.GetExtension(filePath).TrimStart('.').ToLower();

                    if (currentExtension != config.ConvertFormat)
                    {
                        Logger.Log($"Converting from {currentExtension.ToUpper()} to {config.ConvertFormat.ToUpper()} using NAudio...");

                        string? convertedPath = await NAudioConverter.ConvertAudioFile(filePath, config);
                        if (convertedPath != null)
                        {
                            workingFilePath = convertedPath;
                            Logger.Log("✓ NAudio conversion completed");
                        }
                        else
                        {
                            Logger.Log("✗ NAudio conversion failed");
                            return;
                        }
                    }
                    else
                    {
                        Logger.Log($"File is already in {config.ConvertFormat.ToUpper()} format");
                    }
                }

                // Step 2: Get seeder data for this file
                var seederData = GetSeederDataForFile(workingFilePath, config);

                if (seederData == null)
                {
                    Logger.Log("No seeder data found for this file");
                    return;
                }

                // Step 3: Read current metadata
                var currentTags = ReadCurrentTags(workingFilePath);
                DisplayCurrentTags(currentTags);

                // Step 4: Create metadata from seeder data
                Logger.Log($"Using seeder data: {seederData.Artist} - {seederData.Title}");
                var metadata = CreateMetadataFromSeeder(seederData);

                // Step 5: Try to get cover art from MusicBrainz if album info is available
                if (!string.IsNullOrEmpty(seederData.Album) && !string.IsNullOrEmpty(seederData.Artist))
                {
                    Logger.Log("Searching for cover art...");
                    var coverArtUrl = await SearchForCoverArt(seederData);
                    if (!string.IsNullOrEmpty(coverArtUrl))
                    {
                        metadata.CoverArtUrl = coverArtUrl;
                        Logger.Log("✓ Found cover art");
                    }
                    else
                    {
                        Logger.Log("No cover art found");
                    }
                }

                // Step 6: Update tags
                Logger.Log("Updating metadata...");
                UpdateTags(workingFilePath, metadata);

                // Step 7: Download and embed artwork if available
                if (!string.IsNullOrEmpty(metadata.CoverArtUrl))
                {
                    Logger.Log("Downloading and embedding artwork...");
                    await EmbedArtwork(workingFilePath, metadata.CoverArtUrl);
                }

                DisplayUpdatedMetadata(metadata);
                Logger.Log("✓ File updated successfully!");

                // Clean up original file if conversion happened and preserve flag is not set
                if (config.ConvertFormat != null && workingFilePath != filePath && !config.PreserveOriginal)
                {
                    try
                    {
                        System.IO.File.Delete(filePath);
                        Logger.Log("✓ Original file removed");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Warning: Could not remove original file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error processing {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        // Make these methods public static so NAudioConverter can access them
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
                cleaned = cleaned.Replace(c, ' ');
            }

            // Remove additional problematic characters
            foreach (string s in additionalInvalid)
            {
                cleaned = cleaned.Replace(s, " ");
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

        static TrackMetadata CreateMetadataFromSeeder(SeederData seeder)
        {
            var metadata = new TrackMetadata
            {
                Title = ToProperCase(seeder.Title),
                Artist = ToProperCase(seeder.Artist),
                Album = ToProperCase(seeder.Album),
                AlbumArtist = ToProperCase(seeder.AlbumArtist),
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

        static async Task<string?> SearchForCoverArt(SeederData seeder)
        {
            try
            {
                // Build search query for MusicBrainz
                var queryParts = new List<string>();

                if (!string.IsNullOrEmpty(seeder.Album))
                    queryParts.Add($"release:\"{seeder.Album}\"");

                if (!string.IsNullOrEmpty(seeder.Artist))
                    queryParts.Add($"artist:\"{seeder.Artist}\"");

                queryParts.Add("status:official");
                queryParts.Add("format:cd");
                queryParts.Add("type:album");
                queryParts.Add("country:us");

                if (queryParts.Count == 0) return null;

                string? query = string.Join(" AND ", queryParts);
                string? encodedQuery = Uri.EscapeDataString(query);
                string? searchUrl = $"{_MUSICBRAINZ_BASE_URL}release/?query={encodedQuery}&limit=3&fmt=json";

                Logger.Log($"  Searching for cover art: {query}");

                // Add rate limiting delay
                int delayMs = GetConfigurationValue("MusicBrainz:RequestDelayMs", 1000);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                var response = await _httpClient.GetStringAsync(searchUrl);

                var searchResult = JsonSerializer.Deserialize<MusicBrainzReleaseSearchResponse>(
                    response,
                    _jsonSerializerOptions
                );

                if (searchResult?.Releases != null && searchResult.Releases.Length > 0)
                {
                    // Try to find exact album match first, otherwise use first result
                    var release = searchResult.Releases.FirstOrDefault(r =>
                        string.Equals(r.Title, seeder.Album, StringComparison.OrdinalIgnoreCase))
                        ?? searchResult.Releases[0];

                    if (release?.Id != null)
                    {
                        return await GetCoverArtUrl(release.Id);
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

        static async Task<string?> GetCoverArtUrl(string? releaseId)
        {
            if (string.IsNullOrEmpty(releaseId))
            {
                return null; // Return null if releaseId is null or empty
            }

            try
            {
                string? coverArtUrl = $"https://coverartarchive.org/release/{releaseId}";
                var response = await _httpClient.GetStringAsync(coverArtUrl);
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

        static Dictionary<string, string> ReadCurrentTags(string filePath)
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

        static void DisplayCurrentTags(Dictionary<string, string> tags)
        {
            Logger.Log("Current metadata:");
            foreach (var tag in tags)
            {
                if (!string.IsNullOrEmpty(tag.Value) && tag.Value != "0")
                    Logger.Log($"  {tag.Key}: {tag.Value}");
            }
        }

        static void DisplayUpdatedMetadata(TrackMetadata metadata)
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

        static void UpdateTags(string filePath, TrackMetadata metadata)
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

        static async Task EmbedArtwork(string filePath, string imageUrl)
        {
            try
            {
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

                using (var file = TagLib.File.Create(filePath))
                {
                    // Determine MIME type from image data
                    string? mimeType = GetImageMimeType(imageBytes);

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

        static string GetImageMimeType(byte[] imageBytes)
        {
            if (imageBytes.Length >= 4)
            {
                // Check for JPEG
                if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
                    return "image/jpeg";

                // Check for PNG
                if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 &&
                    imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                    return "image/png";
            }

            return "image/jpeg"; // Default fallback
        }

        // Add a helper method to safely retrieve configuration values
        static T GetConfigurationValue<T>(string key, T defaultValue)
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
    }

    // Configuration and data model classes
    public class ProcessingConfig
    {
        public string? InputPath { get; set; }
        public string? ConvertFormat { get; set; }
        public string? Quality { get; set; }
        public string? OutputDirectory { get; set; }
        public bool PreserveOriginal { get; set; }
        public string? SeedersFile { get; set; }
        public Dictionary<string, SeederData>? SeedersData { get; set; }
    }

    public class SeederData
    {
        public string? Artist { get; set; }
        public string? AlbumArtist { get; set; }
        public string? Title { get; set; }
        public string? Album { get; set; }
        public string? Year { get; set; }
        public string? TrackNumber { get; set; }
        public string? DiscNumber { get; set; }
        public string? Genre { get; set; }
        public string? FileName { get; set; }
    }

    // Metadata classes
    public class TrackMetadata
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? AlbumArtist { get; set; }
        public string? ReleaseDate { get; set; }
        public double? Duration { get; set; }
        public int? TrackNumber { get; set; }
        public int? TrackCount { get; set; }
        public int? DiscNumber { get; set; }
        public int? DiscCount { get; set; }
        public string? Genre { get; set; }
        public string? CoverArtUrl { get; set; }
        public double? Score { get; set; } // Search relevance score
    }

    // MusicBrainz API response classes for release search
    public class MusicBrainzReleaseSearchResponse
    {
        public MusicBrainzRelease[]? Releases { get; set; }
    }

    public class MusicBrainzSearchResponse
    {
        public MusicBrainzRecording[]? Recordings { get; set; }
    }

    public class MusicBrainzRecording
    {
        public string? Title { get; set; }
        public int? Length { get; set; }
        public int? Score { get; set; } // Search relevance score (0-100)
        public MusicBrainzArtistCredit[]? ArtistCredit { get; set; }
        public MusicBrainzRelease[]? Releases { get; set; }
        public MusicBrainzGenre[]? Genres { get; set; }
    }

    public class MusicBrainzArtistCredit
    {
        public MusicBrainzArtist? Artist { get; set; }
    }

    public class MusicBrainzArtist
    {
        public string? Name { get; set; }
    }

    public class MusicBrainzRelease
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Date { get; set; }
        public string? Country { get; set; }
        public string? Status { get; set; }
        public MusicBrainzArtistCredit[]? ArtistCredit { get; set; }
        public MusicBrainzMedia[]? Media { get; set; }
    }

    public class MusicBrainzGenre
    {
        public string? Name { get; set; }
    }

    public class MusicBrainzMedia
    {
        public string? Format { get; set; }
        public MusicBrainzTrack[]? Track { get; set; }
    }

    public class MusicBrainzTrack
    {
        public string? Title { get; set; }
        public string? Number { get; set; } // Track number as string
    }

    // Cover Art Archive API response classes
    public class CoverArtResponse
    {
        public CoverArtImage[]? Images { get; set; }
    }

    public class CoverArtImage
    {
        public string? Image { get; set; }
        public bool? Front { get; set; }
        public string[]? Types { get; set; }
    }
}