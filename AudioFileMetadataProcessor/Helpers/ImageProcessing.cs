using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AudioFileMetadataProcessor.Helpers
{
    /// <summary>
    /// Image resizing / compression helpers extracted from Program.cs
    /// </summary>
    public static class ImageProcessing
    {
        /// <summary>
        /// Compresses and resizes image bytes. Attempts to output JPEG at specified quality.
        /// Falls back to original bytes on error.
        /// </summary>
        public static byte[] CompressImage(byte[] inputBytes, int maxDimension, int quality)
        {
            try
            {
                using var inStream = new MemoryStream(inputBytes);
                using var original = Image.FromStream(inStream, useEmbeddedColorManagement: true, validateImageData: true);

                int width = original.Width;
                int height = original.Height;

                // Determine new size while preserving aspect ratio
                int newWidth = width;
                int newHeight = height;
                if (width > maxDimension || height > maxDimension)
                {
                    double ratio = Math.Min((double)maxDimension / width, (double)maxDimension / height);
                    newWidth = Math.Max(1, (int)Math.Round(width * ratio));
                    newHeight = Math.Max(1, (int)Math.Round(height * ratio));
                }

                using var bitmap = new Bitmap(newWidth, newHeight);
                bitmap.SetResolution(original.HorizontalResolution, original.VerticalResolution);

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    var destRect = new Rectangle(0, 0, newWidth, newHeight);
                    g.DrawImage(original, destRect, 0, 0, width, height, GraphicsUnit.Pixel);
                }

                // Get JPEG encoder
                var jpegEncoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var outStream = new MemoryStream();
                if (jpegEncoder != null)
                {
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 10, 100));
                    bitmap.Save(outStream, jpegEncoder, encoderParams);
                }
                else
                {
                    // No JPEG encoder found — fallback to PNG
                    bitmap.Save(outStream, ImageFormat.Png);
                }

                return outStream.ToArray();
            }
            catch
            {
                // On any error, return original bytes
                return inputBytes;
            }
        }

        /// <summary>
        /// Detect limited image MIME types from the byte header.
        /// </summary>
        public static string GetImageMimeType(byte[] imageBytes)
        {
            if (imageBytes.Length >= 4)
            {
                // JPEG
                if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
                    return "image/jpeg";

                // PNG
                if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 &&
                    imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                    return "image/png";
            }

            return "image/jpeg"; // Default fallback
        }
    }
}