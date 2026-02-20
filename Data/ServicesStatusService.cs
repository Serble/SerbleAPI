using System.Text.Json;

namespace SerbleAPI.Data; 

// TODO: Have this be a service and DI
// TODO: Load products from regular config not custom one
public static class ServicesStatusService {
    private static Service[]? _services;
    private static Service[]? _pingedServices;
    private static DateTime _lastUpdated = DateTime.MinValue;

    private static readonly JsonSerializerOptions SerializerOptions = new () {
        WriteIndented = true
    };

    private const string ConfigFileName = "Services.json";
    private static readonly Service[] DefaultConfig = {
        new () {
            Name = "Yes"
        }
    };

    public static void Init() {
        if (!File.Exists(ConfigFileName)) {
            File.Create(ConfigFileName).Close();
            File.WriteAllText(ConfigFileName, JsonSerializer.Serialize(DefaultConfig, SerializerOptions));
            _services = DefaultConfig;
        }
        string json = File.ReadAllText(ConfigFileName);
        Service[]? outputArray;
        try {
            outputArray = JsonSerializer.Deserialize<Service[]>(json);
            if (outputArray == null)
                throw new Exception("Config file is not valid JSON");
        }
        catch (Exception ex) {
            throw new Exception("Config file is invalid: " + ex.Message);
        }
        _services = outputArray;
    }
    
    public static async Task<Service[]> GetServiceStatuses() {
        if (_services == null)
            throw new InvalidOperationException("ServicesStatusService not initialized");

        if (_pingedServices == null || _lastUpdated < DateTime.Now.AddMinutes(-1)) {
            await PingServices();
        }
        return _pingedServices!;
    }

    private static async Task PingServices() {
        Service[] pingedServices = new Service[_services!.Length];
        for (int i = 0; i < _services.Length; i++) {
            Service service = _services[i];
            if (!service.GetStatus) {
                pingedServices[i] = service;
                continue;
            }
            
            try {
                HttpClient client = new ();
                HttpResponseMessage response = await client.GetAsync(service.Url);
                if (response.IsSuccessStatusCode) {
                    service.Online = true;
                    if (service.DisplayResponse) {
                        service.Response = await response.Content.ReadAsStringAsync();
                    }
                    pingedServices[i] = service;
                    continue;
                }
            }
            catch (Exception) {
                // ignored
            }

            service.Online = false;
            pingedServices[i] = service;
        }
        
        _pingedServices = pingedServices;
        _lastUpdated = DateTime.Now;
    }

}

public class Service {
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ButtonText { get; set; }
    public string? ButtonUrl { get; set; }
    public string? Url { get; set; }
    public bool GetStatus { get; set; }
    public bool DisplayResponse { get; set; }
    
    public bool Online { get; set; }
    public string? Response { get; set; }
}