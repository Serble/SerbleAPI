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
    public ActionResult<string[]> GetNotes() {
        User? user = HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        return Ok(noteRepo.GetUserNotes(user.Id));
    }

    [HttpGet("{noteId}")]
    public ActionResult<string> GetNoteContent(string noteId) {
        User? user = HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        return Ok(noteRepo.GetUserNoteContent(user.Id, noteId));
    }

    [HttpPost]
    public ActionResult CreateNote() {
        User? user = HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        string id = Guid.NewGuid().ToString();
        noteRepo.CreateUserNote(user.Id, id, "");
        return Ok(new { success = true, note_id = id });
    }

    [HttpPut("{noteId}")]
    [RequestSizeLimit(16_000_000)]
    public ActionResult UpdateNoteContent(string noteId, [FromBody] string body) {
        User? user = HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        noteRepo.UpdateUserNoteContent(user.Id, noteId, body);
        return Ok();
    }

    [HttpDelete("{noteId}")]
    public ActionResult DeleteNoteContent(string noteId) {
        User? user = HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        noteRepo.DeleteUserNote(user.Id, noteId);
        return Ok();
    }
}