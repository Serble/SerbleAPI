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
    public virtual DbSet<DbUserNote> UserNotes { get; set; }
    public virtual DbSet<DbUserPasskey> UserPasskeys { get; set; }
    public virtual DbSet<DbGroup> Groups { get; set; }
    public virtual DbSet<DbUserGroup> UserGroups { get; set; }
    public virtual DbSet<DbAppGroupRule> AppGroupRules { get; set; }
    public virtual DbSet<DbAppGroupClaim> AppGroupClaims { get; set; }
    public virtual DbSet<DbOidcAuthorizationCode> OidcAuthorizationCodes { get; set; }
    public virtual DbSet<DbOidcRefreshGrant> OidcRefreshGrants { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        
    }
}
