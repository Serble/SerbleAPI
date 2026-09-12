using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
// .NET 8 has two IPNetwork types and ForwardedHeadersOptions wants the ASP.NET one.
using ProxyNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;
using SerbleAPI.API;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Raw;
using SerbleAPI.Models;
using SerbleAPI.Repositories;
using SerbleAPI.Repositories.Impl;
using SerbleAPI.Services;
using SerbleAPI.Services.Impl;
using Stripe;
using TokenService = SerbleAPI.Services.Impl.TokenService;

namespace SerbleAPI;

public static class Program {
    private static bool _runApp = true;

    private static int Main(string[] args) {
        return Run(args);
    }
    
    private static int Run(string[] args) {
        Console.CancelKeyPress += (_, eventArgs) => {
            Console.WriteLine("Shutting down...");
            _runApp = false;
            eventArgs.Cancel = true;
        };
        
        // Backstop for catastrophic backtracking: any Regex constructed without an explicit timeout
        // gives up after this instead of spinning a core. The one pattern that could be driven that
        // way has been replaced by a parser, so nothing relies on this today — it is here so that
        // the next pattern someone adds cannot become a denial of service on its own.
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(250));

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Startup time config info
        StripeSettings? stripeSettings = builder.Configuration.GetSection("Stripe").Get<StripeSettings>();
        ApiSettings? apiSettings = builder.Configuration.GetSection("Api").Get<ApiSettings>();
        PasskeySettings? passkeySettings = builder.Configuration.GetSection("Passkey").Get<PasskeySettings>();
        if (stripeSettings == null || apiSettings == null || passkeySettings == null) {
            throw new Exception("Stripe or API or Passkey settings not found in configuration");
        }
        // The class defaults to a working configuration when the section is absent.
        ForwardedHeadersSettings forwardedHeaders =
            builder.Configuration.GetSection("ForwardedHeaders").Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();
        StripeConfiguration.ApiKey = stripeSettings.ApiKey;
        
        RawDataManager.LoadRawData();
        WordId.Load();

        builder.Services.AddOptions<ApiSettings>().Bind(builder.Configuration.GetSection("Api"));
        builder.Services.AddOptions<StripeSettings>().Bind(builder.Configuration.GetSection("Stripe"));
        builder.Services.AddOptions<PasskeySettings>().Bind(builder.Configuration.GetSection("Passkey"));
        builder.Services.AddOptions<EmailSettings>().Bind(builder.Configuration.GetSection("Email"));
        builder.Services.AddOptions<ReCaptchaSettings>().Bind(builder.Configuration.GetSection("ReCaptcha"));
        builder.Services.AddOptions<JwtSettings>().Bind(builder.Configuration.GetSection("Jwt"));
        builder.Services.AddOptions<TurnstileSettings>().Bind(builder.Configuration.GetSection("Turnstile"));
        builder.Services.AddOptions<OidcSettings>().Bind(builder.Configuration.GetSection("Oidc"));
        builder.Services.AddOptions<RateLimitSettings>().Bind(builder.Configuration.GetSection("RateLimit"));
        builder.Services.AddOptions<ForwardedHeadersSettings>().Bind(builder.Configuration.GetSection("ForwardedHeaders"));
        ConfigureForwardedHeaders(builder, forwardedHeaders);
        
        builder.Services.AddControllers();
        builder.Services.AddHttpClient();
        builder.Services.AddSwaggerGen();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddMemoryCache();
        builder.Services.AddSession(options => {
            options.IdleTimeout = TimeSpan.FromMinutes(5);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });
        
        builder.Services.AddDbContext<SerbleDbContext>(options => {
            string? mySqlConnection = builder.Configuration.GetConnectionString("MySql");
            // EF design-time tooling resolves the context from the application service provider,
            // so AutoDetect would try to open a real connection during `migrations add`. When the
            // tooling sets SERBLE_EF_DESIGNTIME=1 we use a fixed server version instead so
            // scaffolding never needs a live database. Runtime behaviour is unchanged.
            ServerVersion serverVersion = Environment.GetEnvironmentVariable("SERBLE_EF_DESIGNTIME") == "1"
                ? new MySqlServerVersion(new Version(8, 0, 24))
                : ServerVersion.AutoDetect(mySqlConnection);
            options.UseMySql(mySqlConnection, serverVersion);
        });

        // Singleton: the counters are the state.
        builder.Services.AddSingleton<IRateLimitService, RateLimitService>();

        builder.Services.AddScoped<IAntiSpamService, AntiSpamService>();
        builder.Services.AddScoped<IGoogleReCaptchaService, GoogleReCaptchaService>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<ITurnstileCaptchaService, TurnstileCaptchaService>();
        builder.Services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        builder.Services.AddScoped<IRewardTaskService, RewardTaskService>();
        builder.Services.AddScoped<IServerConfigService, ServerConfigService>();
        builder.Services.AddScoped<IFeatureFlagService, FeatureFlagService>();
        builder.Services.AddScoped<ITaxService, TaxService>();
        builder.Services.AddHostedService<TaxBackgroundService>();
        builder.Services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();
        builder.Services.AddHostedService<WebhookDispatcherService>();

