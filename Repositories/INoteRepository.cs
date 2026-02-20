namespace SerbleAPI.Repositories;

public interface INoteRepository {
    string[] GetUserNotes(string userId);
    void CreateUserNote(string userId, string noteId, string content);
    void UpdateUserNoteContent(string userId, string noteId, string content);
    string? GetUserNoteContent(string userId, string noteId);
    void DeleteUserNote(string userId, string noteId);
}
