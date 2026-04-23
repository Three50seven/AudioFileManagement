using System.Text.Json;
using AudioFileMetadataProcessor.Helpers;
using AudioFileMetadataProcessor.Domain;

namespace AudioFileMetadataProcessor
{
    public class Program
    {
        // Keep per-run caches here (previously top-level fields).
        private static string? _cacheKey;
        private static Dictionary<string, byte[]> _coverArtCache = new();
        private static Dictionary<string, string> _coverArtUrlCache = new();
        internal static readonly string[] _stringArray = new[] { ".mp3", ".m4a", ".wav", ".wma", ".aac" };

        // Reuse Json options from ConfigurationHelper where possible
        private static readonly JsonSerializerOptions _jsonSerializerOptions = ConfigurationHelper.JsonOptions;

        public static Dictionary<string, byte[]> CoverArtCache { get => _coverArtCache; set => _coverArtCache = value; }
        public static Dictionary<string, string> CoverArtUrlCache { get => _coverArtUrlCache; set => _coverArtUrlCache = value; }
        public static string? CacheKey { get => _cacheKey; set => _cacheKey = value; }

        static async Task Main(string[] args)
        {
            try
            {
                // Initialize configuration & shared HttpClient
                if (!ConfigurationHelper.Initialize())
                {
                    Console.WriteLine("Failed to load configuration. Please check appsettings.json");
                    return;
                }

                // Initialize logger
                string logPath = ConfigurationHelper.GetValue("AppSettings:LogPath", "Logs");
                Logger.Initialize(logPath);

                Logger.Log("Audio Metadata Tagger & Converter (NAudio Edition)");
                Logger.Log("==================================================");

                // Always use InputPath from configuration
                string inputPath = ConfigurationHelper.GetValue("AppSettings:InputPath", "");
                if (string.IsNullOrEmpty(inputPath))
                {
                    Logger.ShowUsage();
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

        static ProcessingConfig? ParseArguments(string[] args, string inputPath)
        {
            var config = new ProcessingConfig { InputPath = inputPath };

            // Set default output directory from configuration
            config.OutputDirectory = ConfigurationHelper.GetValue("AppSettings:OutputPath", "");

            // Set default seeders file from configuration
            config.SeedersFile = ConfigurationHelper.GetValue("AppSettings:SeedersFileCSVFullPath", "");

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
                config.SeedersData = SeederHelper.LoadSeedersFile(config.SeedersFile);
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

        static async Task ProcessDirectory(string directoryPath, ProcessingConfig config)
        {
            string[] audioExtensions = ConfigurationHelper.GetValue("SupportedFormats:AudioExtensions", _stringArray);

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
                var seederData = SeederHelper.GetSeederDataForFile(workingFilePath, config);

                if (seederData == null)
                {
                    Logger.Log("No seeder data found for this file");
                    return;
                }

                // Step 3: Read current metadata
                var currentTags = MetadataHelper.ReadCurrentTags(workingFilePath);
                MetadataHelper.DisplayCurrentTags(currentTags);

                // Step 4: Create metadata from seeder data
                Logger.Log($"Using seeder data: {seederData.Artist} - {seederData.Title}");
                var metadata = MetadataHelper.CreateMetadataFromSeeder(seederData);

                // Step 5: Try to get cover art from MusicBrainz if album info is available
                if (seederData != null && !string.IsNullOrEmpty(seederData.Album) && !string.IsNullOrEmpty(seederData.Artist))
                {
                    // Set cache key for cover art - saves multiple requests for same artist/album
                    CacheKey = $"{seederData.Artist.ToLowerInvariant()}|{seederData.Album.ToLowerInvariant()}";

                    Logger.Log("Searching for cover art...");
                    var coverArtUrl = await MetadataHelper.SearchForCoverArt(seederData);
                    if (!string.IsNullOrEmpty(coverArtUrl))
                    {
                        metadata.CoverArtUrl = coverArtUrl;
                        Logger.Log($"   ✓ Found/Downloaded cover art using URL: {coverArtUrl}");
                    }
                    else
                    {
                        Logger.Log($"   No cover art found");
                    }
                }

                // Step 6: Update tags
                Logger.Log("Updating metadata...");
                MetadataHelper.UpdateTags(workingFilePath, metadata);

                // Step 7: Embed artwork if available
                if (!string.IsNullOrEmpty(metadata.CoverArtUrl))
                {
                    Logger.Log("Embedding artwork...");
                    await MetadataHelper.EmbedArtwork(workingFilePath, metadata.CoverArtUrl);
                }

                MetadataHelper.DisplayUpdatedMetadata(metadata);
                Logger.Log("✓ File updated successfully!");

                // Step 8: Rename file based on seeder data
                string? newFileName = MetadataHelper.CreateOutputFileName(workingFilePath, seederData);
                if (newFileName != null) {
                    string? newFilePath = Path.Combine(Path.GetDirectoryName(workingFilePath) ?? "", $"{newFileName}.{config.ConvertFormat}");
                    if (newFilePath != workingFilePath)
                    {
                        try
                        {
                            if (File.Exists(newFilePath))
                            {
                                Logger.Log($"Warning: File already exists, skipping rename: {newFilePath}");
                            }
                            else
                            {
                                File.Move(workingFilePath, newFilePath);
                                Logger.Log($"✓ Renamed file to: {newFileName}.{config.ConvertFormat}");
                                workingFilePath = newFilePath; // Update working path to new name
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Error renaming file: {ex.Message}");
                        }
                    }
                }

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
    }
}