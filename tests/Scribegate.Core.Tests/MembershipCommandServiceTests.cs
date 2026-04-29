using FluentAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Services;
using Xunit;

namespace Scribegate.Core.Tests;

// Boundary tests for MembershipCommandService. Mirrors DocumentCommandServiceTests:
// the service consumes a single port (IMembershipCommandContext), and
// InMemoryMembershipCommandContext below substitutes the EF/store/event-bus
// fan-out with plain Dictionary state so we can exercise every result branch
// without Web/Data dependencies.
public class MembershipCommandServiceTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private const string Owner = "alice";
    private const string RepoSlug = "notes";
    private const string TargetUsername = "bob";

    [Fact]
    public async Task Add_RepositoryNotFound_Returns_RepositoryNotFound()
    {
        var ctx = new InMemoryMembershipCommandContext();

        var result = await new MembershipCommandService(ctx).AddAsync(
            new AddMemberCommand(Owner, RepoSlug, TargetUsername, RepositoryRole.Reader, ActorId, "alice"), default);

        result.Should().BeOfType<MembershipCommandResult.RepositoryNotFoundCase>();
        ctx.PersistedMemberships.Should().BeEmpty();
        ctx.EmittedAdded.Should().BeNull();
    }

    [Fact]
    public async Task Add_TargetUserNotFound_Returns_TargetUserNotFound()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new MembershipCommandService(ctx).AddAsync(
            new AddMemberCommand(Owner, RepoSlug, "ghost", RepositoryRole.Reader, ActorId, "alice"), default);

        result.Should().BeOfType<MembershipCommandResult.TargetUserNotFoundCase>()
            .Which.Username.Should().Be("ghost");
        ctx.EmittedAdded.Should().BeNull();
    }

    [Fact]
    public async Task Add_AlreadyMember_Returns_AlreadyMember()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.SeedUser(TargetId, TargetUsername);
        ctx.SeedMembership(TargetId, RepositoryRole.Reader);

        var result = await new MembershipCommandService(ctx).AddAsync(
            new AddMemberCommand(Owner, RepoSlug, TargetUsername, RepositoryRole.Contributor, ActorId, "alice"), default);

        result.Should().BeOfType<MembershipCommandResult.AlreadyMemberCase>()
            .Which.Username.Should().Be(TargetUsername);
        ctx.EmittedAdded.Should().BeNull();
    }

    [Fact]
    public async Task Add_QuotaExceeded_Returns_QuotaExceeded()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.SeedUser(TargetId, TargetUsername);
        ctx.MaxMembersPerRepo = 3;
        ctx.MemberCount = 3;

        var result = await new MembershipCommandService(ctx).AddAsync(
            new AddMemberCommand(Owner, RepoSlug, TargetUsername, RepositoryRole.Reader, ActorId, "alice"), default);

        var quota = result.Should().BeOfType<MembershipCommandResult.QuotaExceededCase>().Subject;
        quota.MaxMembersPerRepo.Should().Be(3);
        quota.Tier.Should().Be("free");
        ctx.EmittedAdded.Should().BeNull();
    }

    [Fact]
    public async Task Add_Persists_And_Emits()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.SeedUser(TargetId, TargetUsername);

        var result = await new MembershipCommandService(ctx).AddAsync(
            new AddMemberCommand(Owner, RepoSlug, TargetUsername, RepositoryRole.Contributor, ActorId, "alice"), default);

        var added = result.Should().BeOfType<MembershipCommandResult.AddedCase>().Subject;
        added.UserId.Should().Be(TargetId);
        added.Username.Should().Be(TargetUsername);
        added.Role.Should().Be(RepositoryRole.Contributor);

        ctx.PersistedMemberships.Should().HaveCount(1);
        ctx.PersistedMemberships[0].Role.Should().Be(RepositoryRole.Contributor);

        ctx.EmittedAdded.Should().NotBeNull();
        ctx.EmittedAdded!.Target.Id.Should().Be(TargetId);
        ctx.EmittedAdded.Role.Should().Be(RepositoryRole.Contributor);
        ctx.EmittedAdded.OldRole.Should().BeNull();
    }

    [Fact]
    public async Task UpdateRole_RepositoryNotFound_Returns_RepositoryNotFound()
    {
        var ctx = new InMemoryMembershipCommandContext();

        var result = await new MembershipCommandService(ctx).UpdateRoleAsync(
            new UpdateMemberCommand(Owner, RepoSlug, TargetId, RepositoryRole.Reviewer, ActorId, "alice"), default);

        result.Should().BeOfType<MembershipCommandResult.RepositoryNotFoundCase>();
    }

    [Fact]
    public async Task UpdateRole_MemberNotFound_Returns_MemberNotFound()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new MembershipCommandService(ctx).UpdateRoleAsync(
            new UpdateMemberCommand(Owner, RepoSlug, TargetId, RepositoryRole.Reviewer, ActorId, "alice"), default);

        result.Should().BeOfType<MembershipCommandResult.MemberNotFoundCase>()
            .Which.UserId.Should().Be(TargetId);
        ctx.EmittedUpdated.Should().BeNull();
    }

    [Fact]
    public async Task UpdateRole_BumpsRoleAndEmits()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.SeedUser(TargetId, TargetUsername);
        ctx.SeedMembership(TargetId, RepositoryRole.Reader);

        var result = await new MembershipCommandService(ctx).UpdateRoleAsync(
            new UpdateMemberCommand(Owner, RepoSlug, TargetId, RepositoryRole.Reviewer, ActorId, "alice"), default);

        var updated = result.Should().BeOfType<MembershipCommandResult.UpdatedCase>().Subject;
        updated.OldRole.Should().Be(RepositoryRole.Reader);
        updated.NewRole.Should().Be(RepositoryRole.Reviewer);

        ctx.EmittedUpdated.Should().NotBeNull();
        ctx.EmittedUpdated!.Role.Should().Be(RepositoryRole.Reviewer);
        ctx.EmittedUpdated.OldRole.Should().Be(RepositoryRole.Reader);
    }

    [Fact]
    public async Task Remove_RepositoryNotFound_Returns_RepositoryNotFound()
    {
        var ctx = new InMemoryMembershipCommandContext();

        var result = await new MembershipCommandService(ctx).RemoveAsync(
            new RemoveMemberCommand(Owner, RepoSlug, TargetId, ActorId, "alice"), default);

        result.Should().BeOfType<MembershipCommandResult.RepositoryNotFoundCase>();
    }

    [Fact]
    public async Task Remove_MemberNotFound_Returns_MemberNotFound()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new MembershipCommandService(ctx).RemoveAsync(
            new RemoveMemberCommand(Owner, RepoSlug, TargetId, ActorId, "alice"), default);

        result.Should().BeOfType<MembershipCommandResult.MemberNotFoundCase>()
            .Which.UserId.Should().Be(TargetId);
        ctx.EmittedRemoved.Should().BeNull();
    }

    [Fact]
    public async Task Remove_DeletesAndEmits()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.SeedUser(TargetId, TargetUsername);
        ctx.SeedMembership(TargetId, RepositoryRole.Reviewer);

        var result = await new MembershipCommandService(ctx).RemoveAsync(
            new RemoveMemberCommand(Owner, RepoSlug, TargetId, ActorId, "alice"), default);

        result.Should().BeOfType<MembershipCommandResult.RemovedCase>();
        ctx.DeletedMemberships.Should().Contain((TargetId, ctx.Repository!.Id));
        ctx.EmittedRemoved.Should().NotBeNull();
        ctx.EmittedRemoved!.Target.Id.Should().Be(TargetId);
    }

    private static InMemoryMembershipCommandContext NewContextWithRepoAndActor()
    {
        var ctx = new InMemoryMembershipCommandContext();
        ctx.Repository = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "Notes",
            Slug = RepoSlug,
            OwnerId = Guid.NewGuid(),
        };
        ctx.Actor = new User
        {
            Id = ActorId,
            Username = "alice",
            Email = "alice@example.com",
            PasswordHash = "hash",
            Tier = "free",
        };
        return ctx;
    }
}

