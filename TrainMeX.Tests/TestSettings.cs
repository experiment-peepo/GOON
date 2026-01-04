using Xunit;

// Disable parallel test execution because several tests rely on static state (Logger, ServiceContainer)
// and concurrent execution can cause flaky failures.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
