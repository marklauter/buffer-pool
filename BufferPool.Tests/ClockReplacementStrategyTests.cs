using BufferPool.ReplacementStrategies;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool.Tests;

public sealed class ClockReplacementStrategyTests
{
    [Fact]
    public async Task BumpAsync_AddsNewItem()
    {
        // Arrange
        using var strategy = new ClockReplacementStrategy<int>();

        // Act
        await strategy.BumpAsync(1, CancellationToken.None);

        // Assert
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(1, item);
    }

    [Fact]
    public async Task BumpAsync_SameItemMultipleTimes_MaintainsOneInstance()
    {
        // Arrange
        using var strategy = new ClockReplacementStrategy<int>();

        // Act
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(1, CancellationToken.None);

        // Assert
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(1, item);

        (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.False(evicted);
        Assert.Equal(default, item);
    }

    [Fact]
    public async Task TryEvictAsync_EmptyStrategy_ReturnsNoEviction()
    {
        // Arrange
        using var strategy = new ClockReplacementStrategy<int>();

        // Act
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);

        // Assert
        Assert.False(evicted);
        Assert.Equal(default, item);
    }

    [Fact]
    public async Task TryEvictAsync_GivesSecondChance()
    {
        // Arrange
        using var strategy = new ClockReplacementStrategy<int>();

        // Add three pages
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);
        await strategy.BumpAsync(3, CancellationToken.None);

        // Reference page 2 again to ensure its reference bit is set
        await strategy.BumpAsync(2, CancellationToken.None);

        // First eviction should pick the first unreferenced page it finds
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(1, item); // Should evict 1 as it's unreferenced

        // Reference page 3 again
        await strategy.BumpAsync(3, CancellationToken.None);

        // Second eviction
        (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(2, item); // Should evict 2

        // Final eviction
        (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(3, item); // Should evict 3
    }

    [Fact]
    public async Task TryEvictAsync_SpecificKey_RemovesCorrectItem()
    {
        // Arrange
        using var strategy = new ClockReplacementStrategy<int>();
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);
        await strategy.BumpAsync(3, CancellationToken.None);

        // Act
        var removed = await strategy.TryEvictAsync(2, CancellationToken.None);

        // Assert
        Assert.True(removed);
        var items = new HashSet<int>();
        while (await strategy.TryEvictAsync(CancellationToken.None) is (true, var key))
        {
            _ = items.Add(key);
        }

        Assert.Contains(1, items);
        Assert.Contains(3, items);
        Assert.DoesNotContain(2, items);
    }

    [Fact]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP017:Prefer using", Justification = "this is for testing")]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP016:Don't use disposed instance", Justification = "this is for testing")]
    public async Task Dispose_PreventsFurtherOperations()
    {
        // Arrange
        var strategy = new ClockReplacementStrategy<int>();
        strategy.Dispose();

        // Act/Assert
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await strategy.BumpAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentOperations_MaintainsConsistency()
    {
        // Arrange
        using var strategy = new ClockReplacementStrategy<int>();
        var tasks = new Task[100];

        // Act - Concurrent bumps
        for (var i = 0; i < 100; i++)
        {
            tasks[i] = strategy.BumpAsync(i, CancellationToken.None).AsTask();
        }

        await Task.WhenAll(tasks);

        // Assert - All items should be present
        var evictedItems = new HashSet<int>();
        while (await strategy.TryEvictAsync(CancellationToken.None) is (true, var item))
        {
            _ = evictedItems.Add(item);
        }

        Assert.Equal(100, evictedItems.Count);
        for (var i = 0; i < 100; i++)
        {
            Assert.Contains(i, evictedItems);
        }
    }

    [Fact]
    public async Task TryEvictAsync_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        using var strategy = new ClockReplacementStrategy<int>();
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);

        // Act
        var removed = await strategy.TryEvictAsync(999, CancellationToken.None);

        // Assert
        Assert.False(removed);
    }
}
