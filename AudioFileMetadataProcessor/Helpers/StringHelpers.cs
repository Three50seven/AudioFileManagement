using System.Globalization;
using System.Text.RegularExpressions;

namespace AudioFileMetadataProcessor.Helpers
{
    public class StringHelpers
    {
        /// <summary>
        /// Converts a string to title case while preserving certain exceptions like and, the, or etc. when they are in the middle of a word.
        /// ref: https://apastyle.apa.org/style-grammar-guidelines/capitalization/title-case
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public static string ToTitleCaseWithExceptions(string title)
        {
            string[] exceptions = ["the", "a", "an", "and", "as", "but", "for",
                "if", "nor", "or", "so", "yet", "as", "at", "by", "for", "in", "of",
                "off", "on", "per", "to", "up", "via"];

            if (string.IsNullOrWhiteSpace(title))
                return title;

            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            string[] words = title.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (i == 0 || i == words.Length - 1 || !exceptions.Contains(word))
                {
                    words[i] = textInfo.ToTitleCase(word);
                }
                else
                {
                    words[i] = word;
                }
            }

            return string.Join(' ', words);
        }
    }
}
