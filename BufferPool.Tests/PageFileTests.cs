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
        Assert.NotNull(pageFile);
    }

    [Fact]
    public void GrowTest()
    {
        using var pageFile = new PageFile("pages.db");
        var size = pageFile.FileSize;
        Assert.Equal(size * 2, pageFile.Grow());
    }
}
