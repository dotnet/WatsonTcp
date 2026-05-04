namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Touchstone.Core;

    public static class WatsonTcpSuites
    {
        private static readonly string[] _AuthorizationScenarioNames =
        {
            nameof(WatsonTcpScenarios.AuthorizeConnectionAllow),
            nameof(WatsonTcpScenarios.AuthorizeConnectionReject),
            nameof(WatsonTcpScenarios.AuthorizeConnectionTimeoutReject),
            nameof(WatsonTcpScenarios.AuthorizeConnectionExceptionReject),
            nameof(WatsonTcpScenarios.AuthorizationAndHandshakeTimeoutSettings),
            nameof(WatsonTcpScenarios.AuthorizationStateMachineApiKeySuccess),
            nameof(WatsonTcpScenarios.AuthorizationStateMachineApiKeyFailure),
            nameof(WatsonTcpScenarios.AuthorizationStateMachineChallengeResponseSuccess),
            nameof(WatsonTcpScenarios.AuthorizationStateMachineChallengeResponseFailure),
            nameof(WatsonTcpScenarios.AuthorizationStateMachineMissingClientCallbackFailure),
            nameof(WatsonTcpScenarios.AuthorizationStateMachineTimeoutFailure),
            nameof(WatsonTcpScenarios.AuthorizationStateMachineServerExceptionFailure),
            nameof(WatsonTcpScenarios.AuthorizationStateMachineClientFailure)
        };

        private static readonly IReadOnlyList<TestSuiteDescriptor> _All = BuildSuites();

        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return _All; }
        }

        private static IReadOnlyList<TestSuiteDescriptor> BuildSuites()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(
                    "regression",
                    "WatsonTcp Regression",
                    BuildCases(excludeAuthorizationScenarios: true)),
                new TestSuiteDescriptor(
                    "auth-handshake",
                    "Authorization And Handshake",
                    BuildCases(excludeAuthorizationScenarios: false, includeOnlyAuthorizationScenarios: true))
            };

            return suites;
        }

        private static IReadOnlyList<TestCaseDescriptor> BuildCases(bool excludeAuthorizationScenarios = false, bool includeOnlyAuthorizationScenarios = false)
        {
            IEnumerable<MethodInfo> methods = typeof(WatsonTcpScenarios)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(void) || m.ReturnType == typeof(System.Threading.Tasks.Task))
                .Where(m => m.GetParameters().Length == 0);

            if (excludeAuthorizationScenarios)
            {
                methods = methods.Where(m => !_AuthorizationScenarioNames.Contains(m.Name, StringComparer.Ordinal));
            }

            if (includeOnlyAuthorizationScenarios)
            {
                methods = methods.Where(m => _AuthorizationScenarioNames.Contains(m.Name, StringComparer.Ordinal));
            }

            return methods
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => new TestCaseDescriptor(
                    includeOnlyAuthorizationScenarios ? "auth-handshake" : "regression",
                    m.Name,
                    ToDisplayName(m.Name),
                    token =>
                    {
                        try
                        {
                            object result = m.Invoke(null, null);
                            if (result is System.Threading.Tasks.Task task) return task;
                            return System.Threading.Tasks.Task.CompletedTask;
                        }
                        catch (TargetInvocationException e) when (e.InnerException != null)
                        {
                            return System.Threading.Tasks.Task.FromException(e.InnerException);
                        }
                    }))
                .ToList();
        }

        private static string ToDisplayName(string methodName)
        {
            if (String.IsNullOrEmpty(methodName)) return methodName;

            List<char> chars = new List<char>(methodName.Length + 8);
            chars.Add(methodName[0]);

            for (int i = 1; i < methodName.Length; i++)
            {
                char current = methodName[i];
                if (Char.IsUpper(current) && !Char.IsUpper(methodName[i - 1]))
                {
                    chars.Add(' ');
                }

                chars.Add(current);
            }

            return new string(chars.ToArray());
        }
    }
}
