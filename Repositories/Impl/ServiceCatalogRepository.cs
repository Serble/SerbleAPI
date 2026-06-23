using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class ServiceCatalogRepository(SerbleDbContext db, IGroupRepository groupRepo) : IServiceCatalogRepository {

    private static ServiceCatalogItem Map(DbServiceCatalogItem service, string[] allowedGroupIds) => new() {
        Id = service.Id,
        Name = service.Name,
        Description = service.Description,
        Url = service.Url,
        IconUrl = service.IconUrl,
        VisibilityMode = (ServiceCatalogVisibilityMode)service.VisibilityMode,
        New = service.New,
        AllowedGroupIds = allowedGroupIds
    };

    public async Task<ServiceCatalogItem[]> ListServices() {
        DbServiceCatalogItem[] services = await db.ServiceCatalogItems
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToArrayAsync();
        string[] ids = services.Select(s => s.Id).ToArray();
        DbServiceCatalogItemGroupRule[] rules = await db.ServiceCatalogItemGroupRules
            .AsNoTracking()
            .Where(r => ids.Contains(r.ServiceId))
            .ToArrayAsync();
        return services
            .Select(service => Map(service, rules.Where(r => r.ServiceId == service.Id).Select(r => r.GroupId).ToArray()))
            .ToArray();
    }

    public async Task<ServiceCatalogItem?> GetService(string id) {
        DbServiceCatalogItem? service = await db.ServiceCatalogItems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (service == null) return null;

        string[] allowedGroupIds = await db.ServiceCatalogItemGroupRules
            .AsNoTracking()
            .Where(r => r.ServiceId == id)
            .Select(r => r.GroupId)
            .ToArrayAsync();

        return Map(service, allowedGroupIds);
    }

    public async Task<bool> CreateService(ServiceCatalogItem service) {
        if (await db.ServiceCatalogItems.AnyAsync(s => s.Id == service.Id)) return false;
        db.ServiceCatalogItems.Add(new DbServiceCatalogItem {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            Url = service.Url,
            IconUrl = service.IconUrl,
            VisibilityMode = (int)service.VisibilityMode,
            New = service.New
        });
        foreach (string groupId in service.AllowedGroupIds.Distinct()) {
            db.ServiceCatalogItemGroupRules.Add(new DbServiceCatalogItemGroupRule {
                ServiceId = service.Id,
                GroupId = groupId
            });
        }
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateService(ServiceCatalogItem service) {
        DbServiceCatalogItem? row = await db.ServiceCatalogItems.FirstOrDefaultAsync(s => s.Id == service.Id);
        if (row == null) return false;
        row.Name = service.Name;
        row.Description = service.Description;
        row.Url = service.Url;
        row.IconUrl = service.IconUrl;
        row.VisibilityMode = (int)service.VisibilityMode;
        row.New = service.New;

        await db.ServiceCatalogItemGroupRules.Where(r => r.ServiceId == service.Id).ExecuteDeleteAsync();
        foreach (string groupId in service.AllowedGroupIds.Distinct()) {
            db.ServiceCatalogItemGroupRules.Add(new DbServiceCatalogItemGroupRule {
                ServiceId = service.Id,
                GroupId = groupId
            });
        }
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteService(string id) {
        DbServiceCatalogItem? row = await db.ServiceCatalogItems.FirstOrDefaultAsync(s => s.Id == id);
        if (row == null) return false;
        await db.ServiceCatalogItemGroupRules.Where(r => r.ServiceId == id).ExecuteDeleteAsync();
        db.ServiceCatalogItems.Remove(row);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<ServiceCatalogItem[]> ListServicesVisibleToUser(string? userId) {
        ServiceCatalogItem[] services = await ListServices();
        if (userId == null) {
            return services.Where(s => s.VisibilityMode == ServiceCatalogVisibilityMode.Public).ToArray();
        }

        string[] userGroupIds = await groupRepo.GetUserGroupIds(userId);
        HashSet<string> userGroupIdSet = userGroupIds.ToHashSet();
        return services.Where(service =>
            service.VisibilityMode == ServiceCatalogVisibilityMode.Public ||
            service.AllowedGroupIds.Any(userGroupIdSet.Contains)
        ).ToArray();
    }
}
