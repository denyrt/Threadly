using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data.SqlTypes;
using Threadly.Application.Commentaries;
using Threadly.Domain.Commentaries;
using Threadly.Infrastructure.Persistence;
using Xunit;

namespace Threadly.Api.IntegrationTests.Commentaries;

public sealed partial class CommentsApiTests
{
    [Theory]
    [InlineData("date", "asc", "CreatedAtUtc")]
    [InlineData("date", "desc", "CreatedAtUtc")]
    [InlineData("username", "asc", "Username")]
    [InlineData("username", "desc", "Username")]
    [InlineData("email", "asc", "Email")]
    [InlineData("email", "desc", "Email")]
    public async Task GetPage_SortsAllRootsBeforePagingWithSqlIdTiesAndMetadata(
        string sortBy, string direction, string column)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        DateTime timestamp = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        List<Commentary> roots = [];
        await using (ThreadlyDbContext context = CreateContext())
        {
            for (int index = 0; index < 57; index++)
            {
                // Mix case and reverse the high GUID bytes to distinguish SQL order from .NET order.
                Commentary root = new(
                    (index % 2 == 0 ? "User" : "user") + index % 3,
                    (index % 2 == 0 ? "Email" : "email") + index % 4 + "@example.com",
                    [new TextContentBlock($"Root {index}")], timestamp.AddTicks(index % 5));
                context.Add(root);
                context.Entry(root).Property(value => value.Id).CurrentValue =
                    Guid.Parse($"{100 - index:x8}-0000-7000-8000-{index:x12}");
                roots.Add(root);
            }
            roots[0].AddAttachment("note.txt", "text/plain", "metadata only"u8.ToArray(), null, null);
            Commentary child = new("AAA", "aaa@example.com", [new TextContentBlock("Child")], timestamp, roots[0].Id);
            context.Add(child);
            context.Add(new Commentary("ZZZ", "zzz@example.com", [new TextContentBlock("Grandchild")], timestamp, child.Id));
            await context.SaveChangesAsync(token);
        }

        IOrderedEnumerable<Commentary> ascending = sortBy switch
        {
            "date" => roots.OrderBy(value => value.CreatedAtUtc),
            "username" => roots.OrderBy(value => value.Username, StringComparer.OrdinalIgnoreCase),
            _ => roots.OrderBy(value => value.Email, StringComparer.OrdinalIgnoreCase)
        };
        Guid[] expected = ascending.ThenBy(value => new SqlGuid(value.Id)).Select(value => value.Id).ToArray();
        if (direction == "desc") Array.Reverse(expected);

        List<CommentaryDto> actual = [];
        queries.Commands.Clear();
        for (int pageNumber = 1; pageNumber <= 4; pageNumber++)
        {
            CommentaryPage page = await ReadPageAsync(
                $"/api/comments?page={pageNumber}&sortBy={sortBy}&sortDirection={direction}");
            Assert.Equal(pageNumber, page.Page);
            Assert.Equal(25, page.PageSize);
            Assert.Equal(57, page.TotalCount);
            Assert.Equal(expected.Skip((pageNumber - 1) * 25).Take(25), page.Items.Select(value => value.Id));
            Assert.All(page.Items, value => Assert.Null(value.ParentId));
            actual.AddRange(page.Items);
        }
        Assert.Equal(expected, actual.Select(value => value.Id));
        Assert.Equal(57, actual.Select(value => value.Id).Distinct().Count());
        CommentaryDto withMetadata = actual.Single(value => value.Id == roots[0].Id);
        Assert.Equal(1, withMetadata.ReplyCount);
        Assert.Equal("note.txt", Assert.Single(withMetadata.Attachments).FileName);

        string suffix = direction == "desc" ? " DESC" : "";
        string[] pageSql = queries.Commands.Select(value => value.Sql).Where(sql => sql.Contains("OFFSET", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, pageSql.Length);
        Assert.All(pageSql, sql =>
        {
            Assert.Contains($"ORDER BY [c].[{column}]{suffix}, [c].[Id]{suffix}", sql);
            Assert.True(sql.IndexOf("ORDER BY", StringComparison.Ordinal) < sql.IndexOf("OFFSET", StringComparison.Ordinal));
            Assert.Contains("[c].[ParentId] IS NULL", sql);
            Assert.Contains("FETCH NEXT", sql);
            Assert.DoesNotContain("[a].[Content]", sql);
            Assert.DoesNotContain("LOWER(", sql);
            Assert.DoesNotContain("COLLATE", sql);
        });
        CommentaryPage distant = await ReadPageAsync(
            $"/api/comments?page=2147483647&sortBy={sortBy}&sortDirection={direction}");
        Assert.Empty(distant.Items);
        Assert.Equal(57, distant.TotalCount);
    }

    [Theory]
    [InlineData("sortBy", "")]
    [InlineData("sortBy", " ")]
    [InlineData("sortBy", "Date")]
    [InlineData("sortBy", "0")]
    [InlineData("sortBy", "1")]
    [InlineData("sortBy", "-1")]
    [InlineData("sortBy", "unknown")]
    [InlineData("sortBy", "date,username")]
    [InlineData("sortBy", "date; DROP TABLE Commentaries")]
    [InlineData("sortDirection", "")]
    [InlineData("sortDirection", " ")]
    [InlineData("sortDirection", "ASC")]
    [InlineData("sortDirection", "0")]
    [InlineData("sortDirection", "1")]
    [InlineData("sortDirection", "-1")]
    [InlineData("sortDirection", "descending")]
    [InlineData("sortDirection", "asc,desc")]
    public async Task GetPage_RejectsExplicitInvalidSortingBeforeQuerying(string parameter, string value)
    {
        queries.Commands.Clear();
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/comments?{parameter}={Uri.EscapeDataString(value)}", TestContext.Current.CancellationToken);
        ValidationProblemDetails problem = await ReadValidationProblemAsync(response);
        Assert.Contains(parameter, problem.Errors.Keys);
        Assert.Empty(queries.Commands);
    }

    [Fact]
    public async Task GetPage_DefaultsEachMissingSortParameterIndependently()
    {
        DateTime time = DateTime.UtcNow;
        await using (ThreadlyDbContext context = CreateContext())
        {
            context.AddRange(
                new Commentary("Zulu", "a@example.com", [new TextContentBlock("Older")], time),
                new Commentary("Alpha", "z@example.com", [new TextContentBlock("Newer")], time.AddSeconds(1)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        Assert.Equal(new[] { "Alpha", "Zulu" }, (await ReadPageAsync("/api/comments")).Items.Select(value => value.Username));
        Assert.Equal(new[] { "Zulu", "Alpha" }, (await ReadPageAsync("/api/comments?sortDirection=asc")).Items.Select(value => value.Username));
        Assert.Equal(new[] { "Zulu", "Alpha" }, (await ReadPageAsync("/api/comments?sortBy=username")).Items.Select(value => value.Username));
        Assert.Equal(new[] { "Alpha", "Zulu" }, (await ReadPageAsync("/api/comments?sortBy=email")).Items.Select(value => value.Username));
    }
}
