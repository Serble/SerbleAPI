using System.Net.Mail;
using System.Text.RegularExpressions;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class AccountEditRequest {
    /// <summary>
    /// The width of the Email column. The standard caps an address at 254 characters, so this
    /// rejects only what could not be stored anyway; <see cref="IsEmailAddress"/> judges whether it
    /// is an address at all.
    /// </summary>
    private const int MaxEmailLength = 255;

    public string Field { get; set; }
    public string NewValue { get; set; }
    
    public AccountEditRequest(string field, string newValue) {
        Field = field;
        NewValue = newValue;
    }

    public async Task<User> ApplyChanges(User target, IUserRepository userRepo) {
        switch (Field.ToLower()) {
            case "username":
                // Check if username is taken
                if (NewValue == "") {
                    throw new ArgumentException("Username cannot be empty");
                }
                if (!UsernameRules.TryValidate(NewValue, out string? usernameError)) {
                    throw new ArgumentException(usernameError);
                }
                User? existingUser = await userRepo.GetUserFromName(NewValue);
                if (existingUser != null) {
                    throw new ArgumentException("Username is already taken");
                }
                target.Username = NewValue;
                break;
            
            case "password":
            case "totpenabled":
                throw new ArgumentException("Use /account/credentials");
            
            case "email":
                if (NewValue.Length > MaxEmailLength) {
                    throw new ArgumentException($"Email cannot be longer than {MaxEmailLength} characters");
                }
                if (!IsEmailAddress(NewValue)) {
                    throw new ArgumentException("Invalid email");
                }
                target.Email = NewValue;
                break;
            
            case "language":  // LAN-RG (Language-Region region is optional)
                if (!Regex.IsMatch(NewValue, @"^[a-z]{3}(-[A-Z]{2})?$")) {
                    throw new ArgumentException("Invalid language");
                }
                target.Language = NewValue;
                break;

            default:
                throw new ArgumentException("Field doesn't exist");
        }
        return target;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is an email address, and nothing but an email address.
    ///
    /// <para>This replaces a hand-written RFC 5322 regex. That pattern had a character class in its
    /// domain-literal branch whose upper bound was mistyped, which let a backslash be consumed by
    /// either side of an alternation — the classic <c>(a|aa)+</c> shape. Matching
    /// <c>a@[1.1.1.a:</c> followed by n backslashes then cost time exponential in n, so roughly
    /// seventy bytes of request body pinned a CPU core for seconds, and the field's 255-character
    /// budget left ample room. A parser walks the input once and cannot be made to backtrack.</para>
    ///
    /// <para>The equality check is not redundant. <see cref="MailAddress"/> parses a full mailbox,
    /// so it happily accepts <c>Name &lt;a@b.co&gt;</c> and keeps only the address part; comparing
    /// what it parsed against what arrived is what rejects a value carrying anything besides the
    /// address. The old regex was also unanchored, so it matched an address sitting anywhere inside
    /// a longer string and stored the whole thing.</para>
    /// </summary>
    private static bool IsEmailAddress(string value) {
        if (!MailAddress.TryCreate(value, out MailAddress? parsed)) return false;
        if (!string.Equals(parsed.Address, value, StringComparison.Ordinal)) return false;

        // The previous pattern required either a dotted domain or a bracketed IPv4 literal, both of
        // which contain a dot. Kept so that loosening validation is not a silent side effect of
        // fixing the denial of service: MailAddress alone would accept "a@b", which can never
        // receive the confirmation mail this address exists to be sent.
        return parsed.Host.Contains('.');
    }
}
