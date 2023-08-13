using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediaBrowser.Plugins.Anime
{
    public static class StringCompatibility
    {
#if NETSTANDARD2_0
        public static bool Contains(this string val, char srch, StringComparison stringComparison)
        {
            return val.Contains(srch);
        }
        public static bool Contains(this string val, string srch, StringComparison stringComparison)
        {
            return val.IndexOf(srch, stringComparison) != -1;
        }
        public static bool StartsWith(this string val, char srch)
        {
            return val.IndexOf(srch) == 0;
        }
        public static int IndexOf(this string val, char srch, StringComparison stringComparison)
        {
            return val.IndexOf(srch);
        }        public static string Replace(this string str, string oldValue, string newValue, StringComparison comparison)
        {
            var sb = new StringBuilder();

            var previousIndex = 0;
            var index = str.IndexOf(oldValue, comparison);

            while (index != -1)
            {
                sb.Append(str.Substring(previousIndex, index - previousIndex));
                sb.Append(newValue);
                index += oldValue.Length;

                previousIndex = index;
                index = str.IndexOf(oldValue, index, comparison);
            }

            sb.Append(str.Substring(previousIndex));

            return sb.ToString();
        }
#endif        

    }
}
