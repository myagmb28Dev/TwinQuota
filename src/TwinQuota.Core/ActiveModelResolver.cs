namespace TwinQuota.Core;

public sealed record ActiveModelResolution(
    IReadOnlyList<ModelAvailability> Models,
    ModelAvailability? ActiveModel);

public static class ActiveModelResolver
{
    public static ActiveModelResolution Resolve(
        string modelsJson,
        ActiveModelObservation? observation)
    {
        var models = AntigravityResponseParser.ParseAvailableModels(modelsJson);
        var activeModel = AntigravityResponseParser.ParseActiveModel(modelsJson);
        if (observation is null)
        {
            return new ActiveModelResolution(models, activeModel);
        }

        var invokedModel = AntigravityResponseParser.ParseModelById(modelsJson, observation.ModelId);
        if (invokedModel is null)
        {
            return new ActiveModelResolution(models, activeModel);
        }

        if (!models.Any(model => model.Id.Equals(invokedModel.Id, StringComparison.OrdinalIgnoreCase)))
        {
            models = models.Append(invokedModel).ToArray();
        }

        return new ActiveModelResolution(models, invokedModel);
    }
}
