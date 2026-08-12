using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveNEIN.Server.Data.Entities;

[Table("coalition_signups")]
public class CoalitionSignup
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Column("last_name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [MaxLength(254)]
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(254)]
    [Column("normalized_email")]
    public string NormalizedEmail { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    [Column("address_line1")]
    public string AddressLine1 { get; set; } = string.Empty;

    [MaxLength(200)]
    [Column("address_line2")]
    public string? AddressLine2 { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("city")]
    public string City { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    [Column("state_province")]
    public string StateProvince { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    [Column("postal_code")]
    public string PostalCode { get; set; } = string.Empty;

    // Preferences
    [Column("display_yard_sign")]
    public bool DisplayYardSign { get; set; }

    [Column("work_event_booth")]
    public bool WorkEventBooth { get; set; }

    [Column("go_door_to_door")]
    public bool GoDoorToDoor { get; set; }

    [Column("write_letter_to_editor")]
    public bool WriteLetterToEditor { get; set; }

    [Column("share_social_media")]
    public bool ShareSocialMedia { get; set; }

    [Column("work_polling_site_election_day")]
    public bool WorkPollingSiteElectionDay { get; set; }

    [Column("make_phone_calls")]
    public bool MakePhoneCalls { get; set; }

    [Column("be_listed_as_supporter")]
    public bool BeListedAsSupporter { get; set; }

    [Column("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updated_at_utc")]
    public DateTime UpdatedAtUtc { get; set; }
}
