using BufferPool.MM;

namespace BufferPool.Tests;

public sealed class PageFileTests
{
    [Fact]
    public void CtorTest()
    {
        using var pageFile = new PageFile("pages.db");
        Assert.NotNull(pageFile);
    }

    [Fact]
    public void LeaseTest()
    {
        using var pageFile = new PageFile("pages.db");
        var lease = pageFile.LeasePage(0);
        Assert.Equal(4 * 1024, lease.Page.Length);
        lease.Release();
    }

    [Fact]
    public void WriteTest()
    {
        using var pageFile = new PageFile("pages.db");
        var lease = pageFile.LeasePage(0);
        lease.Page[0] = 255;
        lease.Flush();
        lease.Release();

        lease = pageFile.LeasePage(0);
        lease.Release();
    }

    [Fact]
    public void GrowTest()
    {
        using var pageFile = new PageFile("pages.db");
        var size = pageFile.FileSize;
        Assert.Equal(size * 2, pageFile.Grow());
    }
}
