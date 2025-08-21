namespace AudioFileMetadataProcessor.Domain
{
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
}
