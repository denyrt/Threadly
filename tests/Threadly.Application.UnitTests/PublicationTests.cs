using Microsoft.Extensions.Logging.Abstractions;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.Captcha;
using Threadly.Application.Commentaries.Content;
using Threadly.Application.Commentaries.CreateCommentary;
using Threadly.Domain.Commentaries;
using Xunit;

namespace Threadly.Application.UnitTests;

public sealed class PublicationTests
{
    [Theory]
    [InlineData(CaptchaVerification.Invalid)]
    [InlineData(CaptchaVerification.Unavailable)]
    public async Task FailedCaptchaNeverProcessesAttachmentsOrWrites(CaptchaVerification outcome)
    {
        List<string> calls = [];
        CreateCommentaryUseCase useCase = Create(calls, outcome);
        Exception? error = await Record.ExceptionAsync(() => useCase.ExecuteAsync(Input(), TestContext.Current.CancellationToken));
        if (outcome == CaptchaVerification.Unavailable) Assert.IsType<CaptchaUnavailableException>(error);
        else Assert.Equal("captchaToken", Assert.IsType<CommentaryValidationException>(error).Field);
        Assert.Equal(["content", "parent", "captcha"], calls);
    }

    [Fact]
    public async Task SuccessfulCaptchaPrecedesAttachmentProcessingAndWrite()
    {
        List<string> calls = [];
        await Create(calls).ExecuteAsync(Input(), TestContext.Current.CancellationToken);
        Assert.Equal(["content", "parent", "captcha", "attachments", "save", "publish"], calls);
    }

    [Fact]
    public async Task MissingParentIsRejectedBeforeSpendingToken()
    {
        List<string> calls = [];
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Create(calls, parentExists: false).ExecuteAsync(Input(), TestContext.Current.CancellationToken));
        Assert.Equal(["content", "parent"], calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2049)]
    public async Task InvalidTokenShapeIsRejectedBeforeAnyProcessing(int length)
    {
        List<string> calls = [];
        await Assert.ThrowsAsync<CommentaryValidationException>(() => Create(calls).ExecuteAsync(Input() with { CaptchaToken = new string('x', length) }, TestContext.Current.CancellationToken));
        Assert.Empty(calls);
        Assert.DoesNotContain("test-token", Input().ToString());
    }

    private static CreateCommentaryInput Input() => new("Reader", "reader@example.com", [new("text", "hello")], "test-token", Guid.NewGuid(),
        [new AttachmentUpload("notes.txt", 5, () => throw new InvalidOperationException("Must not open the upload before verification."))]);

    private static CreateCommentaryUseCase Create(List<string> calls, CaptchaVerification result = CaptchaVerification.Valid,
        bool parentExists = true, string? failure = null) =>
        new(new Repository(calls, parentExists, failure), TimeProvider.System, new Attachments(calls, failure), new Content(calls),
            new Captcha(calls, result), new Publisher(calls, failure), NullLogger<CreateCommentaryUseCase>.Instance);

    [Theory]
    [InlineData("save")]
    [InlineData("attachments")]
    public async Task FailureBeforeCommitNeverPublishes(string failure)
    {
        List<string> calls = [];
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(calls, failure: failure).ExecuteAsync(Input(), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("publish", calls);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("timeout")]
    public async Task DeliveryFailureDoesNotFailCommittedCreate(string failure)
    {
        List<string> calls = [];
        CommentaryDto result = await Create(calls, failure: failure).ExecuteAsync(Input(), TestContext.Current.CancellationToken);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(["content", "parent", "captcha", "attachments", "save", "publish"], calls);
    }

    private sealed class Publisher(List<string> calls, string? failure) : ICommentCreatedPublisher
    {
        public async Task PublishAsync(CommentCreated notification, CancellationToken cancellationToken)
        {
            Assert.Equal("save", calls[^1]);
            Assert.NotEqual(Guid.Empty, notification.EventId);
            Assert.NotEqual(Guid.Empty, notification.CommentId);
            calls.Add("publish");
            if (failure == "publish") throw new InvalidOperationException("delivery failed");
            if (failure == "timeout") await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class Captcha(List<string> calls, CaptchaVerification result) : ICaptchaVerifier
    {
        public Task<CaptchaVerification> VerifyAsync(string token, CancellationToken cancellationToken)
        { calls.Add("captcha"); return Task.FromResult(result); }
    }

    private sealed class Content(List<string> calls) : IContentProcessor
    {
        public ProcessedContent Process(IReadOnlyList<ContentBlockInput>? content)
        { calls.Add("content"); return new([new TextContentBlock("hello")], 5, 2000); }
    }

    private sealed class Attachments(List<string> calls, string? failure) : IAttachmentProcessor
    {
        public Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(IReadOnlyList<AttachmentUpload> uploads, CancellationToken cancellationToken)
        { calls.Add("attachments"); if (failure == "attachments") throw new InvalidOperationException(); return Task.FromResult<IReadOnlyList<ProcessedAttachment>>([]); }
    }

    private sealed class Repository(List<string> calls, bool parentExists, string? failure) : ICommentaryRepository
    {
        public Task AddAsync(Commentary commentary, CancellationToken cancellationToken)
        { calls.Add("save"); if (failure == "save") throw new InvalidOperationException(); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
        { calls.Add("parent"); return Task.FromResult(parentExists); }
        public Task<IReadOnlyList<ReplyCountDto>> GetReplyCountsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommentaryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommentaryPage> GetPageAsync(int page, int pageSize, CommentarySortBy sortBy, CommentarySortDirection sortDirection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommentaryReplies> GetRepliesAsync(Guid parentId, ReplyCursor? cursor, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
