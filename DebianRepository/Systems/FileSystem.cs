using System.Collections.Concurrent;
using DebianRepository.Models;

namespace DebianRepository.Systems;

public class FileSystem(ILogger<FileSystem> logger) : IFileSystem
{
	private readonly IDictionary<string, List<IFileSystem.AvailabilityCallback>> _fileAvailabilityCallbacks =
		new ConcurrentDictionary<string, List<IFileSystem.AvailabilityCallback>>();
	private readonly IDictionary<string, BaseFile>      _files     = new ConcurrentDictionary<string, BaseFile>();
	private          string                         _basePath  = "";
	private          string                         _filter    = "*";
	private          IFileSystem.OnAddFileCallback? _onAddFile = null;
	private          FileSystemWatcher?             _watcher;


	public async Task SetRootDirectory(string rootDirectory)
	{
		_basePath = rootDirectory;

		if (!Directory.Exists(_basePath))
			Directory.CreateDirectory(_basePath);
		logger.LogInformation("RootDirectory set: {Directory}", _basePath);

		await Init().ConfigureAwait(false);
	}

	public void SetOnAddFileCallback(IFileSystem.OnAddFileCallback onAddFileCallback) => _onAddFile = onAddFileCallback;

	public bool FileExists(string fileName) => _files.ContainsKey(Path.GetFileName(fileName).ToLower());

	public string? GetFilePath(string fileName, IFileSystem.AvailabilityCallback? callback = null)
	{
		var fileNameLower = Path.GetFileName(fileName).ToLower();
		if (_files.TryGetValue(fileNameLower, out var value)) return value.Path;
		if (callback == null) return null;

		if (_fileAvailabilityCallbacks.TryGetValue(fileNameLower, out var availabilityCallback))
			availabilityCallback.Add(callback);
		else
			_fileAvailabilityCallbacks[fileNameLower] = [callback];

		return null;
	}

	public bool HasCallback(string fileName) =>
		_fileAvailabilityCallbacks.ContainsKey(Path.GetFileName(fileName).ToLower());

	public void SetFilter(string filter) => _filter = filter;

	public Task<byte[]> GetFileData(string fullPath) => System.IO.File.ReadAllBytesAsync(fullPath);
	public IReadOnlyCollection<BaseFile> ListFiles() => (IReadOnlyCollection<BaseFile>)_files.Values;
	public void MoveFile(string oldPath, string newPath)
	{
		if (System.IO.File.Exists(newPath))
			System.IO.File.Delete(newPath);
		System.IO.File.Move(oldPath, newPath);
	}

	public Task SaveFile(string filePath, byte[] data) => System.IO.File.WriteAllBytesAsync(filePath, data);

	~FileSystem() => _watcher?.Dispose();

	private void OnChanged(object sender, FileSystemEventArgs e)
	{
		if (e.ChangeType != WatcherChangeTypes.Changed) return;
		logger.LogDebug("Changed: {EFullPath}", e.FullPath.Replace(_basePath, ""));
	}

	private void OnCreated(object sender, FileSystemEventArgs e)
	{
		logger.LogDebug("Created: {EFullPath}", e.FullPath.Replace(_basePath, ""));
		if (e.Name != null) AddFile(e.FullPath).ConfigureAwait(false).GetAwaiter().GetResult();
	}

	private void OnDeleted(object sender, FileSystemEventArgs e)
	{
		logger.LogDebug("Deleted: {EFullPath}", e.FullPath.Replace(_basePath, ""));
		if (e.Name != null && _files.ContainsKey(e.Name.ToLower())) _files.Remove(e.Name.ToLower());
	}

	private void OnRenamed(object sender, RenamedEventArgs e)
	{
		logger.LogDebug("Renamed:");
		logger.LogDebug("    Old: {EOldFullPath}", e.OldFullPath.Replace(_basePath, ""));
		logger.LogDebug("    New: {EFullPath}", e.FullPath.Replace(_basePath, ""));
		if (e is not { Name: not null, OldName: not null }) return;

		_files.Remove(e.OldName.ToLower());
		AddFile(e.FullPath).ConfigureAwait(false).GetAwaiter().GetResult();
	}

	private void OnError(object sender, ErrorEventArgs e) => PrintException(e.GetException());

	private void PrintException(Exception? ex)
	{
		while (true)
		{
			if (ex != null)
			{
				logger.LogError(ex, "FileSystem error");
				logger.LogDebug("Message: {ExMessage}", ex.Message);
				logger.LogDebug("Stacktrace:");
				if (ex.StackTrace != null) logger.LogDebug("{ExStackTrace}", ex.StackTrace);
				logger.LogDebug("");
				ex = ex.InnerException;
				continue;
			}

			break;
		}
	}

	private static FileStream WaitForFile(string fullPath)
	{
		for (var numTries = 0; numTries < 10; numTries++)
		{
			FileStream fs = null!;
			try
			{
				fs = System.IO.File.OpenRead(fullPath);
				return fs;
			}
			catch (IOException) {
				fs.Dispose ();
				Thread.Sleep (50);
			}
		}

		return null!;
	}
	private async Task AddFile(string fullPath)
	{
#pragma warning disable CAC001
		await using var fs = WaitForFile(fullPath);
#pragma warning restore CAC001
		//await crc32.AppendAsync(fs).ConfigureAwait(false);

		//var checkSum      = crc32.GetCurrentHash();
		var hash          = ""; //Convert.ToHexStringLower(checkSum);
		var fileNameLower = Path.GetFileName(fullPath).ToLower();

		if (_onAddFile != null)
			_files[fileNameLower] = await _onAddFile(fullPath).ConfigureAwait(false);
		else
			_files[fileNameLower] = new DefaultFile(fullPath);

		_files[fileNameLower].Path = fullPath.Replace(_basePath, "");
		
		logger.LogInformation("File Added: {EFullPath}", fullPath);


		if (_fileAvailabilityCallbacks.TryGetValue(fileNameLower, out var callbacks))
		{
			foreach (var callback in callbacks) callback(fileNameLower, _files[fileNameLower].Path);

			_fileAvailabilityCallbacks.Remove(fileNameLower);
		}
	}

	private async Task Init()
	{
		_files.Clear();

		foreach (var fullPath in Directory.GetFiles(_basePath, _filter, SearchOption.AllDirectories))
		{
			await AddFile(fullPath).ConfigureAwait(false);
		}

		_watcher?.Dispose();
		_watcher = new(_basePath);
		logger.LogDebug("FileSystem Watcher Init");

		_watcher.NotifyFilter = NotifyFilters.Attributes |
		                        NotifyFilters.CreationTime |
		                        NotifyFilters.DirectoryName |
		                        NotifyFilters.FileName |
		                        NotifyFilters.LastWrite |
		                        NotifyFilters.Security |
		                        NotifyFilters.Size;

		_watcher.Changed += OnChanged;
		_watcher.Created += OnCreated;
		_watcher.Deleted += OnDeleted;
		_watcher.Renamed += OnRenamed;
		_watcher.Error   += OnError;

		_watcher.Filter = _filter;
		_watcher.IncludeSubdirectories = true;
		_watcher.EnableRaisingEvents   = true;
	}
}