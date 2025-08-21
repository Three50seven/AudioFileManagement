namespace AudioFileMetadataProcessor.Domain
{
    public class MusicBrainzMedia
    {
        public string? Format { get; set; }
        public MusicBrainzTrack[]? Track { get; set; }
    }
}
