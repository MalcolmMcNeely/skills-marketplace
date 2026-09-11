using Harness;

namespace Harness.Free.Tests;

/// <summary>
/// The suite these tests measure, discovered once.
///
/// #26 gathered csharp-new-class into one folder so that naming it is the only thing a test does.
/// Naming it in every test class would have moved the duplication rather than removed it, and a
/// name typed into a dozen files is the fault the ticket set out to fix.
/// </summary>
internal static class UnderTest
{
    internal static readonly DiscoveredSuite CsharpNewClass =
        SuiteDiscovery.One(new HarnessPaths(), "csharp-new-class");
}
