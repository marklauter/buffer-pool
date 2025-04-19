using static BufferPool.PageManager;

namespace BufferPool.ReplacementStrategies;

internal static class PinExtensions
{
    public static Pin Touch(this Pin pin, IReplacementStrategy<int> replacementStrategy)
    {
        replacementStrategy.Touch(pin.PageId);
        return pin;
    }
}
