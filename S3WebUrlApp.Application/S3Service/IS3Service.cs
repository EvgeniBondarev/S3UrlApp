namespace S3WebUrlApp.Application.S3Service;

public interface IS3Service
{
    Task SetPublicAccessAsync(string objectKey);
    Task<List<string>> GetAllFoldersAsync();
    Task<List<string>> GetFilesInFolderAsync(string folderPath);
    Task<List<string>> GetFileUrlsByNameAsync(string folderPath, string fileNameWithoutExtension);
    Task<List<string>> GetFileUrlsByBaseNameAsync(string folderPath, string baseFileName);
    string GetPublicUrl(string objectKey);
}