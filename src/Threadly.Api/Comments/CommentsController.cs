using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.Attachments;
using Threadly.Application.Commentaries.Content;
using Threadly.Application.Commentaries.CreateCommentary;
using Threadly.Application.Commentaries.GetCommentaries;
using Threadly.Application.Commentaries.GetCommentaryById;
using Threadly.Application.Commentaries.GetCommentaryReplies;

namespace Threadly.Api.Comments;

[ApiController]
[CommentaryValidationFilter]
[Route("api/comments")]
public sealed class CommentsController(
    CreateCommentaryUseCase createCommentary,
    GetCommentaryByIdUseCase getCommentaryById,
    GetCommentariesUseCase getCommentaries,
    GetCommentaryRepliesUseCase getCommentaryReplies,
    IAttachmentRepository attachmentRepository, IContentProcessor contentProcessor) : ControllerBase
{
    [HttpPost("preview")]
    [Consumes("application/json")]
    [RequestSizeLimit(ContentLimits.JsonFieldLength)]
    [ProducesResponseType<ProcessedContent>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<ProcessedContent> Preview([FromBody] PreviewCommentaryPayload payload) =>
        Ok(contentProcessor.Process(payload.Content));

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(AttachmentLimits.MaxRequestBytes)]
    [ProducesResponseType<CommentaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CommentaryDto>> Create(
        [FromBody] CreateCommentaryPayload payload, CancellationToken cancellationToken)
    {
        CommentaryDto commentary = await createCommentary.ExecuteAsync(payload.ToInput(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = commentary.Id }, commentary);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ReadUploadForm]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(AttachmentLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AttachmentLimits.MaxRequestBytes, ValueCountLimit = 14,
        ValueLengthLimit = ContentLimits.JsonFieldLength, KeyLengthLimit = 128, MultipartHeadersLengthLimit = 4096)]
    [ProducesResponseType<CommentaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CommentaryDto>> CreateWithAttachments(
        [FromForm] CreateCommentaryForm payload, CancellationToken cancellationToken)
    {
        // Reject unbound file fields too: no supplied attachment may be silently ignored.
        if (Request.Form.Files.Count != payload.Attachments.Count)
        {
            throw new CommentaryValidationException("attachments", "Submit files using the attachments field.");
        }
        CommentaryDto commentary = await createCommentary.ExecuteAsync(payload.ToInputWithAttachments(), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = commentary.Id }, commentary);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        AttachmentContent? attachment = await attachmentRepository.GetAsync(id, attachmentId, cancellationToken);
        if (attachment is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Attachment not found.");
        }
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return attachment.ContentType == "text/plain"
            ? File(attachment.Content, "text/plain; charset=utf-8", attachment.FileName)
            : File(attachment.Content, attachment.ContentType);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<CommentaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentaryDto>> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        CommentaryDto? commentary = await getCommentaryById.ExecuteAsync(id, cancellationToken);

        if (commentary is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Commentary not found.");
        }

        return Ok(commentary);
    }

    [HttpGet]
    [ProducesResponseType<CommentaryPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CommentaryPage>> GetPage(
        [FromQuery] GetCommentariesPayload payload, CancellationToken cancellationToken)
    {
        CommentaryPage page = await getCommentaries.ExecuteAsync(payload.ToInput(), cancellationToken);

        return Ok(page);
    }

    [HttpGet("{id}/replies")]
    [ProducesResponseType<CommentaryReplies>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentaryReplies>> GetReplies(
        [FromRoute] Guid id, [FromQuery] string? cursor, CancellationToken cancellationToken)
    {
        // Read the raw value so an explicitly empty cursor is rejected rather than treated as absent.
        cursor = Request.Query.TryGetValue("cursor", out Microsoft.Extensions.Primitives.StringValues values)
            ? values.ToString() : null;
        CommentaryReplies? replies = await getCommentaryReplies.ExecuteAsync(id, cursor, cancellationToken);
        return replies is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Commentary not found.")
            : Ok(replies);
    }
}
