using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace AudioFileMetadataProcessor
{
    class Program
    {
        private static readonly HttpClient _httpClient = new();
        private static IConfiguration? _configuration;
        private static string? _MUSICBRAINZ_BASE_URL;
        private static string? _FFMPEG_PATH;
        internal static readonly string[] _stringArray = [".mp3", ".flac", ".m4a", ".ogg", ".wav", ".wma", ".aac"];

        static async Task Main(string[] args)
        {
            Console.WriteLine("Audio Metadata Tagger & Converter");
            Console.WriteLine("=================================");

            // Load configuration
            if (!LoadConfiguration())
            {
                Console.WriteLine("Failed to load configuration. Please check appsettings.json");
                return;
            }

            // Use InputPath from configuration if no arguments provided
            string inputPath;
            if (args.Length == 0)
            {
                inputPath = GetConfigurationValue("AppSettings:InputPath", "");
                if (string.IsNullOrEmpty(inputPath))
                {
                    ShowUsage();
                    return;
                }
                Console.WriteLine($"Using InputPath from configuration: {inputPath}");
            }
            else
            {
                inputPath = args[0];
            }

            var config = ParseArguments(args, inputPath);
            if (config == null) return;

            // Check for required tools
            if (config.ConvertFormat != null && !CheckFFmpegAvailable())
            {
                Console.WriteLine($"FFmpeg is required for audio conversion but was not found at: {_FFMPEG_PATH}");
                Console.WriteLine("Please install FFmpeg and update the FFmpegPath in appsettings.json");
                return;
            }

            try
            {
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
                    Console.WriteLine("Invalid file or directory path.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
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

                // Load settings
                _MUSICBRAINZ_BASE_URL = _configuration["MusicBrainz:BaseUrl"];
                _FFMPEG_PATH = _configuration["FFmpeg:ExecutablePath"];

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
                Console.WriteLine($"Configuration error: {ex.Message}");
                return false;
            }
        }

        static void ShowUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  AudioMetadataTagger.exe [input_path] [options]");
            Console.WriteLine();
            Console.WriteLine("Note: If no input_path is provided, the program will use AppSettings:InputPath from appsettings.json");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -convert <format>     Convert to specified format (mp3, flac, wav, m4a, ogg)");
            Console.WriteLine("  -quality <value>      Audio quality for conversion:");
            Console.WriteLine("                        MP3: 0-9 (0=best, 9=worst) or bitrate (128, 192, 320)");
            Console.WriteLine("                        FLAC: 0-8 compression level");
            Console.WriteLine("                        M4A: bitrate (128, 192, 256, 320)");
            Console.WriteLine("  -output <directory>   Output directory for converted files");
            Console.WriteLine("                        (Uses AppSettings:OutputPath from config if not specified)");
            Console.WriteLine("  -preserve-original    Keep original files when converting");
            Console.WriteLine("  -artist <name>        Artist name for metadata search");
            Console.WriteLine("  -title <name>         Song title for metadata search");
            Console.WriteLine("  -album <name>         Album name for metadata search");
            Console.WriteLine("  -seeders-file <path>  CSV file with seeder data (artist,title,album,filename)");
            Console.WriteLine("                        (Uses AppSettings:SeedersFileCSVFullPath from config if not specified)");
            Console.WriteLine();
            Console.WriteLine("AppSettings.json Configuration:");
            Console.WriteLine("  \"AppSettings\": {");
            Console.WriteLine("    \"SeedersFileCSVFullPath\": \"C:\\\\Data\\\\_Template.csv\",");
            Console.WriteLine("    \"OutputPath\": \"C:\\\\Data\\\\mp3\"");
            Console.WriteLine("  }");
            Console.WriteLine();
            Console.WriteLine("CSV Format:");
            Console.WriteLine("  artist,title,album,filename");
            Console.WriteLine("  \"The Beatles\",\"Hey Jude\",\"The Beatles 1967-1970\",\"hey-jude\"");
            Console.WriteLine("  \"Queen\",\"Bohemian Rhapsody\",\"A Night at the Opera\",\"\"");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  AudioMetadataTagger.exe (uses config paths)");
            Console.WriteLine("  AudioMetadataTagger.exe \"song.wav\" -artist \"The Beatles\" -title \"Hey Jude\"");
            Console.WriteLine("  AudioMetadataTagger.exe \"C:\\Music\" -convert mp3");
            Console.WriteLine("  AudioMetadataTagger.exe -convert flac -quality 5");
        }

        static ProcessingConfig? ParseArguments(string[] args, string inputPath)
        {
            var config = new ProcessingConfig { InputPath = inputPath };

            // Set default output directory from configuration
            config.OutputDirectory = GetConfigurationValue("AppSettings:OutputPath", "");

            // Set default seeders file from configuration if not processing a specific file
            if (Directory.Exists(inputPath) || string.IsNullOrEmpty(Path.GetExtension(inputPath)))
            {
                config.SeedersFile = GetConfigurationValue("AppSettings:SeedersFileCSVFullPath", "");
            }

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
                    case "-artist":
                        if (i + 1 < args.Length)
                            config.SeederArtist = args[++i];
                        break;
                    case "-title":
                        if (i + 1 < args.Length)
                            config.SeederTitle = args[++i];
                        break;
                    case "-album":
                        if (i + 1 < args.Length)
                            config.SeederAlbum = args[++i];
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
                var validFormats = new[] { "mp3", "flac", "wav", "m4a", "ogg" };
                if (!validFormats.Contains(config.ConvertFormat))
                {
                    Console.WriteLine($"Invalid format: {config.ConvertFormat}");
                    Console.WriteLine($"Valid formats: {string.Join(", ", validFormats)}");
                    return null;
                }
            }

            // Load seeders file if specified
            if (!string.IsNullOrEmpty(config.SeedersFile))
            {
                config.SeedersData = LoadSeedersFile(config.SeedersFile);
                if (config.SeedersData == null)
                {
                    Console.WriteLine($"Failed to load seeders file: {config.SeedersFile}");
                    return null;
                }
            }

            // Validate that we have some way to identify tracks
            if (string.IsNullOrEmpty(config.SeederArtist) && string.IsNullOrEmpty(config.SeederTitle) &&
                config.SeedersData == null)
            {
                Console.WriteLine("Error: No seeder data provided. Use -artist/-title or -seeders-file or configure AppSettings:SeedersFileCSVFullPath");
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

                Console.WriteLine($"Loading seeders from: {filePath}");

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
                    if (parts.Count >= 2) // Need at least artist and title
                    {
                        var seeder = new SeederData
                        {
                            Artist = parts[0].Trim(),
                            Title = parts[1].Trim(),
                            Album = parts.Count > 2 ? parts[2].Trim() : "",
                            FileName = parts.Count > 3 ? parts[3].Trim() : ""
                        };

                        // Use filename as key if provided, otherwise use title
                        string? key = !string.IsNullOrEmpty(seeder.FileName) ? seeder.FileName : seeder.Title;
                        if (!string.IsNullOrEmpty(key))
                        {
                            seedersData[key] = seeder;
                        }
                    }
                }

                Console.WriteLine($"Loaded {seedersData.Count} seeder entries");
                return seedersData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading seeders file: {ex.Message}");
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

        static bool CheckFFmpegAvailable()
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = _FFMPEG_PATH,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                if (process == null)
                {
                    Console.WriteLine("FFmpeg process could not be started. Check the FFmpeg path in appsettings.json.");
                    return false;
                }
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
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
                Console.WriteLine($"\n[{currentFile}/{totalFiles}] Processing: {Path.GetFileName(filePath)}");
                await ProcessAudioFile(filePath, config);
            }
        }

        static async Task ProcessAudioFile(string filePath, ProcessingConfig config)
        {
            try
            {
                Console.WriteLine($"Analyzing: {Path.GetFileName(filePath)}");

                string? workingFilePath = filePath;

                // Step 1: Convert format if requested
                if (config.ConvertFormat != null)
                {
                    string? currentExtension = Path.GetExtension(filePath).TrimStart('.').ToLower();

                    if (currentExtension != config.ConvertFormat)
                    {
                        Console.WriteLine($"Converting from {currentExtension.ToUpper()} to {config.ConvertFormat.ToUpper()}...");

                        string? convertedPath = await ConvertAudioFile(filePath, config);
                        if (convertedPath != null)
                        {
                            workingFilePath = convertedPath;
                            Console.WriteLine("✓ Conversion completed");
                        }
                        else
                        {
                            Console.WriteLine("✗ Conversion failed");
                            return;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"File is already in {config.ConvertFormat.ToUpper()} format");
                    }
                }

                // Step 2: Get seeder data for this file
                var seederData = GetSeederDataForFile(workingFilePath, config);

                if (seederData == null)
                {
                    Console.WriteLine("No seeder data found for this file");
                    return;
                }

                // Step 3: Read current metadata
                var currentTags = ReadCurrentTags(workingFilePath);
                DisplayCurrentTags(currentTags);

                // Step 4: Search MusicBrainz using seeder data
                Console.WriteLine($"Searching with: {seederData.Artist} - {seederData.Title}");
                var searchResults = await SearchMusicBrainzBySeeder(seederData);                

                if (searchResults == null || searchResults?.Count == 0)
                {
                    Console.WriteLine("No matches found in MusicBrainz database");
                    return;
                }
                if (searchResults != null && searchResults.Count > 1)
                {
                    Console.WriteLine($"Found {searchResults.Count} matches, selecting best match...");
                }

                if (searchResults == null)
                {
                    Console.WriteLine("Error: Search results are null");
                    return;
                }
                var metadata = searchResults[0]; // Take best match

                Console.WriteLine($"✓ Found match with {metadata.Score:P1} confidence");

                // Step 5: Update tags
                Console.WriteLine("Updating metadata...");
                UpdateTags(workingFilePath, metadata);

                // Step 6: Download and embed artwork
                if (!string.IsNullOrEmpty(metadata.CoverArtUrl))
                {
                    Console.WriteLine("Downloading and embedding artwork...");
                    await EmbedArtwork(workingFilePath, metadata.CoverArtUrl);
                }

                DisplayUpdatedMetadata(metadata);
                Console.WriteLine("✓ File updated successfully!");

                // Clean up original file if conversion happened and preserve flag is not set
                if (config.ConvertFormat != null && workingFilePath != filePath && !config.PreserveOriginal)
                {
                    try
                    {
                        System.IO.File.Delete(filePath);
                        Console.WriteLine("✓ Original file removed");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not remove original file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        static async Task<string?> ConvertAudioFile(string inputPath, ProcessingConfig config)
        {
            try
            {
                // Use configured output directory if available, otherwise fall back to input directory
                string? outputDirectory = !string.IsNullOrEmpty(config.OutputDirectory)
                    ? config.OutputDirectory
                    : Path.GetDirectoryName(inputPath);

                string? fileName = Path.GetFileNameWithoutExtension(inputPath);

                // Ensure outputDirectory is not null before using it in Path.Combine
                if (string.IsNullOrEmpty(outputDirectory))
                {
                    throw new InvalidOperationException("Output directory cannot be null or empty.");
                }

                string? outputPath = Path.Combine(outputDirectory, $"{fileName}.{config.ConvertFormat}");
                // Ensure output directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                    Console.WriteLine($"Created output directory: {outputDirectory}");
                }

                // Build FFmpeg arguments based on target format
                string? ffmpegArgs = BuildFFmpegArguments(inputPath, outputPath, config);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _FFMPEG_PATH,
                        Arguments = ffmpegArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                // Read output to track progress
                string? output = await process.StandardOutput.ReadToEndAsync();
                string? error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && System.IO.File.Exists(outputPath))
                {
                    return outputPath;
                }
                else
                {
                    Console.WriteLine($"FFmpeg error: {error}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Conversion error: {ex.Message}");
                return null;
            }
        }

        static string BuildFFmpegArguments(string inputPath, string outputPath, ProcessingConfig config)
        {
            var args = new List<string>
            {
                "-i", $"\"{inputPath}\"",
                "-y" // Overwrite output file
            };

            if (_configuration == null)
            {
                throw new InvalidOperationException("Configuration is not loaded.");
            }

            switch (config.ConvertFormat)
            {
                case "mp3":
                    args.AddRange(new[] { "-codec:a", "libmp3lame" });
                    args.AddRange(new[] { "-ar", "44100", "-ac", "2" });
                    if (!string.IsNullOrEmpty(config.Quality))
                    {
                        if (int.TryParse(config.Quality, out int qualityNum))
                        {
                            if (qualityNum <= 9)
                            {
                                args.AddRange(new[] { "-q:a", config.Quality });
                            }
                            else
                            {
                                args.AddRange(new[] { "-b:a", $"{config.Quality}k" });
                            }
                        }
                    }
                    else
                    {
                        string defaultQuality = _configuration["Conversion:DefaultQualities:Mp3"] ?? "2";
                        args.AddRange(new[] { "-q:a", defaultQuality });
                    }
                    break;

                case "flac":
                    args.AddRange(new[] { "-codec:a", "flac" });
                    var flacCompression = !string.IsNullOrEmpty(config.Quality) ? config.Quality
                        : _configuration["Conversion:DefaultQualities:Flac"] ?? "5";
                    args.AddRange(new[] { "-compression_level", flacCompression });
                    break;

                case "wav":
                    args.AddRange(new[] { "-codec:a", "pcm_s16le" });
                    break;

                case "m4a":
                    args.AddRange(new[] { "-codec:a", "aac" });
                    var m4aBitrate = !string.IsNullOrEmpty(config.Quality) ? config.Quality
                        : _configuration["Conversion:DefaultQualities:M4a"] ?? "192";
                    args.AddRange(new[] { "-b:a", $"{m4aBitrate}k" });
                    break;

                case "ogg":
                    args.AddRange(new[] { "-codec:a", "libvorbis" });
                    var oggQuality = !string.IsNullOrEmpty(config.Quality) ? config.Quality
                        : _configuration["Conversion:DefaultQualities:Ogg"] ?? "5";
                    args.AddRange(new[] { "-q:a", oggQuality });
                    break;
            }

            args.Add($"\"{outputPath}\"");
            return string.Join(" ", args);
        }

        static SeederData? GetSeederDataForFile(string filePath, ProcessingConfig config)
        {
            string? fileName = Path.GetFileNameWithoutExtension(filePath);

            // Check command line seeders first
            if (!string.IsNullOrEmpty(config.SeederArtist) || !string.IsNullOrEmpty(config.SeederTitle))
            {
                return new SeederData
                {
                    Artist = config.SeederArtist ?? "",
                    Title = config.SeederTitle ?? "",
                    Album = config.SeederAlbum ?? ""
                };
            }

            // Check seeders file data
            if (config.SeedersData != null)
            {
                // Try exact filename match first
                if (config.SeedersData.TryGetValue(fileName, out SeederData? exactMatch))
                {
                    Console.WriteLine("  Found exact filename match");
                    return exactMatch;
                }

                // Try partial filename match
                var partialMatch = config.SeedersData.Values.FirstOrDefault(s =>
                    !string.IsNullOrEmpty(s.FileName) &&
                    fileName.Contains(s.FileName, StringComparison.OrdinalIgnoreCase));

                if (partialMatch != null)
                {
                    Console.WriteLine("  Found partial filename match");
                    return partialMatch;
                }

                // Try fuzzy matching with artist - title pattern in filename
                var fuzzyMatch = config.SeedersData.Values.FirstOrDefault(s =>
                {
                    if (string.IsNullOrEmpty(s.Artist) || string.IsNullOrEmpty(s.Title)) return false;

                    var artistInName = fileName.Contains(s.Artist, StringComparison.OrdinalIgnoreCase);
                    var titleInName = fileName.Contains(s.Title, StringComparison.OrdinalIgnoreCase);

                    return artistInName && titleInName;
                });

                if (fuzzyMatch != null)
                {
                    Console.WriteLine($"  Found fuzzy match: {fuzzyMatch.Artist} - {fuzzyMatch.Title}");
                    return fuzzyMatch;
                }
            }

            return null;
        }

        static async Task<List<TrackMetadata>> SearchMusicBrainzBySeeder(SeederData seeder)
        {
            try
            {
                var results = new List<TrackMetadata>();

                // Build search query
                var queryParts = new List<string>();

                if (!string.IsNullOrEmpty(seeder.Title))
                    queryParts.Add($"recording:\"{seeder.Title}\"");

                if (!string.IsNullOrEmpty(seeder.Artist))
                    queryParts.Add($"artist:\"{seeder.Artist}\"");

                if (!string.IsNullOrEmpty(seeder.Album))
                    queryParts.Add($"release:\"{seeder.Album}\"");

                if (queryParts.Count == 0) return results;

                string? query = string.Join(" AND ", queryParts);
                string? encodedQuery = Uri.EscapeDataString(query);

                // Replace the problematic line with a call to the helper method
                int? searchLimit = GetConfigurationValue("MusicBrainz:SearchLimit", 5);
                string? searchUrl = $"{_MUSICBRAINZ_BASE_URL}recording?query={encodedQuery}&limit={searchLimit}&fmt=json";

                Console.WriteLine($"  Searching MusicBrainz: {query}");

                // Add rate limiting delay
                int delayMs = GetConfigurationValue("MusicBrainz:RequestDelayMs", 1000);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                var response = await _httpClient.GetStringAsync(searchUrl);
                var searchResult = JsonSerializer.Deserialize<MusicBrainzSearchResponse>(response);

                if (searchResult?.Recordings != null)
                {
                    int maxResults = GetConfigurationValue("MusicBrainz:MaxResults", 3);

                    foreach (var recording in searchResult.Recordings.Take(maxResults))
                    {
                        var metadata = new TrackMetadata
                        {
                            Title = recording.Title,
                            Artist = recording.ArtistCredit?[0]?.Artist?.Name ?? "",
                            Duration = recording.Length / 1000.0,
                            Score = recording.Score / 100.0 // Convert to 0-1 range
                        };

                        // Get detailed release info
                        if (recording.Releases?.Length > 0)
                        {
                            var release = recording.Releases[0];
                            metadata.Album = release.Title;
                            metadata.ReleaseDate = release.Date;

                            // Get album artist
                            if (release.ArtistCredit?.Length > 0)
                            {
                                metadata.AlbumArtist = release.ArtistCredit[0].Artist?.Name;
                            }

                            // Get cover art
                            metadata.CoverArtUrl = await GetCoverArtUrl(release.Id);
                        }

                        results.Add(metadata);
                        Console.WriteLine($"  Found: {metadata.Artist} - {metadata.Title} (Score: {metadata.Score:P1})");
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MusicBrainz search error: {ex.Message}");
                return [];
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
                var coverArt = JsonSerializer.Deserialize<CoverArtResponse>(response);

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
                    tags["AlbumArtist"] = string.Join(", ", file.Tag.AlbumArtists ?? new string[0]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading tags: {ex.Message}");
            }

            return tags;
        }

        static void DisplayCurrentTags(Dictionary<string, string> tags)
        {
            Console.WriteLine("Current metadata:");
            foreach (var tag in tags)
            {
                if (!string.IsNullOrEmpty(tag.Value) && tag.Value != "0")
                    Console.WriteLine($"  {tag.Key}: {tag.Value}");
            }
        }

        static void DisplayUpdatedMetadata(TrackMetadata metadata)
        {
            Console.WriteLine("Updated metadata:");
            Console.WriteLine($"  Title: {metadata.Title}");
            Console.WriteLine($"  Artist: {metadata.Artist}");
            Console.WriteLine($"  Album: {metadata.Album}");
            if (!string.IsNullOrEmpty(metadata.AlbumArtist))
                Console.WriteLine($"  Album Artist: {metadata.AlbumArtist}");
            if (!string.IsNullOrEmpty(metadata.ReleaseDate))
                Console.WriteLine($"  Year: {metadata.ReleaseDate}");
            if (metadata.TrackNumber > 0)
                Console.WriteLine($"  Track: {metadata.TrackNumber}");
            if (!string.IsNullOrEmpty(metadata.Genre))
                Console.WriteLine($"  Genre: {metadata.Genre}");
            if (!string.IsNullOrEmpty(metadata.CoverArtUrl))
                Console.WriteLine($"  Cover Art: Embedded");
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
                        if (DateTime.TryParse(metadata.ReleaseDate, out DateTime releaseDate))
                            file.Tag.Year = (uint)releaseDate.Year;
                    }

                    if (metadata.TrackNumber > 0)
                        file.Tag.Track = (uint)metadata.TrackNumber;

                    if (!string.IsNullOrEmpty(metadata.Genre))
                        file.Tag.Genres = metadata.Genre.Split(',').Select(g => g.Trim()).ToArray();

                    file.Save();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating tags: {ex.Message}");
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

                Console.WriteLine("✓ Artwork embedded successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error embedding artwork: {ex.Message}");
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
        public string? SeederArtist { get; set; }
        public string? SeederTitle { get; set; }
        public string? SeederAlbum { get; set; }
        public string? SeedersFile { get; set; }
        public Dictionary<string, SeederData>? SeedersData { get; set; }
    }

    public class SeederData
    {
        public string? Artist { get; set; }
        public string? Title { get; set; }
        public string? Album { get; set; }
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
        public string? Genre { get; set; }
        public string? CoverArtUrl { get; set; }
        public double? Score { get; set; } // Search relevance score
    }

    // MusicBrainz API response classes
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
        public MusicBrainzArtistCredit[]? ArtistCredit { get; set; }
    }

    public class MusicBrainzGenre
    {
        public string? Name { get; set; }
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