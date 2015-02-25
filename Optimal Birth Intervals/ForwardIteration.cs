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
        public void runForward()
        {
            double sumFam = 0.0;

            // variables for capturing rate of increase in fitness
            double popSum = 0.0;
            double popSumPrev = 0.0;
            double refStatePrev = 0.0;

            double[] refStatesPrev = new double[motherArrayMax + 1];
            refStatesPrev = refStates.ToArray();

            double[] lambdaPrev = new double[motherAgeMax + 1];
            double[] lambdaThisYear = new double[motherAgeMax + 1];
            lambdaPrev = lambda.ToArray();
            lambdaThisYear = lambda.ToArray();

            refStatePrev = NoFam1[0][0];

            int motherAge = ageAtMaturity;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // main loop
            for (int i = 0; i < maxTforward; i++)
            {
                motherAge = ageAtMaturity;

                // loop over each permutation of mother's age and family index to find max. fitness
                for (int arrayIndex = 0; arrayIndex < NoFam1.Length; arrayIndex++)
                {
                    for (int familyIndex = 0; familyIndex < numFamilies; familyIndex++)
                    {
                        if (NoFam1[arrayIndex][familyIndex] != 0)
                        {
                            findFMax(motherAge, familyIndex, arrayIndex);
                        }
                    } // end of family index loop

                    motherAge += 1;

                } // end of mother's age loop

                // calc sum of all family values
                sumFam = 0.0;
                popSum = 0.0;
                popSumPrev = 0.0;

                for (int arrayIndex = 0; arrayIndex < NoFam1.Length; arrayIndex++)
                {
                    double ageGrowthRate = 0.0;

                    for (int familyIndex = 0; familyIndex < numFamilies; familyIndex++)
                    {
                        if (NoFam2[arrayIndex][familyIndex] != 0)
                            sumFam += NoFam2[arrayIndex][familyIndex];

                        ageGrowthRate += NoFam2[arrayIndex][familyIndex];
                    }

                    if (ageGrowthRate > 0.0)
                        lambdaThisYear[arrayIndex + 15] = ageGrowthRate;

                    if (NoFam2[arrayIndex][0] > 0.0)
                    {
                        refStates[arrayIndex] = NoFam2[arrayIndex][0] / refStatesPrev[arrayIndex];
                        refStatesPrev[arrayIndex] = NoFam2[arrayIndex][0];
                    }
                }

                // what is the population rate of increase?
                r = NoFam2[0][0] / refStatePrev;

                // calculate age-based growth rate
                for (int age = 0; age <= motherAgeMax; age++)
                {
                    lambda[age] = lambdaThisYear[age] / lambdaPrev[age];
                }

                // track old age-based growth rates
                // keep track of un-normalised reference state
                refStatePrev = NoFam2[0][0];
                lambdaPrev = lambdaThisYear.ToArray();

                // normalise array
                for (int arrayIndex = 0; arrayIndex < NoFam1.Length; arrayIndex++)
                    for (int familyIndex = 0; familyIndex < numFamilies; familyIndex++)
                        NoFam2[arrayIndex][familyIndex] /= sumFam;

                // write to F1 for next time step
                NoFam1 = NoFam2.Select(s => s.ToArray()).ToArray();

                if (logger != null)
                    logger.WriteLine("Finished loop {0} in {1}; r={2}", i, stopwatch.Elapsed, r);
                else
                    Console.WriteLine("Finished loop {0} in {1}; r={2}", i, stopwatch.Elapsed, r);

            } 
            // end of main loop

            stopwatch.Stop();

            finishedForward = true;
        }

        #region Fitness calculations
        void findFMax(int motherAge, int familyIndex, int arrayIndex)
        {
            bool giveBirth = false;

            if (births[arrayIndex][familyIndex] == true)
                giveBirth = true;

            // probabilistic age at first birth (teenage sub-fecundity)?
            if (Properties.Settings.Default.ProbabilisticAFB && motherAge < 20) // only if mother is younger than 20
            {
                double pBirth = TeenageSubfecundityProbability(motherAge);

                Random rnd = new Random();
                if (rnd.NextDouble() >= pBirth)
                {
                    // do not give birth this year
                    giveBirth = false;
                    births[arrayIndex][familyIndex] = false;
                }
            }

            // calculate fitness based on mother's age, current family structure and optimal birth decision
            FwdCalcF(motherAge, family[familyIndex].ToArray(), familyIndex, arrayIndex, giveBirth);
        }

        void FwdCalcF(int motherAge, int[] currentFamily, int familyIndex, int arrayIndex, bool giveBirth)
        {
            double offspringDescendants = 0.0;

            double[] pMother = new double[2];
            double pChild = 0.0;
            double[] pNewbornMotherAlive = new double[2];
            double[] pNewbornMotherDead = new double[2];

            int nextFamily = 0;
            int nextFamilyNoBirth = 0;

            pMother[0] = calcSurvMother(motherAge, currentFamily, giveBirth, familyIndex);
            pMother[1] = 1 - pMother[0];

            pNewbornMotherAlive[0] = 1.0; pNewbornMotherAlive[1] = 0.0;  // set 2nd element = 0 so output not doubled when no newborns
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

                if (children.Contains((ageAtMaturity - 1)))  // changed from currentFamily.Contains(14)
                {
                    // calc weight for having somebody about to be sexually mature
                    double coef = 0.5;
                    offspringDescendants = coef * NoFam1[arrayIndex][familyIndex];
                }

                nextFamily = nextFamilyIndex(children, giveBirth);
                nextFamilyNoBirth = nextFamilyIndex(children, false);

                for (int isNewbornDead = 0; isNewbornDead < 2; isNewbornDead++)
                {
                    // if there's a 14 year old in current family, she will become 15, increase fitness correspondingly
                    if (offspringDescendants > 0.0)
                    {
                        NoFam2[0][0] += offspringDescendants * (pMother[0] * pChild * pNewbornMotherAlive[isNewbornDead]);
                        NoFam2[0][0] += offspringDescendants * (pMother[1] * pChild * pNewbornMotherAlive[isNewbornDead]);
                    }

                    // output fitness += prob * F1(next mother age, next family index) + offspringDescendants
                    // for each combination of probabilities:
                    // 1. p(mother surviving)
                    //    next mother age = mother age++
                    if (motherAge < motherAgeMax)
                    {
                        if (isNewbornDead == 0              // index 0 == newborn survives
                            && nextFamily > -1) // must have a valid family index
                            NoFam2[arrayIndex + 1][nextFamily] += ((pMother[0] * pChild * pNewbornMotherAlive[isNewbornDead]) * (NoFam1[arrayIndex][familyIndex]));

                        if (isNewbornDead == 1                      // index 1 == newborn dies
                            && nextFamilyNoBirth > -1)  // must have valid family index
                            NoFam2[arrayIndex + 1][nextFamilyNoBirth] += ((pMother[0] * pChild * pNewbornMotherAlive[isNewbornDead]) * (NoFam1[arrayIndex][familyIndex]));
                    }

                    // 2. p(mother dying)
                    //    next mother age = maximum
                    if (isNewbornDead == 0              // index 0 == newborn survives
                        && nextFamily > -1) // must have valid family index
                        NoFam2[motherArrayMax][nextFamily] += ((pMother[1] * pChild * pNewbornMotherDead[isNewbornDead]) * (NoFam1[arrayIndex][familyIndex]));

                    if (isNewbornDead == 1                      // index 1 == newborn dies
                        && nextFamilyNoBirth > -1)  // must have valid family index
                        NoFam2[motherArrayMax][nextFamilyNoBirth] += ((pMother[1] * pChild * pNewbornMotherDead[isNewbornDead]) * (NoFam1[arrayIndex][familyIndex]));
                }
            }
        }
        #endregion  
    }
}
