using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MP3FileManager
{
    public class AppSettings
    {
        public string LibraryPath { get; set; } = "";
        public string HighQualityPath { get; set; } = "";
        public string ArchivePath { get; set; } = "";
        public string LogPath { get; set; } = "";
        public bool ReplaceInLibrary { get; set; } = false;
        public bool WhatIfMode { get; set; } = false;
        public bool InteractiveMode { get; set; } = true;
    }

    public class MP3MetadataService
    {
        private readonly ILogger<MP3MetadataService> _logger;
        private readonly AppSettings _settings;
        private StreamWriter? _logWriter;
        private string _processedPath = "";

        public MP3MetadataService(ILogger<MP3MetadataService> logger, AppSettings settings)
        {
            _logger = logger;
            _settings = settings;
        }

        public async Task RunAsync()
        {
            Console.WriteLine("=== MP3 File Manager and Metadata Copier ===\n");
            Console.WriteLine("Manages metatags and updates the specified library with matching (hopefully) higher-quality files, if specified.\n");

            // Handle interactive mode or use settings
            if (_settings.InteractiveMode || string.IsNullOrEmpty(_settings.LibraryPath))
            {
                GetUserInput();
            }

            // Validate required paths
            if (!ValidatePaths())
            {
                return;
            }

            // Initialize logging
            InitializeLogging();

            try
            {
                await ProcessFilesAsync();
            }
            finally
            {
                _logWriter?.Close();
                Console.WriteLine($"\nDone. Log file written to: {_settings.LogPath}");
                Console.WriteLine("Check Processed_With_Metadata folder for results.");
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
        }

        private void GetUserInput()
        {
            if (string.IsNullOrEmpty(_settings.LibraryPath))
            {
                Console.Write("Enter path to MP3 file metadata library path: ");
                _settings.LibraryPath = Console.ReadLine()?.Trim('"') ?? "";
            }

            if (string.IsNullOrEmpty(_settings.HighQualityPath))
            {
                Console.Write("Enter path to High-Quality MP3 folder: ");
                _settings.HighQualityPath = Console.ReadLine()?.Trim('"') ?? "";
            }

            if (string.IsNullOrEmpty(_settings.LogPath))
            {
                Console.Write("Enter path for log output (leave blank for default): ");
                var logInput = Console.ReadLine()?.Trim('"') ?? "";
                _settings.LogPath = string.IsNullOrWhiteSpace(logInput)
                    ? Path.Combine(AppContext.BaseDirectory, $"MP3MetadataCopy_{DateTime.Now:yyyyMMdd_HHmmss}.log")
                    : logInput;
            }

            if (_settings.InteractiveMode)
            {
                Console.Write("Do you want to replace the files in library after processing? (y/N): ");
                _settings.ReplaceInLibrary = (Console.ReadLine()?.Trim() ?? "").Equals("y", StringComparison.CurrentCultureIgnoreCase);

                if (_settings.ReplaceInLibrary && string.IsNullOrEmpty(_settings.ArchivePath))
                {
                    Console.Write("Enter Archive Path for originals (required): ");
                    _settings.ArchivePath = Console.ReadLine()?.Trim('"') ?? "";

                    Console.WriteLine("\n-- WARNING: You chose to replace files in the library. --");
                    Console.WriteLine($"Files will be backed up to: {_settings.ArchivePath}");
                    Console.WriteLine("Check the files in the 'Processed_With_Metadata' folder BEFORE continuing.\n");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                }
            }
        }

        private bool ValidatePaths()
        {
            if (!Directory.Exists(_settings.LibraryPath))
            {
                Console.WriteLine($"Error: Library path not found: {_settings.LibraryPath}");
                return false;
            }

            if (!Directory.Exists(_settings.HighQualityPath))
            {
                Console.WriteLine($"Error: High-quality path not found: {_settings.HighQualityPath}");
                return false;
            }

            if (_settings.ReplaceInLibrary && string.IsNullOrEmpty(_settings.ArchivePath))
            {
                Console.WriteLine("Error: Archive path is required when replacing files in library.");
                return false;
            }

            return true;
        }

        private void InitializeLogging()
        {
            if (string.IsNullOrEmpty(_settings.LogPath))
            {
                _settings.LogPath = Path.Combine(AppContext.BaseDirectory, $"MP3MetadataCopy_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            }

            _processedPath = Path.Combine(_settings.HighQualityPath, "Processed_With_Metadata");

            if (!_settings.WhatIfMode)
            {
                Directory.CreateDirectory(_processedPath);
            }

            _logWriter = new StreamWriter(_settings.LogPath, false);
            Log($"Start Time: {DateTime.Now}");
            Log($"Library Path: {_settings.LibraryPath}");
            Log($"High Quality Path: {_settings.HighQualityPath}");
            Log($"Archive Path: {_settings.ArchivePath}");
            Log($"Replace In Library: {_settings.ReplaceInLibrary}");
            Log($"What If Mode: {_settings.WhatIfMode}");
        }

        private async Task ProcessFilesAsync()
        {
            // Map library by file name only
            Log("Indexing library files...");
            var libraryFilesDirectory = Directory.GetFiles(_settings.LibraryPath, "*.mp3", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            Log($"Found {libraryFilesDirectory.Count} files in library");

            var highQualityFiles = Directory.GetFiles(_settings.HighQualityPath, "*.mp3", SearchOption.AllDirectories);
            Log($"Found {highQualityFiles.Length} high-quality files to process");

            int processed = 0;
            int matched = 0;
            int errors = 0;
            var processedFiles = new List<(string OriginalHQ, string ProcessedFile, string MatchingLibraryFile)>();

            foreach (var hqFile in highQualityFiles)
            {
                var fileName = Path.GetFileName(hqFile);
                Log($"\nProcessing: {fileName}");

                if (!libraryFilesDirectory.ContainsKey(fileName))
                {
                    Log($"Skipping: {fileName} (no matching library file)");
                    continue;
                }

                matched++;
                string sourceFile = libraryFilesDirectory[fileName];
                string destFile = Path.Combine(_processedPath, fileName);

                Log($"Found match: {Path.GetFileName(sourceFile)}");

                if (_settings.WhatIfMode)
                {
                    Log($"[WHAT-IF] Would copy metadata from {Path.GetFileName(sourceFile)} to {fileName}");
                    processed++;
                    continue;
                }

                try
                {
                    // Copy metadata using TagLib
                    using (var source = TagLib.File.Create(sourceFile))
                    using (var dest = TagLib.File.Create(hqFile))
                    {
                        var tag = dest.Tag;
                        var srcTag = source.Tag;

                        // Copy basic metadata
                        tag.Performers = srcTag.Performers;
                        tag.AlbumArtists = srcTag.AlbumArtists;
                        tag.Album = srcTag.Album;
                        tag.Track = srcTag.Track;
                        tag.TrackCount = srcTag.TrackCount;
                        tag.Disc = srcTag.Disc;
                        tag.DiscCount = srcTag.DiscCount;
                        tag.Genres = srcTag.Genres;
                        tag.Title = srcTag.Title;
                        tag.Year = srcTag.Year;
                        tag.Comment = srcTag.Comment;
                        tag.Composers = srcTag.Composers;
                        tag.Conductor = srcTag.Conductor;
                        tag.BeatsPerMinute = srcTag.BeatsPerMinute;

                        // Copy ID3v2 specific tags and album art
                        if (srcTag is TagLib.Id3v2.Tag id3v2src && tag is TagLib.Id3v2.Tag id3v2dest)
                        {
                            id3v2dest.Publisher = id3v2src.Publisher;
                            id3v2dest.Lyrics = id3v2src.Lyrics;
                            // Copy album art
                            id3v2dest.Pictures = id3v2src.Pictures;
                        }

                        dest.Save();
                    }

                    // Copy the updated file to processed directory
                    File.Copy(hqFile, destFile, true);
                    Log($"Successfully processed: {fileName}");
                    processed++;

                    // Track for potential library replacement
                    processedFiles.Add((hqFile, destFile, sourceFile));
                }
                catch (Exception ex)
                {
                    Log($"Error processing {fileName}: {ex.Message}");
                    errors++;
                }
            }

            // Handle library replacement if requested
            if (_settings.ReplaceInLibrary && processedFiles.Count > 0)
            {
                Log("\nPost-processing: Replacing files in library...");
                await ReplaceLibraryFilesAsync(processedFiles);
            }

            // Summary
            Log("\n" + new string('=', 50));
            Log("SUMMARY");
            Log(new string('=', 50));
            Log($"Total high-quality files: {highQualityFiles.Length}");
            Log($"Files matched: {matched}");
            Log($"Files processed successfully: {processed}");
            Log($"Errors: {errors}");
            Log($"Files without matches: {highQualityFiles.Length - matched}");

            if (processed > 0)
            {
                Log($"\nProcessed files location: {_processedPath}");
                if (_settings.ReplaceInLibrary)
                {
                    Log($"Library files replaced: {processedFiles.Count}");
                    if (!string.IsNullOrEmpty(_settings.ArchivePath))
                    {
                        Log($"Original files archived to: {_settings.ArchivePath}");
                    }
                }
            }

            if (_settings.WhatIfMode)
            {
                Log("\nThis was a dry run. Set WhatIfMode to false to execute changes.");
            }
        }

        private async Task ReplaceLibraryFilesAsync(List<(string OriginalHQ, string ProcessedFile, string MatchingLibraryFile)> processedFiles)
        {
            if (!string.IsNullOrEmpty(_settings.ArchivePath))
            {
                Directory.CreateDirectory(_settings.ArchivePath);
            }

            foreach (var (originalHQ, processedFile, libraryFile) in processedFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(libraryFile);
                    Log($"Replacing: {fileName}");

                    if (_settings.WhatIfMode)
                    {
                        Log($"[WHAT-IF] Would replace {libraryFile} with {processedFile}");
                        continue;
                    }

                    // Archive original if path specified
                    if (!string.IsNullOrEmpty(_settings.ArchivePath))
                    {
                        var archiveFile = Path.Combine(_settings.ArchivePath, fileName);
                        File.Copy(libraryFile, archiveFile, true);
                        Log($"Archived original: {fileName}");
                    }

                    // Replace with processed file
                    File.Copy(processedFile, libraryFile, true);
                    Log($"Replaced in library: {fileName}");
                }
                catch (Exception ex)
                {
                    Log($"Error replacing {Path.GetFileName(libraryFile)}: {ex.Message}");
                }
            }
        }

        private void Log(string message)
        {
            Console.WriteLine(message);
            _logWriter?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            _logger.LogInformation(message);
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            // Build configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true) // Local settings not in source control
                .AddUserSecrets<Program>() // User secrets for sensitive data
                .AddEnvironmentVariables()
                .AddCommandLine(args);

            var config = builder.Build();

            // Create host
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Bind configuration
                    var appSettings = new AppSettings();
                    config.GetSection("AppSettings").Bind(appSettings);
                    services.AddSingleton(appSettings);

                    services.AddSingleton<MP3MetadataService>();
                })
                .Build();

            // Run the service
            var service = host.Services.GetRequiredService<MP3MetadataService>();
            await service.RunAsync();
        }
    }
}