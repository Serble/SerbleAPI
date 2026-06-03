using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only management of access groups and their membership. Groups are completely
/// invisible to non-admins: every endpoint here requires the AdminOnly policy and no group
/// data is ever surfaced through owner-facing or public app responses.
/// </summary>
[ApiController]
[Route("api/v1/admin/groups")]
[Authorize(Policy = "AdminOnly")]
public class AdminGroupsController(
    ILogger<AdminGroupsController> logger,
    IGroupRepository groupRepo,
    IUserRepository userRepo) : ControllerManager {

    public class GroupBody {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Group>>> GetAll() {
        return Ok(await groupRepo.GetAllGroups());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Group>> Get(string id) {
        Group? group = await groupRepo.GetGroup(id);
        if (group == null) return NotFound();
        return Ok(group);
    }

    [HttpPost]
    public async Task<ActionResult<Group>> Create([FromBody] GroupBody body) {
        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest("Name is required");
        Group group = new() {
            Id          = Guid.NewGuid().ToString(),
            Name        = body.Name,
            Description = body.Description ?? ""
        };
        await groupRepo.AddGroup(group);
        logger.LogInformation("Admin {AdminId} created group {GroupId}",
            HttpContext.User.GetUserId(), group.Id);
        return Ok(group);
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<Group>> Edit(string id, [FromBody] GroupBody body) {
        Group? group = await groupRepo.GetGroup(id);
        if (group == null) return NotFound();
        if (body.Name        != null) group.Name        = body.Name;
        if (body.Description != null) group.Description = body.Description;
        await groupRepo.UpdateGroup(group);
        return Ok(group);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) {
        Group? group = await groupRepo.GetGroup(id);
        if (group == null) return NotFound();
        await groupRepo.DeleteGroup(id);
        logger.LogWarning("Admin {AdminId} deleted group {GroupId}",
            HttpContext.User.GetUserId(), id);
        return Ok(new { success = true });
    }

    // -------- Membership --------

    [HttpGet("{id}/members")]
    public async Task<ActionResult<IEnumerable<string>>> Members(string id) {
        Group? group = await groupRepo.GetGroup(id);
        if (group == null) return NotFound();
        return Ok(await groupRepo.GetGroupMemberIds(id));
    }

    [HttpPut("{id}/members/{userId}")]
    public async Task<IActionResult> AddMember(string id, string userId) {
        Group? group = await groupRepo.GetGroup(id);
        if (group == null) return NotFound("Group not found");
        User? user = await userRepo.GetUser(userId);
        if (user == null) return NotFound("User not found");
        await groupRepo.AddUserToGroup(userId, id);
        logger.LogInformation("Admin {AdminId} added user {UserId} to group {GroupId}",
            HttpContext.User.GetUserId(), userId, id);
        return Ok(new { success = true });
    }

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(string id, string userId) {
        Group? group = await groupRepo.GetGroup(id);
        if (group == null) return NotFound("Group not found");
        await groupRepo.RemoveUserFromGroup(userId, id);
        logger.LogInformation("Admin {AdminId} removed user {UserId} from group {GroupId}",
            HttpContext.User.GetUserId(), userId, id);
        return Ok(new { success = true });
    }

    /// <summary>Lists the groups a given user belongs to.</summary>
    [HttpGet("by-user/{userId}")]
    public async Task<ActionResult<IEnumerable<Group>>> ByUser(string userId) {
        User? user = await userRepo.GetUser(userId);
        if (user == null) return NotFound("User not found");
        return Ok(await groupRepo.GetUserGroups(userId));
    }
}
