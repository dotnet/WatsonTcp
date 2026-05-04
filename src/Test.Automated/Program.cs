using System;
using System.Threading.Tasks;
using Test.Shared;
using Touchstone.Cli;

namespace Test.Automated
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string resultsPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--results", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    resultsPath = args[i + 1];
                    i++;
                }
            }

            return await ConsoleRunner.RunAsync(WatsonTcpSuites.All, resultsPath: resultsPath).ConfigureAwait(false);
        }
    }
}
