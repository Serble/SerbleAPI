using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class NoteRepository(SerbleDbContext db) : INoteRepository {

    public string[] GetUserNotes(string userId) =>
        db.UserNotes
            .Where(n => n.User == userId)
            .Select(n => n.NoteId!)
            .ToArray();

    public void CreateUserNote(string userId, string noteId, string content) {
        db.UserNotes.Add(new DbUserNote { User = userId, NoteId = noteId, Note = content });
        db.SaveChanges();
    }

    public void UpdateUserNoteContent(string userId, string noteId, string content) {
        DbUserNote? row = db.UserNotes.FirstOrDefault(n => n.User == userId && n.NoteId == noteId);
        if (row == null) return;
        row.Note = content;
        db.SaveChanges();
    }

    public string? GetUserNoteContent(string userId, string noteId) =>
        db.UserNotes
            .Where(n => n.User == userId && n.NoteId == noteId)
            .Select(n => n.Note)
            .FirstOrDefault();

    public void DeleteUserNote(string userId, string noteId) {
        DbUserNote? row = db.UserNotes.FirstOrDefault(n => n.User == userId && n.NoteId == noteId);
        if (row == null) return;
        db.UserNotes.Remove(row);
        db.SaveChanges();
    }
}