        // Webhook endpoints are third-party URLs, so the timeout is short and explicit: a delivery
        // that hangs holds one of the dispatcher's concurrency slots, and the event will be retried
        // anyway. Handler rotation keeps DNS changes from being cached for the process's lifetime.
        builder.Services.AddHttpClient(WebhookDeliveryService.HttpClientName, client => {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).SetHandlerLifetime(TimeSpan.FromMinutes(5));

        // OIDC provider services
        builder.Services.AddSingleton<IOidcKeyService, OidcKeyService>();
        builder.Services.AddScoped<IOidcTokenService, OidcTokenService>();
        builder.Services.AddScoped<IOidcClaimsService, OidcClaimsService>();
        builder.Services.AddScoped<IAppAccessPolicyService, AppAccessPolicyService>();

        // db repos
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IBalanceRepository, BalanceRepository>();
        builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
        builder.Services.AddScoped<ITransactionProposalRepository, TransactionProposalRepository>();
        builder.Services.AddScoped<IUserTradeRepository, UserTradeRepository>();
        builder.Services.AddScoped<IAppApiKeyRepository, AppApiKeyRepository>();
        builder.Services.AddScoped<IAppWebhookRepository, AppWebhookRepository>();
        builder.Services.AddScoped<IAppRepository, AppRepository>();
        builder.Services.AddScoped<IPasskeyRepository, PasskeyRepository>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<INoteRepository, NoteRepository>();
        builder.Services.AddScoped<IKvRepository, KvRepository>();
        builder.Services.AddScoped<IGroupRepository, GroupRepository>();
        builder.Services.AddScoped<IServiceCatalogRepository, ServiceCatalogRepository>();
        builder.Services.AddScoped<ICompletedRewardTaskRepository, CompletedRewardTaskRepository>();
        builder.Services.AddScoped<IItemRepository, ItemRepository>();
        builder.Services.AddScoped<IItemTransactionRepository, ItemTransactionRepository>();
        builder.Services.AddScoped<IAppAccessRepository, AppAccessRepository>();
        builder.Services.AddScoped<IOidcCodeRepository, OidcCodeRepository>();
        builder.Services.AddScoped<IOidcRefreshRepository, OidcRefreshRepository>();

        // Authentication
        builder.Services.AddAuthentication(SerbleAuthenticationHandler.SchemeName)
            .AddScheme<SerbleAuthenticationOptions, SerbleAuthenticationHandler>(
                SerbleAuthenticationHandler.SchemeName, _ => { });

        // Authorisation
        // Register one policy per scope: [Authorize(Policy = "Scope:Vault")] etc.
        // Also a UserOnly policy that blocks app tokens.
        builder.Services.AddAuthorization(opts => {
            foreach (ScopeHandler.ScopesEnum scope in Enum.GetValues<ScopeHandler.ScopesEnum>()) {
                ScopeHandler.ScopesEnum captured = scope;
                opts.AddPolicy($"Scope:{captured}", p =>
                    p.RequireAuthenticatedUser()
                     .RequireAssertion(ctx => ctx.User.HasScope(captured)));
            }
            opts.AddPolicy("UserOnly", p =>
                p.RequireAuthenticatedUser()
                 .RequireAssertion(ctx => ctx.User.IsUser()));
            opts.AddPolicy("AdminOnly", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new AdminOnlyRequirement()));
            opts.AddPolicy("OfficialAppOnly", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new OfficialAppOnlyRequirement()));
            opts.AddPolicy("AppOnly", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new AppKeyOnlyRequirement()));
            opts.AddPolicy("OfficialAppKeyOnly", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new OfficialAppKeyOnlyRequirement()));
            opts.AddPolicy("EconomyAccess", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new EconomyAccessRequirement()));
            opts.AddPolicy("EconomyManage", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new EconomyManageRequirement()));
        });

        builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, OfficialAppAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, AppKeyOnlyAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, OfficialAppKeyOnlyAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, EconomyAccessAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, EconomyManageAuthorizationHandler>();

        builder.WebHost.UseUrls(apiSettings.BindUrl);  // move IP binding to config because I hate launchSettings.json
        
        builder.Services.AddFido2(options => {
            options.ServerDomain = passkeySettings.RelyingPartyId;
            options.ServerName = passkeySettings.RelyingPartyName;
            options.Origins = passkeySettings.AllowedOrigins.ToHashSet();
            options.TimestampDriftTolerance = 1000 * 60 * 5;
            options.ServerIcon = passkeySettings.ServerIconUrl;
        }).AddCachedMetadataService(config => {
            config.AddFidoMetadataRepository(_ => {
                
            });
        });
        
        // Init services
        ServicesStatusService.Init();

        WebApplication app = builder.Build();

        // First in the pipeline: behind nginx or Traefik, Connection.RemoteIpAddress is the proxy
        // until this has run, and rate limiting partitions anonymous traffic by that address.
        if (forwardedHeaders.Enabled) {
            app.UseForwardedHeaders();

            // A trust list that does not name the real proxy fails silently: everything works and
            // every request just looks like it came from one address.
            ForwardedHeadersOptions effective =
                app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
            app.Logger.LogInformation(
                "Forwarded headers on: trusting proxies [{Proxies}] and networks [{Networks}], " +
                "forward limit {Limit}, client header {Header}",
                string.Join(", ", effective.KnownProxies),
                string.Join(", ", effective.KnownNetworks.Select(n => $"{n.Prefix}/{n.PrefixLength}")),
                effective.ForwardLimit, effective.ForwardedForHeaderName);

            if (forwardedHeaders.TrustAllProxies) {
                app.Logger.LogWarning(
                    "ForwardedHeaders.TrustAllProxies is on, so {Header} is believed from any peer. " +
                    "This is only safe while {BindUrl} is unreachable except through the proxy; if that " +
                    "port is exposed, clients can forge their own source address and every per-address " +
                    "limit becomes decorative.",
                    forwardedHeaders.ForwardedForHeaderName, apiSettings.BindUrl);
            }
        }

        if (!app.Environment.IsDevelopment()) {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        using (IServiceScope scope = app.Services.CreateScope()) {
            SerbleDbContext db = scope.ServiceProvider.GetRequiredService<SerbleDbContext>();
            if (db.Database.IsRelational()) {
                db.Database.Migrate();
            }
            else {
                db.Database.EnsureCreated();
            }
            db.SaveChanges();
        }

        // Explicit, because the rate limiter reads the matched endpoint's tier attribute.
        app.UseRouting();

        // Middleware order: cors/options -> redirects -> session -> auth -> rate limit -> controllers
        app.UseMiddleware<SerbleCorsMiddleware>();
        app.UseMiddleware<RedirectsMiddleware>();
        app.UseSession();
        app.UseAuthentication();
        // After authentication so a request can be charged to the account and not only the
        // address; before authorization so an over-limit caller does no handler work.
        app.UseMiddleware<RateLimitMiddleware>();
        app.UseAuthorization();
        app.MapControllers();
        app.UseSwagger();
        app.UseSwaggerUI();

        CancellationTokenSource tokenSource = new();
        CancellationToken cancellationToken = tokenSource.Token;
        Task appTask = app.RunAsync(cancellationToken);
        
        while (_runApp) {
            if (appTask.IsCompleted) {
                break;
            }
            Thread.Sleep(100);
        }
        tokenSource.Cancel();
        Console.WriteLine("Attempting to stop application (Will abort after 10 seconds)");
        appTask.Wait(new TimeSpan(0, 0, 10));
        
        return 0;
    }

    /// <summary>
    /// Translates <see cref="ForwardedHeadersSettings"/> into the framework's options.
    /// <para>
    /// The framework ships with loopback in the trust list, and an entry there is permission to
    /// rewrite the client's address. Configured entries therefore replace that list rather than
    /// extend it; naming nothing falls back to loopback.
    /// </para>
    /// </summary>
    private static void ConfigureForwardedHeaders(WebApplicationBuilder builder, ForwardedHeadersSettings settings) {
        builder.Services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            options.ForwardedForHeaderName = settings.ForwardedForHeaderName;
            options.ForwardedProtoHeaderName = settings.ForwardedProtoHeaderName;
            options.ForwardedHostHeaderName = settings.ForwardedHostHeaderName;
            options.RequireHeaderSymmetry = settings.RequireHeaderSymmetry;
            options.ForwardLimit = Math.Max(1, settings.ForwardLimit);

            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            if (settings.TrustAllProxies) {
                // Empty lists mean no peer is checked. The warning is logged after build.
                return;
            }

            if (settings.KnownProxies.Length == 0 && settings.KnownNetworks.Length == 0) {
                // Nothing named, so assume the common case of a proxy on this host.
                options.KnownProxies.Add(IPAddress.Loopback);
                options.KnownProxies.Add(IPAddress.IPv6Loopback);
                return;
            }

            foreach (string proxy in settings.KnownProxies) {
                if (IPAddress.TryParse(proxy.Trim(), out IPAddress? address)) {
                    options.KnownProxies.Add(address);
                    continue;
                }
                Console.WriteLine($"ForwardedHeaders: ignoring unparseable KnownProxies entry '{proxy}'");
            }

            foreach (string network in settings.KnownNetworks) {
                if (TryParseNetwork(network, out ProxyNetwork parsed)) {
                    options.KnownNetworks.Add(parsed);
                    continue;
                }
                Console.WriteLine($"ForwardedHeaders: ignoring unparseable KnownNetworks entry '{network}'");
            }

            foreach (string host in settings.AllowedHosts) {
                options.AllowedHosts.Add(host);
            }
        });
    }

    /// <summary>Parses CIDR notation, e.g. <c>172.16.0.0/12</c>.</summary>
    private static bool TryParseNetwork(string value, out ProxyNetwork network) {
        network = default!;

        string[] parts = value.Trim().Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out IPAddress? prefix)) return false;
        if (!int.TryParse(parts[1], out int length)) return false;

        int maxLength = prefix.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (length < 0 || length > maxLength) return false;

        network = new ProxyNetwork(prefix, length);
        return true;
    }

}