using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymManagement.IntegrationTests;

/// <summary>
/// The route id / body id contract on every update action — cases R-01 to R-09.
///
/// Update actions assign <c>dto.Id = id</c> in the action body, but the validation filter runs
/// first, so validators requiring <c>Id &gt; 0</c> used to reject a payload that left the id out
/// and trusted the URL — which is exactly what a REST client should do. <c>ValidationFilter</c>
/// now stamps the route id onto a body whose <c>Id</c> is still 0 before validating.
///
/// These cases pin all three halves of that contract: an absent id is filled from the route, a
/// matching id is untouched, and a mismatched id loses to the route.
/// </summary>
[Collection(GymApiCollection.Name)]
public class RouteIdBindingTests : ApiTestBase
{
    public RouteIdBindingTests(GymApiFixture fixture) : base(fixture) { }

    // ------------------------------------------------------------- helpers

    /// <summary>PUTs a hand-built body so a property can genuinely be absent, not merely zero.</summary>
    private static Task<HttpResponseMessage> PutRawAsync(
        HttpClient client, string url, Dictionary<string, object?> body) =>
        client.PutAsync(url, new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    private async Task<TrainerDetailDto> CreateTrainerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/trainers", new CreateTrainerDto
        {
            FullName = UniqueName("Trainer"),
            Gender = Gender.Female,
            Phone = UniquePhone(),
            Email = UniqueEmail(),
            Specialization = "Strength & Conditioning",
            ExperienceYears = 4,
            JoiningDate = DateTime.Today
        });

