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
using System.IO;
using System.Diagnostics;
using System.Configuration;

namespace Optimal_Birth_Intervals
{
    partial class BirthIntervals
    {
        public void runBackward()
        {
            bool birthDecision = false;
            bool converged = false;
            double normaliser = 1.0;

            // variables for capturing rate of increase in fitness
            double r = 0.0;
            double rPrev = 0.0;

            int i = 0;
            int motherAge = ageAtMaturity;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // main loop
            do
            {
                motherAge = ageAtMaturity;

                // loop over each permutation of mother's age and family index to find max. fitness
                for (int arrayIndex = 0; arrayIndex < F1.Length; arrayIndex++)
                {
                    for (int familyIndex = 0; familyIndex < numFamilies; familyIndex++)
                    {
                        if (StateSpace[arrayIndex, familyIndex])
                        {
                            F2[arrayIndex][familyIndex] = findFMax(motherAge, familyIndex, arrayIndex, ref birthDecision);
                            births[arrayIndex][familyIndex] = birthDecision;

                            // normalise the array
                            if (arrayIndex == 0 && familyIndex == 0)  // only set normaliser (denominator) in first iteration
                                normaliser = F2[0][0];  // F2[0][0] is fitness when mother's age is 15 for first family structure

                            F2[arrayIndex][familyIndex] = F2[arrayIndex][familyIndex] / normaliser;
                        }
                    } // end of family index loop

                    motherAge += 1;

                } // end of mother's age loop

                // what is the population rate of increase?
                r = normaliser / F1[0][0];  // use 'normaliser' because F2[0][0] gets 'normalised' above so result == 1 always

                try
                {
                    if (i >= 75)
                    {
                        if (rPrev.ToString().Substring(0, 7) == r.ToString().Substring(0, 7))  // match current and previous pop growth rates to 4th decimal place
                        {
                            Console.WriteLine("CONVERGED");
                            converged = true;
                        }
                    }
                }
                catch (Exception e) { }

                rPrev = r;

                // write to F1 for next time step
                F1 = F2.Select(s => s.ToArray()).ToArray();

                if (logger != null)
                    logger.WriteLine("Finished loop {0} in {1}; r={2}", i, stopwatch.Elapsed, r);
                else
                    Console.WriteLine("Finished loop {0} in {1}; r={2}", i, stopwatch.Elapsed, r);

                i++;

            } while (!converged);
            // end of main loop

            stopwatch.Stop();

            finishedBackward = true;
        }

        #region Fitness calculations
        double findFMax(int motherAge, int familyIndex, int arrayIndex, ref bool birthDecision)
        {
            bool giveBirth = true;

            // look for any 1 year olds in the family
            if (family[familyIndex].Contains(1))
                giveBirth = false;

            // dead mother can't make babies
            if (motherAge >= motherAgeMax)
                giveBirth = false;

            // calculate and compare fitnesses for giving birth and not giving birth
            double fitnessNoBirth = BackCalcF(motherAge, family[familyIndex].ToArray(), familyIndex, arrayIndex, false);
            double fitnessBirth = 0.0;

            if (giveBirth)
                fitnessBirth = BackCalcF(motherAge, family[familyIndex].ToArray(), familyIndex, arrayIndex, true);

            // store fitnesses
            fitnesses[arrayIndex, familyIndex, 0] = fitnessBirth;
            fitnesses[arrayIndex, familyIndex, 1] = fitnessNoBirth;

            // return the maximum fitness value and the corresponding birth decision
            // (no point checking for equality in double-precision variables)
            if (fitnessBirth > fitnessNoBirth)
            {
                birthDecision = true;
                return fitnessBirth;
            }
            else
            {
                birthDecision = false;
                return fitnessNoBirth;
            }
        }

