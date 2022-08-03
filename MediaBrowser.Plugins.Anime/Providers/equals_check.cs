using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MediaBrowser.Model.Extensions;

namespace Emby.Anime
{
    internal static class Equals_check
    {
        private static string ReplaceSafe(this string str, string find, string replace)
        {
            return str.ReplaceSafe(find, replace, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReplaceSafe(this string str, string find, string replace, StringComparison comparison)
        {
            if (string.IsNullOrEmpty(find))
            {
                return str;
            }

            return str.Replace(find, replace, comparison);
        }

        /// <summary>
        /// Clear name
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public static string Clear_name(string a)
        {
            a = a.Trim().ReplaceSafe(One_line_regex(new Regex(@"(?s) \(.*?\)"), a.Trim(), 0), "", StringComparison.OrdinalIgnoreCase);

            a = a.Replace(".", " ", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("-", " ", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("`", "", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("'", "", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("&", "and", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("(", "", StringComparison.OrdinalIgnoreCase);
            a = a.Replace(")", "", StringComparison.OrdinalIgnoreCase);

            a = a.ReplaceSafe(One_line_regex(new Regex(@"(?s)(S[0-9]+)"), a.Trim()), One_line_regex(new Regex(@"(?s)S([0-9]+)"), a.Trim()), StringComparison.OrdinalIgnoreCase);

            return a;
        }
        public static string Half_string(string string_, int min_lenght = 0, int p = 50)
        {
            decimal length = 0;
            if (((int)((decimal)string_.Length - (((decimal)string_.Length / 100m) * (decimal)p)) > min_lenght))
            {
                length = (decimal)string_.Length - (((decimal)string_.Length / 100m) * (decimal)p);
            }
            else
            {
                if (string_.Length < min_lenght)
                {
                    length = string_.Length;
                }
                else
                {
                    length = min_lenght;
                }
            }
            return string_.Substring(0, (int)length);
        }

        /// <summary>
        /// Clear name heavy.
        /// Example: Text & Text to Text and Text
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public static string Clear_name_step2(string a)
        {
            if (a.Contains("Gekijyouban"))
                a = (a.Replace("Gekijyouban", "", StringComparison.OrdinalIgnoreCase) + " Movie").Trim();
            if (a.Contains("gekijyouban"))
                a = (a.Replace("gekijyouban", "", StringComparison.OrdinalIgnoreCase) + " Movie").Trim();

            a = a.Trim().ReplaceSafe(One_line_regex(new Regex(@"(?s) \(.*?\)"), a.Trim(), 0), "", StringComparison.OrdinalIgnoreCase);

            a = a.Replace(".", " ", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("-", " ", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("`", "", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("'", "", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("&", "and", StringComparison.OrdinalIgnoreCase);
            a = a.Replace(":", "", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("␣", "", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("2wei", "zwei", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("3rei", "drei", StringComparison.OrdinalIgnoreCase);
            a = a.Replace("4ier", "vier", StringComparison.OrdinalIgnoreCase);
            return a;
        }

        /// <summary>
        /// If a and b match it return true
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool Compare_strings(string a, string b)
        {
            if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b))
            {
                if (Simple_compare(a, b))
                    return true;

                return false;
            }
            return false;
        }

        /// <summary>
        /// simple regex
        /// </summary>
        /// <param name="regex"></param>
        /// <param name="match"></param>
        /// <param name="group"></param>
        /// <param name="match_int"></param>
        /// <returns></returns>
        public static string One_line_regex(Regex regex, string match, int group = 1, int match_int = 0)
        {
            int x = 0;
            var matches = regex.Matches(match);

            foreach (Match _match in matches)
            {
                if (x > match_int)
                {
                    break;
                }
                if (x == match_int)
                {
                    return _match.Groups[group].Value.ToString();
                }
                x++;
            }
            return "";
        }

        /// <summary>
        /// Compare 2 Strings, and it just works
        /// SeriesA S2 == SeriesA Second Season | True;
        /// </summary>
        public static bool Simple_compare(string a, string b, bool fastmode = false)
        {
            if (fastmode)
            {
                if (a[0] == b[0])
                {
                }
                else
                {
                    return false;
                }
            }

            if (Core_compare(a, b))
                return true;
            if (Core_compare(b, a))
                return true;

            return false;
        }

        /// <summary>
        /// Compare 2 Strings, and it just works
        /// </summary>
        private static bool Core_compare(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            a = a.ToLower().Replace(" ", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(".", "", StringComparison.OrdinalIgnoreCase);
            b = b.ToLower().Replace(" ", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(".", "", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(Clear_name(a), Clear_name(b), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(Clear_name_step2(a), Clear_name_step2(b), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace("-", " ", StringComparison.OrdinalIgnoreCase), b.Replace("-", " ", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace(" 2", ":secondseason", StringComparison.OrdinalIgnoreCase), b.Replace(" 2", ":secondseason", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace("2", "secondseason", StringComparison.OrdinalIgnoreCase), b.Replace("2", "secondseason", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(Convert_symbols_too_numbers(a, "I"), Convert_symbols_too_numbers(b, "I"), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(Convert_symbols_too_numbers(a, "!"), Convert_symbols_too_numbers(b, "!"), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace("ndseason", "", StringComparison.OrdinalIgnoreCase), b.Replace("ndseason", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace("ndseason", "", StringComparison.OrdinalIgnoreCase), b, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(One_line_regex(new Regex(@"((.*)s([0 - 9]))"), a, 2) + One_line_regex(new Regex(@"((.*)s([0 - 9]))"), a, 3), One_line_regex(new Regex(@"((.*)s([0 - 9]))"), b, 2) + One_line_regex(new Regex(@"((.*)s([0 - 9]))"), b, 3), StringComparison.OrdinalIgnoreCase))
                if (!string.IsNullOrEmpty(One_line_regex(new Regex(@"((.*)s([0 - 9]))"), a, 2) + One_line_regex(new Regex(@"((.*)s([0 - 9]))"), a, 3)))
                    return true;
            if (string.Equals(One_line_regex(new Regex(@"((.*)s([0 - 9]))"), a, 2) + One_line_regex(new Regex(@"((.*)s([0 - 9]))"), a, 3), b, StringComparison.OrdinalIgnoreCase))
                if (!string.IsNullOrEmpty(One_line_regex(new Regex(@"((.*)s([0 - 9]))"), a, 2) + One_line_regex(new Regex(@"((.*)s([0 - 9]))"), a, 3)))
                    return true;
            if (string.Equals(a.Replace("rdseason", "", StringComparison.OrdinalIgnoreCase), b.Replace("rdseason", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace("rdseason", "", StringComparison.OrdinalIgnoreCase), b, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace("2", "secondseason").ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), a, 0), ""), b.Replace("2", "secondseason").ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), b, 0), ""), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace("2", "secondseason").ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), a, 0), ""), b, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace(" 2", ":secondseason").ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), a, 0), ""), b.Replace(" 2", ":secondseason").ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), b, 0), ""), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace(" 2", ":secondseason").ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), a, 0), ""), b, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), a, 0), ""), b.ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), b, 0), ""), StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), a, 0), ""), b, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(b.ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), b, 0), "").Replace("  2", ": second Season"), a, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.Replace(" 2ndseason", ":secondseason", StringComparison.OrdinalIgnoreCase) + " vs " + b, a, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(a.ReplaceSafe(One_line_regex(new Regex(@"(?s)\(.*?\)"), a, 0), "").Replace("  2", ":secondseason"), b, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>
        /// Example: Convert II to 2
        /// </summary>
        /// <param name="input"></param>
        /// <param name="symbol"></param>
        /// <returns></returns>
        private static string Convert_symbols_too_numbers(string input, string symbol)
        {
            string regex_c = "_";
            int x = 0;
            int highest_number = 0;
            while (!string.IsNullOrEmpty(regex_c) && x < 100)
            {
                regex_c = (One_line_regex(new Regex(@"(" + symbol + @"+)"), input.ToLower().Trim(), 1, x)).Trim();
                if (highest_number < regex_c.Count())
                    highest_number = regex_c.Count();
                x++;
            }
            x = 0;
            string output = "";
            while (x < highest_number)
            {
                output = output + symbol;
                x++;
            }
            output = input.ReplaceSafe(output, highest_number.ToString(), StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(output))
            {
                output = input;
            }
            return output;
        }
    }
}