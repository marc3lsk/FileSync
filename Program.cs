using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace FileSync
{
    public static class ConfigKeys
    {
        public const string BufferSize = "BufferSize";
        public const string FileCopyRetryCount = "FileCopyRetryCount";
        public const string MailTimeoutMilliseconds = "MailTimeoutMilliseconds";
        public const string MailSubject = "MailSubject";
        public const string MailRecipients = "MailRecipients";
        public const string HtmlReportOutputFile = "HtmlReportOutputFile";

        public static string GetString(string key)
        {
            return (string)System.Configuration.ConfigurationSettings.AppSettings[key];
        }

        public static int? GetInt(string key, int? defaultValue = null)
        {
            var value = default(int?);
            var configValue = 0;
            if (int.TryParse((string)System.Configuration.ConfigurationSettings.AppSettings[key], out configValue))
                value = configValue;
            else if (defaultValue.HasValue)
                value = defaultValue.Value;
            return value;
        }
    }

    public delegate void ErrorCallback(Exception ex);
    public delegate void FilePathCallback(string path);

    public class SyncReport
    {
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public DateTime Started { get; set; }
        public DateTime Finished { get; set; }
        public int BytesTransfered { get; set; }
        public int FilesUpToDate { get; set; }
        public List<Tuple<string, string>> FileErrors { get; set; }
        public List<string> Errors { get; set; }

        public TimeSpan Duration
        {
            get
            {
                return (Finished - Started).Duration();
            }
        }

        public double AvgSpeed
        {
            get
            {
                var duration = Duration;
                return BytesTransfered / (duration.TotalSeconds > 0 ? duration.TotalSeconds : 1);
            }
        }

        public SyncReport()
        {
            Errors = new List<string>();
            FileErrors = new List<Tuple<string, string>>();
            BytesTransfered = 0;
            FilesUpToDate = 0;
        }

        public static string GetHtmlReport(List<SyncReport> reports)
        {
            var sb = new StringBuilder();
            sb.Append("<html><head><title></title></head><body>");
            foreach(var report in reports)
            {
                sb.Append("<table>");
                sb.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", "Source", report.SourcePath));
                sb.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", "Destination", report.DestinationPath));
                sb.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", "Duration", report.Duration));
                sb.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", "Bytes transfered", FileSizeHumanFormat(report.BytesTransfered)));
                sb.Append(string.Format("<tr><td>{0}</td><td>{1}/s</td></tr>", "Avg. speed", FileSizeHumanFormat((long)report.AvgSpeed)));
                sb.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", "Up-to-date files", report.FilesUpToDate));
                sb.Append(string.Format("<tr><td>{0}</td><td></td></tr>", "Files with error:"));
                foreach(var fileError in report.FileErrors)
                {
                    sb.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", fileError.Item1, fileError.Item2));
                }
                sb.Append(string.Format("<tr><td>{0}</td><td></td></tr>", "Other errors:"));
                foreach (var error in report.Errors)
                {
                    sb.Append(string.Format("<tr><td colspan='2'>{0}</td></tr>", error));
                }
                sb.Append("</table><br/><br/>");
            }
            sb.Append("</body></html>");
            return sb.ToString();
        }

        public static string FileSizeHumanFormat(long fileSize)
        {
            if (fileSize < 1)
                return "0 B";
            var i = (int)Math.Floor(Math.Log(fileSize) / Math.Log(1024));
            return (fileSize / Math.Pow(1024, i)).ToString("N2", CultureInfo.GetCultureInfo("sk-SK")) + " " + (new string[] { "B", "kB", "MB", "GB", "TB" })[i];
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var bufferSize = ConfigKeys.GetInt(ConfigKeys.BufferSize, 1024).Value;
            
            if(args != null)
            {
                if(args.Length < 2)
                {
                    throw new Exception("Please provide at least one source and destination");
                }
                if(args.Length % 2 > 0)
                {
                    throw new Exception("Please provide EVEN number of arguments (source and destination pairs)");
                }

                int argIndex = 0;
                var reports = new List<SyncReport>();
                while(argIndex < args.Length)
                {
                    var sourcePath = args[argIndex++];
                    var destinationPath = args[argIndex++];
                    reports.AddRange(Sync(sourcePath, destinationPath, bufferSize, ConfigKeys.GetInt(ConfigKeys.FileCopyRetryCount, 5).Value));
                }
                SendSyncReport(reports);
                var htmlReportOutputFile = ConfigKeys.GetString(ConfigKeys.HtmlReportOutputFile);
                if (!string.IsNullOrEmpty(htmlReportOutputFile))
                {
                    File.WriteAllText(htmlReportOutputFile, SyncReport.GetHtmlReport(reports));
                }
            }

            Console.WriteLine("*** FINISHED, press any key ... ***");
            Console.ReadKey();
        }

        static string[] GetPathParts(string path)
        {
            var pathParts = path.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return pathParts;
        }

        static string[] SubtractPath(string basePath, string fullPath)
        {
            if(!fullPath.ToUpperInvariant().Contains(basePath.ToUpperInvariant()))
                return new string[]{};
            
            var basePathParts = GetPathParts(basePath);
            var fullPathParts = GetPathParts(fullPath);

            if(basePath.Length == 0)
                return fullPathParts;

            return fullPathParts.Skip(basePathParts.Length).ToArray();
        }

        static void SendSyncReport(List<SyncReport> reports)
        {
            var recipients = ConfigKeys.GetString(ConfigKeys.MailRecipients);
            if (!string.IsNullOrEmpty(recipients))
            {
                MailMessage message = new MailMessage();
                foreach(var recipient in recipients.Split(new char[] {';'}, StringSplitOptions.RemoveEmptyEntries))
                {
                    message.To.Add(new MailAddress(recipient));
                }
                message.Subject = ConfigKeys.GetString(ConfigKeys.MailSubject);
                message.Body = SyncReport.GetHtmlReport(reports);
                message.IsBodyHtml = true;

                SmtpClient client = new SmtpClient();
                var timeout = ConfigKeys.GetInt(ConfigKeys.MailTimeoutMilliseconds, 5000).Value;
                client.Timeout = timeout;
                Console.WriteLine(string.Format("Sending mail report, smtp: {0}, port: {1}, username: {2}, password: {3}", client.Host, client.Port, client.Credentials.GetCredential(client.Host, client.Port, "SSL").UserName, client.Credentials.GetCredential(client.Host, client.Port, "SSL").Password));

                try
                {
                    client.Send(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        static List<SyncReport> Sync(string src, string dst, int bufferSize, int retryCount = 5)
        {
            var reports = new Dictionary<Tuple<string, string>, SyncReport>();

            var reportKey = new Tuple<string, string>(src, dst);
            if (!reports.ContainsKey(reportKey))
            {
                reports.Add(reportKey, new SyncReport
                {
                    SourcePath = reportKey.Item1,
                    DestinationPath = reportKey.Item2,
                    Started = DateTime.Now,
                });
            }

            var srcRoot = File.Exists(src) ? Path.GetDirectoryName(src) : src;

            WalkFiles(src,
                (srcFilePath) =>
                {
                    Console.WriteLine(srcFilePath);
                    var srcFileRelativePath = SubtractPath(srcRoot, srcFilePath);
                    var dstFilePath = Path.Combine(dst, string.Join(Path.DirectorySeparatorChar.ToString(), srcFileRelativePath));

                    var srcDir = Path.GetDirectoryName(srcFilePath);
                    var dstDir = Path.GetDirectoryName(dstFilePath);
                    
                    reportKey = new Tuple<string, string>(srcDir, dstDir);
                    if(!reports.ContainsKey(reportKey))
                    {
                        reports.Add(reportKey, new SyncReport
                        {
                            SourcePath = reportKey.Item1,
                            DestinationPath = reportKey.Item2,
                            Started = DateTime.Now,
                        });
                    }
                    var report = reports[reportKey];

                    var retryCounter = 0;
                    var bytesTransfered = -1;
                    while (bytesTransfered < 0 && retryCounter < retryCount)
                    {
                        try
                        {
                            bytesTransfered = CopyFileIfLastWriteTimeIsDifferent(srcFilePath, dstFilePath, bufferSize);
                            report.BytesTransfered += bytesTransfered;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                            retryCounter++;
                            report.FileErrors.Add(new Tuple<string,string>(srcFilePath, ex.Message)); 
                        }
                    }
                    if (bytesTransfered == 0)
                        report.FilesUpToDate++;

                    if (bytesTransfered < 0)
                        report.FileErrors.Add(new Tuple<string, string>(srcFilePath, "Retry count exceeded"));
                    report.Finished = DateTime.Now;
                },
                (ex) =>
                {
                    Console.WriteLine(ex.Message);
                    var report = reports[reportKey];
                    report.Errors.Add(ex.Message);
                    report.Finished = DateTime.Now;
                });

            return reports.Select(x => x.Value).ToList();
        }

        static void WalkFiles(string path, FilePathCallback callback, ErrorCallback err)
        {
            if(File.Exists(path))
            {
                callback(path);
                return;
            }
            if (Directory.Exists(path))
            {
                try
                {
                    foreach (string filePath in Directory.GetFiles(path))
                    {
                        callback(filePath);
                    }
                    foreach (string subDirPath in Directory.GetDirectories(path))
                    {
                        WalkFiles(subDirPath, callback, err);
                    }
                }
                catch (Exception ex)
                {
                    err(ex);
                }
            }
        }

        static int CopyFileIfLastWriteTimeIsDifferent(string src, string dst, int bufferSize)
        {
            var srcLastModified = File.GetLastWriteTime(src);

            if (File.Exists(dst))
            {
                var dstLastModified = File.GetLastWriteTime(dst);
                if (srcLastModified == dstLastModified)
                {
                    return 0;
                }
            }

            using (var srcStream = new FileStream(src, FileMode.Open))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));

                var bytesTransfered = 0;
                using (var dstStream = new FileStream(dst, FileMode.Create))
                {
                    var bytesRead = 1;
                    byte[] b = new byte[bufferSize];
                    while (bytesRead > 0)
                    {
                        bytesRead = srcStream.Read(b, 0, b.Length);
                        bytesTransfered += bytesRead;
                        dstStream.Write(b, 0, bytesRead);
                    }
                }
                File.SetLastWriteTime(dst, srcLastModified);
                return bytesTransfered;
            }
            return -1;
        }
    }
}
