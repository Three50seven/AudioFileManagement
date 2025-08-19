# AudioFileMetadataProcessor
**AudioFileMetadataProcessor** - a command-line tool for tagging audio files with metadata from MusicBrainz, using a simple CSV format for seeders. It supports batch processing, audio conversion, and high-quality artwork embedding.

## Features:
-  Seeder-Based Metadata Tagging: Uses a CSV file to define artists, titles, albums, and filenames for audio files.
-  CSV Matching - Exact, partial, and fuzzy filename matching
-  Configuration-Driven - All settings in appsettings files
-  Rate Limiting - Respects MusicBrainz API guidelines
-  High-Quality Artwork - Downloads and embeds album art
-  Audio Conversion - MP3 standardized to 44.1kHz stereo
-  Batch Processing - Handles entire directories
-  Git-Safe - Sensitive config in local files

## CSV Seeders File Format:
Simple Format:
```
csvartist,title,album,filename
"The Beatles","Hey Jude","The Beatles 1967-1970",""
"Queen","Bohemian Rhapsody","A Night at the Opera","queen_bohemian"
"Led Zeppelin","Stairway to Heaven","Led Zeppelin IV","stairway"
```

Advanced Format with Track Numbers:
```
csvartist,title,album,filename
"Pink Floyd","Speak to Me","The Dark Side of the Moon","01-speak-to-me"
"Pink Floyd","Breathe","The Dark Side of the Moon","02-breathe"
"Pink Floyd","On the Run","The Dark Side of the Moon","03-on-the-run"
```
## Usage Examples:
Batch Processing with CSV:
```
# Convert and tag entire collection
AudioMetadataTagger.exe "C:\Music\Collection" -seeders-file "metadata.csv" -convert mp3 -quality 320
```
# FLAC conversion with custom output
```
AudioMetadataTagger.exe "C:\Albums" -seeders-file "tracklist.csv" -convert flac -output "C:\Converted" -preserve-original
```
Single File Processing:
```
# Tag single file with manual seeders
AudioMetadataTagger.exe "song.wav" -artist "The Beatles" -title "Yesterday" -convert mp3

# Tag with album info
AudioMetadataTagger.exe "track.flac" -artist "Pink Floyd" -title "Money" -album "The Dark Side of the Moon"
```