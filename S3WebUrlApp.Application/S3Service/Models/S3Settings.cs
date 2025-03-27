namespace S3WebUrlApp.Application.S3Service.Models;

public class S3Settings
{
    public string AccessKey { get; set; }
    public string SecretKey { get; set; }
    public string EndpointUrl { get; set; }
    public string RegionName { get; set; }
    public string BucketName { get; set; }
}