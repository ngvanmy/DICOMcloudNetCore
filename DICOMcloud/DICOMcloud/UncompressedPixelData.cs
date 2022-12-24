using FellowOakDicom;
using FellowOakDicom.Imaging;
using fo = FellowOakDicom;

namespace DICOMcloud
{
    public class UncompressedPixelDataWrapper
    {
        public UncompressedPixelDataWrapper ( fo.DicomDataset ds )
        {
            if (ds.InternalTransferSyntax.IsEncapsulated)
            {
                Dataset = ds.Clone().NotValidated();
            }
            else
            {
                // pull uncompressed frame from source pixel data
                Dataset = ds ;
            }
            
            PixelData = DicomPixelData.Create (Dataset) ;
        }

        public fo.DicomDataset Dataset { get; private set; }

        public DicomPixelData PixelData
        {
            get;
            private set;
        }
    }
}
