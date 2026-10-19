using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Full.NET.Modules.ObservabilityAdmin.Configuration;
using Full.NET.Hosting.Observability;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;

/// <summary>
/// 在配置的固定目录内提供有界日志枚举、尾读和下载句柄解析。
/// </summary>
/// <remarks>
/// 客户端只持有由文件名计算的稳定散列标识；每次访问都会重新枚举顶层普通日志文件，
/// 因而无法通过路径、软链接或陈旧映射越过根目录边界。
/// </remarks>
public sealed partial class LogFileControlPlane
{
    private const int UnixOpenReadOnly = 0;
    private const int LinuxOpenCloseOnExec = 0x80000;
    private const int LinuxOpenNoFollow = 0x20000;
    private const int LinuxOpenNonBlocking = 0x800;
    private const int LinuxAtEmptyPath = 0x1000;
    private const uint LinuxStatxType = 0x0001;
    private const ushort LinuxFileTypeMask = 0xF000;
    private const ushort LinuxRegularFile = 0x8000;
    private const int LinuxStatxBufferSize = 256;
    private const int LinuxStatxModeOffset = 28;
    private const uint WindowsGenericRead = 0x80000000;
    private const uint WindowsShareReadWriteDelete = 0x00000007;
    private const uint WindowsOpenExisting = 3;
    private const uint WindowsOpenReparsePoint = 0x00200000;
    private const uint WindowsSequentialScan = 0x08000000;
    private const uint WindowsOverlapped = 0x40000000;
    private const int WindowsFileAttributeTagInfo = 9;
    private const int WindowsMaximumPathCharacters = 32_768;

    private static readonly Encoding TailEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private readonly ObservabilityAdminOptions _options;
    private readonly string? _logRootPath;
    private readonly Action<string>? _beforeOpenTestHook;

    public LogFileControlPlane(
        IOptions<ObservabilityAdminOptions> options,
        IHostEnvironment environment,
        LoggingOptions loggingOptions)
        : this(options, environment, loggingOptions, beforeOpenTestHook: null)
    {
    }

