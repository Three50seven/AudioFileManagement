using System.Globalization;
using System.Text.RegularExpressions;

namespace AudioFileMetadataProcessor.Helpers
{
    public class StringHelpers
    {
        /// <summary>
        /// Converts a string to title case while preserving certain exceptions like and, the, or etc. when they are in the middle of a word.
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public static string ToTitleCaseWithExceptions(string title)
        {
            // Define the list of words to lowercase
            string[] exceptions = ["the", "a", "an", "and", "or", "but", "nor", "on", "at", "to", "from", "by"];

            // Use ToTitleCase to capitalize each word
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            string titleCased = textInfo.ToTitleCase(title.ToLower());

            // Create a regex pattern to match the exceptions
            string pattern = "\\b(" + string.Join("|", exceptions) + ")\\b";
            Regex regex = new(pattern, RegexOptions.IgnoreCase);

            // Replace the exceptions with their lowercase versions
            titleCased = regex.Replace(titleCased, match => match.Value.ToLower());

            return titleCased;
        }
    }
}
