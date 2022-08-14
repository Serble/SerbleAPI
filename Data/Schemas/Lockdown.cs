namespace SerbleAPI.Data.Schemas; 

public class Lockdown {

    /// <summary>
    /// The pages in lockdown
    /// </summary>
    public PageType[] LockedDownPageTypes { get; set; }
    
    /// <summary>
    /// The perm levels excluded from lockdown
    /// </summary>
    public AccountAccessLevel[] ExceptedPermLevels { get; set; }
    
    /// <summary>
    /// The perm levels excluded from lockdown
    /// </summary>
    public int[] ExceptedPermLevelInts => ExceptedPermLevels.Select(x => (int)x).ToArray();

    public Lockdown() {
        LockedDownPageTypes = Array.Empty<PageType>();
        ExceptedPermLevels = Array.Empty<AccountAccessLevel>();
    }

}