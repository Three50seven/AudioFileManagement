using AudioFileMetadataProcessor.Domain;
using NAudio.Lame;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace AudioFileMetadataProcessor
{
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

                string fileName = Path.GetFileNameWithoutExtension(inputPath);
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

        private static async Task<bool> ConvertToMp3(string inputPath, string outputPath, string? quality)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var reader = CreateAudioReader(inputPath);
                    if (reader == null) return false;

                    // Replace the MP3 quality settings block in ConvertToMp3 with the following:
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
                            }
                        }
                    }

                    // Convert to 16-bit for MP3 encoding
                    var resampler = new WaveFormatConversionStream(
                        new WaveFormat(reader.WaveFormat.SampleRate, 16, reader.WaveFormat.Channels),
                        reader);

                    if (bitRate.HasValue)
                    {
                        Logger.Log($"  Converting to MP3 using quality bitrate: {bitRate}kbps");
                        using var writer = new LameMP3FileWriter(outputPath, resampler.WaveFormat, bitRate.Value, null);
                        resampler.CopyTo(writer);
                    }
                    else
                    {
                        Logger.Log($"  Converting to MP3 using LAME Preset: {preset}");
                        using var writer = new LameMP3FileWriter(outputPath, resampler.WaveFormat, preset, null);
                        resampler.CopyTo(writer);
                    }

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
}