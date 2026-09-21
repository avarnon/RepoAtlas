using Microsoft.Extensions.Logging;
using RepoAtlas.McpServer.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RepoAtlas.McpServer.Storage;

/// <inheritdoc cref="IRepoDataStore" />
/// <remarks>
/// Backs the store with a single YAML file on disk. The document is loaded once at construction
/// and kept in memory; every <see cref="MutateAsync"/> call re-reads the file, applies the
/// mutation to that fresh copy, and only then serializes it back to disk and swaps it into
/// <see cref="_document"/>. Reads and mutations within this instance are serialized through a
/// single semaphore; every disk read (construction, <see cref="ReloadAsync"/>) and the whole
/// read-mutate-write cycle in <see cref="MutateAsync"/> additionally hold an OS-level,
/// cross-process file lock, so two <see cref="YamlDataStore"/> instances — in this process or
/// another — can't interleave their writes and clobber each other, race on the same temp file, or
/// have one read the file while the other is mid-rename into it.
/// </remarks>
public sealed class YamlDataStore : IRepoDataStore
{
    /// <summary>
    /// How long <see cref="AcquireCrossProcessLockAsync"/>/<see cref="AcquireCrossProcessLock"/>
    /// will poll for the lock before giving up, so a hung holder can't wedge startup or every
    /// subsequent read/mutate indefinitely.
    /// </summary>
    private static readonly TimeSpan CrossProcessLockTimeout = TimeSpan.FromSeconds(30);

    private readonly string _path;
    private readonly IAtomicFileWriter _atomicFileWriter;
    private readonly ILogger<YamlDataStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;
    private DataStoreDocument _document;

    /// <summary>
    /// Initializes a new instance of <see cref="YamlDataStore"/>, loading the document from
    /// <paramref name="path"/> if it exists.
    /// </summary>
    /// <param name="path">The path to the YAML data file.</param>
    /// <param name="atomicFileWriter">Used to persist the document atomically.</param>
    /// <param name="logger">Logger for load and mutation activity.</param>
    /// <exception cref="InvalidDataException">The file at <paramref name="path"/> exists but is not valid YAML.</exception>
    public YamlDataStore(string path, IAtomicFileWriter atomicFileWriter, ILogger<YamlDataStore> logger)
    {
        _path = path;
        _atomicFileWriter = atomicFileWriter;
        _logger = logger;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        using var crossProcessLock = AcquireCrossProcessLock();
        _document = Load();
    }

    /// <summary>
    /// Reads and deserializes the document from disk, or returns an empty document if the file
    /// doesn't exist or is blank.
    /// </summary>
    private DataStoreDocument Load()
    {
        if (!File.Exists(_path))
        {
            _logger.LogInformation("No data file found at {Path}; starting with an empty catalog.", _path);
            return new DataStoreDocument();
        }

        try
        {
            var yaml = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return new DataStoreDocument();
            }

            var document = _deserializer.Deserialize<DataStoreDocument>(yaml) ?? new DataStoreDocument();
            _logger.LogInformation("Loaded {RepoCount} repo(s) from {Path}.", document.Repos.Count, _path);
            return document;
        }
        catch (YamlException ex)
        {
            _logger.LogError(ex, "Data file at {Path} is not valid YAML.", _path);
            throw new InvalidDataException($"RepoAtlas data file at '{_path}' is not valid YAML.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<T> ReadAsync<T>(Func<DataStoreDocument, T> read, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return read(_document);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task MutateAsync(Action<DataStoreDocument> mutate, CancellationToken cancellationToken = default)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("YamlDataStore.MutateAsync");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var crossProcessLock = await AcquireCrossProcessLockAsync(cancellationToken);

            // Mutate a freshly-loaded copy rather than the live _document: another process (or a
            // hand edit) may have written to the file since this instance last loaded it, and if
            // `mutate` throws, _document must be left exactly as it was — swapping it in only on
            // success is what makes that guarantee (and the interface's "no changes are persisted
            // on throw" contract) actually hold, in memory as well as on disk.
            var latest = Load();
            mutate(latest);
            var yaml = _serializer.Serialize(latest);
            await _atomicFileWriter.WriteAllTextAsync(_path, yaml, cancellationToken);
            _document = latest;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Acquires an OS-level, cross-process exclusive lock backed by a sibling <c>&lt;path&gt;.lock</c>
    /// file, so a read-mutate-write cycle here can't interleave with another process (or another
    /// <see cref="YamlDataStore"/> instance) doing the same — including one reading the file mid-write
    /// via <see cref="File.Move(string, string, bool)"/>'s rename. Polls until acquired, canceled, or
    /// <see cref="CrossProcessLockTimeout"/> elapses.
    /// </summary>
    private Task<FileStream> AcquireCrossProcessLockAsync(CancellationToken cancellationToken) =>
        AcquireCrossProcessLockCoreAsync(cancellationToken, delay: ct => Task.Delay(25, ct));

    /// <summary>Synchronous counterpart to <see cref="AcquireCrossProcessLockAsync"/>, for use from the constructor.</summary>
    private FileStream AcquireCrossProcessLock() =>
        AcquireCrossProcessLockCoreAsync(CancellationToken.None, delay: _ =>
        {
            Thread.Sleep(25);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    /// <summary>
    /// Shared polling loop behind <see cref="AcquireCrossProcessLockAsync"/> and
    /// <see cref="AcquireCrossProcessLock"/>, which differ only in how they wait between attempts
    /// (a real async delay vs. a blocking <see cref="Thread.Sleep(int)"/> for the constructor's
    /// synchronous call site) — both resolve synchronously per iteration (no real async yield, and
    /// no ambient <c>SynchronizationContext</c> in this hosted console app), so the constructor
    /// blocking on this with <c>GetAwaiter().GetResult()</c> can't deadlock.
    /// </summary>
    private async Task<FileStream> AcquireCrossProcessLockCoreAsync(CancellationToken cancellationToken, Func<CancellationToken, Task> delay)
    {
        var lockPath = PrepareLockPath();
        var deadline = DateTime.UtcNow + CrossProcessLockTimeout;

        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await delay(cancellationToken);
            }
        }
    }

    private string PrepareLockPath()
    {
        var lockPath = _path + ".lock";
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return lockPath;
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("YamlDataStore.ReloadAsync");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            using var crossProcessLock = await AcquireCrossProcessLockAsync(cancellationToken);
            _document = Load();
        }
        finally
        {
            _lock.Release();
        }
    }
}
