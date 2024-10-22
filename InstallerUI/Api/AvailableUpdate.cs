using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI.Api
{
    internal class AvailableUpdate
    {
        //Mapping to Bundle Chain (Like a foreign key in DB)
        public PackageIdEnum PackageId { get; set; }

        //New version # available for download 
        public string Version { get; set; }
        
        //Package name shown is Add/Remove Program i.e. BundleName
        public string PackageNameToShowInAddRemoveProgram { get; set; }

        //Package Name shown in UI in Setup
        public string PackageNameToDisplayInSetUpUI { get; set; }

        //A flag to indicate if this is a new module (and was not originally delivered with setup)
        public bool IsNewModule { get; set; }

        //Api Server and FileServer may be different
        //Each File can be downloaded from different server.
        public string ServerName { get; set; }

        public string DownloadFileNameWithExtension { get; set; }
    }
}
