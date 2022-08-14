using System.Security;
using GeneralPurposeLib;
using SerbleAPI.Data;
using SerbleAPI.Data.Raw;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Data.Storage;
using LogLevel = GeneralPurposeLib.LogLevel;
using UnauthorizedAccessException = System.UnauthorizedAccessException;

namespace SerbleAPI;

public static class Program {
    
    private static ConfigManager? _configManager;
    private static readonly Dictionary<string, string> ConfigDefaults = new() {
        { "bind_url", "http://*:5000" },
        { "storage_service", "file" },
        { "http_authorization_token", "my very secure auth token" },
        { "http_url", "https://myverysecurestoragebackend.io/" },
        { "my_host" , "https://theplacewherethisappisaccessable.com/" },
        { "token_issuer", "CoPokBl" },
        { "token_audience", "Privileged Users" },
        { "token_secret" , Guid.NewGuid().ToString() },
        { "mysql_ip", "mysql.example.com" },
        { "mysql_user", "coolperson" },
        { "mysql_password", "myverysecurepassword" },
        { "mysql_database", "serble" },
        { "smtp_username", "system@serble.net" },
        { "smtp_password", "very secure password" },
        { "smtp_host", "smtp.serble.net" },
        { "smtp_port", "587" },
        { "EmailAddress_System", "system@serble.net" },
        { "EmailAddress_Newsletter", "newsletter@serble.net" },
        { "admin_contact_email", "admin@serble.net" },
        { "google_recaptcha_site_key", "" },
        { "google_recaptcha_secret_key", "" },
        { "logging_level", "1" },
        { "website_url", "https://serble.net" }
    };
    public static Dictionary<string, string>? Config;
    public static IStorageService? StorageService;
    public static bool RunApp = true;
    public static bool RestartApp = false;
    public static bool RestartAppOnce = false;
    public static Lockdown? Lockdown = null;
    
    private static int Main(string[] args) {

        try {
            Logger.Init(LogLevel.Debug);
        }
        catch (Exception e) {
            Console.WriteLine(e);
            Console.WriteLine("Failed to initialize logger");
            return 1;
        }

        int stopCode = 0;
        bool firstRun = true;
        try {
            while (RestartApp || RestartAppOnce || firstRun) {
                if (firstRun) { firstRun = false; }
                if (RestartAppOnce) { RestartAppOnce = false; }
                stopCode = Run(args);
                Logger.Warn("Application stopped with code " + stopCode);
            }
            return stopCode;
        }
        catch (Exception e) {
            Logger.Error(e);
            Logger.Error("The application has crashed due to an unhandled exception.");
            Logger.WaitFlush();
            stopCode = 1;
        }

        try {
            Logger.WaitFlush();
        }
        catch (Exception e) {
            Console.WriteLine(e);
            Console.WriteLine("Failed to flush logger, writing error to logfail.log");
            try {
                File.WriteAllText("logfail.log", e.ToString());
            }
            catch (UnauthorizedAccessException) {
                Console.WriteLine("Failed to write logfail.log due to access denied error.");
                return 1;
            }
            catch (SecurityException) {
                Console.WriteLine("Failed to write logfail.log due to access denied error.");
                return 1;
            }
            catch (IOException ioException) {
                Console.WriteLine(ioException);
                Console.WriteLine("Failed to write logfail.log due to IO Error.");
                return 1;
            }
            catch (Exception writeFailEx) {
                Console.WriteLine(writeFailEx);
                Console.WriteLine("Failed to write logfail.log due to an unknown error.");
                return 1;
            }
        }
        return stopCode;
    }
    
    private static int Run(string[] args) {

        // Config
        Logger.Info("Loading config...");
        _configManager = new ConfigManager("config.json", ConfigDefaults);
        Config = _configManager.LoadConfig();
        Logger.Info("Config loaded.");
        
        // Storage service
        try {
            StorageService = Config["storage_service"].ToLower() switch {
                "file" => new FileStorageService(),
                "mysql" => new MySqlStorageService(),
                _ => throw new Exception("Unknown storage service")
            };
        }
        catch (Exception e) {
            if (e.Message != "Unknown storage service") throw;
            Logger.Error("Invalid storage service specified in config.");
            return 1;
        }

        // Init storage
        Logger.Info("Initializing storage...");
        try {
            StorageService.Init();
        }
        catch (Exception e) {
            Logger.Error("Failed to initialize storage");
            Logger.Error(e);
            return 1;
        }

        if (args.Length != 0) {

            switch (args[0]) {
                
                default:
                    Console.WriteLine("Unknown command");
                    return 1;

            }
        }
        
        // Load Raw Data
        Logger.Info("Loading raw data...");
        try {
            RawDataManager.LoadRawData();
            Logger.Info("Raw data loaded.");
        }
        catch (Exception e) {
            Logger.Error("Failed to load raw data");
            Logger.Error(e);
            return 1;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        try {
            builder.Services.AddControllers();
            builder.Services.AddSwaggerGen();
            builder.Services.AddEndpointsApiExplorer();
            builder.WebHost.UseUrls(Config["bind_url"]);
        }
        catch (Exception e) {
            Logger.Error(e);
            Logger.Error("Failed to initialize services");
            return 1;
        }
        
        // Init services
        Logger.Info("Initializing services...");
        try {
            ServicesStatusService.Init();
        }
        catch (Exception e) {
            Logger.Error("Failed to initialize services");
            Logger.Error(e);
            return 1;
        }

        WebApplication app;
        try {
            app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment()) {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.MapControllers();
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        catch (Exception e) {
            Logger.Error(e);
            Logger.Error("Failed to initialize application");
            return 1;
        }

        bool didError = false;
        try {
            CancellationTokenSource tokenSource = new();
            CancellationToken cancellationToken = tokenSource.Token;
            Task appTask = app.RunAsync(cancellationToken);
            Logger.Info("Application started");
            
            while (RunApp) {
                if (appTask.IsCompleted) {
                    Logger.Info("Application execution finished");
                    break;
                }
                Thread.Sleep(100);
            }
            tokenSource.Cancel();
            Logger.Info("Attempting to stop application (Will abort after 10 seconds)");
            bool successfulStop = appTask.Wait(new TimeSpan(0, 0, 10));
            Logger.Info(!successfulStop
                ? "Application stop timed out, completing execution"
                : "Server stopped with no errors.");
        }
        catch (Exception e) {
            Logger.Error(e);
            Logger.Error("Server stopped with error.");
            didError = true;
        }
        
        // Shutdown storage
        Logger.Info("Shutting down storage...");
        try {
            StorageService.Deinit();
        }
        catch (Exception e) {
            Logger.Error("Failed to shutdown storage");
            Logger.Error(e);
        }
        
        return didError ? 1 : 0;
    }

}