internal sealed class InMemoryMembershipCommandContext : IMembershipCommandContext
{
    public Repository? Repository { get; set; }
    public User? Actor { get; set; }
    public int MaxMembersPerRepo { get; set; } = 0;   // 0 = unlimited
    public int MemberCount { get; set; }

    public List<RepositoryMembership> PersistedMemberships { get; } = [];
    public List<(Guid UserId, Guid RepoId)> DeletedMemberships { get; } = [];
    public MembershipEmittedEvent? EmittedAdded { get; private set; }
    public MembershipEmittedEvent? EmittedUpdated { get; private set; }
    public MembershipEmittedEvent? EmittedRemoved { get; private set; }

    private readonly Dictionary<string, User> _byUsername = [];
    private readonly Dictionary<(Guid, Guid), RepositoryMembership> _byKey = [];

    public User SeedUser(Guid id, string username)
    {
        var user = new User
        {
            Id = id,
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Tier = "free",
        };
        _byUsername[username] = user;
        return user;
    }

    public RepositoryMembership SeedMembership(Guid userId, RepositoryRole role)
    {
        var user = _byUsername.Values.FirstOrDefault(u => u.Id == userId)
            ?? throw new InvalidOperationException("Seed the target user first.");
        var membership = new RepositoryMembership
        {
            UserId = userId,
            RepositoryId = Repository!.Id,
            Role = role,
            User = user,
        };
        _byKey[(userId, Repository.Id)] = membership;
        return membership;
    }

    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => Task.FromResult(Repository);

