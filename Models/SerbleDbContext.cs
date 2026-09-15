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
    public virtual DbSet<DbTransactionProposalItem> TransactionProposalItems { get; set; }
    public virtual DbSet<DbAppApiKey> AppApiKeys { get; set; }
    public virtual DbSet<DbUserNote> UserNotes { get; set; }
    public virtual DbSet<DbUserPasskey> UserPasskeys { get; set; }
    public virtual DbSet<DbUserCredential> UserCredentials { get; set; }
    public virtual DbSet<DbUserLoginFlow> UserLoginFlows { get; set; }
    public virtual DbSet<DbLoginSession> LoginSessions { get; set; }
    public virtual DbSet<DbGroup> Groups { get; set; }
    public virtual DbSet<DbUserGroup> UserGroups { get; set; }
    public virtual DbSet<DbAppGroupRule> AppGroupRules { get; set; }
    public virtual DbSet<DbAppGroupClaim> AppGroupClaims { get; set; }
    public virtual DbSet<DbServiceCatalogItem> ServiceCatalogItems { get; set; }
    public virtual DbSet<DbServiceCatalogItemGroupRule> ServiceCatalogItemGroupRules { get; set; }
    public virtual DbSet<DbOidcAuthorizationCode> OidcAuthorizationCodes { get; set; }
    public virtual DbSet<DbOidcRefreshGrant> OidcRefreshGrants { get; set; }
    public virtual DbSet<DbCompletedRewardTask> CompletedRewardTasks { get; set; }
    public virtual DbSet<DbItem> Items { get; set; }
    public virtual DbSet<DbItemTransaction> ItemTransactions { get; set; }
    public virtual DbSet<DbUserTrade> UserTrades { get; set; }
    public virtual DbSet<DbUserTradeItem> UserTradeItems { get; set; }
    public virtual DbSet<DbTaxCycle> TaxCycles { get; set; }
    public virtual DbSet<DbTaxAppCharge> TaxAppCharges { get; set; }
    public virtual DbSet<DbAppWebhook> AppWebhooks { get; set; }
    public virtual DbSet<DbWebhookDelivery> WebhookDeliveries { get; set; }

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

        // Trade proposal item legs are owned by their proposal: removing the proposal removes its
        // item rows.
        modelBuilder.Entity<DbTransactionProposalItem>()
            .HasOne(i => i.ProposalNavigation)
            .WithMany()
            .HasForeignKey(i => i.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        // An item's ownership history is owned by the item: removing the item removes its audit
        // trail. Owner ids are plain strings (no FK), so history survives owner deletion.
        modelBuilder.Entity<DbItemTransaction>()
            .HasOne(i => i.ItemNavigation)
            .WithMany()
            .HasForeignKey(i => i.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // User-trade item legs are owned by their trade: removing the trade removes its item rows.
        modelBuilder.Entity<DbUserTradeItem>()
            .HasOne(i => i.TradeNavigation)
            .WithMany()
            .HasForeignKey(i => i.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Per-app tax detail is owned by its cycle: pruning cycle history prunes the detail with it.
        modelBuilder.Entity<DbTaxAppCharge>()
            .HasOne(c => c.CycleNavigation)
            .WithMany()
            .HasForeignKey(c => c.CycleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Webhook subscriptions belong to their app, and queued deliveries belong to the
        // subscription — so unsubscribing also drops anything still waiting to be sent, rather
        // than leaving the dispatcher retrying against an endpoint nobody owns any more.
        modelBuilder.Entity<DbAppWebhook>()
            .HasOne(w => w.AppNavigation)
            .WithMany()
            .HasForeignKey(w => w.AppId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DbWebhookDelivery>()
            .HasOne(d => d.WebhookNavigation)
            .WithMany()
            .HasForeignKey(d => d.WebhookId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DbUserPasskey>()
            .HasOne(p => p.CredentialNavigation)
            .WithMany()
            .HasForeignKey(p => p.UserCredentialId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DbLoginSession>()
            .HasOne(s => s.UserNavigation)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
