using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Vault;

[ApiController]
[Route("api/v1/vault/notes")]
[Authorize(Policy = "Scope:Vault")]
public class NotesController(IUserRepository userRepo, INoteRepository noteRepo) : ControllerManager {

    [HttpGet]
    public async Task<ActionResult<string[]>> GetNotes() {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        return Ok(noteRepo.GetUserNotes(user.Id));
    }

    [HttpGet("{noteId}")]
    public async Task<ActionResult<string>> GetNoteContent(string noteId) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        return Ok(noteRepo.GetUserNoteContent(user.Id, noteId));
    }

    [HttpPost]
    public async Task<ActionResult> CreateNote() {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        string id = Guid.NewGuid().ToString();
        await noteRepo.CreateUserNote(user.Id, id, "");
        return Ok(new {
            success = true,
            note_id = id
        });
    }

    [HttpPut("{noteId}")]
    [RequestSizeLimit(16_000_000)]
    public async Task<ActionResult> UpdateNoteContent(string noteId, [FromBody] string body) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        await noteRepo.UpdateUserNoteContent(user.Id, noteId, body);
        return Ok();
    }

    [HttpDelete("{noteId}")]
    public async Task<ActionResult> DeleteNoteContent(string noteId) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        await noteRepo.DeleteUserNote(user.Id, noteId);
        return Ok();
    }
}