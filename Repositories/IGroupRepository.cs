using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IGroupRepository {
    Task<Group[]> GetAllGroups();
    Task<Group?> GetGroup(string groupId);
    Task AddGroup(Group group);
    Task UpdateGroup(Group group);
    Task DeleteGroup(string groupId);

    Task<Group[]> GetUserGroups(string userId);
    Task<string[]> GetUserGroupIds(string userId);
    Task<string[]> GetGroupMemberIds(string groupId);
    Task<bool> IsUserInGroup(string userId, string groupId);
    Task AddUserToGroup(string userId, string groupId);
    Task RemoveUserFromGroup(string userId, string groupId);
}
