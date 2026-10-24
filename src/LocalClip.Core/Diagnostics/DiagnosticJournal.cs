using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace LocalClip.Core.Diagnostics;

/// <summary>
/// Local structured diagnostics only. TryRecord accepts into bounded RAM;
/// FlushAsync is the explicit durability fence. No callback or I/O runs on a producer.
/// </summary>
public sealed class DiagnosticJournal : IAsyncDisposable
{
    public const string FileName = "recording-diagnostics.json";
    private const int SchemaVersion = 1;
    private const string Readme = "Flick recording diagnostics\n\nThis user-exported package contains allowlisted recording events only.\nRecordings, settings, file paths, clip IDs, titles, credentials, webhook data and raw native logs are excluded.\nEvents are retained locally up to 256 entries, seven days and 256 KiB. No information is uploaded automatically.\nStorageState describes local journal persistence, not recording success.\n";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _path;
    private readonly DiagnosticAppVersion _version;
    private readonly TimeProvider _clock;
    private readonly List<DiagnosticEntry> _entries = new();
    private readonly Channel<bool> _dirty = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource _shutdown = new();
    private Task _writerTask = Task.CompletedTask;
    private Task? _disposeTask;
    private TaskCompletionSource _progress = NewProgress();
    private DiagnosticStorageState _state = DiagnosticStorageState.Ready;
    private DiagnosticStorageIssue _issue;
    private bool _accepting = true;
    private bool _storeExisted;
    private byte[]? _expectedHash;
    private long _revision;
    private long _persistedRevision;
    private long _dropped;

    private DiagnosticJournal(string directory, DiagnosticAppVersion version, TimeProvider clock)
    { _directory = directory; _path = Path.Combine(directory, FileName); _version = version; _clock = clock; }

    public static async Task<DiagnosticJournal> OpenAsync(string directory, DiagnosticAppVersion version,
        TimeProvider? clock = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(version);
        if (!version.IsValid) throw new ArgumentException("Invalid diagnostic application version.", nameof(version));
        cancellationToken.ThrowIfCancellationRequested();
        var journal = new DiagnosticJournal(Path.GetFullPath(directory), version, clock ?? TimeProvider.System);
        try { await journal.LoadAsync(cancellationToken).ConfigureAwait(false); }
        catch { journal._shutdown.Dispose(); throw; }
        journal._writerTask = Task.Run(journal.WriterLoopAsync);
        journal.SignalWriter();
        return journal;
    }

    public DiagnosticJournalStatus Status
    {
        get { lock (_gate) return new(_state, _issue, _entries.Count, _revision, _persistedRevision,
            _dropped, _revision > _persistedRevision); }
    }

    public bool TryRecord(DiagnosticObservation observation)
    {
        if (!DiagnosticPolicy.IsValid(observation)) return false;
        lock (_gate)
        {
            if (!_accepting) return false;
            PruneLocked();
            _entries.Add(new(_clock.GetUtcNow().ToUniversalTime(), observation, _version));
            while (_entries.Count > DiagnosticPolicy.MaximumEvents) { _entries.RemoveAt(0); AddDroppedLocked(1); }
            _revision++;
        }
        SignalWriter();
        return true;
    }