    internal LogFileControlPlane(
        IOptions<ObservabilityAdminOptions> options,
        IHostEnvironment environment,
        LoggingOptions loggingOptions,
        Action<string>? beforeOpenTestHook)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(loggingOptions);
        _options = options.Value;
        _beforeOpenTestHook = beforeOpenTestHook;
        _logRootPath = _options.LogRootPath is { Length: > 0 } root
            ? Path.GetFullPath(root, environment.ContentRootPath)
            : loggingOptions.DeliveryMode == LoggingDeliveryMode.Local && loggingOptions.LocalFile.Enabled
                ? LocalLogFileLayout.ResolveDirectory(loggingOptions.LocalFile.Directory,
                    environment.ContentRootPath, environment.ApplicationName)
                : null;
    }

    /// <summary>
    /// 枚举日志根目录下的普通日志文件，按最后修改时间倒序返回受 MaximumListFiles 限制的摘要列表；
    /// 每次调用都重新枚举顶层目录，不缓存文件名映射以抵御软链接逃逸。
    /// </summary>
    public LogFileList List(CancellationToken cancellationToken = default)
    {
        var scan = EnumerateCandidates(_options.MaximumScanEntries, cancellationToken);
        var items = scan.Candidates
            .OrderByDescending(candidate => candidate.LastModifiedUtc)
            .ThenBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaximumListFiles)
            .Select(candidate => new LogFileSummary(
                candidate.Id,
                candidate.FileName,
                candidate.SizeBytes,
                candidate.LastModifiedUtc))
            .ToArray();
        return new LogFileList(items, scan.IsTruncated || scan.Candidates.Count > items.Length, scan.ScannedEntries);
    }

    /// <summary>
    /// 按文件散列标识读取日志文件尾部内容；返回受行数/字节上限约束的尾部片段，
    /// 标识无效或文件非安全普通文件时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="id">由文件名 SHA-256 计算的 64 位十六进制标识。</param>
    /// <param name="maximumLines">返回的最大行数；为空时使用 DefaultTailLines。</param>
    /// <param name="maximumBytes">读取的最大字节数；为空时使用 DefaultTailBytes。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<LogFileTail?> ReadTailAsync(
        string id,
        int? maximumLines,
        int? maximumBytes,
        CancellationToken cancellationToken)
    {
        var opened = ResolveAndOpen(id, cancellationToken);
        if (opened is null)
        {
            return null;
        }

        return await ReadOpenedTailAsync(opened, maximumLines, maximumBytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>时间线专用有界扫描：至多检查 1000 个目录项、打开十个文件，复用安全句柄而不重复枚举。</summary>
    internal async Task<IReadOnlyList<LogFileTail>> ReadTimelineTailsAsync(CancellationToken cancellationToken)
    {
        var result = new List<LogFileTail>();
        foreach (var candidate in EnumerateCandidates(Math.Min(1000, _options.MaximumScanEntries), cancellationToken).Candidates
            .OrderByDescending(file => file.LastModifiedUtc).ThenBy(file => file.FileName, StringComparer.Ordinal).Take(10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var opened = TryOpen(candidate);
            if (opened is not null)
                result.Add(await ReadOpenedTailAsync(opened, 5000, 256 * 1024, cancellationToken).ConfigureAwait(false));
        }
        return result;
    }

    private async Task<LogFileTail> ReadOpenedTailAsync(OpenedLogFile opened, int? maximumLines,
        int? maximumBytes, CancellationToken cancellationToken)
    {
        var lineLimit = Math.Clamp(
            maximumLines ?? _options.DefaultTailLines,
            1,
            _options.MaximumTailLines);
        var byteLimit = Math.Clamp(
            maximumBytes ?? _options.DefaultTailBytes,
            1,
            _options.MaximumTailBytes);

        await using var stream = opened.Stream;
        var snapshotLength = opened.SizeBytes;
        var bytesToRead = (int)Math.Min(snapshotLength, byteLimit);
        var buffer = new byte[bytesToRead];
        stream.Seek(snapshotLength - bytesToRead, SeekOrigin.Begin);
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        var start = FindUtf8CharacterBoundary(buffer, totalRead);
        var content = TailEncoding.GetString(buffer, start, totalRead - start)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n');
        var lines = content.Split('\n');
        if (lines.Length > lineLimit)
        {
            content = string.Join('\n', lines[^lineLimit..]);
        }

        return new LogFileTail(
            opened.Id,
            opened.FileName,
            content,
            totalRead,
            snapshotLength > totalRead || lines.Length > lineLimit);
    }

    /// <summary>
    /// 按文件散列标识打开日志文件的只读下载流，返回包含文件名、大小与最后修改时间的下载句柄；
    /// 调用方负责释放返回的流；标识无效或文件非安全普通文件时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="id">由文件名 SHA-256 计算的 64 位十六进制标识。</param>
    public LogFileDownload? OpenDownload(string id, CancellationToken cancellationToken = default)
    {
        var opened = ResolveAndOpen(id, cancellationToken);
        if (opened is null)
        {
            return null;
        }

        return new LogFileDownload(
            opened.Stream,
            opened.FileName,
            opened.SizeBytes,
            opened.LastModifiedUtc);
    }

    private OpenedLogFile? ResolveAndOpen(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length != 64)
        {
            return null;
        }

        var candidate = EnumerateCandidates(_options.MaximumScanEntries, cancellationToken).Candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return candidate is null ? null : TryOpen(candidate);
    }

    private sealed record FileScan(IReadOnlyList<LogFileCandidate> Candidates, int ScannedEntries, bool IsTruncated);

    private FileScan EnumerateCandidates(int maximumEntries, CancellationToken cancellationToken)
    {
        var candidates = new List<LogFileCandidate>();
        cancellationToken.ThrowIfCancellationRequested();
        if (_logRootPath is null || !IsAllowedRootDirectory()) return new(candidates, 0, false);
        var scanned = 0;
        var incomplete = false;
        try
        {
            // 枚举全部顶层目录项再计数，避免大量非日志文件绕过扫描预算。
            foreach (var path in Directory.EnumerateFileSystemEntries(_logRootPath).Take(maximumEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                var extension = Path.GetExtension(path);
                if (!extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                    && !LocalLogFileLayout.IsOwnedFileName(Path.GetFileName(path))) continue;
                try
                {
                    var file = new FileInfo(path);
                    if ((file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                        || !string.Equals(file.DirectoryName, _logRootPath, StringComparison.OrdinalIgnoreCase)) continue;
                    candidates.Add(new LogFileCandidate(CreateId(file.Name), file.Name, file.FullName,
                        file.Length, file.LastWriteTimeUtc));
                }
                catch (IOException) { /* 轮转期间消失的文件跳过。 */ }
                catch (UnauthorizedAccessException) { /* 不扩大文件访问权限。 */ }
            }
        }
        catch (IOException) { incomplete = true; }
        catch (UnauthorizedAccessException) { incomplete = true; }
        // 恰好达到预算也报告不完整，不额外探测下一项以突破扫描上限。
        return new(candidates, scanned, incomplete || scanned >= maximumEntries);
    }

    private OpenedLogFile? TryOpen(LogFileCandidate candidate)
    {
        FileStream? stream = null;
        try
        {
            // 枚举与打开之间日志可能轮转；打开前后都重新确认普通文件边界，失败稳定映射为未找到。
            if (!IsAllowedRegularFile(candidate.FullPath))
            {
                return null;
            }

            _beforeOpenTestHook?.Invoke(candidate.FullPath);
            stream = OpenReadWithoutFollowingLinks(candidate.FullPath, _logRootPath!);
            if (stream is null)
            {
                return null;
            }

            var opened = new OpenedLogFile(
                candidate.Id,
                candidate.FileName,
                stream,
                stream.Length,
                File.GetLastWriteTimeUtc(candidate.FullPath));
            stream = null;
            return opened;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private bool IsAllowedRegularFile(string path)
    {
        var file = new FileInfo(path);
        return file.Exists
            && (file.Attributes & FileAttributes.ReparsePoint) == 0
            && File.ResolveLinkTarget(path, returnFinalTarget: false) is null
            && string.Equals(file.DirectoryName, _logRootPath, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAllowedRootDirectory()
    {
        try
        {
            if (_logRootPath is null) return false;
            var directory = new DirectoryInfo(_logRootPath);
            for (var ancestor = directory.Parent; ancestor is not null; ancestor = ancestor.Parent)
                if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            return directory.Exists
                && (directory.Attributes & FileAttributes.ReparsePoint) == 0
                && Directory.ResolveLinkTarget(_logRootPath, returnFinalTarget: false) is null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 在不跟随链接的前提下打开受信日志文件，并再次核验最终路径仍位于允许目录。
    /// </summary>
    /// <param name="path">待打开的日志文件绝对路径。</param>
    /// <param name="allowedRootPath">允许访问的规范化日志根目录。</param>
    /// <returns>验证通过的只读文件流；文件不安全或平台不受支持时返回 <see langword="null"/>。</returns>
    private static FileStream? OpenReadWithoutFollowingLinks(string path, string allowedRootPath)
    {
        SafeFileHandle handle;
        if (OperatingSystem.IsWindows())
        {
            handle = CreateFileWindows(
                path,
                WindowsGenericRead,
                WindowsShareReadWriteDelete,
                0,
                WindowsOpenExisting,
                WindowsOpenReparsePoint | WindowsSequentialScan | WindowsOverlapped,
                0);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }

            if (!GetFileInformationByHandleExWindows(
                    handle,
                    WindowsFileAttributeTagInfo,
                    out var tagInfo,
                    (uint)Marshal.SizeOf<WindowsFileAttributeTagInformation>())
                || ((FileAttributes)tagInfo.FileAttributes & FileAttributes.ReparsePoint) != 0)
            {
                handle.Dispose();
                return null;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var flags = UnixOpenReadOnly
                | LinuxOpenCloseOnExec
                | LinuxOpenNoFollow
                | LinuxOpenNonBlocking;
            var descriptor = OpenUnix(path, flags);
            if (descriptor < 0)
            {
                return null;
            }

            handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
            if (!IsLinuxRegularFile(descriptor))
            {
                handle.Dispose();
                return null;
            }
        }
        else
        {
            // 官方运行边界仅承诺 Windows 与 Linux；其他 Unix 平台在未提供句柄级普通文件证明时失败关闭。
            return null;
        }

        try
        {
            var finalPath = GetFinalPath(handle);
            if (finalPath is null
                || !string.Equals(
                    Path.GetDirectoryName(finalPath),
                    allowedRootPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                handle.Dispose();
                return null;
            }

            // Windows 句柄显式带 FILE_FLAG_OVERLAPPED；Linux open() 返回同步句柄，必须让 FileStream
            // 使用同步句柄模式，再由 ReadAsync 安全回退，否则 .NET 会在构造阶段拒绝该句柄。
            return new FileStream(
                handle,
                FileAccess.Read,
                64 * 1024,
                isAsync: OperatingSystem.IsWindows());
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static string? GetFinalPath(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetFinalWindowsPath(handle);
        }

        var descriptor = handle.DangerousGetHandle().ToInt64();
        var descriptorLink = $"/proc/self/fd/{descriptor}";
        var target = File.ResolveLinkTarget(descriptorLink, returnFinalTarget: true);
        return target is null ? null : Path.GetFullPath(target.FullName);
    }

    private static unsafe bool IsLinuxRegularFile(int descriptor)
    {
        // statx 是稳定的 Linux UAPI；stx_mode 固定位于 28 字节偏移，缓冲区大小固定为 256 字节。
        var buffer = stackalloc byte[LinuxStatxBufferSize];
        if (StatxUnix(
                descriptor,
                string.Empty,
                LinuxAtEmptyPath,
                LinuxStatxType,
                buffer) != 0)
        {
            return false;
        }

        var mode = *(ushort*)(buffer + LinuxStatxModeOffset);
        return (mode & LinuxFileTypeMask) == LinuxRegularFile;
    }

    private static unsafe string? GetFinalWindowsPath(SafeFileHandle handle)
    {
        var buffer = stackalloc char[WindowsMaximumPathCharacters];
        var length = GetFinalPathNameByHandleWindows(
            handle,
            buffer,
            WindowsMaximumPathCharacters,
            0);
        if (length == 0 || length >= WindowsMaximumPathCharacters)
        {
            return null;
        }

        var path = new string(buffer, 0, (int)length);
        if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
        {
            path = $"\\\\{path[8..]}";
        }
        else if (path.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            path = path[4..];
        }

        return Path.GetFullPath(path);
    }

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileWindows(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleExWindows(
        SafeFileHandle file,
        int fileInformationClass,
        out WindowsFileAttributeTagInformation fileInformation,
        uint bufferSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandleWindows(
        SafeFileHandle file,
        char* path,
        uint pathLength,
        uint flags);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenUnix(string path, int flags);

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int StatxUnix(
        int directoryFileDescriptor,
        string path,
        int flags,
        uint mask,
        void* buffer);

    private static string CreateId(string fileName) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fileName)));

    private static int FindUtf8CharacterBoundary(byte[] buffer, int length)
    {
        var index = 0;
        while (index < length && (buffer[index] & 0b1100_0000) == 0b1000_0000)
        {
            index++;
        }

        return index;
    }

    private sealed record LogFileCandidate(
        string Id,
        string FileName,
        string FullPath,
        long SizeBytes,
        DateTimeOffset LastModifiedUtc);

    private sealed record OpenedLogFile(
        string Id,
        string FileName,
        FileStream Stream,
        long SizeBytes,
        DateTimeOffset LastModifiedUtc);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileAttributeTagInformation
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }
}
