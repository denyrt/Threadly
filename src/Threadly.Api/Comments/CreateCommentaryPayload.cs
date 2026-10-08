using System.ComponentModel.DataAnnotations;
using Threadly.Application.Commentaries.Content;
using Threadly.Application.Commentaries.CreateCommentary;
using Threadly.Domain.Commentaries;

namespace Threadly.Api.Comments;

public class CommentaryIdentityPayload : IValidatableObject
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(Commentary.MaxUsernameLength, ErrorMessage = "Username cannot exceed {1} characters.")]
    [RegularExpression("^[a-zA-Z0-9]+$", ErrorMessage = "Username can contain only ASCII letters and digits.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [StringLength(Commentary.MaxEmailLength, ErrorMessage = "Email cannot exceed {1} characters.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string Email { get; init; } = string.Empty;

    public Guid? ParentId { get; init; }

    [Required(ErrorMessage = "Complete verification before posting.")]
    [StringLength(2048, ErrorMessage = "Verification token cannot exceed {1} characters.")]
    public string CaptchaToken { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ParentId == Guid.Empty)
        {
            yield return new ValidationResult("Parent ID must not be empty.", ["parentId"]);
        }
    }

}

public sealed class CreateCommentaryPayload : CommentaryIdentityPayload
{
    [Required(ErrorMessage = "Content is required.")]
    public IReadOnlyList<ContentBlockInput>? Content { get; init; }

    public CreateCommentaryInput ToInput() => new(Username, Email, Content, CaptchaToken, ParentId);
}
