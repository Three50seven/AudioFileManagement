namespace AudioFileMetadataProcessor.Domain
{
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
}
