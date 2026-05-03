# Email Platform — Scheduler Lambda

AWS Lambda triggered by EventBridge every Thursday at 9am UTC.
Queries Storage for pending announcements, transitions them to QUEUED, and enqueues SQS messages.

## Lambda handler string
```
Scheduler.Lambda::EmailPlatform.Scheduler.Function::Handle
```

## Local testing
```bash
SCHEDULER__QUEUEURL=http://localhost:4566/000000000000/email-jobs \
STORAGECLIENT__BASEURL=http://localhost:5001 \
AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test \
dotnet run
```

Uses EmailPlatform.Shared from GitHub Packages.


