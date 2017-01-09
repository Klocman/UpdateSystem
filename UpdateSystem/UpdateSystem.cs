using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using Ionic.Zip;
using Klocman.Extensions;
using Klocman.Forms;
using Klocman.Tools;
using Klocman.UpdateSystem.Properties;

namespace Klocman.UpdateSystem
{
    public static class UpdateSystem
    {
        public enum UpdateStatus
        {
            CheckFailed,
            UpToDate,
            NewAvailable
        }

        private static readonly string HelperFilename = "UpdateHelper.exe";
        private static string _assemblyLocation;
        public static Version CurrentVersion { get; set; }
        public static string ExtractedUpdatePath => Path.Combine(AssemblyLocation, "Update");
        public static Exception LastError { get; private set; }

        /// <summary>
        ///     Contains latest update reply received by CheckForUpdates.
        ///     If no replies were received yet or latest reply was invalid this property returns null.
        /// </summary>
        public static UpdateReply LatestReply { get; private set; }

        public static Uri UpdateFeedUri { get; set; }

        private static string AssemblyLocation
        {
            get
            {
                if (_assemblyLocation == null)
                {
                    _assemblyLocation = Assembly.GetExecutingAssembly().Location;
                    if (_assemblyLocation.ContainsAny(new[] {".dll", ".exe"}, StringComparison.OrdinalIgnoreCase))
                        _assemblyLocation = PathTools.GetDirectory(_assemblyLocation);
                }
                return _assemblyLocation;
            }
        }

        private static string UpdateHelperPath => Path.Combine(AssemblyLocation, HelperFilename);
        private static string DownloadFilename => Path.Combine(AssemblyLocation, "Update.zip");

        public static void BeginUpdate()
        {
            var dir = new DirectoryInfo(ExtractedUpdatePath);
            var error = LoadingDialog.ShowDialog(Localisation.UpdateSystem_DownloadingTitle, iface =>
            {
                try
                {
                    using (var wc = new WebClient())
                    {
                        iface.SetMaximum(100);
                        wc.DownloadFileAsync(LatestReply.GetDonwnloadLink(), DownloadFilename);

                        wc.DownloadProgressChanged +=
                            (sender, args) => { iface.SetProgress(args.ProgressPercentage); };

                        while (wc.IsBusy)
                        {
                            Thread.Sleep(100);
                        }
                        //iface.SetProgress(-1);
                    }

                    var remoteHash = LatestReply.GetHash();
                    if (remoteHash.IsNotEmpty())
                    {
                        using (var hasher = MD5.Create())
                        using (var fileStream = File.OpenRead(DownloadFilename))
                        {
                            var localHash = hasher.ComputeHash(fileStream);
                            if (!localHash.ToHexString().Equals(remoteHash, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new CryptographicException(Localisation.UpdateSystem_FailedHash);
                            }
                        }
                    }

                    iface.SetTitle(Localisation.UpdateSystem_ExtractingTitle);

                    using (var zip = new ZipFile(DownloadFilename))
                    {
                        zip.ZipErrorAction = ZipErrorAction.Throw;

                        zip.ExtractProgress += (sender, args) =>
                        {
                            iface.SetMaximum(args.EntriesTotal);
                            iface.SetProgress(args.EntriesExtracted);
                        };

                        if (dir.Exists)
                            dir.Delete(true);
                        zip.ExtractAll(dir.FullName);

                        if (dir.GetFiles().Length != zip.Entries.Count(x => !x.FileName.ContainsAny(new[] {'\\', '/'})))
                            throw new IOException(Localisation.UpdateSystem_FailedExtractDetails);
                    }
                }
                catch
                {
                    try
                    {
                        dir.Delete(true);
                    }
                    catch
                    {
                        /* Ignore errors to avoid losing the original exception */
                    }

                    throw;
                }
                finally
                {
                    // Delete the update archive, it is no longer necessary
                    try
                    {
                        File.Delete(DownloadFilename);
                    }
                    catch
                    {
                        /* Ignore errors to avoid losing the original exception */
                    }
                }
            });

            try
            {
                if (error != null)
                    throw error;

                // update success, launch the helper
                if (!File.Exists(UpdateHelperPath))
                    throw new IOException(Localisation.Error_UpdateHelperMissing);

                // Use a magic keyword to bypass the default user-frienly message
                Process.Start(UpdateHelperPath, "ItsMagic");

                Application.Exit();
            }
            catch (Exception ex)
            {
                CustomMessageBox.ShowDialog(null,
                    new CmbBasicSettings(Localisation.UpdateSystem_FailedTitle, ex.Message,
                        Localisation.UpdateSystem_Failed_Details, SystemIcons.Error, "OK")
                    {StartPosition = FormStartPosition.CenterScreen});
            }
        }

        public static UpdateStatus CheckForUpdates()
        {
            try
            {
                var client = new WebClient();
                var result = client.DownloadStringAwareOfEncoding(UpdateFeedUri);
                LatestReply =
                    new UpdateReply(
                        XDocument.Parse(result.Substring(result.IndexOf("<", StringComparison.InvariantCulture))));

                return LatestReply.GetUpdateVersion().CompareTo(CurrentVersion) > 0
                    ? UpdateStatus.NewAvailable
                    : UpdateStatus.UpToDate;
            }
            catch (Exception e)
            {
                LatestReply = null;
                LastError = e;
                return UpdateStatus.CheckFailed;
            }
        }

        public static void ProcessPendingUpdates()
        {
            if (Directory.Exists(ExtractedUpdatePath))
            {
                try
                {
                    var files = Directory.GetFiles(ExtractedUpdatePath);
                    var filePath =
                        files.FirstOrDefault(x => x.Contains(HelperFilename, StringComparison.OrdinalIgnoreCase));

                    if (filePath != null)
                    {
                        var newFile = new FileInfo(filePath);
                        var oldFile = new FileInfo(Path.Combine(AssemblyLocation, HelperFilename));

                        //Time out after 2 sec
                        for (var i = 0; i < 20 && oldFile.IsFileLocked(); i++)
                        {
                            Thread.Sleep(100);
                        }

                        oldFile.Delete();
                        newFile.MoveTo(oldFile.FullName);
                    }
                }
                catch (IOException)
                {
                }

                try
                {
                    Directory.Delete(ExtractedUpdatePath, true);
                }
                catch (IOException)
                {
                }
            }
        }

        public static void RestartApplication()
        {
            try
            {
                // Use a magic keyword to bypass the default user-frienly message
                Process.Start(UpdateHelperPath, "Restart");
            }
            catch
            {
                // If the helper is missing or fails to start just exit the application
            }
            Application.Exit();
        }
    }
}