using System.Diagnostics.CodeAnalysis;

namespace BufferPool.Tests;

public sealed class LruReplacementStrategyTests
{
    [Fact]
    public async Task BumpAsync_MovesItemToFront()
    {
        // Arrange
        using var strategy = new LruReplacementStrategy<int>();

        // Act
        await strategy.BumpAsync(1, CancellationToken.None);
        await strategy.BumpAsync(2, CancellationToken.None);
        await strategy.BumpAsync(1, CancellationToken.None);

        // Assert
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);
        Assert.True(evicted);
        Assert.Equal(2, item);
    }

    [Fact]
    public async Task TryEvictAsync_EmptyStrategy_ReturnsNoEviction()
    {
        // Arrange
        using var strategy = new LruReplacementStrategy<int>();

        // Act
        var (evicted, item) = await strategy.TryEvictAsync(CancellationToken.None);

        // Assert
        Assert.False(evicted);
        Assert.Equal(default, item);
    }

    [Fact]
    public async Task TryEvictAsync_WithItem_RemovesSpecificItem()
    {
        // Arrange
        using var strategy = new LruReplacementStrategy<int>();
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

    [Fact]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP017:Prefer using", Justification = "this is for testing")]
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP016:Don't use disposed instance", Justification = "this is for testing")]
    public async Task Dispose_PreventsFurtherOperations()
    {
        // Arrange
        var strategy = new LruReplacementStrategy<int>();
        strategy.Dispose();

        // Act/Assert
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await strategy.BumpAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task BumpAsync_SameItemMultipleTimes_MaintainsOneInstance()
    {
        // Arrange
        using var strategy = new LruReplacementStrategy<int>();

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
    public async Task TryEvictAsync_EvictsInLruOrder()
    {
        // Arrange
        using var strategy = new LruReplacementStrategy<int>();
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
}
