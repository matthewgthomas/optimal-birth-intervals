using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Optimal_Birth_Intervals
{
    /// <summary>
    /// Code modified from http://stackoverflow.com/questions/343654/c-data-structure-to-mimic-cs-listlistint
    /// </summary>
    /// <typeparam name="T"></typeparam>
    static class PowerSet //<T>
    {
        static public IEnumerable<HashSet<T>> powerset<T>(this T[] currentGroupList)
        {
            int count = currentGroupList.Length;
            Dictionary<int, T> powerToIndex = new Dictionary<int, T>();
            int mask = 1;
            for (int i = 0; i < count; i++)
            {
                powerToIndex[mask] = currentGroupList[i];
                mask <<= 1;
            }

            Dictionary<int, T> result = new Dictionary<int, T>();
            //yield return result.Values.ToArray();
            yield return new HashSet<T>(result.Values);

            int max = 1 << count;
            for (int i = 1; i < max; i++)
            {
                int key = i & -i;
                if (result.ContainsKey(key))
                    result.Remove(key);
                else
                    result[key] = powerToIndex[key];
                //yield return result.Values.ToArray();
                yield return new HashSet<T>(result.Values);
            }
        }

    }
}
