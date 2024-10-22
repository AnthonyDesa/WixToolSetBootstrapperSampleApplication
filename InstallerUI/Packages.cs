using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstallerUI
{
    internal static class Packages
    {
        public static IList<PackageIdEnum> GetPackageIdsAsEnum()
        {
            return Enum.GetValues(typeof(PackageIdEnum)).Cast<PackageIdEnum>().ToList();
        }

        public static string GetInstalledPackageName(PackageIdEnum packageId)
        {
           return $"Is{packageId.ToString()}Installed";
        }
    }

    public enum PackageIdEnum
    {
        FirstInstaller,
        SecondInstaller,
        ThirdInstaller,
        FourthInstaller,
        FifthInstaller,
        FirstInstallerBootstrapper,
        SecondInstallerBootstrapper,
        ThirdInstallerBootstrapper,
        FourthInstallerBootstrapper,
        FifthInstallerBootstrapper,
        SixthInstallerBootstrapper
    }

    public enum UserSelectionEnum
    {
        Skip,
        Keep,
        Install,
        Uninstall,
        Update,
        Repair
    }
}
