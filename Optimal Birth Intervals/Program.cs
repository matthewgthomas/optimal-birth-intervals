using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Configuration;
using System.Threading.Tasks;
using System.Threading;
using System.Management;

namespace Optimal_Birth_Intervals
{
    class Program
    {
        private static BirthIntervals sdpInteractive;

        static void Main(string[] args)
        {
            Console.WriteLine("Optimal Birth Interval Modeller");
            Console.WriteLine("-------------------------------");

            // Have any mortality params been entered on cmd line?
            // (i.e. run in batch mode or require user input?)
            if (args.Length >= 4)  // run model once for specified mortality params
            {
                batchMode(args);
            }
            else if (args.Length == 1)  // user entered a config file with params; run in multi-thread mode
            {
                multiThreadMode(args[0]);  //? or get config file from app.config?
            }
            else  // need user input
            {
                interactiveMode();
            }
        }

        #region Batch/multi-threading modes
        private static void multiThreadMode(string configFile)
        {
            List<string> paramsList = new List<string>();
            
            // get all param combinations from config file
            StreamReader sr = new StreamReader(configFile);
            while (!sr.EndOfStream)
                paramsList.Add(sr.ReadLine());

            // get total numbers of cores in computer
            int coreCount = Environment.ProcessorCount;
            Console.WriteLine("Number Of cores: {0}", coreCount);

            // allow model to run one less than the total (so user can still do stuff at same time)
            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = coreCount - 1;

            // process all models in the config file in parallel
            Parallel.ForEach<string>(paramsList, options, p =>
                {
                    Console.WriteLine("Start thread={0}, model={1}", Thread.CurrentThread.ManagedThreadId, p);

                    batchMode(p.Split(' '));

                    Console.WriteLine("Finish thread={0}, model={1}", Thread.CurrentThread.ManagedThreadId, p);
                });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args">Array of four mortality parameters (n, l, m, h)</param>
        /// <param name="logToFile">If false, program only outputs to Console. If true, program outputs to file rather than Console</param>
        private static void batchMode(string[] args)
        {
            // Get mortality parameters
            MortalityParam mortMaternal, mortSiblingComp, mortSiblingHelp, mortChildInfluence, mortMotherInfluence;

            StreamWriter sw = null;

            // run backwards and forwards using passed mortality params (no user input required)
            // population name
            string population = args[0];
            //
            mortMaternal = parseArgs(args[1]);
            mortSiblingComp = parseArgs(args[2]);
            mortSiblingHelp = parseArgs(args[3]);
            // Siler params
            double silerA1 = double.Parse(args[4]);
            double silerB1 = double.Parse(args[5]);
            double silerA2 = double.Parse(args[6]);
            double silerA3 = double.Parse(args[7]);
            double silerB3 = double.Parse(args[8]);

            string births = "";
            string popMatrix = "";
            string rFile = "";
            if (args.Length == 10)
            {
                births = args[9];
            }
            else if (args.Length >= 11)
            {
                births = args[9];
                popMatrix = args[10];
                rFile = args[11];
            }

            // Return a string of mortality parameter values (e.g. medium, low, medium, high returns "MLMH")
            string sp1 = Enum.GetName(typeof(MortalityParam), mortMaternal).Substring(0, 1);
            string sp2 = Enum.GetName(typeof(MortalityParam), mortSiblingComp).Substring(0, 1);
            string sp3 = Enum.GetName(typeof(MortalityParam), mortSiblingHelp).Substring(0, 1);
            string p = population + "-" + sp1 + sp2 + sp3; // +sp4 + sp5;

            // set up log file
            string logFilename = "Log-" + p + ".log";

            Console.WriteLine("(Running in batch mode)");
            Console.WriteLine();

            // create and initialise model
            Console.WriteLine("-- initialising model");
            BirthIntervals sdp = new BirthIntervals();
            Console.WriteLine("-- model initialised");

            // set parameters of model
            sdp.population = population;
            sdp.childbirthMortality = mortMaternal;
            sdp.siblingCompetition = mortSiblingComp;
            sdp.siblingHelp = mortSiblingHelp;

            // set Siler params
            sdp.silerA1 = silerA1;
            sdp.silerB1 = silerB1;
            sdp.silerA2 = silerA2;
            sdp.silerA3 = silerA3;
            sdp.silerB3 = silerB3;

            Console.WriteLine(PrintParams(mortMaternal, mortSiblingComp, mortSiblingHelp));

            if (args.Length < 10)
            {
                // calculate optimal birth intervals
                Console.WriteLine("-- beginning backward iteration");
                sdp.runBackward();
                Console.WriteLine("-- backward iteration finished");

                // output birth decisions and fitness here
                Console.WriteLine("-- writing birth decisions to disk");
                sdp.outputBirthDecisions();
                sdp.outputGrowthRate();
                Console.WriteLine("-- birth decisions written to disk");

                Console.WriteLine("-- beginning forward iteration");
                sdp.runForward();
                Console.WriteLine("-- forward iteration finished");

                Console.WriteLine("-- writing population data to disk");
                sdp.outputGrowthRate();
                Console.WriteLine("-- population data written to disk");
            }
            if (args.Length == 10)
            {
                Console.WriteLine("-- importing births from {0}", births);
                sdp.importBirthDecisionsFromFile(births);
                Console.WriteLine("-- birth decisions imported");

                Console.WriteLine("-- beginning forward iteration");
                sdp.runForward();
                Console.WriteLine("-- forward iteration finished");

                Console.WriteLine("-- writing population data to disk");
                sdp.outputGrowthRate();
                Console.WriteLine("-- population data written to disk");
            }
            else if (args.Length >= 11)
            {
                Console.WriteLine("-- importing births from {0}", births);
                sdp.importBirthDecisionsFromFile(births);
                Console.WriteLine("-- birth decisions imported");

                Console.WriteLine("-- importing population matrix from {0}", popMatrix);
                sdp.importPopulationFromFile(popMatrix);
                Console.WriteLine("-- population matrix imported");

                Console.WriteLine("-- importing population growth rate from {0}", rFile);
                sdp.importPopulationGrowth(rFile);
                Console.WriteLine("-- population growth rate imported");

                Console.WriteLine("-- importing population matrix from {0}", popMatrix);
                sdp.importPopulationFromFile(popMatrix);
                Console.WriteLine("-- population matrix imported");
            }               

            Console.WriteLine("-- calculating stats");
            sdp.calcStats();
            Console.WriteLine("-- calculated stats");

            Console.WriteLine("-- writing stats data to disk");
            sdp.outputStats();
            Console.WriteLine("-- stats data written to disk");
        }
        #endregion

        #region Interactive (user input) mode
        private static void interactiveMode()
        {
            Console.WriteLine("(Running in interactive mode)");
            Console.WriteLine();

            Console.WriteLine("Please enter mortality parameters (h=high, m=medium, l=low, n=none):");
            Console.Write("Risk of mortality during childbirth: ");
            MortalityParam p1 = parseArgs(Console.ReadLine());

            Console.Write("Intensity of sibling competition: ");
            MortalityParam p2 = parseArgs(Console.ReadLine());

            Console.Write("Level of juvenile help: ");
            MortalityParam p3 = parseArgs(Console.ReadLine());

            Console.WriteLine(PrintParams(p1, p2, p3));

            Console.Write("Enter Siler parameter a1: ");
            double silerA1 = double.Parse(Console.ReadLine());
            Console.Write("Enter Siler parameter b1: ");
            double silerB1 = double.Parse(Console.ReadLine());
            Console.Write("Enter Siler parameter a2: ");
            double silerA2 = double.Parse(Console.ReadLine());
            Console.Write("Enter Siler parameter a3: ");
            double silerA3 = double.Parse(Console.ReadLine());
            Console.Write("Enter Siler parameter b3: ");
            double silerB3 = double.Parse(Console.ReadLine());
            
            bool done = false;

            // create and initialise model
            sdpInteractive = new BirthIntervals();

            // set parameters of model
            sdpInteractive.childbirthMortality = p1;
            sdpInteractive.siblingCompetition = p2;
            sdpInteractive.siblingHelp = p3;

            // set Siler params
            sdpInteractive.silerA1 = silerA1;
            sdpInteractive.silerB1 = silerB1;
            sdpInteractive.silerA2 = silerA2;
            sdpInteractive.silerA3 = silerA3;
            sdpInteractive.silerB3 = silerB3;

            do
            {
                // Main menu
                Console.WriteLine();
                Console.WriteLine("1 = calculate optimal interbirth intervals (backward iteration)");
                Console.WriteLine("2 = calculate population distributions (forward iteration)");
                Console.WriteLine("3 = calculate stats");
                Console.WriteLine("4 = full model run");
                Console.Write("Please select an option (0 = quit): ");

                string input = Console.ReadLine();

                int option;

                try
                {
                    option = int.Parse(input);
                }
                catch (FormatException e)
                {
                    Console.WriteLine("Invalid option. Try again");
                    continue;
                }

                if (option == 0)
                {
                    done = true;
                    continue;
                }
                else if (option == 1)
                {
                    back();
                }
                else if (option == 2)
                {
                    forward();
                }
                else if (option == 3)
                {
                    stats();
                }
                else if (option == 4)
                {
                    back();
                    forward();
                    stats();
                }
                else
                {
                    Console.WriteLine("{0} is an invalid option.", option);
                }


            } while (!done);
        }
        #endregion

        #region Model run methods
        private static void back()
        {
            // calculate optimal birth intervals
            Console.WriteLine("-- beginning backward iteration");
            sdpInteractive.runBackward();
            Console.WriteLine("-- backward iteration finished");

            // output birth decisions and fitness here
            Console.WriteLine("-- writing birth decisions to disk");
            sdpInteractive.outputBirthDecisions();
            sdpInteractive.outputGrowthRate();
            Console.WriteLine("-- birth decisions written to disk");
        }

        private static void forward()
        {
            if (!sdpInteractive.finishedBackward)
                sdpInteractive.importBirthDecisionsFromFile();

            Console.WriteLine("-- beginning forward iteration");
            sdpInteractive.runForward();
            Console.WriteLine("-- forward iteration finished");

            Console.WriteLine("-- writing population data to disk");
            sdpInteractive.outputGrowthRate();
            Console.WriteLine("-- population data written to disk");
        }

        private static void stats()
        {
            if (!sdpInteractive.finishedForward)
            {
                sdpInteractive.importBirthDecisionsFromFile();
                // ask for forward iteration output file(s) - NFxxxx and Rxxxx
                sdpInteractive.importPopulationFromFile();
                sdpInteractive.importPopulationGrowth();
            }

            Console.WriteLine("-- calculating stats");
            sdpInteractive.calcStats();
            Console.WriteLine("-- calculated stats");

            Console.WriteLine("-- writing stats to disk");
            sdpInteractive.outputStats();
            Console.WriteLine("-- stats written to disk");
        }
        #endregion

        #region Parameter methods
        private static string PrintParams(MortalityParam mortMaternal, MortalityParam mortSibComp, MortalityParam mortSibHelp)
        {
            string strParams = "----" + System.Environment.NewLine + "Mortality parameters for this run:\n"
                + string.Format("Risk of mortality during childbirth: {0}\n", Enum.GetName(typeof(MortalityParam), mortMaternal))
                + string.Format("Sibling competition: {0}\n", Enum.GetName(typeof(MortalityParam), mortSibHelp))
                + string.Format("Juvenile help: {0}\n", Enum.GetName(typeof(MortalityParam), mortSibHelp))
                ;
            return strParams;
        }

        /// <summary>
        /// Parse string arguments passed to program. Return level of mortality parameter
        /// </summary>
        /// <param name="arg">String argument passed to model</param>
        /// <returns>MortalityParam: high, medium, low or none (default) </returns>
        private static MortalityParam parseArgs(string arg)
        {
            MortalityParam returnedValue = MortalityParam.None; // default to none

            switch (arg.ToUpper())
            {
                case "HIGH":
                    returnedValue = MortalityParam.High;
                    break;
                case "H":
                    returnedValue = MortalityParam.High;
                    break;

                case "MEDIUM":
                    returnedValue = MortalityParam.Medium;
                    break;
                case "M":
                    returnedValue = MortalityParam.Medium;
                    break;

                case "LOW":
                    returnedValue = MortalityParam.Low;
                    break;
                case "L":
                    returnedValue = MortalityParam.Low;
                    break;

                default:
                    returnedValue = MortalityParam.None;
                    break;
            }

            return returnedValue;
        }
        #endregion
    }

    enum MortalityParam
    {
        None,
        High,
        Medium,
        Low
    }
}
