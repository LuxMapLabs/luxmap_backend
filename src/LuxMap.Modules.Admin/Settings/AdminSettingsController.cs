using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Admin.Settings;

/// <summary>System settings the system admin changes without a deploy (BE-33): the dim threshold and the device offline threshold.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/settings")]
[ClientSurface(ClientSurface.Web)]
public sealed class AdminSettingsController(SettingsService service) : ControllerBase
{
    /// <summary>Every setting with its current value, default and accepted range.</summary>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status200OK)]
    public Task<SettingsResponse> List(CancellationToken ct) => service.ListAsync(ct);

    /// <summary>
    /// Sets one value; the change and its <c>note</c> are kept. Applies from the next read: earlier survey results keep the threshold
    /// they were classified with. Sending the current value changes nothing and records nothing.
    /// </summary>
    [HttpPut("{key}")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<SettingItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<SettingItem> Update(string key, [FromBody] UpdateSettingRequest request, CancellationToken ct)
        => service.UpdateAsync(key, request, ct);
}
