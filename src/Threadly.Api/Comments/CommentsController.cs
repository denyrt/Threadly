using Microsoft.AspNetCore.Mvc;
using Threadly.Application.Commentaries;
using Threadly.Application.Commentaries.CreateCommentary;
using Threadly.Application.Commentaries.GetCommentaries;
using Threadly.Application.Commentaries.GetCommentaryById;

namespace Threadly.Api.Comments;

[ApiController]
[Route("api/comments")]
public sealed class CommentsController(
    CreateCommentaryUseCase createCommentary,
    GetCommentaryByIdUseCase getCommentaryById,
    GetCommentariesUseCase getCommentaries) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CommentaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CommentaryDto>> Create(
        [FromBody] CreateCommentaryPayload payload, CancellationToken cancellationToken)
    {
        CommentaryDto commentary = await createCommentary.ExecuteAsync(payload.ToInput(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = commentary.Id }, commentary);
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
}
