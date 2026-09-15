namespace SerbleAPI.Config;

public class PasswordHashingSettings {
    public int MemoryKiB { get; set; } = 65536;
    /// <summary>At least 3.</summary>
    public int Iterations { get; set; } = 3;
    /// <summary>Concurrent Argon2 operations. Each one holds <see cref="MemoryKiB"/> of memory.</summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public int QueueTimeoutSeconds { get; set; } = 5;
}
