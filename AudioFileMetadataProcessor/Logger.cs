using Serilog;

namespace AudioFileMetadataProcessor
{
    public class Logger
    {
        private static Logger? _instance;
        private static readonly object _lock = new();
        private readonly string _resolvedLogPath;

        private Logger(string logDirectory)
        {
            var basePath = Path.Combine(logDirectory, "log_.log");
            _resolvedLogPath = Path.Combine(logDirectory, $"log_{DateTime.Now:yyyyMMdd}.log");

            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(basePath,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Serilog.Log.Information("Logger initialized. Log file: {LogFilePath}", _resolvedLogPath);
        }

        public static Logger Initialize(string logDirectory)
        {
            lock (_lock)
            {
                _instance ??= new Logger(logDirectory);
                return _instance;
            }
        }

        public static void Log(string message) =>
            Serilog.Log.Information(message);

        public string LogFilePath => _resolvedLogPath;

        public static void ShowUsage()
        {
            Log("Usage:");
            Log("  AudioFileMetadataProcessor.exe [input_path] [options]\n");
            Log("Note: If no input_path is provided, the program will use AppSettings:InputPath from appsettings.json\n");
            Log("Options:");
            Log("  -convert <format>     Convert to specified format (mp3, wav, m4a)");
            Log("  -quality <value>      Audio quality for conversion:");
            Log("                        MP3: 0-9 (0=best, 9=worst) or bitrate (128, 192, 320)");
            Log("                        M4A: bitrate (128, 192, 256, 320)");
            Log("  -output <directory>   Output directory for converted files");
            Log("                        (Uses AppSettings:OutputPath from config if not specified)");
            Log("  -preserve-original    Keep original files when converting");
            Log("  -seeders-file <path>  CSV file with seeder data");
            Log("                        (Uses AppSettings:SeedersFileCSVFullPath from config if not specified)\n");
            Log("NAudio Features:");
            Log("  ✓ No external dependencies (FFmpeg not required)");
            Log("  ✓ Faster conversion with native .NET libraries");
            Log("  ✓ Better error handling and stability");
            Log("  ✓ Support for MP3, WAV, M4A formats\n");
            Log("Examples:");
            Log("  AudioFileMetadataProcessor.exe (uses config paths)");
            Log("  AudioFileMetadataProcessor.exe \"C:\\Music\" -convert mp3");
            Log("  AudioFileMetadataProcessor.exe -convert mp3 -quality 5");
        }
    }
}
