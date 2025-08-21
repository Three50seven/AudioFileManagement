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
```
artist,albumartist,title,album,year,tracknumber,discnumber,genre,filename
Bullet for my Valentine,Bullet for my Valentine,Pretty on the Outside,Fever,2010,11/11,1/1,metal,Track01
Bullet for my Valentine,Bullet for my Valentine,Begging for mercy,Fever,2010,10/11,1/1,metal,Track02
Bullet for my Valentine,Bullet for my Valentine,the last fight,fever,2010,3/11,1/1,metal,Track03
Kings of Leon,Kings of Leon,back down south,Come Around Sundown,2010,7/16,1/1,alternative,Track04
Kings of Leon,Kings of Leon,molly's chambers,Youth and Young Manhood,2003,8/12,1/1,alternative,Track05
Kings of Leon,Kings of Leon,find me,WALLS,2016,4/10,1/1,alternative,Track06
```
## Usage Examples:
Batch Processing with CSV:
```
# Convert and tag entire collection
AudioFileMetadataProcessor.exe "C:\Music\Collection" -seeders-file "metadata.csv" -convert mp3 -quality 320
```
# MP3 conversion with custom output
```
AudioFileMetadataProcessor.exe "C:\Albums" -seeders-file "tracklist.csv" -convert mp3 -output "C:\Converted" -preserve-original
```
Single File Processing:
```
# Tag single file with manual seeders
AudioFileMetadataProcessor.exe "song.wav" -artist "The Beatles" -title "Yesterday" -convert mp3

# Tag with album info
AudioFileMetadataProcessor.exe "track.mp3" -artist "Pink Floyd" -title "Money" -album "The Dark Side of the Moon"
```