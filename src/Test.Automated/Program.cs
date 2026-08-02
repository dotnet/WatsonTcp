using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Test.Shared;
using Touchstone.Core;
using Touchstone.Cli;

namespace Test.Automated
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string resultsPath = null;
            string suiteId = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--results", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    resultsPath = args[i + 1];
                    string resultsDirectory = Path.GetDirectoryName(resultsPath);
                    if (!String.IsNullOrEmpty(resultsDirectory))
                    {
                        Directory.CreateDirectory(resultsDirectory);
                    }

                    i++;
                }
                else if (String.Equals(args[i], "--suite", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    suiteId = args[i + 1];
                    i++;
                }
            }

            IReadOnlyList<TestSuiteDescriptor> suites = WatsonTcpSuites.WithId(suiteId);

            return await ConsoleRunner.RunAsync(suites, resultsPath: resultsPath).ConfigureAwait(false);
        }
    }
}
