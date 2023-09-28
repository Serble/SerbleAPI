using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Vault; 

[ApiController]
[Route("api/v1/vault/notes")]
public class NotesController : ControllerManager {
    
    [HttpGet]
    public ActionResult<string[]> GetNotes([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.CheckAndGetInfo(out User user, out _, ScopeHandler.ScopesEnum.Vault)) {
            return Unauthorized();
        }
        
        Program.StorageService!.GetUserNotes(user.Id, out string[] noteIds);
        return Ok(noteIds);
    }
    
    [HttpGet("{noteId}")]
    public ActionResult<string> GetNoteContent([FromHeader] SerbleAuthorizationHeader authorizationHeader, string noteId) {
        if (!authorizationHeader.CheckAndGetInfo(out User user, out _, ScopeHandler.ScopesEnum.Vault)) {
            return Unauthorized();
        }
        
        Program.StorageService!.GetUserNoteContent(user.Id, noteId, out string? content);
        return Ok(content);
    }
    
    [HttpPost]
    public ActionResult CreateNote([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.CheckAndGetInfo(out User user, out _, ScopeHandler.ScopesEnum.Vault)) {
            return Unauthorized();
        }

        string id = Guid.NewGuid().ToString();
        Program.StorageService!.CreateUserNote(user.Id, id, "");
        return Ok(new {
            success = true,
            note_id = id
        });
    }
    
    [HttpPut("{noteId}")]
    public ActionResult UpdateNoteContent([FromHeader] SerbleAuthorizationHeader authorizationHeader, string noteId, [FromBody] string body) {
        if (!authorizationHeader.CheckAndGetInfo(out User user, out _, ScopeHandler.ScopesEnum.Vault)) {
            return Unauthorized();
        }
        
        Program.StorageService!.UpdateUserNoteContent(user.Id, noteId, body);
        return Ok();
    }
    
    [HttpDelete("{noteId}")]
    public ActionResult DeleteNoteContent([FromHeader] SerbleAuthorizationHeader authorizationHeader, string noteId) {
        if (!authorizationHeader.CheckAndGetInfo(out User user, out _, ScopeHandler.ScopesEnum.Vault)) {
            return Unauthorized();
        }
        
        Program.StorageService!.DeleteUserNote(user.Id, noteId);
        return Ok();
    }
    
}