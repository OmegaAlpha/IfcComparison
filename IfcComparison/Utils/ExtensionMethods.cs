using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace IfcComparison.Utils
{
    public static class ExtensionMethods
    {
        public static string RemoveWhiteSpaces(this string input)
        {
            return string.IsNullOrEmpty(input) ? input : Regex.Replace(input, @"\s+", "");
        }

        public static void SortAlphanumeric(this List<string> list)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            list.Sort((a, b) =>
            {
                string pattern = @"(\d+)|([^\d]+)";
                var regex = new Regex(pattern);
                var matchesA = regex.Matches(a);
                var matchesB = regex.Matches(b);

                for (int i = 0; i < Math.Max(matchesA.Count, matchesB.Count); i++)
                {
                    if (i >= matchesA.Count)
                    {
                        return -1;
                    }
                    if (i >= matchesB.Count)
                    {
                        return 1;
                    }

                    int numA, numB;
                    bool isNumA = int.TryParse(matchesA[i].Value, out numA);
                    bool isNumB = int.TryParse(matchesB[i].Value, out numB);

                    if (isNumA && isNumB)
                    {
                        if (numA != numB)
                        {
                            return numA.CompareTo(numB);
                        }
                    }
                    else
                    {
                        int cmp = string.Compare(matchesA[i].Value, matchesB[i].Value, StringComparison.OrdinalIgnoreCase);
                        if (cmp != 0)
                        {
                            return cmp;
                        }
                    }
                }

                return 0;
            });
        }
    }
}
