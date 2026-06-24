using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

public class SerbleDbContext : DbContext {
    
    public SerbleDbContext() {
        
    }

    public SerbleDbContext(DbContextOptions<SerbleDbContext> options) : base(options) {
        
    }

    public virtual DbSet<DbApp> Apps { get; set; }
    public virtual DbSet<DbKv> Kvs { get; set; }
    public virtual DbSet<DbOwnedProduct> OwnedProducts { get; set; }
    public virtual DbSet<DbSerbleProduct> SerbleProducts { get; set; }
    public virtual DbSet<DbUser> Users { get; set; }
    public virtual DbSet<DbUserAuthorizedApp> UserAuthorizedApps { get; set; }
    public virtual DbSet<DbBalance> Balances { get; set; }
    public virtual DbSet<DbTransaction> Transactions { get; set; }
    public virtual DbSet<DbTransactionProposal> TransactionProposals { get; set; }
    public virtual DbSet<DbAppApiKey> AppApiKeys { get; set; }
    public virtual DbSet<DbUserNote> UserNotes { get; set; }
    public virtual DbSet<DbUserPasskey> UserPasskeys { get; set; }
    public virtual DbSet<DbGroup> Groups { get; set; }
    public virtual DbSet<DbUserGroup> UserGroups { get; set; }
    public virtual DbSet<DbAppGroupRule> AppGroupRules { get; set; }
    public virtual DbSet<DbAppGroupClaim> AppGroupClaims { get; set; }
    public virtual DbSet<DbServiceCatalogItem> ServiceCatalogItems { get; set; }
    public virtual DbSet<DbServiceCatalogItemGroupRule> ServiceCatalogItemGroupRules { get; set; }
    public virtual DbSet<DbOidcAuthorizationCode> OidcAuthorizationCodes { get; set; }
    public virtual DbSet<DbOidcRefreshGrant> OidcRefreshGrants { get; set; }
    public virtual DbSet<DbCompletedRewardTask> CompletedRewardTasks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        // Keep transaction audit records when a referenced balance is deleted: null the FK
        // instead of cascading. Two optional self-referential FKs to Balances are fine on
        // MySQL with ON DELETE SET NULL.
        modelBuilder.Entity<DbTransaction>()
            .HasOne(t => t.FromBalanceNavigation)
            .WithMany()
            .HasForeignKey(t => t.FromBalanceId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DbTransaction>()
            .HasOne(t => t.ToBalanceNavigation)
            .WithMany()
            .HasForeignKey(t => t.ToBalanceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
