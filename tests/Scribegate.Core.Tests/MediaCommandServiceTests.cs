using AwesomeAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Services;
using Xunit;

namespace Scribegate.Core.Tests;

// Boundary tests for MediaCommandService. Mirrors DocumentCommandServiceTests /
// MembershipCommandServiceTests: the service consumes a single port
// (IMediaCommandContext), and InMemoryMediaCommandContext below substitutes
// the EF/store/disk/event-bus fan-out with plain Dictionary state so we can
// exercise every result branch without Web/Data dependencies.
public class MediaCommandServiceTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();
    private const string Owner = "alice";
    private const string RepoSlug = "notes";

    [Fact]
    public async Task Upload_RepositoryNotFound_Returns_RepositoryNotFound()
    {
        var ctx = new InMemoryMediaCommandContext();

        var result = await new MediaCommandService(ctx).UploadAsync(
            new UploadMediaCommand(Owner, RepoSlug, "x.png", "image/png", 10, ActorId, "alice"),
            new MemoryStream([1, 2, 3]), default);

        result.Should().BeOfType<MediaCommandResult.RepositoryNotFoundCase>();
        ctx.PersistedAssets.Should().BeEmpty();
        ctx.SavedFiles.Should().BeEmpty();
        ctx.EmittedUploaded.Should().BeNull();
    }

    [Fact]
    public async Task Upload_FileEmpty_Returns_FileEmpty()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new MediaCommandService(ctx).UploadAsync(
            new UploadMediaCommand(Owner, RepoSlug, "x.png", "image/png", 0, ActorId, "alice"),
            new MemoryStream(), default);

        result.Should().BeOfType<MediaCommandResult.FileEmptyCase>();
        ctx.SavedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_FileTooLarge_Returns_FileTooLarge()
    {
        var ctx = NewContextWithRepoAndActor();
        var oversized = MediaCommandService.MaxFileSizeBytes + 1;

        var result = await new MediaCommandService(ctx).UploadAsync(
            new UploadMediaCommand(Owner, RepoSlug, "x.png", "image/png", oversized, ActorId, "alice"),
            new MemoryStream(), default);

        var tooLarge = result.Should().BeOfType<MediaCommandResult.FileTooLargeCase>().Subject;
        tooLarge.ActualBytes.Should().Be(oversized);
        tooLarge.MaxBytes.Should().Be(MediaCommandService.MaxFileSizeBytes);
        ctx.SavedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_BadContentType_Returns_ContentTypeNotAllowed()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new MediaCommandService(ctx).UploadAsync(
            new UploadMediaCommand(Owner, RepoSlug, "x.exe", "application/x-msdownload", 100, ActorId, "alice"),
            new MemoryStream(new byte[100]), default);

        var bad = result.Should().BeOfType<MediaCommandResult.ContentTypeNotAllowedCase>().Subject;
        bad.ContentType.Should().Be("application/x-msdownload");
        bad.Allowed.Should().Contain("image/png");
        ctx.SavedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_ContentType_Lowercased_BeforeCheck()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new MediaCommandService(ctx).UploadAsync(
            new UploadMediaCommand(Owner, RepoSlug, "x.png", "IMAGE/PNG", 10, ActorId, "alice"),
            new MemoryStream(new byte[10]), default);

        result.Should().BeOfType<MediaCommandResult.UploadedCase>()
            .Which.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Upload_StorageQuotaExceeded_Returns_StorageQuotaExceeded()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.MaxStorageMb = 1;
        ctx.StorageUsageBytes = (long)(0.9 * 1024 * 1024); // 0.9 MB used

        var result = await new MediaCommandService(ctx).UploadAsync(
            new UploadMediaCommand(Owner, RepoSlug, "x.png", "image/png",
                SizeBytes: (long)(0.5 * 1024 * 1024), ActorId, "alice"), // 0.5 MB upload
            new MemoryStream(new byte[16]), default);

        var quota = result.Should().BeOfType<MediaCommandResult.StorageQuotaExceededCase>().Subject;
        quota.MaxStorageMb.Should().Be(1);
        ctx.SavedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_Persists_And_Emits()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new MediaCommandService(ctx).UploadAsync(
            new UploadMediaCommand(Owner, RepoSlug, "diagram.png", "image/png", 1024, ActorId, "alice"),
            new MemoryStream(new byte[1024]), default);

        var uploaded = result.Should().BeOfType<MediaCommandResult.UploadedCase>().Subject;
        uploaded.FileName.Should().Be("diagram.png");
        uploaded.ContentType.Should().Be("image/png");
        uploaded.SizeBytes.Should().Be(1024);
        uploaded.UploaderUsername.Should().Be("alice");

        ctx.PersistedAssets.Should().HaveCount(1);
        ctx.PersistedAssets[0].FileName.Should().Be("diagram.png");
        ctx.PersistedAssets[0].UploadedById.Should().Be(ActorId);

        ctx.SavedFiles.Should().HaveCount(1);
        ctx.SavedFiles[0].FileExtension.Should().Be(".png");
        ctx.SavedFiles[0].RepositoryId.Should().Be(ctx.Repository!.Id);

        ctx.EmittedUploaded.Should().NotBeNull();
        ctx.EmittedUploaded!.Asset.Id.Should().Be(uploaded.AssetId);
    }

    [Fact]
    public async Task Delete_RepositoryNotFound_Returns_RepositoryNotFound()
    {
        var ctx = new InMemoryMediaCommandContext();

        var result = await new MediaCommandService(ctx).DeleteAsync(
            new DeleteMediaCommand(Owner, RepoSlug, Guid.NewGuid(), ActorId, "alice"), default);

        result.Should().BeOfType<MediaCommandResult.RepositoryNotFoundCase>();
    }

    [Fact]
    public async Task Delete_AssetMissing_Returns_MediaNotFound()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new MediaCommandService(ctx).DeleteAsync(
            new DeleteMediaCommand(Owner, RepoSlug, Guid.NewGuid(), ActorId, "alice"), default);

        result.Should().BeOfType<MediaCommandResult.MediaNotFoundCase>();
    }

    [Fact]
    public async Task Delete_AssetInWrongRepo_Returns_MediaNotFound()
    {
        var ctx = NewContextWithRepoAndActor();
        var foreign = ctx.SeedAsset(Guid.NewGuid(), uploaderId: ActorId, repoId: Guid.NewGuid());

        var result = await new MediaCommandService(ctx).DeleteAsync(
            new DeleteMediaCommand(Owner, RepoSlug, foreign.Id, ActorId, "alice"), default);

        result.Should().BeOfType<MediaCommandResult.MediaNotFoundCase>();
        ctx.DeletedAssets.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NotUploaderNotAdmin_Returns_Forbidden()
    {
        var ctx = NewContextWithRepoAndActor();
        // Asset uploaded by someone else; actor is not admin.
        var asset = ctx.SeedAsset(Guid.NewGuid(), uploaderId: OtherId, repoId: ctx.Repository!.Id);

        var result = await new MediaCommandService(ctx).DeleteAsync(
            new DeleteMediaCommand(Owner, RepoSlug, asset.Id, ActorId, "alice"), default);

        result.Should().BeOfType<MediaCommandResult.ForbiddenCase>();
        ctx.DeletedAssets.Should().BeEmpty();
        ctx.DeletedFiles.Should().BeEmpty();
        ctx.EmittedDeleted.Should().BeNull();
    }

    [Fact]
    public async Task Delete_AdminCanDeleteOthersUpload()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.Actor!.IsAdmin = true;
        var asset = ctx.SeedAsset(Guid.NewGuid(), uploaderId: OtherId, repoId: ctx.Repository!.Id);

        var result = await new MediaCommandService(ctx).DeleteAsync(
            new DeleteMediaCommand(Owner, RepoSlug, asset.Id, ActorId, "alice"), default);

        result.Should().BeOfType<MediaCommandResult.DeletedCase>();
        ctx.DeletedAssets.Should().Contain(asset.Id);
    }

    [Fact]
    public async Task Delete_DeletesFileAndRecordAndEmits()
    {
        var ctx = NewContextWithRepoAndActor();
        var asset = ctx.SeedAsset(Guid.NewGuid(), uploaderId: ActorId, repoId: ctx.Repository!.Id);

        var result = await new MediaCommandService(ctx).DeleteAsync(
            new DeleteMediaCommand(Owner, RepoSlug, asset.Id, ActorId, "alice"), default);

        result.Should().BeOfType<MediaCommandResult.DeletedCase>();
        ctx.DeletedAssets.Should().Contain(asset.Id);
        ctx.DeletedFiles.Should().Contain(asset.StoragePath);
        ctx.EmittedDeleted.Should().NotBeNull();
        ctx.EmittedDeleted!.Asset.Id.Should().Be(asset.Id);
    }

    private static InMemoryMediaCommandContext NewContextWithRepoAndActor()
    {
        var ctx = new InMemoryMediaCommandContext();
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

internal sealed class InMemoryMediaCommandContext : IMediaCommandContext
{
    public Repository? Repository { get; set; }
    public User? Actor { get; set; }
    public int MaxStorageMb { get; set; } = 0; // 0 = unlimited
    public long StorageUsageBytes { get; set; } = 0;

    public List<MediaAsset> PersistedAssets { get; } = [];
    public List<Guid> DeletedAssets { get; } = [];
    public List<string> DeletedFiles { get; } = [];
    public List<(Guid RepositoryId, Guid AssetId, string FileExtension, byte[] Bytes)> SavedFiles { get; } = [];
    public MediaEmittedEvent? EmittedUploaded { get; private set; }
    public MediaEmittedEvent? EmittedDeleted { get; private set; }

    private readonly Dictionary<Guid, MediaAsset> _assets = [];

    public MediaAsset SeedAsset(Guid id, Guid uploaderId, Guid repoId)
    {
        var asset = new MediaAsset
        {
            Id = id,
            RepositoryId = repoId,
            FileName = "seed.png",
            ContentType = "image/png",
            SizeBytes = 100,
            StoragePath = $"/data/media/{repoId}/{id}.png",
            UploadedById = uploaderId,
        };
        _assets[id] = asset;
        return asset;
    }

    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => Task.FromResult(Repository);

    public Task<User?> FindActorAsync(Guid userId, CancellationToken ct)
        => Task.FromResult(Actor);

    public Task<MediaAsset?> FindAssetAsync(Guid assetId, CancellationToken ct)
        => Task.FromResult<MediaAsset?>(_assets.GetValueOrDefault(assetId));

    public Task<long> GetStorageUsageByUserAsync(Guid userId, CancellationToken ct)
        => Task.FromResult(StorageUsageBytes);

    public Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct)
        => Task.FromResult(new TierLimits(
            MaxRepositories: 0,
            MaxDocumentsPerRepo: 0,
            MaxStorageMb: MaxStorageMb,
            MaxApiTokens: 0,
            MaxMembersPerRepo: 0));

    public async Task<string> SaveAssetFileAsync(
        Guid repositoryId, Guid assetId, string fileExtension, Stream content, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        SavedFiles.Add((repositoryId, assetId, fileExtension, ms.ToArray()));
        return $"/data/media/{repositoryId}/{assetId}{fileExtension}";
    }

    public Task DeleteAssetFileAsync(string storagePath, CancellationToken ct)
    {
        DeletedFiles.Add(storagePath);
        return Task.CompletedTask;
    }

    public Task PersistAssetAsync(MediaAsset asset, CancellationToken ct)
    {
        PersistedAssets.Add(asset);
        _assets[asset.Id] = asset;
        return Task.CompletedTask;
    }

    public Task DeleteAssetAsync(Guid assetId, CancellationToken ct)
    {
        DeletedAssets.Add(assetId);
        _assets.Remove(assetId);
        return Task.CompletedTask;
    }

    public Task EmitMediaUploadedAsync(MediaEmittedEvent evt, CancellationToken ct)
    {
        EmittedUploaded = evt;
        return Task.CompletedTask;
    }

    public Task EmitMediaDeletedAsync(MediaEmittedEvent evt, CancellationToken ct)
    {
        EmittedDeleted = evt;
        return Task.CompletedTask;
    }
}
