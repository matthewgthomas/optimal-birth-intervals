/*
 * Code for "A Dynamic Framework for the Study of Optimal Birth Intervals Reveals the Importance of Sibling Competition and Mortality Risks"
 * Copyright (C) 2015 Matthew Gwynfryn Thomas (matthewgthomas.co.uk)
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *  
 * You should have received a copy of the GNU General Public License along
 * with this program; if not, write to the Free Software Foundation, Inc.,
 * 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
 */

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
