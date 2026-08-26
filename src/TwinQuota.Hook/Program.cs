using TwinQuota.Core;

try
{
    var payload = await Console.In.ReadToEndAsync();
    var observation = ActiveModelObservationStore.ParseHookPayload(payload, DateTimeOffset.Now);
    if (observation is not null)
    {
        var overridePath = Environment.GetEnvironmentVariable("TWINQUOTA_ACTIVE_MODEL_PATH");
        await new ActiveModelObservationStore(overridePath).SaveAsync(observation);
    }
}
catch
{
    // Model tracking must never interfere with an Antigravity invocation.
}

Console.Out.Write("{}");
