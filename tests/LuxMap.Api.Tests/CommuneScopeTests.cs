using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LuxMap.Shared.Contracts.Errors;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>Group 2 — the commune scope cannot be bypassed (Contract section 7).</summary>
[Collection(nameof(ScopeCollection))]
public class CommuneScopeTests(ScopeTestFixture factory, ITestOutputHelper output)
{
    private async Task<HttpClient> AuthenticatedAsync(string username, string passwordVariable)
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(username, passwordVariable);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task<string[]> CommunesSeenAsync(HttpClient client, string query = "")
    {
        var body = await client.GetStringAsync("/api/v1/_scope/probes" + query);
        return [.. JsonDocument.Parse(body).RootElement.EnumerateArray()
            .Select(item => item.GetProperty("commune_id").GetString()!)
            .Distinct().Order()];
    }

    [Fact]
    public async Task Without_any_query_param_only_communes_in_the_claim_are_visible()
    {
        var client = await AuthenticatedAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var seen = await CommunesSeenAsync(client);

        output.WriteLine($"  no commune_id supplied → sees [{string.Join(", ", seen)}]");
        Assert.Equal([ScopeTestFixture.InScopeCommune], seen);
        Assert.DoesNotContain(factory.OutOfScopeCommune, seen);
    }

    [Fact]
    public async Task Commune_id_inside_the_scope_narrows_the_result()
    {
        await factory.AssignCommuneAsync("agency", factory.SecondCommune);
        try
        {
            var client = await AuthenticatedAsync("agency", "SEED_AGENCY_PASSWORD");

            var all = await CommunesSeenAsync(client);
            var narrowed = await CommunesSeenAsync(client, $"?commune_id={factory.SecondCommune}");

            output.WriteLine($"  claim with 2 communes → sees [{string.Join(", ", all)}]");
            output.WriteLine($"  narrowed to 1 commune → sees [{string.Join(", ", narrowed)}]");

            Assert.Equal(2, all.Length);
            Assert.Equal([factory.SecondCommune], narrowed);
        }
        finally
        {
            await factory.RemoveAllCommunesAsync("agency");
            await factory.AssignCommuneAsync("agency", ScopeTestFixture.InScopeCommune);
        }
    }

    [Fact]
    public async Task Commune_id_outside_the_scope_returns_403()
    {
        var client = await AuthenticatedAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var response = await client.GetAsync($"/api/v1/_scope/probes?commune_id={factory.OutOfScopeCommune}");

        output.WriteLine($"  commune_id outside the scope → HTTP {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.CommuneForbidden, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Mixing_an_allowed_and_a_forbidden_commune_id_still_returns_403()
    {
        var client = await AuthenticatedAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var response = await client.GetAsync(
            $"/api/v1/_scope/probes?commune_id={ScopeTestFixture.InScopeCommune}&commune_id={factory.OutOfScopeCommune}");

        output.WriteLine($"  one allowed commune + one forbidden → HTTP {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Direct_lookup_of_an_out_of_scope_resource_returns_404_not_403()
    {
        var client = await AuthenticatedAsync("engineer", "SEED_ENGINEER_PASSWORD");

        var inScope = await client.GetAsync($"/api/v1/_scope/probes/{factory.InScopeProbeId}");
        var outOfScope = await client.GetAsync($"/api/v1/_scope/probes/{factory.OutOfScopeProbeId}");

        output.WriteLine($"  in scope → {(int)inScope.StatusCode} · out of scope → {(int)outOfScope.StatusCode}");

        Assert.Equal(HttpStatusCode.OK, inScope.StatusCode);

        // A 403 would confirm the resource exists. The filter lives in the WHERE clause, so the row
        // simply is not found.
        Assert.Equal(HttpStatusCode.NotFound, outOfScope.StatusCode);
    }

    [Fact]
    public async Task Empty_commune_claim_sees_nothing()
    {
        await factory.RemoveAllCommunesAsync("crew");
        try
        {
            var client = await AuthenticatedAsync("crew", "SEED_CREW_PASSWORD");
            var seen = await CommunesSeenAsync(client);

            output.WriteLine($"  empty commune_ids claim → sees {seen.Length} commune(s)");
            Assert.Empty(seen);

            var lookup = await client.GetAsync($"/api/v1/_scope/probes/{factory.InScopeProbeId}");
            Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        }
        finally
        {
            await factory.AssignCommuneAsync("crew", ScopeTestFixture.InScopeCommune);
        }
    }

    [Fact]
    public async Task Administrator_with_wildcard_sees_everything()
    {
        var client = await AuthenticatedAsync("admin", "SEED_ADMIN_PASSWORD");
        var seen = await CommunesSeenAsync(client);

        output.WriteLine($"  administrator [\"*\"] → sees [{string.Join(", ", seen)}]");

        Assert.Contains(ScopeTestFixture.InScopeCommune, seen);
        Assert.Contains(factory.SecondCommune, seen);
        Assert.Contains(factory.OutOfScopeCommune, seen);

        // The administrator can even fetch the row the engineer cannot see.
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/v1/_scope/probes/{factory.OutOfScopeProbeId}")).StatusCode);
    }

    [Fact]
    public async Task Wildcard_claim_on_a_non_administrator_is_rejected()
    {
        // Simulate a BUG on the issuing side: turn on the system-wide flag for a field_crew account.
        await factory.SetSystemWideAsync("crew", true);
        try
        {
            var client = await AuthenticatedAsync("crew", "SEED_CREW_PASSWORD");
            var response = await client.GetAsync("/api/v1/_scope/probes");

            output.WriteLine($"  field_crew carrying [\"*\"] → HTTP {(int)response.StatusCode}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("error");
            Assert.Equal(ErrorCodes.CommuneForbidden, error.GetProperty("code").GetString());
        }
        finally
        {
            await factory.SetSystemWideAsync("crew", false);
        }
    }
}
