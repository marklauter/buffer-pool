using BufferPool.ReplacementStrategies;
using System.Diagnostics.CodeAnalysis;

namespace BufferPool.Tests;

public sealed class ReplacementStrategyTests
{
    public static TheoryData<string> StrategyKeys => ["defaultLRU", "optimizedLRU"];

    private static IReplacementStrategy<int> GetStrategy(string strategyKey) =>
        strategyKey switch
        {
            "defaultLRU" => new DefaultLruReplacementStrategy<int>(),
            "optimizedLRU" => new OptimizedLruReplacementStrategy<int>(),
            _ => throw new ArgumentOutOfRangeException(strategyKey)
        };

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task BumpAsync_MovesItemToFront(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);

        // Act
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);
        await strategy.BumpAsync(1, CancellationToken.None);

        // Assert
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(2, item);
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task TryEvictAsync_EmptyStrategy_ReturnsNoEviction(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);

        // Act
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);

        // Assert
        Assert.False(evicted);
        Assert.Equal(default, item);
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task TryEvictAsync_WithItem_RemovesSpecificItem(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);

        // Act
        var removed = await strategy.TryEvictAsync(1, CancellationToken.None);

        // Assert
        Assert.True(removed);
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(2, item);
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP017:Prefer using", Justification = "this is for testing")]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP016:Don't use disposed instance", Justification = "this is for testing")]
    public async Task Dispose_PreventsFurtherOperations(string strategyKey)
    {
        // Arrange
        var strategy = GetStrategy(strategyKey);
        strategy.Dispose();

        // Act/Assert
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await strategy.BumpAsync(1, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task BumpAsync_SameItemMultipleTimes_MaintainsOneInstance(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);

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

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task TryEvictAsync_EvictsInLruOrder(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);
        await strategy.BumpAsync(3, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None); // Move 2 to front

        // Act & Assert
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(1, item); // Least recently used

        (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(3, item); // Second least recently used

        (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(2, item); // Most recently used
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task TryEvictAsync_LeastRecentlyUsedItem(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);
        await strategy.BumpAsync(3, CancellationToken.None);

        // Act
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);

        // Assert
        Assert.True(evicted);
        Assert.Equal(1, item); // Least recently used item
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task ConcurrentBump(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);
        var tasks = new Task[100];

        // Act
        for (var key = 0; key < 100; key++)
        {
            tasks[key] = strategy.BumpAsync(key, CancellationToken.None).AsTask();
        }

        await Task.WhenAll(tasks);

        // Assert
        var keys = new int[100];
        for (var i = 0; i < 100; i++)
        {
            if (await strategy.TryEvictAsync(CancellationToken.None) is (true, var key))
                keys[i] = key;
        }

        for (var i = 0; i < 100; i++)
        {
            Assert.True(keys.Contains(i), $"key not found: {i}");
        }
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task ConcurrentEvict(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);
        for (var key = 0; key < 100; key++)
        {
            await strategy.BumpAsync(key, CancellationToken.None);
        }

        // Act
        var tasks = new Task<(bool wasEvicted, int key)>[100];
        for (var i = 0; i < 100; i++)
        {
            tasks[i] = strategy.TryEvictAsync(CancellationToken.None).AsTask();
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var evictedItems = results
            .Where(result => result.wasEvicted)
            .Select(result => result.key)
            .ToArray();

        Assert.Equal(100, evictedItems.Length);
        for (var key = 0; key < 100; key++)
        {
            Assert.Contains(key, evictedItems);
        }
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task BumpAsync_AddsNewItem(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);

        // Act
        await strategy.BumpAsync(1, CancellationToken.None);

        // Assert
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(1, item);
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task TryEvictAsync_EvictsMostRecentlyUsedItem(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);
        await strategy.BumpAsync(3, CancellationToken.None);

        // Act
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);

        // Assert
        Assert.True(evicted);
        Assert.NotEqual(3, item); // Most recently used item should not be evicted
    }

    [Theory]
    [MemberData(nameof(StrategyKeys))]
    public async Task BumpAsync_UpdatesExistingItem(string strategyKey)
    {
        // Arrange
        using var strategy = GetStrategy(strategyKey);
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);

        // Act
        await strategy.BumpAsync(1, CancellationToken.None);

        // Assert
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(2, item); // Item 1 should be at the front, so item 2 should be evicted first
    }
}

