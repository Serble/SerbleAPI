using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class GroupRepository(SerbleDbContext db) : IGroupRepository {

    private static Group Map(DbGroup r) => new() {
        Id          = r.Id,
        Name        = r.Name        ?? "",
        Description = r.Description ?? ""
    };

    public async Task<Group[]> GetAllGroups() {
        DbGroup[] rows = await db.Groups.OrderBy(g => g.Name).ToArrayAsync();
        return rows.Select(Map).ToArray();
    }

    public async Task<Group?> GetGroup(string groupId) {
        DbGroup? row = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId);
        return row == null ? null : Map(row);
    }

    public Task AddGroup(Group group) {
        db.Groups.Add(new DbGroup {
            Id          = group.Id,
            Name        = group.Name,
            Description = group.Description
        });
        return db.SaveChangesAsync();
    }

    public async Task UpdateGroup(Group group) {
        DbGroup? row = await db.Groups.FirstOrDefaultAsync(g => g.Id == group.Id);
        if (row == null) return;
        row.Name        = group.Name;
        row.Description = group.Description;
        await db.SaveChangesAsync();
    }

    public async Task DeleteGroup(string groupId) {
        await db.UserGroups.Where(g => g.GroupId == groupId).ExecuteDeleteAsync();
        await db.AppGroupRules.Where(r => r.GroupId == groupId).ExecuteDeleteAsync();
        await db.AppGroupClaims.Where(c => c.GroupId == groupId).ExecuteDeleteAsync();
        await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync();
    }

    public async Task<Group[]> GetUserGroups(string userId) {
        DbGroup[] rows = await db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Join(db.Groups, ug => ug.GroupId, g => g.Id, (_, g) => g)
            .OrderBy(g => g.Name)
            .ToArrayAsync();
        return rows.Select(Map).ToArray();
    }

    public Task<string[]> GetUserGroupIds(string userId) =>
        db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToArrayAsync();

    public Task<string[]> GetGroupMemberIds(string groupId) =>
        db.UserGroups
            .Where(ug => ug.GroupId == groupId)
            .Select(ug => ug.UserId)
            .ToArrayAsync();

    public Task<bool> IsUserInGroup(string userId, string groupId) =>
        db.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId);

    public async Task AddUserToGroup(string userId, string groupId) {
        if (await IsUserInGroup(userId, groupId)) return;
        db.UserGroups.Add(new DbUserGroup { UserId = userId, GroupId = groupId });
        await db.SaveChangesAsync();
    }

    public Task RemoveUserFromGroup(string userId, string groupId) =>
        db.UserGroups
            .Where(ug => ug.UserId == userId && ug.GroupId == groupId)
            .ExecuteDeleteAsync();
}
