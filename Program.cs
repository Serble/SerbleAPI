using Microsoft.EntityFrameworkCore;
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
        
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Startup time config info
        StripeSettings? stripeSettings = builder.Configuration.GetSection("Stripe").Get<StripeSettings>();
        ApiSettings? apiSettings = builder.Configuration.GetSection("Api").Get<ApiSettings>();
        PasskeySettings? passkeySettings = builder.Configuration.GetSection("Passkey").Get<PasskeySettings>();
        if (stripeSettings == null || apiSettings == null || passkeySettings == null) {
            throw new Exception("Stripe or API or Passkey settings not found in configuration");
        }
        StripeConfiguration.ApiKey = stripeSettings.ApiKey;
        
        ProductManager.Load();
        RawDataManager.LoadRawData();
        
        builder.Services.AddOptions<ApiSettings>().Bind(builder.Configuration.GetSection("Api"));
        builder.Services.AddOptions<StripeSettings>().Bind(builder.Configuration.GetSection("Stripe"));
        builder.Services.AddOptions<PasskeySettings>().Bind(builder.Configuration.GetSection("Passkey"));
        builder.Services.AddOptions<EmailSettings>().Bind(builder.Configuration.GetSection("Email"));
        builder.Services.AddOptions<ReCaptchaSettings>().Bind(builder.Configuration.GetSection("ReCaptcha"));
        builder.Services.AddOptions<JwtSettings>().Bind(builder.Configuration.GetSection("Jwt"));
        builder.Services.AddOptions<TurnstileSettings>().Bind(builder.Configuration.GetSection("Turnstile"));
        
        builder.Services.AddControllers();
        builder.Services.AddSwaggerGen();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddMemoryCache();
        builder.Services.AddSession(options => {
            options.IdleTimeout = TimeSpan.FromMinutes(5);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });
        
        builder.Services.AddDbContext<SerbleDbContext>(options =>
            options.UseMySql(builder.Configuration.GetConnectionString("MySql"),
                ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("MySql"))));

        builder.Services.AddScoped<IAntiSpamService, AntiSpamService>();
        builder.Services.AddScoped<IGoogleReCaptchaService, GoogleReCaptchaService>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<ITurnstileCaptchaService, TurnstileCaptchaService>();
        builder.Services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();

        // db repos
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IAppRepository, AppRepository>();
        builder.Services.AddScoped<IPasskeyRepository, PasskeyRepository>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<INoteRepository, NoteRepository>();
        builder.Services.AddScoped<IKvRepository, KvRepository>();

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
        });

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
            
        if (!app.Environment.IsDevelopment()) {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        // Middleware order: cors/options -> redirects -> session -> auth -> controllers
        app.UseMiddleware<SerbleCorsMiddleware>();
        app.UseMiddleware<RedirectsMiddleware>();
        app.UseSession();
        app.UseAuthentication();
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

}