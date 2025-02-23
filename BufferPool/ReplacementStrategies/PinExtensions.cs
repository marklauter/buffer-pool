using static BufferPool.PageManager;

namespace BufferPool.ReplacementStrategies;

internal static class PinExtensions
{
    public static async Task<Pin> BumpAsync(this Pin pin, IReplacementStrategy<int> replacementStrategy, CancellationToken cancellationToken)
    {
        await replacementStrategy.BumpAsync(pin.PageId, cancellationToken);
        return pin;
    }
}
