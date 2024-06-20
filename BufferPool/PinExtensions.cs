using static BufferPool.PageManager;

namespace BufferPool;

internal static class PinExtensions
{
    public static Pin Bump(this Pin pin, IReplacementPolicy<int> evictionPolicy)
    {
        evictionPolicy.Bump(pin.PageId);
        return pin;
    }
}
