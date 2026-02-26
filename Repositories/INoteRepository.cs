namespace SerbleAPI.Repositories;

public interface INoteRepository {
    Task<string[]> GetUserNotes(string userId);
    Task CreateUserNote(string userId, string noteId, string content);
    Task UpdateUserNoteContent(string userId, string noteId, string content);
    Task<string?> GetUserNoteContent(string userId, string noteId);
    Task DeleteUserNote(string userId, string noteId);
}
