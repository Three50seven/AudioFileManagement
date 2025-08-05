string _logPath = "";
bool _replaceInLibrary = false;
string _archivePath = "";
string _libraryPath = "";
string _highQualityPath = "";
string _processedPath = "";
StreamWriter? _logWriter = null;

Console.WriteLine("=== MP3 File Manager and Metadata Copier ===\n");
Console.WriteLine("Manages metatags and updates the specified library with matching (hopefully) higher-quality files, if specified.\n\n");

// Get paths interactively
Console.Write("Enter path to MP3 file metadata library path: ");
_libraryPath = (Console.ReadLine()?.Trim('"') ?? "");

Console.Write("Enter path to High-Quality MP3 folder: ");
_highQualityPath = (Console.ReadLine()?.Trim('"') ?? "");

Console.Write("Enter path for log output (leave blank for default): ");
_logPath = (Console.ReadLine()?.Trim('"') ?? "");

Console.Write("Do you want to replace the files in library after processing? (y/N): ");
_replaceInLibrary = (Console.ReadLine()?.Trim() ?? "").Equals("y", StringComparison.CurrentCultureIgnoreCase);

if (_replaceInLibrary)
{
    Console.Write("Enter Archive Path for originals (required): ");
    _archivePath = (Console.ReadLine()?.Trim('"') ?? "");

    Console.WriteLine("\n-- WARNING: You chose to replace files in the library. --");
    Console.WriteLine($"Files will be backed up to: {_archivePath}");
    Console.WriteLine("Check the files in the 'Processed_With_Metadata' folder BEFORE continuing.\n");
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
}

if (string.IsNullOrWhiteSpace(_logPath))
{
    _logPath = Path.Combine(AppContext.BaseDirectory, $"MP3MetadataCopy_{DateTime.Now:yyyyMMdd_HHmmss}.log");
}

_processedPath = Path.Combine(_highQualityPath, "Processed_With_Metadata");
Directory.CreateDirectory(_processedPath);

_logWriter = new StreamWriter(_logPath, false);
Log($"Start Time: {DateTime.Now}");

// Map library by file name only
var libraryFilesDirectory = Directory.GetFiles(_libraryPath, "*.mp3", SearchOption.AllDirectories)
    .ToDictionary(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

int processed = 0;
foreach (var hqFile in Directory.GetFiles(_highQualityPath, "*.mp3", SearchOption.AllDirectories))
{
    var fileName = Path.GetFileName(hqFile);
    if (!libraryFilesDirectory.ContainsKey(fileName))
    {
        Log($"Skipping: {fileName} (no matching library file)");
        continue;
    }

    string sourceFile = libraryFilesDirectory[fileName];
    string destFile = Path.Combine(_processedPath, fileName);

    try
    {
        using (var source = TagLib.File.Create(sourceFile))
        using (var dest = TagLib.File.Create(hqFile))
        {
            var tag = dest.Tag;
            var srcTag = source.Tag;

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

            if (srcTag is TagLib.Id3v2.Tag id3v2src && tag is TagLib.Id3v2.Tag id3v2dest)
            {
                id3v2dest.Publisher = id3v2src.Publisher;
                id3v2dest.Lyrics = id3v2src.Lyrics;

                // Copy album art
                id3v2dest.Pictures = id3v2src.Pictures;
            }

            dest.Save();
        }

        System.IO.File.Copy(hqFile, destFile, true);
        Log($"Processed: {fileName}");
        processed++;
    }
    catch (Exception ex)
    {
        Log($"Error processing {fileName}: {ex.Message}");
    }
}

Log($"\nTotal processed: {processed}");

if (_replaceInLibrary)
{
    Directory.CreateDirectory(_archivePath);
    foreach (var file in Directory.GetFiles(_processedPath, "*.mp3"))
    {
        var target = Path.Combine(_libraryPath, Path.GetFileName(file));
        var archiveFile = Path.Combine(_archivePath, Path.GetFileName(file));
        if (System.IO.File.Exists(target))
        {
            System.IO.File.Copy(target, archiveFile, true);
            System.IO.File.Copy(file, target, true);
            Log($"Replaced and archived: {Path.GetFileName(file)}");
        }
    }
}

_logWriter.Close();
Console.WriteLine($"\nDone. Log file written to: {_logPath}");
Console.WriteLine("Check Processed_With_Metadata folder for results.");

void Log(string message)
{
    Console.WriteLine(message);
    _logWriter.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message);
}
