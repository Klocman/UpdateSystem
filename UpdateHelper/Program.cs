using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using UpdateHelper.Properties;

namespace UpdateHelper
{
    internal static class Program
    {
        private static string AssemblyLocation
        {
            get
            {
                var location = Assembly.GetExecutingAssembly().Location;
                if (location.EndsWith(".exe"))
                    location = GetDirectory(location);
                return location;
            }
        }

        private static string ExecutableFilename => Resources.SelfExecutableName;
        private static string ExtractedUpdatePath => Path.Combine(AssemblyLocation, Resources.UpdateDataFolderName);
        private static string MainExecutablePath => Path.Combine(AssemblyLocation, Resources.ProgramExecutableName);

        private static string GetDirectory(string fullPath)
        {
            var trimmed = fullPath.Trim().Trim('"', ' ').TrimEnd('\\');
            var index = trimmed.LastIndexOf('\\');
            return index > 0 ? trimmed.Substring(0, index) : string.Empty;
        }

        private static bool IsFileLocked(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            FileStream stream = null;

            try
            {
                stream = new FileInfo(filePath).Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // File is locked
                return true;
            }
            finally
            {
                stream?.Close();
            }

            // File is not locked
            return false;
        }

        private static void Main(string[] args)
        {
            if (args.Any(x => x.Equals(Resources.Argument_Restart)))
                RestartApplication();
            else if (args.Any(x => x.Equals(Resources.Argument_Update)))
                RunAutoUpdate();
            else
                MessageBox.Show(Localisation.UserLaunchMessage,
                    Localisation.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void RunAutoUpdate()
        {
            try
            {
                if (string.IsNullOrEmpty(ExtractedUpdatePath) || !Directory.Exists(ExtractedUpdatePath))
                    throw new InvalidDataException(Localisation.RunAutoUpdate_ErrorDataMissing);

                var directories = Directory.GetDirectories(ExtractedUpdatePath, "*", SearchOption.AllDirectories);
                var files = Directory.GetFiles(ExtractedUpdatePath, "*.*", SearchOption.AllDirectories)
                    .Where(x => !x.Contains(ExecutableFilename)).ToList();

                if (files.Count < 6 || !files.Any(x => x.Contains(Resources.ProgramExecutableName)))
                    throw new InvalidDataException(Localisation.RunAutoUpdate_Error_FilesMissing);

                // Create all of the directories
                foreach (var dirPath in directories)
                    Directory.CreateDirectory(dirPath.Replace(ExtractedUpdatePath, AssemblyLocation));

                // Copy all files, replace existing
                foreach (var newPath in files)
                {
                    var targetPath = newPath.Replace(ExtractedUpdatePath, AssemblyLocation);
                    //Time out after 15 sec
                    for (var i = 0; i < 150 && IsFileLocked(targetPath); i++)
                    {
                        Thread.Sleep(100);
                    }
                    File.Copy(newPath, targetPath, true);
                }

                Process.Start(MainExecutablePath);
            }
            catch (Exception ex)
            {
                if (MessageBox.Show(string.Format(Localisation.RunAutoUpdate_ErrorBody, ex.Message),
                    Localisation.MessageBoxTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(@"http://klocmansoftware.weebly.com/");
                    }
                    catch
                    {
                        // Ignore errors, user can figure it out.
                    }
                }
            }
        }

        private static void RestartApplication()
        {
            var i = 0;

            while (i++ < 100 && IsFileLocked(MainExecutablePath))
                Thread.Sleep(100);

            //Time out after 10 sec
            if (i >= 100)
                return;

            try
            {
                Process.Start(MainExecutablePath);
            }
            catch
            {
                // Ignore errors, user can figure it out.
            }
        }
    }
}