using System.Threading;

// Compile under the desired executable name. No model, UI, child processes or file writes.
internal static class ProcessFixture
{
    private static void Main()
    {
        Thread.Sleep(600000);
    }
}
