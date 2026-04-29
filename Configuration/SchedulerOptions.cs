namespace EmailPlatform.Scheduler.Configuration;

/// <summary>
/// Scheduler Lambda configuration. Populated from Lambda environment variables
/// (set via Terraform at deploy time).
/// </summary>
public sealed class SchedulerOptions
{
    public string QueueUrl { get; set; } = "";
    public string StorageBaseUrl { get; set; } = "";
    public string Region { get; set; } = "us-east-1";

    /// <summary>Override for local testing (Moto); null in production.</summary>
    public string? ServiceUrl { get; set; }

    public static SchedulerOptions FromEnvironment() => new()
    {
        QueueUrl = RequiredEnv("SCHEDULER__QUEUEURL"),
        StorageBaseUrl = RequiredEnv("STORAGECLIENT__BASEURL"),
        Region = Environment.GetEnvironmentVariable("SCHEDULER__REGION")
               ?? Environment.GetEnvironmentVariable("AWS_REGION")
               ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("SCHEDULER__SERVICEURL")
    };

    private static string RequiredEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Required env var {name} is not set.");
}
