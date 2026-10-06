using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace LuxMap.Api.Http;

/// <summary>
/// Query-string dates must be ISO 8601 (Contract section 0): <c>YYYY-MM-DD</c> for a <see cref="DateOnly"/>, that plus an
/// optional <c>THH:MM[:SS[.fffffff]]</c> and <c>Z</c> / <c>±HH:MM</c> for a <see cref="DateTime"/>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 The framework binder parses with the invariant culture, which is en-US: <c>night_of=06/10/2026</c> became
/// 10 June, silently. A Vietnamese client writes day first, so the screen showed another month's work with no error
/// anywhere. Found in review of BE-25; the same binder served <c>scheduled_from/to</c> and the lux <c>from/to</c>.
/// </para>
/// <para>
/// This binder only GUARDS THE SHAPE, then hands the value to the framework binder unchanged — so what a valid
/// value means is exactly what it meant before (a <see cref="DateTime"/> without <c>Z</c> still arrives as
/// <c>Unspecified</c> for <c>UtcNormalization.ToUtc</c>, CLAUDE.md BE-REVIEW-02 rule 2). <c>[0-9]</c>, not
/// <c>\d</c>: <c>\d</c> matches every Unicode digit.
/// </para>
/// </remarks>
public sealed partial class IsoDateQueryModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.BindingInfo.BindingSource != BindingSource.Query) return null;
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        var shape = type == typeof(DateOnly) ? DateShape() : type == typeof(DateTime) ? DateTimeShape() : null;
        if (shape is null) return null;
        var inner = new SimpleTypeModelBinder(context.Metadata.ModelType,
            context.Services.GetRequiredService<ILoggerFactory>());
        return new Binder(shape, type == typeof(DateOnly) ? "YYYY-MM-DD" : "ISO 8601 (YYYY-MM-DD[THH:MM[:SS]][Z|±HH:MM])", inner);
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateShape();

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}(T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DateTimeShape();

    private sealed class Binder(Regex shape, string expected, IModelBinder inner) : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext context)
        {
            var value = context.ValueProvider.GetValue(context.ModelName).FirstValue;
            // Absent or empty: the framework binder decides, exactly as before (null for a nullable parameter).
            if (!string.IsNullOrWhiteSpace(value) && !shape.IsMatch(value.Trim()))
            {
                context.ModelState.TryAddModelError(context.ModelName, $"'{value}' is not a date in {expected}.");
                context.Result = ModelBindingResult.Failed();
                return Task.CompletedTask;
            }
            return inner.BindModelAsync(context);
        }
    }
}