        var created = await ReadDataAsync<CreateTrainerResponse>(response);
        return created.Trainer;
    }

    private async Task<int> CreateUserAsync(HttpClient client)
    {
        var roleId = await Factory.FromDbAsync(db =>
            db.Roles.AsNoTracking().Where(r => r.Name == RoleNames.Staff).Select(r => r.Id).FirstAsync());

        var created = await ReadDataAsync<TemporaryPasswordDto>(
            await client.PostAsJsonAsync("/api/users", new CreateUserDto
            {
                UserName = $"r{Guid.NewGuid():N}"[..14],
                FullName = "Route Id Account",
                Email = UniqueEmail(),
                Password = "RoutePassw0rd!",
                MustChangePassword = false,
                RoleIds = new List<int> { roleId },
                Status = UserStatus.Active
            }));

        return created.UserId;
    }

    // ------------------------------------------------- id omitted from the body

    [Fact(DisplayName = "R-01 PUT /api/members/{id} succeeds with no id in the body")]
    public async Task UpdateMember_WithNoIdInTheBody_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var response = await PutRawAsync(client, $"/api/members/{member.Id}", new Dictionary<string, object?>
        {
            // No "id" key at all: the URL is the only thing identifying the record.
            ["fullName"] = member.FullName,
            ["gender"] = (int)member.Gender,
            ["phone"] = member.Phone,
            ["email"] = member.Email,
            ["address"] = "9 Route Street",
            ["city"] = "Mysuru",
            ["joiningDate"] = member.JoiningDate.ToString("yyyy-MM-dd"),
            ["status"] = (int)MemberStatus.Active
        });

        var updated = await ReadDataAsync<MemberDetailDto>(response);

        updated.Id.Should().Be(member.Id, "the route id identifies the record");
        updated.Address.Should().Be("9 Route Street");
        updated.City.Should().Be("Mysuru");
    }

    [Fact(DisplayName = "R-02 PUT /api/trainers/{id} succeeds with no id in the body")]
    public async Task UpdateTrainer_WithNoIdInTheBody_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var trainer = await CreateTrainerAsync(client);

        var response = await PutRawAsync(client, $"/api/trainers/{trainer.Id}", new Dictionary<string, object?>
        {
            ["fullName"] = trainer.FullName,
            ["gender"] = (int)trainer.Gender,
            ["phone"] = trainer.Phone,
            ["email"] = trainer.Email,
            ["specialization"] = "Mobility",
            ["experienceYears"] = 6,
            ["joiningDate"] = trainer.JoiningDate.ToString("yyyy-MM-dd"),
            ["status"] = (int)TrainerStatus.Active
        });

        var updated = await ReadDataAsync<TrainerDetailDto>(response);

        updated.Id.Should().Be(trainer.Id);
        updated.Specialization.Should().Be("Mobility");
        updated.ExperienceYears.Should().Be(6);
    }

    [Fact(DisplayName = "R-03 PUT /api/users/{id} succeeds with no id in the body")]
    public async Task UpdateUser_WithNoIdInTheBody_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var userId = await CreateUserAsync(client);

        var roleId = await Factory.FromDbAsync(db =>
            db.Roles.AsNoTracking().Where(r => r.Name == RoleNames.Staff).Select(r => r.Id).FirstAsync());

        var response = await PutRawAsync(client, $"/api/users/{userId}", new Dictionary<string, object?>
        {
            ["fullName"] = "Renamed By Route",
            ["email"] = UniqueEmail(),
            ["status"] = (int)UserStatus.Active,
            ["roleIds"] = new[] { roleId }
        });

        var updated = await ReadDataAsync<UserDetailDto>(response);

        updated.Id.Should().Be(userId);
        updated.FullName.Should().Be("Renamed By Route");
    }

    [Fact(DisplayName = "R-04 An explicit id of 0 behaves the same as omitting it")]
    public async Task UpdateMember_WithAZeroIdInTheBody_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var update = new UpdateMemberDto
        {
            Id = 0, // the filter fills this in from the route
            FullName = member.FullName,
            Gender = member.Gender,
            Phone = member.Phone,
            Email = member.Email,
            Address = "11 Zero Lane",
            JoiningDate = member.JoiningDate,
            Status = MemberStatus.Active
        };

        var updated = await ReadDataAsync<MemberDetailDto>(
            await client.PutAsJsonAsync($"/api/members/{member.Id}", update));

        updated.Id.Should().Be(member.Id);
        updated.Address.Should().Be("11 Zero Lane");
    }

    // ------------------------------------------------------ id present in the body

    [Fact(DisplayName = "R-05 A body id that matches the route is accepted unchanged")]
    public async Task UpdateMember_WithAMatchingId_Succeeds()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var update = new UpdateMemberDto
        {
            Id = member.Id,
            FullName = member.FullName,
            Gender = member.Gender,
            Phone = member.Phone,
            Email = member.Email,
            Address = "3 Matching Way",
            JoiningDate = member.JoiningDate,
            Status = MemberStatus.Active
        };

        var updated = await ReadDataAsync<MemberDetailDto>(
            await client.PutAsJsonAsync($"/api/members/{member.Id}", update));

        updated.Id.Should().Be(member.Id);
        updated.Address.Should().Be("3 Matching Way");
    }

    [Fact(DisplayName = "R-06 The route wins over a mismatched body id, so the wrong record is never touched")]
    public async Task UpdateMember_WithAMismatchedId_UpdatesTheRoutedRecord()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var target = await CreateMemberAsync(client);
        var bystander = await CreateMemberAsync(client);

        var update = new UpdateMemberDto
        {
            Id = bystander.Id, // points at somebody else; the route must win
            FullName = target.FullName,
            Gender = target.Gender,
            Phone = target.Phone,
            Email = target.Email,
            Address = "7 Route Wins Road",
            JoiningDate = target.JoiningDate,
            Status = MemberStatus.Active
        };

        var updated = await ReadDataAsync<MemberDetailDto>(
            await client.PutAsJsonAsync($"/api/members/{target.Id}", update));

        updated.Id.Should().Be(target.Id);
        updated.Address.Should().Be("7 Route Wins Road");

        // The member named in the body was left completely alone.
        var untouched = await ReadDataAsync<MemberDetailDto>(
            await client.GetAsync($"/api/members/{bystander.Id}"));
        untouched.Address.Should().NotBe("7 Route Wins Road");
        untouched.FullName.Should().Be(bystander.FullName);
    }

    // ------------------------------------------------------------ still validated

    [Fact(DisplayName = "R-07 Filling in the id does not excuse the rest of the payload")]
    public async Task UpdateMember_WithNoIdAndABadPayload_IsStillRejected()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();
        var member = await CreateMemberAsync(client);

        var response = await PutRawAsync(client, $"/api/members/{member.Id}", new Dictionary<string, object?>
        {
            ["fullName"] = "", // still invalid
            ["gender"] = (int)member.Gender,
            ["phone"] = member.Phone,
            ["joiningDate"] = member.JoiningDate.ToString("yyyy-MM-dd"),
            ["status"] = (int)MemberStatus.Active
        });

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        (await ReadErrorAsync(response)).ValidationErrors
            .Should().ContainKey(nameof(UpdateMemberDto.FullName));
    }

    [Fact(DisplayName = "R-08 A route id pointing at nothing is still a 404, not a validation error")]
    public async Task UpdateMember_WithNoIdAndAnUnknownRoute_Is404()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await PutRawAsync(client, "/api/members/999999", new Dictionary<string, object?>
        {
            ["fullName"] = UniqueName(),
            ["gender"] = (int)Gender.Male,
            ["phone"] = UniquePhone(),
            ["joiningDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["status"] = (int)MemberStatus.Active
        });

        await AssertFailureAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact(DisplayName = "R-09 The stamp only applies to the route id, not to other id-bearing fields")]
    public async Task UpdateEnquiry_WithNoIdInTheBody_KeepsItsOtherIdsNull()
    {
        using var client = await Factory.CreateAuthenticatedClientAsync();

        var created = await ReadDataAsync<EnquiryDetailDto>(
            await client.PostAsJsonAsync("/api/enquiries", new SaveEnquiryDto
            {
                FullName = UniqueName("Lead"),
                Phone = UniquePhone(),
                Source = EnquirySource.Website,
                Status = EnquiryStatus.New
            }));

        var response = await PutRawAsync(client, $"/api/enquiries/{created.Id}", new Dictionary<string, object?>
        {
            ["fullName"] = created.FullName,
            ["phone"] = created.Phone,
            ["source"] = (int)EnquirySource.Website,
            ["status"] = (int)EnquiryStatus.Contacted
        });

        var updated = await ReadDataAsync<EnquiryDetailDto>(response);

        updated.Id.Should().Be(created.Id);
        updated.Status.Should().Be(EnquiryStatus.Contacted);

        // InterestedPlanId was absent, and the route id must not have leaked into it.
        updated.InterestedPlanId.Should().BeNull();
        updated.ConvertedMemberId.Should().BeNull();
    }

    /// <summary>Mirrors the anonymous result record the trainers controller returns.</summary>
    private sealed class CreateTrainerResponse
    {
        public TrainerDetailDto Trainer { get; set; } = new();
        public TemporaryPasswordDto? Account { get; set; }
    }
}
