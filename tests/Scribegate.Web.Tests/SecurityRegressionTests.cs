using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

public class SecurityRegressionTests
{
    [Fact]
    public async Task Contributor_CannotSubmitReviewerVerdict()
    {
        await using var factory = new ScribegateWebAppFactory();
        var adminClient = factory.CreateClient();
        var (adminUsername, adminToken) = await RegisterAsync(adminClient, "admin");
        Authenticate(adminClient, adminToken);

        var repo = await CreateRepoAsync(adminClient, "Review Auth", "review-auth");
        var document = await CreateDocumentAsync(adminClient, repo.Owner, repo.Slug, "handbook.md");
        var proposal = await CreateProposalAsync(adminClient, repo.Owner, repo.Slug, document.Id);

        var contributorClient = factory.CreateClient();
        var (contributorUsername, contributorToken) = await RegisterAsync(contributorClient, "contrib");
        await AddMemberAsync(adminClient, repo.Owner, repo.Slug, contributorUsername, "Contributor");

        Authenticate(contributorClient, contributorToken);
        var review = await contributorClient.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/reviews",
            new
            {
                verdict = "Approved",
                body = "Looks good.",
            });

        review.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Proposal_CannotTargetDocumentInAnotherRepository()
    {
        await using var factory = new ScribegateWebAppFactory();
        var client = factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "admin");
        Authenticate(client, token);

        var sourceRepo = await CreateRepoAsync(client, "Source Repo", "source-repo");
        var targetRepo = await CreateRepoAsync(client, "Target Repo", "target-repo");
        var sourceDoc = await CreateDocumentAsync(client, sourceRepo.Owner, sourceRepo.Slug, "source.md");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{targetRepo.Owner}/{targetRepo.Slug}/proposals",
            new
            {
                title = "Cross repo proposal",
                content = "# Updated",
                documentId = sourceDoc.Id,
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublicRepositoryComments_StillRequireMembership()
    {
        await using var factory = new ScribegateWebAppFactory();
        var adminClient = factory.CreateClient();
        var (_, adminToken) = await RegisterAsync(adminClient, "admin");
        Authenticate(adminClient, adminToken);

        var repo = await CreateRepoAsync(adminClient, "Public Repo", "public-comments", visibility: "Public");
        var proposal = await CreateProposalAsync(adminClient, repo.Owner, repo.Slug, documentId: null, documentPath: "notes.md");

        var outsiderClient = factory.CreateClient();
        var (_, outsiderToken) = await RegisterAsync(outsiderClient, "outsider");
        Authenticate(outsiderClient, outsiderToken);

        var response = await outsiderClient.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = "Intruding on a public repo discussion." });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PublicMemberList_RedactsEmailsForAnonymousReaders()
    {
        await using var factory = new ScribegateWebAppFactory();
        var adminClient = factory.CreateClient();
        var (_, adminToken) = await RegisterAsync(adminClient, "admin");
        Authenticate(adminClient, adminToken);

        var repo = await CreateRepoAsync(adminClient, "Members Repo", "members-repo", visibility: "Public");

        var secondClient = factory.CreateClient();
        var (readerUsername, _) = await RegisterAsync(secondClient, "reader");
        await AddMemberAsync(adminClient, repo.Owner, repo.Slug, readerUsername, "Reader");

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/v1/repositories/{repo.Owner}/{repo.Slug}/members");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MemberListResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
        body.Items.Should().OnlyContain(m => string.IsNullOrEmpty(m.Email));
    }

    [Fact]
    public async Task RegistrationConfig_ExposesConfiguredLegalLinks()
    {
        await using var factory = new ScribegateWebAppFactory();
        var adminClient = factory.CreateClient();
        var (_, adminToken) = await RegisterAsync(adminClient, "admin");
        Authenticate(adminClient, adminToken);

        await UpdateSettingAsync(adminClient, "registration.tos_url", "https://example.test/terms");
        await UpdateSettingAsync(adminClient, "registration.privacy_url", "https://example.test/privacy");

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync("/api/v1/admin/settings/registration");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RegistrationStatusResponse>();
        body.Should().NotBeNull();
        body!.RegistrationEnabled.Should().BeTrue();
        body.RequireTos.Should().BeTrue();
        body.TosUrl.Should().Be("https://example.test/terms");
        body.PrivacyUrl.Should().Be("https://example.test/privacy");
    }