    public async Task<DiagnosticFlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long target;
        lock (_gate)
        {
            if (_state == DiagnosticStorageState.Disposed) return DiagnosticFlushResult.Disposed;
            PruneLocked();
            // An explicit flush also fences an ownership check of the on-disk file.
            // Without a new revision it could report an old write as durable after
            // another program replaced that file between recording events.
            target = ++_revision;
        }
        SignalWriter();
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_state == DiagnosticStorageState.Disposed) return DiagnosticFlushResult.Disposed;
                if (_state != DiagnosticStorageState.Ready) return DiagnosticFlushResult.StorageUnavailable;
                if (_persistedRevision >= target) return DiagnosticFlushResult.Persisted;
                changed = _progress.Task;
            }
            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<DiagnosticExportResult> ExportZipAsync(string destinationPath, bool overwriteConfirmed = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? temp = null;
        try
        {
            string destination = Path.GetFullPath(destinationPath);
            string? parent = Path.GetDirectoryName(destination);
            if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(destination, _path, StringComparison.OrdinalIgnoreCase) || parent == null ||
                !Directory.Exists(parent) || Directory.Exists(destination))
                return new(false, DiagnosticExportError.InvalidDestination);
            if (File.Exists(destination) && !overwriteConfirmed) return new(false, DiagnosticExportError.AlreadyExists);
            DiagnosticDocument document;
            lock (_gate)
            {
                if (!_accepting) return new(false, DiagnosticExportError.Disposed);
                if (PruneLocked()) _revision++;
                document = SnapshotLocked();
            }
            SignalWriter();
            byte[] json = SerializeBounded(document, out var bounded);
            cancellationToken.ThrowIfCancellationRequested();
            string candidate = Path.Combine(parent, ".flick-diagnostics-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var output = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8192, FileOptions.Asynchronous))
            {
                temp = candidate; // Only remove temporary files successfully created by this operation.
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await WriteEntryAsync(archive, "diagnostics.json", json, cancellationToken).ConfigureAwait(false);
                    await WriteEntryAsync(archive, "README.txt", Encoding.UTF8.GetBytes(Readme), cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            long bytes = new FileInfo(temp).Length;
            File.Move(temp, destination, overwriteConfirmed);
            temp = null;
            return new(true, DiagnosticExportError.None, bytes, bounded.Events.Length);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return new(false, DiagnosticExportError.AccessDenied); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { return new(false, DiagnosticExportError.InvalidDestination); }
        catch (IOException) { return new(false, DiagnosticExportError.IoFailure); }
        finally { DeleteOwnTemporary(temp); }
    }

    private async Task LoadAsync(CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var attributes = ExistingAttributes(_path);
            if (IsReparse(_directory) || attributes.HasValue &&
                (attributes.Value & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            { SetFailure(DiagnosticStorageState.Unavailable, DiagnosticStorageIssue.InvalidLocation); return; }
            if (!attributes.HasValue) { _revision = 1; return; }
            if (new FileInfo(_path).Length > DiagnosticPolicy.MaximumBytes)
            { SetFailure(DiagnosticStorageState.Malformed, DiagnosticStorageIssue.TooLarge); return; }
            byte[] bytes = await ReadBoundedAsync(_path, token).ConfigureAwait(false);
            using (var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }))
                RejectDuplicateProperties(document.RootElement);
            var stored = JsonSerializer.Deserialize<DiagnosticDocument>(bytes, JsonOptions);
            if (stored is null || stored.SchemaVersion != SchemaVersion)
            { SetFailure(DiagnosticStorageState.Malformed, DiagnosticStorageIssue.UnsupportedVersion); return; }
            var now = _clock.GetUtcNow();
            if (stored.Version is null || !stored.Version.IsValid || !Enum.IsDefined(stored.StorageState) || stored.DroppedEvents < 0 ||
                stored.Events is null || stored.Events.Length > DiagnosticPolicy.MaximumEvents || stored.Events.Any(entry =>
                    entry is null || entry.Version is null || !entry.Version.IsValid || !DiagnosticPolicy.IsValid(entry.Observation) ||
                    entry.TimestampUtc.Offset != TimeSpan.Zero || entry.TimestampUtc > now.AddMinutes(5)))
            { SetFailure(DiagnosticStorageState.Malformed, DiagnosticStorageIssue.InvalidData); return; }
            _entries.AddRange(stored.Events);
            _dropped = stored.DroppedEvents;
            _storeExisted = true;
            _expectedHash = SHA256.HashData(bytes);
            _revision = _persistedRevision = 1;
            if (PruneLocked()) _revision++;
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { SetFailure(DiagnosticStorageState.Unavailable, DiagnosticStorageIssue.AccessDenied); }
        catch (JsonException) { SetFailure(DiagnosticStorageState.Malformed, DiagnosticStorageIssue.InvalidData); }
        catch (InvalidDataException) { SetFailure(DiagnosticStorageState.Malformed, DiagnosticStorageIssue.InvalidData); }
        catch (IOException) { SetFailure(DiagnosticStorageState.Unavailable, DiagnosticStorageIssue.InvalidLocation); }
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            await foreach (bool _ in _dirty.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                DiagnosticDocument document;
                long revision;
                lock (_gate)
                {
                    if (_state != DiagnosticStorageState.Ready) continue;
                    if (PruneLocked()) _revision++;
                    document = SnapshotLocked();
                    revision = _revision;
                }
                string? temp = null;
                try
                {
                    byte[] bytes = SerializeBounded(document, out var bounded);
                    if (!await StoreIsUnchangedAsync(_shutdown.Token).ConfigureAwait(false))
                    { SetFailure(DiagnosticStorageState.Malformed, DiagnosticStorageIssue.ExternalModification); continue; }
                    byte[] hash = SHA256.HashData(bytes);
                    if (_storeExisted && _expectedHash != null && CryptographicOperations.FixedTimeEquals(hash, _expectedHash))
                    {
                        MarkPersisted(document, bounded, revision);
                        continue;
                    }
                    string candidate = Path.Combine(_directory, ".recording-diagnostics-" + Guid.NewGuid().ToString("N") + ".tmp");
                    await using (var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        8192, FileOptions.Asynchronous))
                    {
                        temp = candidate;
                        await file.WriteAsync(bytes, _shutdown.Token).ConfigureAwait(false);
                        await file.FlushAsync(_shutdown.Token).ConfigureAwait(false);
                    }
                    _shutdown.Token.ThrowIfCancellationRequested();
                    File.Move(temp, _path, true);
                    temp = null;
                    _expectedHash = hash;
                    _storeExisted = true;
                    MarkPersisted(document, bounded, revision);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
                catch (UnauthorizedAccessException) { SetFailure(DiagnosticStorageState.Unavailable, DiagnosticStorageIssue.AccessDenied); }
                catch (InvalidDataException) { SetFailure(DiagnosticStorageState.Malformed, DiagnosticStorageIssue.ExternalModification); }
                catch (IOException) { SetFailure(DiagnosticStorageState.Unavailable, DiagnosticStorageIssue.WriteFailed); }
                finally { DeleteOwnTemporary(temp); }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally { lock (_gate) PulseLocked(); }
    }

    private async Task<bool> StoreIsUnchangedAsync(CancellationToken token)
    {
        if (IsReparse(_directory)) return false;
        var attributes = ExistingAttributes(_path);
        bool exists = attributes.HasValue;
        if (exists != _storeExisted) return false;
        if (!exists) return true;
        if ((attributes!.Value & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
            new FileInfo(_path).Length > DiagnosticPolicy.MaximumBytes) return false;
        byte[] bytes = await ReadBoundedAsync(_path, token).ConfigureAwait(false);
        return _expectedHash != null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), _expectedHash);
    }

    private DiagnosticDocument SnapshotLocked() => new(SchemaVersion, _version, _state, _dropped, _entries.ToArray());

    private void MarkPersisted(DiagnosticDocument document, DiagnosticDocument bounded, long revision)
    {
        lock (_gate)
        {
            int removed = document.Events.Length - bounded.Events.Length;
            if (removed > 0)
            {
                var dropped = document.Events.Take(removed).ToHashSet(ReferenceEqualityComparer.Instance);
                AddDroppedLocked(_entries.RemoveAll(entry => dropped.Contains(entry)));
            }
            _persistedRevision = Math.Max(_persistedRevision, revision);
            PulseLocked();
        }
    }

    private bool PruneLocked()
    {
        var cutoff = _clock.GetUtcNow() - DiagnosticPolicy.Retention;
        int count = _entries.RemoveAll(entry => entry.TimestampUtc < cutoff);
        AddDroppedLocked(count);
        return count > 0;
    }

    private void AddDroppedLocked(int count) =>
        _dropped = _dropped > long.MaxValue - count ? long.MaxValue : _dropped + count;

    private static byte[] SerializeBounded(DiagnosticDocument source, out DiagnosticDocument bounded)
    {
        bounded = source;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(bounded, JsonOptions);
        while (bytes.Length > DiagnosticPolicy.MaximumBytes && bounded.Events.Length > 0)
        {
            bounded = bounded with { Events = bounded.Events[1..],
                DroppedEvents = bounded.DroppedEvents == long.MaxValue ? long.MaxValue : bounded.DroppedEvents + 1 };
            bytes = JsonSerializer.SerializeToUtf8Bytes(bounded, JsonOptions);
        }
        return bytes;
    }

    private void SignalWriter()
    { lock (_gate) { if (_state == DiagnosticStorageState.Ready) _dirty.Writer.TryWrite(true); } }

    private void SetFailure(DiagnosticStorageState state, DiagnosticStorageIssue issue)
    { lock (_gate) { if (_state != DiagnosticStorageState.Disposed) { _state = state; _issue = issue; } PulseLocked(); } }

    private void PulseLocked()
    { var previous = _progress; _progress = NewProgress(); previous.TrySetResult(); }

    private static TaskCompletionSource NewProgress() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static FileAttributes? ExistingAttributes(string path)
    {
        // File.Exists follows links and reports false for a dangling symlink.
        // Its directory entry still belongs to someone else and must survive.
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await file.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > DiagnosticPolicy.MaximumBytes) throw new InvalidDataException();
            bytes.Write(buffer, 0, read);
        }
        return bytes.ToArray();
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException(); RejectDuplicateProperties(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] bytes, CancellationToken token)
    {
        await using var output = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
        await output.WriteAsync(bytes, token).ConfigureAwait(false);
    }

    private static void DeleteOwnTemporary(string? path)
    { if (path != null) try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    public ValueTask DisposeAsync()
    { lock (_gate) return new(_disposeTask ??= DisposeCoreAsync()); }

    private async Task DisposeCoreAsync()
    {
        lock (_gate) _accepting = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await FlushAsync(deadline.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
        _dirty.Writer.TryComplete();
        try { await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { _shutdown.Cancel(); }
        lock (_gate) { _state = DiagnosticStorageState.Disposed; _issue = DiagnosticStorageIssue.Disposed; PulseLocked(); }
    }
}
