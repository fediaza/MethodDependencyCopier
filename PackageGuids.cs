using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MethodDependencyCopier
{
    public static class PackageGuids
    {
        public const string guidMethodDependencyCopierPackageCmdSetString = "cb9dfd7f-2fcc-4a3e-aae8-f7fe30b1cfac";
        public static readonly Guid guidMethodDependencyCopierPackageCmdSet = new Guid(guidMethodDependencyCopierPackageCmdSetString);
    }

    public static class PackageIds
    {
        public const int CopyMethodWithDependenciesId = 0x0100;
    }
}
