namespace Test.NUnit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using global::NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    [NonParallelizable]
    public sealed class WatsonTcpNUnitTests
    {
        public static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(WatsonTcpSuites.All);
        }

        [TestCaseSource(nameof(TestCases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
