using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// An immutable audit record of a coin movement between two balances. Transfers are
/// zero-sum: <see cref="Amount"/> is deducted from <see cref="FromBalanceId"/> and added to
/// <see cref="ToBalanceId"/> in a single atomic operation. The balance foreign keys are
/// nullable with <c>ON DELETE SET NULL</c> so audit records survive balance deletion.
/// </summary>
[Index(nameof(FromBalanceId))]
[Index(nameof(ToBalanceId))]
public class DbTransaction {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    [ForeignKey(nameof(FromBalanceNavigation))]
    public string? FromBalanceId { get; set; }

    [StringLength(64)]
    [ForeignKey(nameof(ToBalanceNavigation))]
    public string? ToBalanceId { get; set; }

    public ulong Amount { get; set; }

    [StringLength(256)]
    public string? Description { get; set; }

    public DateTime DateCreated { get; set; }

    // navigation properties
    public DbBalance? FromBalanceNavigation { get; set; }
    public DbBalance? ToBalanceNavigation { get; set; }
}
