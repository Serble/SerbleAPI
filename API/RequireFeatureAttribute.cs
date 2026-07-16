using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SerbleAPI.Services;

namespace SerbleAPI.API;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireFeatureAttribute : TypeFilterAttribute {
    public string Feature { get; }

    public RequireFeatureAttribute(string feature) : base(typeof(RequireFeatureFilter)) {
        if (string.IsNullOrWhiteSpace(feature))
            throw new ArgumentException("Feature name is required.", nameof(feature));
        Feature = feature;
        Arguments = [feature];
    }
}

public class RequireFeatureFilter(string feature, IFeatureFlagService flags) : IAsyncActionFilter {
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {
        bool? enabled = await flags.IsEnabled(feature, context.HttpContext.User);
        if (enabled != true) {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
