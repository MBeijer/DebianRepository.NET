using DebianRepository.Models;

namespace DebianRepository.Systems;

public interface IFileSystem
{
	public delegate void  AvailabilityCallback(string fileName, string path);

	public delegate Task<BaseFile> OnAddFileCallback(string filePath);

	Task                          SetRootDirectory(string rootDirectory);
	void                          SetOnAddFileCallback(OnAddFileCallback onAddFileCallbackCallback);
	bool                          FileExists(string fileName);
	string?                       GetFilePath(string fileName, AvailabilityCallback? callback = null);
	bool                          HasCallback(string fileName);
	void                          SetFilter(string filter);
	Task<byte[]>                  GetFileData(string fullPath);
	IReadOnlyCollection<BaseFile> ListFiles();
	void                          MoveFile(string oldPath, string newPath);
	Task                          SaveFile(string filePath, byte[] data);
}