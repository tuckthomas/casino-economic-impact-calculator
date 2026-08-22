using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaveNEIN.Server.Data.Entities;

[Table("daily_signup_digest_deliveries")]
public class DailySignupDigestDelivery
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("report_date_local", TypeName = "date")]
    public DateOnly ReportDateLocal { get; set; }

    [Column("period_start_utc")]
    public DateTime PeriodStartUtc { get; set; }

    [Column("period_end_utc")]
    public DateTime PeriodEndUtc { get; set; }

    [Column("registration_count")]
    public int RegistrationCount { get; set; }

    [Required]
    [MaxLength(32)]
    [Column("status")]
    public string Status { get; set; } = "Pending";

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("last_attempt_at_utc")]
    public DateTime? LastAttemptAtUtc { get; set; }

    [Column("sent_at_utc")]
    public DateTime? SentAtUtc { get; set; }

    [MaxLength(256)]
    [Column("provider_message_id")]
    public string? ProviderMessageId { get; set; }

    [MaxLength(2000)]
    [Column("last_error")]
    public string? LastError { get; set; }
}
