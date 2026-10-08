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
        Assert.Equal(["content", "parent", "captcha", "attachments", "save"], calls);
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

    private static CreateCommentaryUseCase Create(List<string> calls, CaptchaVerification result = CaptchaVerification.Valid, bool parentExists = true) =>
        new(new Repository(calls, parentExists), TimeProvider.System, new Attachments(calls), new Content(calls), new Captcha(calls, result));

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

    private sealed class Attachments(List<string> calls) : IAttachmentProcessor
    {
        public Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(IReadOnlyList<AttachmentUpload> uploads, CancellationToken cancellationToken)
        { calls.Add("attachments"); return Task.FromResult<IReadOnlyList<ProcessedAttachment>>([]); }
    }

    private sealed class Repository(List<string> calls, bool parentExists) : ICommentaryRepository
    {
        public Task AddAsync(Commentary commentary, CancellationToken cancellationToken)
        { calls.Add("save"); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
        { calls.Add("parent"); return Task.FromResult(parentExists); }
        public Task<CommentaryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommentaryPage> GetPageAsync(int page, int pageSize, CommentarySortBy sortBy, CommentarySortDirection sortDirection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommentaryReplies> GetRepliesAsync(Guid parentId, ReplyCursor? cursor, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
