# MP3FileManager
**MP3FileManager** - manages metatags and updates the specified library with matching (hopefully) higher-quality files, if specified.

I primarily wrote MP3FileManager for replacing lower-quality mp3 files with higher-quality mp3 files.  The problem was that the lower-quality files already had proper metadata, and the high-quality files had better quality, but very little metadata.  So this app allows me to point to my music library and essentially copy the metadata for matching files, and replace the lower-quality file in the library with the higher-quality file.

## Fixing Corrupt Files
If you get warnings when reading the mp3 file library, you may need to re-encode the file.  It's optional to re-encode since the warning will just be ignored, but most likely, since they're in the library, you will probably want to clean them up by re-encoding.

Here's a way to do it with ffmpeg:
```
ffmpeg -i "badfile.mp3" -map_metadata 0 -c copy "cleanfile.mp3"
```
Some of the warnings you may encounter are:
- Warning: Could not read metadata from SongTitle.mp3: Text delimiter expected. 
- Warning: Could not read metadata from SongTitle2.mp3: Not enough bytes in field.

## Tips
Before updating your library, run the AudioFileMetadataProcessor app first to generate MP3 and metadata, then run this app (MP3FileManager) to update your library with the higher-quality files.