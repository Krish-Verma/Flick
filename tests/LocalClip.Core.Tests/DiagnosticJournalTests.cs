using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;

namespace LocalClip.Core.Tests;

public sealed class DiagnosticJournalTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] PrivateCanaries =
    [
        "PRIVATE_WINDOW_TITLE_CANARY", "PRIVATE_EXECUTABLE_PATH_CANARY", "PRIVATE_SOURCE_KIND_CANARY",
        "PRIVATE_ENCODER_ID_CANARY", "PRIVATE_ENCODER_NAME_CANARY", "PRIVATE_NATIVE_ERROR_CANARY",
        "PRIVATE_ERROR_CODE_CANARY", "PRIVATE_SAVE_ID_CANARY", "PRIVATE_CLIP_ID_CANARY",
        "PRIVATE_HOTKEY_ACTION_CANARY", "PRIVATE_HOTKEY_CHORD_CANARY", "PRIVATE_HOTKEY_MESSAGE_CANARY",
        "PRIVATE_WEBHOOK_TOKEN_CANARY", "PRIVATE_DEVICE_ID_CANARY", "PRIVATE_FOREIGN_FIELD_CANARY",
        "4dab5598-c202-463f-bd92-c32ac73d8db1"
    ];

    [Fact]
    public async Task ProjectionJournalAndZipExcludeEveryPrivateHealthString()
    {
        using var fixture = new Sandbox();
        var clock = new ManualClock();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), clock);

        foreach (bool poisonIdentifiers in new[] { false, true })
        {
            var observation = DiagnosticObservation.FromHealth(DiagnosticEventKind.SaveValidated,
                PrivateHealth(clock.GetUtcNow(), poisonIdentifiers), DiagnosticErrorCode.EngineFault);
            AssertPrivateCanariesAbsent(JsonSerializer.Serialize(observation, JsonOptions));
            Assert.True(journal.TryRecord(observation));
        }

        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        AssertPrivateCanariesAbsent(File.ReadAllText(fixture.JournalFile));
        string destination = Path.Combine(fixture.Root, "diagnostics.zip");
        var exported = await journal.ExportZipAsync(destination);

        Assert.True(exported.Success);
        Assert.Equal(2, exported.EventCount);
        Assert.Equal(new FileInfo(destination).Length, exported.Bytes);
        var document = ReadExport(destination);
        Assert.Equal(2, document.Events.Length);
        Assert.Equal(DiagnosticEncoder.CpuX264, document.Events[0].Observation.Encoder);
        Assert.Equal(DiagnosticEncoder.Unknown, document.Events[1].Observation.Encoder);
        Assert.Equal(DiagnosticSourceKind.Window, document.Events[0].Observation.SourceKind);
        Assert.Equal(DiagnosticSourceKind.Unknown, document.Events[1].Observation.SourceKind);
    }

    [Fact]
    public async Task RingRetainsTheNewest256EventsWithinTheByteBudgetAfterReload()
    {
        using var fixture = new Sandbox();
        var clock = new ManualClock();
        await using (var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), clock))
        {
            for (int index = 0; index < 400; index++)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                Assert.True(journal.TryRecord(Observation(index)));
            }

            Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
            Assert.Equal(DiagnosticPolicy.MaximumEvents, journal.Status.RetainedEvents);
            Assert.True(journal.Status.DroppedEvents >= 400 - DiagnosticPolicy.MaximumEvents);
            Assert.Equal(journal.Status.Revision, journal.Status.PersistedRevision);
            Assert.False(journal.Status.PendingWrite);
            Assert.InRange(new FileInfo(fixture.JournalFile).Length, 1, DiagnosticPolicy.MaximumBytes);
            var document = ReadDocument(fixture.JournalFile);
            Assert.Equal(Enumerable.Range(144, 256).Select(value => (long?)value),
                document.Events.Select(item => item.Observation.LatencyMilliseconds));
        }

        await using var reopened = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), clock);
        Assert.Equal(DiagnosticStorageState.Ready, reopened.Status.StorageState);
        Assert.Equal(256, reopened.Status.RetainedEvents);
        Assert.Equal(DiagnosticFlushResult.Persisted, await reopened.FlushAsync());
        Assert.Equal(256, ReadDocument(fixture.JournalFile).Events.Length);
    }

    [Fact]
    public async Task FullyPopulatedValidEventsCannotExceedThePersistenceByteBudget()
    {
        using var fixture = new Sandbox();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock());
        var dense = new DiagnosticObservation(DiagnosticEventKind.DiagnosticTestCompleted,
            RecordingEngineState.Stopping, ReplaySaveState.Validating, CaptureHostHealth.Unresponsive,
            DiagnosticSourceKind.Monitor, DiagnosticEncoder.IntelLegacyCompatibility, DiagnosticErrorCode.StorageUnavailable,
            DiagnosticHotkeyAction.ToggleReplay, HotkeyRegistered: false, NativeError: int.MaxValue,
            Width: 16384, Height: 16384, Fps: 1000, BufferedMilliseconds: 600000,
            MaximumBufferMilliseconds: 600000, DurationMilliseconds: 600000, LatencyMilliseconds: 3600000,
            MemoryBytes: long.MaxValue, SoftwareFallback: true, BufferEstimated: true, BufferTrackingLimited: true);
        for (int index = 0; index < DiagnosticPolicy.MaximumEvents; index++)
            Assert.True(journal.TryRecord(dense));

        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());

        Assert.InRange(new FileInfo(fixture.JournalFile).Length, 1, DiagnosticPolicy.MaximumBytes);
        var document = ReadDocument(fixture.JournalFile);
        Assert.InRange(document.Events.Length, 1, DiagnosticPolicy.MaximumEvents);
        Assert.Equal(document.Events.Length, journal.Status.RetainedEvents);
    }

    [Fact]
    public async Task ReloadUnderANewVersionPreservesEachEventsOriginalVersion()
    {
        using var fixture = new Sandbox();
        var clock = new ManualClock();
        var previousVersion = Version("1.0.5-preview.1");
        var currentVersion = Version();
        await using (var previous = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, previousVersion, clock))
        {
            Assert.True(previous.TryRecord(Observation(1)));
            Assert.Equal(DiagnosticFlushResult.Persisted, await previous.FlushAsync());
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        await using var current = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, currentVersion, clock);
        Assert.True(current.TryRecord(Observation(2)));
        Assert.Equal(DiagnosticFlushResult.Persisted, await current.FlushAsync());

        var document = ReadDocument(fixture.JournalFile);
        Assert.Equal(currentVersion, document.Version);
        Assert.Equal(2, document.Events.Length);
        Assert.Equal(previousVersion, document.Events[0].Version);
        Assert.Equal(currentVersion, document.Events[1].Version);
    }

    [Fact]
    public async Task SevenDayExpiryIsEnforcedOnFlushAndExportWithoutNewEvents()
    {
        using var fixture = new Sandbox();
        var clock = new ManualClock();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), clock);
        Assert.True(journal.TryRecord(Observation(1)));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());

        clock.Advance(DiagnosticPolicy.Retention + TimeSpan.FromSeconds(1));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        Assert.Empty(ReadDocument(fixture.JournalFile).Events);
        Assert.Equal(0, journal.Status.RetainedEvents);

        string destination = Path.Combine(fixture.Root, "expired.zip");
        Assert.True((await journal.ExportZipAsync(destination)).Success);
        Assert.Empty(ReadExport(destination).Events);
    }

    [Fact]
    public async Task ExportRechecksAgeWhenNoRecordOrFlushWasRequested()
    {
        using var fixture = new Sandbox();
        var clock = new ManualClock();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), clock);
        Assert.True(journal.TryRecord(Observation(1)));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        clock.Advance(DiagnosticPolicy.Retention + TimeSpan.FromSeconds(1));

        string destination = Path.Combine(fixture.Root, "expired-without-flush.zip");
        var exported = await journal.ExportZipAsync(destination);

        Assert.True(exported.Success);
        Assert.Equal(0, exported.EventCount);
        Assert.Empty(ReadExport(destination).Events);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("empty")]
    [InlineData("unknown-root")]
    [InlineData("unknown-nested")]
    [InlineData("duplicate-key")]
    [InlineData("future-schema")]
    [InlineData("unknown-enum")]
    [InlineData("future-timestamp")]
    [InlineData("missing-entry-version")]
    [InlineData("oversized")]
    public async Task UnsupportedFilesArePreservedByteForByteAndNewEventsRemainPrivateInMemory(string fault)
    {
        using var fixture = new Sandbox();
        var clock = new ManualClock();
        byte[] valid = await SeedAsync(fixture, clock);
        byte[] damaged = Mutate(valid, fault);
        File.WriteAllBytes(fixture.JournalFile, damaged);

        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), clock);
        Assert.NotEqual(DiagnosticStorageState.Ready, journal.Status.StorageState);
        Assert.NotEqual(DiagnosticStorageIssue.None, journal.Status.StorageIssue);
        Assert.Equal(0, journal.Status.RetainedEvents);
        if (fault == "oversized") Assert.Equal(DiagnosticStorageIssue.TooLarge, journal.Status.StorageIssue);
        Assert.True(journal.TryRecord(Observation(8)));
        Assert.Equal(DiagnosticFlushResult.StorageUnavailable, await journal.FlushAsync());
        Assert.Equal(damaged, File.ReadAllBytes(fixture.JournalFile));

        string destination = Path.Combine(fixture.Root, "memory-only.zip");
        var exported = await journal.ExportZipAsync(destination);
        Assert.True(exported.Success);
        Assert.Equal(1, exported.EventCount);
        Assert.Single(ReadExport(destination).Events);
        Assert.Equal(damaged, File.ReadAllBytes(fixture.JournalFile));
    }

    [Fact]
    public async Task AFileBlockingTheJournalDirectoryCannotBreakInMemoryDiagnosticsOrExport()
    {
        using var fixture = new Sandbox();
        byte[] sentinel = Encoding.UTF8.GetBytes("PRIVATE_FOREIGN_FIELD_CANARY: preserve this unrelated file");
        File.WriteAllBytes(fixture.JournalDirectory, sentinel);
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock());

        Assert.Equal(DiagnosticStorageState.Unavailable, journal.Status.StorageState);
        Assert.NotEqual(DiagnosticStorageIssue.None, journal.Status.StorageIssue);
        Assert.True(journal.TryRecord(Observation(4)));
        Assert.Equal(DiagnosticFlushResult.StorageUnavailable, await journal.FlushAsync());
        Assert.Equal(sentinel, File.ReadAllBytes(fixture.JournalDirectory));
        string destination = Path.Combine(fixture.Root, "unavailable-storage.zip");
        Assert.True((await journal.ExportZipAsync(destination)).Success);
        Assert.Single(ReadExport(destination).Events);
        Assert.Equal(sentinel, File.ReadAllBytes(fixture.JournalDirectory));
    }

    [DiagnosticSymlinkFact]
    public async Task ExistingDanglingJournalLinkIsPreservedDuringRecordingFlushAndExport()
    {
        using var fixture = new Sandbox();
        Directory.CreateDirectory(fixture.JournalDirectory);
        string missingTarget = Path.Combine(fixture.Root, "PRIVATE_FOREIGN_FIELD_CANARY-never-created.json");
        File.CreateSymbolicLink(fixture.JournalFile, missingTarget);
        string originalTarget = new FileInfo(fixture.JournalFile).LinkTarget!;
        Assert.NotNull(originalTarget);
        Assert.False(File.Exists(missingTarget));
        Assert.True((File.GetAttributes(fixture.JournalFile) & FileAttributes.ReparsePoint) != 0);

        await using (var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock()))
        {
            Assert.NotEqual(DiagnosticStorageState.Ready, journal.Status.StorageState);
            Assert.True(journal.TryRecord(Observation(9)));
            Assert.Equal(DiagnosticFlushResult.StorageUnavailable, await journal.FlushAsync());
            Assert.Equal(originalTarget, new FileInfo(fixture.JournalFile).LinkTarget);
            string destination = Path.Combine(fixture.Root, "dangling-journal.zip");
            Assert.True((await journal.ExportZipAsync(destination)).Success);
            Assert.Single(ReadExport(destination).Events);
            Assert.Equal(originalTarget, new FileInfo(fixture.JournalFile).LinkTarget);
        }

        Assert.Equal(originalTarget, new FileInfo(fixture.JournalFile).LinkTarget);
        Assert.False(File.Exists(missingTarget));
    }

    [DiagnosticSymlinkFact]
    public async Task ExternalDanglingReplacementCannotBeOverwrittenByFlushOrDispose()
    {
        using var fixture = new Sandbox();
        string missingTarget = Path.Combine(fixture.Root, "PRIVATE_FOREIGN_FIELD_CANARY-never-created.json");
        string? originalTarget;
        await using (var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock()))
        {
            Assert.True(journal.TryRecord(Observation(1)));
            Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
            File.Delete(fixture.JournalFile);
            File.CreateSymbolicLink(fixture.JournalFile, missingTarget);
            originalTarget = new FileInfo(fixture.JournalFile).LinkTarget;
            Assert.NotNull(originalTarget);
            Assert.True(journal.TryRecord(Observation(2)));

            Assert.Equal(DiagnosticFlushResult.StorageUnavailable, await journal.FlushAsync());
            Assert.Equal(DiagnosticStorageIssue.ExternalModification, journal.Status.StorageIssue);
            Assert.Equal(originalTarget, new FileInfo(fixture.JournalFile).LinkTarget);
            string destination = Path.Combine(fixture.Root, "dangling-replacement.zip");
            Assert.True((await journal.ExportZipAsync(destination)).Success);
            Assert.Equal(2, ReadExport(destination).Events.Length);
        }

        Assert.Equal(originalTarget, new FileInfo(fixture.JournalFile).LinkTarget);
        Assert.False(File.Exists(missingTarget));
    }

    [Fact]
    public async Task ExternalModificationIsNeverOverwrittenByLaterFlushes()
    {
        using var fixture = new Sandbox();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock());
        Assert.True(journal.TryRecord(Observation(1)));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        byte[] foreign = Encoding.UTF8.GetBytes("{ PRIVATE_FOREIGN_FIELD_CANARY }\r\n");
        File.WriteAllBytes(fixture.JournalFile, foreign);
        Assert.True(journal.TryRecord(Observation(2)));

        Assert.Equal(DiagnosticFlushResult.StorageUnavailable, await journal.FlushAsync());
        Assert.Equal(DiagnosticStorageIssue.ExternalModification, journal.Status.StorageIssue);
        Assert.Equal(foreign, File.ReadAllBytes(fixture.JournalFile));
        string destination = Path.Combine(fixture.Root, "external-change.zip");
        Assert.True((await journal.ExportZipAsync(destination)).Success);
        Assert.Equal(2, ReadExport(destination).Events.Length);
        Assert.Equal(foreign, File.ReadAllBytes(fixture.JournalFile));
    }

    [Fact]
    public async Task FlushRevalidatesExternalChangesEvenWithoutAnotherRecordedEvent()
    {
        using var fixture = new Sandbox();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock());
        Assert.True(journal.TryRecord(Observation(1)));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        byte[] foreign = Encoding.UTF8.GetBytes("{ PRIVATE_FOREIGN_FIELD_CANARY: changed after durability fence }");
        File.WriteAllBytes(fixture.JournalFile, foreign);

        Assert.Equal(DiagnosticFlushResult.StorageUnavailable, await journal.FlushAsync());

        Assert.Equal(DiagnosticStorageIssue.ExternalModification, journal.Status.StorageIssue);
        Assert.Equal(1, journal.Status.RetainedEvents);
        Assert.Equal(foreign, File.ReadAllBytes(fixture.JournalFile));
    }

    [Fact]
    public async Task ConcurrentAppendsAndFlushFencesPersistEveryAcceptedEventExactlyOnce()
    {
        using var fixture = new Sandbox();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock());
        int sequence = -1;
        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (int index = 0; index < 32; index++)
            {
                int id = Interlocked.Increment(ref sequence);
                Assert.True(journal.TryRecord(Observation(id)));
                if (index % 8 == 0)
                    Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
            }
        }));
        await Task.WhenAll(writers).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10)));

        var document = ReadDocument(fixture.JournalFile);
        Assert.Equal(128, document.Events.Length);
        Assert.Equal(Enumerable.Range(0, 128).Select(value => (long?)value),
            document.Events.Select(item => item.Observation.LatencyMilliseconds).Order());
        Assert.Equal(128, journal.Status.RetainedEvents);
        Assert.Equal(journal.Status.Revision, journal.Status.PersistedRevision);
        Assert.False(journal.Status.PendingWrite);
    }

    [Fact]
    public async Task CancelledOrUnconfirmedExportPreservesExistingDestinationAndLeavesNoPartialFile()
    {
        using var fixture = new Sandbox();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock());
        Assert.True(journal.TryRecord(Observation(3)));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        string destination = Path.Combine(fixture.Root, "existing.zip");
        byte[] existing = Encoding.UTF8.GetBytes("Existing synthetic document: do not replace without confirmation");
        File.WriteAllBytes(destination, existing);
        string[] before = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();

        var refused = await journal.ExportZipAsync(destination);
        Assert.False(refused.Success);
        Assert.Equal(DiagnosticExportError.AlreadyExists, refused.Error);
        Assert.Equal(existing, File.ReadAllBytes(destination));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            journal.ExportZipAsync(destination, overwriteConfirmed: true, cancellationToken: cancellation.Token));
        Assert.Equal(existing, File.ReadAllBytes(destination));
        Assert.Equal(before, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray());

        Assert.True((await journal.ExportZipAsync(destination, overwriteConfirmed: true)).Success);
        Assert.Single(ReadExport(destination).Events);
    }

    [Fact]
    public async Task ExportCannotOverwriteItsOwnPersistentJournal()
    {
        using var fixture = new Sandbox();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock());
        Assert.True(journal.TryRecord(Observation(3)));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        byte[] before = File.ReadAllBytes(fixture.JournalFile);

        var exported = await journal.ExportZipAsync(fixture.JournalFile, overwriteConfirmed: true);

        Assert.False(exported.Success);
        Assert.Equal(DiagnosticExportError.InvalidDestination, exported.Error);
        Assert.Equal(before, File.ReadAllBytes(fixture.JournalFile));
    }

    [Fact]
    public async Task InvalidEnumsAndUnboundedNumericsNeverEnterTheRingOrFile()
    {
        using var fixture = new Sandbox();
        await using var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), new ManualClock());
        DiagnosticObservation[] invalid =
        [
            new(DiagnosticEventKind.Unknown),
            new((DiagnosticEventKind)int.MaxValue),
            new(DiagnosticEventKind.EngineChanged, EngineState: (RecordingEngineState)int.MaxValue),
            new(DiagnosticEventKind.EngineChanged, Encoder: (DiagnosticEncoder)int.MaxValue),
            new(DiagnosticEventKind.EngineChanged, Width: -1),
            new(DiagnosticEventKind.EngineChanged, Height: 1000000),
            new(DiagnosticEventKind.EngineChanged, Fps: 0),
            new(DiagnosticEventKind.EngineChanged, MemoryBytes: -1),
            new(DiagnosticEventKind.EngineChanged, BufferedMilliseconds: 600001),
            new(DiagnosticEventKind.EngineChanged, LatencyMilliseconds: long.MaxValue)
        ];
        foreach (var item in invalid) Assert.False(journal.TryRecord(item));
        Assert.Equal(0, journal.Status.RetainedEvents);
        Assert.True(journal.TryRecord(Observation(1)));
        Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        Assert.Single(ReadDocument(fixture.JournalFile).Events);
    }

    [Theory]
    [InlineData("1.0.6+PRIVATE_WEBHOOK_TOKEN_CANARY")]
    [InlineData("1.0.6-PRIVATE_WEBHOOK_TOKEN_CANARY")]
    [InlineData("1.0.6-preview.PRIVATE_WEBHOOK_TOKEN_CANARY")]
    [InlineData("C:\\PRIVATE_EXECUTABLE_PATH_CANARY\\Flick.exe")]
    public void ArbitraryVersionStringsCannotEnterTheExportSchema(string version) =>
        Assert.False(DiagnosticAppVersion.TryParse(version, out _));

    private static DiagnosticObservation Observation(int sequence) =>
        new(DiagnosticEventKind.EngineChanged, EngineState: RecordingEngineState.Buffering,
            LatencyMilliseconds: sequence, BufferEstimated: true);

    private static DiagnosticAppVersion Version(string value = "1.0.6-preview.1")
    {
        Assert.True(DiagnosticAppVersion.TryParse(value, out var version));
        return version;
    }

    private static RecordingHealthSnapshot PrivateHealth(DateTimeOffset now, bool poisonIdentifiers)
    {
        var session = Guid.Parse("4dab5598-c202-463f-bd92-c32ac73d8db1");
        var source = new RecordingSource(poisonIdentifiers ? "PRIVATE_SOURCE_KIND_CANARY" : "Window",
            "PRIVATE_WINDOW_TITLE_CANARY — 日本語", @"C:\PRIVATE_EXECUTABLE_PATH_CANARY\Game.exe");
        var profile = new CaptureEncodingProfile(poisonIdentifiers ? "PRIVATE_ENCODER_ID_CANARY" : "obs_x264",
            "PRIVATE_ENCODER_NAME_CANARY", true, 1280, 720, 30);
        const string raw = "PRIVATE_NATIVE_ERROR_CANARY https://discord.com/api/webhooks/123/PRIVATE_WEBHOOK_TOKEN_CANARY PRIVATE_DEVICE_ID_CANARY";
        var issue = new RecordingIssue("PRIVATE_ERROR_CODE_CANARY", raw, now, session, "PRIVATE_SAVE_ID_CANARY");
        return new(19, session, RecordingEngineState.Ready, ReplaySaveState.Succeeded, 0,
            source, profile, profile, CaptureHostHealth.Responsive, 7, 30, true, false, issue, issue,
            new SavedClipSummary("PRIVATE_CLIP_ID_CANARY", "PRIVATE_SAVE_ID_CANARY", session, 7, now, source, profile),
            [new RecordingHotkeyHealth("PRIVATE_HOTKEY_ACTION_CANARY", "PRIVATE_HOTKEY_CHORD_CANARY",
                "PRIVATE_HOTKEY_CHORD_CANARY", false, 5, "PRIVATE_HOTKEY_MESSAGE_CANARY")]);
    }

    private static void AssertPrivateCanariesAbsent(string text)
    {
        foreach (string canary in PrivateCanaries)
            Assert.DoesNotContain(canary, text, StringComparison.OrdinalIgnoreCase);
    }

    private static DiagnosticDocument ReadDocument(string path) =>
        JsonSerializer.Deserialize<DiagnosticDocument>(File.ReadAllText(path), JsonOptions)!;

    private static DiagnosticDocument ReadExport(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        DiagnosticDocument? document = null;
        foreach (var entry in archive.Entries)
        {
            AssertPrivateCanariesAbsent(entry.FullName);
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            string content = reader.ReadToEnd();
            AssertPrivateCanariesAbsent(content);
            if (entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Null(document);
                document = JsonSerializer.Deserialize<DiagnosticDocument>(content, JsonOptions);
            }
        }
        Assert.NotNull(document);
        return document;
    }

    private static async Task<byte[]> SeedAsync(Sandbox fixture, ManualClock clock)
    {
        await using (var journal = await DiagnosticJournal.OpenAsync(fixture.JournalDirectory, Version(), clock))
        {
            Assert.True(journal.TryRecord(Observation(0)));
            Assert.Equal(DiagnosticFlushResult.Persisted, await journal.FlushAsync());
        }
        return File.ReadAllBytes(fixture.JournalFile);
    }

    private static byte[] Mutate(byte[] valid, string fault)
    {
        if (fault == "malformed") return Encoding.UTF8.GetBytes("{ PRIVATE_FOREIGN_FIELD_CANARY ! }");
        if (fault == "empty") return [];
        if (fault == "oversized") return Enumerable.Repeat((byte)' ', DiagnosticPolicy.MaximumBytes + 1).ToArray();
        if (fault == "duplicate-key")
            return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(valid).Insert(1, "\"SchemaVersion\":1,"));
        var root = JsonNode.Parse(valid)!.AsObject();
        if (fault == "unknown-root") root["PrivatePath"] = "PRIVATE_FOREIGN_FIELD_CANARY";
        else if (fault == "future-schema") root["SchemaVersion"] = int.MaxValue;
        else if (fault == "unknown-nested") root["Events"]![0]!["Observation"]!["PrivatePath"] = "PRIVATE_FOREIGN_FIELD_CANARY";
        else if (fault == "unknown-enum") root["Events"]![0]!["Observation"]!["Kind"] = "PRIVATE_FOREIGN_FIELD_CANARY";
        else if (fault == "future-timestamp") root["Events"]![0]!["TimestampUtc"] = "2126-10-24T12:00:00+00:00";
        else if (fault == "missing-entry-version") root["Events"]![0]!.AsObject().Remove("Version");
        else throw new ArgumentOutOfRangeException(nameof(fault));
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "flick-diagnostic-tests-" + Guid.NewGuid().ToString("N"));
        public string JournalDirectory => Path.Combine(Root, "Journal");
        public string JournalFile => Path.Combine(JournalDirectory, DiagnosticJournal.FileName);
        public Sandbox() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

/// <summary>Only link-specific cases skip when Windows denies its required link privilege.</summary>
public sealed class DiagnosticSymlinkFactAttribute : FactAttribute
{
    public DiagnosticSymlinkFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "flick-diagnostic-link-capability-" + Guid.NewGuid().ToString("N"));
        string link = Path.Combine(root, "probe-link.json");
        Directory.CreateDirectory(root);
        try { File.CreateSymbolicLink(link, Path.Combine(root, "absent-target.json")); }
        catch (UnauthorizedAccessException)
        { Skip = "Windows symbolic-link privilege is unavailable; this dangling-link case was not executed."; }
        catch (IOException error) when ((error.HResult & 0xffff) == 1314)
        { Skip = "Windows symbolic-link privilege is unavailable (ERROR_PRIVILEGE_NOT_HELD); this case was not executed."; }
        finally
        {
            File.Delete(link); // Deletes an owned dangling link as well as a normal file.
            Directory.Delete(root);
        }
    }
}
