using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI
{
    public class Package
    {
        //Unique identifier for the package
        public string PackageId { get; set; }


        //{0} - Host
        //{1} - Version
        //{2} - PackageId
        //{3} - Build#

        //localhost and production will have the following format
        //http://{0}/OnlineSetupArtifacts/Versions/{1}/{2}

        //staging (teamcity) will have the following format
        //For current build artifacts {3} will be the build number
        //http://{0}/OnlineSetupArtifacts/{3}/Versions/{1}/{2}
        //For new version build number will be set to empty string to remove dependency from current build number
        //This approach will allow us to use new version with any teamcity build.
        // i.e. http://{0}/OnlineSetupArtifacts//Versions/{1}/{2}
        public string DownloadUrl { get; set; }
    }
}
