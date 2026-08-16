using System.ComponentModel.DataAnnotations;

namespace SaveNEIN.Shared;

public class CoalitionSignupRequest : IValidatableObject
{
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100, ErrorMessage = "First name is too long.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100, ErrorMessage = "Last name is too long.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Must be a valid email address.")]
    [StringLength(254, ErrorMessage = "Email is too long.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address Line 1 is required.")]
    [StringLength(200, ErrorMessage = "Address Line 1 is too long.")]
    public string AddressLine1 { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Address Line 2 is too long.")]
    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = "City is required.")]
    [StringLength(100, ErrorMessage = "City is too long.")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "State/Province is required.")]
    [StringLength(50, ErrorMessage = "State/Province is too long.")]
    public string StateProvince { get; set; } = string.Empty;

    [Required(ErrorMessage = "ZIP/Postal Code is required.")]
    [StringLength(20, ErrorMessage = "ZIP/Postal Code is too long.")]
    public string PostalCode { get; set; } = string.Empty;

    // Checkboxes
    public bool DisplayYardSign { get; set; }
    public bool WorkEventBooth { get; set; }
    public bool GoDoorToDoor { get; set; }
    public bool WriteLetterToEditor { get; set; }
    public bool ShareSocialMedia { get; set; }
    public bool WorkPollingSiteElectionDay { get; set; }
    public bool MakePhoneCalls { get; set; }
    public bool BeListedAsSupporter { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!DisplayYardSign && !WorkEventBooth && !GoDoorToDoor && !WriteLetterToEditor && 
            !ShareSocialMedia && !WorkPollingSiteElectionDay && !MakePhoneCalls && !BeListedAsSupporter)
        {
            yield return new ValidationResult(
                "Please select at least one way to get involved.",
                new[] { nameof(DisplayYardSign) });
        }
    }
}
