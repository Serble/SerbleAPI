using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SerbleAPI.Models;

/// <summary>
/// Design-time factory so EF Core tooling (e.g. <c>dotnet ef migrations add</c> /
/// <c>database update</c>) can construct the context without booting the application. The
/// connection string is read from appsettings so <c>database update</c> targets the real
/// database, while a fixed server version is supplied (instead of AutoDetect) so
/// <c>migrations add</c> never has to open a connection to scaffold.
/// </summary>
public class SerbleDbContextFactory : IDesignTimeDbContextFactory<SerbleDbContext> {
    public SerbleDbContext CreateDbContext(string[] args) {
        IConfigurationRoot config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        string connectionString = config.GetConnectionString("MySql")
            ?? "server=localhost;database=serble;user=root;password=root";

        DbContextOptionsBuilder<SerbleDbContext> options = new();
        options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)));
        return new SerbleDbContext(options.Options);
    }
}