    public Task<User?> FindActorAsync(Guid userId, CancellationToken ct)
        => Task.FromResult(Actor);

    public Task<User?> FindUserByUsernameAsync(string username, CancellationToken ct)
        => Task.FromResult<User?>(_byUsername.GetValueOrDefault(username));

    public Task<RepositoryMembership?> FindMembershipAsync(Guid userId, Guid repositoryId, CancellationToken ct)
        => Task.FromResult<RepositoryMembership?>(_byKey.GetValueOrDefault((userId, repositoryId)));

    public Task<int> CountMembersAsync(Guid repositoryId, CancellationToken ct)
        => Task.FromResult(MemberCount);

    public Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct)
        => Task.FromResult(new TierLimits(
            MaxRepositories: 0,
            MaxDocumentsPerRepo: 0,
            MaxStorageMb: 0,
            MaxApiTokens: 0,
            MaxMembersPerRepo: MaxMembersPerRepo));

    public Task PersistMembershipAsync(RepositoryMembership membership, CancellationToken ct)
    {
        PersistedMemberships.Add(membership);
        _byKey[(membership.UserId, membership.RepositoryId)] = membership;
        return Task.CompletedTask;
    }

    public Task UpdateMembershipAsync(RepositoryMembership membership, CancellationToken ct)
    {
        _byKey[(membership.UserId, membership.RepositoryId)] = membership;
        return Task.CompletedTask;
    }

    public Task DeleteMembershipAsync(Guid userId, Guid repositoryId, CancellationToken ct)
    {
        DeletedMemberships.Add((userId, repositoryId));
        _byKey.Remove((userId, repositoryId));
        return Task.CompletedTask;
    }

    public Task EmitMemberAddedAsync(MembershipEmittedEvent evt, CancellationToken ct)
    {
        EmittedAdded = evt;
        return Task.CompletedTask;
    }

    public Task EmitMemberUpdatedAsync(MembershipEmittedEvent evt, CancellationToken ct)
    {
        EmittedUpdated = evt;
        return Task.CompletedTask;
    }

    public Task EmitMemberRemovedAsync(MembershipEmittedEvent evt, CancellationToken ct)
    {
        EmittedRemoved = evt;
        return Task.CompletedTask;
    }
}
