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
    public class LibraryFileInfo
    {
        public string FullPath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Title { get; set; } = "";
        public uint Year { get; set; }
        public long FileSize { get; set; }
        public string RelativePath { get; set; } = "";
    }

    public class AppSettings
    {
        public string LibraryPath { get; set; } = "";
        public string HighQualityPath { get; set; } = "";
        public string ArchivePath { get; set; } = "";
        public string LogPath { get; set; } = "";
        public bool ReplaceInLibrary { get; set; } = false;
        public bool WhatIfMode { get; set; } = false;
        public bool InteractiveMode { get; set; } = true;
        public bool AutoSelectBestMatch { get; set; } = true;  // New option for handling duplicates
        public bool PromptForDuplicates { get; set; } = false;  // New option for interactive duplicate handling
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

        private void Log(string message)
        {
            var timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            Console.WriteLine(message);
            _logger.LogInformation(message);

            try
            {
                _logWriter?.WriteLine(timestampedMessage);
                _logWriter?.Flush(); // Force immediate write
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not write to log file: {ex.Message}");
                // Continue execution even if logging fails
            }
        }

        private void CopyAlbumArt(TagLib.File source, TagLib.File dest)
        {
            try
            {
                var srcTag = source.Tag;
                var destTag = dest.Tag;

                Log($"    Source file tag types: {source.TagTypes}");
                Log($"    Destination file tag types: {dest.TagTypes}");

                // Clear any existing pictures first
                destTag.Pictures = null;

                if (srcTag.Pictures != null && srcTag.Pictures.Length > 0)
                {
                    Log($"    Found {srcTag.Pictures.Length} pictures in source Tag.Pictures");

                    // iTunes-specific album art embedding
                    var success = EmbedAlbumArtForItunes(source, dest);

                    if (!success)
                    {
                        // Fallback to standard method
                        Log("    iTunes-specific method failed, trying standard approach...");
                        destTag.Pictures = ClonePictureArray(srcTag.Pictures);
                    }

                    return;
                }

                Log("    No album art found in source file");
            }
            catch (Exception ex)
            {
                Log($"    Error copying album art: {ex.Message}");
                Log($"    Stack trace: {ex.StackTrace}");
            }
        }

        private bool EmbedAlbumArtForItunes(TagLib.File source, TagLib.File dest)
        {
            try
            {
                // Ensure destination has ID3v2.3 tag (iTunes preferred format)
                var id3v2Dest = dest.GetTag(TagLib.TagTypes.Id3v2, true) as TagLib.Id3v2.Tag;
                if (id3v2Dest == null)
                {
                    Log("    Could not create ID3v2 tag on destination");
                    return false;
                }

                // Set ID3v2 version to 2.3 (iTunes compatibility)
                id3v2Dest.Version = 3;

                // Remove any existing APIC frames
                id3v2Dest.RemoveFrames("APIC");

                // Find the best album art from source
                TagLib.IPicture? bestPicture = null;

                // First, try to get from source pictures
                if (source.Tag.Pictures != null && source.Tag.Pictures.Length > 0)
                {
                    // Prefer FrontCover, then Other, then any type
                    bestPicture = source.Tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)
                               ?? source.Tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.Other)
                               ?? source.Tag.Pictures.FirstOrDefault();
                }

                if (bestPicture == null)
                {
                    Log("    No suitable picture found in source");
                    return false;
                }

                Log($"    Using picture: Type={bestPicture.Type}, MimeType={bestPicture.MimeType}, Size={bestPicture.Data.Count} bytes");

                // Create iTunes-compatible APIC frame
                var apicFrame = new TagLib.Id3v2.AttachmentFrame
                {
                    Type = TagLib.PictureType.FrontCover, // iTunes expects FrontCover
                    MimeType = NormalizeMimeType(bestPicture.MimeType),
                    Description = "Cover", // iTunes-friendly description
                    Data = bestPicture.Data
                };

                // Validate image data before adding
                if (!IsValidImageData(bestPicture.Data.Data, apicFrame.MimeType))
                {
                    Log($"    Warning: Image data appears invalid for MIME type {apicFrame.MimeType}");
                }

                id3v2Dest.AddFrame(apicFrame);
                Log($"    Added APIC frame: {apicFrame.Type}, {apicFrame.MimeType}, {apicFrame.Data.Count} bytes");

                // Also update the main tag pictures (important for compatibility)
                var itunesPicture = new TagLib.Picture(bestPicture.Data.Data)
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = apicFrame.MimeType,
                    Description = "Cover"
                };

                dest.Tag.Pictures = new TagLib.IPicture[] { itunesPicture };
                Log($"    Updated main tag with iTunes-compatible picture");

                return true;
            }
            catch (Exception ex)
            {
                Log($"    Error in iTunes-specific embedding: {ex.Message}");
                return false;
            }
        }

        private string NormalizeMimeType(string originalMimeType)
        {
            if (string.IsNullOrEmpty(originalMimeType))
                return "image/jpeg"; // Default fallback

            var normalized = originalMimeType.ToLower().Trim();

            // Normalize common variations
            switch (normalized)
            {
                case "image/jpg":
                case "image/jpeg":
                case "jpeg":
                case "jpg":
                    return "image/jpeg";

                case "image/png":
                case "png":
                    return "image/png";

                case "image/gif":
                case "gif":
                    return "image/gif";

                case "image/bmp":
                case "bmp":
                    return "image/bmp";

                default:
                    // If it starts with image/, keep as is, otherwise assume jpeg
                    return normalized.StartsWith("image/") ? normalized : "image/jpeg";
            }
        }

        private TagLib.IPicture[] ClonePictureArray(TagLib.IPicture[] originalPictures)
        {
            var clonedPictures = new TagLib.IPicture[originalPictures.Length];
            for (int i = 0; i < originalPictures.Length; i++)
            {
                var original = originalPictures[i];
                clonedPictures[i] = new TagLib.Picture(original.Data.Data)
                {
                    Type = original.Type == TagLib.PictureType.Other ? TagLib.PictureType.FrontCover : original.Type,
                    MimeType = NormalizeMimeType(original.MimeType),
                    Description = string.IsNullOrEmpty(original.Description) ? "Cover" : original.Description
                };
            }
            return clonedPictures;
        }

        private bool IsValidImageData(byte[] imageData, string mimeType)
        {
            if (imageData == null || imageData.Length < 4)
                return false;

            // Check common image file signatures
            if (mimeType?.ToLower().Contains("jpeg") == true)
            {
                return imageData[0] == 0xFF && imageData[1] == 0xD8; // JPEG signature
            }
            else if (mimeType?.ToLower().Contains("png") == true)
            {
                return imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47; // PNG signature
            }
            else if (mimeType?.ToLower().Contains("gif") == true)
            {
                return (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46); // GIF signature
            }
            else if (mimeType?.ToLower().Contains("bmp") == true)
            {
                return imageData[0] == 0x42 && imageData[1] == 0x4D; // BMP signature
            }

            // For unknown MIME types, just check if we have some data
            return imageData.Length > 100; // Assume valid if we have substantial data
        }

        private void VerifyAlbumArt(string filePath, string context)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);
                var pictureCount = file.Tag.Pictures?.Length ?? 0;
                Log($"  {context}: {pictureCount} embedded images");

                if (pictureCount > 0)
                {
                    TagLib.IPicture[]? pictures = file.Tag.Pictures;
                    if (pictures?.Length > 0)
                    {
                        for (int i = 0; i < pictures.Length; i++)
                        {
                            var picture = pictures[i];
                            Log($"    Image {i + 1}: {picture.Type}, {picture.MimeType}, {picture.Data.Count} bytes");

                            // Additional validation - try to read the image data
                            if (picture.Data.Count > 0)
                            {
                                var imageData = picture.Data.Data;
                                if (IsValidImageData(imageData, picture.MimeType))
                                {
                                    Log($"      ✓ Image data appears valid ({GetImageDimensions(imageData, picture.MimeType)})");
                                }
                                else
                                {
                                    Log($"      ⚠ Image data may be corrupted or invalid signature");
                                }
                            }
                        }
                    }
                }

                // Check ID3v2 specific frames for debugging
                if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                {
                    var id3v2 = file.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
                    if (id3v2 != null)
                    {
                        Log($"    ID3v2 version: {id3v2.Version}");
                        var apicFrames = id3v2.GetFrames<TagLib.Id3v2.AttachmentFrame>();
                        if (apicFrames != null)
                        {
                            Log($"    ID3v2 APIC frames: {apicFrames.Count()}");
                            foreach (var frame in apicFrames)
                            {
                                Log($"      APIC: {frame.Type}, {frame.MimeType}, {frame.Description}, {frame.Data.Count} bytes");

                                // Check if this looks like iTunes-compatible format
                                var isItunesCompatible = frame.Type == TagLib.PictureType.FrontCover &&
                                                       !string.IsNullOrEmpty(frame.MimeType) &&
                                                       frame.MimeType.StartsWith("image/");

                                Log($"      iTunes compatible: {(isItunesCompatible ? "✓" : "✗")}");
                            }
                        }
                    }
                }

                // Test iTunes compatibility
                TestItunesCompatibility(filePath);
            }
            catch (Exception ex)
            {
                Log($"  Error verifying album art in {context}: {ex.Message}");
            }
        }

        private string GetImageDimensions(byte[] imageData, string mimeType)
        {
            try
            {
                if (mimeType?.ToLower().Contains("jpeg") == true && imageData.Length > 10)
                {
                    // Basic JPEG dimension extraction (simplified)
                    for (int i = 2; i < imageData.Length - 10; i++)
                    {
                        if (imageData[i] == 0xFF && (imageData[i + 1] == 0xC0 || imageData[i + 1] == 0xC2))
                        {
                            var height = (imageData[i + 5] << 8) | imageData[i + 6];
                            var width = (imageData[i + 7] << 8) | imageData[i + 8];
                            return $"{width}x{height}";
                        }
                    }
                }
                else if (mimeType?.ToLower().Contains("png") == true && imageData.Length > 24)
                {
                    // PNG dimension extraction
                    var width = (imageData[16] << 24) | (imageData[17] << 16) | (imageData[18] << 8) | imageData[19];
                    var height = (imageData[20] << 24) | (imageData[21] << 16) | (imageData[22] << 8) | imageData[23];
                    return $"{width}x{height}";
                }

                return "dimensions unknown";
            }
            catch
            {
                return "dimensions unknown";
            }
        }

        private void TestItunesCompatibility(string filePath)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);

                var issues = new List<string>();

                // Check ID3v2 version
                if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                {
                    var id3v2 = file.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
                    if (id3v2 != null)
                    {
                        if (id3v2.Version != 3 && id3v2.Version != 4)
                        {
                            issues.Add($"ID3v2 version {id3v2.Version} (iTunes prefers v2.3 or v2.4)");
                        }

                        var apicFrames = id3v2.GetFrames<TagLib.Id3v2.AttachmentFrame>();
                        if (!apicFrames.Any())
                        {
                            issues.Add("No APIC frames found");
                        }
                        else
                        {
                            foreach (var frame in apicFrames)
                            {
                                if (frame.Type != TagLib.PictureType.FrontCover)
                                {
                                    issues.Add($"Picture type is {frame.Type} (iTunes prefers FrontCover)");
                                }

                                if (string.IsNullOrEmpty(frame.MimeType) || !frame.MimeType.StartsWith("image/"))
                                {
                                    issues.Add($"Invalid MIME type: '{frame.MimeType}'");
                                }

                                if (frame.Data.Count == 0)
                                {
                                    issues.Add("Empty image data");
                                }
                            }
                        }
                    }
                }
                else
                {
                    issues.Add("No ID3v2 tag found");
                }

                if (issues.Count == 0)
                {
                    Log($"    ✓ iTunes compatibility: No issues detected");
                }
                else
                {
                    Log($"    ⚠ iTunes compatibility issues:");
                    foreach (var issue in issues)
                    {
                        Log($"      - {issue}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"    Error testing iTunes compatibility: {ex.Message}");
            }
        }

        public async Task RunAsync()
        {
            Console.WriteLine("=== MP3 File Manager and Metadata Copier ===\n");
            Console.WriteLine("Manages metatags and updates the specified library with matching (hopefully) higher-quality files, if specified.\n");

            // Run diagnostics first
            RunDiagnostics();

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

        private void RunDiagnostics()
        {
            Console.WriteLine("=== DIAGNOSTICS ===");
            Console.WriteLine($"Current User: {Environment.UserName}");
            Console.WriteLine($"Application Base Directory: {AppContext.BaseDirectory}");
            Console.WriteLine($"Working Directory: {Directory.GetCurrentDirectory()}");
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"Process Elevated: {IsProcessElevated()}");

            // Test write access to common locations
            TestWriteAccess("Current Directory", Directory.GetCurrentDirectory());
            TestWriteAccess("App Base Directory", AppContext.BaseDirectory);
            TestWriteAccess("Temp Directory", Path.GetTempPath());

            if (!string.IsNullOrEmpty(_settings.LogPath))
            {
                var logDir = Path.GetDirectoryName(_settings.LogPath);
                if (!string.IsNullOrEmpty(logDir))
                {
                    TestWriteAccess("Configured Log Directory", logDir);
                }
            }

            Console.WriteLine("==================\n");
        }

        private static bool IsProcessElevated()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    System.Security.Principal.WindowsPrincipal principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(role: System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                // On non-Windows platforms, assume the process is not elevated
                return false;
            }
        }

        private static void TestWriteAccess(string location, string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Console.WriteLine($"{location}: Directory does not exist: {path}");
                    return;
                }

                var testFile = Path.Combine(path, $"write_test_{Guid.NewGuid():N}.tmp");
                System.IO.File.WriteAllText(testFile, "test");
                System.IO.File.Delete(testFile);
                Console.WriteLine($"{location}: Write access OK - {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{location}: Write access FAILED - {path}");
                Console.WriteLine($"  Error: {ex.Message}");
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
            try
            {
                // Handle log path
                if (string.IsNullOrEmpty(_settings.LogPath))
                {
                    _settings.LogPath = Path.Combine(AppContext.BaseDirectory, $"MP3MetadataCopy_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                }
                else
                {
                    // If LogPath is a directory, create filename in that directory
                    if (Directory.Exists(_settings.LogPath) || _settings.LogPath.EndsWith(Path.DirectorySeparatorChar))
                    {
                        _settings.LogPath = Path.Combine(_settings.LogPath, $"MP3MetadataCopy_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    }
                }

                // Ensure log directory exists
                var logDirectory = Path.GetDirectoryName(_settings.LogPath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                    Console.WriteLine($"Created log directory: {logDirectory}");
                }

                // Test write access to log directory
                var testFile = Path.Combine(Path.GetDirectoryName(_settings.LogPath) ?? "", "test_write.tmp");
                try
                {
                    System.IO.File.WriteAllText(testFile, "test");
                    System.IO.File.Delete(testFile);
                    Console.WriteLine($"Log directory write test successful: {Path.GetDirectoryName(_settings.LogPath)}");
                }
                catch (Exception ex)
                {
                    throw new UnauthorizedAccessException($"Cannot write to log directory '{Path.GetDirectoryName(_settings.LogPath)}': {ex.Message}");
                }

                // Set up processed path
                _processedPath = Path.Combine(_settings.HighQualityPath, "Processed_With_Metadata");

                if (!_settings.WhatIfMode)
                {
                    Directory.CreateDirectory(_processedPath);
                    Console.WriteLine($"Created processed directory: {_processedPath}");
                }

                // Initialize log writer with explicit encoding and buffer settings
                _logWriter = new StreamWriter(_settings.LogPath, false, System.Text.Encoding.UTF8, 1024)
                {
                    AutoFlush = true // Ensure immediate writes
                };

                Console.WriteLine($"Log file initialized: {_settings.LogPath}");

                Log($"Start Time: {DateTime.Now}");
                Log($"Application Base Directory: {AppContext.BaseDirectory}");
                Log($"Working Directory: {Directory.GetCurrentDirectory()}");
                Log($"Library Path: {_settings.LibraryPath}");
                Log($"High Quality Path: {_settings.HighQualityPath}");
                Log($"Archive Path: {_settings.ArchivePath}");
                Log($"Log Path: {_settings.LogPath}");
                Log($"Processed Path: {_processedPath}");
                Log($"Replace In Library: {_settings.ReplaceInLibrary}");
                Log($"What If Mode: {_settings.WhatIfMode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing logging: {ex.Message}");
                Console.WriteLine($"Attempted log path: {_settings.LogPath}");
                Console.WriteLine($"Current user: {Environment.UserName}");
                Console.WriteLine($"Working directory: {Directory.GetCurrentDirectory()}");
                throw;
            }
        }

        private async Task ProcessFilesAsync()
        {
            // Create enhanced library index that handles duplicates
            Log("Indexing library files...");
            var (libraryIndex, duplicates) = CreateLibraryIndex();

            Log($"Found {libraryIndex.Values.Sum(list => list.Count)} files in library");
            if (duplicates.Count > 0)
            {
                Log($"Found {duplicates.Count} duplicate filenames in library");
            }

            var highQualityFiles = Directory.GetFiles(_settings.HighQualityPath, "*.mp3", SearchOption.AllDirectories);
            Log($"Found {highQualityFiles.Length} high-quality files to process");

            int processed = 0;
            int matched = 0;
            int errors = 0;
            int skippedDuplicates = 0;
            var processedFiles = new List<(string OriginalHQ, string ProcessedFile, string MatchingLibraryFile)>();

            foreach (var hqFile in highQualityFiles)
            {
                var fileName = Path.GetFileName(hqFile);
                Log($"\nProcessing: {fileName}");

                if (!libraryIndex.ContainsKey(fileName))
                {
                    Log($"Skipping: {fileName} (no matching library file)");
                    continue;
                }

                var matchingFiles = libraryIndex[fileName];

                if (matchingFiles.Count > 1)
                {
                    // Handle multiple matches
                    var selectedFile = HandleMultipleMatches(hqFile, matchingFiles);
                    if (selectedFile == null)
                    {
                        skippedDuplicates++;
                        Log($"Skipped: {fileName} (multiple matches, requires manual resolution)");
                        continue;
                    }
                    matchingFiles = new List<LibraryFileInfo> { selectedFile };
                }

                matched++;
                var sourceFile = matchingFiles[0];
                string destFile = Path.Combine(_processedPath, fileName);

                Log($"Found match: {Path.GetFileName(sourceFile.FullPath)} (Album: {sourceFile.Album}, Artist: {sourceFile.Artist})");

                if (_settings.WhatIfMode)
                {
                    Log($"[WHAT-IF] Would copy metadata from {Path.GetFileName(sourceFile.FullPath)} to {fileName}");

                    // Verify source has album art in what-if mode
                    VerifyAlbumArt(sourceFile.FullPath, "Source file");

                    processed++;
                    continue;
                }

                try
                {
                    // Copy metadata using TagLib
                    using (var source = TagLib.File.Create(sourceFile.FullPath))
                    using (var dest = TagLib.File.Create(hqFile))
                    {
                        var tag = dest.Tag;
                        var srcTag = source.Tag;

                        Log($"  Source file has {srcTag.Pictures?.Length ?? 0} embedded images");

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

                        // Enhanced album art copying - handle multiple tag formats
                        CopyAlbumArt(source, dest);

                        // Copy additional ID3v2 specific tags
                        if (srcTag is TagLib.Id3v2.Tag id3v2src && tag is TagLib.Id3v2.Tag id3v2dest)
                        {
                            id3v2dest.Publisher = id3v2src.Publisher;
                            id3v2dest.Lyrics = id3v2src.Lyrics;

                            // Copy custom frames that might contain additional metadata
                            foreach (var frame in id3v2src.GetFrames())
                            {
                                if (frame.FrameId.StartsWith("TXXX") || frame.FrameId.StartsWith("COMM"))
                                {
                                    try
                                    {
                                        id3v2dest.AddFrame(frame);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log($"    Warning: Could not copy frame {frame.FrameId}: {ex.Message}");
                                    }
                                }
                            }
                        }

                        Log($"  Destination file now has {tag.Pictures?.Length ?? 0} embedded images");
                        dest.Save();
                    }

                    // Copy the updated file to processed directory
                    System.IO.File.Copy(hqFile, destFile, true);

                    // Verify the album art was copied successfully
                    VerifyAlbumArt(destFile, "Processed file");

                    Log($"Successfully processed: {fileName}");
                    processed++;

                    // Track for potential library replacement
                    processedFiles.Add((hqFile, destFile, sourceFile.FullPath));
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

            // Enhanced Summary
            Log("\n" + new string('=', 50));
            Log("SUMMARY");
            Log(new string('=', 50));
            Log($"Total high-quality files: {highQualityFiles.Length}");
            Log($"Files matched: {matched}");
            Log($"Files processed successfully: {processed}");
            Log($"Files skipped (multiple matches): {skippedDuplicates}");
            Log($"Errors: {errors}");
            Log($"Files without matches: {highQualityFiles.Length - matched - skippedDuplicates}");

            if (skippedDuplicates > 0)
            {
                Log($"\nIMPORTANT: {skippedDuplicates} files were skipped due to multiple matches in your library.");
                Log("Check the log above for details and consider manual processing for these files.");
            }

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
                        System.IO.File.Copy(libraryFile, archiveFile, true);
                        Log($"Archived original: {fileName}");
                    }

                    // Replace with processed file
                    System.IO.File.Copy(processedFile, libraryFile, true);
                    Log($"Replaced in library: {fileName}");

                    await Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    Log($"Error replacing {Path.GetFileName(libraryFile)}: {ex.Message}");
                }
            }
        }

        private (Dictionary<string, List<LibraryFileInfo>> libraryIndex, List<string> duplicates) CreateLibraryIndex()
        {
            var libraryIndex = new Dictionary<string, List<LibraryFileInfo>>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            var libraryFiles = Directory.GetFiles(_settings.LibraryPath, "*.mp3", SearchOption.AllDirectories);

            foreach (var filePath in libraryFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(filePath);
                    var relativePath = Path.GetRelativePath(_settings.LibraryPath, filePath);
                    var fileInfo = new FileInfo(filePath);

                    // Extract metadata for smarter matching
                    var libraryFileInfo = new LibraryFileInfo
                    {
                        FullPath = filePath,
                        FileName = fileName,
                        FileSize = fileInfo.Length,
                        RelativePath = relativePath
                    };

                    try
                    {
                        using var tagFile = TagLib.File.Create(filePath);
                        libraryFileInfo.Artist = string.Join(", ", tagFile.Tag.Performers ?? Array.Empty<string>());
                        libraryFileInfo.Album = tagFile.Tag.Album ?? "";
                        libraryFileInfo.Title = tagFile.Tag.Title ?? "";
                        libraryFileInfo.Year = tagFile.Tag.Year;
                    }
                    catch (Exception ex)
                    {
                        Log($"Warning: Could not read metadata from {fileName}: {ex.Message}");
                        // Continue with file info only
                    }

                    if (!libraryIndex.ContainsKey(fileName))
                    {
                        libraryIndex[fileName] = new List<LibraryFileInfo>();
                    }
                    else if (libraryIndex[fileName].Count == 1)
                    {
                        // First time we're seeing a duplicate
                        duplicates.Add(fileName);
                    }

                    libraryIndex[fileName].Add(libraryFileInfo);
                }
                catch (Exception ex)
                {
                    Log($"Error indexing file {filePath}: {ex.Message}");
                }
            }

            return (libraryIndex, duplicates);
        }

        private LibraryFileInfo? HandleMultipleMatches(string hqFile, List<LibraryFileInfo> matchingFiles)
        {
            var fileName = Path.GetFileName(hqFile);

            Log($"Multiple matches found for '{fileName}':");
            for (int i = 0; i < matchingFiles.Count; i++)
            {
                var file = matchingFiles[i];
                Log($"  {i + 1}. {file.RelativePath}");
                Log($"     Artist: {file.Artist}, Album: {file.Album}, Year: {file.Year}");
                Log($"     Size: {file.FileSize:N0} bytes");
            }

            if (_settings.AutoSelectBestMatch)
            {
                // Try to auto-select the best match using high-quality file metadata
                var bestMatch = SelectBestMatch(hqFile, matchingFiles);
                if (bestMatch != null)
                {
                    Log($"Auto-selected best match: {bestMatch.RelativePath}");
                    return bestMatch;
                }
            }

            if (_settings.PromptForDuplicates && !_settings.WhatIfMode)
            {
                return PromptUserForSelection(matchingFiles);
            }

            // Default: skip file and log for manual resolution
            Log($"Multiple matches require manual resolution. Skipping '{fileName}'.");
            Log("Consider using AutoSelectBestMatch=true or PromptForDuplicates=true in configuration.");

            return null;
        }

        private LibraryFileInfo? SelectBestMatch(string hqFile, List<LibraryFileInfo> candidates)
        {
            try
            {
                using var hqTagFile = TagLib.File.Create(hqFile);
                var hqTag = hqTagFile.Tag;

                var hqArtist = string.Join(", ", hqTag.Performers ?? Array.Empty<string>());
                var hqAlbum = hqTag.Album ?? "";
                var hqTitle = hqTag.Title ?? "";
                var hqYear = hqTag.Year;

                Log($"HQ file metadata - Artist: '{hqArtist}', Album: '{hqAlbum}', Title: '{hqTitle}', Year: {hqYear}");

                var bestMatch = candidates
                    .Select(candidate => new
                    {
                        File = candidate,
                        Score = CalculateMatchScore(hqArtist, hqAlbum, hqTitle, hqYear, candidate)
                    })
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                if (bestMatch != null && bestMatch.Score > 0.5) // Require at least 50% match
                {
                    Log($"Best match score: {bestMatch.Score:F2} for {bestMatch.File.RelativePath}");
                    return bestMatch.File;
                }
            }
            catch (Exception ex)
            {
                Log($"Error analyzing HQ file metadata: {ex.Message}");
            }

            return null;
        }

        private double CalculateMatchScore(string hqArtist, string hqAlbum, string hqTitle, uint hqYear, LibraryFileInfo candidate)
        {
            double score = 0;
            int factors = 0;

            // Artist match (weighted heavily)
            if (!string.IsNullOrEmpty(hqArtist) && !string.IsNullOrEmpty(candidate.Artist))
            {
                score += GetStringSimilarity(hqArtist, candidate.Artist) * 0.4;
                factors++;
            }

            // Album match (weighted heavily)  
            if (!string.IsNullOrEmpty(hqAlbum) && !string.IsNullOrEmpty(candidate.Album))
            {
                score += GetStringSimilarity(hqAlbum, candidate.Album) * 0.3;
                factors++;
            }

            // Title match
            if (!string.IsNullOrEmpty(hqTitle) && !string.IsNullOrEmpty(candidate.Title))
            {
                score += GetStringSimilarity(hqTitle, candidate.Title) * 0.2;
                factors++;
            }

            // Year match
            if (hqYear > 0 && candidate.Year > 0)
            {
                score += (hqYear == candidate.Year ? 1.0 : 0.0) * 0.1;
                factors++;
            }

            return factors > 0 ? score : 0;
        }

        private double GetStringSimilarity(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
                return 0;

            str1 = str1.ToLowerInvariant().Trim();
            str2 = str2.ToLowerInvariant().Trim();

            if (str1 == str2) return 1.0;

            // Simple similarity check - could be enhanced with Levenshtein distance
            var longer = str1.Length > str2.Length ? str1 : str2;
            var shorter = str1.Length > str2.Length ? str2 : str1;

            if (longer.Contains(shorter)) return 0.8;
            if (shorter.Contains(longer)) return 0.8;

            // Count common words
            var words1 = str1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words2 = str2.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var commonWords = words1.Intersect(words2).Count();
            var totalWords = Math.Max(words1.Length, words2.Length);

            return totalWords > 0 ? (double)commonWords / totalWords : 0;
        }

        private LibraryFileInfo? PromptUserForSelection(List<LibraryFileInfo> matchingFiles)
        {
            Console.WriteLine($"\nMultiple matches found. Please select:");
            for (int i = 0; i < matchingFiles.Count; i++)
            {
                var file = matchingFiles[i];
                Console.WriteLine($"  {i + 1}. {file.RelativePath}");
                Console.WriteLine($"     Artist: {file.Artist}, Album: {file.Album}, Year: {file.Year}");
            }
            Console.WriteLine($"  {matchingFiles.Count + 1}. Skip this file");

            while (true)
            {
                Console.Write($"Enter choice (1-{matchingFiles.Count + 1}): ");
                if (int.TryParse(Console.ReadLine(), out int choice))
                {
                    if (choice >= 1 && choice <= matchingFiles.Count)
                    {
                        return matchingFiles[choice - 1];
                    }
                    else if (choice == matchingFiles.Count + 1)
                    {
                        return null; // Skip
                    }
                }
                Console.WriteLine("Invalid choice. Please try again.");
            }
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