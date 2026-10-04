using Threadly.Domain.Commentaries;

namespace Threadly.Application.Commentaries;

public interface ICommentaryRepository
{
    Task AddAsync(Commentary commentary, CancellationToken cancellationToken);
    Task<CommentaryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<CommentaryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);
}