    [Fact]
    public async Task RevisionEndpoint_CannotReadRevisionFromAnotherRepository()
    {
        await using var factory = new ScribegateWebAppFactory();
        var adminClient = factory.CreateClient();
        var (_, adminToken) = await RegisterAsync(adminClient, "admin");
        Authenticate(adminClient, adminToken);

        var publicRepo = await CreateRepoAsync(adminClient, "Public Repo", "public-revisions", visibility: "Public");
        var privateRepo = await CreateRepoAsync(adminClient, "Private Repo", "private-revisions");
        var privateDoc = await CreateDocumentAsync(adminClient, privateRepo.Owner, privateRepo.Slug, "secret.md");
        privateDoc.CurrentRevisionId.Should().NotBeNull();

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync(
            $"/api/v1/repositories/{publicRepo.Owner}/{publicRepo.Slug}/revisions/{privateDoc.Id}/{privateDoc.CurrentRevisionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenProposal_CannotChangeContent()
    {
        await using var factory = new ScribegateWebAppFactory();
        var client = factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "author");
        Authenticate(client, token);

        var repo = await CreateRepoAsync(client, "Proposal Repo", "proposal-edit-lock");
        var document = await CreateDocumentAsync(client, repo.Owner, repo.Slug, "guide.md");
        var proposal = await CreateProposalAsync(client, repo.Owner, repo.Slug, document.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}",
            new { content = "# Updated after opening for review" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task StaleProposal_CannotBeApprovedAfterBaseDocumentChanges()
    {
        await using var factory = new ScribegateWebAppFactory();
        var adminClient = factory.CreateClient();
        var (_, adminToken) = await RegisterAsync(adminClient, "admin");
        Authenticate(adminClient, adminToken);
        await UpdateSettingAsync(adminClient, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(adminClient, "Stale Repo", "stale-proposals");
        var document = await CreateDocumentAsync(adminClient, repo.Owner, repo.Slug, "handbook.md");

        var authorClient = factory.CreateClient();
        var (authorUsername, authorToken) = await RegisterAsync(authorClient, "author");
        await AddMemberAsync(adminClient, repo.Owner, repo.Slug, authorUsername, "Contributor");
        Authenticate(authorClient, authorToken);

        var proposal = await CreateProposalAsync(authorClient, repo.Owner, repo.Slug, document.Id);

        var docUpdate = await adminClient.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents/handbook.md",
            new
            {
                content = "# Updated base",
                message = "move base forward",
            });
        docUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var approve = await adminClient.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/approve",
            content: null);

        approve.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateApiToken_RejectsUnsupportedScopes()
    {
        await using var factory = new ScribegateWebAppFactory();
        var client = factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "admin");
        Authenticate(client, token);

        var response = await client.PostAsJsonAsync("/api/v1/auth/tokens", new
        {
            name = "Scoped token",
            scopes = "read",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task EnablingEmailValidation_IsRejectedUntilVerificationFlowExists()
    {
        await using var factory = new ScribegateWebAppFactory();
        var client = factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "admin");
        Authenticate(client, token);

        var response = await client.PutAsJsonAsync("/api/v1/admin/settings/registration.email_validation", new
        {
            value = "true",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<(string Username, string Token)> RegisterAsync(HttpClient client, string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{prefix}-{suffix}";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            password = "correct-horse-battery-staple",
            acceptTos = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrWhiteSpace();
        return (username, body.Token!);
    }

    private static async Task<RepoResponse> CreateRepoAsync(HttpClient client, string name, string slug, string visibility = "Private")
    {
        var response = await client.PostAsJsonAsync("/api/v1/repositories", new
        {
            name,
            slug,
            visibility,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<RepoResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private static async Task<DocumentResponse> CreateDocumentAsync(HttpClient client, string owner, string slug, string path)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/documents",
            new
            {
                path,
                content = "# Seed",
                message = "seed",
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private static async Task<ProposalResponse> CreateProposalAsync(
        HttpClient client,
        string owner,
        string slug,
        Guid? documentId,
        string? documentPath = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/proposals",
            new
            {
                title = "Proposal",
                content = "# Proposed",
                documentId,
                documentPath,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ProposalResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private static async Task AddMemberAsync(HttpClient client, string owner, string slug, string username, string role)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/members",
            new
            {
                username,
                role,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task UpdateSettingAsync(HttpClient client, string key, string value)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/admin/settings/{key}", new { value });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class RegisterResponse
    {
        public string? Token { get; set; }
    }

    private sealed class RepoResponse
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private sealed class DocumentResponse
    {
        public Guid Id { get; set; }
        public Guid? CurrentRevisionId { get; set; }
    }

    private sealed class ProposalResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class MemberListResponse
    {
        public List<MemberResponse> Items { get; set; } = [];
    }

    private sealed class MemberResponse
    {
        public string? Email { get; set; }
    }

    private sealed class RegistrationStatusResponse
    {
        public bool RegistrationEnabled { get; set; }
        public bool RequireTos { get; set; }
        public string? TosUrl { get; set; }
        public string? PrivacyUrl { get; set; }
    }
}
