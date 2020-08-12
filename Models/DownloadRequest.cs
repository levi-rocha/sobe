namespace SOBE.Models
{
    public class DownloadRequest
    {
        public string FileUrl { get; set; }
        public string OutputName { get; set; }
        public string Owner { get; set; }
        public string Password { get; set; }
    }
}