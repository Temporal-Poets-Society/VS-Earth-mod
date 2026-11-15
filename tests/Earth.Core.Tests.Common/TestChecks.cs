using System;

namespace EarthMap.Tests
{
    /// <summary>
    /// EA-0088: the one check-and-summary helper for the offline tests (the projects each wrote their own Check, in three
    /// behaviours). Counts every check, prints the failures as they come ("FAIL what"), and gives the exit code and the
    /// closing line. New tests use it; older ones move onto it when they are next changed.
    /// </summary>
    public sealed class TestChecks
    {
        public int Count { get; private set; }
        public int Failures { get; private set; }
        readonly string name;

        public TestChecks(string name) { this.name = name; }

        /// <summary>One check; false is printed as a failure and counted.</summary>
        public bool Check(bool ok, string what)
        {
            Count++;
            if (!ok) { Failures++; Console.WriteLine("FAIL " + what); }
            return ok;
        }

        /// <summary>Prints the closing line and returns the process's exit code (0 when every check passed).</summary>
        public int Summary()
        {
            Console.WriteLine(Failures == 0 ? $"{name}: {Count} checks passed" : $"{name}: {Failures} of {Count} checks failed");
            return Failures == 0 ? 0 : 1;
        }
    }
}
