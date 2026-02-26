using Microsoft.EntityFrameworkCore;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class NoteRepository(SerbleDbContext db) : INoteRepository {

    public Task<string[]> GetUserNotes(string userId) =>
        db.UserNotes
            .Where(n => n.User == userId)
            .Select(n => n.NoteId!)
            .ToArrayAsync();

    public Task CreateUserNote(string userId, string noteId, string content) {
        db.UserNotes.Add(new DbUserNote { User = userId, NoteId = noteId, Note = content });
        return db.SaveChangesAsync();
    }

    public async Task UpdateUserNoteContent(string userId, string noteId, string content) {
        DbUserNote? row = await db.UserNotes.FirstOrDefaultAsync(n => n.User == userId && n.NoteId == noteId);
        if (row == null) return;
        row.Note = content;
        await db.SaveChangesAsync();
    }

    public Task<string?> GetUserNoteContent(string userId, string noteId) =>
        db.UserNotes
            .Where(n => n.User == userId && n.NoteId == noteId)
            .Select(n => n.Note)
            .FirstOrDefaultAsync();

    public async Task DeleteUserNote(string userId, string noteId) {
        DbUserNote? row = await db.UserNotes.FirstOrDefaultAsync(n => n.User == userId && n.NoteId == noteId);
        if (row == null) return;
        db.UserNotes.Remove(row);
        await db.SaveChangesAsync();
    }
}
