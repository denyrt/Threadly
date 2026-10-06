using System.ComponentModel.DataAnnotations;
using Threadly.Application.Commentaries.CreateCommentary;
using Threadly.Domain.Commentaries;

namespace Threadly.Api.Comments;

public sealed class CreateCommentaryPayload : IValidatableObject
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(Commentary.MaxUsernameLength, ErrorMessage = "Username cannot exceed {1} characters.")]
    [RegularExpression("^[a-zA-Z0-9]+$", ErrorMessage = "Username can contain only ASCII letters and digits.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [StringLength(Commentary.MaxEmailLength, ErrorMessage = "Email cannot exceed {1} characters.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Text is required.")]
    [StringLength(Commentary.MaxTextLength, ErrorMessage = "Text cannot exceed {1} characters.")]
    public string Text { get; init; } = string.Empty;

    public Guid? ParentId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ParentId == Guid.Empty)
        {
            yield return new ValidationResult("Parent ID must not be empty.", ["parentId"]);
        }
    }

    public CreateCommentaryInput ToInput()
    {
        return new CreateCommentaryInput(Username, Email, Text, ParentId);
    }
}
