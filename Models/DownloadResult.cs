namespace SOBE.Models
{
    public class DownloadResult
    {
        public string Sha1 { get; set; }
        public string FileName { get; set; }

        public DownloadResult(string sha1, string fileName)
        {
            Sha1 = sha1;
            FileName = fileName;
        }
    }
}