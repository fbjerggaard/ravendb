using Lextm.SharpSnmpLib;
using Raven.Server.ServerWide;
using Sparrow;
using Sparrow.Server.Utils;

namespace Raven.Server.Monitoring.Snmp.Objects.Server
{
    public class ServerStorageDiskRemainingSpace : ScalarObjectBase<Gauge32>
    {
        private readonly ServerStore _store;
        private static readonly Gauge32 Empty = new Gauge32(-1);

        public ServerStorageDiskRemainingSpace(ServerStore store)
            : base(SnmpOids.Server.StorageDiskRemainingSpace)
        {
            _store = store;
        }

        protected override Gauge32 GetData()
        {
            if (_store.Configuration.Core.RunInMemory)
                return Empty;

            var result = DiskSpaceChecker.GetDiskSpaceInfo(_store.Configuration.Core.DataDirectory.FullPath);
            if (result == null)
                return Empty;

            return new Gauge32(result.TotalFreeSpace.GetValue(SizeUnit.Megabytes));
        }
    }
}
