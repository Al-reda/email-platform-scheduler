using Amazon.Lambda.CloudWatchEvents.ScheduledEvents;
using Amazon.Lambda.TestUtilities;
using EmailPlatform.Scheduler;

// Local test harness. Lambda itself never runs this Main — it invokes
// Function.Handle directly. This entry point is only used when developers
// (or our CI) want to exercise the handler from a shell:
//
//   SCHEDULER__QUEUEURL=... STORAGECLIENT__BASEURL=... \
//   dotnet run --project src/Scheduler.Lambda
//
// The test context gives a usable ILambdaContext without needing real AWS.

var function = new Function();
var evt = new ScheduledEvent
{
    Time = DateTime.UtcNow,
    Account = "000000000000",
    Region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
    Source = "aws.events"
};
var context = new TestLambdaContext
{
    FunctionName = "email-platform-scheduler",
    FunctionVersion = "$LATEST",
    AwsRequestId = Guid.NewGuid().ToString()
};

var result = await function.Handle(evt, context);
Console.WriteLine($"[LocalRunner] Enqueued={result.Enqueued} Skipped={result.Skipped}");
