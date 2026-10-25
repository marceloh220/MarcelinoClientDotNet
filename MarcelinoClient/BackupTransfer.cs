using System.IO;

namespace MarcelinoClient;

public sealed record TransferProgress(int Completed, int Total, int Copied, int Skipped, int Failed, string Message)
{
    internal LocalizedText? LocalizedMessage { get; init; }

    internal static TransferProgress Create(int completed, int total, int copied, int skipped, int failed, LocalizedText message) =>
        new(completed, total, copied, skipped, failed, message.Format()) { LocalizedMessage = message };
}
public sealed record TransferResult(int Copied, int Skipped, int Failed);

public sealed class BackupTransfer
{
    private sealed record CopyItem(string Source, string Destination);

    public Task<TransferResult> BackupAsync(IReadOnlyList<string> sources, string destination,
        IProgress<TransferProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => RunAsync(sources, destination, restore: false, incremental: true, progress, cancellationToken), cancellationToken);

    public Task<TransferResult> RestoreAsync(string source, string destination, bool incremental,
        IProgress<TransferProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => RunAsync([source], destination, restore: true, incremental, progress, cancellationToken), cancellationToken);

    private static async Task<TransferResult> RunAsync(IReadOnlyList<string> sources, string destination,
        bool restore, bool incremental, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sources.Count == 0)
        {
            throw new ArgumentException(Localization.Get("SourceRequired"));
        }

        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var roots = sources.Select(source => Path.TrimEndingDirectorySeparator(Path.GetFullPath(source))).ToArray();
        var names = roots.Select(GetRootName).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
        {
            throw new IOException(Localization.Get("DuplicateSources"));
        }

        foreach (var root in roots)
        {
            if (IsWithin(target, root) || IsWithin(root, target))
            {
                throw new IOException(Localization.Get("SourceDestinationOverlap"));
            }

            RejectLinks(root);
            if (!restore && !Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(Localization.Get("SourceFolderMissing", root));
            }
        }

        RejectLinks(target);
        Directory.CreateDirectory(target);
        var items = new List<CopyItem>();
        var failed = 0;
            progress?.Report(TransferProgress.Create(0, 0, 0, 0, 0, Localization.Message("MappingFiles")));
        for (var index = 0; index < roots.Length; index++)
        {
            var source = roots[index];
            var destinationRoot = Path.Combine(target, names[index]);
            if (restore && File.Exists(source))
            {
                items.Add(new CopyItem(source, destinationRoot));
                continue;
            }

            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(Localization.Get("SourceFolderMissing", source));
            }

            var pending = new Stack<(string Source, string Destination)>();
            pending.Push((source, destinationRoot));
            while (pending.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    RejectLinks(directory.Source);
                    RejectLinks(directory.Destination);
                    Directory.CreateDirectory(directory.Destination);
                    foreach (var entry in new DirectoryInfo(directory.Source).EnumerateFileSystemInfos())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            failed++;
                            progress?.Report(TransferProgress.Create(0, 0, 0, 0, failed, Localization.Message("SkippedLink", entry.FullName)));
                            continue;
                        }

                        var output = Path.Combine(directory.Destination, entry.Name);
                        if (entry is DirectoryInfo)
                        {
                            pending.Push((entry.FullName, output));
                        }
                        else
                        {
                            items.Add(new CopyItem(entry.FullName, output));
                        }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    progress?.Report(TransferProgress.Create(0, 0, 0, 0, failed, Localization.Message("MappingFailed", directory.Source, error.Message)));
                }
            }
        }

        var completed = 0;
        var copied = 0;
        var skipped = 0;
        var reportLock = new object();
        progress?.Report(TransferProgress.Create(0, items.Count, 0, 0, failed, Localization.Message("FilesFound", items.Count)));
        await Parallel.ForEachAsync(items, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        }, async (item, token) =>
        {
            LocalizedText message;
            var wasSkipped = false;
            var wasCopied = false;
            try
            {
                token.ThrowIfCancellationRequested();
                RejectLinks(item.Source);
                RejectLinks(item.Destination);
                if (incremental && UpToDate(item.Source, item.Destination))
                {
                    wasSkipped = true;
                    message = Localization.Message("UnchangedFile", item.Source);
                }
                else
                {
                    await CopySafelyAsync(item.Source, item.Destination, token).ConfigureAwait(false);
                    wasCopied = true;
                    message = Localization.Message("CopiedFile", item.Source);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                message = Localization.Message("CopyFailed", item.Source, error.Message);
            }

            lock (reportLock)
            {
                completed++;
                copied += wasCopied ? 1 : 0;
                skipped += wasSkipped ? 1 : 0;
                failed += !wasCopied && !wasSkipped ? 1 : 0;
                progress?.Report(TransferProgress.Create(completed, items.Count, copied, skipped, failed, message));
            }
        }).ConfigureAwait(false);

        return new TransferResult(copied, skipped, failed);
    }

    private static bool UpToDate(string source, string destination)
    {
        var original = new FileInfo(source);
        var existing = new FileInfo(destination);
        return existing.Exists && original.Length == existing.Length &&
            existing.LastWriteTimeUtc >= original.LastWriteTimeUtc.AddSeconds(-2);
    }

    private static async Task CopySafelyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var info = new FileInfo(source);
        var modified = info.LastWriteTimeUtc;
        var accessed = info.LastAccessTimeUtc;
        var length = info.Length;
        var parent = Path.GetDirectoryName(destination)!;
        RejectLinks(parent);
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, $".marcelino-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            info.Refresh();
            if (info.Length != length || info.LastWriteTimeUtc != modified)
            {
                throw new IOException(Localization.Get("SourceChanged"));
            }

            File.SetLastWriteTimeUtc(temporary, modified);
            File.SetLastAccessTimeUtc(temporary, accessed);
            RejectLinks(destination);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static string GetRootName(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var name = Path.GetFileName(normalized);
        if (name.Length == 0 && normalized.StartsWith("\\\\", StringComparison.Ordinal))
        {
            var trimmed = normalized.TrimEnd('\\');
            name = trimmed[(trimmed.LastIndexOf('\\') + 1)..];
        }

        return name.Length > 0 ? name : Localization.Get("RootFolder");
    }

    private static bool IsWithin(string path, string parent) =>
        string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RejectLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(Localization.Get("LinksNotAllowed", current));
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }

            current = Path.GetDirectoryName(current);
        }
    }
}