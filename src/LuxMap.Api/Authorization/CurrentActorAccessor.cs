using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Api.Authorization;

public sealed class CurrentActorAccessor(IHttpContextAccessor http) : ICurrentActorAccessor
{
    public string? UserId => http.HttpContext?.User.FindFirst(AuthClaims.Subject)?.Value;
    public UserRole? Role => Enum.GetValues<UserRole>().Cast<UserRole?>()
        .FirstOrDefault(role => ContractEnum.ToDbValue(role!.Value) == http.HttpContext?.User.FindFirst(AuthClaims.Role)?.Value);
}
