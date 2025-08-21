namespace AudioFileMetadataProcessor.Domain
{
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
}
