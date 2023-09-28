using MySql.Data.MySqlClient;

namespace SerbleAPI.Data.Storage.MySQL; 

public partial class MySqlStorageService {
    public void GetUserNotes(string userId, out string[] noteIds) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT noteid FROM serblesite_user_notes WHERE user=@user",
            new MySqlParameter("@user", userId));
        List<string> ids = new();
        while (reader.Read()) {
            ids.Add(reader.GetString("noteid"));
        }
        noteIds = ids.ToArray();
    }

    public void CreateUserNote(string userId, string noteId, string note) {
        MySqlHelper.ExecuteNonQuery(_connectString, "INSERT INTO serblesite_user_notes(user, noteid, note) VALUES(@user, @noteid, @note)",
            new MySqlParameter("@user", userId),
            new MySqlParameter("@noteid", noteId),
            new MySqlParameter("@note", note));
    }

    public void UpdateUserNoteContent(string userId, string noteId, string note) {
        MySqlHelper.ExecuteNonQuery(_connectString, "UPDATE serblesite_user_notes SET note=@note WHERE user=@user AND noteid=@noteid",
            new MySqlParameter("@user", userId),
            new MySqlParameter("@noteid", noteId),
            new MySqlParameter("@note", note));
    }

    public void GetUserNoteContent(string userId, string noteId, out string? content) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT note FROM serblesite_user_notes WHERE user=@user AND noteid=@noteid",
            new MySqlParameter("@user", userId),
            new MySqlParameter("@noteid", noteId));
        if (!reader.Read()) {
            content = null;
            return;
        }
        content = reader.GetString("note");
    }

    public void DeleteUserNote(string userId, string noteId) {
        MySqlHelper.ExecuteNonQuery(_connectString, "DELETE FROM serblesite_user_notes WHERE user=@user AND noteid=@noteid",
            new MySqlParameter("@user", userId),
            new MySqlParameter("@noteid", noteId));
    }
}