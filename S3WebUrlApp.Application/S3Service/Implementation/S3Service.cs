using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using S3WebUrlApp.Application.S3Service.Models;

namespace S3WebUrlApp.Application.S3Service.Implementation;

public class S3Service 
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3Settings _settings;
    
    public S3Service(S3Settings settings)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{settings.EndpointUrl}",
            ForcePathStyle = true
        };

        _s3Client = new AmazonS3Client(settings.AccessKey, settings.SecretKey, config);
        _settings = settings;
    }
    
    /// <summary>
    /// Получает список всех папок в указанном бакете
    /// </summary>
    public async Task<List<string>> GetAllFoldersAsync()
    {
        var folders = new List<string>();
        var request = new ListObjectsV2Request
        {
            BucketName = _settings.BucketName,
            Delimiter = "/"
        };

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client.ListObjectsV2Async(request);
            
            if (response.CommonPrefixes != null)
            {
                folders.AddRange(response.CommonPrefixes);
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated.Value);

        return folders;
    }
    
    /// <summary>
    /// Получает список файлов в указанной папке
    /// </summary>
    /// <param name="folderPath">Путь к папке (должен заканчиваться на "/")</param>
    /// <returns>Список имен файлов (без пути к папке)</returns>
    public async Task<List<string>> GetFilesInFolderAsync(string folderPath)
    {
        var files = new List<string>();
        var request = new ListObjectsV2Request
        {
            BucketName = _settings.BucketName,
            Prefix = folderPath,
            Delimiter = "/" // Чтобы не получать файлы из подпапок
        };

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client.ListObjectsV2Async(request);
            
            if (response.S3Objects != null)
            {
                foreach (var s3Object in response.S3Objects)
                {
                    // Убираем путь папки из имени файла
                    if (!s3Object.Key.EndsWith("/")) // Исключаем сами папки
                    {
                        string fileName = s3Object.Key.Substring(folderPath.Length);
                        files.Add(fileName);
                    }
                }
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated.Value);

        return files;
    }
    
    /// <summary>
    /// Получает публичные ссылки на все файлы с указанным именем (без расширения) в заданной папке
    /// </summary>
    /// <param name="folderPath">Путь к папке (должен заканчиваться на "/")</param>
    /// <param name="fileNameWithoutExtension">Имя файла без расширения</param>
    /// <returns>Список публичных URL для найденных файлов</returns>
    public async Task<List<string>> GetFileUrlsByNameAsync(
        string folderPath, 
        string fileNameWithoutExtension)
    {
        var urls = new List<string>();
        
        // Получаем все файлы в папке
        List<string> allFiles = await GetFilesInFolderAsync(folderPath);
        
        // Ищем файлы, где имя без расширения совпадает с искомым
        foreach (var file in allFiles)
        {
            string currentFileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(currentFileNameWithoutExt, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
            {
                string fullPath = $"{folderPath}{file}";
                string url = $"{_s3Client.Config.ServiceURL}{_settings.BucketName}/{fullPath}";
                urls.Add(url);
            }
        }
        return urls;
    }
    
    /// <summary>
    /// Получает публичные ссылки на все файлы с указанным базовым именем (без суффикса _число и расширения)
    /// </summary>
    /// <param name="folderPath">Путь к папке</param>
    /// <param name="baseFileName">Базовое имя файла (без _число и расширения)</param>
    /// <returns>Список публичных URL для найденных файлов, отсортированный по номеру суффикса</returns>
    public async Task<List<string>> GetFileUrlsByBaseNameAsync(
        string folderPath, 
        string baseFileName)
    {
        var urls = new List<string>();
        
        // Получаем все файлы в папке
        List<string> allFiles = await GetFilesInFolderAsync(folderPath);
        
        // Группируем файлы по базовому имени
        var groupedFiles = new Dictionary<int, string>();
        
        foreach (var file in allFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            var parts = fileName.Split('_');
            
            if (parts.Length > 0 && string.Equals(parts[0], baseFileName, StringComparison.OrdinalIgnoreCase))
            {
                // Для файлов без суффикса
                if (parts.Length == 1)
                {
                    groupedFiles[0] = file;
                }
                // Для файлов с суффиксом _число
                else if (parts.Length == 2 && int.TryParse(parts[1], out int number))
                {
                    groupedFiles[number] = file;
                }
            }
        }
        
        // Сортируем по номеру и формируем URLs
        foreach (var entry in groupedFiles.OrderBy(x => x.Key))
        {
            string fullPath = $"{folderPath}{entry.Value}";
            string url = $"{_s3Client.Config.ServiceURL}{_settings.BucketName}/{fullPath}";
            urls.Add(url);
        }
        
        return urls;
    }
}