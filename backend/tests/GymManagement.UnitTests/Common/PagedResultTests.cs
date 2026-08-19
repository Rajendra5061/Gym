using FluentAssertions;
using GymManagement.Application.Common;
using Xunit;

namespace GymManagement.UnitTests.Common;

/// <summary>
/// Paging arithmetic and the server side page-size clamp that case Z-08 relies on.
/// </summary>
public class PagedResultTests
{
    private static PagedResult<string> Page(int totalCount, int pageNumber, int pageSize) =>
        new(new[] { "row" }, totalCount, pageNumber, pageSize);

    // ------------------------------------------------------------ TotalPages

    [Theory(DisplayName = "TotalPages rounds the row count up to a whole number of pages")]
    [InlineData(0, 25, 0)]
    [InlineData(1, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(50, 25, 2)]
    [InlineData(51, 25, 3)]
    [InlineData(199, 200, 1)]
    [InlineData(201, 200, 2)]
    public void TotalPages_RoundsUp(int totalCount, int pageSize, int expected)
    {
        Page(totalCount, 1, pageSize).TotalPages.Should().Be(expected);
    }

    [Fact(DisplayName = "TotalPages is zero rather than a divide-by-zero when the page size is zero")]
    public void TotalPages_WithZeroPageSize_IsZero()
    {
        var result = new PagedResult<string> { TotalCount = 10, PageSize = 0, PageNumber = 1 };

        result.TotalPages.Should().Be(0);
    }

    // ---------------------------------------------------------- navigation

    [Fact(DisplayName = "The first page has no previous page")]
    public void HasPreviousPage_OnTheFirstPage_IsFalse()
    {
        Page(100, 1, 25).HasPreviousPage.Should().BeFalse();
    }

    [Fact(DisplayName = "A later page has a previous page")]
    public void HasPreviousPage_OnALaterPage_IsTrue()
    {
        Page(100, 2, 25).HasPreviousPage.Should().BeTrue();
    }

    [Fact(DisplayName = "A page before the last one has a next page")]
    public void HasNextPage_BeforeTheLastPage_IsTrue()
    {
        Page(100, 3, 25).HasNextPage.Should().BeTrue();
    }

    [Fact(DisplayName = "The last page has no next page")]
    public void HasNextPage_OnTheLastPage_IsFalse()
    {
        Page(100, 4, 25).HasNextPage.Should().BeFalse();
    }

    [Fact(DisplayName = "An empty result has neither a previous nor a next page")]
    public void EmptyResult_HasNoNavigation()
    {
        var result = PagedResult<string>.Empty();

        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeFalse();
        result.Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "A page number beyond the last page reports no next page")]
    public void HasNextPage_BeyondTheLastPage_IsFalse()
    {
        Page(10, 99, 25).HasNextPage.Should().BeFalse();
    }

    // ----------------------------------------------------- PagedRequest clamp

    [Fact(DisplayName = "Z-08 A page size above 200 is clamped to 200")]
    public void PageSize_AboveTheCeiling_IsClampedTo200()
    {
        var request = new PagedRequest { PageSize = 100_000 };

        request.PageSize.Should().Be(200);
    }

    [Fact(DisplayName = "Z-08 A page size of exactly 200 is accepted unchanged")]
    public void PageSize_AtTheCeiling_IsUnchanged()
    {
        new PagedRequest { PageSize = 200 }.PageSize.Should().Be(200);
    }

    [Theory(DisplayName = "Z-08 A page size below 1 falls back to the default of 25")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void PageSize_BelowOne_FallsBackToTheDefault(int pageSize)
    {
        new PagedRequest { PageSize = pageSize }.PageSize.Should().Be(25);
    }

    [Fact(DisplayName = "The default page size is 25")]
    public void PageSize_Default_Is25()
    {
        new PagedRequest().PageSize.Should().Be(25);
    }

    [Theory(DisplayName = "A page number below 1 is clamped to the first page")]
    [InlineData(0)]
    [InlineData(-5)]
    public void PageNumber_BelowOne_IsClampedToOne(int pageNumber)
    {
        new PagedRequest { PageNumber = pageNumber }.PageNumber.Should().Be(1);
    }

    [Fact(DisplayName = "Skip is derived from the clamped page number and page size")]
    public void Skip_UsesTheClampedValues()
    {
        var request = new PagedRequest { PageNumber = 3, PageSize = 100_000 };

        request.Skip.Should().Be(400); // (3 - 1) * 200
    }

    [Fact(DisplayName = "Skip is zero on the first page")]
    public void Skip_OnTheFirstPage_IsZero()
    {
        new PagedRequest { PageNumber = 1, PageSize = 50 }.Skip.Should().Be(0);
    }
}
