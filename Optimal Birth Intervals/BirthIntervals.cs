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
        #region Constants
        int numFamilies = 987;
        int motherAgeMax = 91;
        int motherArrayMax = 76;  // calculated from motherAgeMax
        //const int maxTback = 75;  // not needed -- model runs to convergence
        const int maxTforward = 150;

        int ageAtMaturity;  // this is calculated by importFamiliesFromString() from the family structures in families.txt

        const string appWorkingDir = @"WorkingDir";
        #endregion

        #region Properties
        public string population { get; set; }

        public MortalityParam childbirthMortality { get; set; }              // risk of dying in childbirth
        public MortalityParam maternalMortalityChildInfluence { get; set; }  // child influence on mother's mortality
        public MortalityParam siblingCompetition { get; set; }               // level of sibling competition
        public MortalityParam childMortalityMotherInfluence { get; set; }    // mother's influence on child mortality
        public MortalityParam siblingHelp { get; set; }                      // level of sibling help

        public double silerA1 { get; set; }
        public double silerB1 { get; set; }
        public double silerA2 { get; set; }
        public double silerA3 { get; set; }
        public double silerB3 { get; set; }

        public StreamWriter logger { private get; set; }  // where to write output to

        // variable for capturing rate of increase in fitness
        public double r { get; private set; }

        // model meta-state variables for interactive mode
        public bool finishedBackward { get; private set; }
        public bool finishedForward { get; private set; }
        #endregion

        #region Internal variables
        string inputFam = @"fam.in";

        // backward iteration variables
        double[][] F1;
        double[][] F2;

        double[, ,] fitnesses; // track mother fitnesses when giving bith [index 0] and not [index 1]

        // birth decisions (common to both forward and back)
        bool[][] births;

        // forward iteration variables
        double[][] NoFam1;
        double[][] NoFam2;

        // for stats
        double[] birthIntervals;
        double[] LBnewborn; // includes under 15s
        double[] LBfreq;    // includes under 15s
        double[,] LB; // 2nd dimension = {freq*r, Sa, b, l} <-- from LB.xls

        // age-based population growth rate
        double[] lambda;    // includes under 15s
        double[] refStates;

        // family structures
        List<HashSet<int>> family = new List<HashSet<int>>();

        // state space containing valid mother age/family structure combinations
        bool[,] StateSpace;
        #endregion

        #region Memoisation variables
        double[, ,] mmMotherSurvival;  // [motherAge,2] because it calcs separate survival probabilities for mothers giving birth and not
        double[, , ,] mmChildSurvival;  // [child age, mother age, family structure, mother giving birth?]

        Dictionary<HashSet<int>, int> mmFamilyIndicesBirths = new Dictionary<HashSet<int>, int>(HashSet<int>.CreateSetComparer());
        Dictionary<HashSet<int>, int> mmFamilyIndicesNoBirths = new Dictionary<HashSet<int>, int>(HashSet<int>.CreateSetComparer());
        #endregion

        #region Constructor
        // Read in family structures and initialise fitness/decision arrays
        public BirthIntervals()
        {
            // start by importing family structures and calculating a lookup table for them
            importFamiliesFromString();
            CalculateLookupTable();

            // before initialising arrays, set some global variables
            motherAgeMax = Properties.Settings.Default.MaxAge + 1;  // add 1 because we don't want death state in output array
            numFamilies = family.Count;
            motherArrayMax = motherAgeMax - ageAtMaturity;

            // set up fitness and strategy arrays
            InitialiseArrays();
            InitialisePopulationGrowthRate();

            // initialise properties
            logger = null;  // defaults to writing output to Console
            finishedBackward = false;
            finishedForward = false;

            exploreStateSpace();
        }

        private void InitialisePopulationGrowthRate()
        {
            // main population growth rate
            r = 0.0;

            // age-based growth rate
            for (int age = 0; age <= motherAgeMax; age++)
            {
                lambda[age] = 1.0;

                if (age < refStates.Length)
                    refStates[age] = 1.0;
            }
        }

        private void InitialiseArrays()
        {
            F1 = new double[motherArrayMax + 1][];
            F2 = new double[motherArrayMax + 1][];

            fitnesses = new double[motherArrayMax + 1, numFamilies, 2]; // track mother fitnesses when giving bith [index 0] and not [index 1]

            // birth decisions (common to both forward and back)
            births = new bool[motherArrayMax + 1][];

            // forward iteration variables
            NoFam1 = new double[motherArrayMax + 1][];
            NoFam2 = new double[motherArrayMax + 1][];

            // for stats
            birthIntervals = new double[motherArrayMax + 1];
            LBnewborn = new double[motherArrayMax + ageAtMaturity + 1]; // includes under 15s
            LBfreq = new double[motherArrayMax + ageAtMaturity + 1];    // includes under 15s
            LB = new double[motherArrayMax + ageAtMaturity + 1, 4]; // 2nd dimension = {freq*r, Sa, b, l} <-- from LB.xls

            // age-based population growth rate
            lambda = new double[motherArrayMax + ageAtMaturity + 1];    // includes under 15s
            refStates = new double[motherArrayMax + 1];

            StateSpace = new bool[motherArrayMax + 1, numFamilies];
            mmMotherSurvival = new double[motherArrayMax + 1, numFamilies, 2];  // [motherAge,2] because it calcs separate survival probabilities for mothers giving birth and not
            mmChildSurvival = new double[ageAtMaturity, motherArrayMax + 1, numFamilies, 2];  // [child age, mother age, family structure, mother giving birth?]
            
            // initialise fitness and birth decision arrays
            for (int i = 0; i < F1.Length; i++)
            {
                // initialise jagged arrays
                F1[i] = new double[numFamilies];
                F2[i] = new double[numFamilies];
                births[i] = new bool[numFamilies];
                NoFam1[i] = new double[numFamilies];
                NoFam2[i] = new double[numFamilies];

                for (int j = 0; j < numFamilies; j++)
                {
                    F1[i][j] = 1;  // set terminal reward = 1
                    F2[i][j] = 0;
                    births[i][j] = false;
                    NoFam1[i][j] = 0;  // no viable family structures until told otherwise
                    NoFam2[i][j] = 0;
                }
            }

            // apparently these exceptions need to be made <-- from Daryl's code
            F1[F1.Length - 1][0] = 0;
            F1[F1.Length - 1][1] = 0;

            // 15 year old mother with no kids is the starting point
            NoFam1[0][0] = 1;
        }
        #endregion

        #region Output methods
        public void outputBirthDecisions()
        {
            string ibiOutput = "Births-" + getParamString() + ".out";
            StreamWriter sw = new StreamWriter(ibiOutput);

            for (int i = 0; i < births.Length; i++)
                for (int j = 0; j < numFamilies; j++)
                    sw.WriteLine("{0},{1},{2}", i + ageAtMaturity, j + 1, births[i][j]);

            sw.Close();
        }

        public void outputGrowthRate()
        {
            // output r
            string fOutput = "R-" + getParamString() + ".out";
            StreamWriter sw = new StreamWriter(fOutput, true);
                sw.WriteLine("{0}", r);
            sw.Close();

            // output lambdas
            fOutput = "R-Lambda-" + getParamString() + ".out";
            sw = new StreamWriter(fOutput, false);
                foreach (double state in lambda)
                    sw.WriteLine("{0}", state);
            sw.Close();
        }
        
        public void outputStats()
        {
            // output birth intervals
            string fOutput = "IB-" + getParamString() + ".csv";
            StreamWriter sw = new StreamWriter(fOutput);

            sw.WriteLine("age,IBI");

            for (int i = 0; i < birthIntervals.Length; i++)
                sw.WriteLine("{0},{1}", i + ageAtMaturity, birthIntervals[i]);

            sw.Close();
        }
        #endregion

        // calculate birth intervals and survival/fertility numbers
        #region Stats
        public void calcStats()
        {
            int youngest = 0, oldest = 0, numChildren = 0;

            for (int age = 0; age < births.Length; age++)
            {
                double IBsum = 0.0, IBfreq = 0.0;

                int motherAge = age + ageAtMaturity;

                for (int familyIndex = 0; familyIndex < numFamilies; familyIndex++)
                {
                    numChildren = 0;

                    double NFvalue = NoFam1[age][familyIndex];

                    if (NFvalue > 0)
                    {
                        HashSet<int> currentFamily = family[familyIndex];

                        try
                        {
                            youngest = currentFamily.Min();
                        }
                        catch (Exception e) { youngest = 0; }
                        if (youngest == -1)
                            youngest = 0;

                        if (births[age][familyIndex])
                        {
                            youngest = 0;
                            numChildren = 1;

                            LBnewborn[motherAge] += NFvalue;
                            // multiply freq. of newborns by 0.5 because we only want to count female offspring
                            LBfreq[0] += 0.5 * NFvalue;
                        }

                        try
                        {
                            oldest = currentFamily.Max();
                        }
                        catch (Exception e) { oldest = 0; }
                        if (oldest == -1)
                            oldest = 0;

                        int familySize = currentFamily.Count;

                        numChildren += familySize;

                        if (numChildren >= 2)
                        {
                            long IBkids = (oldest - youngest) / (numChildren - 1);
                            IBsum += IBkids * NFvalue;

                            if (IBkids > 0)
                                IBfreq += NFvalue;
                        }

                        // mother's age
                        LBfreq[motherAge] += NFvalue;

                        // frequency of children 
                        // multiply freq. of children by 0.5 because we only want to count female offspring
                        foreach (int kid in currentFamily)
                            LBfreq[kid] += 0.5 * NFvalue;
                    }
                }

                birthIntervals[age] = (IBfreq > 0.0) ? (IBsum / IBfreq) : 0.0;
            }
        }
        #endregion

        #region Properties methods
        public bool[][] getBirthDecisions()
        {
            return births;
        }

        public void setBirthDecisions(bool[][] b)
        {
            births = b;
        }

        public double[][] getFitness()
        {
            return F1;
        }
        #endregion
        
        #region Survival calculations
        double calcSurvChild(int motherAge, int[] currentFamily, bool giveBirth, int childAge, int familyIndex)
        {
            double muChild = 0.0;
            double weight = 0.0;

            // very young children die if mother dies
            if (motherAge >= motherAgeMax && childAge <= 2)
                return 0.0;

            // check if memoised value exists and return if so
            double memoSurvival = 0.0;
            
            /* no memoisation so continue with calculations */

            /* CHILD MORTALITY */
            // make mother presence/absence the same effect as Shanley 2007 (i.e. increase in relative risk)
            // so child mortality component modelled as straight Siler rather than the thing below
            muChild = silerA1 * Math.Exp(-1.0 * silerB1 * childAge);

            if (motherAge >= motherAgeMax)
            {
                muChild *= 11.7;
            }

            /* 
             * SIBLING EFFECTS
             */
            if (giveBirth && childAge != 0)
            {
                switch (siblingCompetition)
                {
                    case MortalityParam.None:
                        weight = 0.0; 
                        break;

                    case MortalityParam.Low:
                        weight = 0.5;
                        break;

                    case MortalityParam.Medium:
                        weight = 1.0;
                        break;

                    case MortalityParam.High:
                        weight = 1.0;
                        break;
                }
            }

            // A high weight results in an important effect on mortality,
            // conversely a low weight results in a negligible effect on mortality
            foreach (int age in currentFamily)
            {
                if (age != childAge)
                {
                    switch (siblingCompetition)
                    {
                        case MortalityParam.None:
                            weight = 0.0;
                            break;

                        case MortalityParam.Low:
                            weight += (double)(.5 - (age * .03333));
                            break;

                        case MortalityParam.Medium:
                            weight += (double)(1 - (age * .06666));
                            break;

                        case MortalityParam.High:
                            weight += (double)(1 - (age * 0.0357));
                            break;
                    }

                    // use childbirthMortality param for sibling help for the time being
                    // only those 10 and over can help
                    if (age >= 10)
                    {
                        double helpModifier = 5.0;  // or 10.0 (originally)

                        switch (siblingHelp)
                        {
                            case MortalityParam.None:
                                break;

                            case MortalityParam.Low:
                                //weight -= 0.1;
                                weight += (double)(.5 - (age * .03333)) * -1.0;
                                break;

                            case MortalityParam.Medium:
                                //weight += (double)(-.5 * (age - 10.0) / helpModifier);
                                weight += (double)(1 - (age * .06666)) * -1.0;
                                break;

                            case MortalityParam.High:
                                //weight += (double)(-1.0 * (age - 10.0) / helpModifier);
                                weight += (double)(1 - (age * 0.0357)) * -1.0;
                                break;
                        }
                    }
                }
            }

            muChild *= (1 + weight);

            // calculate probability of survival
            memoSurvival = Math.Exp(-1 * muChild);

            // return survival
            return memoSurvival;
        }

        double calcSurvMother(int motherAge, int[] currentFamily, bool giveBirth, int familyIndex)
        {
            double muSen = 0.0, muBirth = 0.0;

            // Mother reached maximum age. Die.
            if (motherAge >= motherAgeMax)
                return 0.0;

            // check for memoisation and return if found
            double memoSurvival = 0.0;

            /* Adult senescent mortality */
            muSen = silerA3 * Math.Exp(silerB3 * (motherAge - ageAtMaturity));

            /* Maternal mortality in childbirth
             * These parameters get varied for no increase with age, or a low, medium and high age-related increase as shown in Figure 1b
             */
            if (giveBirth && childbirthMortality != MortalityParam.None)
            {
                muBirth = calcMaternalMortality(motherAge);
            }

            /* calculate survival
             * Constant value is the extrinsic adult mortality rate.
             */
            double muExtrinsic = silerA2;
            memoSurvival = Math.Exp((-1.0 * muExtrinsic) - muSen - muBirth);

            // and return survival
            return memoSurvival;
        }

        // returns the mother's probability of dying during childbirth given her age
        private double calcMaternalMortality(int motherAge)
        {
            double muBirth = 0.0;
            double alphaBirth = 0.0, betaBirth = 0.0, gammaBirth = 0.0;

            // calculate maternal morality
            // J-shaped maternal mortality ratios from Blanc et al. (2013)
            // (http://www.plosone.org/article/info%3Adoi%2F10.1371%2Fjournal.pone.0059864)
            if (Properties.Settings.Default.MaternalMortalityFunc == "J")
            {
                // what level of maternal mortality?
                switch (childbirthMortality)
                {
                    case MortalityParam.Low:
                        alphaBirth = 1.424218e-05;
                        betaBirth = 5.415419e-04;
                        gammaBirth = 7.492639e-03;
                        break;

                    case MortalityParam.Medium:
                        alphaBirth = 1.394538e-05;
                        betaBirth = 5.268179e-04;
                        gammaBirth = 8.448535e-03;
                        break;

                    case MortalityParam.High:
                        alphaBirth = 1.236674e-05;
                        betaBirth = 4.478437e-04;
                        gammaBirth = 9.056053e-03;
                        break;
                }

                // a * (age^2) - b * age + c
                muBirth = (alphaBirth * Math.Pow(motherAge, 2)) - (betaBirth * motherAge) + gammaBirth;
            }

            // Exponential maternal mortality fitted from Grimes (1994)
            // as used in from Shanley et al. (2007)
            else if (Properties.Settings.Default.MaternalMortalityFunc == "E")
            {
                // what level of maternal mortality?
                switch (childbirthMortality)
                {
                    case MortalityParam.Low:
                        alphaBirth = 0.002928;
                        betaBirth = 0.1;
                        break;

                    case MortalityParam.Medium:
                        alphaBirth = 0.000485;
                        betaBirth = 0.181221;
                        break;

                    case MortalityParam.High:
                        alphaBirth = 1e-6;
                        betaBirth = 0.5;
                        break;
                }

                // (a * Exp(b * (age - ageAtMaturity))) + (silerA2 - a)
                muBirth = (alphaBirth * Math.Exp(betaBirth * (motherAge - ageAtMaturity))) + (silerA2 - alphaBirth);
            }

            return muBirth;
        }
        #endregion

        #region Internal methods
        // get the index in F0/F1 of next year's family index /// increment ages, ignore those who will be sexually mature
        private int nextFamilyIndex(HashSet<int> currentFamily, bool giveBirth)
        {
            int idx;
            if (giveBirth)
            {
                if (mmFamilyIndicesBirths.TryGetValue(currentFamily, out idx))
                    return idx;
            }
            else
            {
                if (mmFamilyIndicesNoBirths.TryGetValue(currentFamily, out idx))
                    return idx;
            }

            return idx;
        }

        // Return a string of mortality parameter values (e.g. medium, low, medium, high returns "MLMH")
        private string getParamString()
        {
            string mortMaternal = Enum.GetName(typeof(MortalityParam), childbirthMortality).Substring(0, 1);
            string mortChildInfluence = Enum.GetName(typeof(MortalityParam), maternalMortalityChildInfluence).Substring(0, 1);
            string mortSiblingComp = Enum.GetName(typeof(MortalityParam), siblingCompetition).Substring(0, 1);
            string mortMotherInfluence = Enum.GetName(typeof(MortalityParam), childMortalityMotherInfluence).Substring(0, 1);
            string mortSiblingHelp = Enum.GetName(typeof(MortalityParam), siblingHelp).Substring(0, 1);

            return population + "-" + mortMaternal + mortSiblingComp + mortSiblingHelp;
        }

        private void exploreStateSpace()
        {
            int fam = 0;  // track current family index

            StateSpace[0, fam] = true; // 15 year old with no kids is the first valid state

            for (int mother = 1; mother <= motherArrayMax; mother++)
            {
                for (fam = 0; fam < numFamilies; fam++)
                {
                    if (family[fam].Any(child => ((mother + ageAtMaturity) - child) < ageAtMaturity))
                    {
                        StateSpace[mother, fam] = false;
                    }
                    else
                    {
                        StateSpace[mother, fam] = true;
                    }
                }
            }
        }

        public double TeenageSubfecundityProbability(int motherAge)
        {
            double pBirth = 1;

            // assign probabilities of giving birth
            // linear from 0.25 at age 15 to 1.0 at age 20 -- y = 0.25 + 0.15.x
            switch (motherAge)
            {
                case 15:
                    pBirth = 0.25;
                    break;

                case 16:
                    pBirth = 0.4; //0.5;
                    break;

                case 17:
                    pBirth = 0.55; //0.75;
                    break;

                case 18:
                    pBirth = 0.7;
                    break;

                case 19:
                    pBirth = 0.85;
                    break;

                default:
                    break;
            }

            return pBirth;
        }
        #endregion

        #region File import methods
        private void importFamiliesFromString()
        {
            int oldestChild = -1;

            try
            {
                using (Stream stream = this.GetType().Assembly.GetManifestResourceStream("Optimal_Birth_Intervals." + "families.txt"))
                using (StreamReader sr = new StreamReader(stream))
                {
                    int row = 0;

                    // read in family structures as string
                    while (!sr.EndOfStream)
                    {
                        // add blank family to begin with
                        family.Add(new HashSet<int>());

                        string line = sr.ReadLine();

                        // row is space-separated. split to get ages of individual children
                        string[] structure = line.Split(' ');

                        // add each child's age to current HashSet, converting to int first
                        foreach (string child in structure) {
                            int age = 0;

                            if (int.TryParse(child, out age))
                            {
                                family[row].Add(age);

                                // track age of oldest child found so far
                                if (age > oldestChild)
                                    oldestChild = age;
                            }
                        }

                        row++;
                    }
                }

                // set age at maturity
                ageAtMaturity = oldestChild + 1;
            }
            catch (Exception e)
            {
                throw new FileNotFoundException("Couldn't read input file", "families.txt");
            }
        }

        private void CalculateLookupTable()
        {
            foreach (HashSet<int> currentFamily in family)
            {
                HashSet<int> incrementedFamily = new HashSet<int>();

                // increment ages in current family
                foreach (int child in currentFamily)
                {
                    int newAge = child + 1;

                    if (newAge < ageAtMaturity)
                        incrementedFamily.Add(newAge);
                }

                int index = -1;

                // find index of incremented family in main family array
                for (int i = 0; i < family.Count; i++)
                {
                    if (family[i].SetEquals(incrementedFamily))
                    {
                        index = i;
                        break;
                    }
                }

                // add to "no birth" lookup dictionary
                if (index != -1)
                    mmFamilyIndicesNoBirths.Add(currentFamily, index);

                // add a newborn if family doesn't contain a 1 or 2 year old
                if (!(incrementedFamily.Contains(1) || incrementedFamily.Contains(2)))
                {
                    incrementedFamily.Add(1);

                    // reset index for next search
                    index = -1;

                    // find index of family with newborn in main family array
                    for (int i = 0; i < family.Count; i++)
                    {
                        if (family[i].SetEquals(incrementedFamily))
                        {
                            index = i;
                            break;
                        }
                    }

                    // add to "birth" lookup dictionary
                    if (index != -1)
                        mmFamilyIndicesBirths.Add(currentFamily, index);
                }
            }
        }

        // Ask user to specifiy a file name containing birth decisions to import
        public void importBirthDecisionsFromFile(string filename = "")
        {
            string ibiInput = "";

            // initialise birth decisions array
            for (int i = 0; i < births.Length; i++)
            {
                births[i] = new bool[numFamilies];
            }

            //try
            //{
            if (filename == "")
            {
                Console.Write("Enter the name of a file containing birth decisions: ");

                ibiInput = Console.ReadLine(); //"I" + getParamString() + ".out";
            }
            else
            {
                ibiInput = filename;
            }

                StreamReader sr = new StreamReader(ibiInput);
                int row = 0, col = 0;

                // read in birth decisions
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();

                    if (line.Contains(',')) // .csv format
                    {
                        string[] tmp = line.Split(',');
                        line = tmp[tmp.Length - 1];      // get last element in .csv string
                    }
                    else // FORTRAN format
                    {
                        if (line.Contains('1'))
                            line = "true";
                        else if (line.Contains('0'))
                            line = "false";
                    }

                    births[row][col++] = Convert.ToBoolean(line);

                    if (col == numFamilies)
                    {
                        row++;
                        col = 0;
                    }

                    // contents of file too big for array so bail out of loop
                    if (row >= motherAgeMax)
                        break;
                }

                sr.Close();
        }

        // import population files
        public void importPopulationFromFile(string filename = "")
        {
            string ibiInput = "";

            // initialise population distribution array
            for (int i = 0; i < NoFam1.Length; i++)
            {
                NoFam1[i] = new double[numFamilies];
            }

            if (filename == "")
            {
                Console.Write("Enter the name of a file containing the population distribution: ");

                ibiInput = Console.ReadLine();
            }
            else
            {
                ibiInput = filename;
            }
                
            StreamReader sr = new StreamReader(ibiInput);
            int row = 0, col = 0;

            // read in birth decisions
            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();

                char delimiter = ',';
                    
                string[] tmp = line.Split(delimiter);
                row = Convert.ToInt32(tmp[0]) - 15;
                col = Convert.ToInt32(tmp[1]) - 1;
                line = tmp[2];      // get last element in .csv string

                NoFam1[row][col] = Convert.ToDouble(line);

                // contents of file too big for array so bail out of loop
                if (row >= motherAgeMax)
                    break;
            }

            sr.Close();
        }

        public void importPopulationGrowth(string filename = "")
        {
            string strR = "";

            if (filename == "")
            {
                Console.Write("Enter population growth, R=");

                strR = Console.ReadLine();

                try
                {

                    r = double.Parse(strR);
                }
                catch (Exception e) { Console.WriteLine("Invalid R value ({0})", e.ToString()); }
            }
            else
            {
                strR = filename;

                StreamReader sr = new StreamReader(strR);
                string line = sr.ReadLine();
                string[] tmp = line.Split('=');
                if (tmp.Length > 1)
                    r = double.Parse(tmp[1]);
                else
                    r = double.Parse(tmp[0]);
            }
        }
        #endregion

        public void CreateFamilyTransitionMatrix()
        {
            byte[,] tmBirths = new byte[987, 987];
            byte[,] tmNoBirths = new byte[987, 987];

            int[] currentFamily;

            int nextFamilyBirth, nextFamilyNoBirth;
            int countBirth = 0;
            int countNoBirth = 0;

            // loop over all families
            for (int i = 0; i < 987; i++)
            {
                // get family array
                currentFamily = family[i].ToArray();

                // loop over powerset of this family
                foreach (HashSet<int> children in currentFamily.powerset())
                {
                    // find next index with a birth
                    if (!children.Contains(1))
                    {
                        nextFamilyBirth = nextFamilyIndex(children, true);
                        // write into matrix
                        tmBirths[i, nextFamilyBirth] = 1;
                        countBirth++;
                    }

                    // find next index without a birth
                    nextFamilyNoBirth = nextFamilyIndex(children, false);

                    // write into matrices
                    tmNoBirths[i, nextFamilyNoBirth] = 1;
                    countNoBirth++;
                }
            }

            // write to files
            StreamWriter swBirthsMM = new StreamWriter("FamilyBirths.mm");
            StreamWriter swNoBirthsMM = new StreamWriter("FamilyNoBirths.mm");
            StreamWriter swBirths = new StreamWriter("FamilyBirths.txt");
            StreamWriter swNoBirths = new StreamWriter("FamilyNoBirths.txt");

            // write Matrix Market headers
            swBirthsMM.WriteLine("%%MatrixMarket matrix coordinate pattern general");
            swBirthsMM.WriteLine("987 987 {0}", countBirth);

            swNoBirthsMM.WriteLine("%%MatrixMarket matrix coordinate pattern general");
            swNoBirthsMM.WriteLine("987 987 {0}", countNoBirth);


            for (int row = 0; row < 987; row++)
            {
                for (int col = 0; col < 987; col++)
                {
                    // write in Matrix Market format
                    if (tmBirths[row, col] != 0)
                        swBirthsMM.WriteLine("{0} {1}", row + 1, col + 1);

                    if (tmNoBirths[row, col] != 0)
                        swNoBirthsMM.WriteLine("{0} {1}", row + 1, col + 1);

                    // write dense matrix
                    swBirths.Write("{0} ", tmBirths[row, col]);
                    swNoBirths.Write("{0} ", tmNoBirths[row, col]);
                }

                // new lines at the end of all columns
                swBirths.WriteLine();
                swNoBirths.WriteLine();
            }
        }
    }
}
