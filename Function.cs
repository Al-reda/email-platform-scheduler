using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.Lambda.CloudWatchEvents.ScheduledEvents;
using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using EmailPlatform.Scheduler.Configuration;
using EmailPlatform.Shared;
using EmailPlatform.Shared.Clients;
using EmailPlatform.Shared.Contracts;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EmailPlatform.Scheduler;

/// <summary>
/// Scheduler Lambda. Wired to EventBridge cron(0 9 ? * THU *) in Terraform.
///
/// Flow:
///   1. Ask Storage for all announcements with status=PENDING and
///      scheduledFor <= today.
///   2. For each announcement:
///        a. PATCH its status to QUEUED.
///        b. Send {announcementId} as an SQS message to the Email queue.
///   3. Return. Lambda disposes the container.
///
/// Deploy handler: EmailPlatform.Scheduler.Lambda::EmailPlatform.Scheduler.Function::Handle
///
/// Why split into (status change) + (SQS send)?
///   - Flipping to QUEUED *before* enqueuing is idempotency insurance: if the
///     Lambda runs twice in the same minute (EventBridge is at-least-once),
///     the second invocation's query will return zero PENDING items, so
///     nothing gets re-enqueued.
/// </summary>
public class Function
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IStorageClient _storage;
    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;

    /// <summary>
    /// Parameterless ctor used by the Lambda runtime on cold start. Reads
    /// everything from env vars that Terraform configures on the Lambda.
    /// </summary>
    public Function() : this(BuildDependencies()) { }

    /// <summary>
    /// Test-friendly ctor — lets our local harness inject mocks.
    /// </summary>
    public Function(IStorageClient storage, IAmazonSQS sqs, string queueUrl)
    {
        _storage = storage;
        _sqs = sqs;
        _queueUrl = queueUrl;
    }

    private Function((IStorageClient storage, IAmazonSQS sqs, string queueUrl) deps)
        : this(deps.storage, deps.sqs, deps.queueUrl) { }

    private static (IStorageClient, IAmazonSQS, string) BuildDependencies()
    {
        var opts = SchedulerOptions.FromEnvironment();

        var http = new HttpClient
        {
            BaseAddress = new Uri(opts.StorageBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var storage = new StorageClient(http);

        var sqsConfig = new AmazonSQSConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(opts.Region)
        };
        if (!string.IsNullOrWhiteSpace(opts.ServiceUrl))
        {
            sqsConfig.ServiceURL = opts.ServiceUrl;
            sqsConfig.AuthenticationRegion = opts.Region;
        }
        var sqs = new AmazonSQSClient(sqsConfig);

        return (storage, sqs, opts.QueueUrl);
    }

    /// <summary>
    /// Lambda handler invoked by EventBridge. The ScheduledEvent payload carries
    /// a timestamp (useful for logs) but we use server time for the query cutoff.
    /// </summary>
    public async Task<SchedulerResult> Handle(ScheduledEvent evt, ILambdaContext context)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        context.Logger.LogInformation(
            $"Scheduler invoked at {DateTimeOffset.UtcNow:o} (event time: {evt.Time:o}). " +
            $"Querying PENDING announcements scheduledBefore={today:O}.");

        var pending = await _storage.QueryByStatusAsync(
            AnnouncementStatus.Pending, today, context.InvokedFunctionArn is null
                ? CancellationToken.None
                : CancellationToken.None /* Lambda runtime handles timeouts externally */);

        context.Logger.LogInformation($"Found {pending.Items.Count} announcement(s) to dispatch.");

        int enqueued = 0, skipped = 0;
        foreach (var a in pending.Items)
        {
            try
            {
                // Step 1: transition to QUEUED. If this fails, skip — don't send
                // SQS message for an announcement we couldn't claim.
                var updated = await _storage.UpdateStatusAsync(
                    a.AnnouncementId, AnnouncementStatus.Queued, null, CancellationToken.None);

                if (updated is null)
                {
                    context.Logger.LogWarning(
                        $"Could not transition {a.AnnouncementId} to QUEUED — skipping.");
                    skipped++;
                    continue;
                }

                // Step 2: send the job to the email queue.
                var job = new EmailJobMessage { AnnouncementId = a.AnnouncementId, Attempt = 1 };
                await _sqs.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MessageBody = JsonSerializer.Serialize(job, JsonOpts)
                });
                enqueued++;
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Failed to dispatch {a.AnnouncementId}: {ex.Message}");
                skipped++;
            }
        }

        context.Logger.LogInformation($"Scheduler complete. Enqueued={enqueued} Skipped={skipped}.");
        return new SchedulerResult(enqueued, skipped);
    }
}

/// <summary>Return value of a scheduler run. Visible in CloudWatch logs.</summary>
public sealed record SchedulerResult(int Enqueued, int Skipped);
