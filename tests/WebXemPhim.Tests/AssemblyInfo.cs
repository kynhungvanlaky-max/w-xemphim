using Xunit;

// WatchlistService intentionally uses an in-memory static store. Running its tests in
// parallel would make otherwise isolated API scenarios race with one another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
