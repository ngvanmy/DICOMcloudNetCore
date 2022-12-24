using DICOMcloud.DataAccess;
using DICOMcloud.Pacs.Commands;
using fo = FellowOakDicom;

namespace DICOMcloud.Pacs
{
    public interface IObjectStoreService
    {
        DCloudCommandResult StoreDicom ( fo.DicomDataset dataset, InstanceMetadata metadata ) ;
        DCloudCommandResult Delete     ( fo.DicomDataset request, ObjectQueryLevel  level ) ;
    }
}