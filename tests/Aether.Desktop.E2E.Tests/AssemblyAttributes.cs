using Xunit;

// The reference-desktop tests drive one interactive Windows desktop and start real broker,
// shell and sample-app processes. Running two GUI scenarios concurrently would create
// nondeterministic window/pipe contention, so parallelization stays disabled for this
// Windows-only assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
