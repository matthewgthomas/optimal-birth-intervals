optimal-birth-intervals
=======================

[![DOI](https://zenodo.org/badge/doi/10.5281/zenodo.15663.svg)](http://dx.doi.org/10.5281/zenodo.15663)

The optimal birth intervals model is written in C#. To run the code, first download Visual Studio Express from http://go.microsoft.com/fwlink/?linkid=244366

Running the model
-----------------
The model has three modes:
(i) 'interactive mode' lets you choose mortality parameters and run the backward iteration, forward simulation and/or calculate model statistics.
(ii) 'batch mode' lets you enter mortality parameters at the command line.
(iii) 'multi-thread mode' runs several instances of the model in parallel.

Interactive mode
----------------
When you run the executable without any command line parameters, the model will ask you to input intensities (none, low, medium or high) for maternal mortality, sibling competition and juvenile help. You will then be asked to enter five Siler mortality parameters. Finally, choose from the following options:

Press 1 to just run the backward iteration, calculating optimal birth policies
Press 2 to project an optimal birth policy forward in time
Press 3 to calculate IBIs from a population projection matrix
Press 4 to run the three steps above at once

Press 0 to quit the program

Batch mode
----------
From the command line run ibi.exe followed by the following parameters:
1. The name of the population (for output files)
2. The level of maternal mortality (N, L, M or H)
3. The level of sibling competition (N, L, M or H)
4. The level of juvenile help (N, L, M or H)
5. to 9. Siler parameters

E.g. ibi.exe Taiwan n n n 0.226529 1.57375 0.01 0.000841 0.085

Multi-thread mode
-----------------
To perform several runs of the model at once, enter a series of mortality parametrs into a text file (call it whatever you like) in the same format as the command line arguments in batch mode. For example, the file could contain the following two lines:

Taiwan n n n 0.226529 1.57375 0.01 0.000841 0.085
Taiwan h h h 0.226529 1.57375 0.01 0.000841 0.085

Then run the model as follows: ibi.exe [name of parameters file]

Output files
------------
Output files will be named in the form [type]-[population]-XYZ.out (or .csv) where:
- [type] will be Births, IB, R or R-Lambda
- [population] is the name of the population you entered earlier
- X, Y and Z are, respectively, the levels of maternal mortality, sibling competition and juvenile help (N, L, M or H)

'Births' contains the set of optimal birth decisions for each age and family structure
'IB' contains birth intervals
'R' and 'R-Lambda' contain population growth rate

Structure of the code
---------------------
The code is split over five files:
- Program.cs is the main entry point for the program and contains user interface code
- BirthIntervals.cs contains input/output methods for the model as well as internal functions
- BackwardIteration.cs calculates the optimal birth decisions
- ForwardIteration.cs is the population projection code
- PowerSet.cs contains a function to enumerate the powerset of an array (used for state transitions between family structures)

Analysing the model's output
----------------------------
Analysis code (written in R) is available from https://github.com/matthewgthomas/optimal-birth-intervals-stats
