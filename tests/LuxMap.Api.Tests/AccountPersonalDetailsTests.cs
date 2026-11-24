using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LuxMap.Api.Tests;

public partial class AccountAdminTests
{
    [Fact]
    public async Task Personal_details_round_trip_mask_list_and_preserve_self_patch_contract()
    {
        var email = $"{AccountTestFactory.NewUsername()}@example.invalid";
        var document = "p" + Guid.NewGuid().ToString("N");
        var created = await CreateAsync(new { email, full_name = "Hồ sơ thử", role = "field_engineer",
            commune_ids = new[] { await factory.SeededCommuneIdAsync() }, date_of_birth = "2000-02-29",
            gender = "other", address = "  Địa chỉ thử  ", phone_number = "+84 912-345-678",
            identity_type = "passport", identity_number = document });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = (await Json(created)).GetProperty("user");
        var id = user.GetProperty("user_id").GetString();
        var path = $"/api/v1/admin/users/{id}";
        Assert.Equal(document.ToUpperInvariant(), user.GetProperty("identity_number").GetString());
        Assert.Equal("+84912345678", user.GetProperty("phone_number").GetString());
        var admin = await AdminAsync();
        var renamed = await admin.PatchAsJsonAsync(path, new { full_name = "Tên khác" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("2000-02-29", (await Json(renamed)).GetProperty("date_of_birth").GetString());
        var list = await Json(await admin.GetAsync("/api/v1/admin/users?page_size=200"));
        var listed = list.GetProperty("items").EnumerateArray().Single(u => u.GetProperty("user_id").GetString() == id);
        Assert.StartsWith("*", listed.GetProperty("identity_number").GetString());
        Assert.DoesNotContain(document.ToUpperInvariant(), listed.GetProperty("identity_number").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await SetPasswordAsync(factory.Mail.LatestTokenFor(email), NewPassword)).StatusCode);
        var client = Bearer((await (await factory.CreateClient().PostLoginAsync(user.GetProperty("username").GetString()!, NewPassword)).ReadTokensAsync()).AccessToken);
        var me = await Json(await client.GetAsync("/api/v1/auth/me"));
        Assert.Equal(document.ToUpperInvariant(), me.GetProperty("identity_number").GetString());
        var self = await client.PatchAsJsonAsync("/api/v1/auth/me", new { full_name = "Tên tự sửa", address = "Cannot change" });
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        Assert.Equal("Địa chỉ thử", (await Json(self)).GetProperty("address").GetString());
        var cleared = await admin.PatchAsJsonAsync(path, new { identity_type = (string?)null, identity_number = (string?)null,
            date_of_birth = (string?)null, gender = (string?)null, address = (string?)null, phone_number = (string?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        foreach (var name in new[] { "identity_type", "identity_number", "date_of_birth", "gender", "address", "phone_number" })
            Assert.Equal(JsonValueKind.Null, (await Json(cleared)).GetProperty(name).ValueKind);
    }

    [Theory]
    [InlineData("{\"date_of_birth\":\"2999-01-01\"}")]
    [InlineData("{\"gender\":\"invalid\"}")]
    [InlineData("{\"identity_type\":\"invalid\"}")]
    [InlineData("{\"identity_number\":\"00123\"}")]
    [InlineData("{\"phone_number\":\"abc\"}")]
    public async Task Invalid_personal_details_are_rejected(string json)
    {
        var account = await CreateFieldEngineerAsync();
        var response = await (await AdminAsync()).PatchAsync($"/api/v1/admin/users/{account.UserId}",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await Json(response)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Identity_document_is_unique_after_normalization_but_phone_is_not()
    {
        var first = await CreateFieldEngineerAsync();
        var second = await CreateFieldEngineerAsync();
        var admin = await AdminAsync();
        var number = "p" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/v1/admin/users/{first.UserId}",
            new { identity_type = "passport", identity_number = number, phone_number = "0912345678" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/v1/admin/users/{second.UserId}",
            new { identity_type = "passport", identity_number = "  " + number.ToUpperInvariant() + " " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/v1/admin/users/{second.UserId}",
            new { phone_number = "0912345678" })).StatusCode);
    }
}