        double BackCalcF(int motherAge, int[] currentFamily, int familyIndex, int arrayIndex, bool giveBirth)
        {
            double offspringDescendants = 0.0, outputF = 0.0;

            double[] pMother = new double[2];
            double pChild = 0.0;
            double[] pNewbornMotherAlive = new double[2];
            double[] pNewbornMotherDead = new double[2];

            int nextFamily = 0;
            int nextFamilyNoBirth = 0;

            pMother[0] = calcSurvMother(motherAge, currentFamily, giveBirth, familyIndex);
            pMother[1] = 1 - pMother[0];

            pNewbornMotherAlive[0] = 1.0; pNewbornMotherAlive[1] = 0.0;
            pNewbornMotherDead[0] = 1.0; pNewbornMotherDead[1] = 0.0;

            if (giveBirth)
            {
                pNewbornMotherAlive[0] = calcSurvChild(motherAge, currentFamily, giveBirth, 0, familyIndex);
                pNewbornMotherAlive[1] = 1 - pNewbornMotherAlive[0];

                pNewbornMotherDead[0] = calcSurvChild(motherAgeMax, currentFamily, giveBirth, 0, familyIndex);
                pNewbornMotherDead[1] = 1 - pNewbornMotherDead[0];
            }

            // iterate the power set of this particular family structure (i.e. array of ints in family[familyIndex])
            // make power set a delegate/func<>/
            foreach (HashSet<int> children in currentFamily.powerset())
            {
                pChild = 1.0; // reset probabilities counter
                offspringDescendants = 0.0;  // reset weighting of having a 14 year old in family (in case it dies)

                // now loop age of each child in actual family structure, not in current set from power set
                // -- calculate probabilities for survival for each
                foreach (int livingChild in currentFamily)
                {
                    double pChildMotherAlive = calcSurvChild(motherAge, currentFamily, giveBirth, livingChild, familyIndex);

                    // !! if it's an empty set (no children), is the code skipping over this? !!
                    // probably not...
                    if (!children.Contains(livingChild))
                    {
                        // this child is not in current set -- calc its probability of dying
                        pChildMotherAlive = 1 - pChildMotherAlive;
                    }

                    pChild *= pChildMotherAlive;
                }

                if (children.Contains((ageAtMaturity - 1)))  // if there's a child about to mature...
                {
                    // calc weight for having somebody about to be sexually mature
                    double coef = 0.5;
                    offspringDescendants = coef * F1[0][0];
                }

                nextFamily = nextFamilyIndex(children, giveBirth);
                nextFamilyNoBirth = nextFamilyIndex(children, false);

                for (int isNewbornDead = 0; isNewbornDead < 2; isNewbornDead++)
                {
                    // output fitness += prob * F1(next mother age, next family index) + offspringDescendants
                    // for each combination of probabilities:
                    // 1. p(mother surviving)
                    //    next mother age = mother age++
                    if (motherAge < motherAgeMax)
                    {
                        if (isNewbornDead == 0              // index 0 == newborn survives
                            && nextFamily > -1) // must have a valid family index
                            outputF += ((pMother[0] * pChild * pNewbornMotherAlive[0]) * (F1[arrayIndex + 1][nextFamily] + offspringDescendants));

                        if (isNewbornDead == 1                      // index 1 == newborn dies
                            && nextFamilyNoBirth > -1)  // must have valid family index
                            outputF += ((pMother[0] * pChild * pNewbornMotherAlive[1]) * (F1[arrayIndex + 1][nextFamilyNoBirth] + offspringDescendants));
                    }
                    // 2. p(mother dying)
                    //    next mother age = maximum
                    if (isNewbornDead == 0              // index 0 == newborn survives
                        && nextFamily > -1) // must have valid family index
                        outputF += ((pMother[1] * pChild * pNewbornMotherDead[0]) * (F1[motherArrayMax][nextFamily] + offspringDescendants));

                    if (isNewbornDead == 1                      // index 1 == newborn dies
                        && nextFamilyNoBirth > -1)  // must have valid family index
                        outputF += ((pMother[1] * pChild * pNewbornMotherDead[1]) * (F1[motherArrayMax][nextFamilyNoBirth] + offspringDescendants));
                }
            }
            return outputF;
        }
        #endregion
    }
}
