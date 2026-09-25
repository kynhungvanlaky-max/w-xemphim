using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public class ApiResponseTests
{
    [Fact]
    public void Ok_SetsSuccessfulEnvelopeAndPreservesDataAndMessage()
    {
        var data = new object();

        var response = ApiResponse<object>.Ok(data, "created");

        Assert.True(response.Success);
        Assert.Same(data, response.Data);
        Assert.Equal("created", response.Message);
    }

    [Fact]
    public void Fail_SetsFailedEnvelopeWithoutData()
    {
        var response = ApiResponse<int>.Fail("invalid");

        Assert.False(response.Success);
        Assert.Equal("invalid", response.Message);
        Assert.Equal(0, response.Data);
    }

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(1, 5, 1)]
    [InlineData(5, 5, 1)]
    [InlineData(6, 5, 2)]
    [InlineData(12, 5, 3)]
    [InlineData(12, 0, 0)]
    [InlineData(12, -1, 0)]
    public void PagedResult_TotalPagesUsesCeilingAndGuardsInvalidPageSize(
        int totalItems,
        int pageSize,
        int expectedPages)
    {
        var result = new PagedResult<int>
        {
            TotalItems = totalItems,
            PageSize = pageSize
        };

        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Theory]
    [InlineData(1, 3, false, true)]
    [InlineData(2, 3, true, true)]
    [InlineData(3, 3, true, false)]
    [InlineData(4, 3, true, false)]
    public void PagedResult_NavigationFlagsReflectCurrentPage(
        int page,
        int totalPages,
        bool hasPrevious,
        bool hasNext)
    {
        var result = new PagedResult<int>
        {
            Page = page,
            PageSize = 10,
            TotalItems = totalPages * 10
        };

        Assert.Equal(hasPrevious, result.HasPreviousPage);
        Assert.Equal(hasNext, result.HasNextPage);
    }
}
