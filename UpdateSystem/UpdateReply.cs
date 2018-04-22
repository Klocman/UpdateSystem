using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Klocman.UpdateSystem
{
    public class UpdateReply
    {
        /// <exception cref="ArgumentException">Provided reply is invalid</exception>
        public UpdateReply(XDocument reply)
        {
            FullReply = reply;

            try
            {
                GetUpdateVersion();
            }
            catch (Exception e)
            {
                throw new ArgumentException("Provided reply is invalid", nameof(reply), e);
            }
        }

        public XDocument FullReply { get; }
        public XElement UpdateInfo => FullReply.Root?.Element("Update");

        public IEnumerable<string> GetChanges()
        {
            var result = UpdateInfo.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("Changes"));
            if (result == null)
                return Enumerable.Empty<string>();

            var changes =
                result.Value.Trim()
                    .Split(new[] {"\r\n", "\t", "\n"}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim());
            return changes;
        }

        public Uri GetDonwnloadLink()
        {
            var updateAddr = UpdateInfo.Element("URL");
            return string.IsNullOrEmpty(updateAddr?.Value) ? null : new Uri(updateAddr.Value);
        }

        public Uri GetDownloadPageLink()
        {
            var updateAddr = UpdateInfo.Element("PageURL");
            return string.IsNullOrEmpty(updateAddr?.Value) ? null : new Uri(updateAddr.Value);
        }

        public string GetHash()
        {
            var updateHash = UpdateInfo.Element("Hash");
            return updateHash?.Value ?? string.Empty;
        }

        /// <exception cref="OverflowException">
        ///     At least one component of the version represents a number greater than
        ///     <see cref="F:System.Int32.MaxValue" />.
        /// </exception>
        public Version GetUpdateVersion()
        {
            var updateVer = UpdateInfo.Element("Version");
            return updateVer == null ? null : new Version(updateVer.Value);
        }
    }
}