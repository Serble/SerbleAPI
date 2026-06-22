using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IServiceCatalogRepository {
    Task<ServiceCatalogItem[]> ListServices();
    Task<ServiceCatalogItem?> GetService(string id);
    Task<bool> CreateService(ServiceCatalogItem service);
    Task<bool> UpdateService(ServiceCatalogItem service);
    Task<bool> DeleteService(string id);
    Task<ServiceCatalogItem[]> ListServicesVisibleToUser(string? userId);
}
